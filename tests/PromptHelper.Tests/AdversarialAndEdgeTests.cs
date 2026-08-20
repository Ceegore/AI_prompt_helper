using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class AdversarialAndEdgeTests
{
    [TestMethod]
    public void Category_sort_order_overflow_resequences_correctly()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = Guid.NewGuid(), Name = "Cat1", SortOrder = long.MaxValue - 5 },
                new CategoryRecord { Id = Guid.NewGuid(), Name = "Cat2", SortOrder = long.MaxValue - 3 }
            ]
        };
        libRepo.Commit(doc);

        var service = new PromptLibraryService(doc, libRepo, promptRepo);
        var newCat = service.CreateCategory(null, "Cat3").Value;

        Assert.AreEqual(30, newCat.SortOrder);
        Assert.AreEqual(10, service.CurrentDocument.Categories.First(c => c.Name == "Cat1").SortOrder);
        Assert.AreEqual(20, service.CurrentDocument.Categories.First(c => c.Name == "Cat2").SortOrder);
    }

    [TestMethod]
    public void Prompt_sort_order_overflow_resequences_correctly()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        var p1Id = Guid.NewGuid();
        var p2Id = Guid.NewGuid();
        promptRepo.Create(p1Id, "c1");
        promptRepo.Create(p2Id, "c2");

        var doc = new LibraryDocument
        {
            Prompts =
            [
                new PromptRecord { Id = p1Id, CategoryId = null, SortOrder = long.MaxValue - 5 },
                new PromptRecord { Id = p2Id, CategoryId = null, SortOrder = long.MaxValue - 3 }
            ]
        };
        libRepo.Commit(doc);

        var service = new PromptLibraryService(doc, libRepo, promptRepo);
        var newPrompt = service.CreatePrompt(null, "c3").Value;

        Assert.AreEqual(30, newPrompt.SortOrder);
        Assert.AreEqual(10, service.CurrentDocument.Prompts.First(p => p.Id == p1Id).SortOrder);
        Assert.AreEqual(20, service.CurrentDocument.Prompts.First(p => p.Id == p2Id).SortOrder);
    }

    [TestMethod]
    public void Corrupt_primary_and_corrupt_backup_fails_safely_preserving_files()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        paths.EnsureDataDirectories();

        File.WriteAllText(paths.LibraryPath, "{ corrupt primary");
        File.WriteAllText(paths.LibraryBackupPath, "{ corrupt backup");

        var pId = Guid.NewGuid();
        File.WriteAllText(paths.GetPromptPath(pId), "User prompt content");

        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startupService = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);

        Assert.Throws<InvalidDataException>(() => startupService.LoadOrInitialize());

        // Assert prompt content preserved
        Assert.IsTrue(File.Exists(paths.GetPromptPath(pId)));
        Assert.AreEqual("User prompt content", File.ReadAllText(paths.GetPromptPath(pId)));
    }
}