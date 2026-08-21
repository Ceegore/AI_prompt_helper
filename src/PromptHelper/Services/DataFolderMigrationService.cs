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

    internal sealed record TargetContentPass(
        byte[] MetadataBytes,
        LibraryDocument Document,
        IReadOnlyDictionary<Guid, byte[]> PromptHashes);

    internal sealed record TargetContentSnapshot(
        byte[] MetadataBytes,
        LibraryDocument Document,
        IReadOnlyDictionary<Guid, byte[]> PromptHashes,
        byte[] CombinedFingerprint);

    internal abstract record TargetMetadataState
    {
        public sealed record Missing : TargetMetadataState;
        public sealed record StableCurrent(TargetContentSnapshot Snapshot) : TargetMetadataState;
        public sealed record Future(int Version) : TargetMetadataState;
        public sealed record Corrupt(Exception Error) : TargetMetadataState;
        public sealed record Unreadable(Exception Error) : TargetMetadataState;
        public sealed record Unstable(Exception Error) : TargetMetadataState;
    }

    internal enum TargetLibraryKind
    {
        Empty,
        ValidPrimary,
        RecoverableBackupOnly,
        CorruptPrimaryWithValidBackup,
        FutureSchema,
        Unreadable,
        Unstable,
        InterruptedMigration,
        OccupiedNonLibrary,
        Invalid
    }

    internal sealed record TargetInspection(
        string NormalizedRoot,
        TargetLibraryKind Kind,
        LibraryDocument? EffectiveDocument,
        string? EffectiveMetadataPath,
        string? Warning,
        Exception? Error,
        byte[]? Fingerprint);

    internal sealed class MigrationTargetTransaction : IDisposable, ICreatedPathJournal
    {
        private readonly List<string> _createdFiles = [];
        private readonly List<string> _createdDirectories = [];
        private bool _committed;
        private bool _rolledBack;

        public void TrackCreatedFile(string path) => _createdFiles.Add(path);
        public void TrackCreatedDirectory(string path) => _createdDirectories.Add(path);
        public void Commit() => _committed = true;

        public void PromoteCreatedFile(string oldOwnedPath, string newOwnedPath)
        {
            int index = _createdFiles.FindIndex(x => PathIdentity.Equals(x, oldOwnedPath));
            if (index < 0)
            {
                throw new InvalidOperationException("Cannot promote an untracked migration file.");
            }
            _createdFiles[index] = newOwnedPath;
        }

        public MigrationRollbackResult Rollback()
        {
            if (_committed || _rolledBack)
            {
                return new MigrationRollbackResult([]);
            }

            _rolledBack = true;
            var failures = new List<MigrationRollbackFailure>();

            foreach (string file in _createdFiles.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(new MigrationRollbackFailure(file, "DeleteFile", ex.Message));
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
                catch (Exception ex)
                {
                    failures.Add(new MigrationRollbackFailure(dir, "DeleteDirectory", ex.Message));
                }
            }

            return new MigrationRollbackResult(failures);
        }

        public void Dispose()
        {
            if (!_committed && !_rolledBack)
            {
                Rollback();
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
        _defaultBootstrapRoot = defaultBootstrapRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PromptHelper");
        _pathResolver = pathResolver ?? new WindowsPhysicalPathResolver();
    }

    internal MigrationSnapshot CaptureSourceSnapshot(string currentRoot)
    {
        var payload = CaptureSourcePayloadSnapshot(currentRoot);
        var prim = payload.Files.First(f => f.Role == MigrationPayloadRole.PrimaryMetadata);
        byte[] libBytes = _fileOps.ReadAllBytes(Path.Combine(currentRoot, "library.json"));
        var dict = new Dictionary<Guid, byte[]>();
        foreach (var p in payload.ActiveDocument.Prompts)
        {
            string pPath = Path.Combine(currentRoot, "prompts", $"{p.Id:N}.md");
            if (_fileOps.FileExists(pPath))
            {
                dict[p.Id] = _fileOps.ReadAllBytes(pPath);
            }
        }
        return new MigrationSnapshot(libBytes, prim.Sha256, payload.ActiveDocument, dict);
    }

    internal DataFolderChangeResult PrepareTargetForMigrationUnitTest(string currentRoot, string selectedRoot)
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
                _capabilityValidator.ValidateWritable(
                    cleanTarget,
                    null,
                    inspection.EffectiveDocument != null && inspection.EffectiveMetadataPath != null
                        ? new ExistingLibraryCapabilityContext(inspection.Kind, inspection.EffectiveMetadataPath, null, inspection.EffectiveDocument)
                        : null);
                return new DataFolderChangeResult(cleanTarget, ExistingLibraryFound: true, Copied: false, Warning: inspection.Warning);

            case TargetLibraryKind.RecoverableBackupOnly:
                _capabilityValidator.ValidateWritable(
                    cleanTarget,
                    null,
                    inspection.EffectiveDocument != null && inspection.EffectiveMetadataPath != null
                        ? new ExistingLibraryCapabilityContext(inspection.Kind, null, inspection.EffectiveMetadataPath, inspection.EffectiveDocument)
                        : null);
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

            case TargetLibraryKind.Unreadable:
                throw new InvalidOperationException($"The target data folder cannot be read: '{cleanTarget}'. {inspection.Error?.Message}", inspection.Error);

            case TargetLibraryKind.Unstable:
                throw new InvalidOperationException($"The target data folder is unstable: '{cleanTarget}'. {inspection.Error?.Message}", inspection.Error);

            case TargetLibraryKind.InterruptedMigration:
                throw new InvalidOperationException($"The target data folder contains an unfinished migration attempt: '{cleanTarget}'.");

            case TargetLibraryKind.OccupiedNonLibrary:
                throw new InvalidDataException($"The target data folder is not empty and does not contain a valid library: '{cleanTarget}'. {inspection.Error?.Message}");

            case TargetLibraryKind.Invalid:
                throw inspection.Error is InvalidDataException ide
                    ? ide
                    : new InvalidDataException($"The target data folder contains invalid or unreadable library data: '{cleanTarget}'. {inspection.Error?.Message}", inspection.Error);

            case TargetLibraryKind.Empty:
                break;

            default:
                throw new InvalidOperationException($"Unsupported target-library state: {inspection.Kind}.");
        }

        // Empty target copy workflow
        MigrationPayloadSnapshot snapshot = CaptureSourcePayloadSnapshot(cleanCurrent);

        using var tx = new MigrationTargetTransaction();
        CopySnapshotToTarget(cleanCurrent, cleanTarget, snapshot, Guid.NewGuid(), tx);
        _capabilityValidator.ValidateWritable(cleanTarget, tx, null);
        tx.Commit();

        return new DataFolderChangeResult(cleanTarget, ExistingLibraryFound: false, Copied: true);
    }

    internal TargetInspection InspectTarget(string targetRoot, bool isReservationActive = false)
    {
        string normalizedTarget = PathIdentity.NormalizeForComparison(targetRoot);

        if (!_fileOps.DirectoryExists(normalizedTarget))
        {
            return new TargetInspection(normalizedTarget, TargetLibraryKind.Empty, null, null, null, null, null);
        }

        string markerPath = Path.Combine(normalizedTarget, ".prompthelper-migration.json");
        if (_fileOps.FileExists(markerPath))
        {
            return new TargetInspection(
                normalizedTarget,
                TargetLibraryKind.InterruptedMigration,
                null,
                markerPath,
                "The target folder contains an unfinished migration attempt.",
                null,
                null);
        }

        string primaryPath = Path.Combine(normalizedTarget, "library.json");
        string backupPath = Path.Combine(normalizedTarget, "library.backup.json");

        bool primaryExists = _fileOps.FileExists(primaryPath);
        bool backupExists = _fileOps.FileExists(backupPath);

        if (!primaryExists && !backupExists)
        {
            EmptyTargetBaselineInspection baseline = EmptyTargetBaselineInspector.Inspect(
                normalizedTarget,
                _defaultBootstrapRoot ?? string.Empty,
                isReservationActive: isReservationActive,
                _fileOps);

            if (!baseline.IsAcceptable)
            {
                return new TargetInspection(
                    normalizedTarget,
                    TargetLibraryKind.OccupiedNonLibrary,
                    null,
                    null,
                    null,
                    new InvalidDataException(
                        $"The folder is not empty and contains unexpected files: {string.Join(", ", baseline.UnexpectedEntries)}."),
                    null);
            }

            return new TargetInspection(normalizedTarget, TargetLibraryKind.Empty, null, null, null, null, null);
        }

        TargetMetadataState primaryState = primaryExists
            ? ReadMetadataState(normalizedTarget, primaryPath, "Target library.json")
            : new TargetMetadataState.Missing();

        if (primaryState is TargetMetadataState.Future primaryFuture)
        {
            return new TargetInspection(
                normalizedTarget,
                TargetLibraryKind.FutureSchema,
                null,
                primaryPath,
                null,
                new UnsupportedLibrarySchemaException(primaryFuture.Version),
                null);
        }

        if (primaryState is TargetMetadataState.Unreadable primaryUnreadable)
        {
            return new TargetInspection(
                normalizedTarget,
                TargetLibraryKind.Unreadable,
                null,
                primaryPath,
                null,
                primaryUnreadable.Error,
                null);
        }

        if (primaryState is TargetMetadataState.Unstable primaryUnstable)
        {
            return new TargetInspection(
                normalizedTarget,
                TargetLibraryKind.Unstable,
                null,
                primaryPath,
                null,
                primaryUnstable.Error,
                null);
        }

        if (primaryState is TargetMetadataState.StableCurrent primaryStable)
        {
            return new TargetInspection(
                normalizedTarget,
                TargetLibraryKind.ValidPrimary,
                primaryStable.Snapshot.Document,
                primaryPath,
                null,
                null,
                primaryStable.Snapshot.CombinedFingerprint);
        }

        TargetMetadataState backupState = backupExists
            ? ReadMetadataState(normalizedTarget, backupPath, "Target library.backup.json")
            : new TargetMetadataState.Missing();

        if (backupState is TargetMetadataState.Future backupFuture)
        {
            return new TargetInspection(
                normalizedTarget,
                TargetLibraryKind.FutureSchema,
                null,
                backupPath,
                null,
                new UnsupportedLibrarySchemaException(backupFuture.Version),
                null);
        }

        if (primaryState is TargetMetadataState.Corrupt corruptPrimary &&
            backupState is TargetMetadataState.StableCurrent backupStable)
        {
            return new TargetInspection(
                normalizedTarget,
                TargetLibraryKind.CorruptPrimaryWithValidBackup,
                backupStable.Snapshot.Document,
                backupPath,
                null,
                corruptPrimary.Error,
                null);
        }

        if (primaryState is TargetMetadataState.Missing &&
            backupState is TargetMetadataState.StableCurrent validBackupOnly)
        {
            return new TargetInspection(
                normalizedTarget,
                TargetLibraryKind.RecoverableBackupOnly,
                validBackupOnly.Snapshot.Document,
                backupPath,
                "The selected folder contains a recoverable Prompt Helper safety backup but no primary library.json. Prompt Helper will recover it on startup; the current library will not be copied there.",
                null,
                validBackupOnly.Snapshot.CombinedFingerprint);
        }

        Exception error;
        if (primaryState is TargetMetadataState.Corrupt cp)
        {
            error = cp.Error;
        }
        else if (backupState is TargetMetadataState.Corrupt cb)
        {
            error = cb.Error;
        }
        else if (backupState is TargetMetadataState.Unreadable ub)
        {
            error = ub.Error;
        }
        else
        {
            error = new InvalidDataException("Target library metadata is invalid or cannot be recognized.");
        }

        return new TargetInspection(
            normalizedTarget,
            TargetLibraryKind.Invalid,
            null,
            primaryExists ? primaryPath : backupPath,
            null,
            error,
            null);
    }

    private TargetMetadataState ReadMetadataState(
        string root,
        string metadataPath,
        string metadataDescription)
    {
        try
        {
            TargetContentSnapshot snapshot = CaptureTargetContentSnapshot(root, metadataPath, metadataDescription);
            return new TargetMetadataState.StableCurrent(snapshot);
        }
        catch (UnsupportedLibrarySchemaException ex)
        {
            return new TargetMetadataState.Future(ex.SchemaVersion);
        }
        catch (TargetInspectionUnstableException ex)
        {
            return new TargetMetadataState.Unstable(ex);
        }
        catch (Exception ex) when (
            ex is JsonException or
            InvalidDataException or
            ArgumentException)
        {
            return new TargetMetadataState.Corrupt(ex);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            SecurityException)
        {
            return new TargetMetadataState.Unreadable(ex);
        }
    }

    private TargetContentSnapshot CaptureTargetContentSnapshot(
        string root,
        string metadataPath,
        string metadataDescription)
    {
        TargetContentPass pass1 = CaptureTargetContentPass(root, metadataPath, metadataDescription);
        TargetContentPass pass2 = CaptureTargetContentPass(root, metadataPath, metadataDescription);

        if (!ContentPassesEqual(pass1, pass2))
        {
            throw new TargetInspectionUnstableException(
                "Target library changed while being inspected. Retry with a stable target.");
        }

        byte[] combinedFingerprint = ComputeCombinedFingerprint(pass2.MetadataBytes, pass2.PromptHashes);

        return new TargetContentSnapshot(
            MetadataBytes: pass2.MetadataBytes,
            Document: pass2.Document,
            PromptHashes: pass2.PromptHashes,
            CombinedFingerprint: combinedFingerprint);
    }

    private TargetContentPass CaptureTargetContentPass(
        string root,
        string metadataPath,
        string metadataDescription)
    {
        byte[] metadataBytes = _fileOps.ReadAllBytes(metadataPath);
        string metadataJson = DecodeUtf8Text(metadataBytes);

        LibraryDocument document = LibraryRepository.InspectAndDeserialize(metadataJson);
        LibraryValidator.Validate(document);

        string promptsDir = Path.Combine(root, "prompts");
        var promptHashes = new Dictionary<Guid, byte[]>();

        foreach (PromptRecord prompt in document.Prompts)
        {
            string promptPath = Path.Combine(promptsDir, $"{prompt.Id:N}.md");
            if (!_fileOps.FileExists(promptPath))
            {
                throw new InvalidDataException(
                    $"{metadataDescription} references prompt file '{prompt.Id:N}.md', but it is missing from '{promptsDir}'.");
            }

            byte[] bodyBytes = _fileOps.ReadAllBytes(promptPath);
            promptHashes[prompt.Id] = SHA256.HashData(bodyBytes);
        }

        return new TargetContentPass(
            MetadataBytes: metadataBytes,
            Document: document,
            PromptHashes: promptHashes);
    }

    private static bool ContentPassesEqual(TargetContentPass left, TargetContentPass right)
    {
        if (!left.MetadataBytes.AsSpan().SequenceEqual(right.MetadataBytes))
        {
            return false;
        }

        if (left.PromptHashes.Count != right.PromptHashes.Count)
        {
            return false;
        }

        foreach (var pair in left.PromptHashes)
        {
            if (!right.PromptHashes.TryGetValue(pair.Key, out byte[]? other) ||
                !pair.Value.AsSpan().SequenceEqual(other))
            {
                return false;
            }
        }

        return true;
    }

    internal static byte[] ComputeCombinedFingerprint(
        byte[] metadataBytes,
        IReadOnlyDictionary<Guid, byte[]> promptHashes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(metadataBytes);

        foreach (var kvp in promptHashes.OrderBy(x => x.Key))
        {
            hash.AppendData(kvp.Key.ToByteArray());
            hash.AppendData(kvp.Value);
        }

        return hash.GetHashAndReset();
    }

    internal MigrationPayloadSnapshot CaptureSourcePayloadSnapshot(string currentRoot)
    {
        if (!_fileOps.DirectoryExists(currentRoot))
        {
            throw new DirectoryNotFoundException(
                $"Library directory does not exist: '{currentRoot}'");
        }

        string libraryPath = Path.Combine(currentRoot, "library.json");
        if (!_fileOps.FileExists(libraryPath))
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

        var files = new List<MigrationPayloadFile>();
        var relativePathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Primary metadata
        files.Add(new MigrationPayloadFile(
            "library.json",
            MigrationPayloadRole.PrimaryMetadata,
            libraryBytes.Length,
            libraryHash));
        relativePathSet.Add("library.json");

        // 2. Safety backup if present
        string backupPath = Path.Combine(currentRoot, "library.backup.json");
        if (_fileOps.FileExists(backupPath))
        {
            byte[] backupBytes = _fileOps.ReadAllBytes(backupPath);
            files.Add(new MigrationPayloadFile(
                "library.backup.json",
                MigrationPayloadRole.SafetyBackup,
                backupBytes.Length,
                SHA256.HashData(backupBytes)));
            relativePathSet.Add("library.backup.json");
        }

        // 3. Active Prompts & Orphan Prompts
        string promptsDir = Path.Combine(currentRoot, "prompts");
        var activePromptFileNames = new HashSet<string>(
            document.Prompts.Select(p => $"{p.Id:N}.md"),
            StringComparer.OrdinalIgnoreCase);

        if (_fileOps.DirectoryExists(promptsDir))
        {
            foreach (string promptFile in _fileOps.EnumeratePromptFiles(promptsDir))
            {
                string fileName = Path.GetFileName(promptFile);
                string relPath = Path.Combine("prompts", fileName);
                byte[] bytes = _fileOps.ReadAllBytes(promptFile);
                MigrationPayloadRole role = activePromptFileNames.Contains(fileName)
                    ? MigrationPayloadRole.PromptBody
                    : MigrationPayloadRole.OrphanPromptBody;

                files.Add(new MigrationPayloadFile(
                    relPath,
                    role,
                    bytes.Length,
                    SHA256.HashData(bytes)));
                relativePathSet.Add(relPath);
            }
        }

        // Ensure all active prompts exist
        foreach (PromptRecord prompt in document.Prompts)
        {
            string relPath = Path.Combine("prompts", $"{prompt.Id:N}.md");
            if (!relativePathSet.Contains(relPath))
            {
                throw new InvalidDataException(
                    $"Library references prompt file '{prompt.Id:N}.md' which does not exist in '{promptsDir}'.");
            }
        }

        // 4. Top-level Recovery Artifacts
        string recoveryDir = Path.Combine(currentRoot, "recovery");
        if (_fileOps.DirectoryExists(recoveryDir))
        {
            foreach (string recFile in _fileOps.EnumerateFiles(recoveryDir))
            {
                string fileName = Path.GetFileName(recFile);
                string relPath = Path.Combine("recovery", fileName);
                byte[] bytes = _fileOps.ReadAllBytes(recFile);

                files.Add(new MigrationPayloadFile(
                    relPath,
                    MigrationPayloadRole.RecoveryArtifact,
                    bytes.Length,
                    SHA256.HashData(bytes)));
                relativePathSet.Add(relPath);
            }
        }

        return new MigrationPayloadSnapshot(
            ActiveDocument: document,
            Files: files,
            RelativePathSet: relativePathSet);
    }

    internal void CopySnapshotToTarget(
        string currentRoot,
        string targetRoot,
        MigrationPayloadSnapshot snapshot,
        Guid attemptId,
        MigrationTargetTransaction tx,
        IReadOnlyDictionary<string, string>? declaredTempMap = null)
    {
        EnsureDirectoryTracked(targetRoot, tx);

        // Copy all files listed in snapshot
        foreach (MigrationPayloadFile item in snapshot.Files)
        {
            string sourcePath = Path.Combine(currentRoot, item.RelativePath);
            string destPath = Path.Combine(targetRoot, item.RelativePath);

            string tempPath;
            if (declaredTempMap != null && declaredTempMap.TryGetValue(item.RelativePath, out string? declaredTempRel))
            {
                tempPath = Path.Combine(targetRoot, declaredTempRel);
            }
            else
            {
                string directory = Path.GetDirectoryName(destPath) ?? targetRoot;
                tempPath = Path.Combine(
                    directory,
                    $".{Path.GetFileName(destPath)}.migration-{attemptId:N}-{RandomNumberGenerator.GetHexString(16).ToLowerInvariant()}.tmp");
            }

            CopyPayloadFileDurablyWithTemp(sourcePath, destPath, tempPath, tx);
        }

        // Verify eligible source file set has not changed
        HashSet<string> currentEligibleSourceFiles = EnumerateEligiblePayloadFiles(currentRoot);
        if (!currentEligibleSourceFiles.SetEquals(snapshot.RelativePathSet))
        {
            throw new IOException("Source library file set changed during migration. Transition aborted.");
        }

        // Verify SHA256 and length of every source file
        foreach (MigrationPayloadFile item in snapshot.Files)
        {
            string sourcePath = Path.Combine(currentRoot, item.RelativePath);
            if (!_fileOps.FileExists(sourcePath))
            {
                throw new IOException($"Source file '{item.RelativePath}' disappeared during migration.");
            }

            byte[] currentBytes = _fileOps.ReadAllBytes(sourcePath);
            if (currentBytes.Length != item.Length ||
                !SHA256.HashData(currentBytes).AsSpan().SequenceEqual(item.Sha256))
            {
                throw new IOException($"Source file '{item.RelativePath}' changed during migration. Transition aborted.");
            }
        }

        // Verify SHA256 and length of every target file
        foreach (MigrationPayloadFile item in snapshot.Files)
        {
            string targetPath = Path.Combine(targetRoot, item.RelativePath);
            if (!_fileOps.FileExists(targetPath))
            {
                throw new IOException($"Target file '{item.RelativePath}' was not created.");
            }

            byte[] targetBytes = _fileOps.ReadAllBytes(targetPath);
            if (targetBytes.Length != item.Length ||
                !SHA256.HashData(targetBytes).AsSpan().SequenceEqual(item.Sha256))
            {
                throw new IOException($"Target file '{item.RelativePath}' does not match source snapshot.");
            }
        }

        // Structural verification of target metadata and prompts
        ValidateDocumentPromptBodies(targetRoot, snapshot.ActiveDocument, "Migrated target library");
    }

    private HashSet<string> EnumerateEligiblePayloadFiles(string root)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string libPath = Path.Combine(root, "library.json");
        if (_fileOps.FileExists(libPath))
        {
            set.Add("library.json");
        }

        string backupPath = Path.Combine(root, "library.backup.json");
        if (_fileOps.FileExists(backupPath))
        {
            set.Add("library.backup.json");
        }

        string promptsDir = Path.Combine(root, "prompts");
        if (_fileOps.DirectoryExists(promptsDir))
        {
            foreach (string file in _fileOps.EnumeratePromptFiles(promptsDir))
            {
                set.Add(Path.Combine("prompts", Path.GetFileName(file)));
            }
        }

        string recoveryDir = Path.Combine(root, "recovery");
        if (_fileOps.DirectoryExists(recoveryDir))
        {
            foreach (string file in _fileOps.EnumerateFiles(recoveryDir))
            {
                set.Add(Path.Combine("recovery", Path.GetFileName(file)));
            }
        }

        return set;
    }

    private void CopyPayloadFileDurablyWithTemp(
        string sourcePath,
        string finalPath,
        string tempPath,
        MigrationTargetTransaction tx)
    {
        if (_fileOps.FileExists(finalPath))
        {
            throw new IOException($"Target file collision: '{finalPath}' already exists.");
        }

        string directory = Path.GetDirectoryName(finalPath)
            ?? throw new InvalidOperationException("Target payload path has no directory.");

        EnsureDirectoryTracked(directory, tx);

        using (Stream source = _fileOps.OpenRead(sourcePath))
        using (Stream destination = _fileOps.CreateNewFile(tempPath))
        {
            tx.TrackCreatedFile(tempPath);
            source.CopyTo(destination);
            _fileOps.FlushToDisk(destination);
        }

        _fileOps.MoveNoOverwriteWriteThrough(tempPath, finalPath);
        tx.PromoteCreatedFile(tempPath, finalPath);
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
