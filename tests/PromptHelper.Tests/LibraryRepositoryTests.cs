using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class LibraryRepositoryTests
{
    [TestMethod]
    public void Missing_schemaVersion_fails()
    {
        string json = """
        {
          "categories": [],
          "prompts": []
        }
        """;

        Assert.Throws<InvalidDataException>(() =>
            LibraryRepository.InspectAndDeserialize(json));
    }

    [TestMethod]
    public void Duplicate_schemaVersion_fails()
    {
        string json = """
        {
          "schemaVersion": 1,
          "SchemaVersion": 1,
          "categories": [],
          "prompts": []
        }
        """;

        Assert.Throws<InvalidDataException>(() =>
            LibraryRepository.InspectAndDeserialize(json));
    }

    [TestMethod]
    public void Missing_categories_fails()
    {
        string json = """
        {
          "schemaVersion": 1,
          "prompts": []
        }
        """;

        Assert.Throws<JsonException>(() =>
            LibraryRepository.InspectAndDeserialize(json));
    }

    [TestMethod]
    public void Missing_prompts_fails()
    {
        string json = """
        {
          "schemaVersion": 1,
          "categories": []
        }
        """;

        Assert.Throws<JsonException>(() =>
            LibraryRepository.InspectAndDeserialize(json));
    }

    [TestMethod]
    public void Category_missing_id_fails()
    {
        string json = """
        {
          "schemaVersion": 1,
          "categories": [
            {
              "parentId": null,
              "name": "General",
              "sortOrder": 10
            }
          ],
          "prompts": []
        }
        """;

        Assert.Throws<JsonException>(() =>
            LibraryRepository.InspectAndDeserialize(json));
    }

    [TestMethod]
    public void Category_empty_guid_fails()
    {
        string json = """
        {
          "schemaVersion": 1,
          "categories": [
            {
              "id": "00000000-0000-0000-0000-000000000000",
              "parentId": null,
              "name": "General",
              "sortOrder": 10
            }
          ],
          "prompts": []
        }
        """;

        Assert.Throws<InvalidDataException>(() =>
            LibraryRepository.InspectAndDeserialize(json));
    }

    [TestMethod]
    public void Category_missing_parentId_fails()
    {
        string json = """
        {
          "schemaVersion": 1,
          "categories": [
            {
              "id": "10000000-0000-0000-0000-000000000001",
              "name": "General",
              "sortOrder": 10
            }
          ],
          "prompts": []
        }
        """;

        Assert.Throws<JsonException>(() =>
            LibraryRepository.InspectAndDeserialize(json));
    }

    [TestMethod]
    public void Category_missing_name_fails()
    {
        string json = """
        {
          "schemaVersion": 1,
          "categories": [
            {
              "id": "10000000-0000-0000-0000-000000000001",
              "parentId": null,
              "sortOrder": 10
            }
          ],
          "prompts": []
        }
        """;

        Assert.Throws<JsonException>(() =>
            LibraryRepository.InspectAndDeserialize(json));
    }

    [TestMethod]
    public void Category_missing_sortOrder_fails()
    {
        string json = """
        {
          "schemaVersion": 1,
          "categories": [
            {
              "id": "10000000-0000-0000-0000-000000000001",
              "parentId": null,
              "name": "General"
            }
          ],
          "prompts": []
        }
        """;

        Assert.Throws<JsonException>(() =>
            LibraryRepository.InspectAndDeserialize(json));
    }

    [TestMethod]
    public void Prompt_missing_id_fails()
    {
        string json = """
        {
          "schemaVersion": 1,
          "categories": [],
          "prompts": [
            {
              "categoryId": null,
              "sortOrder": 10
            }
          ]
        }
        """;

        Assert.Throws<JsonException>(() =>
            LibraryRepository.InspectAndDeserialize(json));
    }

    [TestMethod]
    public void Prompt_empty_guid_fails()
    {
        string json = """
        {
          "schemaVersion": 1,
          "categories": [],
          "prompts": [
            {
              "id": "00000000-0000-0000-0000-000000000000",
              "categoryId": null,
              "sortOrder": 10
            }
          ]
        }
        """;

        Assert.Throws<InvalidDataException>(() =>
            LibraryRepository.InspectAndDeserialize(json));
    }

    [TestMethod]
    public void Prompt_missing_categoryId_fails()
    {
        string json = """
        {
          "schemaVersion": 1,
          "categories": [],
          "prompts": [
            {
              "id": "20000000-0000-0000-0000-000000000001",
              "sortOrder": 10
            }
          ]
        }
        """;

        Assert.Throws<JsonException>(() =>
            LibraryRepository.InspectAndDeserialize(json));
    }

    [TestMethod]
    public void Prompt_missing_sortOrder_fails()
    {
        string json = """
        {
          "schemaVersion": 1,
          "categories": [],
          "prompts": [
            {
              "id": "20000000-0000-0000-0000-000000000001",
              "categoryId": null
            }
          ]
        }
        """;

        Assert.Throws<JsonException>(() =>
            LibraryRepository.InspectAndDeserialize(json));
    }

    [TestMethod]
    public void Explicit_null_required_nonnullable_property_fails()
    {
        string json = """
        {
          "schemaVersion": 1,
          "categories": [
            {
              "id": "10000000-0000-0000-0000-000000000001",
              "parentId": null,
              "name": null,
              "sortOrder": 10
            }
          ],
          "prompts": []
        }
        """;

        Assert.Throws<JsonException>(() =>
            LibraryRepository.InspectAndDeserialize(json));
    }

    [TestMethod]
    public void Future_schema_detected_before_v1_required_property_validation()
    {
        string json = """
        {
          "schemaVersion": 999
        }
        """;

        var ex = Assert.Throws<UnsupportedLibrarySchemaException>(() =>
            LibraryRepository.InspectAndDeserialize(json));

        Assert.AreEqual(999, ex.SchemaVersion);
    }

    [TestMethod]
    public void Commit_success_commits_primary_and_backup()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var repo = new LibraryRepository(paths, writer);

        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord
                {
                    Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                    ParentId = null,
                    Name = "General",
                    SortOrder = 10
                }
            ]
        };

        var result = repo.Commit(doc);

        Assert.IsTrue(result.BackupSynchronized);
        Assert.IsNull(result.Warning);
        Assert.IsTrue(File.Exists(paths.LibraryPath));
        Assert.IsTrue(File.Exists(paths.LibraryBackupPath));

        var loadedPrimary = repo.ReadPrimary();
        Assert.AreEqual(1, loadedPrimary.Categories.Count);
        Assert.AreEqual("General", loadedPrimary.Categories[0].Name);

        var loadedBackup = repo.ReadBackup();
        Assert.AreEqual(1, loadedBackup.Categories.Count);
    }

    [TestMethod]
    public void Commit_backup_failure_returns_warning_and_preserves_primary()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var innerWriter = new AtomicTextWriter();
        var faultWriter = new FaultInjectingAtomicTextWriter(innerWriter)
        {
            ShouldFail = (path, callNum) => path.EndsWith("library.backup.json", StringComparison.OrdinalIgnoreCase)
        };
        var repo = new LibraryRepository(paths, faultWriter);

        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord
                {
                    Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                    ParentId = null,
                    Name = "General",
                    SortOrder = 10
                }
            ]
        };

        var result = repo.Commit(doc);

        Assert.IsFalse(result.BackupSynchronized);
        Assert.IsNotNull(result.Warning);
        Assert.IsTrue(File.Exists(paths.LibraryPath));
        Assert.IsFalse(File.Exists(paths.LibraryBackupPath));
    }

    [TestMethod]
    public void CRUU5_003_Commit_preserves_future_schema_backup_exactly()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        paths.EnsureDataDirectories();

        string backupContent = "{\"schemaVersion\": 2, \"categories\": [], \"prompts\": []}";
        File.WriteAllText(paths.LibraryBackupPath, backupContent);
        byte[] backupBefore = File.ReadAllBytes(paths.LibraryBackupPath);

        var repo = new LibraryRepository(paths, new AtomicTextWriter());
        var doc = new LibraryDocument
        {
            Categories = [new CategoryRecord { Id = Guid.NewGuid(), Name = "General", SortOrder = 10 }]
        };

        var result = repo.Commit(doc);

        Assert.IsFalse(result.BackupSynchronized);
        Assert.IsNotNull(result.Warning);
        StringAssert.Contains(result.Warning, "newer schema version 2");
        CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(paths.LibraryBackupPath));
    }

    [TestMethod]
    public void CRUU5_003_Commit_refuses_future_schema_primary()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        paths.EnsureDataDirectories();

        File.WriteAllText(paths.LibraryPath, "{\"schemaVersion\": 3, \"categories\": [], \"prompts\": []}");
        File.WriteAllText(paths.LibraryBackupPath, "{\"schemaVersion\": 1, \"categories\": [], \"prompts\": []}");

        byte[] primaryBefore = File.ReadAllBytes(paths.LibraryPath);
        byte[] backupBefore = File.ReadAllBytes(paths.LibraryBackupPath);

        var repo = new LibraryRepository(paths, new AtomicTextWriter());
        var doc = new LibraryDocument();

        Assert.Throws<UnsupportedLibrarySchemaException>(() => repo.Commit(doc));

        CollectionAssert.AreEqual(primaryBefore, File.ReadAllBytes(paths.LibraryPath));
        CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(paths.LibraryBackupPath));
    }

    [TestMethod]
    public void CRUU5_003_SynchronizeBackup_preserves_future_backup()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        paths.EnsureDataDirectories();

        File.WriteAllText(paths.LibraryBackupPath, "{\"schemaVersion\": 2, \"categories\": [], \"prompts\": []}");
        byte[] backupBefore = File.ReadAllBytes(paths.LibraryBackupPath);

        var repo = new LibraryRepository(paths, new AtomicTextWriter());
        var doc = new LibraryDocument();

        var result = repo.SynchronizeBackup(repo.CreateCanonicalPackage(doc));

        Assert.IsFalse(result.BackupSynchronized);
        Assert.IsNotNull(result.Warning);
        CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(paths.LibraryBackupPath));
    }

    [TestMethod]
    public void CRUU5_003_CreateCategory_preserves_future_backup()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        paths.EnsureDataDirectories();

        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var repo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        // Pre-create valid primary and future backup
        repo.Commit(new LibraryDocument());
        File.WriteAllText(paths.LibraryBackupPath, "{\"schemaVersion\": 2, \"categories\": [], \"prompts\": []}");
        byte[] backupBefore = File.ReadAllBytes(paths.LibraryBackupPath);

        var doc = repo.ReadPrimary();
        var service = new PromptLibraryService(doc, repo, promptRepo);

        var result = service.CreateCategory(null, "NewCat");

        Assert.IsNotNull(result.Value);
        Assert.AreEqual("NewCat", result.Value.Name);
        Assert.IsNotNull(result.Warning);
        StringAssert.Contains(result.Warning, "newer schema version 2");
        CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(paths.LibraryBackupPath));
    }

    [TestMethod]
    public void CRUU5_003_DeletePrompt_preserves_body_if_backup_not_synchronized()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        paths.EnsureDataDirectories();

        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var repo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        // Pre-create valid primary with a prompt
        var startupService = new LibraryStartupService(paths, repo, promptRepo, deleter, writer);
        var initResult = startupService.LoadOrInitialize();

        var service = new PromptLibraryService(initResult.Document, repo, promptRepo);
        var prompt = service.CreatePrompt(null, "Body text", "Title").Value;

        // Make backup future schema
        File.WriteAllText(paths.LibraryBackupPath, "{\"schemaVersion\": 2, \"categories\": [], \"prompts\": []}");

        // Delete prompt
        var deleteResult = service.DeletePrompt(prompt.Id);

        Assert.IsNotNull(deleteResult.Warning);

        // Body must be preserved because backup was not synchronized
        Assert.IsTrue(File.Exists(paths.GetPromptPath(prompt.Id)));
    }
}