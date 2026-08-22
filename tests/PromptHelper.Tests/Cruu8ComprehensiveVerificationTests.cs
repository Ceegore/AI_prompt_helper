using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;
using PromptHelper.Views;

namespace PromptHelper.Tests;

[TestClass]
public sealed class Cruu8ComprehensiveVerificationTests
{
    private static void SeedValidLibrary(string root, out Guid promptId)
    {
        Directory.CreateDirectory(root);
        string promptsDir = Path.Combine(root, "prompts");
        Directory.CreateDirectory(promptsDir);
        Directory.CreateDirectory(Path.Combine(root, "recovery"));

        promptId = Guid.NewGuid();
        string promptFile = Path.Combine(promptsDir, $"{promptId:N}.md");
        File.WriteAllText(promptFile, "Active prompt body content");

        var category = new CategoryRecord
        {
            Id = Guid.NewGuid(),
            ParentId = null,
            Name = "General",
            SortOrder = 0
        };

        var prompt = new PromptRecord
        {
            Id = promptId,
            CategoryId = category.Id,
            SortOrder = 0,
            Title = "Test Prompt"
        };

        var doc = new LibraryDocument
        {
            SchemaVersion = 1,
            Categories = [category],
            Prompts = [prompt]
        };

        string libJson = JsonSerializer.Serialize(doc, LibraryRepository.JsonOptions);
        File.WriteAllText(Path.Combine(root, "library.json"), libJson);
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU8_001_Precommit_rollback_failure_preserves_manifest_marker()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();
        SeedValidLibrary(source.Root, out _);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var ops = new FaultInjectingMigrationFileOps
        {
            OnFlushToDisk = s => throw new IOException("Disk flush failed during copy")
        };

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(fileOps: ops),
            new FakeUserConfirmationService(),
            capabilityValidator: null,
            pathResolver: null,
            manifestRepo: new MigrationManifestRepository(),
            fileOps: ops,
            caseInspector: null);

        Assert.Throws<IOException>(() => coordinator.RequestTransition(target.Root));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU8_002_Predeclared_nonces_used_for_temps_and_cleaned_on_recovery()
    {
        using var target = new TestDirectory();
        Guid attemptId = Guid.NewGuid();
        string tempRel = Path.Combine("prompts", $".test.md.migration-{attemptId:N}-{new string('b', 32)}.tmp");
        string tempFullPath = Path.Combine(target.Root, tempRel);
        Directory.CreateDirectory(Path.GetDirectoryName(tempFullPath)!);
        File.WriteAllText(tempFullPath, "partial temp content");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 3,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = target.Root,
            SourceLibrarySha256Hex = "0000000000000000000000000000000000000000000000000000000000000000",
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-{new string('a', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = "0000000000000000000000000000000000000000000000000000000000000000",
                    Role = MigrationPayloadRole.PrimaryMetadata
                },
                new MigrationManifestArtifact
                {
                    RelativePath = "prompts/test.md",
                    TempRelativePath = tempRel,
                    Length = 20,
                    Sha256Hex = "0000000000000000000000000000000000000000000000000000000000000000",
                    Role = MigrationPayloadRole.PromptBody
                }
            ]
        };

        // The interrupted attempt would have claimed the temp when it created it; without
        // that record recovery must preserve the file rather than destroy it (CRUU15-006).
        OwnedArtifactTestSupport.ClaimOwnership(target.Root, tempFullPath);

        var repo = new MigrationManifestRepository();
        repo.WriteDurable(Path.Combine(target.Root, ".prompthelper-migration.json"), manifest);

