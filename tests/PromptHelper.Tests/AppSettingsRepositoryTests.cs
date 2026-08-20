using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Infrastructure;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class AppSettingsRepositoryTests
{
    [TestMethod]
    public void Load_when_file_missing_returns_default_settings()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);

        var settings = repo.Load();

        Assert.IsNotNull(settings);
        Assert.AreEqual(1, settings.SchemaVersion);
        Assert.IsNull(settings.DataRootPath);
    }

    [TestMethod]
    public void GetEffectiveDataRoot_falls_back_to_localappdata_when_setting_is_null_or_whitespace()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);

        string expectedDefault = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PromptHelper");

        Assert.AreEqual(expectedDefault, repo.GetEffectiveDataRoot());

        repo.Save(new AppSettings { SchemaVersion = 1, DataRootPath = "   " });
        Assert.AreEqual(expectedDefault, repo.GetEffectiveDataRoot());
    }

    [TestMethod]
    public void Save_and_load_roundtrip_persists_custom_data_root()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);

        string customPath = Path.Combine(testDir.Root, "CustomData");
        repo.Save(new AppSettings
        {
            SchemaVersion = 1,
            DataRootPath = customPath
        });

        var loaded = repo.Load();
        Assert.AreEqual(1, loaded.SchemaVersion);
        Assert.AreEqual(Path.GetFullPath(customPath), loaded.DataRootPath);
        Assert.AreEqual(Path.GetFullPath(customPath), repo.GetEffectiveDataRoot(loaded));
    }

    [TestMethod]
    public void Load_unsupported_schema_version_throws_InvalidDataException()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        File.WriteAllText(settingsPath, "{\"schemaVersion\": 99, \"dataRootPath\": null}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);

        Assert.Throws<InvalidDataException>(() => repo.Load());
    }

    [TestMethod]
    public void Load_corrupt_json_throws_InvalidDataException()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        File.WriteAllText(settingsPath, "{ malformed json }");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);

        Assert.Throws<InvalidDataException>(() => repo.Load());
    }

    [TestMethod]
    public void LoadOrRecover_corrupt_primary_recovers_from_valid_backup()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        string backupPath = Path.Combine(testDir.Root, "settings.backup.json");
        string customPath = Path.Combine(testDir.Root, "BackupData");

        File.WriteAllText(settingsPath, "corrupt json!!!");
        File.WriteAllText(backupPath, $"{{\"schemaVersion\": 1, \"dataRootPath\": \"{customPath.Replace("\\", "\\\\")}\"}}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath, backupPathOverride: backupPath);

        var result = repo.LoadOrRecover();

        Assert.IsTrue(result.RecoveredFromBackup);
        Assert.IsNotNull(result.Warning);
        Assert.AreEqual(Path.GetFullPath(customPath), result.Settings.DataRootPath);

        // Verify primary was restored
        Assert.IsTrue(File.ReadAllText(settingsPath).Contains("BackupData"));
    }

    [TestMethod]
    public void LoadOrRecover_missing_primary_recovers_from_valid_backup()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        string backupPath = Path.Combine(testDir.Root, "settings.backup.json");
        string customPath = Path.Combine(testDir.Root, "BackupDataOnly");

        File.WriteAllText(backupPath, $"{{\"schemaVersion\": 1, \"dataRootPath\": \"{customPath.Replace("\\", "\\\\")}\"}}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath, backupPathOverride: backupPath);

        var result = repo.LoadOrRecover();

        Assert.IsTrue(result.RecoveredFromBackup);
        Assert.AreEqual(Path.GetFullPath(customPath), result.Settings.DataRootPath);
    }

    [TestMethod]
    public void LoadOrRecover_both_corrupt_throws_controlled_InvalidDataException()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        string backupPath = Path.Combine(testDir.Root, "settings.backup.json");

        File.WriteAllText(settingsPath, "corrupt primary");
        File.WriteAllText(backupPath, "corrupt backup");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath, backupPathOverride: backupPath);

        Assert.Throws<InvalidDataException>(() => repo.LoadOrRecover());
    }

    [TestMethod]
    public void Relative_data_root_path_throws_InvalidDataException()
    {
        Assert.Throws<InvalidDataException>(() =>
            AppSettingsRepository.NormalizeAndValidateDataRoot("relative\\path\\to\\data"));
    }

    [TestMethod]
    public void Save_with_backup_write_failure_returns_warning()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        string backupPath = Path.Combine(testDir.Root, "settings.backup.json");

        var baseWriter = new AtomicTextWriter();
        var faultWriter = new FaultInjectingAtomicTextWriter(baseWriter)
        {
            ShouldFail = (path, _) => path.EndsWith("settings.backup.json")
        };

        var repo = new AppSettingsRepository(
            writer: faultWriter,
            settingsPathOverride: settingsPath,
            backupPathOverride: backupPath);

        var saveResult = repo.Save(new AppSettings
        {
            SchemaVersion = 1,
            DataRootPath = Path.Combine(testDir.Root, "Target")
        });

        Assert.IsNotNull(saveResult.Warning);
        Assert.IsTrue(saveResult.Warning.Contains("settings backup could not be synchronized"));
        Assert.IsTrue(File.Exists(settingsPath));
    }
}
