using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;
using PromptHelper.Views;

namespace PromptHelper.Tests;

[TestClass]
public sealed class DarkModeRegressionTests
{
    [TestMethod]
    public void Schema1_settings_upgrade_in_memory_with_light_mode_default()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":null}");

        AppSettings loaded = new AppSettingsRepository(settingsPathOverride: settingsPath).Load();

        Assert.AreEqual(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.IsFalse(loaded.UseDarkMode);
        StringAssert.Contains(File.ReadAllText(settingsPath), "\"schemaVersion\":1");
    }

    [TestMethod]
    public void Schema1_cannot_smuggle_a_schema2_theme_member()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(
            settingsPath,
            "{\"schemaVersion\":1,\"dataRootPath\":null,\"useDarkMode\":true}");

        Assert.Throws<InvalidDataException>(() =>
            new AppSettingsRepository(settingsPathOverride: settingsPath).Load());
    }

    [TestMethod]
    public void Theme_only_transition_is_persisted_without_restart()
    {
        using var source = new TestDirectory();
        using var settingsDir = new TestDirectory();
        SeedValidLibrary(source.Root);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(
            settingsPath,
            $"{{\"schemaVersion\":2,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\",\"useDarkMode\":false}}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            repo,
            new DataFolderMigrationService(),
            new FakeUserConfirmationService());

        DataFolderTransitionResult result = coordinator.RequestTransition(source.Root, useDarkMode: true);

        Assert.IsFalse(result.Changed);
        Assert.IsFalse(result.RestartRequired);
        Assert.IsTrue(repo.Load().UseDarkMode);
    }

    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void Settings_dialog_previews_commits_and_restores_theme_correctly()
    {
        WpfTestHost.Invoke(() =>
        {
            using var source = new TestDirectory();
            using var settingsDir = new TestDirectory();
            SeedValidLibrary(source.Root);
            string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
            File.WriteAllText(
                settingsPath,
                $"{{\"schemaVersion\":2,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\",\"useDarkMode\":false}}");

            var previewTheme = new RecordingThemeService();
            var cancelDialog = CreateDialog(source.Root, settingsPath, previewTheme, new FakeDataFolderTransitionService());
            ((CheckBox)cancelDialog.FindName("DarkModeToggle")).IsChecked = true;
            Assert.IsTrue(previewTheme.PreferredDarkMode);
            cancelDialog.Close();
            Assert.IsFalse(previewTheme.PreferredDarkMode, "Closing without Save must restore the original theme.");

            bool? requestedTheme = null;
            var commitService = new FakeDataFolderTransitionService
            {
                OnRequestTransitionWithTheme = (path, dark) =>
                {
                    requestedTheme = dark;
                    return new DataFolderTransitionResult(false, false, false, path, null);
                }
            };
            var commitTheme = new RecordingThemeService();
            var saveDialog = CreateDialog(source.Root, settingsPath, commitTheme, commitService);
            ((CheckBox)saveDialog.FindName("DarkModeToggle")).IsChecked = true;
            saveDialog.ExecuteSaveForTest();

            Assert.AreEqual(true, requestedTheme);
            Assert.IsTrue(commitTheme.PreferredDarkMode, "A saved theme must not be reverted by Window.Close.");
        });
    }

    private static SettingsDialog CreateDialog(
        string root,
        string settingsPath,
        IThemeService theme,
        IDataFolderTransitionService transition) =>
        new(
            root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(),
            new FakeUserConfirmationService(),
            transition,
            theme);

    private static void SeedValidLibrary(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "prompts"));
        Directory.CreateDirectory(Path.Combine(root, "recovery"));
        File.WriteAllText(
            Path.Combine(root, "library.json"),
            "{\"schemaVersion\":1,\"categories\":[],\"prompts\":[]}");
    }

    private sealed class RecordingThemeService : IThemeService
    {
        public List<bool> AppliedValues { get; } = [];

        public bool PreferredDarkMode { get; private set; }

        public void ApplyPreferredTheme(bool useDarkMode)
        {
            PreferredDarkMode = useDarkMode;
            AppliedValues.Add(useDarkMode);
        }

        public void RefreshSystemContrast()
        {
        }
    }
}
