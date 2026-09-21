using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Platform.Linux.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LinuxAtomicExpectedFileReplacerCoverageTests
{
    private sealed class PhaseFailingJournal : IOwnedArtifactJournal
    {
        public LinuxOwnedArtifactJournal Inner { get; } = new();
        public OwnedArtifactPhase? FailPhase { get; init; }

        public void Record(string root, OwnedArtifactRecord record)
        {
            if (record.Phase == FailPhase)
            {
                throw new IOException($"Injected failure at {record.Phase}.");
            }

            Inner.Record(root, record);
        }

        public OwnedArtifactJournalSnapshot Read(string root) => Inner.Read(root);

        public void Rewrite(
            string root,
            OwnedArtifactJournalSnapshot expected,
            IReadOnlyList<OwnedArtifactRecord> surviving) =>
            Inner.Rewrite(root, expected, surviving);
    }

    [TestMethod]
    public void Expected_missing_stage_disappears_before_publish_reports_native_failure()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string target = Path.Combine(root.Path, "new.json");

        LinuxAtomicExpectedFileReplacer.PreSwapBarrierForTests = _ =>
        {
            foreach (string stage in Directory.GetFiles(root.Path, ".prompthelper-tmp-*"))
            {
                File.Delete(stage);
            }
        };

        IOException ex = Assert.Throws<IOException>(() =>
            new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                root.Path,
                target,
                ExpectedFileState.Missing,
                Encoding.UTF8.GetBytes("candidate"),
                DurableFileClass.LibraryMetadata));

        StringAssert.Contains(ex.Message, "Unable to publish expected-missing");
        Assert.IsFalse(File.Exists(target));
    }

    [TestMethod]
    public void Expected_present_missing_target_is_stale()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string target = Path.Combine(root.Path, "missing.json");

        StaleExpectedFileException ex = Assert.Throws<StaleExpectedFileException>(() =>
            new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                root.Path,
                target,
                ExpectedFileState.Present(Hash(Encoding.UTF8.GetBytes("old"))),
                Encoding.UTF8.GetBytes("candidate"),
                DurableFileClass.LibraryMetadata));

        StringAssert.Contains(ex.Message, "expected to exist");
    }

    [TestMethod]
    public void Target_disappearing_before_sideline_is_stale()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string target = Path.Combine(root.Path, "library.json");
        byte[] old = Encoding.UTF8.GetBytes("old");
        File.WriteAllBytes(target, old);

        LinuxAtomicExpectedFileReplacer.PreSwapBarrierForTests = path =>
            File.Delete(path);

        StaleExpectedFileException ex = Assert.Throws<StaleExpectedFileException>(() =>
            new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                root.Path,
                target,
                ExpectedFileState.Present(Hash(old)),
                Encoding.UTF8.GetBytes("candidate"),
                DurableFileClass.LibraryMetadata));

        StringAssert.Contains(ex.Message, "disappeared");
    }

    [TestMethod]
    public void Sidelined_phase_record_failure_restores_previous_target()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string target = Path.Combine(root.Path, "library.json");
        byte[] old = Encoding.UTF8.GetBytes("old");
        File.WriteAllBytes(target, old);

        var journal = new PhaseFailingJournal
        {
            FailPhase = OwnedArtifactPhase.PreimageSidelined
        };

        Assert.Throws<IOException>(() =>
            new LinuxAtomicExpectedFileReplacer(journal).ReplaceIfExpected(
                root.Path,
                target,
                ExpectedFileState.Present(Hash(old)),
                Encoding.UTF8.GetBytes("candidate"),
                DurableFileClass.LibraryMetadata));

        CollectionAssert.AreEqual(old, File.ReadAllBytes(target));
        Assert.AreEqual(0, Directory.GetFiles(root.Path, ".prompthelper-preimage-*").Length);
    }

    [TestMethod]
    public void Old_inode_changed_after_sideline_is_restored_but_operation_fails_stale()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string target = Path.Combine(root.Path, "library.json");
        byte[] old = Encoding.UTF8.GetBytes("old");
        File.WriteAllBytes(target, old);

        LinuxAtomicExpectedFileReplacer.BetweenRenamesForTests = _ =>
        {
            string preimage = Directory.GetFiles(root.Path, ".prompthelper-preimage-*").Single();
            File.AppendAllText(preimage, "-changed");
        };

        Assert.Throws<StaleExpectedFileException>(() =>
            new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                root.Path,
                target,
                ExpectedFileState.Present(Hash(old)),
                Encoding.UTF8.GetBytes("candidate"),
                DurableFileClass.LibraryMetadata));

        Assert.AreEqual("old-changed", File.ReadAllText(target));
        Assert.AreEqual(0, Directory.GetFiles(root.Path, ".prompthelper-preimage-*").Length);
    }

    [TestMethod]
    public void Missing_stage_at_promotion_restores_previous_target()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string target = Path.Combine(root.Path, "library.json");
        byte[] old = Encoding.UTF8.GetBytes("old");
        File.WriteAllBytes(target, old);

        LinuxAtomicExpectedFileReplacer.BeforeCandidatePromotionForTests = _ =>
        {
            foreach (string stage in Directory.GetFiles(root.Path, ".prompthelper-tmp-*"))
            {
                File.Delete(stage);
            }
        };

        StaleExpectedFileException ex = Assert.Throws<StaleExpectedFileException>(() =>
            new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                root.Path,
                target,
                ExpectedFileState.Present(Hash(old)),
                Encoding.UTF8.GetBytes("candidate"),
                DurableFileClass.LibraryMetadata));

        StringAssert.Contains(ex.Message, "previous content was restored");
        CollectionAssert.AreEqual(old, File.ReadAllBytes(target));
    }

    [TestMethod]
    public void Foreign_target_at_promotion_is_preserved_and_preimage_remains()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string target = Path.Combine(root.Path, "library.json");
        byte[] old = Encoding.UTF8.GetBytes("old");
        byte[] foreign = Encoding.UTF8.GetBytes("foreign");
        File.WriteAllBytes(target, old);

        LinuxAtomicExpectedFileReplacer.BeforeCandidatePromotionForTests = path =>
            File.WriteAllBytes(path, foreign);

        StaleExpectedFileException ex = Assert.Throws<StaleExpectedFileException>(() =>
            new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                root.Path,
                target,
                ExpectedFileState.Present(Hash(old)),
                Encoding.UTF8.GetBytes("candidate"),
                DurableFileClass.LibraryMetadata));

        StringAssert.Contains(ex.Message, "concurrent target was preserved");
        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(target));
        string preimage = Directory.GetFiles(root.Path, ".prompthelper-preimage-*").Single();
        CollectionAssert.AreEqual(old, File.ReadAllBytes(preimage));
    }

    [TestMethod]
    public void Candidate_published_record_failure_requires_restart_and_preserves_commit()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string target = Path.Combine(root.Path, "library.json");
        byte[] old = Encoding.UTF8.GetBytes("old");
        byte[] candidate = Encoding.UTF8.GetBytes("candidate");
        File.WriteAllBytes(target, old);

        var journal = new PhaseFailingJournal
        {
            FailPhase = OwnedArtifactPhase.CandidatePublished
        };

        CommittedAtomicReplacementRequiresRestartException ex =
            Assert.Throws<CommittedAtomicReplacementRequiresRestartException>(() =>
                new LinuxAtomicExpectedFileReplacer(journal).ReplaceIfExpected(
                    root.Path,
                    target,
                    ExpectedFileState.Present(Hash(old)),
                    candidate,
                    DurableFileClass.LibraryMetadata));

        StringAssert.Contains(ex.Message, "committed");
        CollectionAssert.AreEqual(candidate, File.ReadAllBytes(target));
        Assert.AreEqual(1, Directory.GetFiles(root.Path, ".prompthelper-preimage-*").Length);
    }

    [TestMethod]
    public void Preimage_replaced_during_post_commit_retirement_requires_restart_without_losing_candidate()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string target = Path.Combine(root.Path, "library.json");
        byte[] old = Encoding.UTF8.GetBytes("old");
        byte[] candidate = Encoding.UTF8.GetBytes("candidate");
        byte[] foreign = Encoding.UTF8.GetBytes("foreign-preimage");
        File.WriteAllBytes(target, old);

        LinuxExactFileRetirement.BeforeQuarantineRenameForTests = path =>
        {
            if (Path.GetFileName(path).StartsWith(".prompthelper-preimage-", StringComparison.Ordinal))
            {
                LinuxExactFileRetirement.BeforeQuarantineRenameForTests = null;
                AtomicExternalReplace(path, foreign);
            }
        };

        CommittedAtomicReplacementRequiresRestartException ex =
            Assert.Throws<CommittedAtomicReplacementRequiresRestartException>(() =>
                new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                    root.Path,
                    target,
                    ExpectedFileState.Present(Hash(old)),
                    candidate,
                    DurableFileClass.LibraryMetadata));

        StringAssert.Contains(ex.Message, "committed");
        CollectionAssert.AreEqual(candidate, File.ReadAllBytes(target));
        string preimage = Directory.GetFiles(root.Path, ".prompthelper-preimage-*").Single();
        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(preimage));
    }

    [TestMethod]
    public void Sidelined_preimage_disappearing_causes_fail_closed_preservation_error()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string target = Path.Combine(root.Path, "library.json");
        byte[] old = Encoding.UTF8.GetBytes("old");
        File.WriteAllBytes(target, old);

        LinuxAtomicExpectedFileReplacer.AfterSidelineBeforePhaseRecordForTests = _ =>
        {
            string preimage = Directory.GetFiles(root.Path, ".prompthelper-preimage-*").Single();
            File.Delete(preimage);
        };

        StaleExpectedFileException ex = Assert.Throws<StaleExpectedFileException>(() =>
            new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                root.Path,
                target,
                ExpectedFileState.Present(Hash(old)),
                Encoding.UTF8.GetBytes("candidate"),
                DurableFileClass.LibraryMetadata));

        StringAssert.Contains(ex.Message, "concurrent change");
        Assert.IsFalse(File.Exists(target));
    }

    [TestMethod]
    public void Sidelined_preimage_identity_replacement_is_never_overwritten()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string target = Path.Combine(root.Path, "library.json");
        byte[] old = Encoding.UTF8.GetBytes("old");
        byte[] foreign = Encoding.UTF8.GetBytes("foreign");
        File.WriteAllBytes(target, old);

        LinuxAtomicExpectedFileReplacer.AfterSidelineBeforePhaseRecordForTests = _ =>
        {
            string preimage = Directory.GetFiles(root.Path, ".prompthelper-preimage-*").Single();
            AtomicExternalReplace(preimage, foreign);
        };

        Assert.Throws<StaleExpectedFileException>(() =>
            new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                root.Path,
                target,
                ExpectedFileState.Present(Hash(old)),
                Encoding.UTF8.GetBytes("candidate"),
                DurableFileClass.LibraryMetadata));

        // The foreign replacement is preserved, whether rollback could reclaim the public
        // pathname or had to leave it at the preimage name.
        bool foreignAtTarget =
            File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(foreign);
        bool foreignAtPreimage =
            Directory.GetFiles(root.Path, ".prompthelper-preimage-*")
                .Any(p => File.ReadAllBytes(p).AsSpan().SequenceEqual(foreign));
        Assert.IsTrue(foreignAtTarget || foreignAtPreimage);
    }

    private static void AtomicExternalReplace(string target, byte[] bytes)
    {
        string scratch = Path.Combine(
            Path.GetDirectoryName(target)!,
            "external-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllBytes(scratch, bytes);
        File.Move(scratch, target, overwrite: true);
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class Root : IDisposable
    {
        public Root()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PromptHelper-LinuxReplacerCoverage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            LinuxAtomicExpectedFileReplacer.PreSwapBarrierForTests = null;
            LinuxAtomicExpectedFileReplacer.AfterPreparedRecordForTests = null;
            LinuxAtomicExpectedFileReplacer.AfterSidelineBeforePhaseRecordForTests = null;
            LinuxAtomicExpectedFileReplacer.BetweenRenamesForTests = null;
            LinuxAtomicExpectedFileReplacer.BeforeCandidatePromotionForTests = null;
            LinuxAtomicExpectedFileReplacer.AfterCandidatePublishBeforePhaseRecordForTests = null;
            LinuxExactFileRetirement.BeforeQuarantineRenameForTests = null;

            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
