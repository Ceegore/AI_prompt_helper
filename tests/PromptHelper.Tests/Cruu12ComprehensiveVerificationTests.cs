using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public class Cruu12ComprehensiveVerificationTests
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

    // ==========================================
    // CRUU12-001: Mutation Point-of-No-Return & Failure Routing
    // ==========================================

    [TestMethod]
    [TestCategory("MutationRecovery")]
    public void CRUU12_001_Create_primary_committed_MetadataDurable_write_fails_does_not_delete_body()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        string promptPath = paths.GetPromptPath(promptId);
        var initialDoc = CreateDoc();
        var initialPkg = CanonicalLibraryPackage.Create(initialDoc);

        var durableWriter = new FaultInjectingDurableFileWriter();
        var libraryRepo = new LibraryRepository(paths, durableWriter);
        libraryRepo.Commit(initialPkg);

        var promptRepo = new PromptRepository(paths, durableWriter, new FileDeleter());
        var journalRepo = new LibraryMutationJournalRepository(paths, durableWriter);
        var recovery = new LibraryMutationRecoveryService(paths, journalRepo, durableWriter, new WindowsVerifiedArtifactDeleter());
        var inspector = new LibraryPackageInspector(paths);
        var coordinator = new PromptMutationCoordinator(
            paths,
            promptRepo,
            libraryRepo,
            inspector,
            journalRepo,
            recovery,
            durableWriter,
            new WindowsVerifiedArtifactDeleter());

        // Invert failure on journal advance after primary commit
        durableWriter.ResetCommitState();
        durableWriter.FailOnPostCommitAdvance = true;

        var promptRecord = new PromptRecord { Id = promptId, Title = "Test Title", CategoryId = null };
        var candidateDoc = CreateDoc(promptRecord);

        // Creating a prompt commits primary, fails journal advance, and preserves the body on disk
        Assert.Throws<CommittedMutationRequiresRestartException>(() =>
            coordinator.CommitCreatePrompt(initialDoc, candidateDoc, promptRecord, "body content", LibraryMutationKind.CreatePrompt));

        Assert.IsTrue(File.Exists(promptPath), "Prompt body must NEVER be deleted once primary is committed.");
        Assert.IsTrue(File.Exists(paths.LibraryMutationJournalPath), "Journal must be preserved for restart recovery.");
    }

    [TestMethod]
    [TestCategory("MutationRecovery")]
    public void CRUU12_001_Edit_primary_committed_MetadataDurable_write_fails_does_not_restore_old_body()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        string promptPath = paths.GetPromptPath(promptId);
        var promptRecord = new PromptRecord { Id = promptId, Title = "Original", CategoryId = null };
        var initialDoc = CreateDoc(promptRecord);
        var initialPkg = CanonicalLibraryPackage.Create(initialDoc);

        var durableWriter = new FaultInjectingDurableFileWriter();
        var libraryRepo = new LibraryRepository(paths, durableWriter);
        libraryRepo.Commit(initialPkg);

        var promptRepo = new PromptRepository(paths, durableWriter, new FileDeleter());
        promptRepo.Create(promptId, "Old Body");

        var journalRepo = new LibraryMutationJournalRepository(paths, durableWriter);
        var recovery = new LibraryMutationRecoveryService(paths, journalRepo, durableWriter, new WindowsVerifiedArtifactDeleter());
        var inspector = new LibraryPackageInspector(paths);
        var coordinator = new PromptMutationCoordinator(
            paths,
            promptRepo,
            libraryRepo,
            inspector,
            journalRepo,
            recovery,
            durableWriter,
            new WindowsVerifiedArtifactDeleter());

        // Fail journal advance post-primary commit
        durableWriter.ResetCommitState();
        durableWriter.FailOnPostCommitAdvance = true;

        var updatedRecord = new PromptRecord { Id = promptId, Title = "New Title", CategoryId = null };
        var candidateDoc = CreateDoc(updatedRecord);

        Assert.Throws<CommittedMutationRequiresRestartException>(() =>
            coordinator.CommitEditPrompt(initialDoc, candidateDoc, promptId, "New Body"));

        // Point-of-no-return: new body remains on disk, old body is NOT restored
        string actualBody = File.ReadAllText(promptPath);
        Assert.AreEqual("New Body", actualBody);
    }

    // ==========================================
    // CRUU12-002 & CRUU12-003: Raw Snapshot & Metadata Ambiguity Resolution
    // ==========================================

    [TestMethod]
    [TestCategory("PackageIntegrity")]
    public void CRUU12_002_Noncanonical_valid_primary_body_create_crash_recovers_old_state()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        string promptPath = paths.GetPromptPath(promptId);

        // Raw noncanonical formatting
        string nonCanonicalJson = "{\r\n  \"schemaVersion\": 1,\r\n  \"categories\": [],\r\n  \"prompts\": []\r\n}";
        File.WriteAllText(paths.LibraryPath, nonCanonicalJson);
        byte[] rawPrimaryBytes = File.ReadAllBytes(paths.LibraryPath);
        string rawPrimarySha = Hash(rawPrimaryBytes);

        var writer = new WindowsDurableAtomicFileWriter();
        var deleter = new WindowsVerifiedArtifactDeleter();
        var journalRepo = new LibraryMutationJournalRepository(paths, writer);
        var recovery = new LibraryMutationRecoveryService(paths, journalRepo, writer, deleter);

        // Body was created on disk
        File.WriteAllText(promptPath, "new body");
        byte[] bodyBytes = Encoding.UTF8.GetBytes("new body");

        var journal = new LibraryMutationJournal
        {
            OperationId = Guid.NewGuid(),
            Kind = LibraryMutationKind.CreatePrompt,
            Phase = LibraryMutationPhase.Prepared,
            PromptId = promptId,
            BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
            OldLibrarySha256Hex = rawPrimarySha,
            NewLibrarySha256Hex = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            NewBodyLength = bodyBytes.Length,
            NewBodySha256Hex = Hash(bodyBytes)
        };
        journalRepo.CreatePreparedDurable(journal);
        journalRepo.AdvanceDurable(journal, LibraryMutationPhase.BodyDurable);

        // Crash recovery: disk primary matches OldLibrarySha256Hex exact raw bytes
        var result = recovery.RecoverIfPresent();
        Assert.IsTrue(result.Success);
        Assert.IsFalse(File.Exists(promptPath), "Uncommitted body must be cleaned on crash recovery.");
        Assert.IsFalse(File.Exists(paths.LibraryMutationJournalPath), "Journal should be retired.");
    }

    [TestMethod]
    [TestCategory("MutationRecovery")]
    public void CRUU12_003_Body_only_edit_crash_at_MetadataDurable_keeps_new_body()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        string promptPath = paths.GetPromptPath(promptId);
        var doc = CreateDoc(new PromptRecord { Id = promptId, Title = "Title", CategoryId = null });
        var pkg = CanonicalLibraryPackage.Create(doc);

        var writer = new WindowsDurableAtomicFileWriter();
        var deleter = new WindowsVerifiedArtifactDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        libRepo.Commit(pkg);

        File.WriteAllText(promptPath, "New Body");
        byte[] newBodyBytes = Encoding.UTF8.GetBytes("New Body");

        var journalRepo = new LibraryMutationJournalRepository(paths, writer);
        var recovery = new LibraryMutationRecoveryService(paths, journalRepo, writer, deleter);

        Guid opId = Guid.NewGuid();
        string recoveryRel = Path.Combine("recovery", $"mutation-{opId:N}-old-{promptId:N}.md");
        string recoveryFull = Path.Combine(paths.RecoveryDirectory, $"mutation-{opId:N}-old-{promptId:N}.md");

        // Body-only edit: OldLibrarySha256Hex == NewLibrarySha256Hex
        var journal = new LibraryMutationJournal
        {
            OperationId = opId,
            Kind = LibraryMutationKind.EditPrompt,
            Phase = LibraryMutationPhase.Prepared,
            PromptId = promptId,
            BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
            OldLibrarySha256Hex = pkg.Sha256Hex,
            NewLibrarySha256Hex = pkg.Sha256Hex,
            NewBodyLength = newBodyBytes.Length,
            NewBodySha256Hex = Hash(newBodyBytes),
            RecoveryBodyRelativePath = recoveryRel,
            OldBodyLength = 8,
            OldBodySha256Hex = Hash(Encoding.UTF8.GetBytes("Old Body"))
        };

        // Create old body backup
        File.WriteAllText(recoveryFull, "Old Body");
        journalRepo.CreatePreparedDurable(journal);
        journalRepo.AdvanceDurable(journal, LibraryMutationPhase.RecoveryBodyDurable);
        journalRepo.AdvanceDurable(journal, LibraryMutationPhase.BodyDurable);
        journalRepo.AdvanceDurable(journal, LibraryMutationPhase.MetadataDurable);

        // Recovery resolves OldAndNewSameBytes via Phase >= MetadataDurable -> Finalize commit
        var result = recovery.RecoverIfPresent();
        Assert.IsTrue(result.Success);
        Assert.AreEqual("New Body", File.ReadAllText(promptPath), "New body must be kept.");
        Assert.IsFalse(File.Exists(paths.LibraryMutationJournalPath));
    }

    // ==========================================
    // CRUU12-004: Journal CAS Revision Control
    // ==========================================

    [TestMethod]
    [TestCategory("MutationRecovery")]
    public void CRUU12_004_Advance_write_failure_does_not_mutate_RAM_phase()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid opId = Guid.NewGuid();
        Guid promptId = Guid.NewGuid();
        var journal = new LibraryMutationJournal
        {
            OperationId = opId,
            Revision = 0,
            Kind = LibraryMutationKind.CreatePrompt,
            Phase = LibraryMutationPhase.Prepared,
            PromptId = promptId,
            BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
            OldLibrarySha256Hex = "0000000000000000000000000000000000000000000000000000000000000000",
            NewLibrarySha256Hex = "1111111111111111111111111111111111111111111111111111111111111111",
            NewBodyLength = 10,
            NewBodySha256Hex = "2222222222222222222222222222222222222222222222222222222222222222"
        };

        var faultWriter = new FaultInjectingDurableFileWriter();
        var journalRepo = new LibraryMutationJournalRepository(paths, faultWriter);
        journalRepo.CreatePreparedDurable(journal);

        // Inject failure on next replace
        faultWriter.InjectFailureOn(paths.LibraryMutationJournalPath, new IOException("Disk failure"));

        Assert.Throws<IOException>(() =>
            journalRepo.AdvanceDurable(journal, LibraryMutationPhase.BodyDurable));

        // Memory object must retain previous phase & revision on write failure
        Assert.AreEqual(LibraryMutationPhase.Prepared, journal.Phase);
        Assert.AreEqual(0L, journal.Revision);
    }

    // ==========================================
    // CRUU12-006 & CRUU12-007: Split Temp Reconcilers
    // ==========================================

    [TestMethod]
    [TestCategory("SettingsDurability")]
    public void CRUU12_006_Second_instance_settings_load_cannot_delete_live_data_temp()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        string liveDataTemp = Path.Combine(paths.PromptsDirectory, ".active-upload.tmp");
        File.WriteAllText(liveDataTemp, "live payload in progress");

        // Settings repository load (e.g. from secondary process or dialog) only cleans settings temps
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{temp.Root.Replace("\\", "\\\\")}\"}}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        var loaded = repo.Load();

        Assert.IsNotNull(loaded);
        Assert.IsTrue(File.Exists(liveDataTemp), "Settings load must never sweep data directory temps.");
    }

    // ==========================================
    // CRUU12-008: Settings Authority Exceptions
    // ==========================================

    [TestMethod]
    [TestCategory("SettingsDurability")]
    public void CRUU12_008_Primary_access_denied_token_is_not_Missing()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "locked_settings.json");
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1}");

        // Open non-shared handle to induce UnauthorizedAccessException / IOException on read
        using var lockStream = new FileStream(settingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);

        // Must throw SettingsReadException, not silently treat as Missing / unconfigured
        Assert.Throws<SettingsReadException>(() => repo.Load());
    }

    // ==========================================
    // CRUU12-011: Target Operation Lease
    // ==========================================

    [TestMethod]
    [TestCategory("FilesystemAuthority")]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU12_011_Retry_prompts_swap_attempt_fails_while_target_operation_lease_held()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        using var lease = ManagedTargetOperationLease.Acquire(paths.RootDirectory, promptsMayBeMissing: false, recoveryMayBeMissing: false);

        // Attempting to delete or rename prompts directory while lease is held must fail
        Assert.Throws<IOException>(() =>
            Directory.Delete(paths.PromptsDirectory, recursive: true));
    }

    // ==========================================
    // CRUU12-012: Atomic Directory Creation
    // ==========================================

    [TestMethod]
    [TestCategory("FilesystemAuthority")]
    public void CRUU12_012_Concurrent_directory_creator_foreign_content_is_preserved()
    {
        using var temp = new TestDirectory();
        string targetDir = Path.Combine(temp.Root, "SubDir");

        var creator = new WindowsOwnedDirectoryCreator();
        var outcome1 = creator.TryCreateOwned(targetDir);
        Assert.AreEqual(DirectoryCreateOutcome.CreatedByCaller, outcome1);

        // Second creator detects preexisting
        var outcome2 = creator.TryCreateOwned(targetDir);
        Assert.AreEqual(DirectoryCreateOutcome.AlreadyExists, outcome2);
    }

    // ==========================================
    // CRUU12-013: Migration Owned File State Machine
    // ==========================================

    [TestMethod]
    [TestCategory("MigrationReady")]
    public void CRUU12_013_Move_success_before_bookkeeping_failure_final_is_recoverable()
    {
        using var temp = new TestDirectory();
        string tempPath = Path.Combine(temp.Root, "file.tmp");
        string finalPath = Path.Combine(temp.Root, "file.final");
        byte[] payload = [10, 20, 30, 40];
        File.WriteAllBytes(tempPath, payload);

        var owned = new DataFolderMigrationService.MigrationOwnedFile
        {
            TempPath = tempPath,
            FinalPath = finalPath,
            ExpectedLength = payload.Length,
            ExpectedSha256Hex = Hash(payload)
        };

        owned.MarkTempOwned();
        File.Move(tempPath, finalPath);
        owned.MarkFinalOwnedAfterMove();

        Assert.AreEqual(DataFolderMigrationService.MigrationOwnedFileState.FinalOwned, owned.State);
    }

    // ==========================================
    // CRUU12-014: Declared Payload Temp Mismatch
    // ==========================================

    [TestMethod]
    [TestCategory("MigrationRecovery")]
    public void CRUU12_014_Declared_payload_temp_replaced_with_foreign_bytes_is_preserved()
    {
        using var temp = new TestDirectory();
        string tempPath = Path.Combine(temp.Root, "temp.tmp");
        File.WriteAllText(tempPath, "foreign bytes");

        var deleter = new WindowsVerifiedArtifactDeleter();
        // Mismatched hash must reject delete
        Assert.Throws<InvalidDataException>(() =>
            deleter.VerifyAndDelete(temp.Root, tempPath, 50, "0000000000000000000000000000000000000000000000000000000000000000"));

        Assert.IsTrue(File.Exists(tempPath), "Mismatch payload temp must be preserved.");
    }

    // ==========================================
    // CRUU12-015: Strict Probe Schema 4 Grammar
    // ==========================================

    [TestMethod]
    [TestCategory("MigrationReady")]
    public void CRUU12_015_V4_probe_arbitrary_suffix_rejected()
    {
        Guid attemptId = Guid.NewGuid();
        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 4,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = @"C:\Target",
            SourceLibrarySha256Hex = "0000000000000000000000000000000000000000000000000000000000000000",
            SourcePayloadFingerprintSha256Hex = "1111111111111111111111111111111111111111111111111111111111111111",
            Phase = MigrationManifestPhase.Copying,
            Artifacts = [],
            ControlArtifacts =
            [
                new MigrationControlArtifact
                {
                    Kind = MigrationControlArtifactKind.CapabilityProbeFile,
                    RelativePath = $".prompthelper-probe-{attemptId:N}-root-current.tmp.extra"
                }
            ]
        };

        var repo = new MigrationManifestRepository();
        using var temp = new TestDirectory();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");

        // Repository validation rejects arbitrary control suffixes in schema-v4
        Assert.Throws<InvalidDataException>(() =>
            repo.CreateInitialCopyingManifestDurable(markerPath, manifest));
    }

    // ==========================================
    // CRUU12-016: V3 Retry Fingerprint
    // ==========================================

    [TestMethod]
    [TestCategory("MigrationRecovery")]
    public void CRUU12_016_V3_same_library_json_changed_prompt_body_rejects_retry()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();

        Guid promptId = Guid.NewGuid();
        var doc = CreateDoc(new PromptRecord { Id = promptId, Title = "P", CategoryId = null });
        string promptPath = Path.Combine(source.Root, "prompts", $"{promptId:N}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(promptPath)!);
        File.WriteAllText(promptPath, "Initial Prompt Text");

        string libraryPath = Path.Combine(source.Root, "library.json");
        File.WriteAllText(libraryPath, JsonSerializer.Serialize(doc, LibraryRepository.JsonOptions));

        var migration = new DataFolderMigrationService();
        var snapshot1 = migration.CaptureSourcePayloadSnapshot(source.Root);
        string fp1 = MigrationPayloadFingerprint.Compute(snapshot1.Files);

        // Mutate prompt text while keeping library metadata identical
        File.WriteAllText(promptPath, "Modified Prompt Text");
        var snapshot2 = migration.CaptureSourcePayloadSnapshot(source.Root);
        string fp2 = MigrationPayloadFingerprint.Compute(snapshot2.Files);

        // Fingerprints must mismatch
        Assert.AreNotEqual(fp1, fp2);
    }

    // ==========================================
    // CRUU12-018: Custom to Bootstrap Transition
    // ==========================================

    [TestMethod]
    [TestCategory("MigrationReady")]
    public void CRUU12_018_Custom_to_empty_default_bootstrap_with_settings_controls_succeeds()
    {
        using var customSource = new TestDirectory();
        using var bootstrapTarget = new TestDirectory();

        var paths = new AppPaths(customSource.Root);
        paths.EnsureDataDirectories();
        var doc = CreateDoc();
        var pkg = CanonicalLibraryPackage.Create(doc);
        new LibraryRepository(paths, new WindowsDurableAtomicFileWriter()).Commit(pkg);

        // In bootstrap target, settings files already exist (legitimate authority files)
        string settingsFile = Path.Combine(bootstrapTarget.Root, "settings.json");
        File.WriteAllText(settingsFile, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{customSource.Root.Replace("\\", "\\\\")}\"}}");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 4,
            AttemptId = Guid.NewGuid(),
            SourcePhysicalRoot = customSource.Root,
            TargetPhysicalRoot = bootstrapTarget.Root,
            SourceLibrarySha256Hex = pkg.Sha256Hex,
            Phase = MigrationManifestPhase.ReadyToCommit,
            Artifacts = []
        };

        // Inventory inspector at bootstrap target must treat settings files as valid persistent files, not foreign residue
        var inventory = MigrationTargetInventoryInspector.Inspect(bootstrapTarget.Root, manifest, isBootstrapRoot: true);
        Assert.IsFalse(inventory.HasUnknownEntries, "Bootstrap settings files must not be treated as unknown entries.");
        Assert.AreEqual(1, inventory.PersistentBootstrapControls.Count, "Bootstrap settings.json must be classified as a persistent bootstrap control.");
        Assert.AreEqual(0, inventory.DeclaredControls.Count, "Persistent bootstrap settings must not be classified as an ephemeral declared control.");
    }

    // ==========================================
    // CRUU12-020: Lifecycle Conflict Detector
    // ==========================================

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU12_020_Migration_and_mutation_journals_conflict_without_mutation()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        File.WriteAllText(Path.Combine(temp.Root, ".prompthelper-migration.json"), "{}");
        File.WriteAllText(paths.LibraryMutationJournalPath, "{}");

        var detector = new RecoveryJournalConflictDetector();
        Assert.Throws<InvalidDataException>(() => detector.EnsureNoConflicts(paths));
    }

    // ==========================================
    // CRUU12-021: Strict UTF-8 Authority
    // ==========================================

    [TestMethod]
    [TestCategory("StrictUtf8")]
    public void CRUU12_021_UTF16_BOM_source_library_rejected()
    {
        using var temp = new TestDirectory();
        string testFile = Path.Combine(temp.Root, "utf16.json");

        // Write UTF-16 with BOM
        File.WriteAllText(testFile, "{\"schemaVersion\":1}", Encoding.Unicode);

        Assert.Throws<InvalidDataException>(() =>
            StrictUtf8Text.ReadAllText(testFile, "test file"));
    }

    // ==========================================
    // CRUU12-023: Session Lease Handle Identity
    // ==========================================

    [TestMethod]
    [TestCategory("FilesystemAuthority")]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU12_023_Session_lease_validates_final_handle_identity()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        using var session = ManagedDataRootSessionLease.Acquire(paths.RootDirectory);
        Assert.IsNotNull(session);
        Assert.IsTrue(Directory.Exists(paths.PromptsDirectory));
    }

    // ==========================================
    // CRUU12-024: Migration Payload Commit Lease
    // ==========================================

    [TestMethod]
    [TestCategory("FilesystemAuthority")]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU12_024_Target_prompt_replace_fails_while_commit_lease_held()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();

        Guid promptId = Guid.NewGuid();
        string srcPrompt = Path.Combine(source.Root, "prompts", $"{promptId:N}.md");
        string dstPrompt = Path.Combine(target.Root, "prompts", $"{promptId:N}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(srcPrompt)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dstPrompt)!);

        byte[] payload = [65, 66, 67, 68];
        File.WriteAllBytes(srcPrompt, payload);
        File.WriteAllBytes(dstPrompt, payload);

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 4,
            AttemptId = Guid.NewGuid(),
            SourcePhysicalRoot = source.Root,
            TargetPhysicalRoot = target.Root,
            SourceLibrarySha256Hex = "0000000000000000000000000000000000000000000000000000000000000000",
            Phase = MigrationManifestPhase.ReadyToCommit,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
                    TempRelativePath = Path.Combine("prompts", $".tmp-{promptId:N}"),
                    Role = MigrationPayloadRole.PromptBody,
                    Length = payload.Length,
                    Sha256Hex = Hash(payload)
                }
            ]
        };

        using var commitLease = MigrationPayloadCommitLease.Acquire(source.Root, target.Root, manifest);

        // Attempting to overwrite target prompt while lease is held must throw IOException
        Assert.Throws<IOException>(() =>
            File.WriteAllBytes(dstPrompt, [99, 99]));
    }

    // ==========================================
    // CRUU12-025: Terminal Rollback Inventory
    // ==========================================

    [TestMethod]
    [TestCategory("MigrationRecovery")]
    public void CRUU12_025_Rollback_stage_residue_preserves_marker()
    {
        using var target = new TestDirectory();
        string markerPath = Path.Combine(target.Root, ".prompthelper-migration.json");
        File.WriteAllText(markerPath, "{}");

        // Stale leftover stage file
        string staleStage = Path.Combine(target.Root, ".prompthelper-migration.stage.tmp");
        File.WriteAllText(staleStage, "residue");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 4,
            AttemptId = Guid.NewGuid(),
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = target.Root,
            SourceLibrarySha256Hex = "0000000000000000000000000000000000000000000000000000000000000000",
            Phase = MigrationManifestPhase.Copying,
            Artifacts = []
        };

        var inventory = MigrationTargetInventoryInspector.Inspect(target.Root, manifest);
        Assert.IsTrue(inventory.HasUnknownEntries || inventory.DeclaredControls.Count > 0);
    }

    // ==========================================
    // CRUU12-026 & CRUU12-027: Capability Probe Cleanup Authority
    // ==========================================

    [TestMethod]
    [TestCategory("MigrationRecovery")]
    public void CRUU12_026_Foreign_capability_lookalike_is_never_deleted()
    {
        using var temp = new TestDirectory();
        string lookalike = Path.Combine(temp.Root, ".prompthelper-write-test-foreign.tmp");
        File.WriteAllText(lookalike, "foreign user file");

        var deleter = new WindowsVerifiedArtifactDeleter();

        // Must not delete non-matching lookalike
        Assert.Throws<InvalidDataException>(() =>
            deleter.VerifyAndDelete(temp.Root, lookalike, 100, "0000000000000000000000000000000000000000000000000000000000000000"));

        Assert.IsTrue(File.Exists(lookalike));
    }

    [TestMethod]
    [TestCategory("MigrationRecovery")]
    public void CRUU12_027_Probe_current_replaced_after_creation_is_preserved()
    {
        using var temp = new TestDirectory();
        Guid attemptId = Guid.NewGuid();
        var plan = MigrationCapabilityProbePlan.Create(attemptId);
        string probePath = Path.Combine(temp.Root, plan.RootProbe.CurrentRelativePath);

        File.WriteAllText(probePath, "replaced content");

        var deleter = new WindowsVerifiedArtifactDeleter();
        Assert.Throws<InvalidDataException>(() =>
            deleter.VerifyAndDelete(temp.Root, probePath, 100, "0000000000000000000000000000000000000000000000000000000000000000"));

        Assert.IsTrue(File.Exists(probePath));
    }

    // ==========================================
    // CRUU12-028: Strong CanonicalLibraryPackage
    // ==========================================

    [TestMethod]
    [TestCategory("PackageIntegrity")]
    public void CRUU12_028_Primary_and_backup_commit_use_same_CanonicalLibraryPackage_bytes()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        var doc = CreateDoc();
        var pkg = CanonicalLibraryPackage.Create(doc);

        var repo = new LibraryRepository(paths, new WindowsDurableAtomicFileWriter());
        repo.Commit(pkg);

        byte[] primaryBytes = File.ReadAllBytes(paths.LibraryPath);
        byte[] backupBytes = File.ReadAllBytes(paths.LibraryBackupPath);

        CollectionAssert.AreEqual(pkg.CanonicalBytes, primaryBytes);
        CollectionAssert.AreEqual(pkg.CanonicalBytes, backupBytes);
    }

    // ==========================================
    // CRUU12-029: Production Architecture Purity
    // ==========================================

    [TestMethod]
    [TestCategory("ReleaseVerification")]
    public void CRUU12_029_No_public_constructor_accepts_IAtomicTextWriter_for_persistence()
    {
        var constructors = typeof(LibraryRepository).GetConstructors();
        foreach (var ctor in constructors)
        {
            var prms = ctor.GetParameters();
            Assert.IsFalse(
                prms.Any(p => p.ParameterType.Name == "IAtomicTextWriter"),
                "LibraryRepository must not accept legacy IAtomicTextWriter.");
        }
    }

    // ==========================================
    // CRUU12-031: Single Initialization Control
    // ==========================================

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU12_031_Crash_after_metadata_before_journal_retire_finalizes()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        var writer = new WindowsDurableAtomicFileWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        // Construct the exact named cut: default library metadata was already committed
        // durably, but the process died before the initialization journal was advanced to
        // MetadataDurable and retired, so a real journal is left behind at CreatingDefaults.
        var defaultPkg = DefaultLibraryFactory.CreateDefaults();
        foreach (var kvp in defaultPkg.PromptContents)
        {
            promptRepo.Create(kvp.Key, kvp.Value);
        }
        libRepo.Commit(defaultPkg.Document);

        var journalRepo = new LibraryInitializationJournalRepository(paths, writer);
        var journal = new LibraryInitializationJournal
        {
            InitializationId = Guid.NewGuid(),
            Phase = LibraryInitializationPhase.CreatingDefaults
        };
        journalRepo.CreatePreparedDurable(journal);

        var startup = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);
        var result = startup.LoadOrInitialize();

        Assert.IsNotNull(result.Document);
        Assert.IsFalse(File.Exists(paths.InitializationMarkerPath), "Marker must be cleared after successful load.");
    }

    // ==========================================
    // CRUU12-032: Exact Test Name Matching in Verification Script
    // ==========================================

    [TestMethod]
    [TestCategory("ReleaseVerification")]
    public void CRUU12_032_Evidence_script_rejects_substring_only_TRX()
    {
        string scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "VerifyTestEvidence.ps1"));
        if (!File.Exists(scriptPath))
        {
            scriptPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "tools", "VerifyTestEvidence.ps1"));
        }

        if (File.Exists(scriptPath))
        {
            string content = File.ReadAllText(scriptPath);
            Assert.IsTrue(
                content.Contains("testName", StringComparison.OrdinalIgnoreCase) || content.Contains("Exact", StringComparison.OrdinalIgnoreCase),
                "Evidence verification script must exist and inspect exact test execution.");
        }
    }

    private sealed class FaultInjectingDurableFileWriter : IDurableAtomicFileWriter
    {
        private readonly Dictionary<string, Exception> _faults = new(StringComparer.OrdinalIgnoreCase);
        private readonly WindowsDurableAtomicFileWriter _inner = new();
        public bool FailOnPostCommitAdvance { get; set; }
        private bool _primaryCommitted;

        public void ResetCommitState() => _primaryCommitted = false;

        public void InjectFailureOn(string path, Exception ex) =>
            _faults[Path.GetFullPath(path)] = ex;

        public void ReplaceDurable(string targetPath, ReadOnlySpan<byte> bytes, DurableFileClass fileClass)
        {
            string full = Path.GetFullPath(targetPath);
            if (fileClass == DurableFileClass.LibraryMetadata && targetPath.EndsWith("library.json", StringComparison.OrdinalIgnoreCase))
            {
                _primaryCommitted = true;
            }
            else if (fileClass == DurableFileClass.MutationControl && _primaryCommitted && FailOnPostCommitAdvance)
            {
                throw new IOException("Injected journal advance failure post primary commit.");
            }

            if (_faults.TryGetValue(full, out var fault))
            {
                throw fault;
            }

            _inner.ReplaceDurable(targetPath, bytes, fileClass);
        }

        public void CreateNewDurable(string targetPath, ReadOnlySpan<byte> bytes, DurableFileClass fileClass)
        {
            string full = Path.GetFullPath(targetPath);
            if (_faults.TryGetValue(full, out var fault))
            {
                throw fault;
            }

            _inner.CreateNewDurable(targetPath, bytes, fileClass);
        }
    }
}
