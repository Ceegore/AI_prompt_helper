using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PromptHelper.Infrastructure;
using PromptHelper.Models;

namespace PromptHelper.Services;

public sealed class DataFolderMigrationService
{
    private readonly IMigrationFileOps _fileOps;
    private readonly DataRootCapabilityValidator _capabilityValidator;
    private readonly string? _defaultBootstrapRoot;
    private readonly IPhysicalPathResolver _pathResolver;

    internal sealed record MigrationSnapshot(
        byte[] LibraryBytes,
        byte[] LibraryHash,
        LibraryDocument Document,
        IReadOnlyDictionary<Guid, byte[]> PromptHashes);

    internal enum TargetLibraryKind
    {
        Empty,
        ValidPrimary,
        RecoverableBackupOnly,
        CorruptPrimaryWithValidBackup,
        FutureSchema,
        Invalid
    }

    internal sealed record TargetInspection(
        string NormalizedRoot,
        TargetLibraryKind Kind,
        LibraryDocument? EffectiveDocument,
        string? EffectiveMetadataPath,
        string? Warning,
        Exception? Error);

    internal sealed class MigrationTargetTransaction : IDisposable
    {
        private readonly List<string> _createdFiles = [];
        private readonly List<string> _createdDirectories = [];
        private bool _committed;

        public void TrackCreatedFile(string path) => _createdFiles.Add(path);
        public void TrackCreatedDirectory(string path) => _createdDirectories.Add(path);
        public void Commit() => _committed = true;

        public void Dispose()
        {
            if (_committed)
            {
                return;
            }

            foreach (string file in _createdFiles.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Best effort cleanup
                }
            }

