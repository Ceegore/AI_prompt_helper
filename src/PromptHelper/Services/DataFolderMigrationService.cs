using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using PromptHelper.Infrastructure;
using PromptHelper.Models;

namespace PromptHelper.Services;

public sealed class DataFolderMigrationService
{
    private readonly IMigrationFileOps _fileOps;
    private readonly DataRootCapabilityValidator _capabilityValidator;
    private readonly string? _defaultBootstrapRoot;

    internal sealed record MigrationSnapshot(
        byte[] LibraryHash,
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

    public DataFolderMigrationService()
        : this(null, null, null)
    {
    }

    internal DataFolderMigrationService(
        IMigrationFileOps? fileOps = null,
        DataRootCapabilityValidator? capabilityValidator = null,
        string? defaultBootstrapRoot = null)
    {
        _fileOps = fileOps ?? new DefaultMigrationFileOps();
        _capabilityValidator = capabilityValidator ?? new DataRootCapabilityValidator();
        _defaultBootstrapRoot = defaultBootstrapRoot;
    }

    public DataFolderChangeResult PrepareTarget(string currentRoot, string selectedRoot)
    {
        if (string.IsNullOrWhiteSpace(selectedRoot))
        {
            throw new ArgumentException("Selected data folder path cannot be empty or whitespace.", nameof(selectedRoot));
        }

        string normalizedTarget = Path.GetFullPath(selectedRoot.Trim());
        string normalizedCurrent = Path.GetFullPath((currentRoot ?? string.Empty).Trim());

        string cleanTarget = normalizedTarget.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string cleanCurrent = normalizedCurrent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(cleanTarget, cleanCurrent, StringComparison.OrdinalIgnoreCase))
        {
            return new DataFolderChangeResult(normalizedTarget, ExistingLibraryFound: false, Copied: false);
        }

        // Two-way topology and bootstrap overlap validation
        DataRootTopologyValidator.ValidateDisjointOrSame(cleanCurrent, cleanTarget, _defaultBootstrapRoot);

        if (File.Exists(normalizedTarget))
        {
            throw new ArgumentException($"Selected path is a file, not a directory: {normalizedTarget}", nameof(selectedRoot));
        }

        // Validate source library before touching target
        LibraryDocument sourceDoc = ValidateLibraryRoot(normalizedCurrent, requirePrimaryLibrary: true);

        var createdDirs = new List<string>();
        if (!Directory.Exists(normalizedTarget))
        {
            Directory.CreateDirectory(normalizedTarget);
            createdDirs.Add(normalizedTarget);
        }

        TargetLibraryKind targetKind = ClassifyTargetLibrary(normalizedTarget, out string? targetWarning, out Exception? targetError);

        switch (targetKind)
        {
            case TargetLibraryKind.ValidPrimary:
                ValidateExistingTargetLibrary(normalizedTarget, Path.Combine(normalizedTarget, "library.json"));
                _capabilityValidator.ValidateWritable(normalizedTarget);
                return new DataFolderChangeResult(normalizedTarget, ExistingLibraryFound: true, Copied: false, Warning: targetWarning);

            case TargetLibraryKind.RecoverableBackupOnly:
                _capabilityValidator.ValidateWritable(normalizedTarget);
                return new DataFolderChangeResult(
                    normalizedTarget,
                    ExistingLibraryFound: true,
                    Copied: false,
                    Warning: targetWarning ?? "The selected folder contains a recoverable Prompt Helper safety backup but no primary library.json. Prompt Helper will recover it on startup; the current library will not be copied there.");

            case TargetLibraryKind.CorruptPrimaryWithValidBackup:
                throw new InvalidDataException(
                    "The target folder contains a corrupt primary library.json and a safety backup. Start Prompt Helper on that folder to recover it before selecting it as a migration target.",
                    targetError);

            case TargetLibraryKind.FutureSchema:
                throw targetError ?? new UnsupportedLibrarySchemaException(999);

            case TargetLibraryKind.Invalid:
                throw targetError is InvalidDataException ide
                    ? ide
                    : new InvalidDataException($"The target data folder contains invalid or unreadable library data: '{normalizedTarget}'. {targetError?.Message}", targetError);

            case TargetLibraryKind.Empty:
            default:
                break;
        }

        CopyAndValidateLibrary(normalizedCurrent, normalizedTarget, sourceDoc, createdDirs);
        return new DataFolderChangeResult(normalizedTarget, ExistingLibraryFound: false, Copied: true);
    }

