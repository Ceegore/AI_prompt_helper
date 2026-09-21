using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using PromptHelper.Services;

namespace PromptHelper.Platform.Linux.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LinuxNativeAuthorityCoverageTests
{
    [TestMethod]
    public void Native_file_open_helpers_cover_existing_missing_and_symlink_paths()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string file = Path.Combine(root.Path, "file.txt");
        string link = Path.Combine(root.Path, "link.txt");
        File.WriteAllText(file, "data");

        using (SafeFileHandle? rw = LinuxNativeFileSystem.OpenReadWriteNoFollowOrNull(file))
        {
            Assert.IsNotNull(rw);
        }

        Assert.IsNull(
            LinuxNativeFileSystem.OpenReadWriteNoFollowOrNull(
                Path.Combine(root.Path, "missing-rw")));

        using (SafeFileHandle? ro = LinuxNativeFileSystem.OpenReadOnlyNoFollowOrNull(file))
        {
            Assert.IsNotNull(ro);
        }

        Assert.IsNull(
            LinuxNativeFileSystem.OpenReadOnlyNoFollowOrNull(
                Path.Combine(root.Path, "missing-ro")));

        using (SafeFileHandle created =
               LinuxNativeFileSystem.OpenReadWriteNoFollowOrCreate(
                   Path.Combine(root.Path, "created.txt")))
        {
            Assert.IsFalse(created.IsInvalid);
        }

        File.CreateSymbolicLink(link, file);

