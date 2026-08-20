using System.IO;
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
    public void PLH007_Destination_paths_are_globally_unique_case_insensitively()
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
                // Category literally named "Home"
                new CategoryRecord { Id = c1Id, Name = "Home", SortOrder = 10 },
                // Category named "Home [11111111]" colliding with disambiguated label
                new CategoryRecord { Id = c2Id, Name = "Home [11111111]", SortOrder = 20 }
            ]
        };
        libRepo.Commit(doc);

        var service = new PromptLibraryService(doc, libRepo, promptRepo);
        var destinations = service.GetDestinations();

        var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dest in destinations)
        {
            bool added = uniquePaths.Add(dest.DisplayPath);
            Assert.IsTrue(added, $"Duplicate destination label found: {dest.DisplayPath}");
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
        Assert.AreEqual(10, moved.SortOrder); // First in empty category must be 10, NOT 1000010
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
    public void PLH014_CurrentDocument_is_defensive_clone()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        var doc = new LibraryDocument
        {
            Categories = [new CategoryRecord { Id = Guid.NewGuid(), Name = "Original", SortOrder = 10 }]
        };
        libRepo.Commit(doc);

        var service = new PromptLibraryService(doc, libRepo, promptRepo);

        // Mutating initial document after construction has no effect
        doc.Categories[0].Name = "ExternalMutated";
        Assert.AreEqual("Original", service.CurrentDocument.Categories[0].Name);

        // Mutating snapshot returned by CurrentDocument has no effect
        var snapshot = service.CurrentDocument;
        snapshot.Categories[0].Name = "SnapshotMutated";
        Assert.AreEqual("Original", service.CurrentDocument.Categories[0].Name);
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