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
    public void PLH007_PLH2006_Destination_paths_are_globally_unique_and_sorted_by_final_path()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        var c1Id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var c2Id = Guid.Parse("11111111-9999-8888-7777-666666666666");

        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = c1Id, Name = "Home", SortOrder = 10 },
                new CategoryRecord { Id = c2Id, Name = "Home [11111111]", SortOrder = 20 }
            ]
        };
        libRepo.Commit(doc);

        var service = new PromptLibraryService(doc, libRepo, promptRepo);
        var destinations = service.GetDestinations();

        // 1. Home is first
        Assert.AreEqual("Home", destinations[0].DisplayPath);
        Assert.IsNull(destinations[0].CategoryId);

        // 2. Global uniqueness
        var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dest in destinations)
        {
            bool added = uniquePaths.Add(dest.DisplayPath);
            Assert.IsTrue(added, $"Duplicate destination label found: {dest.DisplayPath}");
        }

        // 3. Sorted by final display path
        for (int i = 1; i < destinations.Count - 1; i++)
        {
            Assert.IsTrue(
                string.Compare(destinations[i].DisplayPath, destinations[i + 1].DisplayPath, StringComparison.OrdinalIgnoreCase) <= 0,
                $"Destinations not sorted by final display path: {destinations[i].DisplayPath} vs {destinations[i + 1].DisplayPath}");
        }
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
    public void PLH012_CanDeleteCategory_identifies_empty_vs_non_empty()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        var catWithPrompt = new CategoryRecord { Id = Guid.NewGuid(), Name = "HasPrompt", SortOrder = 10 };
        var emptyCat = new CategoryRecord { Id = Guid.NewGuid(), Name = "Empty", SortOrder = 20 };
        var pId = Guid.NewGuid();
        promptRepo.Create(pId, "p");

        var doc = new LibraryDocument
        {
            Categories = [catWithPrompt, emptyCat],
            Prompts = [new PromptRecord { Id = pId, CategoryId = catWithPrompt.Id, SortOrder = 10 }]
        };
        libRepo.Commit(doc);

        var service = new PromptLibraryService(doc, libRepo, promptRepo);

        Assert.IsFalse(service.CanDeleteCategory(catWithPrompt.Id, out string? reason1));
        Assert.IsNotNull(reason1);

        Assert.IsTrue(service.CanDeleteCategory(emptyCat.Id, out string? reason2));
        Assert.IsNull(reason2);
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
            Categories = [cB, cA], // Intentionally unordered in list
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