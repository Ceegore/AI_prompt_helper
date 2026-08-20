using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class DataFolderMigrationServiceTests
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
        var result = service.CreatePrompt(null, "Test prompt content for migration", "Migration Test Title");
        promptId = result.Value.Id;
    }

    [TestMethod]
    public void PrepareTarget_same_directory_is_noop()
    {
        using var testDir = new TestDirectory();
        SeedValidLibrary(testDir.Root, out _);

        var migration = new DataFolderMigrationService();
        var result = migration.PrepareTarget(testDir.Root, testDir.Root);

        Assert.AreEqual(Path.GetFullPath(testDir.Root), result.NormalizedTargetRoot);
        Assert.IsFalse(result.ExistingLibraryFound);
        Assert.IsFalse(result.Copied);
    }

    [TestMethod]
    public void PrepareTarget_empty_target_copies_library_and_prompts_without_lock_files()
    {
        using var sourceDir = new TestDirectory();
        using var targetDir = new TestDirectory();
        SeedValidLibrary(sourceDir.Root, out Guid promptId);

        // Place lock and marker files in source to ensure they are NOT copied
        File.WriteAllText(Path.Combine(sourceDir.Root, ".app.lock"), "lock data");
        File.WriteAllText(Path.Combine(sourceDir.Root, "initializing.marker"), "marker data");

        var migration = new DataFolderMigrationService();
        var result = migration.PrepareTarget(sourceDir.Root, targetDir.Root);

        Assert.AreEqual(Path.GetFullPath(targetDir.Root), result.NormalizedTargetRoot);
        Assert.IsFalse(result.ExistingLibraryFound);
        Assert.IsTrue(result.Copied);

        // Verify target contents
        Assert.IsTrue(File.Exists(Path.Combine(targetDir.Root, "library.json")));
        Assert.IsTrue(File.Exists(Path.Combine(targetDir.Root, "prompts", $"{promptId:N}.md")));
        Assert.IsFalse(File.Exists(Path.Combine(targetDir.Root, ".app.lock")));
        Assert.IsFalse(File.Exists(Path.Combine(targetDir.Root, "initializing.marker")));

        // Verify source files are intact (source was not deleted)
        Assert.IsTrue(File.Exists(Path.Combine(sourceDir.Root, "library.json")));
        Assert.IsTrue(File.Exists(Path.Combine(sourceDir.Root, "prompts", $"{promptId:N}.md")));

        // Verify target library can be opened by LibraryStartupService
        var targetPaths = new AppPaths(targetDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(targetPaths, writer);
        var promptRepo = new PromptRepository(targetPaths, writer, deleter);
        var startupService = new LibraryStartupService(targetPaths, libRepo, promptRepo, deleter, writer);
        var loaded = startupService.LoadOrInitialize();

        Assert.IsNotNull(loaded.Document);
        Assert.IsTrue(loaded.Document.Prompts.Any(p => p.Id == promptId));
        var loadedPrompt = loaded.Document.Prompts.First(p => p.Id == promptId);
        Assert.AreEqual("Migration Test Title", loadedPrompt.Title);
        Assert.AreEqual("Test prompt content for migration", promptRepo.Read(promptId));
    }

    [TestMethod]
    public void PrepareTarget_existing_valid_target_library_does_not_overwrite()
    {
        using var sourceDir = new TestDirectory();
        using var targetDir = new TestDirectory();
        SeedValidLibrary(sourceDir.Root, out Guid sourcePromptId);
        SeedValidLibrary(targetDir.Root, out Guid targetPromptId);

        var migration = new DataFolderMigrationService();
        var result = migration.PrepareTarget(sourceDir.Root, targetDir.Root);

        Assert.AreEqual(Path.GetFullPath(targetDir.Root), result.NormalizedTargetRoot);
        Assert.IsTrue(result.ExistingLibraryFound);
        Assert.IsFalse(result.Copied);

        // Verify target library still contains targetPromptId and not sourcePromptId
        var targetPaths = new AppPaths(targetDir.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(targetPaths, writer);
        var promptRepo = new PromptRepository(targetPaths, writer, deleter);
        var startupService = new LibraryStartupService(targetPaths, libRepo, promptRepo, deleter, writer);
        var loaded = startupService.LoadOrInitialize();

        Assert.IsTrue(loaded.Document.Prompts.Any(p => p.Id == targetPromptId));
        Assert.IsFalse(loaded.Document.Prompts.Any(p => p.Id == sourcePromptId));
    }

    [TestMethod]
    public void PrepareTarget_existing_corrupt_target_library_throws_and_does_not_mutate()
    {
        using var sourceDir = new TestDirectory();
        using var targetDir = new TestDirectory();
        SeedValidLibrary(sourceDir.Root, out _);

        // Put a corrupt library.json in target
        File.WriteAllText(Path.Combine(targetDir.Root, "library.json"), "{ invalid JSON content }");

        var migration = new DataFolderMigrationService();
        Assert.Throws<InvalidDataException>(() => migration.PrepareTarget(sourceDir.Root, targetDir.Root));
    }

    [TestMethod]
    public void PrepareTarget_existing_target_with_missing_referenced_prompt_throws()
    {
        using var sourceDir = new TestDirectory();
        using var targetDir = new TestDirectory();
        SeedValidLibrary(sourceDir.Root, out _);
        SeedValidLibrary(targetDir.Root, out Guid targetPromptId);

        // Delete the referenced prompt file from target
        File.Delete(Path.Combine(targetDir.Root, "prompts", $"{targetPromptId:N}.md"));

        var migration = new DataFolderMigrationService();
        Assert.Throws<InvalidDataException>(() => migration.PrepareTarget(sourceDir.Root, targetDir.Root));
    }

    [TestMethod]
    public void PrepareTarget_empty_or_whitespace_path_throws_ArgumentException()
    {
        using var sourceDir = new TestDirectory();
        var migration = new DataFolderMigrationService();

        Assert.Throws<ArgumentException>(() => migration.PrepareTarget(sourceDir.Root, ""));
        Assert.Throws<ArgumentException>(() => migration.PrepareTarget(sourceDir.Root, "   "));
    }

    [TestMethod]
    public void PrepareTarget_prompt_collision_in_target_aborts_and_cleans_up()
    {
        using var sourceDir = new TestDirectory();
        using var targetDir = new TestDirectory();
        SeedValidLibrary(sourceDir.Root, out Guid promptId);

        // Target directory has no library.json, but has a pre-existing file in prompts with the same name
        string targetPromptsDir = Path.Combine(targetDir.Root, "prompts");
        Directory.CreateDirectory(targetPromptsDir);
        File.WriteAllText(Path.Combine(targetPromptsDir, $"{promptId:N}.md"), "pre-existing colliding file");

        var migration = new DataFolderMigrationService();
        Assert.Throws<IOException>(() => migration.PrepareTarget(sourceDir.Root, targetDir.Root));

        // Ensure target library.json was rolled back / deleted
        Assert.IsFalse(File.Exists(Path.Combine(targetDir.Root, "library.json")));
    }
}
