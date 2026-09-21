using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using PromptHelper.Services;

namespace PromptHelper.Platform.Linux.Tests;

[TestClass]
public sealed class LinuxAtomicExpectedFileReplacerTests
{
    [TestMethod]
    public void Expected_missing_publishes_candidate_atomically()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        string target = Path.Combine(root, "library.json");
        byte[] candidate = Encoding.UTF8.GetBytes("candidate");

        try
        {
            new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                root,
                target,
                ExpectedFileState.Missing,
                candidate,
                DurableFileClass.LibraryMetadata);

            CollectionAssert.AreEqual(candidate, File.ReadAllBytes(target));
            Assert.AreEqual(0, Directory.GetFiles(root, ".prompthelper-tmp-*").Length);
        }
        finally
        {
            ResetHooks();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Expected_missing_preserves_target_created_at_last_moment()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        string target = Path.Combine(root, "library.json");
        byte[] foreign = Encoding.UTF8.GetBytes("foreign");

        try
        {
            LinuxAtomicExpectedFileReplacer.PreSwapBarrierForTests =
                path => File.WriteAllBytes(path, foreign);

            Assert.Throws<StaleExpectedFileException>(() =>
                new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                    root,
                    target,
                    ExpectedFileState.Missing,
                    Encoding.UTF8.GetBytes("ours"),
                    DurableFileClass.LibraryMetadata));

            CollectionAssert.AreEqual(foreign, File.ReadAllBytes(target));
        }
        finally
        {
            ResetHooks();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Expected_present_replaces_verified_content_and_retires_preimage()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        string target = Path.Combine(root, "library.json");
        byte[] original = Encoding.UTF8.GetBytes("original");
        byte[] candidate = Encoding.UTF8.GetBytes("candidate");
        File.WriteAllBytes(target, original);

        try
        {
            new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                root,
                target,
                ExpectedFileState.Present(Hash(original)),
                candidate,
                DurableFileClass.LibraryMetadata);

            CollectionAssert.AreEqual(candidate, File.ReadAllBytes(target));
            Assert.AreEqual(0, Directory.GetFiles(root, ".prompthelper-preimage-*").Length);
            Assert.AreEqual(0, Directory.GetFiles(root, ".prompthelper-tmp-*").Length);
        }
        finally
        {
            ResetHooks();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Atomic_external_replace_at_preswap_barrier_is_restored_and_never_overwritten()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        string target = Path.Combine(root, "library.json");
        byte[] original = Encoding.UTF8.GetBytes("original");
        byte[] foreign = Encoding.UTF8.GetBytes("foreign");
        File.WriteAllBytes(target, original);

        try
        {
            LinuxAtomicExpectedFileReplacer.PreSwapBarrierForTests =
                path => AtomicExternalReplace(path, foreign);

            Assert.Throws<StaleExpectedFileException>(() =>
                new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                    root,
                    target,
                    ExpectedFileState.Present(Hash(original)),
                    Encoding.UTF8.GetBytes("ours"),
                    DurableFileClass.LibraryMetadata));

            CollectionAssert.AreEqual(
                foreign,
                File.ReadAllBytes(target),
                "The concurrent atomic replacement must be restored to its pathname.");
        }
        finally
        {
            ResetHooks();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void In_place_write_at_preswap_barrier_is_detected_by_post_rename_hash()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        string target = Path.Combine(root, "library.json");
        byte[] original = Encoding.UTF8.GetBytes("original");
        byte[] foreign = Encoding.UTF8.GetBytes("in-place update");
        File.WriteAllBytes(target, original);

        try
        {
            LinuxAtomicExpectedFileReplacer.PreSwapBarrierForTests =
                path => File.WriteAllBytes(path, foreign);

            Assert.Throws<StaleExpectedFileException>(() =>
                new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                    root,
                    target,
                    ExpectedFileState.Present(Hash(original)),
                    Encoding.UTF8.GetBytes("ours"),
                    DurableFileClass.LibraryMetadata));

            CollectionAssert.AreEqual(
                foreign,
                File.ReadAllBytes(target),
                "The same-inode concurrent write must survive the failed CAS.");
        }
        finally
        {
            ResetHooks();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Target_created_after_sideline_is_preserved_with_previous_content_in_preimage()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        string target = Path.Combine(root, "library.json");
        byte[] original = Encoding.UTF8.GetBytes("original");
        byte[] foreign = Encoding.UTF8.GetBytes("foreign after sideline");
        File.WriteAllBytes(target, original);

        try
        {
            LinuxAtomicExpectedFileReplacer.BeforeCandidatePromotionForTests =
                path => File.WriteAllBytes(path, foreign);

            Assert.Throws<StaleExpectedFileException>(() =>
                new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                    root,
                    target,
                    ExpectedFileState.Present(Hash(original)),
                    Encoding.UTF8.GetBytes("ours"),
                    DurableFileClass.LibraryMetadata));

            CollectionAssert.AreEqual(foreign, File.ReadAllBytes(target));

            string[] preimages =
                Directory.GetFiles(root, ".prompthelper-preimage-library.json-*.tmp");
            Assert.AreEqual(1, preimages.Length);
            CollectionAssert.AreEqual(
                original,
                File.ReadAllBytes(preimages[0]),
                "The previous committed content must remain recoverable.");
        }
        finally
        {
            ResetHooks();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Exact_identity_rejects_same_byte_replacement_object()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        string target = Path.Combine(root, "library.json");
        byte[] bytes = Encoding.UTF8.GetBytes("same bytes");
        File.WriteAllBytes(target, bytes);

        try
        {
            LinuxFileIdentity identity = IdentityOf(target);

            File.Delete(target);
            File.WriteAllBytes(target, bytes);

            Assert.Throws<StaleExpectedFileException>(() =>
                new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                    root,
                    target,
                    ExpectedFileState.Present(Hash(bytes), identity),
                    Encoding.UTF8.GetBytes("candidate"),
                    DurableFileClass.LibraryMetadata));

            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(target));
        }
        finally
        {
            ResetHooks();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Symlink_target_is_refused_without_touching_link_destination()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        string outside = Path.Combine(root, "outside.json");
        string link = Path.Combine(root, "library.json");
        byte[] outsideBytes = Encoding.UTF8.GetBytes("outside");
        File.WriteAllBytes(outside, outsideBytes);
        File.CreateSymbolicLink(link, outside);

        try
        {
            Assert.Throws<InvalidDataException>(() =>
                new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                    root,
                    link,
                    ExpectedFileState.Present(Hash(outsideBytes)),
                    Encoding.UTF8.GetBytes("candidate"),
                    DurableFileClass.LibraryMetadata));

            CollectionAssert.AreEqual(outsideBytes, File.ReadAllBytes(outside));
            Assert.IsNotNull(new FileInfo(link).LinkTarget);
        }
        finally
        {
            ResetHooks();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Target_outside_physical_root_is_refused()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        string other = CreateRoot();
        string target = Path.Combine(other, "library.json");
        byte[] original = Encoding.UTF8.GetBytes("outside root");
        File.WriteAllBytes(target, original);

        try
        {
            Assert.Throws<InvalidDataException>(() =>
                new LinuxAtomicExpectedFileReplacer().ReplaceIfExpected(
                    root,
                    target,
                    ExpectedFileState.Present(Hash(original)),
                    Encoding.UTF8.GetBytes("candidate"),
                    DurableFileClass.LibraryMetadata));

            CollectionAssert.AreEqual(original, File.ReadAllBytes(target));
        }
        finally
        {
            ResetHooks();
            Directory.Delete(root, recursive: true);
            Directory.Delete(other, recursive: true);
        }
    }

    private static LinuxFileIdentity IdentityOf(string path)
    {
        using SafeFileHandle handle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        return LinuxFileIdentity.FromHandle(handle);
    }

    private static void AtomicExternalReplace(string target, byte[] bytes)
    {
        string scratch = Path.Combine(
            Path.GetDirectoryName(target)!,
            $"external-{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(scratch, bytes);
        File.Move(scratch, target, overwrite: true);
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string CreateRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-LinuxCAS-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void ResetHooks()
    {
        LinuxAtomicExpectedFileReplacer.PreSwapBarrierForTests = null;
        LinuxAtomicExpectedFileReplacer.BeforeCandidatePromotionForTests = null;
    }
}
