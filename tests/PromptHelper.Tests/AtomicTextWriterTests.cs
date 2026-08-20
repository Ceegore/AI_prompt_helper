using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class AtomicTextWriterTests
{
    [TestMethod]
    public void Write_new_file_creates_content()
    {
        using var testDir = new TestDirectory();
        var writer = new AtomicTextWriter();
        string file = Path.Combine(testDir.Root, "test.txt");

        writer.Write(file, "hello world");

        Assert.IsTrue(File.Exists(file));
        Assert.AreEqual("hello world", File.ReadAllText(file));
    }

    [TestMethod]
    public void Replace_existing_file_changes_content()
    {
        using var testDir = new TestDirectory();
        var writer = new AtomicTextWriter();
        string file = Path.Combine(testDir.Root, "test.txt");

        writer.Write(file, "initial");
        writer.Write(file, "updated");

        Assert.AreEqual("updated", File.ReadAllText(file));
    }

    [TestMethod]
    public void Unicode_round_trip()
    {
        using var testDir = new TestDirectory();
        var writer = new AtomicTextWriter();
        string file = Path.Combine(testDir.Root, "unicode.txt");
        string unicodeContent = "ä ö ü Ä Ö Ü ß\n日本語\n한국어\n中文\nРусский\n🚀 ✅ ❌";

        writer.Write(file, unicodeContent);

        Assert.AreEqual(unicodeContent, File.ReadAllText(file));
    }

    [TestMethod]
    public void Markdown_round_trip()
    {
        using var testDir = new TestDirectory();
        var writer = new AtomicTextWriter();
        string file = Path.Combine(testDir.Root, "prompt.md");
        string markdownContent = "# ROLE\r\n\r\nYou are an AI agent.\r\n\r\n```json\r\n{\r\n  \"test\": true\r\n}\r\n```\r\n\r\nEnd.";

        writer.Write(file, markdownContent);

        Assert.AreEqual(markdownContent, File.ReadAllText(file));
    }

    [TestMethod]
    public void No_tmp_left_after_success()
    {
        using var testDir = new TestDirectory();
        var writer = new AtomicTextWriter();
        string file = Path.Combine(testDir.Root, "test.txt");

        writer.Write(file, "clean content");

        var tmpFiles = Directory.GetFiles(testDir.Root, "*.tmp");
        Assert.AreEqual(0, tmpFiles.Length);
    }

    [TestMethod]
    public void Failed_write_does_not_modify_existing_target()
    {
        using var testDir = new TestDirectory();
        var writer = new AtomicTextWriter();
        string file = Path.Combine(testDir.Root, "locked_target.txt");

        writer.Write(file, "ORIGINAL UNMODIFIED CONTENT");

        // Hold file locked preventing replacement/deletion
        using (var lockStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<IOException>(() => writer.Write(file, "NEW CORRUPTED CONTENT"));
        }

        // Assert original content is completely intact
        Assert.AreEqual("ORIGINAL UNMODIFIED CONTENT", File.ReadAllText(file));

        // Assert temporary file cleanup occurred
        var tmpFiles = Directory.GetFiles(testDir.Root, "*.tmp");
        Assert.AreEqual(0, tmpFiles.Length);
    }
}