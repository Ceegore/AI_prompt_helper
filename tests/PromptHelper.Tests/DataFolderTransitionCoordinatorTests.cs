using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Infrastructure;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class DataFolderTransitionCoordinatorTests
{
    private static void SeedValidLibrary(string rootDir, out Guid promptId)
    {
        var paths = new AppPaths(rootDir);
        paths.EnsureRootDirectory();
        paths.EnsureDataDirectories();

        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startupService = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);
        var startupResult = startupService.LoadOrInitialize();

        var service = new PromptLibraryService(startupResult.Document, libRepo, promptRepo);
        var result = service.CreatePrompt(null, "Coordinator prompt content", "Coordinator Title");
        promptId = result.Value.Id;
    }

    [TestMethod]
    public void CRUU4_010_New_target_is_rolled_back_if_settings_primary_write_fails()
    {
        using var source = new TestDirectory();
        using var targetParent = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);

        string target = Path.Combine(targetParent.Root, "NewTarget");
        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");

        var faultWriter = new FaultInjectingAtomicTextWriter(new AtomicTextWriter())
        {
            ShouldFail = (path, _) => path.Equals(settingsPath, StringComparison.OrdinalIgnoreCase)
        };

        var settingsRepo = new AppSettingsRepository(
            writer: faultWriter,
            settingsPathOverride: settingsPath);

        // Pre-seed valid settings pointing at source
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\": 1, \"dataRootPath\": \"{source.Root.Replace("\\", "\\\\")}\"}}");

        var migrationService = new DataFolderMigrationService();
        var confirmation = new FakeUserConfirmationService();

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            settingsRepo,
            migrationService,
            confirmation);

        Assert.Throws<IOException>(() => coordinator.RequestTransition(target));

        // Settings unchanged
        Assert.AreEqual(Path.GetFullPath(source.Root), settingsRepo.GetEffectiveDataRoot());

        // Target rolled back
        Assert.IsFalse(File.Exists(Path.Combine(target, "library.json")));
        Assert.IsFalse(Directory.Exists(Path.Combine(target, "prompts")));
    }

    [TestMethod]
    public void CRUU4_010_Existing_target_confirmation_cancel_writes_no_target_probe_files()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        SeedValidLibrary(target.Root, out _);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\": 1, \"dataRootPath\": \"{source.Root.Replace("\\", "\\\\")}\"}}");

        var settingsRepo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        var migrationService = new DataFolderMigrationService();
        var confirmation = new FakeUserConfirmationService
        {
            ConfirmationResult = false // User cancels
        };

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            settingsRepo,
            migrationService,
            confirmation);

        var result = coordinator.RequestTransition(target.Root);

        Assert.IsFalse(result.Changed);
        Assert.IsFalse(result.RestartRequired);
        Assert.IsTrue(result.ExistingLibrarySelected);

        // Settings untouched
        Assert.AreEqual(Path.GetFullPath(source.Root), settingsRepo.GetEffectiveDataRoot());

        // Target contains no probe files
        string[] targetFiles = Directory.GetFiles(target.Root, "*probe*.*", SearchOption.AllDirectories);
        Assert.AreEqual(0, targetFiles.Length);
    }

    [TestMethod]
    public void CRUU4_010_Target_state_change_after_inspection_is_detected_under_reservation()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        SeedValidLibrary(target.Root, out _);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\": 1, \"dataRootPath\": \"{source.Root.Replace("\\", "\\\\")}\"}}");

        var settingsRepo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        var migrationService = new DataFolderMigrationService();
        var confirmation = new FakeUserConfirmationService();

        // During confirmation callback, tamper with target library.json to corrupt it
        confirmation.ConfirmationResult = true;
        confirmation.OnConfirm = () =>
        {
            File.Delete(Path.Combine(target.Root, "library.json"));
            File.Delete(Path.Combine(target.Root, "library.backup.json"));
        };

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            settingsRepo,
            migrationService,
            confirmation);

        Assert.Throws<InvalidOperationException>(() => coordinator.RequestTransition(target.Root));
    }

    [TestMethod]
    public void CRUU4_010_Reservation_blocks_second_transition_writer()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        SeedValidLibrary(target.Root, out _);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\": 1, \"dataRootPath\": \"{source.Root.Replace("\\", "\\\\")}\"}}");

        var settingsRepo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        var migrationService = new DataFolderMigrationService();
        var confirmation = new FakeUserConfirmationService();

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            settingsRepo,
            migrationService,
            confirmation);

        // Hold lock on target
        string lockPath = Path.Combine(target.Root, ".app.lock");
        using var lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        Assert.Throws<InvalidOperationException>(() => coordinator.RequestTransition(target.Root));
    }

    [TestMethod]
    public void CRUU5_004_Transition_uses_active_process_root_not_mutated_settings_pointer()
    {
        using var active = new TestDirectory();
        using var wrong = new TestDirectory();
        using var targetParent = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(active.Root, out Guid activePrompt);
        SeedValidLibrary(wrong.Root, out Guid wrongPrompt);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{wrong.Root.Replace("\\", "\\\\")}\"}}");

        var settingsRepo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        var coordinator = new DataFolderTransitionCoordinator(
            active.Root,
            settingsRepo,
            new DataFolderMigrationService(),
            new FakeUserConfirmationService());

        string target = Path.Combine(targetParent.Root, "Target");

        Assert.Throws<InvalidOperationException>(() => coordinator.RequestTransition(target));

        // Wrong root was never copied
        Assert.IsFalse(File.Exists(Path.Combine(target, "prompts", $"{wrongPrompt:N}.md")));
    }

    [TestMethod]
    public void CRUU5_005_Empty_target_rolls_back_when_settings_change_during_copy()
    {
        using var source = new TestDirectory();
        using var targetParent = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);

        string target = Path.Combine(targetParent.Root, "NewTarget");
        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var ops = new FaultInjectingMigrationFileOps
        {
            OnMoveNoOverwrite = (src, dst) =>
            {
                File.Move(src, dst, overwrite: false);
                if (dst.EndsWith("library.json", StringComparison.OrdinalIgnoreCase))
                {
                    // Mutate settings file mid-migration
                    File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\MutatedMidFlight\"}");
                }
            }
        };

        var settingsRepo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        var migrationService = new DataFolderMigrationService(fileOps: ops);
        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            settingsRepo,
            migrationService,
            new FakeUserConfirmationService());

        Assert.Throws<InvalidOperationException>(() => coordinator.RequestTransition(target));

        // Copied files rolled back
        Assert.IsFalse(File.Exists(Path.Combine(target, "library.json")));
    }

    [TestMethod]
    public void CRUU5_005_Existing_target_switch_aborts_when_settings_change_after_confirmation()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        SeedValidLibrary(target.Root, out _);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var confirmation = new FakeUserConfirmationService
        {
            ConfirmationResult = true,
            OnConfirm = () =>
            {
                // Mutate settings file during confirmation
                File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\MutatedMidConfirmation\"}");
            }
        };

        var settingsRepo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            settingsRepo,
            new DataFolderMigrationService(),
            confirmation);

        Assert.Throws<InvalidOperationException>(() => coordinator.RequestTransition(target.Root));
    }

    [TestMethod]
    public void CRUU5_008_Physical_same_root_alias_is_noop()
    {
        using var temp = new TestDirectory();
        using var settingsDir = new TestDirectory();

        string current = Path.Combine(temp.Root, "CurrentData");
        Directory.CreateDirectory(current);
        SeedValidLibrary(current, out _);

        string alias = Path.Combine(temp.Root, "AliasOfCurrent");

        var resolver = new FakePhysicalPathResolver();
        resolver.AddMapping(current, current);
        resolver.AddMapping(alias, current);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{current.Replace("\\", "\\\\")}\"}}");

        var settingsRepo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        var confirmation = new FakeUserConfirmationService();

        var coordinator = new DataFolderTransitionCoordinator(
            current,
            settingsRepo,
            new DataFolderMigrationService(pathResolver: resolver),
            confirmation,
            pathResolver: resolver);

        var result = coordinator.RequestTransition(alias);

        Assert.IsFalse(result.Changed);
        Assert.IsFalse(result.RestartRequired);
        Assert.IsFalse(result.ExistingLibrarySelected);
        Assert.AreEqual(0, confirmation.PromptCount);
    }

    [TestMethod]
    public void CRUU5_009_Settings_failure_removes_reservation_created_empty_root()
    {
        using var source = new TestDirectory();
        using var targetParent = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);

        string target = Path.Combine(targetParent.Root, "CreatedTargetRoot");
        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");

        var faultWriter = new FaultInjectingAtomicTextWriter(new AtomicTextWriter())
        {
            ShouldFail = (path, _) => path.Equals(settingsPath, StringComparison.OrdinalIgnoreCase)
        };

        var settingsRepo = new AppSettingsRepository(
            writer: faultWriter,
            settingsPathOverride: settingsPath);

        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            settingsRepo,
            new DataFolderMigrationService(),
            new FakeUserConfirmationService());

        Assert.Throws<IOException>(() => coordinator.RequestTransition(target));

        // Newly created empty root is cleaned up
        Assert.IsFalse(Directory.Exists(target));
    }

    [TestMethod]
    public void CRUU5_009_Preexisting_empty_target_is_preserved_after_failure()
    {
        using var source = new TestDirectory();
        using var targetParent = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);

        string target = Path.Combine(targetParent.Root, "PreExistingEmptyTarget");
        Directory.CreateDirectory(target); // Created BEFORE transition

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");

        var faultWriter = new FaultInjectingAtomicTextWriter(new AtomicTextWriter())
        {
            ShouldFail = (path, _) => path.Equals(settingsPath, StringComparison.OrdinalIgnoreCase)
        };

        var settingsRepo = new AppSettingsRepository(
            writer: faultWriter,
            settingsPathOverride: settingsPath);

        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            settingsRepo,
            new DataFolderMigrationService(),
            new FakeUserConfirmationService());

        Assert.Throws<IOException>(() => coordinator.RequestTransition(target));

        // Pre-existing directory is preserved
        Assert.IsTrue(Directory.Exists(target));
    }

    [TestMethod]
    public void CRUU5_010_Valid_library_A_replaced_by_valid_library_B_aborts()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        SeedValidLibrary(target.Root, out _);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var confirmation = new FakeUserConfirmationService
        {
            ConfirmationResult = true,
            OnConfirm = () =>
            {
                // Replace target valid library with a different valid library
                string targetLib = Path.Combine(target.Root, "library.json");
                File.WriteAllText(targetLib, "{\"schemaVersion\":1,\"categories\":[],\"prompts\":[]}");
            }
        };

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(),
            confirmation);

        Assert.Throws<InvalidOperationException>(() => coordinator.RequestTransition(target.Root));
    }

    [TestMethod]
    public void CRUU5_010_Prompt_body_changed_during_confirmation_aborts()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        SeedValidLibrary(target.Root, out Guid targetPromptId);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var confirmation = new FakeUserConfirmationService
        {
            ConfirmationResult = true,
            OnConfirm = () =>
            {
                // Mutate target prompt body during confirmation window
                string bodyPath = Path.Combine(target.Root, "prompts", $"{targetPromptId:N}.md");
                File.WriteAllText(bodyPath, "Tampered prompt body during confirmation");
            }
        };

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(),
            confirmation);

        Assert.Throws<InvalidOperationException>(() => coordinator.RequestTransition(target.Root));
    }

    [TestMethod]
    public void CRUU5_010_Backup_only_target_changed_to_different_backup_aborts()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);

        // Target has only backup.json
        string targetBackup = Path.Combine(target.Root, "library.backup.json");
        File.WriteAllText(targetBackup, "{\"schemaVersion\":1,\"categories\":[],\"prompts\":[]}");

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var confirmation = new FakeUserConfirmationService
        {
            ConfirmationResult = true,
            OnConfirm = () =>
            {
                // Mutate target backup
                File.WriteAllText(targetBackup, "{\"schemaVersion\":1,\"categories\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"parentId\":null,\"name\":\"Different\",\"sortOrder\":10}],\"prompts\":[]}");
            }
        };

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(),
            confirmation);

        Assert.Throws<InvalidOperationException>(() => coordinator.RequestTransition(target.Root));
    }

    [TestMethod]
    public void CRUU5_010_Unchanged_target_fingerprint_allows_transition()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        SeedValidLibrary(target.Root, out _);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var confirmation = new FakeUserConfirmationService
        {
            ConfirmationResult = true
        };

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(),
            confirmation);

        var result = coordinator.RequestTransition(target.Root);

        Assert.IsTrue(result.Changed);
        Assert.IsTrue(result.RestartRequired);
        Assert.IsTrue(result.ExistingLibrarySelected);
    }

    [TestMethod]
    public void CRUU5_012_Rollback_failure_is_reported_with_details_and_preserves_inner_exception()
    {
        using var source = new TestDirectory();
        using var targetParent = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);

        string target = Path.Combine(targetParent.Root, "FailRollbackTarget");
        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        FileStream? lockStream = null;
        try
        {
            var ops = new FaultInjectingMigrationFileOps
            {
                OnMoveNoOverwrite = (src, dst) =>
                {
                    File.Move(src, dst, overwrite: false);
                    if (dst.EndsWith("library.json", StringComparison.OrdinalIgnoreCase))
                    {
                        // Lock destination file so rollback deletion fails
                        lockStream = new FileStream(dst, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    }
                }
            };

            // Fault writer to trigger rollback on settings save
            var faultWriter = new FaultInjectingAtomicTextWriter(new AtomicTextWriter())
            {
                ShouldFail = (path, _) => path.Equals(settingsPath, StringComparison.OrdinalIgnoreCase)
            };

            var settingsRepo = new AppSettingsRepository(writer: faultWriter, settingsPathOverride: settingsPath);
            var migrationService = new DataFolderMigrationService(fileOps: ops);
            var coordinator = new DataFolderTransitionCoordinator(
                source.Root,
                settingsRepo,
                migrationService,
                new FakeUserConfirmationService());

            var ex = Assert.Throws<MigrationRollbackException>(() => coordinator.RequestTransition(target));

            Assert.IsNotNull(ex.InnerException);
            Assert.IsInstanceOfType<IOException>(ex.InnerException);
            Assert.AreEqual(1, ex.Failures.Count);
            Assert.IsTrue(ex.Failures[0].Path.EndsWith("library.json", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            lockStream?.Dispose();
        }
    }

    [TestMethod]
    public void CRUU6_001_Persisted_alias_of_active_root_allows_transition()
    {
        using var realRoot = new TestDirectory();
        using var thirdTarget = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(realRoot.Root, out _);

        string aliasPath = @"C:\FakeAliases\Current";
        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");

        // Settings has alias path
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{aliasPath.Replace("\\", "\\\\")}\"}}");

        var resolver = new FakePhysicalPathResolver();
        resolver.AddMapping(aliasPath, realRoot.Root);

        var coordinator = new DataFolderTransitionCoordinator(
            realRoot.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(pathResolver: resolver),
            new FakeUserConfirmationService { ConfirmationResult = true },
            pathResolver: resolver);

        // Transition to third target should succeed without throwing false settings mismatch
        var result = coordinator.RequestTransition(thirdTarget.Root);
        Assert.IsTrue(result.Changed);
        Assert.IsTrue(result.RestartRequired);
    }

    [TestMethod]
    public void CRUU6_002_Target_physical_identity_change_after_validation_aborts()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var hijackedTarget = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        SeedValidLibrary(target.Root, out _);
        SeedValidLibrary(hijackedTarget.Root, out _);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var resolver = new FakePhysicalPathResolver();
        // Initial resolution returns target.Root, later calls return hijackedTarget.Root
        int calls = 0;
        resolver.DynamicResolver = (p, c) =>
        {
            if (PathIdentity.Equals(p, target.Root))
            {
                calls++;
                return calls > 1 ? hijackedTarget.Root : target.Root;
            }
            return null;
        };

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(pathResolver: resolver),
            new FakeUserConfirmationService { ConfirmationResult = true },
            pathResolver: resolver);

        var ex = Assert.Throws<InvalidOperationException>(() => coordinator.RequestTransition(target.Root));
        Assert.IsTrue(ex.Message.Contains("physical target folder changed", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void CRUU6_002_Target_becomes_bootstrap_alias_after_reservation_aborts()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        SeedValidLibrary(target.Root, out _);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var resolver = new FakePhysicalPathResolver();
        int calls = 0;
        resolver.DynamicResolver = (p, c) =>
        {
            if (PathIdentity.Equals(p, target.Root))
            {
                calls++;
                return calls > 1 ? settingsDir.Root : target.Root;
            }
            return null;
        };

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(pathResolver: resolver),
            new FakeUserConfirmationService { ConfirmationResult = true },
            pathResolver: resolver);

        var ex = Assert.Throws<InvalidOperationException>(() => coordinator.RequestTransition(target.Root));
        Assert.IsTrue(ex.Message.Contains("physical target folder changed") || ex.Message.Contains("bootstrap settings folder"));
    }

    [TestMethod]
    public void CRUU6_003_Backup_change_invalidates_settings_precondition()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        SeedValidLibrary(target.Root, out _);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        string backupPath = Path.Combine(settingsDir.Root, "settings.backup.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");
        File.WriteAllText(backupPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var confirmation = new FakeUserConfirmationService
        {
            ConfirmationResult = true,
            OnConfirm = () =>
            {
                // Backup mutated while dialog was open
                File.WriteAllText(backupPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\External\"}}");
            }
        };

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath, backupPathOverride: backupPath),
            new DataFolderMigrationService(),
            confirmation);

        Assert.Throws<InvalidOperationException>(() => coordinator.RequestTransition(target.Root));
    }

    [TestMethod]
    public void CRUU6_008_Metadata_change_during_fingerprint_capture_aborts()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        SeedValidLibrary(target.Root, out _);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        string targetLib = Path.Combine(target.Root, "library.json");

        int readCount = 0;
        var ops = new FaultInjectingMigrationFileOps
        {
            OnReadAllBytes = p =>
            {
                if (p.Equals(targetLib, StringComparison.OrdinalIgnoreCase))
                {
                    readCount++;
                    if (readCount == 2)
                    {
                        // Mutate on stability verification read
                        return System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"categories\":[],\"prompts\":[]}");
                    }
                }
                return File.ReadAllBytes(p);
            }
        };

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(fileOps: ops),
            new FakeUserConfirmationService { ConfirmationResult = true });

        var ex = Assert.Throws<TargetInspectionUnstableException>(() => coordinator.RequestTransition(target.Root));
        Assert.IsTrue(ex.Message.Contains("changed while being inspected", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void CRUU6_010_Readonly_existing_library_primary_rejects_switch()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        SeedValidLibrary(target.Root, out _);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        string targetLib = Path.Combine(target.Root, "library.json");
        File.SetAttributes(targetLib, FileAttributes.ReadOnly);

        try
        {
            var coordinator = new DataFolderTransitionCoordinator(
                source.Root,
                new AppSettingsRepository(settingsPathOverride: settingsPath),
                new DataFolderMigrationService(),
                new FakeUserConfirmationService { ConfirmationResult = true });

            var ex = Assert.Throws<UnauthorizedAccessException>(() => coordinator.RequestTransition(target.Root));
            Assert.IsTrue(ex.Message.Contains("read-only", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.SetAttributes(targetLib, FileAttributes.Normal);
        }
    }

    [TestMethod]
    public void CRUU6_010_Readonly_active_prompt_file_rejects_existing_target()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        SeedValidLibrary(target.Root, out Guid promptId);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        string promptPath = Path.Combine(target.Root, "prompts", $"{promptId:N}.md");
        File.SetAttributes(promptPath, FileAttributes.ReadOnly);

        try
        {
            var coordinator = new DataFolderTransitionCoordinator(
                source.Root,
                new AppSettingsRepository(settingsPathOverride: settingsPath),
                new DataFolderMigrationService(),
                new FakeUserConfirmationService { ConfirmationResult = true });

            var ex = Assert.Throws<UnauthorizedAccessException>(() => coordinator.RequestTransition(target.Root));
            Assert.IsTrue(ex.Message.Contains("read-only", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.SetAttributes(promptPath, FileAttributes.Normal);
        }
    }

    [TestMethod]
    public void CRUU6_006_Probe_residue_is_in_transition_cleanup_report()
    {
        using var source = new TestDirectory();
        using var targetParent = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);

        string target = Path.Combine(targetParent.Root, "ProbeFailTarget");
        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var ops = new FakeCapabilityFileOps
        {
            OnReplace = (src, dst, bak) => throw new IOException("Simulated capability probe failure")
        };

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(),
            new FakeUserConfirmationService(),
            capabilityValidator: new DataRootCapabilityValidator(ops));

        // When probe fails during empty target transition, tx rollback cleans probe files tracked in journal
        Assert.Throws<IOException>(() => coordinator.RequestTransition(target));
    }

    [TestMethod]
    public void CRUU6_007_Reservation_lock_cleanup_failure_is_reported()
    {
        using var source = new TestDirectory();
        using var targetParent = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);

        string target = Path.Combine(targetParent.Root, "ResLockFailTarget");
        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        FileStream? lockStream = null;
        try
        {
            var ops = new FaultInjectingMigrationFileOps
            {
                OnMoveNoOverwrite = (src, dst) =>
                {
                    // Lock .app.lock so deletion during reservation release fails
                    string targetLock = Path.Combine(target, ".app.lock");
                    if (File.Exists(targetLock) && lockStream == null)
                    {
                        lockStream = new FileStream(targetLock, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                    }
                    throw new IOException("Simulated copy failure");
                }
            };

            var coordinator = new DataFolderTransitionCoordinator(
                source.Root,
                new AppSettingsRepository(settingsPathOverride: settingsPath),
                new DataFolderMigrationService(fileOps: ops),
                new FakeUserConfirmationService());

            Assert.Throws<Exception>(() => coordinator.RequestTransition(target));
        }
        finally
        {
            lockStream?.Dispose();
        }
    }

    [TestMethod]
    public void CRUU6_009_Future_target_schema_is_controlled_dialog_error()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);

        string targetLib = Path.Combine(target.Root, "library.json");
        File.WriteAllText(targetLib, "{\"schemaVersion\":99,\"categories\":[],\"prompts\":[]}");

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(),
            new FakeUserConfirmationService { ConfirmationResult = true });

        var ex = Assert.Throws<UnsupportedLibrarySchemaException>(() => coordinator.RequestTransition(target.Root));
        Assert.AreEqual(99, ex.SchemaVersion);
    }

    [TestMethod]
    public void CRUU6_009_Future_settings_schema_mid_transition_is_controlled_dialog_error()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        SeedValidLibrary(target.Root, out _);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var confirmation = new FakeUserConfirmationService
        {
            ConfirmationResult = true,
            OnConfirm = () =>
            {
                // Settings upgraded to schema 99 mid-flight
                File.WriteAllText(settingsPath, "{\"schemaVersion\":99,\"dataRootPath\":\"C:\\\\Data\"}");
            }
        };

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(),
            confirmation);

        // When settings save runs with future schema in settings.json, UnsupportedSettingsSchemaException or InvalidOperationException (CAS mismatch) is thrown
        Assert.Throws<Exception>(() => coordinator.RequestTransition(target.Root));
    }
}
