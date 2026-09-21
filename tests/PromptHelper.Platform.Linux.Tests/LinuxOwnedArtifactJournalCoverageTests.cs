using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Platform.Linux.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LinuxOwnedArtifactJournalCoverageTests
{
    [TestMethod]
    public void Rewrite_absent_snapshot_is_noop()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        var journal = new LinuxOwnedArtifactJournal();

        journal.Rewrite(root.Path, OwnedArtifactJournalSnapshot.Absent, []);

        Assert.IsFalse(File.Exists(OwnedArtifactJournalPaths.GetJournalPath(root.Path)));
    }

    [TestMethod]
    public void Rewrite_missing_ledger_is_noop_without_survivors_but_fatal_with_survivors()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        var journal = new LinuxOwnedArtifactJournal();
        OwnedArtifactRecord record = Stage();
        journal.Record(root.Path, record);

        OwnedArtifactJournalSnapshot expected = journal.Read(root.Path);
        File.Delete(OwnedArtifactJournalPaths.GetJournalPath(root.Path));

        journal.Rewrite(root.Path, expected, []);

        Assert.Throws<StaleExpectedFileException>(() =>
            journal.Rewrite(root.Path, expected, [record]));
    }

    [TestMethod]
    public void Rewrite_detects_same_inode_content_change()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        var journal = new LinuxOwnedArtifactJournal();
        OwnedArtifactRecord record = Stage();
        journal.Record(root.Path, record);

        OwnedArtifactJournalSnapshot expected = journal.Read(root.Path);
        string path = OwnedArtifactJournalPaths.GetJournalPath(root.Path);
        File.AppendAllText(path, "foreign");

        Assert.Throws<StaleExpectedFileException>(() =>
            journal.Rewrite(root.Path, expected, []));
        Assert.IsTrue(File.ReadAllText(path).EndsWith("foreign", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Rewrite_with_survivors_keeps_append_only_bytes_unchanged()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        var journal = new LinuxOwnedArtifactJournal();
        OwnedArtifactRecord record = Stage();
        journal.Record(root.Path, record);

        string path = OwnedArtifactJournalPaths.GetJournalPath(root.Path);
        byte[] before = File.ReadAllBytes(path);
        OwnedArtifactJournalSnapshot expected = journal.Read(root.Path);

        journal.Rewrite(root.Path, expected, [record]);

        CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void Invalid_utf8_and_empty_complete_record_are_corrupt()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string path = OwnedArtifactJournalPaths.GetJournalPath(root.Path);
        var journal = new LinuxOwnedArtifactJournal();

        File.WriteAllBytes(path, [0xff, 0xfe, 0x0a]);
        Assert.Throws<OwnedArtifactJournalCorruptException>(() =>
            journal.Read(root.Path));

        File.WriteAllText(path, "\n", new UTF8Encoding(false));
        Assert.Throws<OwnedArtifactJournalCorruptException>(() =>
            journal.Read(root.Path));
    }

    [TestMethod]
    public void All_supported_kinds_and_phases_round_trip()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        var journal = new LinuxOwnedArtifactJournal();

        OwnedArtifactKind[] kinds = Enum.GetValues<OwnedArtifactKind>();
        OwnedArtifactPhase[] phases = Enum.GetValues<OwnedArtifactPhase>();
        var expected = new List<OwnedArtifactRecord>();

        for (int i = 0; i < phases.Length; i++)
        {
            OwnedArtifactKind kind = kinds[i % kinds.Length];
            if (phases[i] >= OwnedArtifactPhase.MarkerPrepared)
            {
                kind = OwnedArtifactKind.MigrationMarker;
            }

            Guid? attempt = kind == OwnedArtifactKind.MigrationMarker
                ? Guid.NewGuid()
                : null;

            var record = new OwnedArtifactRecord(
                Guid.NewGuid(),
                kind,
                phases[i],
                $"record-{i}.tmp",
                new FileObjectIdentity("linux-statx-v1", $"identity-{i}"),
                RestoreRelativePath: kind == OwnedArtifactKind.CasPreimage
                    ? "library.json"
                    : null,
                CandidateSha256Hex: kind == OwnedArtifactKind.CasPreimage
                    ? new string('a', 64)
                    : null,
                CandidateLength: kind == OwnedArtifactKind.CasPreimage ? 12 : -1,
                MarkerAttemptId: attempt,
                PreviousSha256Hex: kind == OwnedArtifactKind.CasPreimage
                    ? new string('b', 64)
                    : null);

            journal.Record(root.Path, record);
            expected.Add(record);
        }

        // Guarantee every kind is also serialized at least once, independent of phase count.
        foreach (OwnedArtifactKind kind in kinds)
        {
            Guid? attempt = kind == OwnedArtifactKind.MigrationMarker
                ? Guid.NewGuid()
                : null;
            var record = new OwnedArtifactRecord(
                Guid.NewGuid(),
                kind,
                kind == OwnedArtifactKind.MigrationMarker
                    ? OwnedArtifactPhase.MarkerPrepared
                    : OwnedArtifactPhase.Claimed,
                $"kind-{kind}.tmp",
                new FileObjectIdentity("linux-statx-v1", $"kind-{kind}"),
                MarkerAttemptId: attempt);
            journal.Record(root.Path, record);
            expected.Add(record);
        }

        OwnedArtifactJournalSnapshot snapshot = journal.Read(root.Path);
        CollectionAssert.AreEqual(expected, snapshot.Records.ToArray());
    }

    [TestMethod]
    public void Legacy_linux_v5_record_parses_without_previous_hash()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        Guid op = Guid.NewGuid();
        string body = string.Join('|',
            "5",
            op.ToString("N"),
            "preimage",
            "prepared",
            B64("linux-statx-v1"),
            B64("identity"),
            B64("preimage.tmp"),
            B64("library.json"),
            new string('a', 64),
            "9",
            string.Empty);

        WriteRecord(root.Path, body);

        OwnedArtifactRecord record =
            new LinuxOwnedArtifactJournal().Read(root.Path).Records.Single();

        Assert.AreEqual(op, record.OperationId);
        Assert.AreEqual(OwnedArtifactKind.CasPreimage, record.Kind);
        Assert.IsNull(record.PreviousSha256Hex);
        Assert.AreEqual(9L, record.CandidateLength);
    }

    [TestMethod]
    public void Windows_v4_marker_record_parses_as_foreign_identity()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        Guid op = Guid.NewGuid();
        Guid attempt = Guid.NewGuid();
        string identity = "12345678:0123456789abcdef:fedcba9876543210";
        string body = string.Join('|',
            "4",
            op.ToString("N"),
            "marker",
            "marker-ready",
            identity,
            B64("marker.tmp"),
            string.Empty,
            string.Empty,
            "-1",
            attempt.ToString("N"));

        WriteRecord(root.Path, body);

        OwnedArtifactRecord record =
            new LinuxOwnedArtifactJournal().Read(root.Path).Records.Single();

        Assert.AreEqual("windows-file-id-v1", record.Identity.Scheme);
        Assert.AreEqual(identity, record.Identity.Value);
        Assert.AreEqual(attempt, record.MarkerAttemptId);
    }

    [TestMethod]
    public void Malformed_authority_fields_are_rejected_even_with_valid_checksum()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        Guid op = Guid.NewGuid();
        Guid attempt = Guid.NewGuid();

        string[] malformedBodies =
        [
            // invalid operation id
            V6("not-a-guid", "stage", "claimed", B64("linux-statx-v1"), B64("id"), B64("x.tmp"), "", "", "-1", "", ""),
            // unknown kind
            V6(op.ToString("N"), "unknown", "claimed", B64("linux-statx-v1"), B64("id"), B64("x.tmp"), "", "", "-1", "", ""),
            // unknown phase
            V6(op.ToString("N"), "stage", "unknown", B64("linux-statx-v1"), B64("id"), B64("x.tmp"), "", "", "-1", "", ""),
            // malformed base64
            V6(op.ToString("N"), "stage", "claimed", "***", B64("id"), B64("x.tmp"), "", "", "-1", "", ""),
            // blank identity scheme
            V6(op.ToString("N"), "stage", "claimed", B64(" "), B64("id"), B64("x.tmp"), "", "", "-1", "", ""),
            // unsafe path
            V6(op.ToString("N"), "stage", "claimed", B64("linux-statx-v1"), B64("id"), B64("../escape"), "", "", "-1", "", ""),
            // malformed restore path
            V6(op.ToString("N"), "preimage", "prepared", B64("linux-statx-v1"), B64("id"), B64("pre.tmp"), B64("./bad"), new string('a',64), "1", "", ""),
            // invalid candidate hash
            V6(op.ToString("N"), "preimage", "prepared", B64("linux-statx-v1"), B64("id"), B64("pre.tmp"), B64("library.json"), "xyz", "1", "", ""),
            // invalid candidate length
            V6(op.ToString("N"), "preimage", "prepared", B64("linux-statx-v1"), B64("id"), B64("pre.tmp"), B64("library.json"), new string('a',64), "NaN", "", ""),
            // marker missing attempt
            V6(op.ToString("N"), "marker", "marker-ready", B64("linux-statx-v1"), B64("id"), B64("marker.tmp"), "", "", "-1", "", ""),
            // non-marker with marker attempt
            V6(op.ToString("N"), "stage", "claimed", B64("linux-statx-v1"), B64("id"), B64("x.tmp"), "", "", "-1", attempt.ToString("N"), ""),
            // invalid previous hash
            V6(op.ToString("N"), "preimage", "prepared", B64("linux-statx-v1"), B64("id"), B64("pre.tmp"), B64("library.json"), new string('a',64), "1", "", "bad")
        ];

        var journal = new LinuxOwnedArtifactJournal();
        string path = OwnedArtifactJournalPaths.GetJournalPath(root.Path);

        foreach (string body in malformedBodies)
        {
            WriteRecord(root.Path, body);
            Assert.Throws<OwnedArtifactJournalCorruptException>(() =>
                journal.Read(root.Path), $"Malformed body unexpectedly parsed: {body}");
            File.Delete(path);
        }

        // Valid body, deliberately invalid checksum.
        string valid = V6(op.ToString("N"), "stage", "claimed",
            B64("linux-statx-v1"), B64("id"), B64("x.tmp"), "", "", "-1", "", "");
        File.WriteAllText(path, valid + "|deadbeefdeadbeef\n", new UTF8Encoding(false));
        Assert.Throws<OwnedArtifactJournalCorruptException>(() =>
            journal.Read(root.Path));
    }

    [TestMethod]
    public void Invalid_windows_identity_and_legacy_marker_shape_are_rejected()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        Guid op = Guid.NewGuid();

        string invalidIdentityBody = string.Join('|',
            "2", op.ToString("N"), "stage", "claimed",
            "bad-identity",
            B64("stage.tmp"), "", "", "-1");
        WriteRecord(root.Path, invalidIdentityBody);
        Assert.Throws<OwnedArtifactJournalCorruptException>(() =>
            new LinuxOwnedArtifactJournal().Read(root.Path));

        File.Delete(OwnedArtifactJournalPaths.GetJournalPath(root.Path));

        string legacyMarkerWithoutAttempt = string.Join('|',
            "3", op.ToString("N"), "marker", "marker-ready",
            "12345678:0123456789abcdef:fedcba9876543210",
            B64("marker.tmp"), "", "", "-1");
        WriteRecord(root.Path, legacyMarkerWithoutAttempt);
        Assert.Throws<OwnedArtifactJournalCorruptException>(() =>
            new LinuxOwnedArtifactJournal().Read(root.Path));
    }

    private static OwnedArtifactRecord Stage() =>
        new(
            Guid.NewGuid(),
            OwnedArtifactKind.Stage,
            OwnedArtifactPhase.Claimed,
            "stage.tmp",
            new FileObjectIdentity("linux-statx-v1", "identity"));

    private static string V6(
        string op,
        string kind,
        string phase,
        string scheme,
        string value,
        string path,
        string restore,
        string candidate,
        string length,
        string attempt,
        string previous) =>
        string.Join('|',
            "6", op, kind, phase, scheme, value, path, restore,
            candidate, length, attempt, previous);

    private static string B64(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static void WriteRecord(string root, string body)
    {
        File.WriteAllText(
            OwnedArtifactJournalPaths.GetJournalPath(root),
            body + "|" + Checksum(body) + "\n",
            new UTF8Encoding(false));
    }

    private static string Checksum(string body) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(body)))[..16];

    private sealed class Root : IDisposable
    {
        public Root()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PromptHelper-LinuxJournalCoverage-" + Guid.NewGuid().ToString("N"));
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
