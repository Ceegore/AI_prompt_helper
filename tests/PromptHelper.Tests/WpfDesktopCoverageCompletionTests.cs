using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;
using PromptHelper.ViewModels;
using PromptHelper.Views;

namespace PromptHelper.Tests;

[TestClass]
[DoNotParallelize]
public sealed class WpfDesktopCoverageCompletionTests
{
    private sealed class RecordingThemeService : IThemeService
    {
        public bool PreferredDarkMode { get; private set; }
        public List<bool> Applied { get; } = [];
        public int RefreshCount { get; private set; }

        public void ApplyPreferredTheme(bool useDarkMode)
        {
            PreferredDarkMode = useDarkMode;
            Applied.Add(useDarkMode);
        }

        public void RefreshSystemContrast() => RefreshCount++;
    }

    [TestMethod]
    [TestCategory("WpfIntegration")]
    [Timeout(60_000)]
    public void Theme_service_applies_both_palettes_and_high_contrast_palette_is_constructible()
    {
        WpfTestHost.Invoke(() =>
        {
            Application app = Application.Current
                ?? throw new AssertFailedException("WPF host application is unavailable.");

            Assert.Throws<ArgumentNullException>(() =>
                new WpfThemeService(null!));

            var theme = new WpfThemeService(app);

            theme.ApplyPreferredTheme(useDarkMode: false);
            Assert.IsFalse(theme.PreferredDarkMode);
            AssertPaletteResources(app);

            theme.ApplyPreferredTheme(useDarkMode: true);
            Assert.IsTrue(theme.PreferredDarkMode);
            AssertPaletteResources(app);

            theme.RefreshSystemContrast();
            AssertPaletteResources(app);

            MethodInfo createHighContrast =
                typeof(WpfThemeService).GetMethod(
                    "CreateHighContrastPalette",
                    BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new AssertFailedException(
                    "High-contrast palette factory was not found.");

            var palette =
                (IReadOnlyDictionary<string, Color>)createHighContrast.Invoke(
                    null,
                    null)!;

            Assert.AreEqual(16, palette.Count);
            Assert.IsTrue(palette.ContainsKey("AppBackgroundBrush"));
            Assert.IsTrue(palette.ContainsKey("DangerBorderBrush"));
        });
    }

    [TestMethod]
    [TestCategory("WpfIntegration")]
    [Timeout(60_000)]
    public void Name_and_move_dialogs_cover_nonmodal_validation_and_selection_logic()
    {
        WpfTestHost.Invoke(() =>
        {
            var nameDialog = new NameDialog(
                "Rename",
                "Save",
                " initial ",
                value => string.IsNullOrWhiteSpace(value)
                    ? "required"
                    : null);

            Assert.AreEqual("Rename", nameDialog.Title);
            TextBox nameBox = Require<TextBox>(nameDialog, "NameInputTextBox");
            TextBlock error = Require<TextBlock>(nameDialog, "ErrorTextBlock");

            nameBox.Text = "   ";
            InvokePrivate(nameDialog, "Submit");
            Assert.AreEqual(Visibility.Visible, error.Visibility);
            Assert.AreEqual("required", error.Text);
            Assert.AreEqual(string.Empty, nameDialog.ResultName);

            nameDialog.Close();

            Guid categoryId = Guid.NewGuid();
            var destinations = new[]
            {
                new DestinationOptionViewModel(null, "Home"),
                new DestinationOptionViewModel(categoryId, "Folder")
            };

            var move = new MovePromptDialog(
                "preview",
                destinations,
                categoryId,
                allowDuplicate: true);

            Assert.AreEqual(categoryId, move.DestinationCategoryId);
            CheckBox copy = Require<CheckBox>(move, "CopyInsteadOfMoveCheckBox");
            Button action = Require<Button>(move, "ActionButton");

            copy.IsChecked = true;
            InvokePrivate(
                move,
                "CopyInsteadOfMoveCheckBox_Checked",
                copy,
                new RoutedEventArgs());
            Assert.AreEqual("Copy", action.Content);
            Assert.IsTrue(move.CopyInsteadOfMove);

            copy.IsChecked = false;
            InvokePrivate(
                move,
                "CopyInsteadOfMoveCheckBox_Unchecked",
                copy,
                new RoutedEventArgs());
            Assert.AreEqual("Move", action.Content);
            Assert.IsFalse(move.CopyInsteadOfMove);
            move.Close();

            var unavailable = new MovePromptDialog(
                "preview",
                destinations,
                currentCategoryId: null,
                allowDuplicate: false);
            Assert.IsFalse(
                Require<CheckBox>(
                    unavailable,
                    "CopyInsteadOfMoveCheckBox").IsEnabled);
            Assert.AreEqual(
                Visibility.Visible,
                Require<TextBlock>(
                    unavailable,
                    "UnavailablePromptNoticeTextBlock").Visibility);
            unavailable.Close();
        });
    }

    [TestMethod]
    [TestCategory("WpfIntegration")]
    [Timeout(60_000)]
    public void Settings_dialog_covers_theme_preview_nochange_commit_warning_and_changed_commit()
    {
        WpfTestHost.Invoke(() =>
        {
            using var dir = new TestDirectory();
            string settingsPath = Path.Combine(dir.Root, "settings.json");
            string backupPath = Path.Combine(dir.Root, "settings.backup.json");
            var settingsRepo = new AppSettingsRepository(
                settingsPathOverride: settingsPath,
                backupPathOverride: backupPath);
            var migration = new DataFolderMigrationService();
            var confirmation = new FakeUserConfirmationService();
            var transition = new FakeDataFolderTransitionService();
            var theme = new RecordingThemeService();

            transition.OnRequestTransitionWithTheme = (candidate, dark) =>
                new DataFolderTransitionResult(
                    Changed: false,
                    RestartRequired: false,
                    ExistingLibrarySelected: true,
                    NormalizedTargetRoot: candidate,
                    Warning: null);

            var cancelled = new SettingsDialog(
                dir.Root,
                settingsRepo,
                migration,
                confirmation,
                transition,
                theme);

            CheckBox toggle = Require<CheckBox>(cancelled, "DarkModeToggle");
            toggle.IsChecked = true;
            InvokePrivate(
                cancelled,
                "DarkModeToggle_Changed",
                toggle,
                new RoutedEventArgs());
            Assert.AreEqual(true, theme.Applied.Last());

            cancelled.ExecuteSaveForTest();
            Assert.IsFalse(cancelled.RestartRequired);
            cancelled.Close();
            Assert.AreEqual(false, theme.Applied.Last());

            transition.OnRequestTransitionWithTheme = (candidate, dark) =>
                new DataFolderTransitionResult(
                    Changed: false,
                    RestartRequired: true,
                    ExistingLibrarySelected: false,
                    NormalizedTargetRoot: candidate,
                    Warning: "saved warning");

            var sameRoot = new SettingsDialog(
                dir.Root,
                settingsRepo,
                migration,
                confirmation,
                transition,
                theme);
            Require<CheckBox>(sameRoot, "DarkModeToggle").IsChecked = true;

            sameRoot.ExecuteSaveForTest();

            Assert.IsTrue(sameRoot.RestartRequired);
            Assert.IsTrue(confirmation.WarningCount >= 1);
            StringAssert.Contains(confirmation.LastWarning!, "saved warning");
            Assert.AreEqual(true, theme.Applied.Last());

            transition.OnRequestTransitionWithTheme = (candidate, dark) =>
                new DataFolderTransitionResult(
                    Changed: true,
                    RestartRequired: true,
                    ExistingLibrarySelected: false,
                    NormalizedTargetRoot: candidate,
                    Warning: "migration warning");

            var changed = new SettingsDialog(
                dir.Root,
                settingsRepo,
                migration,
                confirmation,
                transition,
                theme);

            changed.ExecuteSaveForTest();

            Assert.IsTrue(changed.RestartRequired);
            Assert.IsTrue(confirmation.InfoCount >= 1);
            StringAssert.Contains(
                confirmation.LastMessage!,
                "migration warning");
        });
    }

    private static void AssertPaletteResources(Application app)
    {
        foreach (string key in new[]
        {
            "AppBackgroundBrush",
            "SurfaceBrush",
            "TextPrimaryBrush",
            "AccentBrush",
            "DangerBorderBrush"
        })
        {
            Assert.IsInstanceOfType<SolidColorBrush>(app.Resources[key]);
        }
    }

    private static T Require<T>(FrameworkElement root, string name)
        where T : FrameworkElement =>
        root.FindName(name) as T
        ?? throw new AssertFailedException(
            $"Required WPF element '{name}' was not found.");

    private static object? InvokePrivate(
        object target,
        string name,
        params object?[] args)
    {
        MethodInfo method = target.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(info =>
                info.Name == name &&
                info.GetParameters().Length == args.Length);

        try
        {
            return method.Invoke(target, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