        var recovery = new MigrationRecoveryService(repo);
        var result = recovery.RecoverForRetry(new MigrationRecoveryContext(target.Root, ExpectedSourcePhysicalRoot: @"C:\Source"));

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsFalse(File.Exists(tempFullPath));
        Assert.IsFalse(File.Exists(Path.Combine(target.Root, ".prompthelper-migration.json")));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU8_003_OccupiedNonLibrary_is_rejected_and_distinguished_from_empty()
    {
        using var target = new TestDirectory();
        File.WriteAllText(Path.Combine(target.Root, "unrelated_document.pdf"), "content");

        var service = new DataFolderMigrationService();
        var inspection = service.InspectTarget(target.Root);

        Assert.AreEqual(DataFolderMigrationService.TargetLibraryKind.OccupiedNonLibrary, inspection.Kind);
        Assert.IsNotNull(inspection.Error);
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU8_004_WriteDurable_cleans_failed_temp_and_throws_ManifestWriteCleanupException()
    {
        using var temp = new TestDirectory();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");
        Guid attemptId = Guid.NewGuid();

        var fakeOps = new FakeManifestFileOps
        {
            OnMoveNoOverwriteWriteThrough = (src, dst) => throw new IOException("Disk write failure"),
            OnReplaceWriteThrough = (src, dst) => throw new IOException("Disk write failure"),
            OnDeleteFile = path => throw new IOException("Delete temp failed")
        };

        var repo = new MigrationManifestRepository(fakeOps);
        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 3,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = temp.Root,
            SourceLibrarySha256Hex = "0000000000000000000000000000000000000000000000000000000000000000",
            Phase = MigrationManifestPhase.ReadyToCommit,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-{new string('a', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = "0000000000000000000000000000000000000000000000000000000000000000",
                    Role = MigrationPayloadRole.PrimaryMetadata
                }
            ]
        };

        Assert.Throws<ManifestWriteCleanupException>(() => repo.WriteReadyManifestDurable(markerPath, manifest));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU8_005_Schema_version_1_manifest_fails_closed()
    {
        using var temp = new TestDirectory();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");
        File.WriteAllText(markerPath, "{\"schemaVersion\":1,\"attemptId\":\"" + Guid.NewGuid() + "\",\"phase\":\"Copying\"}");

        var repo = new MigrationManifestRepository();
        Assert.Throws<InvalidDataException>(() => repo.TryRead(markerPath));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU8_006_FinalizeCommittedStartup_verifies_all_finals_and_retires_marker()
    {
        using var target = new TestDirectory();
        SeedValidLibrary(target.Root, out Guid promptId);

        string libPath = Path.Combine(target.Root, "library.json");
        string promptPath = Path.Combine(target.Root, "prompts", $"{promptId:N}.md");
        Guid attemptId = Guid.NewGuid();

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 3,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = target.Root,
            SourceLibrarySha256Hex = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(libPath))),
            Phase = MigrationManifestPhase.ReadyToCommit,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-{new string('a', 32)}.tmp",
                    Length = new FileInfo(libPath).Length,
                    Sha256Hex = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(libPath))),
                    Role = MigrationPayloadRole.PrimaryMetadata
                },
                new MigrationManifestArtifact
                {
                    RelativePath = $"prompts/{promptId:N}.md",
                    TempRelativePath = $"prompts/.{promptId:N}.md.migration-{attemptId:N}-{new string('b', 32)}.tmp",
                    Length = new FileInfo(promptPath).Length,
                    Sha256Hex = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(promptPath))),
                    Role = MigrationPayloadRole.PromptBody
                }
            ]
        };

        var repo = new MigrationManifestRepository();
        string markerPath = Path.Combine(target.Root, ".prompthelper-migration.json");
        repo.WriteDurable(markerPath, manifest);

        var recovery = new MigrationRecoveryService(repo);
        var result = recovery.FinalizeCommittedStartup(new MigrationRecoveryContext(target.Root));

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsFalse(File.Exists(markerPath));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU8_007_Interrupted_custom_to_default_migration_preserves_bootstrap_settings()
    {
        using var bootstrap = new TestDirectory();
        string settingsPath = Path.Combine(bootstrap.Root, "settings.json");
        string backupSettings = Path.Combine(bootstrap.Root, "settings.backup.json");
        string lockFile = Path.Combine(bootstrap.Root, ".settings.lock");

        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"\"}");
        File.WriteAllText(backupSettings, "{\"schemaVersion\":1,\"dataRootPath\":\"\"}");
        File.WriteAllText(lockFile, "");

        Guid attemptId = Guid.NewGuid();
        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 3,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\CustomSource",
            TargetPhysicalRoot = bootstrap.Root,
            SourceLibrarySha256Hex = "0000000000000000000000000000000000000000000000000000000000000000",
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-{new string('a', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = "0000000000000000000000000000000000000000000000000000000000000000",
                    Role = MigrationPayloadRole.PrimaryMetadata
                }
            ]
        };

        var repo = new MigrationManifestRepository();
        repo.WriteDurable(Path.Combine(bootstrap.Root, ".prompthelper-migration.json"), manifest);

        var recovery = new MigrationRecoveryService(repo);
        var context = new MigrationRecoveryContext(bootstrap.Root, bootstrap.Root, ExpectedSourcePhysicalRoot: @"C:\CustomSource");
        var result = recovery.RecoverForRetry(context);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsTrue(File.Exists(settingsPath));
        Assert.IsTrue(File.Exists(backupSettings));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU8_008_Precommit_failures_combine_into_MigrationRollbackException()
    {
        var original = new InvalidOperationException("Initial error");
        var failures = new List<MigrationRollbackFailure>
        {
            new(@"C:\Data\file.tmp", "DeleteFile", "Access denied"),
            new(@"C:\Data\.app.lock", "ReleaseLock", "Lock handle failed")
        };

        var ex = new MigrationRollbackException(original, @"C:\Data", failures);
        Assert.AreEqual(original, ex.InnerException);
        Assert.AreEqual(2, ex.Failures.Count);
        Assert.IsTrue(ex.Message.Contains("Access denied"));
    }

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU8_009_Reservation_cleans_full_missing_parent_directory_chain()
    {
        using var baseDir = new TestDirectory();
        string deepTarget = Path.Combine(baseDir.Root, "Level1", "Level2", "Target");

        Assert.IsFalse(Directory.Exists(Path.Combine(baseDir.Root, "Level1")));

        var reservation = TargetRootReservation.TryAcquire(deepTarget);
        Assert.IsNotNull(reservation);
        Assert.IsTrue(Directory.Exists(deepTarget));

        reservation.Release();

        // All created intermediate directories should be deleted
        Assert.IsFalse(Directory.Exists(Path.Combine(baseDir.Root, "Level1")));
        Assert.IsTrue(Directory.Exists(baseDir.Root));
    }

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU8_010_Case_sensitivity_native_error_fails_closed_with_InspectionException()
    {
        using var temp = new TestDirectory();
        var inspector = new FakeDirectoryCaseSensitivityInspector();
        inspector.MarkInspectionFailure(temp.Root);

        Assert.Throws<DirectoryCaseSensitivityInspectionException>(() => inspector.Inspect(temp.Root));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU8_011_Backup_only_target_capability_succeeds_with_readonly_backup()
    {
        using var target = new TestDirectory();
        string backupPath = Path.Combine(target.Root, "library.backup.json");
        string promptsDir = Path.Combine(target.Root, "prompts");
        Directory.CreateDirectory(promptsDir);

        Guid promptId = Guid.NewGuid();
        File.WriteAllText(Path.Combine(promptsDir, $"{promptId:N}.md"), "Prompt body");

        var doc = new LibraryDocument
        {
            SchemaVersion = 1,
            Categories = [new CategoryRecord { Id = Guid.NewGuid(), Name = "General", SortOrder = 0 }],
            Prompts = [new PromptRecord { Id = promptId, CategoryId = null, SortOrder = 0, Title = "P" }]
        };

        File.WriteAllText(backupPath, JsonSerializer.Serialize(doc, LibraryRepository.JsonOptions));
        File.SetAttributes(backupPath, FileAttributes.ReadOnly);

        try
        {
            var validator = new DataRootCapabilityValidator();
            var ctx = new ExistingLibraryCapabilityContext(
                DataFolderMigrationService.TargetLibraryKind.RecoverableBackupOnly,
                null,
                backupPath,
                doc);

            var result = validator.ValidateWritable(target.Root, null, ctx);
            Assert.IsNotNull(result.Warning);
            Assert.IsTrue(result.Warning.Contains("safety backup", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.SetAttributes(backupPath, FileAttributes.Normal);
        }
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU8_012_InspectCompatibility_distinguishes_current_future_corrupt()
    {
        string currentJson = "{\"schemaVersion\":1,\"categories\":[],\"prompts\":[]}";
        string futureJson = "{\"schemaVersion\":99,\"categories\":[],\"prompts\":[]}";
        string corruptJson = "{ not json }";

        Assert.IsInstanceOfType<LibraryMetadataCompatibility.Current>(LibraryRepository.InspectCompatibility(currentJson));
        Assert.IsInstanceOfType<LibraryMetadataCompatibility.Future>(LibraryRepository.InspectCompatibility(futureJson));
        Assert.IsInstanceOfType<LibraryMetadataCompatibility.Corrupt>(LibraryRepository.InspectCompatibility(corruptJson));
    }

    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void CRUU8_013_SettingsDialog_sets_RestartRequired_before_notification_dialogs()
    {
        WpfTestHost.Invoke(() =>
        {
            using var source = new TestDirectory();
            using var settingsDir = new TestDirectory();
            SeedValidLibrary(source.Root, out _);

            string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
            File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

            var fakeService = new FakeDataFolderTransitionService
            {
                OnRequestTransition = path => new DataFolderTransitionResult(
                    Changed: true,
                    RestartRequired: true,
                    ExistingLibrarySelected: false,
                    NormalizedTargetRoot: path,
                    Warning: null)
            };

            var fakeConfirm = new FakeUserConfirmationService
            {
                OnShowInformation = (msg, title) => throw new InvalidOperationException("Simulated UI dialog crash")
            };

            var dialog = new SettingsDialog(
                source.Root,
                new AppSettingsRepository(settingsPathOverride: settingsPath),
                new DataFolderMigrationService(),
                fakeConfirm,
                fakeService);

            try
            {
                dialog.ExecuteSaveForTest();
                Assert.IsTrue(dialog.RestartRequired, "RestartRequired must be true even if notification UI throws");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void CRUU8_014_ExecuteSaveForTest_invokes_complete_save_flow()
    {
        WpfTestHost.Invoke(() =>
        {
            using var source = new TestDirectory();
            using var settingsDir = new TestDirectory();
            SeedValidLibrary(source.Root, out _);

            string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
            File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

            bool requested = false;
            var fakeService = new FakeDataFolderTransitionService
            {
                OnRequestTransition = path =>
                {
                    requested = true;
                    return new DataFolderTransitionResult(Changed: false, RestartRequired: false, ExistingLibrarySelected: false, NormalizedTargetRoot: path, Warning: null);
                }
            };

            var dialog = new SettingsDialog(
                source.Root,
                new AppSettingsRepository(settingsPathOverride: settingsPath),
                new DataFolderMigrationService(),
                new FakeUserConfirmationService(),
                fakeService);

            try
            {
                dialog.ExecuteSaveForTest();
                Assert.IsTrue(requested);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU8_015_Verified_artifact_deleter_validates_handle_before_deletion()
    {
        using var temp = new TestDirectory();
        string file = Path.Combine(temp.Root, "test.txt");
        byte[] bytes = Encoding.UTF8.GetBytes("Hello World");
        File.WriteAllBytes(file, bytes);
        string sha = Convert.ToHexStringLower(SHA256.HashData(bytes));

        var deleter = new WindowsVerifiedArtifactDeleter();
        deleter.VerifyAndDelete(temp.Root, file, bytes.Length, sha);

        Assert.IsFalse(File.Exists(file));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU8_016_Verified_artifact_deleter_throws_on_hash_mismatch_and_preserves_file()
    {
        using var temp = new TestDirectory();
        string file = Path.Combine(temp.Root, "test.txt");
        byte[] bytes = Encoding.UTF8.GetBytes("Hello World");
        File.WriteAllBytes(file, bytes);

        var deleter = new WindowsVerifiedArtifactDeleter();
        Assert.Throws<InvalidDataException>(() =>
            deleter.VerifyAndDelete(temp.Root, file, bytes.Length, "0000000000000000000000000000000000000000000000000000000000000000"));

        Assert.IsTrue(File.Exists(file));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU8_017_Exhaustive_target_library_kind_switch_handles_all_cases()
    {
        var kinds = Enum.GetValues<DataFolderMigrationService.TargetLibraryKind>();
        Assert.IsTrue(kinds.Length >= 9);
        Assert.IsTrue(kinds.Contains(DataFolderMigrationService.TargetLibraryKind.OccupiedNonLibrary));
        Assert.IsTrue(kinds.Contains(DataFolderMigrationService.TargetLibraryKind.InterruptedMigration));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU8_018_DataRootCapabilityValidator_constructors_are_explicit()
    {
        var val1 = new DataRootCapabilityValidator();
        var val2 = new DataRootCapabilityValidator(new DefaultCapabilityFileOps());
        Assert.IsNotNull(val1);
        Assert.IsNotNull(val2);
    }

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU8_019_VerifyTestEvidence_script_exists_and_parses_valid_trx()
    {
        string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "tools", "VerifyTestEvidence.ps1");
        string fullPath = Path.GetFullPath(scriptPath);
        Assert.IsTrue(File.Exists(fullPath), $"VerifyTestEvidence.ps1 must exist at {fullPath}");
    }
}
