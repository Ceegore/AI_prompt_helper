using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Tests;

/// <summary>
/// CRUU16-002 and CRUU16-003: the ownership ledger authorizes deletion and compare-and-swap
/// restoration, so it is held to the same standard as the data it protects — parsed exactly,
/// never followed through a reparse point, and replaced only against the object that was read.
/// </summary>
[TestClass]
public sealed class Cruu16OwnershipJournalTests
{
    private static string JournalPath(string root) =>
        Path.Combine(root, ".prompthelper-owned.log");

    /// <summary>
    /// Rewrites the ledger in place. The real one is created hidden, and the default
    /// <c>FileMode.Create</c> that <c>File.WriteAllText</c> uses is refused for hidden files.
    /// </summary>
    private static void WriteJournalRaw(string root, byte[] content)
    {
        using var stream = new FileStream(JournalPath(root), FileMode.Truncate, FileAccess.Write, FileShare.None);
        stream.Write(content);
    }

    private static void WriteJournalRaw(string root, string content) =>
        WriteJournalRaw(root, Encoding.UTF8.GetBytes(content));

    private static void AppendJournalRaw(string root, string content)
    {
        using var stream = new FileStream(JournalPath(root), FileMode.Append, FileAccess.Write, FileShare.None);
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    /// <summary>Writes one genuine record by claiming a real artifact, then returns that line.</summary>
    private static string SeedOneRecord(string root)
    {
        string artifact = Path.Combine(root, $".prompthelper-tmp-library-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(artifact, "staged");
        OwnedArtifactTestSupport.ClaimOwnership(root, artifact);
        return File.ReadAllText(JournalPath(root));
    }

    private static void CreateFileSymlinkOrFail(string linkPath, string targetPath)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink \"{linkPath}\" \"{targetPath}\"");
        ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 30_000);

