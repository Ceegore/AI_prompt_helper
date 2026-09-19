using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PromptHelper.Tests;

[TestClass]
public sealed class AccessibilityRegressionTests
{
    [TestMethod]
    public void Theme_uses_accessible_normal_text_colors_and_keeps_default_focus_visuals()
    {
        string theme = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "src", "PromptHelper", "Styles", "Theme.xaml"));

        StringAssert.Contains(theme, "<Color x:Key=\"TextSecondaryColor\">#596170</Color>");
        StringAssert.Contains(theme, "<Color x:Key=\"SubtleTextColor\">#5E6675</Color>");
        Assert.IsFalse(theme.Contains("FocusVisualStyle\" Value=\"{x:Null}"));
        StringAssert.Contains(theme, "RecognizesAccessKey=\"True\"");
    }

    [TestMethod]
    public void Main_window_exposes_contextual_automation_names_headings_and_keyboard_entry_points()
    {
        string xaml = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "src", "PromptHelper", "MainWindow.xaml"));
        string code = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "src", "PromptHelper", "MainWindow.xaml.cs"));

        StringAssert.Contains(xaml, "AutomationProperties.HeadingLevel=\"Level1\"");
        StringAssert.Contains(xaml, "AutomationProperties.HeadingLevel=\"Level2\"");
        StringAssert.Contains(xaml, "StringFormat='Delete prompt: {0}'");
        StringAssert.Contains(xaml, "StringFormat='Edit prompt: {0}'");
        StringAssert.Contains(xaml, "StringFormat='Move prompt: {0}'");
        StringAssert.Contains(xaml, "StringFormat='Copy prompt: {0}'");
        StringAssert.Contains(xaml, "PreviewKeyDown=\"Window_PreviewKeyDown\"");
        StringAssert.Contains(code, "ModifierKeys.Control | ModifierKeys.Shift");
        StringAssert.Contains(code, "Key.OemComma");
        StringAssert.Contains(code, "ModifierKeys.Alt");
    }

    [TestMethod]
    public void Application_maps_theme_resources_to_windows_high_contrast_colors()
    {
        string code = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "src", "PromptHelper", "App.xaml.cs"));

        StringAssert.Contains(code, "SystemParameters.HighContrast");
        StringAssert.Contains(code, "SystemColors.WindowColor");
        StringAssert.Contains(code, "SystemColors.WindowTextColor");
        StringAssert.Contains(code, "SystemColors.HighlightColor");
    }
}