    private static TargetLibraryKind ClassifyTargetLibrary(
        string targetRoot,
        out string? warning,
        out Exception? error)
    {
        warning = null;
        error = null;

        string primaryPath = Path.Combine(targetRoot, "library.json");
        string backupPath = Path.Combine(targetRoot, "library.backup.json");

        bool primaryExists = File.Exists(primaryPath);
        bool backupExists = File.Exists(backupPath);

        if (!primaryExists && !backupExists)
        {
            return TargetLibraryKind.Empty;
        }

        bool primaryValid = false;
        bool primaryFuture = false;
        int primaryFutureVersion = 0;
        Exception? primaryEx = null;

        if (primaryExists)
        {
            try
            {
                string json = File.ReadAllText(primaryPath);
                var doc = LibraryRepository.InspectAndDeserialize(json);
                LibraryValidator.Validate(doc);
                primaryValid = true;
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
            }
        }

        if (primaryFuture)
        {
            error = new UnsupportedLibrarySchemaException(primaryFutureVersion);
            return TargetLibraryKind.FutureSchema;
        }

        if (primaryValid)
        {
            return TargetLibraryKind.ValidPrimary;
        }

        bool backupValid = false;
        bool backupFuture = false;
        int backupFutureVersion = 0;
        Exception? backupEx = null;

        if (backupExists)
        {
            try
            {
                string json = File.ReadAllText(backupPath);
                var doc = LibraryRepository.InspectAndDeserialize(json);
                LibraryValidator.Validate(doc);
                backupValid = true;
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
            }
        }

        if (backupFuture)
        {
            error = new UnsupportedLibrarySchemaException(backupFutureVersion);
            return TargetLibraryKind.FutureSchema;
        }

        if (primaryExists && !primaryValid && backupValid)
        {
            error = primaryEx;
            return TargetLibraryKind.CorruptPrimaryWithValidBackup;
        }

        if (!primaryExists && backupValid)
        {
            warning = "The selected folder contains a recoverable Prompt Helper safety backup but no primary library.json. Prompt Helper will recover it on startup; the current library will not be copied there.";
            return TargetLibraryKind.RecoverableBackupOnly;
        }

        error = primaryEx ?? backupEx ?? new InvalidDataException("Invalid target library files.");
        return TargetLibraryKind.Invalid;
    }

