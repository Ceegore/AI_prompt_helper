using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Infrastructure;

namespace PromptHelper.Tests;

[TestClass]
public sealed class TextUtilitiesTests
{
    [TestMethod]
    public void GetTextElementCount_StandardString_ReturnsCorrectCount()
    {
        Assert.AreEqual(5, TextUtilities.GetTextElementCount("Hello"));
    }

    [TestMethod]
    public void GetTextElementCount_Emoji_ReturnsSingleElement()
    {
        // 🚀 is a surrogate pair (2 char units, 1 text element)
        Assert.AreEqual(1, TextUtilities.GetTextElementCount("🚀"));
    }

    [TestMethod]
    public void TruncateWithEllipsis_UnderLimit_ReturnsOriginal()
    {
        string text = "Short text";
        Assert.AreEqual(text, TextUtilities.TruncateWithEllipsis(text, 20));
    }

    [TestMethod]
    public void TruncateWithEllipsis_ExactLimit_ReturnsOriginal()
    {
        string text = "12345";
        Assert.AreEqual(text, TextUtilities.TruncateWithEllipsis(text, 5));
    }

    [TestMethod]
    public void TruncateWithEllipsis_OverLimit_TruncatesAndAppendsEllipsis()
    {
        string text = "1234567890";
        // 5 elements max -> keeps 4 elements + "…"
        Assert.AreEqual("1234…", TextUtilities.TruncateWithEllipsis(text, 5));
    }

    [TestMethod]
    public void TruncateWithEllipsis_EmojiSequence_PreservesGraphemeClusters()
    {
        string text = "🚀🔥✨🎉💡🌟";
        string truncated = TextUtilities.TruncateWithEllipsis(text, 4);
        Assert.AreEqual("🚀🔥✨…", truncated);
    }

    [TestMethod]
    public void TruncateWithEllipsis_LessThanTwoMax_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TextUtilities.TruncateWithEllipsis("Hello", 1));
    }

    [TestMethod]
    public void CreateCompactPreview_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, TextUtilities.CreateCompactPreview(null));
        Assert.AreEqual(string.Empty, TextUtilities.CreateCompactPreview("   \r\n\t  "));
    }

    [TestMethod]
    public void CreateCompactPreview_NormalizesNewlinesAndTabs()
    {
        string input = "Line 1\r\n\tLine 2\n   Line   3";
        string expected = "Line 1 Line 2 Line 3";
        string result = TextUtilities.CreateCompactPreview(input);

        Assert.AreEqual(expected, result);
        // Ensure original string was unmodified
        Assert.IsTrue(input.Contains("\r\n"));
    }

    [TestMethod]
    public void CreateCompactPreview_TruncatesLongTextWithEllipsis()
    {
        string input = new('a', 200);
        string result = TextUtilities.CreateCompactPreview(input, 50);

        Assert.AreEqual(50, TextUtilities.GetTextElementCount(result));
        Assert.IsTrue(result.EndsWith("…"));
    }
}