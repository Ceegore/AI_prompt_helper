using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;
using PromptHelper.ViewModels;

namespace PromptHelper.Tests;

[TestClass]
public sealed class Cruu13ComprehensiveVerificationTests
{
    private static string Hash(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static LibraryDocument CreateDoc(params PromptRecord[] prompts) =>
        new()
        {
            SchemaVersion = 1,
            Categories = [],
            Prompts = [.. prompts]
        };

    private static void SeedValidLibrary(string root, out LibraryDocument doc)
    {
        Directory.CreateDirectory(root);
        string promptsDir = Path.Combine(root, "prompts");
        Directory.CreateDirectory(promptsDir);
        Directory.CreateDirectory(Path.Combine(root, "recovery"));

        Guid promptId = Guid.NewGuid();
        string promptFile = Path.Combine(promptsDir, $"{promptId:N}.md");
        File.WriteAllText(promptFile, "Active prompt body content");

        var category = new CategoryRecord { Id = Guid.NewGuid(), Name = "General" };
        var prompt = new PromptRecord { Id = promptId, CategoryId = category.Id, Title = "Test Prompt" };

        doc = new LibraryDocument
        {
            SchemaVersion = 1,
            Categories = [category],
            Prompts = [prompt]
        };

        string json = JsonSerializer.Serialize(doc, LibraryRepository.JsonOptions);
        File.WriteAllText(Path.Combine(root, "library.json"), json);
        File.WriteAllText(Path.Combine(root, "library.backup.json"), json);
    }

    // ==========================================
    // CRUU13-001: Fatal postcommit exception forces shutdown
    // ==========================================

    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void CRUU13_001_Fatal_mutation_exception_requests_shutdown_not_swallowed_as_save_error()
    {
        WpfTestHost.Invoke(() =>
        {
            using var temp = new TestDirectory();
            SeedValidLibrary(temp.Root, out var doc);
            var paths = new AppPaths(temp.Root);
            var writer = new AtomicTextWriter();
            var deleter = new FileDeleter();
            var libRepo = new LibraryRepository(paths, writer);
            var promptRepo = new PromptRepository(paths, writer, deleter);
            var libService = new PromptLibraryService(doc, libRepo, promptRepo);
            var lifetime = new FakeApplicationLifetime();
            var vm = new MainViewModel(libService, promptRepo, temp.Root);
            var window = new MainWindow(
                vm,
                new FakeClipboardService(),
                applicationLifetime: lifetime,
                showRestartMessage: (_, _) => { });

            Assert.IsFalse(window.FatalMutationShutdownRequested);

            var fatal = new CommittedMutationRequiresRestartException(
                Guid.NewGuid(),
                "The prompt was saved, but recovery bookkeeping could not be completed. Restart required.");

            // CommittedMutationRequiresRestartException derives from IOException. Before the
            // fix, MainWindow's `catch (Exception ex) when (ex is IOException or ...)` clauses
            // caught it as an ordinary save error and returned to the editor loop instead of
            // shutting down. HandleFatalMutationException is the path that must run instead.
            window.HandleFatalMutationException(fatal);

            Assert.IsTrue(window.FatalMutationShutdownRequested);
            Assert.IsTrue(lifetime.ShutdownRequested, "A committed-but-unresolved mutation must request application shutdown.");
        });
    }

    [TestMethod]
    public void CRUU13_001_Committed_mutation_exception_is_an_IOException_and_must_be_caught_first()
    {
        // Documents the exact hazard: because this type derives from IOException, any catch
        // clause ordered as `catch (Exception ex) when (ex is IOException or ...)` before a
        // specific `catch (CommittedMutationRequiresRestartException ex)` will silently absorb
        // it as an ordinary I/O failure.
        var ex = new CommittedMutationRequiresRestartException(Guid.NewGuid(), "restart required");
        Assert.IsInstanceOfType<IOException>(ex);
    }

    // ==========================================
    // CRUU13-002: Body-only edit commit authority is journal-phase-based, not content-hash-based
    // ==========================================

    [TestMethod]
    [TestCategory("MutationRecovery")]
    public void CRUU13_002_Body_only_edit_postcommit_failure_keeps_new_body_not_rolled_back()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        string promptPath = paths.GetPromptPath(promptId);

        // Body-only edit: the prompt record (title/category) is byte-identical before and
        // after, so OldLibrarySha256Hex == NewLibrarySha256Hex.
        var promptRecord = new PromptRecord { Id = promptId, Title = "Same Title", CategoryId = null };
        var doc = CreateDoc(promptRecord);
        var pkg = CanonicalLibraryPackage.Create(doc);

        var durableWriter = new FaultInjectingLibraryWriter();
        var libraryRepo = new LibraryRepository(paths, durableWriter);
        libraryRepo.Commit(pkg);

        var promptRepo = new PromptRepository(paths, durableWriter, new FileDeleter());
        promptRepo.Create(promptId, "Old Body");

        var journalRepo = new LibraryMutationJournalRepository(paths, durableWriter);
        var recovery = new LibraryMutationRecoveryService(paths, journalRepo, durableWriter, new WindowsVerifiedArtifactDeleter());
        var inspector = new LibraryPackageInspector(paths);
        var coordinator = new PromptMutationCoordinator(
            paths, promptRepo, libraryRepo, inspector, journalRepo, recovery, durableWriter,
            new WindowsVerifiedArtifactDeleter());

        // The primary commit (library.json) is content-neutral for this edit (bytes identical
        // before/after), so make it throw to simulate a crash right at that point. Under the
        // fix, the journal was already durably advanced to MetadataDurable *before* this call,
        // so recovery treats the mutation as committed and keeps the new body instead of
        // restoring the old one.
        durableWriter.FailLibraryJsonWrite = true;

        CommitResult result = coordinator.CommitEditPrompt(doc, doc, promptId, "New Body");

        Assert.IsTrue(result.BackupSynchronized, "Self-healing recovery must report the mutation as committed.");
        Assert.AreEqual("New Body", File.ReadAllText(promptPath),
            "A body-only edit whose content-neutral primary commit failed after the journal " +
            "already recorded MetadataDurable must keep the new body, not roll back to the old one.");
        Assert.IsFalse(File.Exists(paths.LibraryMutationJournalPath), "Journal must be retired by self-healing recovery.");
    }

