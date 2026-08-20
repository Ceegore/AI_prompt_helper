using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class LibraryStartupServiceTests
{
    private static (LibraryStartupService service, AppPaths paths, LibraryRepository libRepo, PromptRepository promptRepo) CreateTestContext(string root)
    {
        var paths = new AppPaths(root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var service = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);

        return (service, paths, libRepo, promptRepo);
    }

    [TestMethod]
    public void Fresh_start_creates_defaults()
    {
        using var testDir = new TestDirectory();
        var (service, paths, _, promptRepo) = CreateTestContext(testDir.Root);

        var result = service.LoadOrInitialize();

        Assert.IsNotNull(result.Document);
        Assert.IsFalse(result.RecoveredFromBackup);
        Assert.IsNull(result.Warning);
        Assert.AreEqual(8, result.Document.Categories.Count);
        Assert.AreEqual(2, result.Document.Prompts.Count);
        Assert.IsTrue(File.Exists(paths.LibraryPath));
        Assert.IsTrue(File.Exists(paths.LibraryBackupPath));
        Assert.IsTrue(promptRepo.Exists(DefaultLibraryFactory.DefaultPrompt1Id));
        Assert.IsTrue(promptRepo.Exists(DefaultLibraryFactory.DefaultPrompt2Id));
        Assert.IsFalse(File.Exists(paths.InitializationMarkerPath));
    }

    [TestMethod]
    public void Second_start_does_not_duplicate_defaults()
    {
        using var testDir = new TestDirectory();
        var (service1, _, _, _) = CreateTestContext(testDir.Root);
        service1.LoadOrInitialize();

        var (service2, _, _, _) = CreateTestContext(testDir.Root);
        var result = service2.LoadOrInitialize();

        Assert.AreEqual(8, result.Document.Categories.Count);
        Assert.AreEqual(2, result.Document.Prompts.Count);
    }

    [TestMethod]
    public void Valid_primary_loads()
    {
        using var testDir = new TestDirectory();
        var (service, paths, libRepo, _) = CreateTestContext(testDir.Root);
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = Guid.NewGuid(), Name = "CustomCat", SortOrder = 10 }
            ]
        };
        libRepo.Commit(doc);

        var result = service.LoadOrInitialize();

        Assert.AreEqual(1, result.Document.Categories.Count);
        Assert.AreEqual("CustomCat", result.Document.Categories[0].Name);
        Assert.IsFalse(result.RecoveredFromBackup);
    }

    [TestMethod]
    public void Valid_primary_recreates_missing_backup()
    {
        using var testDir = new TestDirectory();
        var (service, paths, libRepo, _) = CreateTestContext(testDir.Root);
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = Guid.NewGuid(), Name = "CustomCat", SortOrder = 10 }
            ]
        };
        libRepo.Commit(doc);
        File.Delete(paths.LibraryBackupPath);

        var result = service.LoadOrInitialize();

        Assert.IsTrue(File.Exists(paths.LibraryBackupPath));
        Assert.AreEqual(1, result.Document.Categories.Count);
    }

    [TestMethod]
    public void Valid_primary_replaces_corrupt_backup()
    {
        using var testDir = new TestDirectory();
        var (service, paths, libRepo, _) = CreateTestContext(testDir.Root);
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = Guid.NewGuid(), Name = "CustomCat", SortOrder = 10 }
            ]
        };
        libRepo.Commit(doc);
        File.WriteAllText(paths.LibraryBackupPath, "corrupt json {}");

        var result = service.LoadOrInitialize();

        Assert.AreEqual(1, result.Document.Categories.Count);
        var backupDoc = libRepo.ReadBackup();
        Assert.AreEqual(1, backupDoc.Categories.Count);
    }

    [TestMethod]
    public void Corrupt_primary_valid_backup_recovers()
    {
        using var testDir = new TestDirectory();
        var (service, paths, libRepo, _) = CreateTestContext(testDir.Root);
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = Guid.NewGuid(), Name = "SavedInBackup", SortOrder = 10 }
            ]
        };
        libRepo.Commit(doc);
        File.WriteAllText(paths.LibraryPath, "{ not valid json");

        var result = service.LoadOrInitialize();

        Assert.IsTrue(result.RecoveredFromBackup);
        Assert.IsNotNull(result.Warning);
        Assert.AreEqual(1, result.Document.Categories.Count);
        Assert.AreEqual("SavedInBackup", result.Document.Categories[0].Name);

        // Check recovery copy was created
        var recoveryFiles = Directory.GetFiles(paths.RecoveryDirectory, "*.json");
        Assert.IsTrue(recoveryFiles.Length > 0);
    }

    [TestMethod]
    public void Missing_primary_valid_backup_recovers()
    {
        using var testDir = new TestDirectory();
        var (service, paths, libRepo, _) = CreateTestContext(testDir.Root);
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = Guid.NewGuid(), Name = "SavedInBackup", SortOrder = 10 }
            ]
        };
        libRepo.Commit(doc);
        File.Delete(paths.LibraryPath);

        var result = service.LoadOrInitialize();

        Assert.IsTrue(result.RecoveredFromBackup);
        Assert.IsNotNull(result.Warning);
        Assert.IsTrue(File.Exists(paths.LibraryPath));
        Assert.AreEqual(1, result.Document.Categories.Count);
    }

    [TestMethod]
    public void Corrupt_primary_corrupt_backup_fails()
    {
        using var testDir = new TestDirectory();
        var (service, paths, _, _) = CreateTestContext(testDir.Root);
        paths.EnsureDataDirectories();
        File.WriteAllText(paths.LibraryPath, "{ corrupt");
        File.WriteAllText(paths.LibraryBackupPath, "{ corrupt");

        Assert.Throws<InvalidDataException>(() => service.LoadOrInitialize());
    }

    [TestMethod]
    public void Corrupt_primary_missing_backup_fails()
    {
        using var testDir = new TestDirectory();
        var (service, paths, _, _) = CreateTestContext(testDir.Root);
        paths.EnsureDataDirectories();
        File.WriteAllText(paths.LibraryPath, "{ corrupt");

        Assert.Throws<InvalidDataException>(() => service.LoadOrInitialize());
    }

    [TestMethod]
    public void Missing_primary_corrupt_backup_fails()
    {
        using var testDir = new TestDirectory();
        var (service, paths, _, _) = CreateTestContext(testDir.Root);
        paths.EnsureDataDirectories();
        File.WriteAllText(paths.LibraryBackupPath, "{ corrupt");

        Assert.Throws<InvalidDataException>(() => service.LoadOrInitialize());
    }

    [TestMethod]
    public void Future_primary_never_falls_back_to_old_backup()
    {
        using var testDir = new TestDirectory();
        var (service, paths, libRepo, _) = CreateTestContext(testDir.Root);
        var doc = new LibraryDocument
        {
            Categories = [new CategoryRecord { Id = Guid.NewGuid(), Name = "OldCat", SortOrder = 10 }]
        };
        libRepo.Commit(doc);

        File.WriteAllText(paths.LibraryPath, """
        {
          "schemaVersion": 999,
          "customData": "some future format"
        }
        """);

        var ex = Assert.Throws<UnsupportedLibrarySchemaException>(() => service.LoadOrInitialize());
        Assert.AreEqual(999, ex.SchemaVersion);

        // Verify primary was NOT replaced
        Assert.IsTrue(File.ReadAllText(paths.LibraryPath).Contains("999"));
    }

    [TestMethod]
    public void Future_backup_when_primary_missing_fails()
    {
        using var testDir = new TestDirectory();
        var (service, paths, _, _) = CreateTestContext(testDir.Root);
        paths.EnsureDataDirectories();
        File.WriteAllText(paths.LibraryBackupPath, """
        {
          "schemaVersion": 999
        }
        """);

        Assert.Throws<UnsupportedLibrarySchemaException>(() => service.LoadOrInitialize());
    }

    [TestMethod]
    public void Interrupted_init_with_no_prompt_files_resumes()
    {
        using var testDir = new TestDirectory();
        var (service, paths, _, promptRepo) = CreateTestContext(testDir.Root);
        paths.EnsureDataDirectories();
        File.WriteAllText(paths.InitializationMarkerPath, "initializing");

        var result = service.LoadOrInitialize();

        Assert.AreEqual(8, result.Document.Categories.Count);
        Assert.IsTrue(promptRepo.Exists(DefaultLibraryFactory.DefaultPrompt1Id));
        Assert.IsTrue(promptRepo.Exists(DefaultLibraryFactory.DefaultPrompt2Id));
        Assert.IsFalse(File.Exists(paths.InitializationMarkerPath));
    }

    [TestMethod]
    public void Interrupted_init_with_partial_exact_defaults_resumes()
    {
        using var testDir = new TestDirectory();
        var (service, paths, _, promptRepo) = CreateTestContext(testDir.Root);
        paths.EnsureDataDirectories();
        File.WriteAllText(paths.InitializationMarkerPath, "initializing");
        promptRepo.Create(DefaultLibraryFactory.DefaultPrompt1Id, DefaultLibraryFactory.DefaultPrompt1Content);

        var result = service.LoadOrInitialize();

        Assert.AreEqual(8, result.Document.Categories.Count);
        Assert.IsTrue(promptRepo.Exists(DefaultLibraryFactory.DefaultPrompt1Id));
        Assert.IsTrue(promptRepo.Exists(DefaultLibraryFactory.DefaultPrompt2Id));
        Assert.IsFalse(File.Exists(paths.InitializationMarkerPath));
    }

    [TestMethod]
    public void Interrupted_init_with_modified_default_file_stops()
    {
        using var testDir = new TestDirectory();
        var (service, paths, _, promptRepo) = CreateTestContext(testDir.Root);
        paths.EnsureDataDirectories();
        File.WriteAllText(paths.InitializationMarkerPath, "initializing");
        promptRepo.Create(DefaultLibraryFactory.DefaultPrompt1Id, "Modified user content");

        Assert.Throws<InvalidOperationException>(() => service.LoadOrInitialize());
    }

    [TestMethod]
    public void Interrupted_init_with_unknown_file_stops()
    {
        using var testDir = new TestDirectory();
        var (service, paths, _, promptRepo) = CreateTestContext(testDir.Root);
        paths.EnsureDataDirectories();
        File.WriteAllText(paths.InitializationMarkerPath, "initializing");
        promptRepo.Create(Guid.NewGuid(), "Some random prompt");

        Assert.Throws<InvalidOperationException>(() => service.LoadOrInitialize());
    }

    [TestMethod]
    public void Unknown_prompt_files_without_marker_stop_initialization()
    {
        using var testDir = new TestDirectory();
        var (service, paths, _, promptRepo) = CreateTestContext(testDir.Root);
        paths.EnsureDataDirectories();
        promptRepo.Create(Guid.NewGuid(), "Existing prompt file");

        Assert.Throws<InvalidOperationException>(() => service.LoadOrInitialize());
    }

    [TestMethod]
    public void Valid_primary_ignores_and_removes_stale_marker_best_effort()
    {
        using var testDir = new TestDirectory();
        var (service, paths, libRepo, _) = CreateTestContext(testDir.Root);
        var doc = new LibraryDocument
        {
            Categories = [new CategoryRecord { Id = Guid.NewGuid(), Name = "Valid", SortOrder = 10 }]
        };
        libRepo.Commit(doc);
        File.WriteAllText(paths.InitializationMarkerPath, "stale marker");

        var result = service.LoadOrInitialize();

        Assert.AreEqual(1, result.Document.Categories.Count);
        Assert.IsFalse(File.Exists(paths.InitializationMarkerPath));
    }

    [TestMethod]
    public void CRUU4_002_Valid_primary_preserves_future_schema_library_backup()
    {
        using var testDir = new TestDirectory();
        var (service, paths, libRepo, _) = CreateTestContext(testDir.Root);
        paths.EnsureDataDirectories();

        var doc = new LibraryDocument
        {
            Categories = [new CategoryRecord { Id = Guid.NewGuid(), Name = "ValidPrimary", SortOrder = 10 }]
        };
        libRepo.Commit(doc);

        string futureBackupJson = "{\"schemaVersion\": 99, \"categories\": [], \"prompts\": []}";
        File.WriteAllText(paths.LibraryBackupPath, futureBackupJson);
        byte[] backupBefore = File.ReadAllBytes(paths.LibraryBackupPath);

        var result = service.LoadOrInitialize();

        Assert.IsFalse(result.RecoveredFromBackup);
        Assert.AreEqual(1, result.Document.Categories.Count);
        Assert.IsNotNull(result.Warning);
        StringAssert.Contains(result.Warning, "schema version 99");
        CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(paths.LibraryBackupPath));
    }

    [TestMethod]
    public void CRUU4_002_Corrupt_primary_future_backup_throws_unsupported_schema()
    {
        using var testDir = new TestDirectory();
        var (service, paths, _, _) = CreateTestContext(testDir.Root);
        paths.EnsureDataDirectories();

        File.WriteAllText(paths.LibraryPath, "corrupt json");
        File.WriteAllText(paths.LibraryBackupPath, "{\"schemaVersion\": 99, \"categories\": [], \"prompts\": []}");

        var ex = Assert.Throws<UnsupportedLibrarySchemaException>(() => service.LoadOrInitialize());
        Assert.AreEqual(99, ex.SchemaVersion);
    }

    [TestMethod]
    public void CRUU4_002_Valid_primary_locked_backup_still_starts()
    {
        using var testDir = new TestDirectory();
        var (service, paths, libRepo, _) = CreateTestContext(testDir.Root);
        paths.EnsureDataDirectories();

        var doc = new LibraryDocument
        {
            Categories = [new CategoryRecord { Id = Guid.NewGuid(), Name = "ValidPrimary", SortOrder = 10 }]
        };
        libRepo.Commit(doc);

        using var lockStream = new FileStream(paths.LibraryBackupPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = service.LoadOrInitialize();

        Assert.IsFalse(result.RecoveredFromBackup);
        Assert.AreEqual(1, result.Document.Categories.Count);
        Assert.IsNotNull(result.Warning);
        StringAssert.Contains(result.Warning, "could not");
    }
}