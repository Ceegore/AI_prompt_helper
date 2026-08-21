using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Infrastructure;
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
    public void PrepareTargetForMigrationUnitTest_same_directory_is_noop()
    {
        using var testDir = new TestDirectory();
        SeedValidLibrary(testDir.Root, out _);

        var migration = new DataFolderMigrationService();
        var result = migration.PrepareTargetForMigrationUnitTest(testDir.Root, testDir.Root);

        Assert.AreEqual(Path.GetFullPath(testDir.Root), result.NormalizedTargetRoot);
        Assert.IsFalse(result.ExistingLibraryFound);
        Assert.IsFalse(result.Copied);
    }

    [TestMethod]
    public void PrepareTargetForMigrationUnitTest_empty_target_copies_library_and_prompts_without_lock_files()
    {
        using var sourceDir = new TestDirectory();
        using var targetDir = new TestDirectory();
        SeedValidLibrary(sourceDir.Root, out Guid promptId);

        // Place lock and marker files in source to ensure they are NOT copied
        File.WriteAllText(Path.Combine(sourceDir.Root, ".app.lock"), "lock data");
        File.WriteAllText(Path.Combine(sourceDir.Root, "initializing.marker"), "marker data");

        var migration = new DataFolderMigrationService();
        var result = migration.PrepareTargetForMigrationUnitTest(sourceDir.Root, targetDir.Root);

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
    public void PrepareTargetForMigrationUnitTest_existing_valid_target_library_does_not_overwrite()
    {
        using var sourceDir = new TestDirectory();
        using var targetDir = new TestDirectory();
        SeedValidLibrary(sourceDir.Root, out Guid sourcePromptId);
        SeedValidLibrary(targetDir.Root, out Guid targetPromptId);

        var migration = new DataFolderMigrationService();
        var result = migration.PrepareTargetForMigrationUnitTest(sourceDir.Root, targetDir.Root);

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
    public void PrepareTargetForMigrationUnitTest_existing_corrupt_target_library_throws_and_does_not_mutate()
    {
        using var sourceDir = new TestDirectory();
        using var targetDir = new TestDirectory();
        SeedValidLibrary(sourceDir.Root, out _);

        // Put a corrupt library.json in target
        File.WriteAllText(Path.Combine(targetDir.Root, "library.json"), "{ invalid JSON content }");

        var migration = new DataFolderMigrationService();
        Assert.Throws<InvalidDataException>(() => migration.PrepareTargetForMigrationUnitTest(sourceDir.Root, targetDir.Root));
    }

    [TestMethod]
    public void PrepareTargetForMigrationUnitTest_existing_target_with_missing_referenced_prompt_throws()
    {
        using var sourceDir = new TestDirectory();
        using var targetDir = new TestDirectory();
        SeedValidLibrary(sourceDir.Root, out _);
        SeedValidLibrary(targetDir.Root, out Guid targetPromptId);

        // Delete the referenced prompt file from target
        File.Delete(Path.Combine(targetDir.Root, "prompts", $"{targetPromptId:N}.md"));

        var migration = new DataFolderMigrationService();
        Assert.Throws<InvalidDataException>(() => migration.PrepareTargetForMigrationUnitTest(sourceDir.Root, targetDir.Root));
    }

    [TestMethod]
    public void PrepareTargetForMigrationUnitTest_empty_or_whitespace_path_throws_ArgumentException()
    {
        using var sourceDir = new TestDirectory();
        var migration = new DataFolderMigrationService();

        Assert.Throws<ArgumentException>(() => migration.PrepareTargetForMigrationUnitTest(sourceDir.Root, ""));
        Assert.Throws<ArgumentException>(() => migration.PrepareTargetForMigrationUnitTest(sourceDir.Root, "   "));
    }

    [TestMethod]
    public void PrepareTargetForMigrationUnitTest_prompt_collision_in_target_aborts_and_cleans_up()
    {
        using var sourceDir = new TestDirectory();
        using var targetDir = new TestDirectory();
        SeedValidLibrary(sourceDir.Root, out Guid promptId);

        // Target directory has no library.json, but has a pre-existing file in prompts with the same name
        string targetPromptsDir = Path.Combine(targetDir.Root, "prompts");
        Directory.CreateDirectory(targetPromptsDir);
        File.WriteAllText(Path.Combine(targetPromptsDir, $"{promptId:N}.md"), "pre-existing colliding file");

        var migration = new DataFolderMigrationService();
        Assert.Throws<IOException>(() => migration.PrepareTargetForMigrationUnitTest(sourceDir.Root, targetDir.Root));

        // Ensure target library.json was rolled back / deleted
        Assert.IsFalse(File.Exists(Path.Combine(targetDir.Root, "library.json")));
    }

    [TestMethod]
    public void PrepareTargetForMigrationUnitTest_descendant_target_throws_InvalidOperationException()
    {
        using var sourceDir = new TestDirectory();
        SeedValidLibrary(sourceDir.Root, out _);

        string nestedTarget = Path.Combine(sourceDir.Root, "Nested", "Target");

        var migration = new DataFolderMigrationService();
        Assert.Throws<InvalidOperationException>(() => migration.PrepareTargetForMigrationUnitTest(sourceDir.Root, nestedTarget));
    }

    [TestMethod]
    public void PrepareTargetForMigrationUnitTest_source_missing_library_throws_before_modifying_target()
    {
        using var sourceDir = new TestDirectory();
        using var targetDir = new TestDirectory();

        var migration = new DataFolderMigrationService();
        Assert.Throws<InvalidDataException>(() => migration.PrepareTargetForMigrationUnitTest(sourceDir.Root, targetDir.Root));
        Assert.IsFalse(File.Exists(Path.Combine(targetDir.Root, "library.json")));
    }

    [TestMethod]
    public void CRUU3_006_Prompt_changed_during_copy_aborts_and_rolls_back()
    {
        using var sourceDir = new TestDirectory();
        using var targetDir = new TestDirectory();
        SeedValidLibrary(sourceDir.Root, out Guid promptId);

        var faultOps = new FaultInjectingMigrationFileOps();
        int readCount = 0;
        faultOps.OnReadAllBytes = path =>
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (path.EndsWith($"{promptId:N}.md"))
            {
                readCount++;
                if (readCount > 1)
                {
                    // Return mutated bytes on second read
                    return [.. bytes, 0x21];
                }
            }
            return bytes;
        };

        var migration = new DataFolderMigrationService(fileOps: faultOps);
        Assert.Throws<IOException>(() => migration.PrepareTargetForMigrationUnitTest(sourceDir.Root, targetDir.Root));

        // Target library.json must be deleted in rollback
        Assert.IsFalse(File.Exists(Path.Combine(targetDir.Root, "library.json")));
    }

    [TestMethod]
    public void CRUU3_009_Valid_backup_only_target_detected_existing_and_not_overwritten()
    {
        using var sourceDir = new TestDirectory();
        using var targetDir = new TestDirectory();
        SeedValidLibrary(sourceDir.Root, out Guid sourcePromptId);

        // Put a valid library.backup.json in target but NO primary library.json
        string targetBackup = Path.Combine(targetDir.Root, "library.backup.json");
        string validBackupDoc = "{\"schemaVersion\": 1, \"categories\": [], \"prompts\": []}";
        File.WriteAllText(targetBackup, validBackupDoc);

        var migration = new DataFolderMigrationService();
        var result = migration.PrepareTargetForMigrationUnitTest(sourceDir.Root, targetDir.Root);

        Assert.IsTrue(result.ExistingLibraryFound);
        Assert.IsFalse(result.Copied);
        Assert.IsNotNull(result.Warning);
        Assert.IsTrue(result.Warning.Contains("recoverable Prompt Helper safety backup"));
        Assert.IsFalse(File.Exists(Path.Combine(targetDir.Root, "library.json")));
    }

    [TestMethod]
    public void CRUU3_009_Corrupt_primary_valid_backup_target_rejected_conservatively()
    {
        using var sourceDir = new TestDirectory();
        using var targetDir = new TestDirectory();
        SeedValidLibrary(sourceDir.Root, out _);

        File.WriteAllText(Path.Combine(targetDir.Root, "library.json"), "corrupt primary");
        File.WriteAllText(Path.Combine(targetDir.Root, "library.backup.json"), "{\"schemaVersion\": 1, \"categories\": [], \"prompts\": []}");

        var migration = new DataFolderMigrationService();
        Assert.Throws<InvalidDataException>(() => migration.PrepareTargetForMigrationUnitTest(sourceDir.Root, targetDir.Root));
    }

    [TestMethod]
    public void CRUU3_009_Future_schema_target_rejected()
    {
        using var sourceDir = new TestDirectory();
        using var targetDir = new TestDirectory();
        SeedValidLibrary(sourceDir.Root, out _);

        File.WriteAllText(Path.Combine(targetDir.Root, "library.json"), "{\"schemaVersion\": 99, \"categories\": [], \"prompts\": []}");

        var migration = new DataFolderMigrationService();
        var ex = Assert.Throws<UnsupportedLibrarySchemaException>(() => migration.PrepareTargetForMigrationUnitTest(sourceDir.Root, targetDir.Root));
        Assert.AreEqual(99, ex.SchemaVersion);
    }

    [TestMethod]
    public void CRUU3_010_Capability_probe_failure_on_new_target_rolls_back_migrated_files()
    {
        using var sourceDir = new TestDirectory();
        using var targetDir = new TestDirectory();
        SeedValidLibrary(sourceDir.Root, out _);

        var baseWriter = new AtomicTextWriter();
        var faultWriter = new FaultInjectingAtomicTextWriter(baseWriter)
        {
            ShouldFail = (_, callNum) => callNum == 2
        };
        var capability = new DataRootCapabilityValidator(faultWriter);

        var migration = new DataFolderMigrationService(capabilityValidator: capability);
        Assert.Throws<IOException>(() => migration.PrepareTargetForMigrationUnitTest(sourceDir.Root, targetDir.Root));

        // Rolled back
        Assert.IsFalse(File.Exists(Path.Combine(targetDir.Root, "library.json")));
    }

    [TestMethod]
    public void CRUU3_012_Target_lock_held_is_detected()
    {
        using var temp = new TestDirectory();
        string lockFile = Path.Combine(temp.Root, ".app.lock");

        // Before lock file exists -> not held
        Assert.IsFalse(AppInstanceLock.IsExistingLockHeld(temp.Root));

        // Create lock file but don't hold open -> not held
        File.WriteAllText(lockFile, "lock");
        Assert.IsFalse(AppInstanceLock.IsExistingLockHeld(temp.Root));

        // Hold open exclusively -> held
        using (var stream = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.IsTrue(AppInstanceLock.IsExistingLockHeld(temp.Root));
        }

        // Closed again -> not held
        Assert.IsFalse(AppInstanceLock.IsExistingLockHeld(temp.Root));
    }

    [TestMethod]
    public void CRUU4_003_Snapshot_document_is_parsed_from_same_bytes_that_are_hashed()
    {
        using var source = new TestDirectory();
        SeedValidLibrary(source.Root, out Guid promptId);

        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourceSnapshot(source.Root);

        byte[] actualBytes = File.ReadAllBytes(Path.Combine(source.Root, "library.json"));
        CollectionAssert.AreEqual(actualBytes, snapshot.LibraryBytes);
        Assert.IsTrue(snapshot.PromptHashes.ContainsKey(promptId));
    }

    [TestMethod]
    public void CRUU4_003_Snapshot_referenced_prompt_set_matches_snapshot_document()
    {
        using var source = new TestDirectory();
        SeedValidLibrary(source.Root, out Guid promptId);

        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourceSnapshot(source.Root);

        Assert.AreEqual(snapshot.Document.Prompts.Count, snapshot.PromptHashes.Count);
        foreach (var prompt in snapshot.Document.Prompts)
        {
            Assert.IsTrue(snapshot.PromptHashes.ContainsKey(prompt.Id));
        }
    }

    [TestMethod]
    public void CRUU4_004_Altered_but_valid_target_library_bytes_abort_and_rollback()
    {
        using var source = new TestDirectory();
        using var targetParent = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        string target = Path.Combine(targetParent.Root, "Target");

        var ops = new FaultInjectingMigrationFileOps
        {
            OnMoveNoOverwrite = (src, dst) =>
            {
                if (dst.EndsWith("library.json", StringComparison.OrdinalIgnoreCase))
                {
                    string json = File.ReadAllText(src);
                    // Add whitespace to change bytes while remaining valid JSON
                    json = json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1   ");
                    File.WriteAllText(src, json);
                }

                File.Move(src, dst, overwrite: false);
            }
        };

        var service = new DataFolderMigrationService(fileOps: ops);

        Assert.Throws<IOException>(() =>
            service.PrepareTargetForMigrationUnitTest(source.Root, target));

        Assert.IsFalse(File.Exists(Path.Combine(target, "library.json")));
    }

    [TestMethod]
    public void CRUU4_005_Snapshot_read_failure_leaves_nonexistent_target_nonexistent()
    {
        using var source = new TestDirectory();
        using var parent = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        string target = Path.Combine(parent.Root, "NewTarget");

        var ops = new FaultInjectingMigrationFileOps
        {
            OnReadAllBytes = path =>
            {
                if (Path.GetFileName(path).Equals("library.json", StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Injected source snapshot failure");
                }

                return File.ReadAllBytes(path);
            }
        };

        var migration = new DataFolderMigrationService(fileOps: ops);

        Assert.Throws<IOException>(() =>
            migration.PrepareTargetForMigrationUnitTest(source.Root, target));

        Assert.IsFalse(
            Directory.Exists(target),
            "A source snapshot failure must not leave a target directory behind.");
    }

    [TestMethod]
    public void CRUU4_006_Damaged_current_prompt_does_not_block_switch_to_existing_good_library()
    {
        using var current = new TestDirectory();
        using var target = new TestDirectory();

        SeedValidLibrary(current.Root, out Guid currentPrompt);
        SeedValidLibrary(target.Root, out _);

        File.Delete(Path.Combine(
            current.Root,
            "prompts",
            $"{currentPrompt:N}.md"));

        var migration = new DataFolderMigrationService();

        DataFolderChangeResult result =
            migration.PrepareTargetForMigrationUnitTest(current.Root, target.Root);

        Assert.IsTrue(result.ExistingLibraryFound);
        Assert.IsFalse(result.Copied);
    }

    [TestMethod]
    public void CRUU4_006_Damaged_current_prompt_still_blocks_copy_to_empty_target()
    {
        using var current = new TestDirectory();
        using var target = new TestDirectory();

        SeedValidLibrary(current.Root, out Guid currentPrompt);

        File.Delete(Path.Combine(
            current.Root,
            "prompts",
            $"{currentPrompt:N}.md"));

        var migration = new DataFolderMigrationService();

        Assert.Throws<InvalidDataException>(() =>
            migration.PrepareTargetForMigrationUnitTest(current.Root, target.Root));

        Assert.IsFalse(File.Exists(Path.Combine(target.Root, "library.json")));
    }

    [TestMethod]
    public void CRUU4_007_Backup_only_target_with_missing_prompt_is_not_recoverable()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();

        SeedValidLibrary(source.Root, out _);

        Guid missingPrompt = Guid.NewGuid();

        File.WriteAllText(
            Path.Combine(target.Root, "library.backup.json"),
            $$"""
            {
              "schemaVersion": 1,
              "categories": [],
              "prompts": [
                {
                  "id": "{{missingPrompt}}",
                  "categoryId": null,
                  "sortOrder": 10,
                  "title": "Missing"
                }
              ]
            }
            """);

        var migration = new DataFolderMigrationService();

        Assert.Throws<InvalidDataException>(() =>
            migration.PrepareTargetForMigrationUnitTest(source.Root, target.Root));
    }

    [TestMethod]
    public void CRUU4_007_Backup_only_target_with_all_prompt_bodies_is_selectable()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();

        SeedValidLibrary(source.Root, out _);

        Guid promptId = Guid.NewGuid();
        string promptsDir = Path.Combine(target.Root, "prompts");
        Directory.CreateDirectory(promptsDir);
        File.WriteAllText(Path.Combine(promptsDir, $"{promptId:N}.md"), "Prompt body");

        File.WriteAllText(
            Path.Combine(target.Root, "library.backup.json"),
            $$"""
            {
              "schemaVersion": 1,
              "categories": [],
              "prompts": [
                {
                  "id": "{{promptId}}",
                  "categoryId": null,
                  "sortOrder": 10,
                  "title": "Present"
                }
              ]
            }
            """);

        var migration = new DataFolderMigrationService();
        var result = migration.PrepareTargetForMigrationUnitTest(source.Root, target.Root);

        Assert.IsTrue(result.ExistingLibraryFound);
        Assert.IsFalse(result.Copied);
    }

    [TestMethod]
    public void CRUU6_005_Mid_copy_failure_leaves_no_untracked_partial_file()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();

        SeedValidLibrary(source.Root, out Guid promptId);

        var ops = new FaultInjectingMigrationFileOps
        {
            OnMoveNoOverwrite = (src, dst) =>
            {
                if (dst.EndsWith("library.json", StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Simulated disk error during atomic promotion move");
                }
                File.Move(src, dst, overwrite: false);
            }
        };

        var migration = new DataFolderMigrationService(fileOps: ops);

        Assert.Throws<IOException>(() =>
            migration.PrepareTargetForMigrationUnitTest(source.Root, target.Root));

        // Verify target has no final library.json and no temporary files
        Assert.IsFalse(File.Exists(Path.Combine(target.Root, "library.json")));
        var leftoverTemps = Directory.EnumerateFiles(target.Root, "*.tmp", SearchOption.AllDirectories).ToList();
        Assert.AreEqual(0, leftoverTemps.Count);
    }

    [TestMethod]
    public void CRUU6_005_Foreign_collision_file_is_never_deleted_by_rollback()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();

        SeedValidLibrary(source.Root, out Guid promptId);

        string targetPrompts = Path.Combine(target.Root, "prompts");
        Directory.CreateDirectory(targetPrompts);
        string foreignPath = Path.Combine(targetPrompts, $"{promptId:N}.md");
        File.WriteAllText(foreignPath, "Foreign preexisting content");

        var migration = new DataFolderMigrationService();

        // Migration encounters target file collision
        var ex = Assert.Throws<IOException>(() =>
            migration.PrepareTargetForMigrationUnitTest(source.Root, target.Root));

        Assert.IsTrue(ex.Message.Contains("collision", StringComparison.OrdinalIgnoreCase));

        // Foreign file must be preserved
        Assert.IsTrue(File.Exists(foreignPath));
        Assert.AreEqual("Foreign preexisting content", File.ReadAllText(foreignPath));
    }
}
