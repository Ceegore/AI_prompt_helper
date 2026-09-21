using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using PromptHelper.Services;

namespace PromptHelper.Platform.Linux.Tests;

[TestClass]
public sealed class LinuxCasRecoveryTests
{
    private sealed class SimulatedCrash : Exception;

    [TestMethod]
    public void Crash_after_prepared_before_sideline_leaves_original_and_retires_stage()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        string target = Path.Combine(root, "library.json");
        byte[] original = Encoding.UTF8.GetBytes("original");
        File.WriteAllBytes(target, original);

        try
        {
            LinuxAtomicExpectedFileReplacer.AfterPreparedRecordForTests =
                _ => throw new SimulatedCrash();

            Assert.Throws<SimulatedCrash>(() =>
                Replace(root, target, original, "candidate"));

            LinuxOwnedArtifactReconciler.Result result = Reconcile(root);

            Assert.IsFalse(result.HasFatal, Describe(result));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(target));
            Assert.AreEqual(0, Directory.GetFiles(root, ".prompthelper-preimage-*").Length);
            Assert.AreEqual(0, Directory.GetFiles(root, ".prompthelper-tmp-*").Length);
            AssertJournalSettled(root);
        }
        finally
        {
            ResetHooks();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Crash_after_sideline_before_phase_record_restores_previous_content()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        string target = Path.Combine(root, "library.json");
        byte[] original = Encoding.UTF8.GetBytes("original");
        File.WriteAllBytes(target, original);

        try
        {
            LinuxAtomicExpectedFileReplacer.AfterSidelineBeforePhaseRecordForTests =
                _ => throw new SimulatedCrash();

            Assert.Throws<SimulatedCrash>(() =>
                Replace(root, target, original, "candidate"));

            Assert.IsFalse(File.Exists(target));

            LinuxOwnedArtifactReconciler.Result result = Reconcile(root);

            Assert.IsFalse(result.HasFatal, Describe(result));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(target));
            Assert.AreEqual(0, Directory.GetFiles(root, ".prompthelper-preimage-*").Length);
            AssertJournalSettled(root);
        }
        finally
        {
            ResetHooks();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Crash_between_renames_restores_previous_content()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        string target = Path.Combine(root, "library.json");
        byte[] original = Encoding.UTF8.GetBytes("old committed");
        File.WriteAllBytes(target, original);

        try
        {
            LinuxAtomicExpectedFileReplacer.BetweenRenamesForTests =
                _ => throw new SimulatedCrash();

            Assert.Throws<SimulatedCrash>(() =>
                Replace(root, target, original, "candidate"));

            LinuxOwnedArtifactReconciler.Result result = Reconcile(root);

            Assert.IsFalse(result.HasFatal, Describe(result));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(target));
            Assert.IsTrue(result.Outcomes.Any(o =>
                o.Code == "CAS_PREIMAGE_RESTORED"));
            AssertJournalSettled(root);
        }
        finally
        {
            ResetHooks();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Crash_after_candidate_publish_before_phase_record_completes_commit()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        string target = Path.Combine(root, "library.json");
        byte[] original = Encoding.UTF8.GetBytes("old committed");
        byte[] candidate = Encoding.UTF8.GetBytes("new committed");
        File.WriteAllBytes(target, original);

        try
        {
            LinuxAtomicExpectedFileReplacer.AfterCandidatePublishBeforePhaseRecordForTests =
                _ => throw new SimulatedCrash();

            Assert.Throws<SimulatedCrash>(() =>
                new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                    root,
                    target,
                    ExpectedFileState.Present(Hash(original)),
                    candidate,
                    DurableFileClass.LibraryMetadata));

            CollectionAssert.AreEqual(candidate, File.ReadAllBytes(target));

            LinuxOwnedArtifactReconciler.Result result = Reconcile(root);

            Assert.IsFalse(result.HasFatal, Describe(result));
            CollectionAssert.AreEqual(candidate, File.ReadAllBytes(target));
            Assert.AreEqual(0, Directory.GetFiles(root, ".prompthelper-preimage-*").Length);
            Assert.IsTrue(result.Outcomes.Any(o =>
                o.Code == "CAS_COMPLETED_DURING_RECOVERY"));
            AssertJournalSettled(root);
        }
        finally
        {
            ResetHooks();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Durable_candidate_published_phase_is_not_reopened_by_later_target_update()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        string target = Path.Combine(root, "library.json");
        byte[] v1 = Encoding.UTF8.GetBytes("v1");
        byte[] v2 = Encoding.UTF8.GetBytes("v2");
        byte[] v3 = Encoding.UTF8.GetBytes("v3");
        File.WriteAllBytes(target, v1);

        try
        {
            new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                root,
                target,
                ExpectedFileState.Present(Hash(v1)),
                v2,
                DurableFileClass.LibraryMetadata);

            File.WriteAllBytes(target, v3);

            LinuxOwnedArtifactReconciler.Result result = Reconcile(root);

            Assert.IsFalse(result.HasFatal, Describe(result));
            CollectionAssert.AreEqual(v3, File.ReadAllBytes(target));
            AssertJournalSettled(root);
        }
        finally
        {
            ResetHooks();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Foreign_target_while_exact_preimage_survives_is_preserved_and_fatal()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        string target = Path.Combine(root, "library.json");
        byte[] original = Encoding.UTF8.GetBytes("old");
        byte[] foreign = Encoding.UTF8.GetBytes("foreign");
        File.WriteAllBytes(target, original);

        try
        {
            LinuxAtomicExpectedFileReplacer.BetweenRenamesForTests =
                _ => throw new SimulatedCrash();

            Assert.Throws<SimulatedCrash>(() =>
                Replace(root, target, original, "candidate"));

            File.WriteAllBytes(target, foreign);

            LinuxOwnedArtifactReconciler.Result result = Reconcile(root);

            Assert.IsTrue(result.HasFatal);
            Assert.IsTrue(result.Outcomes.Any(o => o.Code == "CAS_AMBIGUOUS"));
            CollectionAssert.AreEqual(foreign, File.ReadAllBytes(target));

            string[] preimages = Directory.GetFiles(root, ".prompthelper-preimage-*");
            Assert.AreEqual(1, preimages.Length);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(preimages[0]));
        }
        finally
        {
            ResetHooks();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Foreign_replacement_at_stage_path_is_restored_and_never_deleted()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        string stage = Path.Combine(
            root,
            ".prompthelper-tmp-library-" + Guid.NewGuid().ToString("N") + ".tmp");
        byte[] ours = Encoding.UTF8.GetBytes("ours");
        byte[] foreign = Encoding.UTF8.GetBytes("foreign");
        File.WriteAllBytes(stage, ours);

        try
        {
            FileObjectIdentity identity = IdentityOf(stage);
            var journal = new LinuxOwnedArtifactJournal();
            journal.Record(
                root,
                new OwnedArtifactRecord(
                    Guid.NewGuid(),
                    OwnedArtifactKind.Stage,
                    OwnedArtifactPhase.Claimed,
                    Path.GetRelativePath(root, stage),
                    identity));

            LinuxExactFileRetirement.BeforeQuarantineRenameForTests = path =>
                AtomicExternalReplace(path, foreign);

            LinuxOwnedArtifactReconciler.Result result =
                LinuxOwnedArtifactReconciler.Reconcile(root, journal);

            Assert.IsFalse(result.HasFatal, Describe(result));
            CollectionAssert.AreEqual(foreign, File.ReadAllBytes(stage));
            Assert.IsTrue(result.Outcomes.Any(o => o.Code == "STAGE_REPLACED"));
            AssertJournalSettled(root);
        }
        finally
        {
            ResetHooks();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Foreign_platform_identity_in_journal_fails_closed()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        string artifact = Path.Combine(root, "artifact.tmp");
        File.WriteAllText(artifact, "keep");

        try
        {
            var journal = new LinuxOwnedArtifactJournal();
            journal.Record(
                root,
                new OwnedArtifactRecord(
                    Guid.NewGuid(),
                    OwnedArtifactKind.Stage,
                    OwnedArtifactPhase.Claimed,
                    Path.GetRelativePath(root, artifact),
                    new FileObjectIdentity(
                        "windows-file-id-v1",
                        "12345678:0123456789abcdef:fedcba9876543210")));

            LinuxOwnedArtifactReconciler.Result result =
                LinuxOwnedArtifactReconciler.Reconcile(root, journal);

            Assert.IsTrue(result.HasFatal);
            Assert.IsTrue(result.Outcomes.Any(o =>
                o.Code == "OWNERSHIP_IDENTITY_SCHEME_UNSUPPORTED"));
            Assert.IsTrue(File.Exists(artifact));
            Assert.AreEqual("keep", File.ReadAllText(artifact));
        }
        finally
        {
            ResetHooks();
            Directory.Delete(root, recursive: true);
        }
    }

    private static LinuxOwnedArtifactReconciler.Result Reconcile(string root) =>
        LinuxOwnedArtifactReconciler.Reconcile(
            root,
            new LinuxOwnedArtifactJournal());

    private static void Replace(
        string root,
        string target,
        byte[] expected,
        string candidate)
    {
        new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
            root,
            target,
            ExpectedFileState.Present(Hash(expected)),
            Encoding.UTF8.GetBytes(candidate),
            DurableFileClass.LibraryMetadata);
    }

    private static void AssertJournalSettled(string root)
    {
        OwnedArtifactJournalSnapshot snapshot =
            new LinuxOwnedArtifactJournal().Read(root);
        Assert.AreEqual(0, snapshot.Records.Count);
    }

    private static string Describe(
        LinuxOwnedArtifactReconciler.Result result) =>
        string.Join(
            "; ",
            result.Outcomes.Select(o =>
                $"[{o.Severity}:{o.Code}] {o.Message}"));

    private static string Hash(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static FileObjectIdentity IdentityOf(string path)
    {
        using SafeFileHandle handle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        return LinuxFileIdentity.FromHandle(handle).ToObjectIdentity();
    }

    private static void AtomicExternalReplace(
        string target,
        byte[] bytes)
    {
        string scratch = Path.Combine(
            Path.GetDirectoryName(target)!,
            "external-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllBytes(scratch, bytes);
        File.Move(scratch, target, overwrite: true);
    }

    private static void ResetHooks()
    {
        LinuxAtomicExpectedFileReplacer.PreSwapBarrierForTests = null;
        LinuxAtomicExpectedFileReplacer.AfterPreparedRecordForTests = null;
        LinuxAtomicExpectedFileReplacer.AfterSidelineBeforePhaseRecordForTests = null;
        LinuxAtomicExpectedFileReplacer.BetweenRenamesForTests = null;
        LinuxAtomicExpectedFileReplacer.BeforeCandidatePromotionForTests = null;
        LinuxAtomicExpectedFileReplacer.AfterCandidatePublishBeforePhaseRecordForTests = null;
        LinuxExactFileRetirement.BeforeQuarantineRenameForTests = null;
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-LinuxCasRecovery-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
