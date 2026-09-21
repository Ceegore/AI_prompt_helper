using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Platform.Linux.Tests;

[TestClass]
public sealed class LinuxOwnedArtifactJournalTests
{
    [TestMethod]
    public void Record_and_read_round_trip_preserves_platform_identity_and_claim()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        try
        {
            var journal = new LinuxOwnedArtifactJournal();
            var identity = new FileObjectIdentity(
                "linux-statx-v1",
                "00000008:00000001:0000000000001234:0000000068d00000:00000001");
            var record = new OwnedArtifactRecord(
                Guid.NewGuid(),
                OwnedArtifactKind.CasPreimage,
                OwnedArtifactPhase.Prepared,
                ".prompthelper-preimage-library.json-test.tmp",
                identity,
                "library.json",
                new string('a', 64),
                42);

            journal.Record(root, record);
            OwnedArtifactJournalSnapshot snapshot = journal.Read(root);

            Assert.IsTrue(snapshot.Exists);
            Assert.AreEqual(1, snapshot.Records.Count);
            Assert.AreEqual(record, snapshot.Records[0]);
            Assert.AreEqual("linux-statx-v1", snapshot.Identity!.Value.Scheme);
            Assert.IsNotNull(snapshot.Sha256Hex);
            Assert.AreEqual(64, snapshot.Sha256Hex.Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Empty_rewrite_truncates_exact_ledger_without_path_delete()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        try
        {
            var journal = new LinuxOwnedArtifactJournal();
            journal.Record(root, StageRecord());

            OwnedArtifactJournalSnapshot before = journal.Read(root);
            journal.Rewrite(root, before, []);

            string path = OwnedArtifactJournalPaths.GetJournalPath(root);
            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual(0L, new FileInfo(path).Length);

            OwnedArtifactJournalSnapshot after = journal.Read(root);
            Assert.IsTrue(after.Exists);
            Assert.AreEqual(0, after.Records.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Rewrite_preserves_foreign_replacement_at_ledger_path()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        try
        {
            var journal = new LinuxOwnedArtifactJournal();
            journal.Record(root, StageRecord());

            OwnedArtifactJournalSnapshot expected = journal.Read(root);
            string path = OwnedArtifactJournalPaths.GetJournalPath(root);

            File.Delete(path);
            File.WriteAllText(path, "foreign replacement");

            journal.Rewrite(root, expected, []);

            Assert.AreEqual(
                "foreign replacement",
                File.ReadAllText(path),
                "A different inode at the ledger pathname must never be truncated or deleted.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Rewrite_with_surviving_claim_fails_closed_on_foreign_replacement()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        try
        {
            var journal = new LinuxOwnedArtifactJournal();
            OwnedArtifactRecord record = StageRecord();
            journal.Record(root, record);

            OwnedArtifactJournalSnapshot expected = journal.Read(root);
            string path = OwnedArtifactJournalPaths.GetJournalPath(root);

            File.Delete(path);
            File.WriteAllText(path, "foreign replacement");

            Assert.Throws<StaleExpectedFileException>(() =>
                journal.Rewrite(root, expected, [record]));

            Assert.AreEqual("foreign replacement", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Symlink_ledger_is_rejected_without_touching_destination()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        try
        {
            string outside = Path.Combine(root, "outside.log");
            File.WriteAllText(outside, "outside");

            string ledger = OwnedArtifactJournalPaths.GetJournalPath(root);
            File.CreateSymbolicLink(ledger, outside);

            var journal = new LinuxOwnedArtifactJournal();
            Assert.Throws<OwnedArtifactJournalCorruptException>(() =>
                journal.Read(root));

            Assert.AreEqual("outside", File.ReadAllText(outside));
            Assert.IsNotNull(new FileInfo(ledger).LinkTarget);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Complete_corrupt_record_is_fatal_but_torn_trailing_fragment_is_ignored()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        try
        {
            string path = OwnedArtifactJournalPaths.GetJournalPath(root);
            File.WriteAllText(path, "garbage\n");

            var journal = new LinuxOwnedArtifactJournal();
            Assert.Throws<OwnedArtifactJournalCorruptException>(() =>
                journal.Read(root));

            File.Delete(path);
            journal.Record(root, StageRecord());
            File.AppendAllText(path, "torn-fragment-without-newline");

            OwnedArtifactJournalSnapshot snapshot = journal.Read(root);
            Assert.AreEqual(1, snapshot.Records.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Legacy_windows_v2_record_is_parsed_as_foreign_platform_identity()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        try
        {
            Guid operation = Guid.NewGuid();
            string identity = "12345678:0123456789abcdef:fedcba9876543210";
            string relative = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(".prompthelper-tmp-library-test.tmp"));
            string body = string.Join('|',
                "2",
                operation.ToString("N"),
                "stage",
                "claimed",
                identity,
                relative,
                string.Empty,
                string.Empty,
                "-1");
            string line = body + "|" + Checksum(body) + "\n";

            File.WriteAllText(
                OwnedArtifactJournalPaths.GetJournalPath(root),
                line,
                new UTF8Encoding(false));

            OwnedArtifactJournalSnapshot snapshot =
                new LinuxOwnedArtifactJournal().Read(root);

            Assert.AreEqual(1, snapshot.Records.Count);
            Assert.AreEqual(
                "windows-file-id-v1",
                snapshot.Records[0].Identity.Scheme);
            Assert.AreEqual(identity, snapshot.Records[0].Identity.Value);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static OwnedArtifactRecord StageRecord() =>
        new(
            Guid.NewGuid(),
            OwnedArtifactKind.Stage,
            OwnedArtifactPhase.Claimed,
            ".prompthelper-tmp-library-test.tmp",
            new FileObjectIdentity(
                "linux-statx-v1",
                "00000008:00000001:0000000000004321:0000000068d00000:00000002"));

    private static string Checksum(string body) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(body)))[..16];

    private static string CreateRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-LinuxJournal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