        Assert.Throws<InvalidDataException>(() =>
            LinuxNativeFileSystem.OpenReadWriteNoFollowOrNull(link));
        Assert.Throws<InvalidDataException>(() =>
            LinuxNativeFileSystem.OpenReadWriteNoFollowOrCreate(link));
        Assert.Throws<InvalidDataException>(() =>
            LinuxNativeFileSystem.OpenReadOnlyNoFollowOrNull(link));
    }

    [TestMethod]
    public void Exclusive_stage_refuses_existing_path_and_rename_primitives_are_fail_closed()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string stage = Path.Combine(root.Path, "stage.tmp");

        using (FileStream created = LinuxNativeFileSystem.CreateExclusiveStage(stage))
        {
            created.Write(Encoding.UTF8.GetBytes("stage"));
            created.Flush(true);
        }

        Assert.Throws<IOException>(() =>
            LinuxNativeFileSystem.CreateExclusiveStage(stage));

        string target = Path.Combine(root.Path, "target.tmp");
        Assert.IsTrue(
            LinuxNativeFileSystem.TryRenameNoReplace(stage, target, out int firstError));
        Assert.AreEqual(0, firstError);

        string source2 = Path.Combine(root.Path, "source2.tmp");
        File.WriteAllText(source2, "two");
        Assert.IsFalse(
            LinuxNativeFileSystem.TryRenameNoReplace(source2, target, out int secondError));
        Assert.AreEqual(LinuxNativeFileSystem.ErrorExists, secondError);
        Assert.AreEqual("stage", File.ReadAllText(target));
        Assert.AreEqual("two", File.ReadAllText(source2));

        string replacement = Path.Combine(root.Path, "replacement.tmp");
        File.WriteAllText(replacement, "replacement");
        LinuxNativeFileSystem.RenameReplace(replacement, target);
        Assert.AreEqual("replacement", File.ReadAllText(target));

        Assert.Throws<IOException>(() =>
            LinuxNativeFileSystem.RenameReplace(
                Path.Combine(root.Path, "missing-source"),
                target));
    }

    [TestMethod]
    public void Flush_directory_succeeds_for_directory_and_fails_for_missing_path()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        LinuxNativeFileSystem.FlushDirectory(root.Path);

        Assert.Throws<IOException>(() =>
            LinuxNativeFileSystem.FlushDirectory(
                Path.Combine(root.Path, "missing-directory")));

        IOException ex = LinuxNativeFileSystem.NativeIOException("native", 2);
        StringAssert.Contains(ex.Message, "errno 2");
        Assert.IsNotNull(ex.InnerException);
    }

    [TestMethod]
    public void Expected_authority_reads_and_validates_exact_file()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        string file = Path.Combine(root.Path, "file.txt");
        byte[] bytes = Encoding.UTF8.GetBytes("payload");
        File.WriteAllBytes(file, bytes);

        using LinuxExpectedTargetAuthority? authority =
            LinuxExpectedTargetAuthority.Open(file, root.Path);

        Assert.IsNotNull(authority);
        Assert.AreEqual(Path.GetFullPath(file), authority!.OpenedPath);
        Assert.AreEqual(Path.GetFullPath(file), authority.FinalPhysicalPath);
        CollectionAssert.AreEqual(bytes, authority.ReadAllBytes());

        string hash = Hash(bytes);
        authority.AssertContentMatches(hash);
        authority.AssertIdentityMatches(authority.Identity.ToObjectIdentity());

        Assert.Throws<StaleExpectedFileException>(() =>
            authority.AssertContentMatches(Hash(Encoding.UTF8.GetBytes("other"))));

        Assert.Throws<StaleExpectedFileException>(() =>
            authority.AssertIdentityMatches(
                new FileObjectIdentity("linux-statx-v1", "different")));
    }

    [TestMethod]
    public void Expected_authority_missing_symlink_directory_and_outside_root_are_refused()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        using var other = new Root();

        Assert.IsNull(
            LinuxExpectedTargetAuthority.Open(
                Path.Combine(root.Path, "missing"),
                root.Path));

        string outside = Path.Combine(other.Path, "outside.txt");
        File.WriteAllText(outside, "outside");
        Assert.Throws<InvalidDataException>(() =>
            LinuxExpectedTargetAuthority.Open(outside, root.Path));

        string real = Path.Combine(root.Path, "real.txt");
        string link = Path.Combine(root.Path, "link.txt");
        File.WriteAllText(real, "real");
        File.CreateSymbolicLink(link, real);
        Assert.Throws<InvalidDataException>(() =>
            LinuxExpectedTargetAuthority.Open(link, root.Path));

        string directory = Path.Combine(root.Path, "directory");
        Directory.CreateDirectory(directory);
        Assert.Throws<InvalidDataException>(() =>
            LinuxExpectedTargetAuthority.Open(directory, root.Path));
    }

    [TestMethod]
    public void Expected_authority_validates_arguments()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        Assert.Throws<ArgumentException>(() =>
            LinuxExpectedTargetAuthority.Open("", root.Path));
        Assert.Throws<ArgumentException>(() =>
            LinuxExpectedTargetAuthority.Open(root.Path, ""));
    }

    [TestMethod]
    public void Case_sensitivity_inspector_rejects_missing_and_blank_directories()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        var inspector = new LinuxDirectoryCaseSensitivityInspector();

        Assert.Throws<ArgumentException>(() => inspector.Inspect(""));
        Assert.Throws<DirectoryNotFoundException>(() =>
            inspector.Inspect(Path.Combine(root.Path, "missing")));
    }

    [TestMethod]
    public void Instance_lock_rejects_symlink_and_dispose_is_idempotent()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var root = new Root();
        var provider = new LinuxAppInstanceLockProvider();

        Assert.Throws<ArgumentException>(() => provider.TryAcquire(""));
        Assert.Throws<ArgumentException>(() => provider.IsExistingLockHeld(""));

        string real = Path.Combine(root.Path, "real.lock");
        string link = Path.Combine(root.Path, "linked.lock");
        File.WriteAllText(real, "");
        File.CreateSymbolicLink(link, real);
        Assert.Throws<IOException>(() => provider.TryAcquire(link));

        string lockPath = Path.Combine(root.Path, ".app.lock");
        IAppInstanceLease? lease = provider.TryAcquire(lockPath);
        Assert.IsNotNull(lease);
        lease!.Dispose();
        lease.Dispose();
    }

    [TestMethod]
    public void Linux_guard_throws_on_non_linux_hosts()
    {
        if (OperatingSystem.IsLinux()) return;

        Assert.Throws<PlatformNotSupportedException>(() =>
            LinuxNativeFileSystem.EnsureLinux());
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class Root : IDisposable
    {
        public Root()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PromptHelper-LinuxNativeCoverage-" + Guid.NewGuid().ToString("N"));
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
