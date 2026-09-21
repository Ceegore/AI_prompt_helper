using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Platform.Linux.Tests;

[TestClass]
public sealed class LinuxDesktopSettingsStoreTests
{
    [TestMethod]
    public void NormalizeDataRoot_requires_absolute_path()
    {
        if (!OperatingSystem.IsLinux()) return;

        Assert.Throws<InvalidDataException>(() =>
            LinuxDesktopSettingsStore.NormalizeDataRoot("relative/path"));

        string absolute = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-settings-root");
        Assert.AreEqual(
            Path.GetFullPath(absolute),
            LinuxDesktopSettingsStore.NormalizeDataRoot(absolute));
    }

    [TestMethod]
    public void Effective_root_defaults_to_platform_data_root()
    {
        if (!OperatingSystem.IsLinux()) return;

        var store = new LinuxDesktopSettingsStore();
        var settings = new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            DataRootPath = null,
            UseDarkMode = true
        };

        Assert.AreEqual(
            DefaultDataRoot.Path,
            store.ResolveEffectiveDataRoot(settings));
    }

    [TestMethod]
    public void Save_and_load_roundtrip_persists_current_settings_and_backup()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var test = new SettingsTestDirectory();
        var store = new LinuxDesktopSettingsStore(test.Root);
        string customRoot = Path.Combine(test.Root, "custom-library");

        store.Save(new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            DataRootPath = customRoot,
            UseDarkMode = true
        });

        AppSettings loaded = store.Load();

        Assert.AreEqual(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.AreEqual(Path.GetFullPath(customRoot), loaded.DataRootPath);
        Assert.IsTrue(loaded.UseDarkMode);
        Assert.IsTrue(File.Exists(store.SettingsPath));
        Assert.IsTrue(File.Exists(Path.Combine(test.Root, "settings.backup.json")));
    }

    [TestMethod]
    public void Corrupt_primary_recovers_from_current_backup_and_repairs_primary()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var test = new SettingsTestDirectory();
        var store = new LinuxDesktopSettingsStore(test.Root);
        store.Save(new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            UseDarkMode = true
        });

        string backupPath = Path.Combine(test.Root, "settings.backup.json");
        byte[] expected = File.ReadAllBytes(backupPath);
        File.WriteAllText(store.SettingsPath, "{ broken", new UTF8Encoding(false));

        AppSettings loaded = store.Load();

        Assert.IsTrue(loaded.UseDarkMode);
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(store.SettingsPath));
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(backupPath));
    }

    [TestMethod]
    public void Future_primary_fails_closed_and_is_not_overwritten_by_older_backup()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var test = new SettingsTestDirectory();
        var store = new LinuxDesktopSettingsStore(test.Root);
        store.Save(new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            UseDarkMode = false
        });

        byte[] future = Encoding.UTF8.GetBytes(
            """
            {
              "schemaVersion": 999,
              "dataRootPath": null,
              "useDarkMode": true
            }
            """);
        File.WriteAllBytes(store.SettingsPath, future);

        NotSupportedException ex =
            Assert.Throws<NotSupportedException>(() => store.Load());

        StringAssert.Contains(ex.Message, "999");
        CollectionAssert.AreEqual(future, File.ReadAllBytes(store.SettingsPath));
    }

    [TestMethod]
    public void Future_backup_fails_closed_when_primary_is_missing()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var test = new SettingsTestDirectory();
        var store = new LinuxDesktopSettingsStore(test.Root);
        store.Save(new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            UseDarkMode = false
        });

        File.Delete(store.SettingsPath);
        string backupPath = Path.Combine(test.Root, "settings.backup.json");
        byte[] future = Encoding.UTF8.GetBytes(
            """
            {
              "schemaVersion": 999,
              "dataRootPath": null,
              "useDarkMode": true
            }
            """);
        File.WriteAllBytes(backupPath, future);

        NotSupportedException ex =
            Assert.Throws<NotSupportedException>(() => store.Load());

        StringAssert.Contains(ex.Message, "999");
        CollectionAssert.AreEqual(future, File.ReadAllBytes(backupPath));
        Assert.IsFalse(File.Exists(store.SettingsPath));
    }

    [TestMethod]
    public void Corrupt_primary_and_backup_fail_closed()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var test = new SettingsTestDirectory();
        Directory.CreateDirectory(test.Root);
        File.WriteAllText(
            Path.Combine(test.Root, "settings.json"),
            "{ broken",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(test.Root, "settings.backup.json"),
            "[]",
            new UTF8Encoding(false));

        var store = new LinuxDesktopSettingsStore(test.Root);

        Assert.Throws<InvalidDataException>(() => store.Load());
    }

    [TestMethod]
    public void Empty_store_loads_current_defaults_without_creating_files()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var test = new SettingsTestDirectory();
        var store = new LinuxDesktopSettingsStore(test.Root);

        AppSettings loaded = store.Load();

        Assert.AreEqual(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.IsNull(loaded.DataRootPath);
        Assert.IsFalse(loaded.UseDarkMode);
        Assert.IsFalse(File.Exists(store.SettingsPath));
        Assert.IsFalse(File.Exists(Path.Combine(test.Root, "settings.backup.json")));
    }

    [TestMethod]
    public void Version_one_settings_are_migrated_in_memory_with_dark_mode_disabled()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var test = new SettingsTestDirectory();
        Directory.CreateDirectory(test.Root);
        string customRoot = Path.Combine(test.Root, "library");
        File.WriteAllText(
            Path.Combine(test.Root, "settings.json"),
            $"""
            {
              "schemaVersion": 1,
              "dataRootPath": "{{customRoot}}",
              "useDarkMode": true
            }
            """,
            new UTF8Encoding(false));

        var store = new LinuxDesktopSettingsStore(test.Root);
        AppSettings loaded = store.Load();

        Assert.AreEqual(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.AreEqual(Path.GetFullPath(customRoot), loaded.DataRootPath);
        Assert.IsFalse(loaded.UseDarkMode);
    }

    [TestMethod]
    public void Invalid_nonpositive_schema_uses_valid_backup()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var test = new SettingsTestDirectory();
        var store = new LinuxDesktopSettingsStore(test.Root);
        store.Save(new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            UseDarkMode = true
        });

        File.WriteAllText(
            store.SettingsPath,
            """
            {
              "schemaVersion": 0,
              "dataRootPath": null,
              "useDarkMode": false
            }
            """,
            new UTF8Encoding(false));

        AppSettings loaded = store.Load();

        Assert.IsTrue(loaded.UseDarkMode);
        Assert.AreEqual(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [TestMethod]
    public void Save_rejects_noncurrent_schema_and_null_settings()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var test = new SettingsTestDirectory();
        var store = new LinuxDesktopSettingsStore(test.Root);

        Assert.Throws<ArgumentNullException>(() => store.Save(null!));
        Assert.Throws<InvalidDataException>(() =>
            store.Save(new AppSettings
            {
                SchemaVersion = AppSettings.CurrentSchemaVersion + 1
            }));
    }

    [TestMethod]
    public void NormalizeDataRoot_handles_blank_and_trimmed_absolute_paths()
    {
        if (!OperatingSystem.IsLinux()) return;

        Assert.IsNull(LinuxDesktopSettingsStore.NormalizeDataRoot(null));
        Assert.IsNull(LinuxDesktopSettingsStore.NormalizeDataRoot("   "));

        string absolute = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-settings-trimmed");

        Assert.AreEqual(
            Path.GetFullPath(absolute),
            LinuxDesktopSettingsStore.NormalizeDataRoot("  " + absolute + "  "));
    }

    private sealed class SettingsTestDirectory : IDisposable
    {
        public SettingsTestDirectory()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "PromptHelper-LinuxSettings-" + Guid.NewGuid().ToString("N"));
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
