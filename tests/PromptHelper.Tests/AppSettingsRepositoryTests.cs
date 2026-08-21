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
    public void GetEffectiveDataRoot_falls_back_to_bootstrap_root_when_setting_is_null_or_whitespace()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);

        string expectedDefault = testDir.Root;

        Assert.AreEqual(Path.GetFullPath(expectedDefault), repo.GetEffectiveDataRoot());

        repo.Save(new AppSettings { SchemaVersion = 1, DataRootPath = "   " });
        Assert.AreEqual(Path.GetFullPath(expectedDefault), repo.GetEffectiveDataRoot());
    }

    [TestMethod]
    public void GetEffectiveDataRoot_without_override_falls_back_to_localappdata()
    {
        var repo = new AppSettingsRepository();
        string expectedDefault = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PromptHelper");

        Assert.AreEqual(expectedDefault, repo.GetEffectiveDataRoot(new AppSettings()));
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
    public void Load_unsupported_schema_version_throws_UnsupportedSettingsSchemaException()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        File.WriteAllText(settingsPath, "{\"schemaVersion\": 99, \"dataRootPath\": null}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);

        var ex = Assert.Throws<UnsupportedSettingsSchemaException>(() => repo.Load());
        Assert.AreEqual(99, ex.SchemaVersion);
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
    public void CRUU3_002_Primary_future_schema_with_valid_old_backup_stops_without_recovery()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        string backupPath = Path.Combine(testDir.Root, "settings.backup.json");

        File.WriteAllText(settingsPath, "{\"schemaVersion\": 2, \"dataRootPath\": \"C:\\\\Future\"}");
        File.WriteAllText(backupPath, "{\"schemaVersion\": 1, \"dataRootPath\": \"C:\\\\Old\"}");

        byte[] primaryBefore = File.ReadAllBytes(settingsPath);
        byte[] backupBefore = File.ReadAllBytes(backupPath);

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath, backupPathOverride: backupPath);

        var ex = Assert.Throws<UnsupportedSettingsSchemaException>(() => repo.LoadOrRecover());
        Assert.AreEqual(2, ex.SchemaVersion);

        // Verify neither primary nor backup was modified
        CollectionAssert.AreEqual(primaryBefore, File.ReadAllBytes(settingsPath));
        CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(backupPath));
    }

    [TestMethod]
    public void CRUU3_002_Missing_primary_with_future_schema_backup_stops()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        string backupPath = Path.Combine(testDir.Root, "settings.backup.json");

        File.WriteAllText(backupPath, "{\"schemaVersion\": 5, \"dataRootPath\": \"C:\\\\FutureBackup\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath, backupPathOverride: backupPath);

        var ex = Assert.Throws<UnsupportedSettingsSchemaException>(() => repo.LoadOrRecover());
        Assert.AreEqual(5, ex.SchemaVersion);
    }

    [TestMethod]
    public void CRUU3_002_Corrupt_primary_with_future_schema_backup_stops()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        string backupPath = Path.Combine(testDir.Root, "settings.backup.json");

        File.WriteAllText(settingsPath, "corrupt json text");
        File.WriteAllText(backupPath, "{\"schemaVersion\": 3, \"dataRootPath\": \"C:\\\\FutureBackup\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath, backupPathOverride: backupPath);

        var ex = Assert.Throws<UnsupportedSettingsSchemaException>(() => repo.LoadOrRecover());
        Assert.AreEqual(3, ex.SchemaVersion);
    }

    [TestMethod]
    public void CRUU3_003_Locked_valid_primary_does_not_fall_back_to_stale_backup()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        string backupPath = Path.Combine(testDir.Root, "settings.backup.json");

        string customPrimary = Path.Combine(testDir.Root, "PrimaryData");
        string customBackup = Path.Combine(testDir.Root, "StaleBackupData");

        File.WriteAllText(settingsPath, $"{{\"schemaVersion\": 1, \"dataRootPath\": \"{customPrimary.Replace("\\", "\\\\")}\"}}");
        File.WriteAllText(backupPath, $"{{\"schemaVersion\": 1, \"dataRootPath\": \"{customBackup.Replace("\\", "\\\\")}\"}}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath, backupPathOverride: backupPath);

        // Lock the primary settings file
        using var lockStream = new FileStream(
            settingsPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        Assert.Throws<SettingsReadException>(() => repo.LoadOrRecover());
    }

    [TestMethod]
    public void CRUU3_004_Valid_primary_backup_sync_failure_returns_warning_and_primary_settings()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        string backupPath = Path.Combine(testDir.Root, "settings.backup.json");
        string customPath = Path.Combine(testDir.Root, "ValidPrimaryData");

        File.WriteAllText(settingsPath, $"{{\"schemaVersion\": 1, \"dataRootPath\": \"{customPath.Replace("\\", "\\\\")}\"}}");

        var baseWriter = new AtomicTextWriter();
        var faultWriter = new FaultInjectingAtomicTextWriter(baseWriter)
        {
            ShouldFail = (path, _) => path.EndsWith("settings.backup.json")
        };

        var repo = new AppSettingsRepository(
            writer: faultWriter,
            settingsPathOverride: settingsPath,
            backupPathOverride: backupPath);

        var result = repo.LoadOrRecover();

        Assert.IsFalse(result.RecoveredFromBackup);
        Assert.IsNotNull(result.Warning);
        Assert.IsTrue(result.Warning.Contains("settings.backup.json could not be synchronized"));
        Assert.AreEqual(Path.GetFullPath(customPath), result.Settings.DataRootPath);
    }

    [TestMethod]
    public void CRUU3_004_Backup_recovery_primary_restore_failure_returns_warning_and_backup_settings()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        string backupPath = Path.Combine(testDir.Root, "settings.backup.json");
        string customPath = Path.Combine(testDir.Root, "BackupRecoveredData");

        File.WriteAllText(settingsPath, "corrupt primary");
        File.WriteAllText(backupPath, $"{{\"schemaVersion\": 1, \"dataRootPath\": \"{customPath.Replace("\\", "\\\\")}\"}}");

        var baseWriter = new AtomicTextWriter();
        var faultWriter = new FaultInjectingAtomicTextWriter(baseWriter)
        {
            ShouldFail = (path, _) => path.EndsWith("settings.json")
        };

        var repo = new AppSettingsRepository(
            writer: faultWriter,
            settingsPathOverride: settingsPath,
            backupPathOverride: backupPath);

        var result = repo.LoadOrRecover();

        Assert.IsTrue(result.RecoveredFromBackup);
        Assert.IsNotNull(result.Warning);
        Assert.IsTrue(result.Warning.Contains("settings.json could not be restored"));
        Assert.AreEqual(Path.GetFullPath(customPath), result.Settings.DataRootPath);
    }

    [TestMethod]
    public void CRUU3_005_Save_invalid_schema_writes_nothing()
    {
        using var testDir = new TestDirectory();
        string settingsPath = Path.Combine(testDir.Root, "settings.json");
        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);

        Assert.Throws<InvalidDataException>(() => repo.Save(new AppSettings
        {
            SchemaVersion = 99,
            DataRootPath = Path.Combine(testDir.Root, "SomeTarget")
        }));

        Assert.IsFalse(File.Exists(settingsPath));
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

        var faultDurable = new FakeDurableSettingsFileWriter
        {
            OnWriteDurable = (path, _) =>
            {
                if (path.Equals(backupPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Backup write failed");
                }
            }
        };

        var repo = new AppSettingsRepository(
            settingsPathOverride: settingsPath,
            backupPathOverride: backupPath,
            durableWriter: faultDurable);

        var saveResult = repo.Save(new AppSettings
        {
            SchemaVersion = 1,
            DataRootPath = Path.Combine(testDir.Root, "Target")
        });

        Assert.IsNotNull(saveResult.Warning);
        Assert.IsTrue(saveResult.Warning.Contains("settings backup could not be synchronized"));
        Assert.IsTrue(File.Exists(settingsPath));
    }

    [TestMethod]
    public void CRUU4_001_Valid_primary_preserves_future_schema_backup()
    {
        using var temp = new TestDirectory();

        string primary = Path.Combine(temp.Root, "settings.json");
        string backup = Path.Combine(temp.Root, "settings.backup.json");

        File.WriteAllText(
            primary,
            "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Current\"}");

        File.WriteAllText(
            backup,
            "{\"schemaVersion\":2,\"dataRootPath\":\"C:\\\\Newer\"}");

        byte[] backupBefore = File.ReadAllBytes(backup);

        var repo = new AppSettingsRepository(
            settingsPathOverride: primary,
            backupPathOverride: backup);

        SettingsLoadResult result = repo.LoadOrRecover();

        Assert.IsFalse(result.RecoveredFromBackup);
        Assert.AreEqual(Path.GetFullPath(@"C:\Current"), result.Settings.DataRootPath);
        Assert.IsNotNull(result.Warning);
        StringAssert.Contains(result.Warning, "newer");
        CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(backup));
    }

    [TestMethod]
    public void CRUU4_001_Valid_primary_unreadable_backup_starts_with_warning()
    {
        using var temp = new TestDirectory();

        string primary = Path.Combine(temp.Root, "settings.json");
        string backup = Path.Combine(temp.Root, "settings.backup.json");

        File.WriteAllText(
            primary,
            "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Current\"}");

        File.WriteAllText(backup, "{\"schemaVersion\":1}");

        using var lockStream = new FileStream(backup, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var repo = new AppSettingsRepository(
            settingsPathOverride: primary,
            backupPathOverride: backup);

        SettingsLoadResult result = repo.LoadOrRecover();

        Assert.IsFalse(result.RecoveredFromBackup);
        Assert.AreEqual(Path.GetFullPath(@"C:\Current"), result.Settings.DataRootPath);
        Assert.IsNotNull(result.Warning);
        StringAssert.Contains(result.Warning, "could not be inspected or synchronized");
    }

    [TestMethod]
    public void CRUU5_001_Save_preserves_future_schema_backup_exactly()
    {
        using var temp = new TestDirectory();

        string primary = Path.Combine(temp.Root, "settings.json");
        string backup = Path.Combine(temp.Root, "settings.backup.json");

        File.WriteAllText(
            primary,
            "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Old\"}");

        File.WriteAllText(
            backup,
            "{\"schemaVersion\":2,\"dataRootPath\":\"C:\\\\Newer\"}");

        byte[] backupBefore = File.ReadAllBytes(backup);

        var repo = new AppSettingsRepository(
            settingsPathOverride: primary,
            backupPathOverride: backup);

        SettingsSaveResult result = repo.Save(new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            DataRootPath = @"C:\ChangedByOldBuild"
        });

        CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(backup));
        Assert.IsNotNull(result.Warning);
        StringAssert.Contains(result.Warning, "newer");
    }

    [TestMethod]
    public void CRUU5_001_Save_refuses_to_overwrite_future_schema_primary()
    {
        using var temp = new TestDirectory();

        string primary = Path.Combine(temp.Root, "settings.json");
        string backup = Path.Combine(temp.Root, "settings.backup.json");

        File.WriteAllText(
            primary,
            "{\"schemaVersion\":2,\"dataRootPath\":\"C:\\\\Future\"}");
        File.WriteAllText(
            backup,
            "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Old\"}");

        byte[] primaryBefore = File.ReadAllBytes(primary);
        byte[] backupBefore = File.ReadAllBytes(backup);

        var repo = new AppSettingsRepository(
            settingsPathOverride: primary,
            backupPathOverride: backup);

        Assert.Throws<UnsupportedSettingsSchemaException>(() =>
            repo.Save(new AppSettings
            {
                SchemaVersion = 1,
                DataRootPath = @"C:\AttemptedOverwrite"
            }));

        CollectionAssert.AreEqual(primaryBefore, File.ReadAllBytes(primary));
        CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(backup));
    }

    [TestMethod]
    public void CRUU5_001_Save_with_unreadable_backup_saves_primary_and_preserves_backup()
    {
        using var temp = new TestDirectory();

        string primary = Path.Combine(temp.Root, "settings.json");
        string backup = Path.Combine(temp.Root, "settings.backup.json");

        File.WriteAllText(primary, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Old\"}");
        File.WriteAllText(backup, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Backup\"}");

        using (var lockStream = new FileStream(backup, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var repo = new AppSettingsRepository(
                settingsPathOverride: primary,
                backupPathOverride: backup);

            var result = repo.Save(new AppSettings
            {
                SchemaVersion = 1,
                DataRootPath = @"C:\NewRoot"
            });

            Assert.IsNotNull(result.Warning);
            StringAssert.Contains(result.Warning, "could not be inspected or synchronized");
            Assert.IsTrue(File.ReadAllText(primary).Contains("NewRoot"));
        }
    }

    [TestMethod]
    public void CRUU5_001_Save_with_unreadable_primary_writes_nothing()
    {
        using var temp = new TestDirectory();

        string primary = Path.Combine(temp.Root, "settings.json");
        string backup = Path.Combine(temp.Root, "settings.backup.json");

        File.WriteAllText(primary, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Old\"}");
        File.WriteAllText(backup, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Backup\"}");

        using (var lockStream = new FileStream(primary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var repo = new AppSettingsRepository(
                settingsPathOverride: primary,
                backupPathOverride: backup);

            Assert.Throws<SettingsReadException>(() =>
                repo.Save(new AppSettings
                {
                    SchemaVersion = 1,
                    DataRootPath = @"C:\NewRoot"
                }));

            Assert.IsTrue(File.ReadAllText(backup).Contains("Backup"));
        }
    }

    [TestMethod]
    public void CRUU5_002_Settings_missing_schemaVersion_is_corrupt()
    {
        using var temp = new TestDirectory();
        string settings = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(settings, "{\"dataRootPath\":\"C:\\\\Data\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settings);
        Assert.Throws<InvalidDataException>(() => repo.LoadOrRecover());
    }

    [TestMethod]
    public void CRUU5_002_Settings_duplicate_same_case_schemaVersion_is_corrupt()
    {
        using var temp = new TestDirectory();
        string settings = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(settings, "{\"schemaVersion\":1,\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Data\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settings);
        Assert.Throws<InvalidDataException>(() => repo.LoadOrRecover());
    }

    [TestMethod]
    public void CRUU5_002_Settings_duplicate_case_variant_schemaVersion_is_corrupt()
    {
        using var temp = new TestDirectory();
        string settings = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(settings, "{\"schemaVersion\":2,\"SchemaVersion\":1,\"dataRootPath\":\"C:\\\\Data\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settings);
        Assert.Throws<InvalidDataException>(() => repo.LoadOrRecover());
    }

    [TestMethod]
    public void CRUU5_002_Future_schema_cannot_be_hidden_by_duplicate()
    {
        using var temp = new TestDirectory();
        string settings = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(settings, "{\"schemaVersion\":1,\"SchemaVersion\":2,\"dataRootPath\":\"C:\\\\Data\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settings);
        Assert.Throws<InvalidDataException>(() => repo.LoadOrRecover());
    }

    [TestMethod]
    public void CRUU5_002_Noninteger_schemaVersion_is_corrupt()
    {
        using var temp = new TestDirectory();
        string settings = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(settings, "{\"schemaVersion\":\"1\",\"dataRootPath\":\"C:\\\\Data\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settings);
        Assert.Throws<InvalidDataException>(() => repo.LoadOrRecover());
    }

    [TestMethod]
    public void CRUU5_002_Nonobject_settings_root_is_corrupt()
    {
        using var temp = new TestDirectory();
        string settings = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(settings, "[{\"schemaVersion\":1}]");

        var repo = new AppSettingsRepository(settingsPathOverride: settings);
        Assert.Throws<InvalidDataException>(() => repo.LoadOrRecover());
    }

    [TestMethod]
    public void CRUU5_005_SaveIfUnchanged_accepts_unchanged_primary()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Old\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        var snapshot = repo.LoadForTransitionAndCapturePrecondition();

        var result = repo.SaveIfUnchanged(new AppSettings
        {
            SchemaVersion = 1,
            DataRootPath = @"C:\New"
        }, snapshot.Precondition);

        Assert.IsNull(result.Warning);
        Assert.AreEqual(Path.GetFullPath(@"C:\New"), repo.GetEffectiveDataRoot());
    }

    [TestMethod]
    public void CRUU5_005_SaveIfUnchanged_rejects_changed_primary()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Old\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        var snapshot = repo.LoadForTransitionAndCapturePrecondition();

        // Simulate concurrent mutation
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Concurrent\"}");

        Assert.Throws<InvalidOperationException>(() =>
            repo.SaveIfUnchanged(new AppSettings
            {
                SchemaVersion = 1,
                DataRootPath = @"C:\New"
            }, snapshot.Precondition));

        Assert.AreEqual(Path.GetFullPath(@"C:\Concurrent"), repo.GetEffectiveDataRoot());
    }

    [TestMethod]
    public void CRUU5_005_SaveIfUnchanged_rejects_appearing_primary()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        var snapshot = repo.LoadForTransitionAndCapturePrecondition();
        Assert.IsFalse(snapshot.Precondition.Primary.Exists);

        // File appears unexpectedly
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Appeared\"}");

        Assert.Throws<InvalidOperationException>(() =>
            repo.SaveIfUnchanged(new AppSettings
            {
                SchemaVersion = 1,
                DataRootPath = @"C:\New"
            }, snapshot.Precondition));
    }

    [TestMethod]
    public void CRUU5_005_SaveIfUnchanged_rejects_disappearing_primary()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Old\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        var snapshot = repo.LoadForTransitionAndCapturePrecondition();
        Assert.IsTrue(snapshot.Precondition.Primary.Exists);

        // File deleted unexpectedly
        File.Delete(settingsPath);

        Assert.Throws<InvalidOperationException>(() =>
            repo.SaveIfUnchanged(new AppSettings
            {
                SchemaVersion = 1,
                DataRootPath = @"C:\New"
            }, snapshot.Precondition));
    }

    [TestMethod]
    public void CRUU6_003_Backup_change_invalidates_transition_precondition()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        string backupPath = Path.Combine(temp.Root, "settings.backup.json");
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Data\"}");
        File.WriteAllText(backupPath, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Data\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath, backupPathOverride: backupPath);
        var snapshot = repo.LoadForTransitionAndCapturePrecondition();

        // Mutate backup externally
        File.WriteAllText(backupPath, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\MutatedBackup\"}");

        Assert.Throws<InvalidOperationException>(() =>
            repo.SaveIfUnchanged(new AppSettings
            {
                SchemaVersion = 1,
                DataRootPath = @"C:\Third"
            }, snapshot.Precondition));
    }

    [TestMethod]
    public void CRUU6_003_Backup_appearing_invalidates_transition_precondition()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        string backupPath = Path.Combine(temp.Root, "settings.backup.json");
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Data\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath, backupPathOverride: backupPath);
        var snapshot = repo.LoadForTransitionAndCapturePrecondition();

        // Ensure backup did not exist in captured precondition
        if (File.Exists(backupPath)) File.Delete(backupPath);
        var tokenWithoutBackup = repo.CaptureWritePreconditionCore();

        // Backup appears externally
        File.WriteAllText(backupPath, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\AppearedBackup\"}");

        Assert.Throws<InvalidOperationException>(() =>
            repo.SaveIfUnchanged(new AppSettings
            {
                SchemaVersion = 1,
                DataRootPath = @"C:\Third"
            }, tokenWithoutBackup));
    }

    [TestMethod]
    public void CRUU6_003_Backup_disappearing_invalidates_transition_precondition()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        string backupPath = Path.Combine(temp.Root, "settings.backup.json");
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Data\"}");
        File.WriteAllText(backupPath, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Data\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath, backupPathOverride: backupPath);
        var snapshot = repo.LoadForTransitionAndCapturePrecondition();

        // Backup deleted externally
        File.Delete(backupPath);

        Assert.Throws<InvalidOperationException>(() =>
            repo.SaveIfUnchanged(new AppSettings
            {
                SchemaVersion = 1,
                DataRootPath = @"C:\Third"
            }, snapshot.Precondition));
    }

    [TestMethod]
    public void CRUU6_003_Future_backup_appearing_before_commit_is_not_overwritten()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        string backupPath = Path.Combine(temp.Root, "settings.backup.json");
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Data\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath, backupPathOverride: backupPath);
        var snapshot = repo.LoadForTransitionAndCapturePrecondition();

        // Future backup appears
        File.WriteAllText(backupPath, "{\"schemaVersion\":99,\"dataRootPath\":\"C:\\\\Future\"}");

        Assert.Throws<InvalidOperationException>(() =>
            repo.SaveIfUnchanged(new AppSettings
            {
                SchemaVersion = 1,
                DataRootPath = @"C:\Third"
            }, snapshot.Precondition));

        // Verify future backup is untouched
        Assert.IsTrue(File.ReadAllText(backupPath).Contains("\"schemaVersion\":99"));
    }

    [TestMethod]
    public void CRUU6_003_Final_compare_and_write_happen_under_settings_lease()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        string backupPath = Path.Combine(temp.Root, "settings.backup.json");
        string lockPath = Path.Combine(temp.Root, ".settings.lock");
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Data\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath, backupPathOverride: backupPath);
        var snapshot = repo.LoadForTransitionAndCapturePrecondition();

        // Hold settings lock externally
        using var externalLease = SettingsMutationLease.Acquire(lockPath);

        // Attempting to Save with timeout 100ms should fail due to lock contention
        Assert.Throws<InvalidOperationException>(() =>
            repo.SaveIfUnchanged(new AppSettings
            {
                SchemaVersion = 1,
                DataRootPath = @"C:\Third"
            }, snapshot.Precondition));
    }

    [TestMethod]
    public void CRUU6_004_Post_recovery_precondition_does_not_self_invalidate()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        string backupPath = Path.Combine(temp.Root, "settings.backup.json");
        string customPath = Path.Combine(temp.Root, "Data");

        // Primary corrupt, backup valid
        File.WriteAllText(settingsPath, "corrupt json");
        File.WriteAllText(backupPath, $"{{\"schemaVersion\": 1, \"dataRootPath\": \"{customPath.Replace("\\", "\\\\")}\"}}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath, backupPathOverride: backupPath);

        // LoadForTransitionAndCapturePrecondition executes recovery and captures token AFTER repair
        var snapshot = repo.LoadForTransitionAndCapturePrecondition();

        Assert.IsNotNull(snapshot.Warning);
        Assert.AreEqual(Path.GetFullPath(customPath), snapshot.Settings.DataRootPath);

        // Commit new settings with the captured token - should succeed without false CAS mismatch
        var saveResult = repo.SaveIfUnchanged(new AppSettings
        {
            SchemaVersion = 1,
            DataRootPath = Path.Combine(temp.Root, "NewData")
        }, snapshot.Precondition);

        Assert.IsNull(saveResult.Warning);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(temp.Root, "NewData")), repo.GetEffectiveDataRoot());
    }
}
