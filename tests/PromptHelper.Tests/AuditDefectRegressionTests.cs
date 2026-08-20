using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;
using PromptHelper.ViewModels;

namespace PromptHelper.Tests;

[TestClass]
public sealed class AuditDefectRegressionTests
{
    [TestMethod]
    public void PLH004_Zero_byte_primary_recovers_from_valid_backup()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        paths.EnsureDataDirectories();

        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        // Valid backup
        var validDoc = new LibraryDocument
        {
            Categories = [new CategoryRecord { Id = Guid.NewGuid(), Name = "BackupCat", SortOrder = 10 }]
        };
        libRepo.SynchronizeBackup(validDoc);

        // Empty 0-byte primary
        File.WriteAllText(paths.LibraryPath, "");

        var startupService = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);
        var result = startupService.LoadOrInitialize();

        Assert.IsTrue(result.RecoveredFromBackup);
        Assert.AreEqual("BackupCat", result.Document.Categories[0].Name);
        Assert.IsNotNull(result.Warning);
    }

    [TestMethod]
    public void PLH004_Whitespace_primary_recovers_from_valid_backup()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        paths.EnsureDataDirectories();

        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        var validDoc = new LibraryDocument
        {
            Categories = [new CategoryRecord { Id = Guid.NewGuid(), Name = "BackupCat2", SortOrder = 10 }]
        };
        libRepo.SynchronizeBackup(validDoc);

        // Whitespace primary
        File.WriteAllText(paths.LibraryPath, "   \r\n\t  ");

        var startupService = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);
        var result = startupService.LoadOrInitialize();

        Assert.IsTrue(result.RecoveredFromBackup);
        Assert.AreEqual("BackupCat2", result.Document.Categories[0].Name);
    }

    [TestMethod]
    public void PLH002_Valid_primary_with_backup_sync_failure_returns_warning()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        paths.EnsureDataDirectories();

        var baseWriter = new AtomicTextWriter();
        var baseDeleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, baseWriter);
        var promptRepo = new PromptRepository(paths, baseWriter, baseDeleter);

        var doc = new LibraryDocument();
        libRepo.Commit(doc);

        var faultWriter = new FaultInjectingAtomicTextWriter(baseWriter)
        {
            ShouldFail = (path, _) => path.EndsWith("library.backup.json")
        };

        var faultLibRepo = new LibraryRepository(paths, faultWriter);
        var startupService = new LibraryStartupService(paths, faultLibRepo, promptRepo, baseDeleter, faultWriter);

        var result = startupService.LoadOrInitialize();

        Assert.IsNotNull(result.Warning);
        Assert.IsTrue(result.Warning.Contains("safety backup could not be synchronized"));
    }

    [TestMethod]
    public void PLH4002_Valid_primary_with_locked_unreadable_backup_loads_and_warns()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        paths.EnsureDataDirectories();

        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        var primaryDoc = new LibraryDocument
        {
            Categories = [new CategoryRecord { Id = Guid.NewGuid(), Name = "PrimaryCat", SortOrder = 10 }]
        };
        libRepo.Commit(primaryDoc);

        var startupService = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);

        // Lock backup file preventing reading or writing
        using (var lockStream = new FileStream(paths.LibraryBackupPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = startupService.LoadOrInitialize();

            Assert.IsFalse(result.RecoveredFromBackup);
            Assert.AreEqual("PrimaryCat", result.Document.Categories[0].Name);
            Assert.IsNotNull(result.Warning);
            Assert.IsTrue(result.Warning.Contains("safety backup could not be synchronized"));
        }
    }

    [TestMethod]
    public void PLH002_First_run_backup_write_failure_returns_warning()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        paths.EnsureDataDirectories();

        var baseWriter = new AtomicTextWriter();
        var baseDeleter = new FileDeleter();
        var promptRepo = new PromptRepository(paths, baseWriter, baseDeleter);

        var faultWriter = new FaultInjectingAtomicTextWriter(baseWriter)
        {
            ShouldFail = (path, _) => path.EndsWith("library.backup.json")
        };

        var libRepo = new LibraryRepository(paths, faultWriter);
        var startupService = new LibraryStartupService(paths, libRepo, promptRepo, baseDeleter, faultWriter);

        var result = startupService.LoadOrInitialize();

        Assert.IsNotNull(result.Warning);
        Assert.IsTrue(result.Warning.Contains("safety backup could not be updated"));
    }

    [TestMethod]
    public void PLH3001_Non_IOException_during_backup_is_warning_only_and_commits_primary()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var baseWriter = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var promptRepo = new PromptRepository(paths, baseWriter, deleter);

        var faultWriter = new FaultInjectingAtomicTextWriter(baseWriter)
        {
            FailureFactory = (path, _) => path.EndsWith("library.backup.json")
                ? new InvalidOperationException("Injected non-IOException")
                : null
        };

        var libRepo = new LibraryRepository(paths, faultWriter);
        var doc = new LibraryDocument();

        var result = libRepo.Commit(doc);

        Assert.IsFalse(result.BackupSynchronized);
        Assert.IsNotNull(result.Warning);
        Assert.IsTrue(File.Exists(paths.LibraryPath));
    }

    [TestMethod]
    public void PLH3001_GUID_generation_retries_on_metadata_or_orphan_collision()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        var existingMetadataGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var orphanFileGuid = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var freeGuid = Guid.Parse("33333333-3333-3333-3333-333333333333");

        // Existing prompt in metadata
        promptRepo.Create(existingMetadataGuid, "meta");
        var doc = new LibraryDocument
        {
            Prompts = [new PromptRecord { Id = existingMetadataGuid, CategoryId = null, SortOrder = 10 }]
        };
        libRepo.Commit(doc);

        // Orphan file on disk
        File.WriteAllText(paths.GetPromptPath(orphanFileGuid), "orphan");

        // Sequence of generated GUIDs: 1. metadata collision, 2. orphan collision, 3. free
        var sequence = new Queue<Guid>([existingMetadataGuid, orphanFileGuid, freeGuid]);
        var service = new PromptLibraryService(doc, libRepo, promptRepo, () => sequence.Dequeue());

        var result = service.CreatePrompt(null, "new prompt");

        Assert.AreEqual(freeGuid, result.Value.Id);
        Assert.AreEqual("new prompt", promptRepo.Read(freeGuid));
        Assert.AreEqual("orphan", File.ReadAllText(paths.GetPromptPath(orphanFileGuid)));
    }

    [TestMethod]
    public void PLH3001_GUID_generation_fails_after_ten_collisions()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        var collidingGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        promptRepo.Create(collidingGuid, "existing");
        var doc = new LibraryDocument
        {
            Prompts = [new PromptRecord { Id = collidingGuid, CategoryId = null, SortOrder = 10 }]
        };
        libRepo.Commit(doc);

        var service = new PromptLibraryService(doc, libRepo, promptRepo, () => collidingGuid);

        Assert.Throws<InvalidOperationException>(() => service.CreatePrompt(null, "new prompt"));
    }

    [TestMethod]
    public void PLH4001_Destination_paths_unique_even_with_32_char_guid_exhaustion()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        var cId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        string hex = cId.ToString("N");

        var categories = new List<CategoryRecord>
        {
            // Blocker categories placed FIRST to occupy all prefix lengths 8 through 32
            new() { Id = Guid.NewGuid(), Name = $"Home [{hex[..8]}]", SortOrder = 1 },
            new() { Id = Guid.NewGuid(), Name = $"Home [{hex[..12]}]", SortOrder = 2 },
            new() { Id = Guid.NewGuid(), Name = $"Home [{hex[..16]}]", SortOrder = 3 },
            new() { Id = Guid.NewGuid(), Name = $"Home [{hex[..20]}]", SortOrder = 4 },
            new() { Id = Guid.NewGuid(), Name = $"Home [{hex[..24]}]", SortOrder = 5 },
            new() { Id = Guid.NewGuid(), Name = $"Home [{hex[..28]}]", SortOrder = 6 },
            new() { Id = Guid.NewGuid(), Name = $"Home [{hex[..32]}]", SortOrder = 7 },
            // Category named Home with ID cId processed last, forcing suffix extension and #2 fallback
            new() { Id = cId, Name = "Home", SortOrder = 8 }
        };

        var doc = new LibraryDocument { Categories = categories };
        libRepo.Commit(doc);

        var service = new PromptLibraryService(doc, libRepo, promptRepo);
        var destinations = service.GetDestinations();

        // 1. Root Home is first
        Assert.AreEqual("Home", destinations[0].DisplayPath);
        Assert.IsNull(destinations[0].CategoryId);

        // 2. Global uniqueness
        var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dest in destinations)
        {
            bool added = uniquePaths.Add(dest.DisplayPath);
            Assert.IsTrue(added, $"Duplicate destination path found: {dest.DisplayPath}");
        }

        // 3. Category cId receives exact #2 fallback
        var targetDest = destinations.FirstOrDefault(d => d.CategoryId == cId);
        Assert.IsNotNull(targetDest);
        Assert.AreEqual($"Home [{hex}] #2", targetDest.DisplayPath);

        // 4. Sorted by final display path
        for (int i = 1; i < destinations.Count - 1; i++)
        {
            Assert.IsTrue(
                string.Compare(destinations[i].DisplayPath, destinations[i + 1].DisplayPath, StringComparison.OrdinalIgnoreCase) <= 0,
                $"Destinations not sorted by final display path: {destinations[i].DisplayPath} vs {destinations[i + 1].DisplayPath}");
        }
    }

    [TestMethod]
    public void PLH3004_CanDeleteCategory_returns_exact_locked_text()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        var parentCat = new CategoryRecord { Id = Guid.NewGuid(), Name = "Parent", SortOrder = 10 };
        var childCat = new CategoryRecord { Id = Guid.NewGuid(), ParentId = parentCat.Id, Name = "Child", SortOrder = 20 };
        var catWithPrompt = new CategoryRecord { Id = Guid.NewGuid(), Name = "PromptOnly", SortOrder = 30 };
        var pId = Guid.NewGuid();
        promptRepo.Create(pId, "p");

        var doc = new LibraryDocument
        {
            Categories = [parentCat, childCat, catWithPrompt],
            Prompts = [new PromptRecord { Id = pId, CategoryId = catWithPrompt.Id, SortOrder = 10 }]
        };
        libRepo.Commit(doc);

        var service = new PromptLibraryService(doc, libRepo, promptRepo);

        const string expectedMessage = "This category is not empty.\r\n\r\nMove or delete its prompts and subcategories first.";

        // Subcategories only
        Assert.IsFalse(service.CanDeleteCategory(parentCat.Id, out string? reason1));
        Assert.AreEqual(expectedMessage, reason1);

        // Prompts only
        Assert.IsFalse(service.CanDeleteCategory(catWithPrompt.Id, out string? reason2));
        Assert.AreEqual(expectedMessage, reason2);
    }

    [TestMethod]
    public void PLH011_Move_prompt_destination_sort_order_not_inflated_by_source()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        var targetCat = new CategoryRecord { Id = Guid.NewGuid(), Name = "EmptyDest", SortOrder = 10 };
        var pId = Guid.NewGuid();
        promptRepo.Create(pId, "prompt content");

        var doc = new LibraryDocument
        {
            Categories = [targetCat],
            Prompts =
            [
                new PromptRecord { Id = pId, CategoryId = null, SortOrder = 1000000 }
            ]
        };
        libRepo.Commit(doc);

        var service = new PromptLibraryService(doc, libRepo, promptRepo);
        service.MovePrompt(pId, targetCat.Id);

        var moved = service.CurrentDocument.Prompts.First(p => p.Id == pId);
        Assert.AreEqual(targetCat.Id, moved.CategoryId);
        Assert.AreEqual(10, moved.SortOrder);
    }

    [TestMethod]
    public void PLH013_EditPrompt_missing_file_throws_FileNotFoundException_with_path()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        var pId = Guid.NewGuid();
        var doc = new LibraryDocument
        {
            Prompts = [new PromptRecord { Id = pId, CategoryId = null, SortOrder = 10 }]
        };
        libRepo.Commit(doc);

        var service = new PromptLibraryService(doc, libRepo, promptRepo);

        var ex = Assert.Throws<FileNotFoundException>(() => service.EditPrompt(pId, "new content"));
        Assert.IsTrue(ex.FileName?.EndsWith($"{pId:N}.md") == true);
    }

    [TestMethod]
    public void PLH2001_Public_APIs_return_safe_clones_not_live_references()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        var catId = Guid.NewGuid();
        var doc = new LibraryDocument
        {
            Categories = [new CategoryRecord { Id = catId, Name = "Original", SortOrder = 10 }]
        };
        libRepo.Commit(doc);

        var service = new PromptLibraryService(doc, libRepo, promptRepo);

        // 1. GetCategories leak check
        var retrievedCategories = service.GetCategories(null);
        retrievedCategories[0].Name = "MutatedThroughGetCategories";
        Assert.AreEqual("Original", service.CurrentDocument.Categories[0].Name);

        // 2. CreateCategory result leak check
        var createdCategoryResult = service.CreateCategory(null, "CreatedCat");
        createdCategoryResult.Value.Name = "MutatedThroughCreateResult";
        Assert.AreEqual("CreatedCat", service.CurrentDocument.Categories.First(c => c.Id == createdCategoryResult.Value.Id).Name);

        // 3. CreatePrompt result leak check
        var createdPromptResult = service.CreatePrompt(null, "Content");
        createdPromptResult.Value.SortOrder = 999999;
        Assert.AreNotEqual(999999, service.CurrentDocument.Prompts.First(p => p.Id == createdPromptResult.Value.Id).SortOrder);
    }

    [TestMethod]
    public void PLH2003_Deterministic_category_and_prompt_tie_break_ordering()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        var cB = new CategoryRecord { Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Name = "B_Cat", SortOrder = 10 };
        var cA = new CategoryRecord { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Name = "A_Cat", SortOrder = 10 };

        var p2 = new PromptRecord { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), CategoryId = null, SortOrder = 10 };
        var p1 = new PromptRecord { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), CategoryId = null, SortOrder = 10 };

        promptRepo.Create(p1.Id, "c1");
        promptRepo.Create(p2.Id, "c2");

        var doc = new LibraryDocument
        {
            Categories = [cB, cA],
            Prompts = [p2, p1]
        };
        libRepo.Commit(doc);

        var service = new PromptLibraryService(doc, libRepo, promptRepo);

        var categories = service.GetCategories(null);
        Assert.AreEqual("A_Cat", categories[0].Name);
        Assert.AreEqual("B_Cat", categories[1].Name);

        var prompts = service.GetPrompts(null);
        Assert.AreEqual(p1.Id, prompts[0].Id);
        Assert.AreEqual(p2.Id, prompts[1].Id);
    }

    [TestMethod]
    public void PLH2004_Assembly_version_is_0_1_0()
    {
        var asm = typeof(PromptHelper.App).Assembly;
        var version = asm.GetName().Version;
        Assert.IsNotNull(version);
        Assert.AreEqual(0, version.Major);
        Assert.AreEqual(1, version.Minor);
        Assert.AreEqual(0, version.Build);
    }

    [TestMethod]
    public void PLH008_80_unicode_text_elements_accepted()
    {
        string eightyEmojis = string.Concat(Enumerable.Repeat("🚀", 80));
        Assert.AreEqual(80, PromptHelper.Infrastructure.TextUtilities.GetTextElementCount(eightyEmojis));
        Assert.IsNull(LibraryValidator.ValidateCategoryNameInput(eightyEmojis, []));

        string eightyOneEmojis = string.Concat(Enumerable.Repeat("🚀", 81));
        Assert.AreEqual(81, PromptHelper.Infrastructure.TextUtilities.GetTextElementCount(eightyOneEmojis));
        Assert.IsNotNull(LibraryValidator.ValidateCategoryNameInput(eightyOneEmojis, []));
    }
}