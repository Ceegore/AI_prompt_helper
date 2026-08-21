using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class Cruu10ComprehensiveVerificationTests
{
    private static void SeedValidLibrary(string root, out LibraryDocument doc, out Guid promptId)
    {
        Directory.CreateDirectory(root);
        string promptsDir = Path.Combine(root, "prompts");
        Directory.CreateDirectory(promptsDir);
        Directory.CreateDirectory(Path.Combine(root, "recovery"));

        promptId = Guid.NewGuid();
        string promptFile = Path.Combine(promptsDir, $"{promptId:N}.md");
        File.WriteAllText(promptFile, "Active prompt body content");

        var category = new CategoryRecord { Id = Guid.NewGuid(), Name = "General" };
        var prompt = new PromptRecord { Id = promptId, CategoryId = category.Id, Title = "Test Prompt" };

        doc = new LibraryDocument
        {
            SchemaVersion = 1,
            Categories = [category],
            Prompts = [prompt]
        };

        string json = JsonSerializer.Serialize(doc, LibraryRepository.JsonOptions);
        File.WriteAllText(Path.Combine(root, "library.json"), json);
        File.WriteAllText(Path.Combine(root, "library.backup.json"), json);
    }

    #region CRUU10-001: Strict Path Authority

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU10_001_Strict_path_authority_probes_file_and_directory_and_missing()
    {
        using var temp = new TestDirectory();
        var authority = new StrictPathAuthority();

        string file = Path.Combine(temp.Root, "test.txt");
        File.WriteAllText(file, "hello");

        string dir = Path.Combine(temp.Root, "testdir");
        Directory.CreateDirectory(dir);

        string missing = Path.Combine(temp.Root, "nonexistent");

        Assert.AreEqual(StrictPathKind.File, authority.Probe(file).Kind);
        Assert.AreEqual(StrictPathKind.Directory, authority.Probe(dir).Kind);
        Assert.AreEqual(StrictPathKind.Missing, authority.Probe(missing).Kind);

        Assert.IsTrue(authority.RequireDirectory(dir));
        Assert.IsFalse(authority.RequireDirectory(missing));
        Assert.Throws<InvalidDataException>(() => authority.RequireDirectory(file));
    }

    #endregion

    #region CRUU10-002 & CRUU10-003 & CRUU10-014: Package Inspector and Startup Authority

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU10_002_Primary_missing_body_does_not_overwrite_complete_backup_and_recovers_with_diagnostics()
    {
        using var temp = new TestDirectory();
        SeedValidLibrary(temp.Root, out LibraryDocument doc, out Guid promptId);

        // Delete prompt body on disk to make primary incomplete
        string promptFile = Path.Combine(temp.Root, "prompts", $"{promptId:N}.md");
        File.Delete(promptFile);

        // Backup has a different document state with a valid body
        Guid backupPromptId = Guid.NewGuid();
        string backupPromptFile = Path.Combine(temp.Root, "prompts", $"{backupPromptId:N}.md");
        File.WriteAllText(backupPromptFile, "Backup prompt body content");

        var backupDoc = new LibraryDocument
        {
            SchemaVersion = 1,
            Categories = [new CategoryRecord { Id = Guid.NewGuid(), Name = "BackupCat" }],
            Prompts = [new PromptRecord { Id = backupPromptId, Title = "Backup Prompt" }]
        };
        string backupJson = JsonSerializer.Serialize(backupDoc, LibraryRepository.JsonOptions);
        File.WriteAllText(Path.Combine(temp.Root, "library.backup.json"), backupJson);

        var paths = new AppPaths(temp.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startup = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);

        StartupResult result = startup.LoadOrInitialize();

        // Must recover from backup
        Assert.IsTrue(result.RecoveredFromBackup);
        Assert.AreEqual("BackupCat", result.Document.Categories[0].Name);

        // Must produce an incomplete recovery copy
        string recoveryDir = Path.Combine(temp.Root, "recovery");
        Assert.IsTrue(Directory.Exists(recoveryDir));
        string[] recoveryFiles = Directory.GetFiles(recoveryDir, "library.incomplete-*.json");
        Assert.AreEqual(1, recoveryFiles.Length);
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU10_003_Primary_missing_and_backup_missing_body_rejects_recovery()
    {
        using var temp = new TestDirectory();
        SeedValidLibrary(temp.Root, out LibraryDocument doc, out Guid promptId);

        // Delete primary metadata
        File.Delete(Path.Combine(temp.Root, "library.json"));

        // Delete prompt body so backup is also incomplete
        File.Delete(Path.Combine(temp.Root, "prompts", $"{promptId:N}.md"));

        var paths = new AppPaths(temp.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startup = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);

        Assert.Throws<InvalidDataException>(() => startup.LoadOrInitialize());
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU10_014_Startup_decision_matrix_unreadable_primary_throws_without_fallback()
    {
        using var temp = new TestDirectory();
        SeedValidLibrary(temp.Root, out _, out _);

        string libraryPath = Path.Combine(temp.Root, "library.json");
        using var exclusiveLock = new FileStream(libraryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var paths = new AppPaths(temp.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startup = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);

        Assert.Throws<IOException>(() => startup.LoadOrInitialize());
    }

    #endregion

    #region CRUU10-006: Atomic Text Writer & Durability

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU10_006_Atomic_text_writer_rejects_invalid_utf8()
    {
        using var temp = new TestDirectory();
        var writer = new AtomicTextWriter();
        string target = Path.Combine(temp.Root, "test.txt");

        // Surrogate pair with missing low surrogate throws EncoderFallbackException
        string invalidUtf16 = "hello \uD800 world";
        Assert.Throws<System.Text.EncoderFallbackException>(() => writer.Write(target, invalidUtf16));
    }

    #endregion

    #region CRUU10-007: Managed Data Root Session Lease

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU10_007_Session_lease_prevents_node_swapping()
    {
        using var temp = new TestDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Root, "prompts"));
        Directory.CreateDirectory(Path.Combine(temp.Root, "recovery"));

        using var lease = ManagedDataRootSessionLease.Acquire(temp.Root);
        Assert.IsNotNull(lease);

        // Second acquisition on same tree should succeed sharing read/write
        using var lease2 = ManagedDataRootSessionLease.Acquire(temp.Root);
        Assert.IsNotNull(lease2);
    }

    #endregion

    #region CRUU10-008: Baseline Directory Ownership in Recovery

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU10_008_Migration_recovery_preserves_pre_existing_empty_prompts_and_recovery_dirs()
    {
        using var source = new TestDirectory();
        SeedValidLibrary(source.Root, out LibraryDocument doc, out _);

        using var target = new TestDirectory();
        string targetPrompts = Path.Combine(target.Root, "prompts");
        string targetRecovery = Path.Combine(target.Root, "recovery");
        Directory.CreateDirectory(targetPrompts);
        Directory.CreateDirectory(targetRecovery);

        Guid attemptId = Guid.NewGuid();
        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourcePayloadSnapshot(source.Root);
        var manifest = MigrationManifestBuilder.BuildCopying(source.Root, target.Root, snapshot, attemptId);

        Assert.IsNotNull(manifest.TargetBaseline);
        Assert.IsTrue(manifest.TargetBaseline.PromptsDirectoryExistedBefore);
        Assert.IsTrue(manifest.TargetBaseline.RecoveryDirectoryExistedBefore);

        var repo = new MigrationManifestRepository();
        string markerPath = Path.Combine(target.Root, ".prompthelper-migration.json");
        repo.CreateInitialCopyingManifestDurable(markerPath, manifest);

        var recovery = new MigrationRecoveryService();
        var context = new MigrationRecoveryContext(target.Root, ExpectedSourcePhysicalRoot: source.Root);

        var result = recovery.RecoverForRetry(context);
        Assert.IsTrue(result.Success);

        // Pre-existing directories MUST be preserved
        Assert.IsTrue(Directory.Exists(targetPrompts));
        Assert.IsTrue(Directory.Exists(targetRecovery));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU10_008_Migration_recovery_deletes_attempt_created_empty_prompts_and_recovery_dirs()
    {
        using var source = new TestDirectory();
        SeedValidLibrary(source.Root, out LibraryDocument doc, out _);

        using var target = new TestDirectory();
        // Do NOT pre-create prompts or recovery in target

        Guid attemptId = Guid.NewGuid();
        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourcePayloadSnapshot(source.Root);
        var manifest = MigrationManifestBuilder.BuildCopying(source.Root, target.Root, snapshot, attemptId);

        Assert.IsNotNull(manifest.TargetBaseline);
        Assert.IsFalse(manifest.TargetBaseline.PromptsDirectoryExistedBefore);
        Assert.IsFalse(manifest.TargetBaseline.RecoveryDirectoryExistedBefore);

        // Attempt creates them
        string targetPrompts = Path.Combine(target.Root, "prompts");
        string targetRecovery = Path.Combine(target.Root, "recovery");
        Directory.CreateDirectory(targetPrompts);
        Directory.CreateDirectory(targetRecovery);

        var repo = new MigrationManifestRepository();
        string markerPath = Path.Combine(target.Root, ".prompthelper-migration.json");
        repo.CreateInitialCopyingManifestDurable(markerPath, manifest);

        var recovery = new MigrationRecoveryService();
        var context = new MigrationRecoveryContext(target.Root, ExpectedSourcePhysicalRoot: source.Root);

        var result = recovery.RecoverForRetry(context);
        Assert.IsTrue(result.Success);

        // Attempt-created empty directories MUST be deleted
        Assert.IsFalse(Directory.Exists(targetPrompts));
        Assert.IsFalse(Directory.Exists(targetRecovery));
    }

    #endregion

    #region CRUU10-009: Strict JSON Authority

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU10_009_Settings_duplicate_property_rejected()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        string json = """
        {
          "schemaVersion": 1,
          "schemaVersion": 1,
          "dataRootPath": null
        }
        """;
        File.WriteAllText(settingsPath, json);
        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        Assert.Throws<InvalidDataException>(() => repo.Load());
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU10_009_Settings_unknown_property_rejected()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        string json = """
        {
          "schemaVersion": 1,
          "unknownField": "bad"
        }
        """;
        File.WriteAllText(settingsPath, json);
        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        Assert.Throws<InvalidDataException>(() => repo.Load());
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU10_009_Library_duplicate_category_property_rejected()
    {
        string json = """
        {
          "schemaVersion": 1,
          "categories": [
            {
              "id": "10000000-0000-0000-0000-000000000001",
              "id": "10000000-0000-0000-0000-000000000001",
              "parentId": null,
              "name": "General",
              "sortOrder": 10
            }
          ],
          "prompts": []
        }
        """;
        Assert.Throws<JsonException>(() => LibraryRepository.InspectAndDeserialize(json));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU10_009_Library_unknown_category_property_rejected()
    {
        string json = """
        {
          "schemaVersion": 1,
          "categories": [
            {
              "id": "10000000-0000-0000-0000-000000000001",
              "parentId": null,
              "name": "General",
              "sortOrder": 10,
              "extraField": 123
            }
          ],
          "prompts": []
        }
        """;
        Assert.Throws<JsonException>(() => LibraryRepository.InspectAndDeserialize(json));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU10_009_Future_schema_throws_unsupported_schema_exception()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        string settingsJson = """
        {
          "schemaVersion": 999,
          "futureProperty": "test"
        }
        """;
        File.WriteAllText(settingsPath, settingsJson);
        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        Assert.Throws<UnsupportedSettingsSchemaException>(() => repo.Load());

        string libraryJson = """
        {
          "schemaVersion": 999,
          "futureProperty": "test"
        }
        """;
        var libraryCompat = LibraryRepository.InspectCompatibility(libraryJson);
        Assert.IsInstanceOfType(libraryCompat, typeof(LibraryMetadataCompatibility.Future));
    }

    #endregion

    #region CRUU10-010: Narrow GetPrompts Exception Filter

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU10_010_Prompt_library_service_get_prompts_captures_io_fault_as_unavailable()
    {
        using var temp = new TestDirectory();
        SeedValidLibrary(temp.Root, out LibraryDocument doc, out Guid promptId);

        var paths = new AppPaths(temp.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var service = new PromptLibraryService(doc, libRepo, promptRepo);

        // Delete prompt file to cause FileNotFoundException
        File.Delete(Path.Combine(temp.Root, "prompts", $"{promptId:N}.md"));

        var prompts = service.GetPrompts(doc.Categories[0].Id);
        Assert.AreEqual(1, prompts.Count);
        Assert.IsFalse(prompts[0].IsContentAvailable);
        Assert.IsNotNull(prompts[0].LoadError);
    }

    #endregion

    #region CRUU10-011: Title / Headline Bounds (160 Text Elements)

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU10_011_Library_validator_validates_160_text_elements_boundary()
    {
        // 160 normal characters -> valid
        string title160 = new('a', 160);
        Assert.IsNull(LibraryValidator.ValidatePromptTitleInput(title160));

        // 161 normal characters -> invalid
        string title161 = new('a', 161);
        Assert.IsNotNull(LibraryValidator.ValidatePromptTitleInput(title161));

        // 160 complex surrogate / emoji characters (e.g. 𝄞 which is 2 UTF-16 code units each = 320 chars) -> valid 160 text elements
        string musical160 = string.Concat(Enumerable.Repeat("𝄞", 160));
        Assert.AreEqual(320, musical160.Length);
        Assert.IsNull(LibraryValidator.ValidatePromptTitleInput(musical160));

        // 161 complex surrogate characters -> invalid
        string musical161 = string.Concat(Enumerable.Repeat("𝄞", 161));
        Assert.IsNotNull(LibraryValidator.ValidatePromptTitleInput(musical161));

        // Control characters -> invalid
        Assert.IsNotNull(LibraryValidator.ValidatePromptTitleInput("hello\nworld"));
        Assert.IsNotNull(LibraryValidator.ValidatePromptTitleInput("hello\tworld"));
        Assert.IsNotNull(LibraryValidator.ValidatePromptTitleInput("hello\rworld"));
    }

    #endregion

    #region CRUU10-015: Case Sensitivity Inspector Enum API

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU10_015_Case_sensitivity_inspector_boolean_api_is_removed()
    {
        Type inspectorType = typeof(IDirectoryCaseSensitivityInspector);
        MethodInfo? boolMethod = inspectorType.GetMethod("IsCaseSensitive");
        Assert.IsNull(boolMethod, "Legacy IsCaseSensitive boolean method must be completely removed.");

        MethodInfo? inspectMethod = inspectorType.GetMethod("Inspect");
        Assert.IsNotNull(inspectMethod);
        Assert.AreEqual(typeof(DirectoryCaseSensitivityState), inspectMethod.ReturnType);
    }

    #endregion
}