    [TestMethod]
    [TestCategory("MutationRecovery")]
    public void CRUU13_002_Body_only_edit_precommit_failure_still_restores_old_body()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        string promptPath = paths.GetPromptPath(promptId);

        var promptRecord = new PromptRecord { Id = promptId, Title = "Same Title", CategoryId = null };
        var doc = CreateDoc(promptRecord);
        var pkg = CanonicalLibraryPackage.Create(doc);

        var durableWriter = new FaultInjectingLibraryWriter();
        var libraryRepo = new LibraryRepository(paths, durableWriter);
        libraryRepo.Commit(pkg);

        var promptRepo = new PromptRepository(paths, durableWriter, new FileDeleter());
        promptRepo.Create(promptId, "Old Body");

        var journalRepo = new LibraryMutationJournalRepository(paths, durableWriter);
        var recovery = new LibraryMutationRecoveryService(paths, journalRepo, durableWriter, new WindowsVerifiedArtifactDeleter());
        var inspector = new LibraryPackageInspector(paths);
        var coordinator = new PromptMutationCoordinator(
            paths, promptRepo, libraryRepo, inspector, journalRepo, recovery, durableWriter,
            new WindowsVerifiedArtifactDeleter());

        // Fail the pre-commit journal advance itself (before the body-only "commit" is even
        // attempted). Nothing durable has happened for metadata yet, so rollback to the old
        // body must still occur — the fix must not treat every failure as committed.
        durableWriter.FailMutationControlWrite = true;

