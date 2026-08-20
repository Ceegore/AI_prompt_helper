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
            settingsRepo,
            migrationService,
            confirmation);

        // Hold lock on target
        string lockPath = Path.Combine(target.Root, ".app.lock");
        using var lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        Assert.Throws<InvalidOperationException>(() => coordinator.RequestTransition(target.Root));
    }
}
