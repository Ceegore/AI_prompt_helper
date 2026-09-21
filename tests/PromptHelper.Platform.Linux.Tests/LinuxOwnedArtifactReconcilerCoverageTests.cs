using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using PromptHelper.Services;

namespace PromptHelper.Platform.Linux.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LinuxOwnedArtifactReconcilerCoverageTests
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
    public void Corrupt_journal_is_fatal_and_preserved()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        var journal = new FakeJournal
        {
            ReadFailure = new OwnedArtifactJournalCorruptException("corrupt")
        };

        LinuxOwnedArtifactReconciler.Result result =
            LinuxOwnedArtifactReconciler.Reconcile(root.Path, journal);

        Assert.IsTrue(result.HasFatal);
        Assert.IsTrue(result.Outcomes.Any(o => o.Code == "OWNERSHIP_JOURNAL_CORRUPT"));
        Assert.AreEqual(0, journal.RewriteCalls);
    }

    [TestMethod]
    public void Unreadable_journal_is_fatal_and_preserved()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        var journal = new FakeJournal
        {
            ReadFailure = new IOException("unreadable")
        };

        LinuxOwnedArtifactReconciler.Result result =
            LinuxOwnedArtifactReconciler.Reconcile(root.Path, journal);

        Assert.IsTrue(result.HasFatal);
        Assert.IsTrue(result.Outcomes.Any(o => o.Code == "OWNERSHIP_JOURNAL_UNREADABLE"));
        Assert.AreEqual(0, journal.RewriteCalls);
    }

    [TestMethod]
    public void Existing_empty_journal_is_retired_and_rewrite_failure_is_warning()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        var journal = new FakeJournal
        {
            Snapshot = ExistingSnapshot([]),
            RewriteFailure = new IOException("rewrite blocked")
        };

        LinuxOwnedArtifactReconciler.Result result =
            LinuxOwnedArtifactReconciler.Reconcile(root.Path, journal);

        Assert.IsFalse(result.HasFatal);
        Assert.AreEqual(1, journal.RewriteCalls);
        Assert.IsTrue(result.Outcomes.Any(o =>
            o.Code == "OWNERSHIP_JOURNAL_REWRITE_FAILED" &&
            o.Severity == ReconciliationSeverity.Warning));
    }

    [TestMethod]
    public void Unsupported_linux_ownership_kind_fails_closed()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string artifact = System.IO.Path.Combine(root.Path, "migration.tmp");
        File.WriteAllText(artifact, "keep");
        FileObjectIdentity identity = IdentityOf(artifact);

        var record = new OwnedArtifactRecord(
            Guid.NewGuid(),
            OwnedArtifactKind.MigrationArtifact,
            OwnedArtifactPhase.Prepared,
            "migration.tmp",
            identity);

        var journal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };

        LinuxOwnedArtifactReconciler.Result result =
            LinuxOwnedArtifactReconciler.Reconcile(root.Path, journal);

        Assert.IsTrue(result.HasFatal);
        Assert.IsTrue(result.Outcomes.Any(o =>
            o.Code == "OWNERSHIP_KIND_NOT_IMPLEMENTED_ON_LINUX"));
        Assert.AreEqual("keep", File.ReadAllText(artifact));
        Assert.AreEqual(0, journal.RewriteCalls);
    }

    [TestMethod]
    public void Incomplete_cas_authority_is_fatal()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string old = System.IO.Path.Combine(root.Path, "old.tmp");
        File.WriteAllText(old, "old");
        FileObjectIdentity identity = IdentityOf(old);

        var record = new OwnedArtifactRecord(
            Guid.NewGuid(),
            OwnedArtifactKind.CasPreimage,
            OwnedArtifactPhase.Prepared,
            "old.tmp",
            identity);

        var journal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };

        LinuxOwnedArtifactReconciler.Result result =
            LinuxOwnedArtifactReconciler.Reconcile(root.Path, journal);

        Assert.IsTrue(result.HasFatal);
        Assert.IsTrue(result.Outcomes.Any(o => o.Code == "CAS_AUTHORITY_INCOMPLETE"));
        Assert.AreEqual(0, journal.RewriteCalls);
    }

    [TestMethod]
    public void Candidate_without_preimage_before_commit_is_fatal()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string identitySource = System.IO.Path.Combine(root.Path, "old-source.tmp");
        byte[] oldBytes = Encoding.UTF8.GetBytes("old");
        byte[] candidate = Encoding.UTF8.GetBytes("candidate");
        File.WriteAllBytes(identitySource, oldBytes);
        FileObjectIdentity oldIdentity = IdentityOf(identitySource);
        File.Delete(identitySource);

        string target = System.IO.Path.Combine(root.Path, "library.json");
        File.WriteAllBytes(target, candidate);

        var record = CasRecord(
            oldIdentity,
            OwnedArtifactPhase.PreimageSidelined,
            "missing-preimage.tmp",
            "library.json",
            oldBytes,
            candidate);

        var journal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };

        LinuxOwnedArtifactReconciler.Result result =
            LinuxOwnedArtifactReconciler.Reconcile(root.Path, journal);

        Assert.IsTrue(result.HasFatal);
        Assert.IsTrue(result.Outcomes.Any(o => o.Code == "CAS_PREIMAGE_MISSING"));
        CollectionAssert.AreEqual(candidate, File.ReadAllBytes(target));
    }

    [TestMethod]
    public void Replaced_preimage_is_fatal_and_foreign_object_is_preserved()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        byte[] oldBytes = Encoding.UTF8.GetBytes("old");
        byte[] candidate = Encoding.UTF8.GetBytes("candidate");

        string identitySource = System.IO.Path.Combine(root.Path, "identity.tmp");
        File.WriteAllBytes(identitySource, oldBytes);
        FileObjectIdentity oldIdentity = IdentityOf(identitySource);
        File.Delete(identitySource);

        string preimage = System.IO.Path.Combine(root.Path, "preimage.tmp");
        File.WriteAllText(preimage, "foreign");

        var record = CasRecord(
            oldIdentity,
            OwnedArtifactPhase.PreimageSidelined,
            "preimage.tmp",
            "library.json",
            oldBytes,
            candidate);

        var journal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };

        LinuxOwnedArtifactReconciler.Result result =
            LinuxOwnedArtifactReconciler.Reconcile(root.Path, journal);

        Assert.IsTrue(result.HasFatal);
        Assert.IsTrue(result.Outcomes.Any(o => o.Code == "CAS_PREIMAGE_REPLACED"));
        Assert.AreEqual("foreign", File.ReadAllText(preimage));
    }

    [TestMethod]
    public void Missing_target_and_preimage_are_unrecoverable()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        byte[] oldBytes = Encoding.UTF8.GetBytes("old");
        byte[] candidate = Encoding.UTF8.GetBytes("candidate");

        string identitySource = System.IO.Path.Combine(root.Path, "identity.tmp");
        File.WriteAllBytes(identitySource, oldBytes);
        FileObjectIdentity oldIdentity = IdentityOf(identitySource);
        File.Delete(identitySource);

        var record = CasRecord(
            oldIdentity,
            OwnedArtifactPhase.PreimageSidelined,
            "gone-preimage.tmp",
            "gone-target.json",
            oldBytes,
            candidate);

        var journal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };

        LinuxOwnedArtifactReconciler.Result result =
            LinuxOwnedArtifactReconciler.Reconcile(root.Path, journal);

        Assert.IsTrue(result.HasFatal);
        Assert.IsTrue(result.Outcomes.Any(o => o.Code == "CAS_UNRECOVERABLE"));
    }

    [TestMethod]
    public void Foreign_preimage_after_committed_phase_is_notice_not_fatal()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        byte[] oldBytes = Encoding.UTF8.GetBytes("old");
        byte[] candidate = Encoding.UTF8.GetBytes("candidate");

        string identitySource = System.IO.Path.Combine(root.Path, "identity.tmp");
        File.WriteAllBytes(identitySource, oldBytes);
        FileObjectIdentity oldIdentity = IdentityOf(identitySource);
        File.Delete(identitySource);

        string preimage = System.IO.Path.Combine(root.Path, "preimage.tmp");
        File.WriteAllText(preimage, "foreign");

        var record = CasRecord(
            oldIdentity,
            OwnedArtifactPhase.CandidatePublished,
            "preimage.tmp",
            "library.json",
            oldBytes,
            candidate);

        var journal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };

        LinuxOwnedArtifactReconciler.Result result =
            LinuxOwnedArtifactReconciler.Reconcile(root.Path, journal);

        Assert.IsFalse(result.HasFatal);
        Assert.IsTrue(result.Outcomes.Any(o =>
            o.Code == "CAS_PREIMAGE_REPLACED_AFTER_COMMIT"));
        Assert.AreEqual("foreign", File.ReadAllText(preimage));
        Assert.AreEqual(1, journal.RewriteCalls);
    }

    [TestMethod]
    public void Missing_stage_allows_compaction_and_rewrite_failure_is_reported()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string identitySource = System.IO.Path.Combine(root.Path, "identity.tmp");
        File.WriteAllText(identitySource, "identity");
        FileObjectIdentity identity = IdentityOf(identitySource);
        File.Delete(identitySource);

        var record = new OwnedArtifactRecord(
            Guid.NewGuid(),
            OwnedArtifactKind.Stage,
            OwnedArtifactPhase.Claimed,
            "missing-stage.tmp",
            identity);

        var journal = new FakeJournal
        {
            Snapshot = ExistingSnapshot([record]),
            RewriteFailure = new StaleExpectedFileException("changed")
        };

        LinuxOwnedArtifactReconciler.Result result =
            LinuxOwnedArtifactReconciler.Reconcile(root.Path, journal);

        Assert.IsFalse(result.HasFatal);
        Assert.IsTrue(result.Outcomes.Any(o =>
            o.Code == "OWNERSHIP_JOURNAL_REWRITE_FAILED"));
        Assert.AreEqual(1, journal.RewriteCalls);
    }

    [TestMethod]
    public void Symlink_stage_retirement_failure_is_warning_and_claim_survives()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string real = System.IO.Path.Combine(root.Path, "real.tmp");
        string stage = System.IO.Path.Combine(root.Path, "stage.tmp");
        File.WriteAllText(real, "real");
        FileObjectIdentity identity = IdentityOf(real);
        File.CreateSymbolicLink(stage, real);

        var record = new OwnedArtifactRecord(
            Guid.NewGuid(),
            OwnedArtifactKind.Stage,
            OwnedArtifactPhase.Claimed,
            "stage.tmp",
            identity);

        var journal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };

        LinuxOwnedArtifactReconciler.Result result =
            LinuxOwnedArtifactReconciler.Reconcile(root.Path, journal);

        Assert.IsFalse(result.HasFatal);
        Assert.IsTrue(result.Outcomes.Any(o => o.Code == "STAGE_RETIRE_FAILED"));
        Assert.AreEqual(1, journal.RewriteCalls);
        Assert.IsNotNull(journal.LastSurviving);
        Assert.AreEqual(1, journal.LastSurviving!.Count);
        Assert.AreEqual("real", File.ReadAllText(real));
        Assert.IsNotNull(new FileInfo(stage).LinkTarget);
    }

    [TestMethod]
    public void Unsafe_relative_path_is_rejected()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string source = System.IO.Path.Combine(root.Path, "identity.tmp");
        File.WriteAllText(source, "x");
        FileObjectIdentity identity = IdentityOf(source);

        var record = new OwnedArtifactRecord(
            Guid.NewGuid(),
            OwnedArtifactKind.Stage,
            OwnedArtifactPhase.Claimed,
            "../escape.tmp",
            identity);

        var journal = new FakeJournal { Snapshot = ExistingSnapshot([record]) };

        Assert.Throws<InvalidDataException>(() =>
            LinuxOwnedArtifactReconciler.Reconcile(root.Path, journal));
    }

    private static OwnedArtifactRecord CasRecord(
        FileObjectIdentity identity,
        OwnedArtifactPhase phase,
        string preimage,
        string target,
        byte[] oldBytes,
        byte[] candidate) =>
        new(
            Guid.NewGuid(),
            OwnedArtifactKind.CasPreimage,
            phase,
            preimage,
            identity,
            target,
            Hash(candidate),
            candidate.LongLength,
            MarkerAttemptId: null,
            PreviousSha256Hex: Hash(oldBytes));

    private static OwnedArtifactJournalSnapshot ExistingSnapshot(
        IReadOnlyList<OwnedArtifactRecord> records) =>
        new(
            records,
            new FileObjectIdentity("linux-statx-v1", "ledger:1"),
            "00");

    private static FileObjectIdentity IdentityOf(string path)
    {
        using SafeFileHandle handle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return LinuxFileIdentity.FromHandle(handle).ToObjectIdentity();
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class Root : IDisposable
    {
        public Root()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PromptHelper-LinuxReconcileCoverage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            LinuxExactFileRetirement.BeforeQuarantineRenameForTests = null;
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