        Assert.Throws<IOException>(() =>
            coordinator.CommitEditPrompt(doc, doc, promptId, "New Body"));

        Assert.AreEqual("Old Body", File.ReadAllText(promptPath),
            "A body-only edit that never reached a durable MetadataDurable phase must roll back to the old body.");
    }

    // ==========================================
    // CRUU13-003: Existing-target commit lease binds final content through settings commit
    // ==========================================

    [TestMethod]
    public void CRUU13_003_ExistingTargetCommitLease_denies_concurrent_write_to_leased_metadata()
    {
        using var temp = new TestDirectory();
        SeedValidLibrary(temp.Root, out var doc);
        string metadataPath = Path.Combine(temp.Root, "library.json");

        byte[] metadataBytes = File.ReadAllBytes(metadataPath);
        string promptPath = Path.Combine(temp.Root, "prompts", $"{doc.Prompts[0].Id:N}.md");
        byte[] promptBytes = File.ReadAllBytes(promptPath);

        byte[] expectedFingerprint = DataFolderMigrationService.ComputeCombinedFingerprint(
            metadataBytes,
            new System.Collections.Generic.Dictionary<Guid, byte[]> { [doc.Prompts[0].Id] = SHA256.HashData(promptBytes) });

        using var lease = ExistingTargetCommitLease.Acquire(temp.Root, metadataPath, doc, expectedFingerprint);

        // While the lease is held (FileShare.Read only), another writer must be denied.
        Assert.Throws<IOException>(() =>
        {
            using var writeAttempt = new FileStream(metadataPath, FileMode.Open, FileAccess.Write, FileShare.None);
        });
    }

    [TestMethod]
    public void CRUU13_003_ExistingTargetCommitLease_rejects_content_that_changed_since_inspection()
    {
        using var temp = new TestDirectory();
        SeedValidLibrary(temp.Root, out var doc);
        string metadataPath = Path.Combine(temp.Root, "library.json");

        // Fingerprint captured from a *stale* view; simulate the target having changed since.
        byte[] staleFingerprint = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("stale"));

        Assert.Throws<IOException>(() =>
            ExistingTargetCommitLease.Acquire(temp.Root, metadataPath, doc, staleFingerprint));
    }

    // ==========================================
    // CRUU13-004: Retry cleanup uses verified deletion, not raw path deletion
    // ==========================================

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU13_004_Foreign_content_at_declared_probe_control_path_is_not_deleted()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourcePayloadSnapshot(source.Root);
        Guid attemptId = Guid.NewGuid();
        var probePlan = MigrationCapabilityProbePlan.Create(attemptId);
        var manifest = MigrationManifestBuilder.BuildCopying(source.Root, target.Root, snapshot, attemptId, probePlan);

        string markerPath = Path.Combine(target.Root, ".prompthelper-migration.json");
        new MigrationManifestRepository().CreateInitialCopyingManifestDurable(markerPath, manifest);

        // A foreign file at the exact declared probe path, with content that does NOT match
        // the manifest's recorded expected hash ("create").
        string probeFile = Path.Combine(target.Root, probePlan.RootProbe.CurrentRelativePath);
        File.WriteAllText(probeFile, "not the real probe content");

        var recovery = new MigrationRecoveryService();
        var context = new MigrationRecoveryContext(target.Root, ExpectedSourcePhysicalRoot: source.Root);
        var result = recovery.RecoverForRetry(context);

        Assert.IsFalse(result.Success, "Recovery must fail closed instead of deleting unverified content at a declared control path.");
        Assert.IsTrue(File.Exists(probeFile), "Foreign content at a declared control path must never be destroyed.");
        Assert.AreEqual("not the real probe content", File.ReadAllText(probeFile));
    }

    // ==========================================
    // CRUU13-006: Schema-v3 retry enforces full payload fingerprint, not just the library hash
    // ==========================================

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU13_006_V3_retry_rejects_when_prompt_body_changed_but_library_hash_unchanged()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();

        SeedValidLibrary(source.Root, out var doc);
        var migration = new DataFolderMigrationService();
        var originalSnapshot = migration.CaptureSourcePayloadSnapshot(source.Root);
        Guid attemptId = Guid.NewGuid();
        var manifest = MigrationManifestBuilder.BuildCopying(source.Root, target.Root, originalSnapshot, attemptId);
        manifest.SchemaVersion = 3;

        string markerPath = Path.Combine(target.Root, ".prompthelper-migration.json");
        new MigrationManifestRepository().CreateInitialCopyingManifestDurable(markerPath, manifest);

        var primaryFile = originalSnapshot.Files.First(f => f.Role == MigrationPayloadRole.PrimaryMetadata);
        string expectedLibraryHash = Convert.ToHexStringLower(primaryFile.Sha256);

        // Modify the prompt BODY only; library.json is untouched, so the (old, v3-only)
        // library-hash check alone would still pass.
        string promptPath = Path.Combine(source.Root, "prompts", $"{doc.Prompts[0].Id:N}.md");
        File.WriteAllText(promptPath, "Body changed after the attempt was recorded");

        var freshSnapshot = migration.CaptureSourcePayloadSnapshot(source.Root);
        string freshFingerprint = MigrationPayloadFingerprint.Compute(freshSnapshot.Files);

        var recovery = new MigrationRecoveryService();
        var context = new MigrationRecoveryContext(
            target.Root,
            ExpectedSourcePhysicalRoot: source.Root,
            ExpectedSourcePayloadFingerprint: freshFingerprint,
            ExpectedSourceLibrarySha256: expectedLibraryHash);

        var result = recovery.RecoverForRetry(context);

        Assert.IsFalse(result.Success,
            "A schema-v3 retry must fail closed when the full payload fingerprint no longer matches, even if the library hash alone still matches.");
    }

    // ==========================================
    // CRUU13-009: Migration text decode is strict UTF-8, no BOM auto-detection
    // ==========================================

    [TestMethod]
    public void CRUU13_009_UTF16_encoded_target_metadata_is_not_silently_accepted()
    {
        using var target = new TestDirectory();
        Directory.CreateDirectory(target.Root);

        string json = "{\"schemaVersion\":1,\"categories\":[],\"prompts\":[]}";
        byte[] utf16Bytes = System.Text.Encoding.Unicode.GetPreamble()
            .Concat(System.Text.Encoding.Unicode.GetBytes(json))
            .ToArray();
        File.WriteAllBytes(Path.Combine(target.Root, "library.json"), utf16Bytes);

        var migration = new DataFolderMigrationService();
        var inspection = migration.InspectTarget(target.Root);

        Assert.AreNotEqual(
            DataFolderMigrationService.TargetLibraryKind.ValidPrimary,
            inspection.Kind,
            "UTF-16-encoded metadata must never be silently accepted as valid UTF-8 library content.");
    }

    // ==========================================
    // CRUU13-011: No wildcard capability-probe cleanup in production
    // ==========================================

    [TestMethod]
    public void CRUU13_011_Stale_foreign_probe_residue_is_not_wildcard_deleted()
    {
        using var temp = new TestDirectory();
        Directory.CreateDirectory(temp.Root);

        // Orphaned residue from an unrelated, already-finished (or different) attempt.
        string staleProbe = Path.Combine(temp.Root, ".prompthelper-capability-deadbeefdeadbeefdeadbeefdeadbeef-current.tmp");
        File.WriteAllText(staleProbe, "leftover");

        var validator = new DataRootCapabilityValidator();
        var result = validator.ValidateWritable(temp.Root);

        Assert.IsNull(result.Warning);
        Assert.IsTrue(File.Exists(staleProbe), "Production capability probing must not wildcard-delete unrelated leftover probe files.");
    }

    // ==========================================
    // CRUU13-012: Backup synchronization only accepts a primary-bound package
    // ==========================================

    [TestMethod]
    public void CRUU13_012_SynchronizeBackup_has_no_public_LibraryDocument_overload()
    {
        bool hasIndependentDocumentOverload = typeof(LibraryRepository)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(LibraryRepository.SynchronizeBackup))
            .Any(m =>
            {
                var parameters = m.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == typeof(LibraryDocument);
            });

        Assert.IsFalse(hasIndependentDocumentOverload,
            "Backup content must not be authorable independently from the current primary via a public LibraryDocument overload.");

        // Exercise the legitimate production path as well as checking the forbidden API
        // shape: a canonical package committed to primary is the authority used for backup.
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();
        var repository = new LibraryRepository(paths, new AtomicTextWriter());
        LibraryDocument document = CreateDoc();

        CommitResult result = repository.Commit(repository.CreateCanonicalPackage(document));

        Assert.IsTrue(result.BackupSynchronized);
        CollectionAssert.AreEqual(
            File.ReadAllBytes(paths.LibraryPath),
            File.ReadAllBytes(paths.LibraryBackupPath));
    }

    // ==========================================
    // CRUU13-018: Mutation journal schema requires an explicit revision
    // ==========================================

    [TestMethod]
    public void CRUU13_018_Journal_missing_revision_is_rejected_not_defaulted_to_zero()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        string json = $$"""
        {
            "schemaVersion": 2,
            "operationId": "{{Guid.NewGuid()}}",
            "kind": "CreatePrompt",
            "phase": "BodyDurable",
            "promptId": "{{promptId}}",
            "bodyRelativePath": "prompts\\{{promptId:N}}.md",
            "oldLibrarySha256Hex": "{{new string('0', 64)}}",
            "newLibrarySha256Hex": "{{new string('1', 64)}}",
            "newBodyLength": 4,
            "newBodySha256Hex": "{{new string('2', 64)}}"
        }
        """;
        File.WriteAllBytes(paths.LibraryMutationJournalPath, StrictUtf8Text.Encode(json));

        var repo = new LibraryMutationJournalRepository(paths, new AtomicTextWriter());
        Assert.Throws<JsonException>(() => repo.TryReadStrict());
    }

    // ==========================================
    // CRUU13-019: Ordinary CRUD commit preconditions close the TOCTOU window
    // ==========================================

    [TestMethod]
    public void CRUU13_019_CommitIfPrimaryUnchanged_rejects_stale_precondition()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        var writer = new AtomicTextWriter();
        var libRepo = new LibraryRepository(paths, writer);
        var originalDoc = CreateDoc();
        var originalPkg = CanonicalLibraryPackage.Create(originalDoc);
        libRepo.Commit(originalPkg);
        string staleHash = Hash(File.ReadAllBytes(paths.LibraryPath));

        // External writer changes library.json after the hash above was captured.
        var externallyChangedDoc = CreateDoc(new PromptRecord { Id = Guid.NewGuid(), Title = "External", CategoryId = null });
        libRepo.Commit(CanonicalLibraryPackage.Create(externallyChangedDoc));

        var newPkg = CanonicalLibraryPackage.Create(CreateDoc(new PromptRecord { Id = Guid.NewGuid(), Title = "Mine", CategoryId = null }));

        Assert.Throws<InvalidOperationException>(() => libRepo.CommitIfPrimaryUnchanged(newPkg, staleHash));

        // The externally-committed version must survive, not be silently overwritten.
        var current = libRepo.ReadPrimary();
        Assert.AreEqual(1, current.Prompts.Count);
        Assert.AreEqual("External", current.Prompts[0].Title);
    }

    [TestMethod]
    [TestCategory("MutationRecovery")]
    public void CRUU13_019_Edit_rejects_body_changed_externally_between_read_and_replace()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        var promptRecord = new PromptRecord { Id = promptId, Title = "Original", CategoryId = null };
        var initialDoc = CreateDoc(promptRecord);
        var initialPkg = CanonicalLibraryPackage.Create(initialDoc);

        // Injects an external body change the moment the recovery copy is durably written,
        // simulating a concurrent writer racing between the coordinator's initial body read
        // and its later ReplaceDurable call.
        string promptPath = paths.GetPromptPath(promptId);
        var durableWriter = new SideEffectDurableWriter(() => File.WriteAllText(promptPath, "Externally changed body"));

        var libraryRepo = new LibraryRepository(paths, durableWriter);
        libraryRepo.Commit(initialPkg);

        var promptRepo = new PromptRepository(paths, durableWriter, new FileDeleter());
        promptRepo.Create(promptId, "Old Body");

        var journalRepo = new LibraryMutationJournalRepository(paths, durableWriter);
        var recovery = new LibraryMutationRecoveryService(paths, journalRepo, durableWriter, new WindowsVerifiedArtifactDeleter());
        var inspector = new LibraryPackageInspector(paths);
        var coordinator = new PromptMutationCoordinator(
            paths, promptRepo, libraryRepo, inspector, journalRepo, recovery, durableWriter,
            new WindowsVerifiedArtifactDeleter());

        var updatedRecord = new PromptRecord { Id = promptId, Title = "New Title", CategoryId = null };
        var candidateDoc = CreateDoc(updatedRecord);

        Assert.Throws<IOException>(() =>
            coordinator.CommitEditPrompt(initialDoc, candidateDoc, promptId, "New Body"));

        Assert.AreEqual("Externally changed body", File.ReadAllText(promptPath),
            "The externally-written body must not be silently clobbered by the pending edit.");
    }

    private sealed class FaultInjectingLibraryWriter : IDurableAtomicFileWriter
    {
        private readonly WindowsDurableAtomicFileWriter _inner = new();
        public bool FailLibraryJsonWrite { get; set; }
        public bool FailMutationControlWrite { get; set; }

        public void ReplaceDurable(string targetPath, ReadOnlySpan<byte> bytes, DurableFileClass fileClass)
        {
            if (FailLibraryJsonWrite && fileClass == DurableFileClass.LibraryMetadata &&
                targetPath.EndsWith("library.json", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Injected primary commit failure.");
            }

            if (FailMutationControlWrite && fileClass == DurableFileClass.MutationControl)
            {
                throw new IOException("Injected journal advance failure.");
            }

            _inner.ReplaceDurable(targetPath, bytes, fileClass);
        }

        public void CreateNewDurable(string targetPath, ReadOnlySpan<byte> bytes, DurableFileClass fileClass)
        {
            if (FailMutationControlWrite && fileClass == DurableFileClass.MutationControl)
            {
                throw new IOException("Injected journal advance failure.");
            }

            _inner.CreateNewDurable(targetPath, bytes, fileClass);
        }
    }

    private sealed class SideEffectDurableWriter : IDurableAtomicFileWriter
    {
        private readonly WindowsDurableAtomicFileWriter _inner = new();
        private readonly Action _onRecoveryArtifactWritten;
        private bool _fired;

        public SideEffectDurableWriter(Action onRecoveryArtifactWritten)
        {
            _onRecoveryArtifactWritten = onRecoveryArtifactWritten;
        }

        public void ReplaceDurable(string targetPath, ReadOnlySpan<byte> bytes, DurableFileClass fileClass)
        {
            _inner.ReplaceDurable(targetPath, bytes, fileClass);
        }

        public void CreateNewDurable(string targetPath, ReadOnlySpan<byte> bytes, DurableFileClass fileClass)
        {
            _inner.CreateNewDurable(targetPath, bytes, fileClass);

            if (!_fired && fileClass == DurableFileClass.RecoveryArtifact)
            {
                _fired = true;
                _onRecoveryArtifactWritten();
            }
        }
    }
}
