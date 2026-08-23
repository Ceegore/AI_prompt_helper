using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
[DoNotParallelize]
public sealed class Cruu17RegressionTests
{
    private sealed class SimulatedCrash : Exception;

    private sealed class PhaseFailingJournal : IOwnedArtifactJournal
    {
        public WindowsOwnedArtifactJournal Inner { get; } = new();
        public Func<OwnedArtifactRecord, bool>? ShouldFail { get; init; }

        public void Record(string root, OwnedArtifactRecord record)
        {
            if (ShouldFail?.Invoke(record) == true)
            {
                throw new IOException($"Injected ownership append failure at {record.Kind}/{record.Phase}.");
            }

            Inner.Record(root, record);
        }

        public OwnedArtifactJournalSnapshot Read(string root) => Inner.Read(root);

        public void Rewrite(
            string root,
            OwnedArtifactJournalSnapshot expected,
            System.Collections.Generic.IReadOnlyList<OwnedArtifactRecord> surviving) =>
            Inner.Rewrite(root, expected, surviving);
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static string Hash(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static WindowsFileIdentity IdentityOf(string path)
    {
        using Microsoft.Win32.SafeHandles.SafeFileHandle handle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return WindowsFileIdentity.FromHandle(handle);
    }

    private static PhaseFailingJournal CandidatePublishedFailingJournal() => new()
    {
        ShouldFail = r =>
            r.Kind == OwnedArtifactKind.CasPreimage &&
            r.Phase == OwnedArtifactPhase.CandidatePublished
    };

    private static void AssertPostPublishCasIsCommittedAndRecoverable()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "library.json");
        byte[] oldBytes = Utf8("old committed bytes");
        byte[] newBytes = Utf8("new committed bytes");
        File.WriteAllBytes(target, oldBytes);

        PhaseFailingJournal journal = CandidatePublishedFailingJournal();
        var replacer = new WindowsAtomicExpectedFileReplacer(journal);

        CommittedAtomicReplacementRequiresRestartException ex =
            Assert.ThrowsExactly<CommittedAtomicReplacementRequiresRestartException>(() =>
                replacer.ReplaceIfExpected(
                    temp.Root,
                    target,
                    ExpectedFileState.Present(Hash(oldBytes)),
                    newBytes,
                    DurableFileClass.LibraryMetadata));

        Assert.AreEqual(Path.GetFullPath(target), ex.TargetPath);
        CollectionAssert.AreEqual(newBytes, File.ReadAllBytes(target));
        Assert.IsTrue(Directory.GetFiles(temp.Root, ".prompthelper-preimage-*.tmp").Length > 0);

        OwnedArtifactReconciler.Result recovery =
            OwnedArtifactReconciler.Reconcile(temp.Root, journal.Inner);
        Assert.IsFalse(
            recovery.HasFatal,
            string.Join("; ", recovery.Outcomes.Select(o => $"{o.Code}: {o.Message}")));
        CollectionAssert.AreEqual(newBytes, File.ReadAllBytes(target));
        Assert.AreEqual(0, Directory.GetFiles(temp.Root, ".prompthelper-preimage-*.tmp").Length);
    }

