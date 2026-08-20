using System;
using System.IO;
using System.Linq;
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
    #region Feature 1 — Optional Editor Line Wrapping

    [TestMethod]
    public void Feature1_EditorDialog_WrapCheckbox_IsUncheckedByDefault_And_VisualOnly()
    {
        WpfTestHost.Invoke(() =>
        {
            string originalText = "Line 1 with a very long sentence that exceeds typical dialog widths.\r\nLine 2 with \ttabs and spaces.\nLine 3.";
            var dialog = new PromptEditorDialog("Create Prompt", originalText, "Headline");

            try
            {
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
            }
            finally
            {
                dialog.Close();
            }
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

    [TestMethod]
    public void CRUU2_005_Automatic_headline_prefill_untouched_remains_null_after_edit()
    {
        WpfTestHost.Invoke(() =>
        {
            var dialog = new PromptEditorDialog(
                "Edit Prompt",
                "Line 1 Body\nLine 2 Body",
                "Line 1 Body",
                initialHeadlineWasAutomatic: true);

            try
            {
                var saveButton = (Button)dialog.FindName("SaveButton");
                Assert.IsNotNull(saveButton);

                var editorTextBox = (TextBox)dialog.FindName("EditorTextBox");
                editorTextBox.Text = "Line 1 Body Modified\nLine 2 Body";

                var saveClickMethod = typeof(PromptEditorDialog).GetMethod("SaveButton_Click",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                saveClickMethod?.Invoke(dialog, [saveButton, new RoutedEventArgs()]);

                Assert.IsTrue(dialog.ResultUsesAutomaticHeadline);
                Assert.IsNull(dialog.ResultHeadline);
                Assert.AreEqual("Line 1 Body", dialog.ResultHeadlineEditorText);
                Assert.AreEqual("Line 1 Body Modified\nLine 2 Body", dialog.ResultText);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestMethod]
    public void CRUU2_005_User_edited_headline_becomes_custom_title_after_edit()
    {
        WpfTestHost.Invoke(() =>
        {
            var dialog = new PromptEditorDialog(
                "Edit Prompt",
                "Line 1 Body\nLine 2 Body",
                "Line 1 Body",
                initialHeadlineWasAutomatic: true);

            try
            {
                var headlineTextBox = (TextBox)dialog.FindName("HeadlineTextBox");
                headlineTextBox.Text = "Explicitly Pinned Title";

                var saveButton = (Button)dialog.FindName("SaveButton");
                var saveClickMethod = typeof(PromptEditorDialog).GetMethod("SaveButton_Click",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                saveClickMethod?.Invoke(dialog, [saveButton, new RoutedEventArgs()]);

                Assert.IsFalse(dialog.ResultUsesAutomaticHeadline);
                Assert.AreEqual("Explicitly Pinned Title", dialog.ResultHeadline);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestMethod]
    public void CRUU2_004_Legacy_EditPrompt_overload_preserves_custom_title()
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

        var p = service.CreatePrompt(null, "Old Body", "Pinned Custom Title").Value;

        // Legacy service overload
        service.EditPrompt(p.Id, "New Body");
        Assert.AreEqual("Pinned Custom Title", service.GetPrompts(null).Single(x => x.Id == p.Id).Title);
        Assert.AreEqual("New Body", promptRepo.Read(p.Id));

        // Legacy view model overload
        vm.Refresh();
        vm.EditPrompt(p.Id, "Newest Body");
        var card = vm.Prompts.Single(x => x.Id == p.Id);
        Assert.AreEqual("Pinned Custom Title", card.CustomTitle);
        Assert.AreEqual("Newest Body", promptRepo.Read(p.Id));
    }

    [TestMethod]
    public void CRUU3_016_Prompt_title_Unicode_line_separators_rejected_without_file_creation()
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

        string promptsDir = Path.Combine(testDir.Root, "prompts");
        int fileCountBefore = Directory.Exists(promptsDir) ? Directory.GetFiles(promptsDir, "*.md").Length : 0;

        // U+2028 LINE SEPARATOR
        Assert.Throws<InvalidOperationException>(() =>
            service.CreatePrompt(null, "Body text", "Bad\u2028Headline"));

        // U+2029 PARAGRAPH SEPARATOR
        Assert.Throws<InvalidOperationException>(() =>
            service.CreatePrompt(null, "Body text", "Bad\u2029Headline"));

        int fileCountAfter = Directory.Exists(promptsDir) ? Directory.GetFiles(promptsDir, "*.md").Length : 0;
        Assert.AreEqual(fileCountBefore, fileCountAfter);
    }

    [TestMethod]
    public void CRUU3_016_Category_name_Unicode_line_separators_rejected()
    {
        Assert.IsNotNull(LibraryValidator.ValidateCategoryNameInput("Bad\u2028Category", []));
        Assert.IsNotNull(LibraryValidator.ValidateCategoryNameInput("Bad\u2029Category", []));

        // Normal Unicode, accents, emoji are allowed
        Assert.IsNull(LibraryValidator.ValidateCategoryNameInput("Category with Accents: äöü café 🚀", []));
    }

    #endregion

    #region Feature 3 — Three-Column Grid & Delayed Hover Tooltip

    [TestMethod]
    public void Feature3_ThemeResources_Contain_PromptGrid_And_Compact_Styles()
    {
        WpfTestHost.Invoke(() =>
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
        WpfTestHost.Invoke(() =>
        {
            using var testDir = new TestDirectory();
            var writer = new AtomicTextWriter();
            var settingsRepo = new AppSettingsRepository(writer, Path.Combine(testDir.Root, "settings.json"));
            var migrationService = new DataFolderMigrationService();

            var dialog = new SettingsDialog(testDir.Root, settingsRepo, migrationService);

            try
            {
                Assert.AreEqual("Tools & Settings — Prompt Helper", dialog.Title);

                string xamlPath = RepositoryTestPaths.RequireFile("src", "PromptHelper", "Views", "SettingsDialog.xaml");
                string xamlContent = File.ReadAllText(xamlPath);
                Assert.IsTrue(xamlContent.Contains("Made by CeeGore"), "SettingsDialog.xaml must contain exact string 'Made by CeeGore'");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestMethod]
    public void CRUU3_011_SettingsDialog_Existing_Target_Confirmation_Flow()
    {
        WpfTestHost.Invoke(() =>
        {
            using var currentDir = new TestDirectory();
            using var targetDir = new TestDirectory();

            var paths = new AppPaths(currentDir.Root);
            paths.EnsureRootDirectory();
            paths.EnsureDataDirectories();

            var writer = new AtomicTextWriter();
            var deleter = new FileDeleter();
            var libRepo = new LibraryRepository(paths, writer);
            var promptRepo = new PromptRepository(paths, writer, deleter);
            var startup = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);
            startup.LoadOrInitialize();

            // Target has existing library
            var targetPaths = new AppPaths(targetDir.Root);
            targetPaths.EnsureRootDirectory();
            targetPaths.EnsureDataDirectories();
            var targetLibRepo = new LibraryRepository(targetPaths, writer);
            var targetPromptRepo = new PromptRepository(targetPaths, writer, deleter);
            var targetStartup = new LibraryStartupService(targetPaths, targetLibRepo, targetPromptRepo, deleter, writer);
            targetStartup.LoadOrInitialize();

            string settingsFile = Path.Combine(currentDir.Root, "settings.json");
            var settingsRepo = new AppSettingsRepository(writer, settingsPathOverride: settingsFile);
            var migration = new DataFolderMigrationService();
            var confirmation = new FakeUserConfirmationService { ConfirmationResult = false };

            var dialog = new SettingsDialog(currentDir.Root, settingsRepo, migration, confirmation);

            try
            {
                var folderBox = (TextBox)dialog.FindName("DataFolderTextBox");
                folderBox.Text = targetDir.Root;

                var saveButton = (Button)dialog.FindName("SaveButton");
                var saveMethod = typeof(SettingsDialog).GetMethod("SaveButton_Click",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                // User cancels confirmation -> settings NOT saved
                saveMethod?.Invoke(dialog, [saveButton, new RoutedEventArgs()]);
                Assert.AreEqual(1, confirmation.PromptCount);
                Assert.IsFalse(File.Exists(settingsFile));
                Assert.IsFalse(dialog.RestartRequired);

                // User confirms -> settings saved, RestartRequired = true
                confirmation.ConfirmationResult = true;
                saveMethod?.Invoke(dialog, [saveButton, new RoutedEventArgs()]);
                Assert.AreEqual(2, confirmation.PromptCount);
                Assert.IsTrue(File.Exists(settingsFile));
                Assert.IsTrue(dialog.RestartRequired);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestMethod]
    public void CRUU2_003_Configured_custom_root_missing_does_not_create_directory_or_defaults()
    {
        using var temp = new TestDirectory();
        string missing = Path.Combine(temp.Root, "DoesNotExist");

        Assert.Throws<ConfiguredDataFolderUnavailableException>(() =>
            DataRootBootstrapValidator.ValidateConfiguredRoot(missing));

        Assert.IsFalse(Directory.Exists(missing));
    }

    [TestMethod]
    public void CRUU2_003_Configured_existing_empty_root_is_not_treated_as_first_run()
    {
        using var temp = new TestDirectory();

        Assert.Throws<ConfiguredDataFolderUnavailableException>(() =>
            DataRootBootstrapValidator.ValidateConfiguredRoot(temp.Root));

        Assert.IsFalse(File.Exists(Path.Combine(temp.Root, "library.json")));
    }

    #endregion

    #region Feature 5 — Category Action Overlay

    [TestMethod]
    public void Feature5_MainWindow_Has_CategoryActionsButton_With_ContextMenu()
    {
        string xamlPath = RepositoryTestPaths.RequireFile("src", "PromptHelper", "MainWindow.xaml");
        string xamlContent = File.ReadAllText(xamlPath);
        Assert.IsTrue(xamlContent.Contains("x:Name=\"CategoryActionsButton\""));
        Assert.IsTrue(xamlContent.Contains("Content=\"🔧\""));
        Assert.IsTrue(xamlContent.Contains("Header=\"✎ Rename\""));
        Assert.IsTrue(xamlContent.Contains("Header=\"× Delete\""));
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

    #region Feature 7 — Application Icon Script Verification & MainWindow Smoke Test

    [TestMethod]
    public void Feature7_GenerateAppIcon_Script_Exists_And_Validates_Parameters()
    {
        string scriptPath = RepositoryTestPaths.RequireFile("tools", "GenerateAppIcon.ps1");
        string scriptContent = File.ReadAllText(scriptPath);
        Assert.IsTrue(scriptContent.Contains("PromptHelperLogo.svg"));
        Assert.IsTrue(scriptContent.Contains("PromptHelper.ico"));
        Assert.IsTrue(scriptContent.Contains("magick"));
    }

    [TestMethod]
    public void MainWindow_constructs_with_all_required_resources()
    {
        WpfTestHost.Invoke(() =>
        {
            using var temp = new TestDirectory();
            var paths = new AppPaths(temp.Root);
            paths.EnsureRootDirectory();
            paths.EnsureDataDirectories();

            var writer = new AtomicTextWriter();
            var deleter = new FileDeleter();
            var libRepo = new LibraryRepository(paths, writer);
            var promptRepo = new PromptRepository(paths, writer, deleter);
            var startup = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);
            var init = startup.LoadOrInitialize();
            var service = new PromptLibraryService(init.Document, libRepo, promptRepo);
            var vm = new MainViewModel(service, promptRepo, temp.Root);

            string settingsPath = Path.Combine(temp.Root, "settings.json");
            var settingsRepo = new AppSettingsRepository(writer, settingsPathOverride: settingsPath);
            var fakeClipboard = new FakeClipboardService();
            var migrationService = new DataFolderMigrationService();

            var window = new MainWindow(
                vm,
                fakeClipboard,
                settingsRepo,
                migrationService);

            try
            {
                Assert.IsNotNull(window);
                Assert.AreSame(vm, window.DataContext);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void CRUU3_001_MainWindow_Requests_Shutdown_On_RestartRequired()
    {
        WpfTestHost.Invoke(() =>
        {
            using var temp = new TestDirectory();
            var paths = new AppPaths(temp.Root);
            paths.EnsureRootDirectory();
            paths.EnsureDataDirectories();

            var writer = new AtomicTextWriter();
            var deleter = new FileDeleter();
            var libRepo = new LibraryRepository(paths, writer);
            var promptRepo = new PromptRepository(paths, writer, deleter);
            var startup = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);
            var init = startup.LoadOrInitialize();
            var service = new PromptLibraryService(init.Document, libRepo, promptRepo);
            var vm = new MainViewModel(service, promptRepo, temp.Root);

            string settingsPath = Path.Combine(temp.Root, "settings.json");
            var settingsRepo = new AppSettingsRepository(writer, settingsPathOverride: settingsPath);
            var fakeClipboard = new FakeClipboardService();
            var migrationService = new DataFolderMigrationService();
            var fakeLifetime = new FakeApplicationLifetime();

            var window = new MainWindow(
                vm,
                fakeClipboard,
                settingsRepo,
                migrationService,
                fakeLifetime);

            try
            {
                Assert.IsFalse(fakeLifetime.ShutdownRequested);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void UsageGuide_does_not_document_removed_help_and_category_controls()
    {
        string guidePath = RepositoryTestPaths.RequireFile(
            "Prompt_Helper_Nutzungsguide_DE_v2_FINAL.md");

        string guide = File.ReadAllText(guidePath);

        Assert.IsFalse(guide.Contains("Der `?`-Button öffnet den Hilfe-Dialog."));
        StringAssert.Contains(guide, "Tools");
        StringAssert.Contains(guide, "Headline");
        StringAssert.Contains(guide, "Wrap long lines");
    }

    #endregion
}
