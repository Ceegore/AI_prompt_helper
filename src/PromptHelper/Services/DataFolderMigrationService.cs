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
using Microsoft.Win32.SafeHandles;

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

    public enum MigrationOwnedFileState
    {
        TempPlanned,
        TempOwned,
        FinalOwned,

        /// <summary>
        /// The stage was created and then destroyed through its own retained handle, so
        /// nothing this attempt owns remains at the temp pathname. Rollback must not touch
        /// that pathname afterwards: anything there now belongs to someone else.
        /// </summary>
        TempAbandoned
    }

    public sealed class MigrationOwnedFile
    {
        public required string TempPath { get; init; }
        public required string FinalPath { get; init; }
        public required long ExpectedLength { get; init; }
        public required string ExpectedSha256Hex { get; init; }

        public MigrationOwnedFileState State { get; private set; } = MigrationOwnedFileState.TempPlanned;

        /// <summary>The exact stage identity captured while its creation handle is retained.</summary>
        public string? TempIdentityToken { get; private set; }

        public void MarkTempOwned(string identityToken)
        {
            if (State != MigrationOwnedFileState.TempPlanned)
                throw new InvalidOperationException($"Cannot transition to TempOwned from {State}.");

            ArgumentException.ThrowIfNullOrWhiteSpace(identityToken);
            TempIdentityToken = identityToken;
            State = MigrationOwnedFileState.TempOwned;
        }

        /// <summary>The identity of the promoted object, so rollback can prove it before deleting.</summary>
        public string? FinalIdentityToken { get; private set; }

        public void MarkFinalOwnedAfterMove(string? identityToken = null)
        {
            State = MigrationOwnedFileState.FinalOwned;
            FinalIdentityToken = identityToken;
        }

        public void MarkTempAbandoned()
        {
            State = MigrationOwnedFileState.TempAbandoned;
        }
    }

    internal sealed class MigrationTargetTransaction : IDisposable, ICreatedPathJournal
    {
        internal static Action? RollbackEnteredForTests;

        private readonly List<MigrationOwnedFile> _ownedFiles = [];
        private readonly List<string> _createdDirectories = [];
        private readonly IVerifiedArtifactDeleter _verifiedDeleter;
        private readonly string _targetPhysicalRoot;
        private bool _committed;
        private bool _rolledBack;

        public MigrationTargetTransaction(string targetPhysicalRoot = "", IVerifiedArtifactDeleter? verifiedDeleter = null)
        {
            _targetPhysicalRoot = string.IsNullOrWhiteSpace(targetPhysicalRoot) ? Directory.GetCurrentDirectory() : targetPhysicalRoot;
            _verifiedDeleter = verifiedDeleter ?? new WindowsVerifiedArtifactDeleter();
        }

        public MigrationOwnedFile RegisterPlannedFile(
            string tempPath,
            string finalPath,
            long expectedLength,
            string expectedSha256Hex)
        {
            var file = new MigrationOwnedFile
            {
                TempPath = tempPath,
                FinalPath = finalPath,
                ExpectedLength = expectedLength,
                ExpectedSha256Hex = expectedSha256Hex
            };
            _ownedFiles.Add(file);
            return file;
        }

        public void TrackCreatedFile(string path) { }
        public void TrackCreatedDirectory(string path) => _createdDirectories.Add(path);
        public void Commit() => _committed = true;

        public void PromoteCreatedFile(string oldOwnedPath, string newOwnedPath) { }

        /// <summary>
        /// Deletes a promoted payload object only when the identity recorded at creation still
        /// matches what is there. An object with the right bytes but a different identity is
        /// somebody else's file that happens to look like ours (CRUU16-005).
        /// </summary>
        private void RemoveOwnedFile(
            MigrationOwnedFile file,
            string path,
            string? identityToken,
            string operation,
            List<MigrationRollbackFailure> failures)
        {
            try
            {
                if (identityToken is null)
                {
                    // No exact-object authority exists for this location.
                    return;
                }

                if (new StrictPathAuthority().Probe(path).Kind != StrictPathKind.File)
                {
                    return;
                }

                // Identity, content and deletion all through one retained handle: a second open
                // would both reintroduce a substitution window and collide with the exclusive
                // share mode the deletion itself needs.
                bool deleted = _verifiedDeleter.TryVerifyIdentityContentAndDelete(
                    _targetPhysicalRoot,
                    path,
                    file.ExpectedLength,
                    file.ExpectedSha256Hex,
                    identityToken);

                if (!deleted)
                {
                    failures.Add(new MigrationRollbackFailure(
                        path,
                        operation,
                        "The object here is not the one this attempt created, so it was preserved."));
                }
            }
            catch (Exception ex)
            {
                failures.Add(new MigrationRollbackFailure(path, operation, ex.Message));
            }
        }

        public MigrationRollbackResult Rollback()
        {
            RollbackEnteredForTests?.Invoke();

            if (_committed || _rolledBack)
            {
                return new MigrationRollbackResult([]);
            }

            _rolledBack = true;
            var failures = new List<MigrationRollbackFailure>();

            // CRUU16-005: a promoted final is removed only when the recorded creation identity
            // still matches the object at that pathname. Expected length and hash prove content
            // and location, which a foreign replacement carrying identical bytes also satisfies.
            foreach (MigrationOwnedFile file in _ownedFiles.AsEnumerable().Reverse())
            {
                if (file.State == MigrationOwnedFileState.FinalOwned)
                {
                    RemoveOwnedFile(
                        file,
                        file.FinalPath,
                        file.FinalIdentityToken,
                        "DeleteFinal",
                        failures);
                }
                else if (file.State == MigrationOwnedFileState.TempOwned)
                {
                    RemoveOwnedFile(
                        file,
                        file.TempPath,
                        file.TempIdentityToken,
                        "DeleteTemp",
                        failures);
                    // A promotion that reached the filesystem but threw before the in-memory
                    // phase advanced is still authorized by the same exact stage identity.
                    RemoveOwnedFile(
                        file,
                        file.FinalPath,
                        file.TempIdentityToken,
                        "DeleteUnexpectedFinal",
                        failures);
                }
            }

            // Every claim this attempt made is now dead, so the ledger itself has nothing left
            // to protect. Retiring it here is what lets an attempt-created root actually become
            // empty again; leaving it behind would make rollback unable to remove the very
            // directory it created.
            try
            {
                OwnedArtifactReconciler.Result reconciliation =
                    OwnedArtifactReconciler.Reconcile(
                        _targetPhysicalRoot,
                        new WindowsOwnedArtifactJournal());

                foreach (ReconciliationOutcome outcome in reconciliation.Outcomes.Where(
                             o => o.Severity is ReconciliationSeverity.Warning or ReconciliationSeverity.Fatal))
                {
                    failures.Add(new MigrationRollbackFailure(
                        outcome.Path,
                        $"Ownership:{outcome.Code}",
                        outcome.Message));
                }

                if (reconciliation.HasFatal)
                {
                    // The ledger is the authority for every remaining destructive action. A
                    // fatal semantic state means the manifest and directories must be kept.
                    return new MigrationRollbackResult(failures);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new MigrationRollbackFailure(
                    _targetPhysicalRoot, "RetireOwnershipLedger", ex.Message));
            }

            // CRUU16-006: removed through a single retained directory handle, so the directory
            // proven empty and the directory removed are the same object. The kernel re-checks
            // emptiness atomically when the disposition is applied.
            foreach (string dir in _createdDirectories.OrderByDescending(x => x.Length))
            {
                try
                {
                    using WindowsRetirableDirectory? handle =
                        WindowsRetirableDirectory.OpenExistingOrNull(dir, _targetPhysicalRoot);
                    handle?.DeleteExact();
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

            // CRUU14-007: a target could otherwise have a fully valid, strictly-UTF-8
            // library.json with stable content fingerprints while an active prompt body is
            // UTF-16 or otherwise invalid UTF-8 — content normal prompt reading would refuse.
            // Reject that target here, in both content passes, not just when it is later read.
            try
            {
                DecodeUtf8Text(bodyBytes);
            }
            catch (Exception ex) when (ex is DecoderFallbackException or InvalidDataException)
            {
                throw new InvalidDataException(
                    $"{metadataDescription} references prompt file '{prompt.Id:N}.md', but it is not valid UTF-8 text.",
                    ex);
            }

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
            string backupJson = DecodeUtf8Text(backupBytes);
            try
            {
                LibraryDocument backupDoc = LibraryRepository.InspectAndDeserialize(backupJson);
                LibraryValidator.Validate(backupDoc);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Safety backup at '{backupPath}' is invalid: {ex.Message}", ex);
            }

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

                // Strict UTF-8 validation for all markdown prompt bodies
                if (fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    StrictUtf8Text.Decode(bytes, $"prompt body '{fileName}'");
                }

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
        MigrationAttemptManifest manifest,
        MigrationTargetTransaction tx)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (manifest.Artifacts.Count != snapshot.Files.Count)
        {
            throw new InvalidDataException(
                $"Manifest artifact count ({manifest.Artifacts.Count}) does not match snapshot file count ({snapshot.Files.Count}).");
        }

        var artifactMap = new Dictionary<string, MigrationManifestArtifact>(StringComparer.OrdinalIgnoreCase);
        foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
        {
            if (!artifactMap.TryAdd(artifact.RelativePath, artifact))
            {
                throw new InvalidDataException($"Duplicate artifact relative path in manifest: '{artifact.RelativePath}'.");
            }
        }

        var treeValidator = new ManagedTreeTopologyValidator(_pathResolver);
        treeValidator.ValidateManagedTree(targetRoot, ManagedTreeValidationMode.PreCreation);

        EnsureDirectoryTracked(targetRoot, tx);

        // Copy all files listed in snapshot
        foreach (MigrationPayloadFile item in snapshot.Files)
        {
            if (!artifactMap.TryGetValue(item.RelativePath, out MigrationManifestArtifact? artifact))
            {
                throw new InvalidDataException($"Snapshot file '{item.RelativePath}' is missing from migration manifest.");
            }

            if (artifact.Role != item.Role || artifact.Length != item.Length ||
                !string.Equals(artifact.Sha256Hex, Convert.ToHexStringLower(item.Sha256), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Manifest artifact metadata mismatch for '{item.RelativePath}'.");
            }

            string sourcePath = Path.Combine(currentRoot, item.RelativePath);
            string destPath = Path.Combine(targetRoot, item.RelativePath);
            string tempPath = Path.Combine(targetRoot, artifact.TempRelativePath);

            CopyPayloadFileDurablyWithTemp(sourcePath, destPath, tempPath, artifact, tx);
        }

        // Every payload stage created above reached a terminal state (promoted, or destroyed
        // through its own handle), so its ownership claims are spent. Settle them now, while
        // this operation still owns the target, rather than leaving in-flight control state
        // behind for the terminal invariants to trip over (CRUU15-006).
        _fileOps.RetireOwnedArtifacts(targetRoot);

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
        MigrationManifestArtifact artifact,
        MigrationTargetTransaction tx)
    {
        if (_fileOps.FileExists(finalPath))
        {
            throw new IOException($"Target file collision: '{finalPath}' already exists.");
        }

        string directory = Path.GetDirectoryName(finalPath)
            ?? throw new InvalidOperationException("Target payload path has no directory.");

        EnsureDirectoryTracked(directory, tx);

        MigrationOwnedFile owned = tx.RegisterPlannedFile(
            tempPath,
            finalPath,
            artifact.Length,
            artifact.Sha256Hex);

        // CRUU15-002: the staged copy is owned by a retained handle from creation through
        // promotion. "Owned temp" used to be a path string, so an object substituted at that
        // pathname after the copy closed could be promoted into the target in place of the
        // bytes actually written here.
        byte[] payload;
        using (Stream source = _fileOps.OpenRead(sourcePath))
        {
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);
            payload = buffer.ToArray();
        }

        string targetRoot = Path.GetDirectoryName(Path.GetFullPath(finalPath)) == directory
            ? ResolvePayloadRoot(directory)
            : ResolvePayloadRoot(directory);

        using IOwnedFileStage stage = _fileOps.CreateOwnedStage(targetRoot, tempPath);
        owned.MarkTempOwned(stage.IdentityToken);

        try
        {
            stage.Write(payload);
            stage.FlushDurable();

            MigrationArtifactClaim claim = _fileOps.RecordMigrationArtifactPrepared(
                targetRoot,
                tempPath,
                finalPath,
                stage.IdentityToken,
                artifact.Length,
                artifact.Sha256Hex);

            stage.PromoteNoOverwriteExact(finalPath);

            // Publication, not the following journal append, advances in-memory rollback
            // authority. The retained stage identity is unchanged by rename.
            owned.MarkFinalOwnedAfterMove(stage.IdentityToken);
            _fileOps.RecordMigrationArtifactPublished(targetRoot, claim);
        }
        catch
        {
            try
            {
                stage.DeleteExact();
                owned.MarkTempAbandoned();
            }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
            {
                // Left for rollback/recovery, which can still prove ownership from the manifest.
            }

            throw;
        }

        // The same durable operation describes both paths across the rename cut. There is no
        // post-publication window in which recovery has only a vanished temp pathname.
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

            if (new StrictPathAuthority().Probe(promptPath).Kind != StrictPathKind.File)
            {
                throw new InvalidDataException(
                    $"{metadataDescription} references prompt file " +
                    $"'{prompt.Id:N}.md', but it is missing from '{promptsDir}'.");
            }

            try
            {
                StrictUtf8Text.ReadAllText(promptPath, $"{metadataDescription} prompt file '{prompt.Id:N}.md'");
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                SecurityException or
                InvalidDataException)
            {
                throw new InvalidDataException(
                    $"{metadataDescription} references unreadable prompt file " +
                    $"'{promptPath}': {ex.Message}",
                    ex);
            }
        }
    }

    /// <summary>
    /// The data root a payload artifact belongs to: payload files live either directly in the
    /// target root or in its <c>prompts</c>/<c>recovery</c> children.
    /// </summary>
    private static string ResolvePayloadRoot(string directory)
        => DefaultMigrationFileOps.ResolveJournalRoot(directory);

    private static void EnsureDirectoryTracked(
        string path,
        MigrationTargetTransaction tx,
        IOwnedDirectoryCreator? creator = null)
    {
        var activeCreator = creator ?? new WindowsOwnedDirectoryCreator();
        DirectoryCreateOutcome outcome = activeCreator.TryCreateOwned(path);
        if (outcome == DirectoryCreateOutcome.CreatedByCaller)
        {
            tx.TrackCreatedDirectory(path);
        }
        else
        {
            if (new StrictPathAuthority().Probe(path).Kind != StrictPathKind.Directory)
            {
                throw new InvalidDataException($"Expected directory at '{path}'.");
            }
        }
    }

    private static string DecodeUtf8Text(byte[] bytes)
    {
        // Route through the same strict decoder used elsewhere: it only strips a UTF-8 BOM
        // and otherwise requires valid UTF-8, so a UTF-16/UTF-32 BOM can no longer switch
        // .NET's auto-detection into decoding the bytes as something other than UTF-8.
        return StrictUtf8Text.Decode(bytes, "migration text content");
    }
}