            foreach (string dir in _createdDirectories.OrderByDescending(x => x.Length))
            {
                try
                {
                    if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                    }
                }
                catch
                {
                    // Best effort directory cleanup
                }
            }
        }
    }

    public DataFolderMigrationService()
        : this(null, null, null, null)
    {
    }

    internal DataFolderMigrationService(
        IMigrationFileOps? fileOps = null,
        DataRootCapabilityValidator? capabilityValidator = null,
        string? defaultBootstrapRoot = null,
        IPhysicalPathResolver? pathResolver = null)
    {
        _fileOps = fileOps ?? new DefaultMigrationFileOps();
        _capabilityValidator = capabilityValidator ?? new DataRootCapabilityValidator();
        _defaultBootstrapRoot = defaultBootstrapRoot;
        _pathResolver = pathResolver ?? new WindowsPhysicalPathResolver();
    }

    public DataFolderChangeResult PrepareTarget(string currentRoot, string selectedRoot)
    {
        if (string.IsNullOrWhiteSpace(selectedRoot))
        {
            throw new ArgumentException("Selected data folder path cannot be empty or whitespace.", nameof(selectedRoot));
        }

        string cleanTarget = PathIdentity.NormalizeForComparison(selectedRoot.Trim());
        string cleanCurrent = PathIdentity.NormalizeForComparison((currentRoot ?? string.Empty).Trim());

        if (PathIdentity.Equals(cleanTarget, cleanCurrent))
        {
            return new DataFolderChangeResult(cleanTarget, ExistingLibraryFound: false, Copied: false);
        }

        DataRootTopologyValidator.ValidateDisjointOrSame(cleanCurrent, cleanTarget, _defaultBootstrapRoot, _pathResolver);

        if (File.Exists(cleanTarget))
        {
            throw new ArgumentException($"Selected path is a file, not a directory: {cleanTarget}", nameof(selectedRoot));
        }

        TargetInspection inspection = InspectTarget(cleanTarget);

        switch (inspection.Kind)
        {
            case TargetLibraryKind.ValidPrimary:
                _capabilityValidator.ValidateWritable(cleanTarget);
                return new DataFolderChangeResult(cleanTarget, ExistingLibraryFound: true, Copied: false, Warning: inspection.Warning);

            case TargetLibraryKind.RecoverableBackupOnly:
                _capabilityValidator.ValidateWritable(cleanTarget);
                return new DataFolderChangeResult(
                    cleanTarget,
                    ExistingLibraryFound: true,
                    Copied: false,
                    Warning: inspection.Warning ?? "The selected folder contains a recoverable Prompt Helper safety backup but no primary library.json. Prompt Helper will recover it on startup; the current library will not be copied there.");

            case TargetLibraryKind.CorruptPrimaryWithValidBackup:
                throw new InvalidDataException(
                    "The target folder contains a corrupt primary library.json and a safety backup. Start Prompt Helper on that folder to recover it before selecting it as a migration target.",
                    inspection.Error);

            case TargetLibraryKind.FutureSchema:
                throw inspection.Error ?? new UnsupportedLibrarySchemaException(999);

            case TargetLibraryKind.Invalid:
                throw inspection.Error is InvalidDataException ide
                    ? ide
                    : new InvalidDataException($"The target data folder contains invalid or unreadable library data: '{cleanTarget}'. {inspection.Error?.Message}", inspection.Error);

            case TargetLibraryKind.Empty:
            default:
                break;
        }

        // Empty target copy workflow
        MigrationSnapshot snapshot = CaptureSourceSnapshot(cleanCurrent);

        using var tx = new MigrationTargetTransaction();
        CopySnapshotToTarget(cleanCurrent, cleanTarget, snapshot, tx);
        _capabilityValidator.ValidateWritable(cleanTarget);
        tx.Commit();

        return new DataFolderChangeResult(cleanTarget, ExistingLibraryFound: false, Copied: true);
    }

    internal TargetInspection InspectTarget(string targetRoot)
    {
        string normalizedTarget = PathIdentity.NormalizeForComparison(targetRoot);

        if (!Directory.Exists(normalizedTarget))
        {
            return new TargetInspection(normalizedTarget, TargetLibraryKind.Empty, null, null, null, null);
        }

        string primaryPath = Path.Combine(normalizedTarget, "library.json");
        string backupPath = Path.Combine(normalizedTarget, "library.backup.json");

        bool primaryExists = File.Exists(primaryPath);
        bool backupExists = File.Exists(backupPath);

        if (!primaryExists && !backupExists)
        {
            return new TargetInspection(normalizedTarget, TargetLibraryKind.Empty, null, null, null, null);
        }

        LibraryDocument? primaryDoc = null;
        bool primaryFuture = false;
        int primaryFutureVersion = 0;
        Exception? primaryEx = null;

        if (primaryExists)
        {
            try
            {
                string json = File.ReadAllText(primaryPath);
                primaryDoc = LibraryRepository.InspectAndDeserialize(json);
                LibraryValidator.Validate(primaryDoc);
                ValidateDocumentPromptBodies(normalizedTarget, primaryDoc, "Target library.json");
            }
            catch (UnsupportedLibrarySchemaException ex)
            {
                primaryFuture = true;
                primaryFutureVersion = ex.SchemaVersion;
                primaryEx = ex;
            }
            catch (Exception ex)
            {
                primaryEx = ex;
                primaryDoc = null;
            }
        }

        if (primaryFuture)
        {
            return new TargetInspection(
                normalizedTarget,
                TargetLibraryKind.FutureSchema,
                null,
                primaryPath,
                null,
                new UnsupportedLibrarySchemaException(primaryFutureVersion));
        }

        if (primaryDoc is not null)
        {
            return new TargetInspection(
                normalizedTarget,
                TargetLibraryKind.ValidPrimary,
                primaryDoc,
                primaryPath,
                null,
                null);
        }

        LibraryDocument? backupDoc = null;
        bool backupFuture = false;
        int backupFutureVersion = 0;
        Exception? backupEx = null;

        if (backupExists)
        {
            try
            {
                string json = File.ReadAllText(backupPath);
                backupDoc = LibraryRepository.InspectAndDeserialize(json);
                LibraryValidator.Validate(backupDoc);
                ValidateDocumentPromptBodies(normalizedTarget, backupDoc, "Target library.backup.json");
            }
            catch (UnsupportedLibrarySchemaException ex)
            {
                backupFuture = true;
                backupFutureVersion = ex.SchemaVersion;
                backupEx = ex;
            }
            catch (Exception ex)
            {
                backupEx = ex;
                backupDoc = null;
            }
        }

        if (backupFuture)
        {
            return new TargetInspection(
                normalizedTarget,
                TargetLibraryKind.FutureSchema,
                null,
                backupPath,
                null,
                new UnsupportedLibrarySchemaException(backupFutureVersion));
        }

        if (primaryExists && primaryDoc is null && backupDoc is not null)
        {
            return new TargetInspection(
                normalizedTarget,
                TargetLibraryKind.CorruptPrimaryWithValidBackup,
                backupDoc,
                backupPath,
                null,
                primaryEx);
        }

        if (!primaryExists && backupDoc is not null)
        {
            return new TargetInspection(
                normalizedTarget,
                TargetLibraryKind.RecoverableBackupOnly,
                backupDoc,
                backupPath,
                "The selected folder contains a recoverable Prompt Helper safety backup but no primary library.json. Prompt Helper will recover it on startup; the current library will not be copied there.",
                null);
        }

        Exception error = primaryEx is InvalidDataException pIde
            ? pIde
            : backupEx is InvalidDataException bIde
                ? bIde
                : new InvalidDataException($"Target library metadata is invalid: {primaryEx?.Message ?? backupEx?.Message}", primaryEx ?? backupEx);

        return new TargetInspection(
            normalizedTarget,
            TargetLibraryKind.Invalid,
            null,
            primaryExists ? primaryPath : backupPath,
            null,
            error);
    }

    internal MigrationSnapshot CaptureSourceSnapshot(string currentRoot)
    {
        if (!Directory.Exists(currentRoot))
        {
            throw new DirectoryNotFoundException(
                $"Library directory does not exist: '{currentRoot}'");
        }

        string libraryPath = Path.Combine(currentRoot, "library.json");
        if (!File.Exists(libraryPath))
        {
            throw new InvalidDataException(
                $"Library directory does not contain library.json: '{currentRoot}'");
        }

        byte[] libraryBytes = _fileOps.ReadAllBytes(libraryPath);
        byte[] libraryHash = SHA256.HashData(libraryBytes);

        string libraryJson = DecodeUtf8Text(libraryBytes);

        LibraryDocument document;
        try
        {
            document = LibraryRepository.InspectAndDeserialize(libraryJson);
            LibraryValidator.Validate(document);
        }
        catch (Exception ex) when (
            ex is JsonException or
            InvalidDataException or
            ArgumentException)
        {
            throw new InvalidDataException(
                $"Source library metadata at '{libraryPath}' is invalid: {ex.Message}",
                ex);
        }

        string promptsDir = Path.Combine(currentRoot, "prompts");
        var promptHashes = new Dictionary<Guid, byte[]>();

        foreach (PromptRecord prompt in document.Prompts)
        {
            string promptPath = Path.Combine(
                promptsDir,
                $"{prompt.Id:N}.md");

            if (!File.Exists(promptPath))
            {
                throw new InvalidDataException(
                    $"Library references prompt file '{prompt.Id:N}.md' " +
                    $"which does not exist in '{promptsDir}'.");
            }

            byte[] promptBytes;
            try
            {
                promptBytes = _fileOps.ReadAllBytes(promptPath);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    $"Prompt file '{promptPath}' cannot be read: {ex.Message}",
                    ex);
            }

            promptHashes.Add(
                prompt.Id,
                SHA256.HashData(promptBytes));
        }

        return new MigrationSnapshot(
            LibraryBytes: libraryBytes,
            LibraryHash: libraryHash,
            Document: document,
            PromptHashes: promptHashes);
    }

    internal void CopySnapshotToTarget(
        string currentRoot,
        string targetRoot,
        MigrationSnapshot snapshot,
        MigrationTargetTransaction tx)
    {
        EnsureDirectoryTracked(targetRoot, tx);

        string targetPromptsDir = Path.Combine(targetRoot, "prompts");
        string targetRecoveryDir = Path.Combine(targetRoot, "recovery");

        EnsureDirectoryTracked(targetPromptsDir, tx);
        EnsureDirectoryTracked(targetRecoveryDir, tx);

        string targetLibraryPath = Path.Combine(targetRoot, "library.json");
        string sourceLibraryPath = Path.Combine(currentRoot, "library.json");

        CopyFileNoOverwrite(sourceLibraryPath, targetLibraryPath, tx);

        string currentBackupPath = Path.Combine(currentRoot, "library.backup.json");
        if (File.Exists(currentBackupPath))
        {
            string targetBackupPath = Path.Combine(targetRoot, "library.backup.json");
            CopyFileNoOverwrite(currentBackupPath, targetBackupPath, tx);
        }

        string sourcePromptsDir = Path.Combine(currentRoot, "prompts");
        if (Directory.Exists(sourcePromptsDir))
        {
            foreach (string promptFile in _fileOps.EnumeratePromptFiles(sourcePromptsDir))
            {
                string fileName = Path.GetFileName(promptFile);
                string destPromptPath = Path.Combine(targetPromptsDir, fileName);
                CopyFileNoOverwrite(promptFile, destPromptPath, tx);
            }
        }

        string currentRecoveryDir = Path.Combine(currentRoot, "recovery");
        if (Directory.Exists(currentRecoveryDir))
        {
            foreach (string recoveryFile in Directory.EnumerateFiles(currentRecoveryDir, "*", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(recoveryFile);
                string destRecoveryPath = Path.Combine(targetRecoveryDir, fileName);
                CopyFileNoOverwrite(recoveryFile, destRecoveryPath, tx);
            }
        }

        // Verify source library.json did not mutate
        byte[] finalSourceLibHash = SHA256.HashData(_fileOps.ReadAllBytes(sourceLibraryPath));
        if (!snapshot.LibraryHash.AsSpan().SequenceEqual(finalSourceLibHash))
        {
            throw new IOException("Source library metadata changed during migration. Retry after it is stable.");
        }

        // Verify target library.json bytes match snapshot
        byte[] targetLibraryHash = SHA256.HashData(_fileOps.ReadAllBytes(targetLibraryPath));
        if (!snapshot.LibraryHash.AsSpan().SequenceEqual(targetLibraryHash))
        {
            throw new IOException("Target library.json does not match the captured source snapshot.");
        }

        // Verify source and target prompt body hashes
        foreach (PromptRecord prompt in snapshot.Document.Prompts)
        {
            string pPath = Path.Combine(sourcePromptsDir, $"{prompt.Id:N}.md");
            byte[] finalSourcePromptHash = SHA256.HashData(_fileOps.ReadAllBytes(pPath));
            if (!snapshot.PromptHashes[prompt.Id].AsSpan().SequenceEqual(finalSourcePromptHash))
            {
                throw new IOException($"Source prompt '{prompt.Id:N}.md' changed during migration. Retry after it is stable.");
            }

            string targetPPath = Path.Combine(targetPromptsDir, $"{prompt.Id:N}.md");
            byte[] targetPromptHash = SHA256.HashData(_fileOps.ReadAllBytes(targetPPath));
            if (!snapshot.PromptHashes[prompt.Id].AsSpan().SequenceEqual(targetPromptHash))
            {
                throw new IOException($"Target prompt '{prompt.Id:N}.md' does not match source snapshot.");
            }
        }

        // Structural verification of target metadata and prompts
        ValidateDocumentPromptBodies(targetRoot, snapshot.Document, "Migrated target library");
    }

    private static void ValidateDocumentPromptBodies(
        string root,
        LibraryDocument document,
        string metadataDescription)
    {
        string promptsDir = Path.Combine(root, "prompts");

        foreach (PromptRecord prompt in document.Prompts)
        {
            string promptPath = Path.Combine(
                promptsDir,
                $"{prompt.Id:N}.md");

            if (!File.Exists(promptPath))
            {
                throw new InvalidDataException(
                    $"{metadataDescription} references prompt file " +
                    $"'{prompt.Id:N}.md', but it is missing from '{promptsDir}'.");
            }

            try
            {
                using FileStream stream = new(
                    promptPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                if (!stream.CanRead)
                {
                    throw new IOException("File stream is not readable.");
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                SecurityException)
            {
                throw new InvalidDataException(
                    $"{metadataDescription} references unreadable prompt file " +
                    $"'{promptPath}': {ex.Message}",
                    ex);
            }
        }
    }

    private static void EnsureDirectoryTracked(
        string path,
        MigrationTargetTransaction tx)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            tx.TrackCreatedDirectory(path);
        }
    }

    private void CopyFileNoOverwrite(
        string sourcePath,
        string destPath,
        MigrationTargetTransaction tx)
    {
        if (File.Exists(destPath))
        {
            throw new IOException(
                $"Target file collision: '{destPath}' already exists.");
        }

        _fileOps.CopyFile(sourcePath, destPath, overwrite: false);
        tx.TrackCreatedFile(destPath);
    }

    private static string DecodeUtf8Text(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true);

        return reader.ReadToEnd();
    }
}
