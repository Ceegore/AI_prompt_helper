using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class PublishedLifecycleAndGuiFlowRegressionTests
{
    [TestMethod]
    public void Category_and_Prompt_Full_CRUD_Lifecycle_and_Restart_Persistence()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startup = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);

        // 1. Fresh Start creates defaults
        var initResult = startup.LoadOrInitialize();
        Assert.IsTrue(File.Exists(paths.LibraryPath));
        Assert.IsTrue(File.Exists(paths.LibraryBackupPath));
        Assert.IsTrue(initResult.Document.Categories.Count > 0);
        Assert.IsTrue(initResult.Document.Prompts.Count > 0);

        // 2. Service CRUD Operations
        var service = new PromptLibraryService(initResult.Document, libRepo, promptRepo);

        // Create Category & Nested Category
        var catA = service.CreateCategory(null, "E2E_Cat_A");
        Assert.AreEqual("E2E_Cat_A", catA.Value.Name);
        var catNested = service.CreateCategory(catA.Value.Id, "E2E_Cat_Nested");
        Assert.AreEqual(catA.Value.Id, catNested.Value.ParentId);

        // Duplicate sibling rejection
        Assert.Throws<InvalidOperationException>(() => service.CreateCategory(catA.Value.Id, "e2e_cat_nested"));

        // Rename Category
        service.RenameCategory(catNested.Value.Id, "E2E_Cat_Nested_Renamed");
        Assert.AreEqual("E2E_Cat_Nested_Renamed", service.GetCategories(catA.Value.Id)[0].Name);

        // Create, Edit, Move, Duplicate Prompts
        var prompt1 = service.CreatePrompt(catA.Value.Id, "# E2E Prompt Content\nLine 2");
        Assert.IsTrue(promptRepo.Exists(prompt1.Value.Id));
        Assert.AreEqual("# E2E Prompt Content\nLine 2", promptRepo.Read(prompt1.Value.Id));

        service.EditPrompt(prompt1.Value.Id, "# E2E Prompt Content Edited\nLine 2");
        Assert.AreEqual("# E2E Prompt Content Edited\nLine 2", promptRepo.Read(prompt1.Value.Id));

        service.MovePrompt(prompt1.Value.Id, catNested.Value.Id);
        Assert.AreEqual(catNested.Value.Id, service.CurrentDocument.Prompts.First(p => p.Id == prompt1.Value.Id).CategoryId);

        var promptDup = service.DuplicatePrompt(prompt1.Value.Id, null);
        Assert.AreNotEqual(prompt1.Value.Id, promptDup.Value.Id);
        Assert.IsNull(promptDup.Value.CategoryId);
        Assert.AreEqual("# E2E Prompt Content Edited\nLine 2", promptRepo.Read(promptDup.Value.Id));

        // Create 50k Prompt
        string largeContent = new string('A', 50000);
        var promptLarge = service.CreatePrompt(null, largeContent);
        Assert.AreEqual(50000, promptRepo.Read(promptLarge.Value.Id).Length);

        // Create empty prompt
        var promptEmpty = service.CreatePrompt(null, "");
        Assert.AreEqual("", promptRepo.Read(promptEmpty.Value.Id));

        // Non-empty category deletion rejection
        Assert.IsFalse(service.CanDeleteCategory(catA.Value.Id, out string? blockReason));
        Assert.AreEqual("This category is not empty.\r\n\r\nMove or delete its prompts and subcategories first.", blockReason);

        // Delete prompt and empty category
        service.DeletePrompt(prompt1.Value.Id);
        Assert.IsFalse(promptRepo.Exists(prompt1.Value.Id));
        service.DeleteCategory(catNested.Value.Id);
        Assert.IsTrue(service.CanDeleteCategory(catA.Value.Id, out _));
        service.DeleteCategory(catA.Value.Id);

        // 3. Restart Persistence Verification
        var restartResult = startup.LoadOrInitialize();
        Assert.IsFalse(restartResult.RecoveredFromBackup);
        var restartService = new PromptLibraryService(restartResult.Document, libRepo, promptRepo);

        // Assert deleted category is absent
        Assert.IsFalse(restartService.GetCategories(null).Any(c => c.Name == "E2E_Cat_A"));

        // Assert duplicate prompt and large prompt persisted
        var persistedPrompts = restartService.GetPrompts(null);
        Assert.IsTrue(persistedPrompts.Any(p => p.Id == promptDup.Value.Id && p.Content == "# E2E Prompt Content Edited\nLine 2"));
        Assert.IsTrue(persistedPrompts.Any(p => p.Id == promptLarge.Value.Id && p.Content.Length == 50000));
        Assert.IsTrue(persistedPrompts.Any(p => p.Id == promptEmpty.Value.Id && p.Content == ""));
    }

    [TestMethod]
    public void Unavailable_prompt_state_and_actions()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);

        var pId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();

        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord
                {
                    Id = destinationId,
                    ParentId = null,
                    Name = "Destination",
                    SortOrder = 10
                }
            ],
            Prompts =
            [
                new PromptRecord
                {
                    Id = pId,
                    CategoryId = null,
                    SortOrder = 10
                }
            ]
        };
        libRepo.Commit(doc);

        var service = new PromptLibraryService(doc, libRepo, promptRepo);

        // Verify unavailable status on Home
        var prompts = service.GetPrompts(null);
        Assert.AreEqual(1, prompts.Count);
        Assert.IsFalse(prompts[0].IsContentAvailable);
        Assert.IsNotNull(prompts[0].LoadError);

        // Unavailable prompt can be moved to another category (PLH8-002)
        service.MovePrompt(pId, destinationId);
        var moved = service.CurrentDocument.Prompts.Single(p => p.Id == pId);
        Assert.AreEqual(destinationId, moved.CategoryId);

        var destinationPrompts = service.GetPrompts(destinationId);
        Assert.AreEqual(1, destinationPrompts.Count);
        Assert.AreEqual(pId, destinationPrompts[0].Id);
        Assert.IsFalse(destinationPrompts[0].IsContentAvailable);
        Assert.IsNotNull(destinationPrompts[0].LoadError);

        // Unavailable prompt can be deleted
        service.DeletePrompt(pId);
        Assert.IsFalse(service.CurrentDocument.Prompts.Any(p => p.Id == pId));
    }

    [TestMethod]
    public void Unavailable_prompt_cannot_be_duplicated()
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
            Prompts =
            [
                new PromptRecord
                {
                    Id = pId,
                    CategoryId = null,
                    SortOrder = 10
                }
            ]
        };
        libRepo.Commit(doc);

        var service = new PromptLibraryService(doc, libRepo, promptRepo);

        // Duplicating an unavailable prompt throws InvalidOperationException
        var ex = Assert.Throws<InvalidOperationException>(() => service.DuplicatePrompt(pId, null));
        Assert.IsTrue(ex.Message.Contains("content file could not be read"));
    }

    [TestMethod]
    public void Orphan_prompt_files_preserved_on_restart()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startup = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);

        var initResult = startup.LoadOrInitialize();

        // Create orphan file on disk
        var orphanId = Guid.NewGuid();
        string orphanPath = paths.GetPromptPath(orphanId);
        File.WriteAllText(orphanPath, "Orphan Content");

        // Restart
        var restartResult = startup.LoadOrInitialize();
        var service = new PromptLibraryService(restartResult.Document, libRepo, promptRepo);

        // Orphan is not in metadata list
        Assert.IsFalse(service.GetPrompts(null).Any(p => p.Id == orphanId));

        // Orphan is preserved on disk
        Assert.IsTrue(File.Exists(orphanPath));
        Assert.AreEqual("Orphan Content", File.ReadAllText(orphanPath));
    }

    [TestMethod]
    public void Double_corruption_and_future_schema_safety_stop()
    {
        using var testDir = new TestDirectory();
        var paths = new AppPaths(testDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startup = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);

        startup.LoadOrInitialize();

        // 1. Double corruption
        File.WriteAllText(paths.LibraryPath, "CORRUPT_PRIMARY");
        File.WriteAllText(paths.LibraryBackupPath, "CORRUPT_BACKUP");
        Assert.Throws<InvalidDataException>(() => startup.LoadOrInitialize());

        // 2. Future schema
        File.WriteAllText(paths.LibraryPath, "{\"schemaVersion\":999,\"categories\":[],\"prompts\":[]}");
        Assert.Throws<UnsupportedLibrarySchemaException>(() => startup.LoadOrInitialize());
    }

    [TestMethod]
    public void Full_E2E_Title_CRUD_Migration_and_Restart_Persistence()
    {
        using var testDir1 = new TestDirectory();
        using var testDir2 = new TestDirectory();

        var paths1 = new AppPaths(testDir1.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo1 = new LibraryRepository(paths1, writer);
        var promptRepo1 = new PromptRepository(paths1, writer, deleter);
        var startup1 = new LibraryStartupService(paths1, libRepo1, promptRepo1, deleter, writer);

        var initResult = startup1.LoadOrInitialize();
        var service1 = new PromptLibraryService(initResult.Document, libRepo1, promptRepo1);

        // 1. Create prompt with custom headline
        var prompt = service1.CreatePrompt(null, "Line 1 Content\nLine 2 Content", "Custom Headline").Value;
        Assert.AreEqual("Custom Headline", prompt.Title);

        // 2. Edit prompt title
        service1.EditPrompt(prompt.Id, "Line 1 Modified\nLine 2 Content", "Updated Headline");
        var updated = service1.GetPrompts(null).Single(p => p.Id == prompt.Id);
        Assert.AreEqual("Updated Headline", updated.Title);
        Assert.AreEqual("Line 1 Modified\nLine 2 Content", promptRepo1.Read(prompt.Id));

        // 3. Migrate data to second directory via DataFolderTransitionCoordinator
        string settingsPath = Path.Combine(testDir1.Root, "settings.json");
        var settingsRepo = new AppSettingsRepository(writer, settingsPath);
        var confirmation = new FakeUserConfirmationService { ConfirmationResult = true };
        var coordinator = new DataFolderTransitionCoordinator(
            testDir1.Root,
            settingsRepo,
            new DataFolderMigrationService(),
            confirmation);

        DataFolderTransitionResult transitionResult = coordinator.RequestTransition(testDir2.Root);
        Assert.IsTrue(transitionResult.Changed);
        Assert.IsTrue(transitionResult.RestartRequired);

        // 4. Start from second directory using settings
        string effectiveRoot = settingsRepo.GetEffectiveDataRoot();
        var paths2 = new AppPaths(effectiveRoot);
        var libRepo2 = new LibraryRepository(paths2, writer);
        var promptRepo2 = new PromptRepository(paths2, writer, deleter);
        var startup2 = new LibraryStartupService(paths2, libRepo2, promptRepo2, deleter, writer);

        var migratedStartup = startup2.LoadOrInitialize();
        var service2 = new PromptLibraryService(migratedStartup.Document, libRepo2, promptRepo2);

        var migratedPrompt = service2.GetPrompts(null).Single(p => p.Id == prompt.Id);
        Assert.AreEqual("Updated Headline", migratedPrompt.Title);
        Assert.AreEqual("Line 1 Modified\nLine 2 Content", promptRepo2.Read(prompt.Id));
    }
}