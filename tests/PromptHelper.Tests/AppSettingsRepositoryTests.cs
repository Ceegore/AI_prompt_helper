using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
}
