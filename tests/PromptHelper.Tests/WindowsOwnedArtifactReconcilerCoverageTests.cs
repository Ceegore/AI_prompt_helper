using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
[DoNotParallelize]
public sealed class WindowsOwnedArtifactReconcilerCoverageTests
{
    private sealed class FakeJournal : IOwnedArtifactJournal
    {
        public OwnedArtifactJournalSnapshot Snapshot { get; set; } =
            OwnedArtifactJournalSnapshot.Absent;
        public Exception? ReadFailure { get; set; }
        public Exception? RewriteFailure { get; set; }
        public int RewriteCalls { get; private set; }
        public IReadOnlyList<OwnedArtifactRecord>? LastSurviving { get; private set; }

        public void Record(string root, OwnedArtifactRecord record) =>
            throw new NotSupportedException();

        public OwnedArtifactJournalSnapshot Read(string root)
        {
            if (ReadFailure is not null) throw ReadFailure;
            return Snapshot;
        }

        public void Rewrite(
            string root,
            OwnedArtifactJournalSnapshot expected,
            IReadOnlyList<OwnedArtifactRecord> surviving)
        {
            RewriteCalls++;
            LastSurviving = surviving;
            if (RewriteFailure is not null) throw RewriteFailure;
        }
    }

    [TestMethod]
    public void Corrupt_and_unreadable_ledgers_fail_closed_without_compaction()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var root = new Root();

