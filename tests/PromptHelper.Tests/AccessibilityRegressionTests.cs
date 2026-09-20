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
        StringAssert.Contains(code, "SystemParameters.StaticPropertyChanged +=");
        StringAssert.Contains(code, "RefreshSystemContrast");

        string themeService = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "src", "PromptHelper", "Services", "WpfThemeService.cs"));
        StringAssert.Contains(themeService, "DarkPalette");
        StringAssert.Contains(themeService, "SystemParameters.HighContrast");
    }

    [TestMethod]
    public void Dialogs_expose_headings_labels_and_live_validation_to_assistive_technology()
    {
        string editor = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "src", "PromptHelper", "Views", "PromptEditorDialog.xaml"));
        string name = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "src", "PromptHelper", "Views", "NameDialog.xaml"));
        string settings = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "src", "PromptHelper", "Views", "SettingsDialog.xaml"));
        string move = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "src", "PromptHelper", "Views", "MovePromptDialog.xaml"));
        string delete = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "src", "PromptHelper", "Views", "ConfirmDeleteDialog.xaml"));

        StringAssert.Contains(editor, "AutomationProperties.LabeledBy=\"{Binding ElementName=HeadlineLabel}\"");
        StringAssert.Contains(editor, "AutomationProperties.Name=\"Prompt text\"");
        StringAssert.Contains(name, "AutomationProperties.LiveSetting=\"Assertive\"");
        StringAssert.Contains(settings, "AutomationProperties.LabeledBy=\"{Binding ElementName=DataFolderLabel}\"");
        StringAssert.Contains(settings, "AutomationProperties.Name=\"Dark mode\"");
        StringAssert.Contains(settings, "Style=\"{StaticResource ToggleSwitchStyle}\"");
        StringAssert.Contains(move, "AutomationProperties.LabeledBy=\"{Binding ElementName=DestinationLabel}\"");

        foreach (string dialog in new[] { editor, name, settings, move, delete })
        {
            StringAssert.Contains(dialog, "AutomationProperties.HeadingLevel=\"Level1\"");
        }
    }

    [TestMethod]
    public void Palette_consumers_use_dynamic_resources_for_live_theme_updates()
    {
        string sourceRoot = RepositoryTestPaths.RequireFile("src", "PromptHelper", "App.xaml");
        string projectRoot = Path.GetDirectoryName(sourceRoot)!;
        string[] xamlFiles = Directory.GetFiles(projectRoot, "*.xaml", SearchOption.AllDirectories);

        string[] paletteKeys =
        [
            "AppBackgroundBrush", "SurfaceBrush", "TextPrimaryBrush", "TextSecondaryBrush",
            "SubtleTextBrush", "BorderBrush", "BorderHoverBrush", "AccentBrush",
            "AccentHoverBrush", "AccentPressedBrush", "AccentLightBrush", "SecondaryHoverBrush",
            "DangerBrush", "DangerLightBrush", "DangerBorderBrush"
        ];

        foreach (string file in xamlFiles)
        {
            string xaml = File.ReadAllText(file);
            foreach (string key in paletteKeys)
            {
                Assert.IsFalse(
                    xaml.Contains($"{{StaticResource {key}}}", StringComparison.Ordinal),
                    $"{file} still freezes live palette resource {key}.");
            }
        }
    }
}
