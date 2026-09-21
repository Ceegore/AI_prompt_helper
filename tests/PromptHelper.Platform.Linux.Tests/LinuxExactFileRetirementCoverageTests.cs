using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using PromptHelper.Services;

namespace PromptHelper.Platform.Linux.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LinuxExactFileRetirementCoverageTests
{
    [TestMethod]
    public void Missing_path_returns_missing()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        FileObjectIdentity identity =
            new("linux-statx-v1", "nonexistent");

        LinuxExactRetirementOutcome result =
            LinuxExactFileRetirement.Retire(
                root.Path,
                Path.Combine(root.Path, "missing.tmp"),
                identity);

        Assert.AreEqual(LinuxExactRetirementOutcome.Missing, result);
    }

    [TestMethod]
    public void Foreign_identity_is_preserved()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string path = Path.Combine(root.Path, "artifact.tmp");
        File.WriteAllText(path, "foreign");

        LinuxExactRetirementOutcome result =
            LinuxExactFileRetirement.Retire(
                root.Path,
                path,
                new FileObjectIdentity("linux-statx-v1", "different"));

        Assert.AreEqual(LinuxExactRetirementOutcome.ForeignPreserved, result);
        Assert.AreEqual("foreign", File.ReadAllText(path));
    }

    [TestMethod]
    public void Exact_identity_without_content_authority_retires_file()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string path = Path.Combine(root.Path, "artifact.tmp");
        File.WriteAllText(path, "payload");
        FileObjectIdentity identity = IdentityOf(path);

        LinuxExactRetirementOutcome result =
            LinuxExactFileRetirement.Retire(root.Path, path, identity);

        Assert.AreEqual(LinuxExactRetirementOutcome.Retired, result);
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public void Length_mismatch_fails_closed_without_deleting_file()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string path = Path.Combine(root.Path, "artifact.tmp");
        byte[] bytes = Encoding.UTF8.GetBytes("payload");
        File.WriteAllBytes(path, bytes);
        FileObjectIdentity identity = IdentityOf(path);

        StaleExpectedFileException ex =
            Assert.Throws<StaleExpectedFileException>(() =>
                LinuxExactFileRetirement.Retire(
                    root.Path,
                    path,
                    identity,
                    expectedSha256Hex: Hash(bytes),
                    expectedLength: bytes.LongLength + 1));

        StringAssert.Contains(ex.Message, "length changed");
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void Hash_mismatch_fails_closed_without_deleting_file()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string path = Path.Combine(root.Path, "artifact.tmp");
        byte[] bytes = Encoding.UTF8.GetBytes("payload");
        File.WriteAllBytes(path, bytes);
        FileObjectIdentity identity = IdentityOf(path);

        StaleExpectedFileException ex =
            Assert.Throws<StaleExpectedFileException>(() =>
                LinuxExactFileRetirement.Retire(
                    root.Path,
                    path,
                    identity,
                    expectedSha256Hex: Hash(Encoding.UTF8.GetBytes("other")),
                    expectedLength: bytes.LongLength));

        StringAssert.Contains(ex.Message, "content changed");
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void File_disappearing_before_quarantine_rename_returns_missing()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string path = Path.Combine(root.Path, "artifact.tmp");
        File.WriteAllText(path, "payload");
        FileObjectIdentity identity = IdentityOf(path);

        LinuxExactFileRetirement.BeforeQuarantineRenameForTests =
            p => File.Delete(p);

        LinuxExactRetirementOutcome result =
            LinuxExactFileRetirement.Retire(root.Path, path, identity);

        Assert.AreEqual(LinuxExactRetirementOutcome.Missing, result);
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public void Foreign_path_replacement_before_quarantine_is_restored_and_preserved()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string path = Path.Combine(root.Path, "artifact.tmp");
        byte[] ours = Encoding.UTF8.GetBytes("ours");
        byte[] foreign = Encoding.UTF8.GetBytes("foreign");
        File.WriteAllBytes(path, ours);
        FileObjectIdentity identity = IdentityOf(path);

        LinuxExactFileRetirement.BeforeQuarantineRenameForTests =
            p => AtomicExternalReplace(p, foreign);

        LinuxExactRetirementOutcome result =
            LinuxExactFileRetirement.Retire(root.Path, path, identity);

        Assert.AreEqual(LinuxExactRetirementOutcome.ForeignPreserved, result);
        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(path));
        Assert.AreEqual(
            0,
            Directory.GetFiles(root.Path, ".prompthelper-retired-*").Length);
    }

    [TestMethod]
    public void Retirement_path_without_parent_is_rejected()
    {
        if (!OperatingSystem.IsLinux()) return;

        // '/' has no parent path after normalization. The guard must reject it before
        // attempting to treat the root directory as a retireable file.
        Assert.Throws<InvalidOperationException>(() =>
            LinuxExactFileRetirement.Retire(
                "/",
                "/",
                new FileObjectIdentity("linux-statx-v1", "irrelevant")));
    }

    private static void AtomicExternalReplace(string target, byte[] bytes)
    {
        string scratch = Path.Combine(
            Path.GetDirectoryName(target)!,
            "foreign-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllBytes(scratch, bytes);
        File.Move(scratch, target, overwrite: true);
    }

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
                "PromptHelper-LinuxRetirementCoverage-" + Guid.NewGuid().ToString("N"));
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
