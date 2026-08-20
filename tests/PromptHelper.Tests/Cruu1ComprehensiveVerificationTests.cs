using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Infrastructure;
using PromptHelper.Models;
using PromptHelper.Services;
using PromptHelper.ViewModels;
using PromptHelper.Views;

namespace PromptHelper.Tests;

[TestClass]
public sealed class Cruu1ComprehensiveVerificationTests
{
    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    _ = new Application();
                }

                if (Application.Current!.Resources.MergedDictionaries.Count == 0)
                {
                    Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/PromptHelper;component/Styles/Theme.xaml", UriKind.Absolute)
                    });
                }

                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            throw new AggregateException("STA Thread Action Failed", exception);
        }
    }

    #region Feature 1 — Optional Editor Line Wrapping

    [TestMethod]
    public void Feature1_EditorDialog_WrapCheckbox_IsUncheckedByDefault_And_VisualOnly()
    {
        RunOnStaThread(() =>
        {
            string originalText = "Line 1 with a very long sentence that exceeds typical dialog widths.\r\nLine 2 with \ttabs and spaces.\nLine 3.";
            var dialog = new PromptEditorDialog("Create Prompt", originalText, "Headline");

            // Find EditorTextBox and CheckBox
            var textBox = (TextBox)dialog.FindName("EditorTextBox");
            var checkBox = (CheckBox)dialog.FindName("WrapLinesCheckBox");

            Assert.IsNotNull(textBox);
            Assert.IsNotNull(checkBox);

            // Default state: Unchecked -> NoWrap, ScrollBarVisibility.Auto
            Assert.IsFalse(checkBox.IsChecked == true);
            Assert.AreEqual(TextWrapping.NoWrap, textBox.TextWrapping);
            Assert.AreEqual(ScrollBarVisibility.Auto, textBox.HorizontalScrollBarVisibility);

            // Toggle Checked -> Wrap, ScrollBarVisibility.Disabled
            checkBox.IsChecked = true;
            Assert.AreEqual(TextWrapping.Wrap, textBox.TextWrapping);
            Assert.AreEqual(ScrollBarVisibility.Disabled, textBox.HorizontalScrollBarVisibility);

            // Verify text content was NOT mutated
            Assert.AreEqual(originalText, textBox.Text);

            // Toggle Unchecked again
            checkBox.IsChecked = false;
            Assert.AreEqual(TextWrapping.NoWrap, textBox.TextWrapping);
            Assert.AreEqual(ScrollBarVisibility.Auto, textBox.HorizontalScrollBarVisibility);
            Assert.AreEqual(originalText, textBox.Text);
        });
    }

    #endregion

    #region Feature 2 — Editable Headline with Automatic Fallback

    [TestMethod]
    public void Feature2_Headline_Hierarchy_And_EditableHeadline_Prefill()
    {
        // 1. Custom Title present
        var cardWithCustomTitle = new PromptCardViewModel(
            Guid.NewGuid(),
            "  My Custom Title  ",
            "First Line of Body\nSecond Line",
            true,
            null);

        Assert.AreEqual("My Custom Title", cardWithCustomTitle.CustomTitle);
        Assert.AreEqual("My Custom Title", cardWithCustomTitle.PreviewTitle);
        Assert.AreEqual("My Custom Title", cardWithCustomTitle.EditableHeadline);

        // 2. Custom Title null -> falls back to first non-empty line
        var cardWithNullTitle = new PromptCardViewModel(
            Guid.NewGuid(),
            null,
            "\r\n  \nFirst Non-Empty Line\nSecond Line",
            true,
            null);

        Assert.IsNull(cardWithNullTitle.CustomTitle);
        Assert.AreEqual("First Non-Empty Line", cardWithNullTitle.PreviewTitle);
        Assert.AreEqual("First Non-Empty Line", cardWithNullTitle.EditableHeadline);

        // 3. Custom Title whitespace -> normalized to null, falls back to first non-empty line
        var cardWithWhitespaceTitle = new PromptCardViewModel(
            Guid.NewGuid(),
            "   \t\r\n  ",
            "Body First Line\nBody Second Line",
            true,
            null);

        Assert.IsNull(cardWithWhitespaceTitle.CustomTitle);
        Assert.AreEqual("Body First Line", cardWithWhitespaceTitle.PreviewTitle);
        Assert.AreEqual("Body First Line", cardWithWhitespaceTitle.EditableHeadline);

        // 4. Unavailable prompt fallback
        var unavailableCard = new PromptCardViewModel(
            Guid.NewGuid(),
            null,
            "Unreadable content",
            false,
            "File read error");

        Assert.AreEqual("(Unavailable prompt)", unavailableCard.PreviewTitle);
        Assert.AreEqual("(Unavailable prompt)", unavailableCard.EditableHeadline);

        // 5. Empty prompt fallback
        var emptyCard = new PromptCardViewModel(
            Guid.NewGuid(),
            null,
            "   \r\n\t  ",
            true,
            null);

        Assert.AreEqual("(Empty prompt)", emptyCard.PreviewTitle);
        Assert.AreEqual("(Empty prompt)", emptyCard.EditableHeadline);
    }

    [TestMethod]
    public void Feature2_Clearing_Custom_Headline_Reverts_To_Automatic_Mode()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startup = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);
        var initResult = startup.LoadOrInitialize();
        var service = new PromptLibraryService(initResult.Document, libRepo, promptRepo);

        // Create with custom headline
        var prompt = service.CreatePrompt(null, "Line 1 Auto Title\nLine 2", "Custom Title").Value;
        Assert.AreEqual("Custom Title", prompt.Title);
        Assert.AreEqual("Custom Title", service.GetPrompts(null).Single(p => p.Id == prompt.Id).Title);

        // Edit and clear custom headline (pass empty / whitespace string)
        service.EditPrompt(prompt.Id, "Line 1 Auto Title\nLine 2", "   ");
        var updated = service.GetPrompts(null).Single(p => p.Id == prompt.Id);
        Assert.IsNull(updated.Title);

        // Card derived from updated record uses first line
        var card = new PromptCardViewModel(updated.Id, updated.Title, updated.Content, updated.IsContentAvailable, updated.LoadError);
        Assert.AreEqual("Line 1 Auto Title", card.PreviewTitle);
    }

    #endregion

    #region Feature 3 — Three-Column Grid & Delayed Hover Tooltip

    [TestMethod]
    public void Feature3_ThemeResources_Contain_PromptGrid_And_Compact_Styles()
    {
        RunOnStaThread(() =>
        {
            var appDict = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/PromptHelper;component/Styles/Theme.xaml", UriKind.Absolute)
            };

            Assert.IsTrue(appDict.Contains("PromptGridListBoxItemStyle"));
            Assert.IsTrue(appDict.Contains("CompactActionButtonStyle"));
            Assert.IsTrue(appDict.Contains("RecentPromptTileStyle"));
            Assert.IsTrue(appDict.Contains("CompactCopyButtonStyle"));

            var gridItemStyle = (Style)appDict["PromptGridListBoxItemStyle"];
            Assert.AreEqual(typeof(ListBoxItem), gridItemStyle.TargetType);

            var compactBtnStyle = (Style)appDict["CompactActionButtonStyle"];
            Assert.AreEqual(typeof(Button), compactBtnStyle.TargetType);
        });
    }

    #endregion

    #region Feature 4 — Settings Dialog, Native Folder Picker, Safe Migration & Author Attribution

    [TestMethod]
    public void Feature4_SettingsDialog_Contains_Exact_Author_Attribution_And_Title()
    {
        RunOnStaThread(() =>
        {
            using var testDir = new TestDirectory();
            var writer = new AtomicTextWriter();
            var settingsRepo = new AppSettingsRepository(writer, Path.Combine(testDir.Root, "settings.json"));
            var migrationService = new DataFolderMigrationService();

            var dialog = new SettingsDialog(testDir.Root, settingsRepo, migrationService);

            Assert.AreEqual("Tools & Settings — Prompt Helper", dialog.Title);

            // Verify the XAML contains the exact text "Made by CeeGore"
            string xamlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "PromptHelper", "Views", "SettingsDialog.xaml");
            if (File.Exists(xamlPath))
            {
                string xamlContent = File.ReadAllText(xamlPath);
                Assert.IsTrue(xamlContent.Contains("Made by CeeGore"), "SettingsDialog.xaml must contain exact string 'Made by CeeGore'");
            }
        });
    }

    [TestMethod]
    public void Feature4_DataFolderMigration_CaseInsensitive_And_TrailingSlashes_Handled_Safely()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        paths.EnsureRootDirectory();
        paths.EnsureDataDirectories();

        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startup = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);
        startup.LoadOrInitialize();

        var migration = new DataFolderMigrationService();

        // Same path with different case or trailing slashes
        string pathWithSlash = testDir.Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string pathUpper = testDir.Root.ToUpperInvariant();

        var result = migration.PrepareTarget(pathWithSlash, pathUpper);
        Assert.IsFalse(result.Copied);
        Assert.IsFalse(result.ExistingLibraryFound);
    }

    #endregion

    #region Feature 5 — Category Action Overlay

    [TestMethod]
    public void Feature5_MainWindow_Has_CategoryActionsButton_With_ContextMenu()
    {
        string xamlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "PromptHelper", "MainWindow.xaml");
        if (File.Exists(xamlPath))
        {
            string xamlContent = File.ReadAllText(xamlPath);
            Assert.IsTrue(xamlContent.Contains("x:Name=\"CategoryActionsButton\""));
            Assert.IsTrue(xamlContent.Contains("Content=\"🔧\""));
            Assert.IsTrue(xamlContent.Contains("Header=\"✎ Rename\""));
            Assert.IsTrue(xamlContent.Contains("Header=\"× Delete\""));
        }
    }

    #endregion

    #region Feature 6 — Session-Only Recent-Copy Quick Bar

    [TestMethod]
    public void Feature6_RecentPrompts_Maintains_MaxThree_And_No_Persistence()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startup = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);
        var initResult = startup.LoadOrInitialize();
        var service = new PromptLibraryService(initResult.Document, libRepo, promptRepo);
        var vm = new MainViewModel(service, promptRepo, paths.RootDirectory);

        // Starts empty
        Assert.AreEqual(0, vm.RecentPrompts.Count);

        var p1 = service.CreatePrompt(null, "Prompt 1 content", "Headline 1").Value;
        var p2 = service.CreatePrompt(null, "Prompt 2 content", "Headline 2").Value;
        var p3 = service.CreatePrompt(null, "Prompt 3 content", "Headline 3").Value;
        var p4 = service.CreatePrompt(null, "Prompt 4 content", "Headline 4").Value;

        // Copy p1, p2, p3
        vm.RecordSuccessfulPromptCopy(p1.Id, "Headline 1", "Prompt 1 content");
        vm.RecordSuccessfulPromptCopy(p2.Id, "Headline 2", "Prompt 2 content");
        vm.RecordSuccessfulPromptCopy(p3.Id, "Headline 3", "Prompt 3 content");

        Assert.AreEqual(3, vm.RecentPrompts.Count);
        Assert.AreEqual(p3.Id, vm.RecentPrompts[0].Id);
        Assert.AreEqual(p2.Id, vm.RecentPrompts[1].Id);
        Assert.AreEqual(p1.Id, vm.RecentPrompts[2].Id);

        // Copy p4 -> evicts p1
        vm.RecordSuccessfulPromptCopy(p4.Id, "Headline 4", "Prompt 4 content");
        Assert.AreEqual(3, vm.RecentPrompts.Count);
        Assert.AreEqual(p4.Id, vm.RecentPrompts[0].Id);
        Assert.AreEqual(p3.Id, vm.RecentPrompts[1].Id);
        Assert.AreEqual(p2.Id, vm.RecentPrompts[2].Id);
        Assert.IsFalse(vm.RecentPrompts.Any(x => x.Id == p1.Id));

        // Re-copy p2 -> moves p2 to index 0, order becomes [p2, p4, p3]
        vm.RecordSuccessfulPromptCopy(p2.Id, "Headline 2", "Prompt 2 content");
        Assert.AreEqual(3, vm.RecentPrompts.Count);
        Assert.AreEqual(p2.Id, vm.RecentPrompts[0].Id);
        Assert.AreEqual(p4.Id, vm.RecentPrompts[1].Id);
        Assert.AreEqual(p3.Id, vm.RecentPrompts[2].Id);

        // Restarting session creates fresh MainViewModel which starts with empty recent prompts
        var vmNewSession = new MainViewModel(service, promptRepo, paths.RootDirectory);
        Assert.AreEqual(0, vmNewSession.RecentPrompts.Count);
    }

    #endregion

    #region Feature 7 — Application Icon Script Verification

    [TestMethod]
    public void Feature7_GenerateAppIcon_Script_Exists_And_Validates_Parameters()
    {
        string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "tools", "GenerateAppIcon.ps1");
        Assert.IsTrue(File.Exists(scriptPath), "tools/GenerateAppIcon.ps1 must exist");

        string scriptContent = File.ReadAllText(scriptPath);
        Assert.IsTrue(scriptContent.Contains("PromptHelperLogo.svg"));
        Assert.IsTrue(scriptContent.Contains("PromptHelper.ico"));
        Assert.IsTrue(scriptContent.Contains("magick"));
    }

    #endregion
}