        Assert.IsTrue(
            run.Exited && run.ExitCode == 0 && File.Exists(linkPath),
            "Creating a file symlink is required for this test. Enable Windows Developer Mode " +
            $"or run elevated.\n{run.CombinedOutput}");
    }

    // ==========================================
    // CRUU16-002: strict, fail-closed parsing
    // ==========================================

    [TestMethod]
    public void CRUU16_002_Malformed_middle_ownership_record_fails_closed()
    {
        using var temp = new TestDirectory();
        SeedOneRecord(temp.Root);
        SeedOneRecord(temp.Root);

        string[] lines = File.ReadAllText(JournalPath(temp.Root)).Split('\n');
        lines[0] = "2|not-a-guid|stage|claimed|zzz|!!!|||deadbeefdeadbeef";
        WriteJournalRaw(temp.Root, string.Join('\n', lines));

        var journal = new WindowsOwnedArtifactJournal();

        Assert.ThrowsExactly<OwnedArtifactJournalCorruptException>(() => journal.Read(temp.Root));
    }

    [TestMethod]
    public void CRUU16_002_Malformed_complete_final_record_fails_closed()
    {
        using var temp = new TestDirectory();
        SeedOneRecord(temp.Root);

        // Complete (newline-terminated) but corrupt: not a torn append, so it must not be
        // treated as one.
        AppendJournalRaw(temp.Root, "2|corrupt|stage|claimed|x|y|||0000000000000000\n");

        var journal = new WindowsOwnedArtifactJournal();

        Assert.ThrowsExactly<OwnedArtifactJournalCorruptException>(() => journal.Read(temp.Root));
    }

    [TestMethod]
    public void CRUU16_002_Only_incomplete_nonterminated_final_tail_may_be_ignored()
    {
        using var temp = new TestDirectory();
        SeedOneRecord(temp.Root);

        // Exactly the shape a torn append produces: bytes with no terminating newline.
        AppendJournalRaw(temp.Root, "2|abc|stage|clai");

        var journal = new WindowsOwnedArtifactJournal();
        OwnedArtifactJournalSnapshot snapshot = journal.Read(temp.Root);

        Assert.AreEqual(1, snapshot.Records.Count,
            "A torn final append is discarded; every complete record before it survives.");
    }

    [TestMethod]
    public void CRUU16_002_Invalid_UTF8_ownership_journal_fails_closed()
    {
        using var temp = new TestDirectory();
        SeedOneRecord(temp.Root);

        byte[] raw = File.ReadAllBytes(JournalPath(temp.Root));
        byte[] corrupted = [.. raw, 0xC3, 0x28, (byte)'\n'];
        WriteJournalRaw(temp.Root, corrupted);

        var journal = new WindowsOwnedArtifactJournal();

        Assert.ThrowsExactly<OwnedArtifactJournalCorruptException>(() => journal.Read(temp.Root));
    }

    [TestMethod]
    public void CRUU16_002_Corrupt_journal_never_gets_compacted_over_original_evidence()
    {
        using var temp = new TestDirectory();
        SeedOneRecord(temp.Root);
        AppendJournalRaw(temp.Root, "2|corrupt|stage|claimed|x|y|||0000000000000000\n");

        byte[] before = File.ReadAllBytes(JournalPath(temp.Root));

        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(temp.Root, new WindowsOwnedArtifactJournal());

        Assert.IsTrue(result.HasFatal);
        Assert.IsTrue(result.Outcomes.Any(o => o.Code == "OWNERSHIP_JOURNAL_CORRUPT"));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(JournalPath(temp.Root)),
            "A ledger nobody could read is the evidence needed to sort the situation out by hand.");
    }

    [TestMethod]
    public void CRUU16_002_Corrupt_CasPreimage_record_never_silently_disappears()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "library.json");
        byte[] committed = Encoding.UTF8.GetBytes("committed");
        File.WriteAllBytes(target, committed);

        WindowsAtomicExpectedFileReplacer.BetweenRenamesForTests = _ => throw new InterruptedSwap();
        try
        {
            Assert.ThrowsExactly<InterruptedSwap>(() =>
                new WindowsAtomicExpectedFileReplacer().ReplaceIfExpected(
                    temp.Root,
                    target,
                    ExpectedFileState.Present(Convert.ToHexStringLower(SHA256.HashData(committed))),
                    Encoding.UTF8.GetBytes("candidate"),
                    DurableFileClass.LibraryMetadata));
        }
        finally
        {
            WindowsAtomicExpectedFileReplacer.BetweenRenamesForTests = null;
        }

        // Corrupt the pre-image record's checksum. Dropping it silently would strand the last
        // committed content with nothing recording where it went.
        string[] lines = File.ReadAllText(JournalPath(temp.Root)).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("|preimage|", StringComparison.Ordinal))
            {
                lines[i] = lines[i][..^4] + "0000";
            }
        }

        WriteJournalRaw(temp.Root, string.Join('\n', lines));

        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(temp.Root, new WindowsOwnedArtifactJournal());

        Assert.IsTrue(result.HasFatal, "A corrupt pre-image record must stop reconciliation, not vanish from it.");
        Assert.AreEqual(1, Directory.GetFiles(temp.Root, ".prompthelper-preimage-*").Length,
            "The last committed content must still be on disk.");
    }

    private sealed class InterruptedSwap : Exception;

    // ==========================================
    // CRUU16-003: the ledger is itself a strict authority
    // ==========================================

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU16_003_Ownership_journal_symlink_is_never_followed_for_append()
    {
        using var temp = new TestDirectory();
        using var outside = new TestDirectory();

        string victim = Path.Combine(outside.Root, "someone-elses-file.log");
        File.WriteAllText(victim, "outside content");
        CreateFileSymlinkOrFail(JournalPath(temp.Root), victim);

        string artifact = Path.Combine(temp.Root, $".prompthelper-tmp-library-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(artifact, "staged");

        Assert.ThrowsExactly<OwnedArtifactJournalCorruptException>(
            () => OwnedArtifactTestSupport.ClaimOwnership(temp.Root, artifact));

        Assert.AreEqual("outside content", File.ReadAllText(victim),
            "Ownership claims must never be appended to a file outside the data root.");
    }

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU16_003_Ownership_journal_symlink_is_never_followed_for_read()
    {
        using var temp = new TestDirectory();
        using var outside = new TestDirectory();

        string victim = Path.Combine(outside.Root, "someone-elses-file.log");
        File.WriteAllText(victim, "outside content");
        CreateFileSymlinkOrFail(JournalPath(temp.Root), victim);

        var journal = new WindowsOwnedArtifactJournal();

        Assert.ThrowsExactly<OwnedArtifactJournalCorruptException>(() => journal.Read(temp.Root));
        Assert.AreEqual("outside content", File.ReadAllText(victim));
    }

    [TestMethod]
    public void CRUU16_003_Journal_replaced_after_read_is_not_deleted_by_empty_rewrite()
    {
        using var temp = new TestDirectory();
        SeedOneRecord(temp.Root);

        var journal = new WindowsOwnedArtifactJournal();
        OwnedArtifactJournalSnapshot snapshot = journal.Read(temp.Root);

        // A different object takes the ledger's pathname between the read and the rewrite.
        File.Delete(JournalPath(temp.Root));
        byte[] foreign = Encoding.UTF8.GetBytes("somebody else's file");
        File.WriteAllBytes(JournalPath(temp.Root), foreign);

        journal.Rewrite(temp.Root, snapshot, []);

        Assert.IsTrue(File.Exists(JournalPath(temp.Root)));
        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(JournalPath(temp.Root)),
            "An empty rewrite must delete only the ledger it actually read.");
    }

    [TestMethod]
    public void CRUU16_003_Journal_replaced_after_read_is_not_overwritten_by_nonempty_rewrite()
    {
        using var temp = new TestDirectory();
        SeedOneRecord(temp.Root);

        var journal = new WindowsOwnedArtifactJournal();
        OwnedArtifactJournalSnapshot snapshot = journal.Read(temp.Root);

        File.Delete(JournalPath(temp.Root));
        byte[] foreign = Encoding.UTF8.GetBytes("somebody else's file");
        File.WriteAllBytes(JournalPath(temp.Root), foreign);

        Assert.ThrowsExactly<StaleExpectedFileException>(
            () => journal.Rewrite(temp.Root, snapshot, snapshot.Records));

        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(JournalPath(temp.Root)),
            "A rewrite is bound to the ledger that was read, not to its pathname.");
    }

    [TestMethod]
    public void CRUU16_003_Journal_rewrite_stage_is_physically_root_bound()
    {
        using var temp = new TestDirectory();
        SeedOneRecord(temp.Root);

        var journal = new WindowsOwnedArtifactJournal();
        OwnedArtifactJournalSnapshot snapshot = journal.Read(temp.Root);

        string[] before = Directory.GetFiles(temp.Root, ".prompthelper-tmp-*");

        // The rewrite goes through the same audited compare-and-swap as every other managed
        // file, which creates its stage root-bound and leaves nothing behind.
        journal.Rewrite(temp.Root, snapshot, snapshot.Records);

        CollectionAssert.AreEquivalent(
            before,
            Directory.GetFiles(temp.Root, ".prompthelper-tmp-*"),
            "The rewrite must leave no staging residue of its own.");
        Assert.AreEqual(snapshot.Records.Count, journal.Read(temp.Root).Records.Count);
    }

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU16_003_Journal_final_handle_path_must_equal_expected_managed_location()
    {
        using var temp = new TestDirectory();
        using var outside = new TestDirectory();

        // A ledger that resolves anywhere other than its own managed pathname is refused, for
        // reading and for appending alike.
        string victim = Path.Combine(outside.Root, "elsewhere.log");
        File.WriteAllText(victim, "outside");
        CreateFileSymlinkOrFail(JournalPath(temp.Root), victim);

        var journal = new WindowsOwnedArtifactJournal();
        Assert.ThrowsExactly<OwnedArtifactJournalCorruptException>(() => journal.Read(temp.Root));

        // And reconciliation treats that as fatal rather than as an absent ledger.
        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(temp.Root, journal);

        Assert.IsTrue(result.HasFatal);
    }
}
