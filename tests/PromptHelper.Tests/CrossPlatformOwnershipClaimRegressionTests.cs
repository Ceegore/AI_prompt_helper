using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CrossPlatformOwnershipClaimRegressionTests
{
    private sealed class RewriteFailingJournal : IOwnedArtifactJournal
    {
        public WindowsOwnedArtifactJournal Inner { get; } = new();

        public void Record(string root, OwnedArtifactRecord record) =>
            Inner.Record(root, record);

        public OwnedArtifactJournalSnapshot Read(string root) =>
            Inner.Read(root);

        public void Rewrite(
            string root,
            OwnedArtifactJournalSnapshot expected,
            IReadOnlyList<OwnedArtifactRecord> surviving) =>
            throw new IOException("Injected completed-claim retirement failure.");
    }

    [TestMethod]
    public void Completed_expected_missing_CAS_retires_ownership_ledger_immediately()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "new.json");
        byte[] candidate = Encoding.UTF8.GetBytes("new content");

        new WindowsAtomicExpectedFileReplacer().ReplaceIfExpected(
            temp.Root,
            target,
            ExpectedFileState.Missing,
            candidate,
            DurableFileClass.LibraryMetadata);

        CollectionAssert.AreEqual(candidate, File.ReadAllBytes(target));
        Assert.IsFalse(
            File.Exists(WindowsOwnedArtifactJournal.GetJournalPath(temp.Root)),
            "A clean completed Windows CAS must not leave a Windows-only recovery ledger that blocks Linux.");
    }

    [TestMethod]
    public void Completed_expected_present_CAS_retires_ownership_ledger_immediately()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "library.json");
        byte[] before = Encoding.UTF8.GetBytes("before");
        byte[] after = Encoding.UTF8.GetBytes("after");
        File.WriteAllBytes(target, before);

        new WindowsAtomicExpectedFileReplacer().ReplaceIfExpected(
            temp.Root,
            target,
            ExpectedFileState.Present(Hash(before)),
            after,
            DurableFileClass.LibraryMetadata);

        CollectionAssert.AreEqual(after, File.ReadAllBytes(target));
        Assert.IsFalse(
            File.Exists(WindowsOwnedArtifactJournal.GetJournalPath(temp.Root)),
            "A clean completed replacement must retire its Windows identity claims before another OS opens the library.");
    }

    [TestMethod]
    public void Claim_retirement_failure_after_commit_requires_restart_and_preserves_ledger()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "new.json");
        byte[] candidate = Encoding.UTF8.GetBytes("committed");
        var journal = new RewriteFailingJournal();

        CommittedAtomicReplacementRequiresRestartException ex =
            Assert.Throws<CommittedAtomicReplacementRequiresRestartException>(() =>
                new WindowsAtomicExpectedFileReplacer(journal).ReplaceIfExpected(
                    temp.Root,
                    target,
                    ExpectedFileState.Missing,
                    candidate,
                    DurableFileClass.LibraryMetadata));

        Assert.AreEqual(Path.GetFullPath(target), Path.GetFullPath(ex.TargetPath));
        CollectionAssert.AreEqual(candidate, File.ReadAllBytes(target));
        Assert.IsTrue(
            File.Exists(WindowsOwnedArtifactJournal.GetJournalPath(temp.Root)),
            "If completed-claim retirement cannot be proven, the ledger must be preserved for startup recovery.");
        Assert.IsTrue(journal.Inner.Read(temp.Root).Records.Count > 0);
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
}
