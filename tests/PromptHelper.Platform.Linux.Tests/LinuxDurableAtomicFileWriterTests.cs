using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Platform.Linux.Tests;

[TestClass]
public sealed class LinuxDurableAtomicFileWriterTests
{
    [TestMethod]
    public void ReplaceDurable_creates_and_replaces_content_without_temp_leftovers()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        string target = Path.Combine(root, "library.json");
        var writer = new LinuxDurableAtomicFileWriter();

        try
        {
            writer.ReplaceDurable(
                target,
                Encoding.UTF8.GetBytes("first"),
                DurableFileClass.LibraryMetadata);

            CollectionAssert.AreEqual(
                Encoding.UTF8.GetBytes("first"),
                File.ReadAllBytes(target));

            writer.ReplaceDurable(
                target,
                Encoding.UTF8.GetBytes("second"),
                DurableFileClass.LibraryMetadata);

            CollectionAssert.AreEqual(
                Encoding.UTF8.GetBytes("second"),
                File.ReadAllBytes(target));

            Assert.AreEqual(
                0,
                Directory.GetFiles(root, ".prompthelper-tmp-*").Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void CreateNewDurable_refuses_existing_target_without_modifying_it()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        string target = Path.Combine(root, "prompt.md");
        byte[] original = Encoding.UTF8.GetBytes("existing");
        File.WriteAllBytes(target, original);

        try
        {
            var writer = new LinuxDurableAtomicFileWriter();

            Assert.Throws<IOException>(() =>
                writer.CreateNewDurable(
                    target,
                    Encoding.UTF8.GetBytes("replacement"),
                    DurableFileClass.PromptBody));

            CollectionAssert.AreEqual(original, File.ReadAllBytes(target));
            Assert.AreEqual(
                0,
                Directory.GetFiles(root, ".prompthelper-tmp-*").Length,
                "The common pre-existing-target path should not create a staging artifact.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void CreateNewDurable_atomic_gate_preserves_target_created_after_staging()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        string target = Path.Combine(root, "prompt.md");
        byte[] foreign = Encoding.UTF8.GetBytes("foreign");

        try
        {
            LinuxDurableAtomicFileWriter.BeforeCreatePromotionForTests = path =>
                File.WriteAllBytes(path, foreign);

            var writer = new LinuxDurableAtomicFileWriter();

            Assert.Throws<IOException>(() =>
                writer.CreateNewDurable(
                    target,
                    Encoding.UTF8.GetBytes("ours"),
                    DurableFileClass.PromptBody));

            CollectionAssert.AreEqual(
                foreign,
                File.ReadAllBytes(target),
                "RENAME_NOREPLACE must preserve a target created after staging.");

            string[] stages = Directory.GetFiles(root, ".prompthelper-tmp-prompt-*.tmp");
            Assert.AreEqual(
                1,
                stages.Length,
                "A failed publish leaves the app-owned stage for startup reconciliation rather than deleting by pathname after an error.");
        }
        finally
        {
            LinuxDurableAtomicFileWriter.BeforeCreatePromotionForTests = null;
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ReplaceDurable_replaces_symlink_entry_without_touching_link_target()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        string realTarget = Path.Combine(root, "outside.txt");
        string linkPath = Path.Combine(root, "library.json");
        File.WriteAllText(realTarget, "outside");
        File.CreateSymbolicLink(linkPath, realTarget);

        try
        {
            var writer = new LinuxDurableAtomicFileWriter();

            writer.ReplaceDurable(
                linkPath,
                Encoding.UTF8.GetBytes("replacement"),
                DurableFileClass.LibraryMetadata);

            Assert.AreEqual("outside", File.ReadAllText(realTarget));
            Assert.AreEqual("replacement", File.ReadAllText(linkPath));
            Assert.IsNull(
                new FileInfo(linkPath).LinkTarget,
                "The symlink entry itself should have been atomically replaced by the new file.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void CreateNewDurable_refuses_symlink_target_and_preserves_link()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        string realTarget = Path.Combine(root, "outside.txt");
        string linkPath = Path.Combine(root, "prompt.md");
        File.WriteAllText(realTarget, "outside");
        File.CreateSymbolicLink(linkPath, realTarget);

        try
        {
            var writer = new LinuxDurableAtomicFileWriter();

            Assert.Throws<IOException>(() =>
                writer.CreateNewDurable(
                    linkPath,
                    Encoding.UTF8.GetBytes("ours"),
                    DurableFileClass.PromptBody));

            Assert.IsNotNull(new FileInfo(linkPath).LinkTarget);
            Assert.AreEqual("outside", File.ReadAllText(realTarget));
            Assert.AreEqual("outside", File.ReadAllText(linkPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Writer_rejects_non_Linux_use()
    {
        if (OperatingSystem.IsLinux())
        {
            return;
        }

        var writer = new LinuxDurableAtomicFileWriter();
        string target = Path.Combine(Path.GetTempPath(), "prompt-helper-linux-writer-test");

        Assert.Throws<PlatformNotSupportedException>(() =>
            writer.ReplaceDurable(
                target,
                Encoding.UTF8.GetBytes("data"),
                DurableFileClass.PromptBody));
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-LinuxDurable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