    private static LibraryDocument ValidateLibraryRoot(string root, bool requirePrimaryLibrary)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Library directory does not exist: '{root}'");
        }

        string libraryPath = Path.Combine(root, "library.json");
        if (!File.Exists(libraryPath))
        {
            if (requirePrimaryLibrary)
            {
                throw new InvalidDataException($"Library directory does not contain library.json: '{root}'");
            }
            return new LibraryDocument();
        }

        string json = File.ReadAllText(libraryPath);
        LibraryDocument document;
        try
        {
            document = LibraryRepository.InspectAndDeserialize(json);
            LibraryValidator.Validate(document);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or ArgumentException)
        {
            throw new InvalidDataException($"Library metadata at '{libraryPath}' is invalid: {ex.Message}", ex);
        }

        string promptsDir = Path.Combine(root, "prompts");
        foreach (var prompt in document.Prompts)
        {
            string promptPath = Path.Combine(promptsDir, $"{prompt.Id:N}.md");
            if (!File.Exists(promptPath))
            {
                throw new InvalidDataException($"Library references prompt file '{prompt.Id:N}.md' which does not exist in '{promptsDir}'.");
            }

            try
            {
                using var stream = File.OpenRead(promptPath);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Prompt file '{promptPath}' cannot be read: {ex.Message}", ex);
            }
        }

        return document;
    }

    private static void ValidateExistingTargetLibrary(string targetRoot, string targetLibraryPath)
    {
        string json = File.ReadAllText(targetLibraryPath);
        LibraryDocument document;
        try
        {
            document = LibraryRepository.InspectAndDeserialize(json);
            LibraryValidator.Validate(document);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or ArgumentException)
        {
            throw new InvalidDataException($"Target library at '{targetLibraryPath}' is invalid: {ex.Message}", ex);
        }

        string targetPromptsDir = Path.Combine(targetRoot, "prompts");
        foreach (var prompt in document.Prompts)
        {
            string promptPath = Path.Combine(targetPromptsDir, $"{prompt.Id:N}.md");
            if (!File.Exists(promptPath))
            {
                throw new InvalidDataException($"Target library references prompt file '{prompt.Id:N}.md' which does not exist in '{targetPromptsDir}'.");
            }

            try
            {
                using var stream = File.OpenRead(promptPath);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Target prompt file '{promptPath}' cannot be read: {ex.Message}", ex);
            }
        }
    }

    private void CopyAndValidateLibrary(
        string currentRoot,
        string targetRoot,
        LibraryDocument sourceDoc,
        List<string> createdDirs)
    {
        string targetPromptsDir = Path.Combine(targetRoot, "prompts");
        string targetRecoveryDir = Path.Combine(targetRoot, "recovery");

        if (!Directory.Exists(targetPromptsDir))
        {
            Directory.CreateDirectory(targetPromptsDir);
            createdDirs.Add(targetPromptsDir);
        }

        if (!Directory.Exists(targetRecoveryDir))
        {
            Directory.CreateDirectory(targetRecoveryDir);
            createdDirs.Add(targetRecoveryDir);
        }

        // Capture initial snapshot of source library.json and every referenced prompt body
        string sourceLibraryPath = Path.Combine(currentRoot, "library.json");
        string sourcePromptsDir = Path.Combine(currentRoot, "prompts");

        byte[] initialLibraryBytes = _fileOps.ReadAllBytes(sourceLibraryPath);
        byte[] initialLibraryHash = SHA256.HashData(initialLibraryBytes);

        var initialPromptHashes = new Dictionary<Guid, byte[]>();
        foreach (var prompt in sourceDoc.Prompts)
        {
            string pPath = Path.Combine(sourcePromptsDir, $"{prompt.Id:N}.md");
            byte[] pBytes = _fileOps.ReadAllBytes(pPath);
            initialPromptHashes.Add(prompt.Id, SHA256.HashData(pBytes));
        }

        var snapshot = new MigrationSnapshot(initialLibraryHash, initialPromptHashes);
        var createdFiles = new List<string>();

        try
        {
            string targetLibraryPath = Path.Combine(targetRoot, "library.json");
            CopyFileNoOverwrite(sourceLibraryPath, targetLibraryPath, createdFiles);

            string currentBackupPath = Path.Combine(currentRoot, "library.backup.json");
            if (File.Exists(currentBackupPath))
            {
                string targetBackupPath = Path.Combine(targetRoot, "library.backup.json");
                CopyFileNoOverwrite(currentBackupPath, targetBackupPath, createdFiles);
            }

            if (Directory.Exists(sourcePromptsDir))
            {
                foreach (string promptFile in _fileOps.EnumeratePromptFiles(sourcePromptsDir))
                {
                    string fileName = Path.GetFileName(promptFile);
                    string destPromptPath = Path.Combine(targetPromptsDir, fileName);
                    CopyFileNoOverwrite(promptFile, destPromptPath, createdFiles);
                }
            }

            string currentRecoveryDir = Path.Combine(currentRoot, "recovery");
            if (Directory.Exists(currentRecoveryDir))
            {
                foreach (string recoveryFile in Directory.EnumerateFiles(currentRecoveryDir, "*", SearchOption.TopDirectoryOnly))
                {
                    string fileName = Path.GetFileName(recoveryFile);
                    string destRecoveryPath = Path.Combine(targetRecoveryDir, fileName);
                    CopyFileNoOverwrite(recoveryFile, destRecoveryPath, createdFiles);
                }
            }

            // 1. Verify source library.json did not mutate
            byte[] finalSourceLibHash = SHA256.HashData(_fileOps.ReadAllBytes(sourceLibraryPath));
            if (!snapshot.LibraryHash.AsSpan().SequenceEqual(finalSourceLibHash))
            {
                throw new IOException("Source library metadata changed during migration. Retry after it is stable.");
            }

            // 2. Verify source prompt bodies did not mutate
            foreach (var prompt in sourceDoc.Prompts)
            {
                string pPath = Path.Combine(sourcePromptsDir, $"{prompt.Id:N}.md");
                byte[] finalSourcePromptHash = SHA256.HashData(_fileOps.ReadAllBytes(pPath));
                if (!snapshot.PromptHashes[prompt.Id].AsSpan().SequenceEqual(finalSourcePromptHash))
                {
                    throw new IOException($"Source prompt '{prompt.Id:N}.md' changed during migration. Retry after it is stable.");
                }

                // 3. Verify target prompt body matches source snapshot hash
                string targetPPath = Path.Combine(targetPromptsDir, $"{prompt.Id:N}.md");
                byte[] targetPromptHash = SHA256.HashData(_fileOps.ReadAllBytes(targetPPath));
                if (!snapshot.PromptHashes[prompt.Id].AsSpan().SequenceEqual(targetPromptHash))
                {
                    throw new IOException($"Target prompt '{prompt.Id:N}.md' does not match source snapshot.");
                }
            }

            // Validate target structure and probe target write capability
            ValidateExistingTargetLibrary(targetRoot, targetLibraryPath);
            _capabilityValidator.ValidateWritable(targetRoot);
        }
        catch
        {
            foreach (var createdFile in createdFiles)
            {
                try
                {
                    if (File.Exists(createdFile))
                    {
                        File.Delete(createdFile);
                    }
                }
                catch
                {
                    // Best effort cleanup of files created during failed migration
                }
            }

            var orderedDirs = createdDirs.OrderByDescending(d => d.Length).ToList();
            foreach (var dir in orderedDirs)
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

            throw;
        }
    }

    private void CopyFileNoOverwrite(string sourcePath, string destPath, List<string> createdFiles)
    {
        if (File.Exists(destPath))
        {
            throw new IOException($"Target file collision: '{destPath}' already exists.");
        }

        _fileOps.CopyFile(sourcePath, destPath, overwrite: false);
        createdFiles.Add(destPath);
    }
}