        foreach (Exception failure in new Exception[]
        {
            new OwnedArtifactJournalCorruptException("corrupt"),
            new IOException("unreadable")
        })
        {
            var journal = new FakeJournal { ReadFailure = failure };
            OwnedArtifactReconciler.Result result =
                OwnedArtifactReconciler.Reconcile(root.Path, journal);

            Assert.IsTrue(result.HasFatal);
            Assert.AreEqual(0, journal.RewriteCalls);
            Assert.IsTrue(result.Outcomes.Any(o =>
                o.Code is "OWNERSHIP_JOURNAL_CORRUPT" or "OWNERSHIP_JOURNAL_UNREADABLE"));
        }
    }

    [TestMethod]
    public void Existing_empty_ledger_rewrite_failure_is_warning_not_fatal()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var root = new Root();
        var journal = new FakeJournal
        {
            Snapshot = ExistingSnapshot([]),
            RewriteFailure = new IOException("blocked")
        };

        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(root.Path, journal);

        Assert.IsFalse(result.HasFatal);
        Assert.AreEqual(1, journal.RewriteCalls);
        Assert.IsTrue(result.Outcomes.Any(o =>
            o.Code == "EMPTY_OWNERSHIP_JOURNAL_RETIRE_FAILED" &&
            o.Severity == ReconciliationSeverity.Warning));
    }

    [TestMethod]
    public void Ledger_compaction_failure_after_safe_resolution_is_warning()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var root = new Root();
        string stage = Path.Combine(root.Path, "gone.tmp");
        string identitySource = Path.Combine(root.Path, "identity.tmp");
        File.WriteAllText(identitySource, "id");
        FileObjectIdentity identity = IdentityOf(identitySource);
        File.Delete(identitySource);

        var journal = new FakeJournal
        {
            Snapshot = ExistingSnapshot([
                new OwnedArtifactRecord(
                    Guid.NewGuid(),
                    OwnedArtifactKind.Stage,
                    OwnedArtifactPhase.Claimed,
                    Path.GetFileName(stage),
                    identity)
            ]),
            RewriteFailure = new StaleExpectedFileException("changed")
        };

        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(root.Path, journal);

        Assert.IsFalse(result.HasFatal);
        Assert.IsTrue(result.Outcomes.Any(o =>
            o.Code == "OWNERSHIP_JOURNAL_COMPACTION_FAILED"));
        Assert.AreEqual(1, journal.RewriteCalls);
    }

    [TestMethod]
    public void Candidate_published_retires_exact_preimage_but_preserves_foreign_replacement()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var root = new Root();
        string preimage = Path.Combine(root.Path, "old.tmp");
        File.WriteAllText(preimage, "old");
        FileObjectIdentity owned = IdentityOf(preimage);

        var published = CasRecord(
            owned,
            OwnedArtifactPhase.CandidatePublished,
            "old.tmp",
            "library.json",
            Encoding.UTF8.GetBytes("candidate"));

        var journal = new FakeJournal { Snapshot = ExistingSnapshot([published]) };
        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(root.Path, journal);

        Assert.IsFalse(result.HasFatal);
        Assert.IsFalse(File.Exists(preimage));
        Assert.IsTrue(result.ProvenOwnedPaths.Contains(Path.GetFullPath(preimage)));
        Assert.AreEqual(0, journal.LastSurviving!.Count);

        File.WriteAllText(preimage, "foreign");
        var foreignJournal = new FakeJournal { Snapshot = ExistingSnapshot([published]) };
        OwnedArtifactReconciler.Result foreignResult =
            OwnedArtifactReconciler.Reconcile(root.Path, foreignJournal);

        Assert.IsFalse(foreignResult.HasFatal);
        Assert.AreEqual("foreign", File.ReadAllText(preimage));
        Assert.IsTrue(foreignResult.Outcomes.Any(o => o.Code == "CAS_PREIMAGE_REPLACED"));
    }

    [TestMethod]
    public void Prepared_transaction_with_recorded_old_object_still_at_target_is_already_rolled_back()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var root = new Root();
        string target = Path.Combine(root.Path, "library.json");
        File.WriteAllText(target, "old");
        FileObjectIdentity oldIdentity = IdentityOf(target);

        var record = CasRecord(
            oldIdentity,
            OwnedArtifactPhase.Prepared,
            "missing-preimage.tmp",
            "library.json",
            Encoding.UTF8.GetBytes("candidate"));

        var journal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };
        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(root.Path, journal);

        Assert.IsFalse(result.HasFatal);
        Assert.AreEqual("old", File.ReadAllText(target));
        Assert.AreEqual(0, journal.LastSurviving!.Count);
    }

    [TestMethod]
    public void Missing_preimage_with_matching_candidate_completes_but_foreign_target_fails_closed()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var root = new Root();
        byte[] candidate = Encoding.UTF8.GetBytes("candidate");

        string identitySource = Path.Combine(root.Path, "identity.tmp");
        File.WriteAllText(identitySource, "old");
        FileObjectIdentity oldIdentity = IdentityOf(identitySource);
        File.Delete(identitySource);

        string target = Path.Combine(root.Path, "library.json");
        File.WriteAllBytes(target, candidate);

        var record = CasRecord(
            oldIdentity,
            OwnedArtifactPhase.PreimageSidelined,
            "missing-preimage.tmp",
            "library.json",
            candidate);

        var matchingJournal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };
        OwnedArtifactReconciler.Result matching =
            OwnedArtifactReconciler.Reconcile(root.Path, matchingJournal);

        Assert.IsFalse(matching.HasFatal);
        CollectionAssert.AreEqual(candidate, File.ReadAllBytes(target));
        Assert.AreEqual(0, matchingJournal.LastSurviving!.Count);

        File.WriteAllText(target, "foreign");
        var foreignJournal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };
        OwnedArtifactReconciler.Result foreign =
            OwnedArtifactReconciler.Reconcile(root.Path, foreignJournal);

        Assert.IsTrue(foreign.HasFatal);
        Assert.IsTrue(foreign.Outcomes.Any(o => o.Code == "CAS_AMBIGUOUS"));
        Assert.AreEqual("foreign", File.ReadAllText(target));
        Assert.AreEqual(1, foreignJournal.LastSurviving!.Count);
    }

    [TestMethod]
    public void Exact_preimage_restores_into_missing_target_and_ambiguous_foreign_target_is_preserved()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var root = new Root();
        byte[] candidate = Encoding.UTF8.GetBytes("candidate");
        string preimage = Path.Combine(root.Path, "old.tmp");
        string target = Path.Combine(root.Path, "library.json");
        File.WriteAllText(preimage, "old");
        FileObjectIdentity oldIdentity = IdentityOf(preimage);

        var record = CasRecord(
            oldIdentity,
            OwnedArtifactPhase.PreimageSidelined,
            "old.tmp",
            "library.json",
            candidate);

        var restoreJournal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };
        OwnedArtifactReconciler.Result restored =
            OwnedArtifactReconciler.Reconcile(root.Path, restoreJournal);

        Assert.IsFalse(restored.HasFatal);
        Assert.AreEqual("old", File.ReadAllText(target));
        Assert.IsFalse(File.Exists(preimage));
        Assert.IsTrue(restored.Outcomes.Any(o => o.Code == "CAS_PREIMAGE_RESTORED"));

        File.Move(target, preimage);
        File.WriteAllText(target, "foreign");

        var ambiguousJournal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };
        OwnedArtifactReconciler.Result ambiguous =
            OwnedArtifactReconciler.Reconcile(root.Path, ambiguousJournal);

        Assert.IsTrue(ambiguous.HasFatal);
        Assert.IsTrue(ambiguous.Outcomes.Any(o => o.Code == "CAS_AMBIGUOUS"));
        Assert.AreEqual("old", File.ReadAllText(preimage));
        Assert.AreEqual("foreign", File.ReadAllText(target));
    }

    [TestMethod]
    public void Exact_stage_is_retired_and_foreign_stage_replacement_is_preserved()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var root = new Root();
        string stage = Path.Combine(root.Path, "stage.tmp");
        File.WriteAllText(stage, "owned");
        FileObjectIdentity identity = IdentityOf(stage);

        var record = new OwnedArtifactRecord(
            Guid.NewGuid(),
            OwnedArtifactKind.Stage,
            OwnedArtifactPhase.Claimed,
            "stage.tmp",
            identity);

        var journal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };
        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(root.Path, journal);

        Assert.IsFalse(result.HasFatal);
        Assert.IsFalse(File.Exists(stage));
        Assert.IsTrue(result.ProvenOwnedPaths.Contains(Path.GetFullPath(stage)));

        File.WriteAllText(stage, "foreign");
        var foreignJournal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };
        OwnedArtifactReconciler.Result foreign =
            OwnedArtifactReconciler.Reconcile(root.Path, foreignJournal);

        Assert.IsFalse(foreign.HasFatal);
        Assert.AreEqual("foreign", File.ReadAllText(stage));
        Assert.AreEqual(0, foreignJournal.LastSurviving!.Count);
    }

    [TestMethod]
    public void Migration_final_claim_is_retained_before_commit_and_retired_after_commit_without_deleting_data()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var root = new Root();
        string final = Path.Combine(root.Path, "prompt.md");
        File.WriteAllText(final, "live");
        FileObjectIdentity identity = IdentityOf(final);

        var record = new OwnedArtifactRecord(
            Guid.NewGuid(),
            OwnedArtifactKind.MigrationFinal,
            OwnedArtifactPhase.CandidatePublished,
            "prompt.md",
            identity);

        var beforeCommitJournal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };
        OwnedArtifactReconciler.Result beforeCommit =
            OwnedArtifactReconciler.Reconcile(root.Path, beforeCommitJournal);

        Assert.IsFalse(beforeCommit.HasFatal);
        Assert.IsTrue(File.Exists(final));
        Assert.AreEqual(1, beforeCommitJournal.LastSurviving!.Count);

        var committedJournal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };
        OwnedArtifactReconciler.Result committed =
            OwnedArtifactReconciler.Reconcile(
                root.Path,
                committedJournal,
                retireCommittedMigrationArtifacts: true);

        Assert.IsFalse(committed.HasFatal);
        Assert.AreEqual("live", File.ReadAllText(final));
        Assert.AreEqual(0, committedJournal.LastSurviving!.Count);
    }

    [TestMethod]
    public void Migration_artifact_tracks_exact_published_content_and_fails_closed_on_tamper()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var root = new Root();
        byte[] bytes = Encoding.UTF8.GetBytes("migrated");
        string temp = Path.Combine(root.Path, "stage.tmp");
        string final = Path.Combine(root.Path, "final.md");
        File.WriteAllBytes(final, bytes);
        FileObjectIdentity identity = IdentityOf(final);

        var record = new OwnedArtifactRecord(
            Guid.NewGuid(),
            OwnedArtifactKind.MigrationArtifact,
            OwnedArtifactPhase.CandidatePublished,
            "stage.tmp",
            identity,
            "final.md",
            Hash(bytes),
            bytes.LongLength);

        var journal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };
        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(root.Path, journal);

        Assert.IsFalse(result.HasFatal);
        Assert.IsTrue(result.ProvenOwnedPaths.Contains(Path.GetFullPath(final)));
        Assert.AreEqual(1, journal.LastSurviving!.Count);

        File.WriteAllText(final, "tampered");
        var tamperedJournal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };
        OwnedArtifactReconciler.Result tampered =
            OwnedArtifactReconciler.Reconcile(root.Path, tamperedJournal);

        Assert.IsFalse(tampered.HasFatal);
        Assert.AreEqual("tampered", File.ReadAllText(final));
        Assert.AreEqual(0, tamperedJournal.LastSurviving!.Count);
    }

    [TestMethod]
    public void Capability_probe_claim_is_retired_and_durable_content_mismatch_is_fatal()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var root = new Root();
        string probe = Path.Combine(root.Path, "probe.tmp");
        byte[] bytes = Encoding.UTF8.GetBytes("probe");
        File.WriteAllBytes(probe, bytes);
        FileObjectIdentity identity = IdentityOf(probe);

        var claimed = new OwnedArtifactRecord(
            Guid.NewGuid(),
            OwnedArtifactKind.CapabilityProbe,
            OwnedArtifactPhase.ProbeCreatedClaimed,
            "probe.tmp",
            identity);

        var claimedJournal = new FakeJournal { Snapshot = ExistingSnapshot([claimed]) };
        OwnedArtifactReconciler.Result claimedResult =
            OwnedArtifactReconciler.Reconcile(root.Path, claimedJournal);

        Assert.IsFalse(claimedResult.HasFatal);
        Assert.IsFalse(File.Exists(probe));

        File.WriteAllText(probe, "different");
        FileObjectIdentity durableIdentity = IdentityOf(probe);
        var durable = new OwnedArtifactRecord(
            Guid.NewGuid(),
            OwnedArtifactKind.CapabilityProbe,
            OwnedArtifactPhase.ProbeContentDurable,
            "probe.tmp",
            durableIdentity,
            CandidateSha256Hex: Hash(bytes),
            CandidateLength: bytes.LongLength);

        var durableJournal = new FakeJournal { Snapshot = ExistingSnapshot([durable]) };
        OwnedArtifactReconciler.Result durableResult =
            OwnedArtifactReconciler.Reconcile(root.Path, durableJournal);

        Assert.IsTrue(durableResult.HasFatal);
        Assert.IsTrue(durableResult.Outcomes.Any(o =>
            o.Code == "CAPABILITY_PROBE_DURABLE_CONTENT_MISMATCH"));
        Assert.AreEqual("different", File.ReadAllText(probe));
        Assert.AreEqual(1, durableJournal.LastSurviving!.Count);
    }

    private static OwnedArtifactRecord CasRecord(
        FileObjectIdentity identity,
        OwnedArtifactPhase phase,
        string preimage,
        string target,
        byte[] candidate) =>
        new(
            Guid.NewGuid(),
            OwnedArtifactKind.CasPreimage,
            phase,
            preimage,
            identity,
            target,
            Hash(candidate),
            candidate.LongLength);

    private static OwnedArtifactJournalSnapshot ExistingSnapshot(
        IReadOnlyList<OwnedArtifactRecord> records) =>
        new(
            records,
            new FileObjectIdentity(
                "windows-file-id-v1",
                "00000000:0000000000000001:0000000000000001"),
            "00");

    private static FileObjectIdentity IdentityOf(string path)
    {
        using SafeFileHandle handle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return WindowsFileIdentity.FromHandle(handle).ToObjectIdentity();
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class Root : IDisposable
    {
        public Root()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PromptHelper-WindowsReconcileCoverage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