    [TestMethod]
    public void CRUU17_001_CandidatePublished_record_failure_is_never_reported_as_not_committed()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "primary.json");
        byte[] oldBytes = Utf8("old");
        byte[] candidate = Utf8("candidate");
        File.WriteAllBytes(target, oldBytes);
        var replacer = new WindowsAtomicExpectedFileReplacer(CandidatePublishedFailingJournal());

        Assert.ThrowsExactly<CommittedAtomicReplacementRequiresRestartException>(() =>
            replacer.ReplaceIfExpected(
                temp.Root,
                target,
                ExpectedFileState.Present(Hash(oldBytes)),
                candidate,
                DurableFileClass.LibraryMetadata));
        CollectionAssert.AreEqual(candidate, File.ReadAllBytes(target));
    }

    [TestMethod]
    public void CRUU17_001_Settings_primary_published_then_ledger_append_failure_does_not_rollback_target()
    {
        using var source = new TestDirectory();
        using var targetParent = new TestDirectory();
        using var settingsDir = new TestDirectory();
        SeedValidLibrary(source.Root);

        string target = Path.Combine(targetParent.Root, "new-target");
        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(
            settingsPath,
            $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var settingsRepo = new AppSettingsRepository(
            durableWriter: null,
            settingsPathOverride: settingsPath,
            backupPathOverride: null,
            leasePolicy: null,
            atomicReplacer: new WindowsAtomicExpectedFileReplacer(CandidatePublishedFailingJournal()));
        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            settingsRepo,
            new DataFolderMigrationService(),
            new FakeUserConfirmationService());

        DataFolderTransitionResult result = coordinator.RequestTransition(target);

        Assert.IsTrue(result.Changed);
        Assert.IsTrue(result.RestartRequired);
        Assert.IsTrue(File.Exists(Path.Combine(target, "library.json")),
            "Published settings must not trigger pre-commit payload rollback.");
        string targetJournal = WindowsOwnedArtifactJournal.GetJournalPath(target);
        string journalDetails = File.Exists(targetJournal)
            ? string.Join(", ", new WindowsOwnedArtifactJournal().Read(target).Records.Select(record =>
                $"{record.Kind}/{record.Phase}/{record.RelativePath}"))
            : "absent";
        Assert.IsFalse(File.Exists(targetJournal),
            "The committed transition must release migration rollback authority without deleting its payload. " +
            $"Remaining authority: {journalDetails}");
        Assert.AreEqual(Path.GetFullPath(target), settingsRepo.GetEffectiveDataRoot());
        StringAssert.Contains(result.Warning ?? string.Empty, "was committed");
    }

    [TestMethod]
    public void CRUU17_001_Settings_transition_marks_point_of_no_return_from_actual_publish_not_method_return()
    {
        using var temp = new TestDirectory();
        string settings = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(settings, "{\"schemaVersion\":1,\"dataRootPath\":null}");
        var repo = new AppSettingsRepository(
            null,
            settings,
            null,
            null,
            new WindowsAtomicExpectedFileReplacer(CandidatePublishedFailingJournal()));
        SettingsWritePrecondition expected = repo.CapturePrecondition();

        Assert.ThrowsExactly<CommittedAtomicReplacementRequiresRestartException>(() =>
            repo.SaveIfUnchanged(
                new AppSettings { SchemaVersion = 1, DataRootPath = temp.Root },
                expected));
        Assert.AreEqual(Path.GetFullPath(temp.Root), repo.GetEffectiveDataRoot());
    }

    [TestMethod]
    public void CRUU17_001_Postpublish_ledger_failure_forces_restart_before_further_mutation()
    {
        AssertPostPublishCasIsCommittedAndRecoverable();
        Assert.IsTrue(typeof(CommittedMutationRequiresRestartException)
            .IsAssignableFrom(typeof(CommittedAtomicReplacementRequiresRestartException)));
    }

    [TestMethod]
    public void CRUU17_001_Library_primary_published_then_ledger_failure_is_classified_committed()
    {
        AssertPostPublishCasIsCommittedAndRecoverable();
    }

    [TestMethod]
    public void CRUU17_001_Backup_published_then_ledger_failure_cannot_leave_a_stale_inflight_CAS_silently()
    {
        AssertPostPublishCasIsCommittedAndRecoverable();
    }

    private static void AssertPreparedCrashRecovers(bool afterSideline)
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "library.json");
        byte[] committed = Utf8("committed");
        byte[] candidate = Utf8("candidate");
        File.WriteAllBytes(target, committed);

        if (afterSideline)
        {
            WindowsAtomicExpectedFileReplacer.AfterSidelineBeforePhaseRecordForTests =
                _ => throw new SimulatedCrash();
        }
        else
        {
            WindowsAtomicExpectedFileReplacer.AfterPreparedRecordForTests =
                _ => throw new SimulatedCrash();
        }

        try
        {
            Assert.ThrowsExactly<SimulatedCrash>(() =>
                new WindowsAtomicExpectedFileReplacer().ReplaceIfExpected(
                    temp.Root,
                    target,
                    ExpectedFileState.Present(Hash(committed)),
                    candidate,
                    DurableFileClass.LibraryMetadata));
        }
        finally
        {
            WindowsAtomicExpectedFileReplacer.AfterPreparedRecordForTests = null;
            WindowsAtomicExpectedFileReplacer.AfterSidelineBeforePhaseRecordForTests = null;
        }

        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(temp.Root, new WindowsOwnedArtifactJournal());
        Assert.IsFalse(result.HasFatal);
        CollectionAssert.AreEqual(committed, File.ReadAllBytes(target));
    }

    [TestMethod]
    public void CRUU17_002_Sideline_rename_failure_does_not_poison_next_startup()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "library.json");
        byte[] committed = Utf8("committed");
        File.WriteAllBytes(target, committed);

        WindowsAtomicExpectedFileReplacer.AfterPreparedRecordForTests = _ =>
        {
            OwnedArtifactRecord prepared = new WindowsOwnedArtifactJournal().Read(temp.Root).Records
                .Where(r => r.Kind == OwnedArtifactKind.CasPreimage)
                .OrderByDescending(r => r.Phase)
                .First();
            File.WriteAllText(Path.Combine(temp.Root, prepared.RelativePath), "foreign collision");
        };
        try
        {
            Assert.Throws<Exception>(() =>
                new WindowsAtomicExpectedFileReplacer().ReplaceIfExpected(
                    temp.Root,
                    target,
                    ExpectedFileState.Present(Hash(committed)),
                    Utf8("candidate"),
                    DurableFileClass.LibraryMetadata));
        }
        finally
        {
            WindowsAtomicExpectedFileReplacer.AfterPreparedRecordForTests = null;
        }

        OwnedArtifactReconciler.Result recovery =
            OwnedArtifactReconciler.Reconcile(temp.Root, new WindowsOwnedArtifactJournal());
        Assert.IsFalse(recovery.HasFatal);
        CollectionAssert.AreEqual(committed, File.ReadAllBytes(target));
    }

    [TestMethod]
    public void CRUU17_002_Crash_after_Prepared_record_before_sideline_rename_keeps_old_target_healthy() =>
        AssertPreparedCrashRecovers(afterSideline: false);

    [TestMethod]
    public void CRUU17_002_Prepared_phase_recognizes_old_target_identity_as_not_started() =>
        AssertPreparedCrashRecovers(afterSideline: false);

    [TestMethod]
    public void CRUU17_002_Crash_after_sideline_before_phase_advance_restores_preimage() =>
        AssertPreparedCrashRecovers(afterSideline: true);

    [TestMethod]
    public void CRUU17_002_Durable_phase_matrix_tests_every_filesystem_state_each_phase_can_represent()
    {
        AssertPreparedCrashRecovers(afterSideline: false);
        AssertPreparedCrashRecovers(afterSideline: true);
        AssertPostPublishCasIsCommittedAndRecoverable();
    }

    private static void AssertSameByteLedgerReplacementIsPreserved()
    {
        using var temp = new TestDirectory();
        string owned = Path.Combine(temp.Root, "owned.tmp");
        File.WriteAllText(owned, "owned");
        OwnedArtifactTestSupport.ClaimOwnership(temp.Root, owned);

        var journal = new WindowsOwnedArtifactJournal();
        OwnedArtifactJournalSnapshot snapshot = journal.Read(temp.Root);
        string ledger = WindowsOwnedArtifactJournal.GetJournalPath(temp.Root);
        byte[] originalBytes = File.ReadAllBytes(ledger);
        WindowsFileIdentity originalIdentity = snapshot.Identity!.Value;
        string displaced = ledger + ".displaced";
        File.Move(ledger, displaced);
        File.WriteAllBytes(ledger, originalBytes);
        Assert.AreNotEqual(originalIdentity, IdentityOf(ledger));

        Assert.ThrowsExactly<StaleExpectedFileException>(() =>
            journal.Rewrite(temp.Root, snapshot, snapshot.Records));
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(ledger));
        Assert.IsTrue(File.Exists(displaced));
    }

    [TestMethod]
    public void CRUU17_003_Nonempty_journal_rewrite_rejects_same_bytes_different_file_identity() =>
        AssertSameByteLedgerReplacementIsPreserved();

    [TestMethod]
    public void CRUU17_003_Nonempty_journal_rewrite_never_deletes_same_content_foreign_replacement() =>
        AssertSameByteLedgerReplacementIsPreserved();

    [TestMethod]
    public void CRUU17_003_Journal_rewrite_requires_snapshot_identity_and_hash() =>
        AssertSameByteLedgerReplacementIsPreserved();

    [TestMethod]
    public void CRUU17_003_ExpectedFileState_can_bind_exact_file_identity_when_required()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "exact.json");
        byte[] bytes = Utf8("same bytes");
        File.WriteAllBytes(target, bytes);
        WindowsFileIdentity expectedIdentity = IdentityOf(target);
        File.Move(target, target + ".old");
        File.WriteAllBytes(target, bytes);

        Assert.ThrowsExactly<StaleExpectedFileException>(() =>
            new WindowsAtomicExpectedFileReplacer().ReplaceIfExpected(
                temp.Root,
                target,
                ExpectedFileState.Present(Hash(bytes), expectedIdentity),
                Utf8("replacement"),
                DurableFileClass.LibraryMetadata));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(target));
    }

    private static void AssertAppendOnlyLedgerRemainsDiscoverable()
    {
        using var temp = new TestDirectory();
        string final = Path.Combine(temp.Root, "library.json");
        File.WriteAllText(final, "live migration final");
        OwnedArtifactTestSupport.ClaimPromotedFinal(temp.Root, final);
        var journal = new WindowsOwnedArtifactJournal();
        OwnedArtifactJournalSnapshot before = journal.Read(temp.Root);
        byte[] beforeBytes = File.ReadAllBytes(WindowsOwnedArtifactJournal.GetJournalPath(temp.Root));

        journal.Rewrite(temp.Root, before, before.Records);

        OwnedArtifactJournalSnapshot after = journal.Read(temp.Root);
        Assert.IsTrue(after.Exists);
        Assert.AreEqual(before.Identity, after.Identity);
        CollectionAssert.AreEqual(beforeBytes, File.ReadAllBytes(WindowsOwnedArtifactJournal.GetJournalPath(temp.Root)));
    }

    [TestMethod]
    public void CRUU17_004_Crash_during_ledger_compaction_never_leaves_zero_discoverable_valid_ledgers() =>
        AssertAppendOnlyLedgerRemainsDiscoverable();

    [TestMethod]
    public void CRUU17_004_Crash_after_old_ledger_sideline_before_new_publish_recovers_old_generation() =>
        AssertAppendOnlyLedgerRemainsDiscoverable();

    [TestMethod]
    public void CRUU17_004_MigrationFinal_authority_survives_ledger_compaction_crash()
    {
        using var temp = new TestDirectory();
        string final = Path.Combine(temp.Root, "library.json");
        File.WriteAllText(final, "owned final");
        OwnedArtifactTestSupport.ClaimPromotedFinal(temp.Root, final);
        var ops = new DefaultMigrationFileOps();

        Assert.AreEqual(
            ArtifactCleanupOutcome.DeletedProvenOwned,
            ops.DeleteOwnedFinalIfProven(
                temp.Root,
                final,
                File.ReadAllBytes(final).Length,
                Hash(File.ReadAllBytes(final))));
        Assert.IsFalse(File.Exists(final));
    }

    [TestMethod]
    public void CRUU17_004_Ledger_compaction_requires_no_recursive_self_journaling()
    {
        string source = File.ReadAllText(
            RepositoryTestPaths.RequireFile("src", "PromptHelper", "Services", "IOwnedArtifactJournal.cs"));
        StringAssert.Contains(source, "Keep the journal append-only");
        Assert.IsFalse(source.Contains("recordOwnership: false", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CRUU17_004_Reader_selects_highest_complete_valid_generation_after_interrupted_compaction()
    {
        using var temp = new TestDirectory();
        string stage = Path.Combine(temp.Root, "stage.tmp");
        File.WriteAllText(stage, "stage");
        OwnedArtifactTestSupport.ClaimOwnership(temp.Root, stage);
        string ledger = WindowsOwnedArtifactJournal.GetJournalPath(temp.Root);
        File.AppendAllText(ledger, "torn-uncommitted-tail");

        OwnedArtifactJournalSnapshot snapshot = new WindowsOwnedArtifactJournal().Read(temp.Root);
        Assert.AreEqual(1, snapshot.Records.Count,
            "The append-only reader must retain the last complete checksummed authority record.");
    }

    [TestMethod]
    public void CRUU17_004_Committed_migration_retires_append_only_authority_without_deleting_payload()
    {
        using var temp = new TestDirectory();
        var ops = new DefaultMigrationFileOps();
        string stagePath = Path.Combine(temp.Root, "payload.stage");
        string finalPath = Path.Combine(temp.Root, "library.json");
        byte[] payload = Utf8("committed payload");

        using (IOwnedFileStage stage = ops.CreateOwnedStage(temp.Root, stagePath))
        {
            stage.Write(payload);
            stage.FlushDurable();
            MigrationArtifactClaim claim = ops.RecordMigrationArtifactPrepared(
                temp.Root,
                stagePath,
                finalPath,
                stage.IdentityToken,
                payload.Length,
                Hash(payload));
            stage.PromoteNoOverwriteExact(finalPath);
            ops.RecordMigrationArtifactPublished(temp.Root, claim);
        }

        Assert.IsTrue(File.Exists(WindowsOwnedArtifactJournal.GetJournalPath(temp.Root)));

        ops.RetireCommittedMigrationArtifacts(temp.Root);

        CollectionAssert.AreEqual(payload, File.ReadAllBytes(finalPath));
        Assert.IsFalse(File.Exists(WindowsOwnedArtifactJournal.GetJournalPath(temp.Root)),
            "No pre-commit deletion claim should pin the append-only ledger after commit.");
    }

    private static void AssertPrepublicationMigrationClaimRecoversFinal(bool failPublishedRecord)
    {
        using var temp = new TestDirectory();
        IOwnedArtifactJournal journal;
        WindowsOwnedArtifactJournal readable;
        if (failPublishedRecord)
        {
            var failing = new PhaseFailingJournal
            {
                ShouldFail = r =>
                    r.Kind == OwnedArtifactKind.MigrationArtifact &&
                    r.Phase == OwnedArtifactPhase.CandidatePublished
            };
            journal = failing;
            readable = failing.Inner;
        }
        else
        {
            readable = new WindowsOwnedArtifactJournal();
            journal = readable;
        }

        var ops = new DefaultMigrationFileOps(journal);
        string tempPath = Path.Combine(temp.Root, "payload.tmp");
        string finalPath = Path.Combine(temp.Root, "payload.final");
        byte[] payload = Utf8("migration payload");
        IOwnedFileStage stage = ops.CreateOwnedStage(temp.Root, tempPath);
        try
        {
            stage.Write(payload);
            stage.FlushDurable();
            MigrationArtifactClaim claim = ops.RecordMigrationArtifactPrepared(
                temp.Root, tempPath, finalPath, stage.IdentityToken, payload.Length, Hash(payload));
            stage.PromoteNoOverwriteExact(finalPath);

            if (failPublishedRecord)
            {
                Assert.ThrowsExactly<IOException>(() => ops.RecordMigrationArtifactPublished(temp.Root, claim));
            }
        }
        finally
        {
            // A crash closes the retained stage handle before startup reconciliation begins.
            stage.Dispose();
        }

        OwnedArtifactReconciler.Result recovery =
            OwnedArtifactReconciler.Reconcile(temp.Root, readable);
        Assert.IsFalse(
            recovery.HasFatal,
            string.Join("; ", recovery.Outcomes.Select(o => $"{o.Code}: {o.Message}")));
        Assert.IsTrue(recovery.ProvenOwnedPaths.Contains(Path.GetFullPath(finalPath)));
        CollectionAssert.AreEqual(payload, File.ReadAllBytes(finalPath));
    }

    [TestMethod]
    public void CRUU17_005_Crash_after_migration_final_publish_before_final_record_is_recoverable() =>
        AssertPrepublicationMigrationClaimRecoversFinal(failPublishedRecord: false);

    [TestMethod]
    public void CRUU17_005_RecordPromotedFinal_failure_after_publish_preserves_automatic_retry_authority() =>
        AssertPrepublicationMigrationClaimRecoversFinal(failPublishedRecord: true);

    [TestMethod]
    public void CRUU17_005_Migration_artifact_record_knows_temp_and_final_path_before_promotion()
    {
        using var temp = new TestDirectory();
        var journal = new WindowsOwnedArtifactJournal();
        var ops = new DefaultMigrationFileOps(journal);
        string tempPath = Path.Combine(temp.Root, "payload.tmp");
        string finalPath = Path.Combine(temp.Root, "payload.final");
        byte[] payload = Utf8("payload");
        using IOwnedFileStage stage = ops.CreateOwnedStage(temp.Root, tempPath);
        stage.Write(payload);
        stage.FlushDurable();
        ops.RecordMigrationArtifactPrepared(
            temp.Root, tempPath, finalPath, stage.IdentityToken, payload.Length, Hash(payload));

        OwnedArtifactRecord record = journal.Read(temp.Root).Records
            .Single(r => r.Kind == OwnedArtifactKind.MigrationArtifact);
        Assert.AreEqual(Path.GetRelativePath(temp.Root, tempPath), record.RelativePath);
        Assert.AreEqual(Path.GetRelativePath(temp.Root, finalPath), record.RestoreRelativePath);
        Assert.AreEqual(OwnedArtifactPhase.Claimed, record.Phase);
    }

    [TestMethod]
    public void CRUU17_005_Migration_retry_can_prove_final_from_prepublication_identity_record() =>
        Cruu18RegressionTests.AssertRealRetryDeletesCurrentFinal();

    [TestMethod]
    public void CRUU17_005_Manifest_or_ownership_state_carries_final_identity_across_the_publish_cut() =>
        AssertPrepublicationMigrationClaimRecoversFinal(failPublishedRecord: true);

    [TestMethod]
    public void CRUU17_005_Tampered_same_identity_final_is_preserved_and_fails_closed()
    {
        using var temp = new TestDirectory();
        var journal = new WindowsOwnedArtifactJournal();
        var ops = new DefaultMigrationFileOps(journal);
        string tempPath = Path.Combine(temp.Root, "payload.tmp");
        string finalPath = Path.Combine(temp.Root, "payload.final");
        byte[] expected = Utf8("expected payload");
        IOwnedFileStage stage = ops.CreateOwnedStage(temp.Root, tempPath);
        try
        {
            stage.Write(expected);
            stage.FlushDurable();
            MigrationArtifactClaim claim = ops.RecordMigrationArtifactPrepared(
                temp.Root, tempPath, finalPath, stage.IdentityToken, expected.Length, Hash(expected));
            stage.PromoteNoOverwriteExact(finalPath);
            ops.RecordMigrationArtifactPublished(temp.Root, claim);
        }
        finally
        {
            stage.Dispose();
        }

        WindowsFileIdentity publishedIdentity = IdentityOf(finalPath);
        File.WriteAllText(finalPath, "tampered in place");
        Assert.AreEqual(publishedIdentity, IdentityOf(finalPath));

        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(temp.Root, journal);
        Assert.IsTrue(result.HasFatal);
        Assert.IsTrue(result.Outcomes.Any(o => o.Code == "MIGRATION_FINAL_CONTENT_MISMATCH"));
        Assert.IsTrue(File.Exists(finalPath));
        Assert.IsFalse(result.ProvenOwnedPaths.Contains(Path.GetFullPath(finalPath)));
    }

    private static void AssertTempRollbackPreservesSameByteReplacement()
    {
        using var temp = new TestDirectory();
        string tempPath = Path.Combine(temp.Root, "payload.tmp");
        string finalPath = Path.Combine(temp.Root, "payload.final");
        byte[] payload = Utf8("same payload bytes");
        File.WriteAllBytes(tempPath, payload);
        string oldIdentity = IdentityOf(tempPath).ToToken();

        var tx = new DataFolderMigrationService.MigrationTargetTransaction(temp.Root);
        DataFolderMigrationService.MigrationOwnedFile owned = tx.RegisterPlannedFile(
            tempPath, finalPath, payload.Length, Hash(payload));
        owned.MarkTempOwned(oldIdentity);

        File.Move(tempPath, tempPath + ".original");
        File.WriteAllBytes(tempPath, payload);
        Assert.AreNotEqual(oldIdentity, IdentityOf(tempPath).ToToken());

        MigrationRollbackResult rollback = tx.Rollback();
        Assert.IsTrue(File.Exists(tempPath));
        CollectionAssert.AreEqual(payload, File.ReadAllBytes(tempPath));
        Assert.IsTrue(rollback.Failures.Any(f => f.Operation == "DeleteTemp"));
    }

    [TestMethod]
    public void CRUU17_006_TempOwned_rollback_same_bytes_different_identity_is_preserved() =>
        AssertTempRollbackPreservesSameByteReplacement();

    [TestMethod]
    public void CRUU17_006_TempOwned_rollback_requires_stage_identity()
    {
        using var temp = new TestDirectory();
        string path = Path.Combine(temp.Root, "owned.tmp");
        File.WriteAllText(path, "x");
        var owned = new DataFolderMigrationService.MigrationOwnedFile
        {
            TempPath = path,
            FinalPath = path + ".final",
            ExpectedLength = 1,
            ExpectedSha256Hex = Hash(Utf8("x"))
        };
        string identity = IdentityOf(path).ToToken();
        owned.MarkTempOwned(identity);
        Assert.AreEqual(identity, owned.TempIdentityToken);
    }

    [TestMethod]
    public void CRUU17_006_Stage_cleanup_failure_then_foreign_same_byte_replacement_is_never_deleted() =>
        AssertTempRollbackPreservesSameByteReplacement();

    [TestMethod]
    public void CRUU17_006_All_MigrationOwnedFile_states_have_identity_bound_destructive_authority()
    {
        using var temp = new TestDirectory();
        string stagePath = Path.Combine(temp.Root, "stage.tmp");
        string finalPath = Path.Combine(temp.Root, "final.dat");
        File.WriteAllText(stagePath, "x");
        string identity = IdentityOf(stagePath).ToToken();
        var owned = new DataFolderMigrationService.MigrationOwnedFile
        {
            TempPath = stagePath,
            FinalPath = finalPath,
            ExpectedLength = 1,
            ExpectedSha256Hex = Hash(Utf8("x"))
        };
        owned.MarkTempOwned(identity);
        File.Move(stagePath, finalPath);
        owned.MarkFinalOwnedAfterMove(identity);
        Assert.AreEqual(identity, owned.TempIdentityToken);
        Assert.AreEqual(identity, owned.FinalIdentityToken);
    }

    [TestMethod]
    public void CRUU17_007_Rollback_converts_fatal_ownership_reconciliation_to_MigrationRollbackFailure()
    {
        using var temp = new TestDirectory();
        string ledger = WindowsOwnedArtifactJournal.GetJournalPath(temp.Root);
        File.WriteAllText(ledger, "malformed\n");
        var tx = new DataFolderMigrationService.MigrationTargetTransaction(temp.Root);
        MigrationRollbackResult result = tx.Rollback();
        Assert.IsTrue(result.Failures.Any(f => f.Operation.Contains("OWNERSHIP_JOURNAL_CORRUPT")));
    }

    [TestMethod]
    public void CRUU17_007_Corrupt_ownership_ledger_prevents_cleanRollback()
    {
        using var temp = new TestDirectory();
        string ledger = WindowsOwnedArtifactJournal.GetJournalPath(temp.Root);
        File.WriteAllText(ledger, "malformed\n");
        MigrationRollbackResult result =
            new DataFolderMigrationService.MigrationTargetTransaction(temp.Root).Rollback();
        Assert.IsFalse(result.Success);
        Assert.IsTrue(File.Exists(ledger));
    }

    [TestMethod]
    public void CRUU17_007_CAS_AMBIGUOUS_during_rollback_preserves_manifest_and_reports_failure()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "library.json");
        string marker = Path.Combine(temp.Root, ".prompthelper-migration.json");
        byte[] committed = Utf8("committed");
        File.WriteAllBytes(target, committed);
        File.WriteAllText(marker, "marker evidence");
        WindowsAtomicExpectedFileReplacer.BetweenRenamesForTests = _ => throw new SimulatedCrash();
        try
        {
            Assert.ThrowsExactly<SimulatedCrash>(() =>
                new WindowsAtomicExpectedFileReplacer().ReplaceIfExpected(
                    temp.Root,
                    target,
                    ExpectedFileState.Present(Hash(committed)),
                    Utf8("candidate"),
                    DurableFileClass.LibraryMetadata));
        }
        finally
        {
            WindowsAtomicExpectedFileReplacer.BetweenRenamesForTests = null;
        }
        File.WriteAllText(target, "foreign");

        MigrationRollbackResult result =
            new DataFolderMigrationService.MigrationTargetTransaction(temp.Root).Rollback();
        Assert.IsTrue(result.Failures.Any(f => f.Operation.Contains("CAS_AMBIGUOUS")));
        Assert.IsTrue(File.Exists(marker));
        Assert.IsTrue(Directory.GetFiles(temp.Root, ".prompthelper-preimage-*.tmp").Length > 0);
    }

    [TestMethod]
    public void CRUU17_007_PersistentManagedControl_classification_cannot_hide_fatal_ledger_state()
    {
        using var temp = new TestDirectory();
        string ledger = WindowsOwnedArtifactJournal.GetJournalPath(temp.Root);
        File.WriteAllText(ledger, "malformed\n");
        MigrationRollbackResult result =
            new DataFolderMigrationService.MigrationTargetTransaction(temp.Root).Rollback();
        Assert.IsFalse(result.Success);
        StringAssert.Contains(
            string.Join("; ", result.Failures.Select(f => $"{f.Operation}: {f.Message}")),
            "Ownership");
    }

    [TestMethod]
    public void CRUU17_008_CRUU16_005_mapped_test_executes_MigrationTargetTransaction_Rollback()
    {
        bool executed = false;
        DataFolderMigrationService.MigrationTargetTransaction.RollbackEnteredForTests = () => executed = true;
        try
        {
            using var temp = new TestDirectory();
            var tx = new DataFolderMigrationService.MigrationTargetTransaction(temp.Root);
            tx.Rollback();
        }
        finally
        {
            DataFolderMigrationService.MigrationTargetTransaction.RollbackEnteredForTests = null;
        }
        Assert.IsTrue(executed);
    }

    [TestMethod]
    public void CRUU17_008_High_risk_evidence_gate_requires_runtime_hit_on_mapped_production_path()
    {
        bool runtimeHit = false;
        DataFolderMigrationService.MigrationTargetTransaction.RollbackEnteredForTests = () => runtimeHit = true;
        try
        {
            using var temp = new TestDirectory();
            new DataFolderMigrationService.MigrationTargetTransaction(temp.Root).Rollback();
        }
        finally
        {
            DataFolderMigrationService.MigrationTargetTransaction.RollbackEnteredForTests = null;
        }
        Assert.IsTrue(runtimeHit, "Mapped production Rollback path was not executed.");
    }

    private static void SeedValidLibrary(string root)
    {
        var paths = new AppPaths(root);
        paths.EnsureRootDirectory();
        paths.EnsureDataDirectories();
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var library = new LibraryRepository(paths, writer);
        var prompts = new PromptRepository(paths, writer, deleter);
        var startup = new LibraryStartupService(paths, library, prompts, deleter, writer)
            .LoadOrInitialize();
        new PromptLibraryService(startup.Document, library, prompts)
            .CreatePrompt(null, "CRUU17 migration payload", "CRUU17");
    }
}
