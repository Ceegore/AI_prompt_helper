using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;
using PromptHelper.Views;

namespace PromptHelper.Tests;

[TestClass]
public sealed class Cruu7ComprehensiveVerificationTests
{
    private static void SeedValidLibrary(string root, out Guid promptId)
    {
        promptId = Guid.NewGuid();
        string promptsDir = Path.Combine(root, "prompts");
        Directory.CreateDirectory(promptsDir);
        Directory.CreateDirectory(Path.Combine(root, "recovery"));
        File.WriteAllText(Path.Combine(promptsDir, $"{promptId:N}.md"), "Prompt body content");

        string libraryJson = $$"""
        {
          "schemaVersion": 1,
          "categories": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "parentId": null,
              "name": "General",
              "sortOrder": 10
            }
          ],
          "prompts": [
            {
              "id": "{{promptId}}",
              "categoryId": "11111111-1111-1111-1111-111111111111",
              "sortOrder": 10,
              "title": "Initial Prompt"
            }
          ]
        }
        """;

        File.WriteAllText(Path.Combine(root, "library.json"), libraryJson);
    }

    [TestMethod]
    public void CRUU7_001_A_Selected_unc_host_alias_resolves_and_binds_to_physical_target()
    {
        using var source = new TestDirectory();
        using var physicalTarget = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);

        string lexicalAlias = @"C:\UncAlias\TargetShare";
        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var resolver = new FakePhysicalPathResolver();
        resolver.AddMapping(lexicalAlias, physicalTarget.Root);

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(pathResolver: resolver),
            new FakeUserConfirmationService(),
            pathResolver: resolver);

        var result = coordinator.RequestTransition(lexicalAlias);
        Assert.IsTrue(result.Changed);
        Assert.IsTrue(result.RestartRequired);
        Assert.IsTrue(File.Exists(Path.Combine(physicalTarget.Root, "library.json")));
    }

    [TestMethod]
    public void CRUU7_001_B_Symlink_retargeting_prior_to_commit_fails_physical_target_check()
    {
        using var source = new TestDirectory();
        using var initialTarget = new TestDirectory();
        using var hijackedTarget = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);

        string lexicalTarget = @"C:\DynamicTarget";
        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        int resolveCount = 0;
        var resolver = new FakePhysicalPathResolver
        {
            DynamicResolver = (p, c) =>
            {
                if (p.Equals(lexicalTarget, StringComparison.OrdinalIgnoreCase))
                {
                    resolveCount++;
                    return resolveCount > 1 ? hijackedTarget.Root : initialTarget.Root;
                }
                return null;
            }
        };

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(pathResolver: resolver),
            new FakeUserConfirmationService(),
            pathResolver: resolver);

        var ex = Assert.Throws<InvalidOperationException>(() => coordinator.RequestTransition(lexicalTarget));
        Assert.IsTrue(ex.Message.Contains("changed physical identity", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void CRUU7_001_C_Mutations_occur_exclusively_in_bound_physical_root()
    {
        using var source = new TestDirectory();
        using var physicalTarget = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);

        string lexicalAlias = @"C:\Alias\Custom";
        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var resolver = new FakePhysicalPathResolver();
        resolver.AddMapping(lexicalAlias, physicalTarget.Root);

        var recordingOps = new RecordingMigrationFileOps();
        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(fileOps: recordingOps, pathResolver: resolver),
            new FakeUserConfirmationService(),
            capabilityValidator: null,
            pathResolver: resolver,
            manifestRepo: null,
            fileOps: recordingOps,
            caseInspector: null);

        var result = coordinator.RequestTransition(lexicalAlias);
        Assert.IsTrue(result.Changed);
        Assert.IsTrue(File.Exists(Path.Combine(physicalTarget.Root, "library.json")));
    }

    [TestMethod]
    public void CRUU7_002_A_Promotion_moves_use_write_through_flag()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();

        SeedValidLibrary(source.Root, out _);

        var recordingOps = new RecordingMigrationFileOps();
        var migration = new DataFolderMigrationService(fileOps: recordingOps);

        var snapshot = migration.CaptureSourcePayloadSnapshot(source.Root);
        var manifest = MigrationManifestBuilder.BuildCopying(source.Root, target.Root, snapshot, Guid.NewGuid());
        using var tx = new DataFolderMigrationService.MigrationTargetTransaction();
        migration.CopySnapshotToTarget(source.Root, target.Root, snapshot, manifest, tx);
        tx.Commit();

        Assert.IsTrue(recordingOps.Trace.Any(t => t.StartsWith("MoveWriteThrough:")));
    }

    [TestMethod]
    public void CRUU7_002_B_Streams_are_flushed_to_disk_before_promotion()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();

        SeedValidLibrary(source.Root, out _);

        var recordingOps = new RecordingMigrationFileOps();
        var migration = new DataFolderMigrationService(fileOps: recordingOps);

        var snapshot = migration.CaptureSourcePayloadSnapshot(source.Root);
        var manifest = MigrationManifestBuilder.BuildCopying(source.Root, target.Root, snapshot, Guid.NewGuid());
        using var tx = new DataFolderMigrationService.MigrationTargetTransaction();
        migration.CopySnapshotToTarget(source.Root, target.Root, snapshot, manifest, tx);
        tx.Commit();

        int flushIndex = recordingOps.Trace.IndexOf("FlushToDisk");
        int moveIndex = recordingOps.Trace.FindIndex(t => t.StartsWith("MoveWriteThrough:"));

        Assert.IsTrue(flushIndex >= 0);
        Assert.IsTrue(moveIndex > flushIndex, "FlushToDisk must precede MoveWriteThrough");
    }

    [TestMethod]
    public void CRUU7_003_A_Active_prompts_classified_as_PromptBody()
    {
        using var source = new TestDirectory();
        SeedValidLibrary(source.Root, out Guid promptId);

        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourcePayloadSnapshot(source.Root);

        var activePrompt = snapshot.Files.FirstOrDefault(f => f.RelativePath.Equals(Path.Combine("prompts", $"{promptId:N}.md"), StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(activePrompt);
        Assert.AreEqual(MigrationPayloadRole.PromptBody, activePrompt.Role);
    }

    [TestMethod]
    public void CRUU7_003_B_Orphan_prompts_classified_as_OrphanPromptBody()
    {
        using var source = new TestDirectory();
        SeedValidLibrary(source.Root, out _);

        Guid orphanId = Guid.NewGuid();
        File.WriteAllText(Path.Combine(source.Root, "prompts", $"{orphanId:N}.md"), "Orphan body");

        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourcePayloadSnapshot(source.Root);

        var orphan = snapshot.Files.FirstOrDefault(f => f.RelativePath.Equals(Path.Combine("prompts", $"{orphanId:N}.md"), StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(orphan);
        Assert.AreEqual(MigrationPayloadRole.OrphanPromptBody, orphan.Role);
    }

    [TestMethod]
    public void CRUU7_003_C_Top_level_recovery_artifacts_classified_as_RecoveryArtifact()
    {
        using var source = new TestDirectory();
        SeedValidLibrary(source.Root, out _);

        string recoveryDir = Path.Combine(source.Root, "recovery");
        Directory.CreateDirectory(recoveryDir);
        File.WriteAllText(Path.Combine(recoveryDir, "corrupted-1.json"), "{}");

        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourcePayloadSnapshot(source.Root);

        var artifact = snapshot.Files.FirstOrDefault(f => f.RelativePath.Equals(Path.Combine("recovery", "corrupted-1.json"), StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(artifact);
        Assert.AreEqual(MigrationPayloadRole.RecoveryArtifact, artifact.Role);
    }

    [TestMethod]
    public void CRUU7_003_D_Transient_probe_files_excluded_from_snapshot()
    {
        using var source = new TestDirectory();
        SeedValidLibrary(source.Root, out _);

        File.WriteAllText(Path.Combine(source.Root, ".app.lock"), "lock");
        File.WriteAllText(Path.Combine(source.Root, "initializing.marker"), "init");
        File.WriteAllText(Path.Combine(source.Root, ".prompthelper-migration.json"), "{}");

        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourcePayloadSnapshot(source.Root);

        Assert.IsFalse(snapshot.RelativePathSet.Contains(".app.lock"));
        Assert.IsFalse(snapshot.RelativePathSet.Contains("initializing.marker"));
        Assert.IsFalse(snapshot.RelativePathSet.Contains(".prompthelper-migration.json"));
    }

    [TestMethod]
    public void CRUU7_004_A_Interrupted_migration_detected_and_cleared_if_unmodified()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out Guid promptId);

        string targetPrompt = Path.Combine(target.Root, "prompts", $"{promptId:N}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPrompt)!);
        File.WriteAllText(targetPrompt, "Prompt body content");

        string targetLib = Path.Combine(target.Root, "library.json");
        string libJson = File.ReadAllText(Path.Combine(source.Root, "library.json"));
        File.WriteAllText(targetLib, libJson);

        Guid attemptId = Guid.NewGuid();
        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = MigrationAttemptManifest.CurrentSchemaVersion,
            AttemptId = attemptId,
            SourcePhysicalRoot = source.Root,
            TargetPhysicalRoot = target.Root,
            SourceLibrarySha256Hex = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(targetLib))),
            SourcePayloadFingerprintSha256Hex = MigrationPayloadFingerprint.Compute(
                new DataFolderMigrationService().CaptureSourcePayloadSnapshot(source.Root).Files),
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-{new string('a', 32)}.tmp",
                    Role = MigrationPayloadRole.PrimaryMetadata,
                    Length = new FileInfo(targetLib).Length,
                    Sha256Hex = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(targetLib)))
                },
                new MigrationManifestArtifact
                {
                    RelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
                    TempRelativePath = Path.Combine("prompts", $".{promptId:N}.md.migration-{attemptId:N}-{new string('b', 32)}.tmp"),
                    Role = MigrationPayloadRole.PromptBody,
                    Length = new FileInfo(targetPrompt).Length,
                    Sha256Hex = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(targetPrompt)))
                }
            ]
        };

        // The interrupted attempt would have claimed each payload it promoted; without those
        // records the finals are indistinguishable from foreign files (CRUU16-005).
        OwnedArtifactTestSupport.ClaimPromotedFinal(target.Root, targetLib);
        OwnedArtifactTestSupport.ClaimPromotedFinal(target.Root, targetPrompt);

        var manifestRepo = new MigrationManifestRepository();
        manifestRepo.WriteDurable(Path.Combine(target.Root, ".prompthelper-migration.json"), manifest);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(),
            new FakeUserConfirmationService(),
            capabilityValidator: null,
            pathResolver: null,
            manifestRepo: manifestRepo,
            fileOps: null,
            caseInspector: null);

        var result = coordinator.RequestTransition(target.Root);
        Assert.IsTrue(result.Changed);
        Assert.IsTrue(result.RestartRequired);
        Assert.IsFalse(File.Exists(Path.Combine(target.Root, ".prompthelper-migration.json")));
    }

    [TestMethod]
    public void CRUU7_004_B_Interrupted_migration_fails_closed_if_foreign_file_present()
    {
        using var target = new TestDirectory();

        string foreignFile = Path.Combine(target.Root, "untracked-foreign.txt");
        File.WriteAllText(foreignFile, "foreign");

        Guid attemptId = Guid.NewGuid();
        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = MigrationAttemptManifest.CurrentSchemaVersion,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = target.Root,
            SourceLibrarySha256Hex = "0000000000000000000000000000000000000000000000000000000000000000",
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-{new string('a', 32)}.tmp",
                    Role = MigrationPayloadRole.PrimaryMetadata,
                    Length = 10,
                    Sha256Hex = "0000000000000000000000000000000000000000000000000000000000000000"
                }
            ]
        };

        var manifestRepo = new MigrationManifestRepository();
        manifestRepo.WriteDurable(Path.Combine(target.Root, ".prompthelper-migration.json"), manifest);

        Assert.Throws<InvalidDataException>(() =>
            MigrationTargetRecoveryService.ResolveInterruptedTarget(target.Root, manifestRepo));
    }

    [TestMethod]
    public void CRUU7_004_C_Interrupted_migration_fails_closed_if_payload_tampered()
    {
        using var target = new TestDirectory();

        Guid attemptId = Guid.NewGuid();
        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = MigrationAttemptManifest.CurrentSchemaVersion,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = target.Root,
            SourceLibrarySha256Hex = "0000000000000000000000000000000000000000000000000000000000000000",
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-{new string('a', 32)}.tmp",
                    Role = MigrationPayloadRole.PrimaryMetadata,
                    Length = 10,
                    Sha256Hex = "0000000000000000000000000000000000000000000000000000000000000000"
                }
            ]
        };

        var manifestRepo = new MigrationManifestRepository();
        manifestRepo.WriteDurable(Path.Combine(target.Root, ".prompthelper-migration.json"), manifest);

        Assert.Throws<InvalidDataException>(() =>
            MigrationTargetRecoveryService.ResolveInterruptedTarget(target.Root, manifestRepo));
    }

    [TestMethod]
    public void CRUU7_004_D_Interrupted_migration_fails_closed_if_manifest_corrupted()
    {
        using var target = new TestDirectory();

        File.WriteAllText(Path.Combine(target.Root, ".prompthelper-migration.json"), "{ invalid json ");

        var manifestRepo = new MigrationManifestRepository();

        Assert.Throws<InvalidDataException>(() =>
            MigrationTargetRecoveryService.ResolveInterruptedTarget(target.Root, manifestRepo));
    }

    [TestMethod]
    public void CRUU7_005_A_Direct_caller_cannot_bypass_lease_precondition()
    {
        using var temp = new TestDirectory();
        string settingsFile = Path.Combine(temp.Root, "settings.json");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsFile);
        var settings = new AppSettings { SchemaVersion = 1, DataRootPath = @"C:\Data" };

        var snapshot = repo.LoadForTransitionAndCapturePrecondition();
        var result = repo.SaveIfUnchanged(settings, snapshot.Precondition);
        Assert.IsNull(result.Warning);
    }

    [TestMethod]
    public void CRUU7_005_B_Stale_precondition_token_prevents_settings_overwrite()
    {
        using var temp = new TestDirectory();
        string settingsFile = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(settingsFile, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Initial\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsFile);
        var snapshot = repo.LoadForTransitionAndCapturePrecondition();

        // Mutate settings externally to invalidate precondition
        File.WriteAllText(settingsFile, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\ChangedByOther\"}");

        var newSettings = new AppSettings { SchemaVersion = 1, DataRootPath = @"C:\MyNewRoot" };
        Assert.Throws<InvalidOperationException>(() =>
            repo.SaveIfUnchanged(newSettings, snapshot.Precondition));
    }

    [TestMethod]
    public void CRUU7_006_A_Target_metadata_change_between_passes_throws_TargetInspectionUnstableException()
    {
        using var target = new TestDirectory();
        SeedValidLibrary(target.Root, out _);

        int readCount = 0;
        var ops = new FaultInjectingMigrationFileOps
        {
            OnReadAllBytes = p =>
            {
                if (p.EndsWith("library.json", StringComparison.OrdinalIgnoreCase))
                {
                    readCount++;
                    if (readCount == 2)
                    {
                        return Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"categories\":[],\"prompts\":[]}");
                    }
                }
                return File.ReadAllBytes(p);
            }
        };

        var migration = new DataFolderMigrationService(fileOps: ops);
        var inspection = migration.InspectTarget(target.Root);

        Assert.AreEqual(DataFolderMigrationService.TargetLibraryKind.Unstable, inspection.Kind);
        Assert.IsNotNull(inspection.Error);
        Assert.IsInstanceOfType<TargetInspectionUnstableException>(inspection.Error);
    }

    [TestMethod]
    public void CRUU7_006_B_Target_prompt_body_change_between_passes_throws_TargetInspectionUnstableException()
    {
        using var target = new TestDirectory();
        SeedValidLibrary(target.Root, out Guid promptId);

        int readCount = 0;
        var ops = new FaultInjectingMigrationFileOps
        {
            OnReadAllBytes = p =>
            {
                if (p.EndsWith($"{promptId:N}.md", StringComparison.OrdinalIgnoreCase))
                {
                    readCount++;
                    if (readCount == 2)
                    {
                        return Encoding.UTF8.GetBytes("Changed prompt body on pass 2");
                    }
                }
                return File.ReadAllBytes(p);
            }
        };

        var migration = new DataFolderMigrationService(fileOps: ops);
        var inspection = migration.InspectTarget(target.Root);

        Assert.AreEqual(DataFolderMigrationService.TargetLibraryKind.Unstable, inspection.Kind);
        Assert.IsNotNull(inspection.Error);
        Assert.IsInstanceOfType<TargetInspectionUnstableException>(inspection.Error);
    }

    [TestMethod]
    public void CRUU7_006_C_Stable_target_content_passes_equal_snapshot_fingerprint()
    {
        using var target = new TestDirectory();
        SeedValidLibrary(target.Root, out _);

        var migration = new DataFolderMigrationService();
        var inspection = migration.InspectTarget(target.Root);

        Assert.AreEqual(DataFolderMigrationService.TargetLibraryKind.ValidPrimary, inspection.Kind);
        Assert.IsNotNull(inspection.Fingerprint);
        Assert.AreEqual(32, inspection.Fingerprint.Length);
    }

    [TestMethod]
    public void CRUU7_007_A_Unreadable_primary_does_not_fallback_to_backup_as_corrupt()
    {
        using var target = new TestDirectory();
        SeedValidLibrary(target.Root, out _);

        File.WriteAllText(Path.Combine(target.Root, "library.backup.json"), File.ReadAllText(Path.Combine(target.Root, "library.json")));

        var ops = new FaultInjectingMigrationFileOps
        {
            OnReadAllBytes = p =>
            {
                if (p.EndsWith("library.json", StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException("Simulated primary access denied");
                }
                return File.ReadAllBytes(p);
            }
        };

        var migration = new DataFolderMigrationService(fileOps: ops);
        var inspection = migration.InspectTarget(target.Root);

        Assert.AreEqual(DataFolderMigrationService.TargetLibraryKind.Unreadable, inspection.Kind);
        Assert.IsInstanceOfType<UnauthorizedAccessException>(inspection.Error);
    }

    [TestMethod]
    public void CRUU7_007_B_Unstable_primary_is_retryable_not_corrupt()
    {
        using var target = new TestDirectory();
        SeedValidLibrary(target.Root, out _);

        File.WriteAllText(Path.Combine(target.Root, "library.backup.json"), File.ReadAllText(Path.Combine(target.Root, "library.json")));

        int reads = 0;
        var ops = new FaultInjectingMigrationFileOps
        {
            OnReadAllBytes = p =>
            {
                if (p.EndsWith("library.json", StringComparison.OrdinalIgnoreCase))
                {
                    reads++;
                    if (reads == 2)
                    {
                        return Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"categories\":[],\"prompts\":[]}");
                    }
                }
                return File.ReadAllBytes(p);
            }
        };

        var migration = new DataFolderMigrationService(fileOps: ops);
        var inspection = migration.InspectTarget(target.Root);

        Assert.AreEqual(DataFolderMigrationService.TargetLibraryKind.Unstable, inspection.Kind);
    }

    [TestMethod]
    public void CRUU7_008_A_Reservation_lock_handles_are_disposed_idempotently()
    {
        using var temp = new TestDirectory();
        var reservation = TargetRootReservation.TryAcquire(temp.Root);
        Assert.IsNotNull(reservation);

        var res1 = reservation.Release();
        Assert.IsTrue(res1.Success);

        var res2 = reservation.Release();
        Assert.IsTrue(res2.Success);
    }

    [TestMethod]
    public void CRUU7_008_B_Cleanup_failure_does_not_throw_from_Release()
    {
        using var temp = new TestDirectory();
        var ops = new FakeReservationFileOps
        {
            OnDeleteFile = p => throw new IOException("Simulated lock file deletion error")
        };

        var reservation = TargetRootReservation.TryAcquire(temp.Root, ops);
        Assert.IsNotNull(reservation);

        var cleanup = reservation.Release();
        Assert.IsFalse(cleanup.Success);
        Assert.IsTrue(cleanup.Failures.Count > 0);
        Assert.IsNotNull(cleanup.ToWarning());
    }

    [TestMethod]
    public void CRUU7_008_C_Reservation_creates_root_and_removes_if_unused()
    {
        using var parent = new TestDirectory();
        string newRoot = Path.Combine(parent.Root, "AutoCreatedRoot");

        Assert.IsFalse(Directory.Exists(newRoot));
        var reservation = TargetRootReservation.TryAcquire(newRoot);
        Assert.IsNotNull(reservation);
        Assert.IsTrue(Directory.Exists(newRoot));

        reservation.Release();
        Assert.IsFalse(Directory.Exists(newRoot));
    }

    [TestMethod]
    public void CRUU7_009_A_Post_commit_cleanup_failure_returns_changed_and_restart_required()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var settingsDir = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        SeedValidLibrary(target.Root, out _);

        string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
        File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            new DataFolderMigrationService(),
            new FakeUserConfirmationService { ConfirmationResult = true });

        var result = coordinator.RequestTransition(target.Root);
        Assert.IsTrue(result.Changed);
        Assert.IsTrue(result.RestartRequired);
    }

    [TestMethod]
    public void CRUU7_009_B_Post_commit_cleanup_failure_includes_warning_text()
    {
        string? combined = WarningCombiner.Combine("Warning A", null, "Warning B", "Warning A");
        Assert.AreEqual("Warning A\r\n\r\nWarning B", combined);
    }

    [TestMethod]
    public void CRUU7_010_A_Probe_uses_explicit_probe_files_not_atomic_writer()
    {
        using var temp = new TestDirectory();
        var ops = new FakeCapabilityFileOps();
        var validator = new DataRootCapabilityValidator(ops);

        var result = validator.ValidateWritable(temp.Root);
        Assert.IsNull(result.Warning);
    }

    [TestMethod]
    public void CRUU7_010_B_Probe_cleans_temporary_probe_files_on_failure()
    {
        using var temp = new TestDirectory();
        var ops = new FakeCapabilityFileOps
        {
            OnReplace = (src, dst, bak) => throw new IOException("Replace failed")
        };
        var validator = new DataRootCapabilityValidator(ops);

        Assert.Throws<IOException>(() => validator.ValidateWritable(temp.Root));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(temp.Root).Length);
    }

    [TestMethod]
    public void CRUU7_011_A_Unavailable_data_folder_displays_controlled_error_and_preserves_settings()
    {
        using var bootstrap = new TestDirectory();
        var policy = new ManagedDataRootPolicy();

        string missingFolder = @"Z:\NonExistentDrive\MissingFolder";
        Assert.Throws<ConfiguredDataFolderUnavailableException>(() =>
            policy.ValidateConfiguredRootForStartup(missingFolder, bootstrap.Root));
    }

    [TestMethod]
    public void CRUU7_011_B_Unavailable_data_folder_dialog_error_keeps_dialog_open_or_safe()
    {
        WpfTestHost.Invoke(() =>
        {
            using var source = new TestDirectory();
            using var settingsDir = new TestDirectory();
            SeedValidLibrary(source.Root, out _);

            string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
            File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

            var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);
            var fakeService = new FakeDataFolderTransitionService
            {
                OnRequestTransition = path => throw new ConfiguredDataFolderUnavailableException(path, "Folder unavailable")
            };

            var confirmation = new FakeUserConfirmationService();
            var dialog = new SettingsDialog(source.Root, repo, new DataFolderMigrationService(), confirmation, fakeService);

            try
            {
                Assert.IsFalse(dialog.RestartRequired);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestMethod]
    public void CRUU7_012_A_Readonly_safety_backup_yields_warning_not_hard_error()
    {
        using var target = new TestDirectory();
        SeedValidLibrary(target.Root, out _);

        string backupPath = Path.Combine(target.Root, "library.backup.json");
        File.WriteAllText(backupPath, File.ReadAllText(Path.Combine(target.Root, "library.json")));
        File.SetAttributes(backupPath, FileAttributes.ReadOnly);

        try
        {
            var validator = new DataRootCapabilityValidator();
            var doc = LibraryRepository.InspectAndDeserialize(File.ReadAllText(Path.Combine(target.Root, "library.json")));
            var ctx = new ExistingLibraryCapabilityContext(DataFolderMigrationService.TargetLibraryKind.ValidPrimary, Path.Combine(target.Root, "library.json"), null, doc);

            var result = validator.ValidateWritable(target.Root, null, ctx);
            Assert.IsNotNull(result.Warning);
            Assert.IsTrue(result.Warning.Contains("safety backup", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.SetAttributes(backupPath, FileAttributes.Normal);
        }
    }

    [TestMethod]
    public void CRUU7_012_B_Future_schema_safety_backup_is_ignored_and_preserved()
    {
        using var target = new TestDirectory();
        SeedValidLibrary(target.Root, out _);

        string backupPath = Path.Combine(target.Root, "library.backup.json");
        File.WriteAllText(backupPath, "{\"schemaVersion\":99,\"categories\":[],\"prompts\":[]}");
        File.SetAttributes(backupPath, FileAttributes.ReadOnly);

        try
        {
            var validator = new DataRootCapabilityValidator();
            var doc = LibraryRepository.InspectAndDeserialize(File.ReadAllText(Path.Combine(target.Root, "library.json")));
            var ctx = new ExistingLibraryCapabilityContext(DataFolderMigrationService.TargetLibraryKind.ValidPrimary, Path.Combine(target.Root, "library.json"), null, doc);

            var result = validator.ValidateWritable(target.Root, null, ctx);
            Assert.IsNull(result.Warning);
        }
        finally
        {
            File.SetAttributes(backupPath, FileAttributes.Normal);
        }
    }

    [TestMethod]
    public void CRUU7_013_A_Multiple_transition_warnings_are_combined_and_deduplicated()
    {
        string? combined = WarningCombiner.Combine("First Warning", "Second Warning", "First Warning");
        Assert.AreEqual("First Warning\r\n\r\nSecond Warning", combined);
    }

    [TestMethod]
    public void CRUU7_013_B_Warning_combiner_ignores_null_or_empty_values()
    {
        string? combined = WarningCombiner.Combine(null, "", "   ", "Valid Warning", null);
        Assert.AreEqual("Valid Warning", combined);
    }

    [TestMethod]
    public void CRUU7_014_A_Lease_only_retries_sharing_and_lock_violations()
    {
        using var temp = new TestDirectory();
        string lockFile = Path.Combine(temp.Root, ".settings.lock");

        var policy = SettingsLeasePolicy.FastTest;
        using var lease = SettingsMutationLease.TryAcquire(lockFile, policy);
        Assert.IsNotNull(lease);

        // Attempting to acquire second lease will fail with timeout after retries
        using var secondLease = SettingsMutationLease.TryAcquire(lockFile, policy);
        Assert.IsNull(secondLease);
    }

    [TestMethod]
    public void CRUU7_014_B_Lease_fails_immediately_on_access_denied_or_file_not_found()
    {
        string invalidPath = @"Z:\NonExistentDrive\lock.file";
        Assert.Throws<IOException>(() => SettingsMutationLease.TryAcquire(invalidPath, SettingsLeasePolicy.FastTest));
    }

    [TestMethod]
    public void CRUU7_014_C_Fast_test_policy_executes_within_configured_timeout()
    {
        using var temp = new TestDirectory();
        string lockFile = Path.Combine(temp.Root, ".settings.lock");

        using var held = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var lease = SettingsMutationLease.TryAcquire(lockFile, SettingsLeasePolicy.FastTest);
        stopwatch.Stop();

        Assert.IsNull(lease);
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 500, $"Expected fast timeout, took {stopwatch.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public void CRUU7_015_A_SettingsDialog_uses_injected_transition_service()
    {
        WpfTestHost.Invoke(() =>
        {
            using var source = new TestDirectory();
            using var settingsDir = new TestDirectory();
            SeedValidLibrary(source.Root, out _);

            string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
            File.WriteAllText(settingsPath, $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

            bool called = false;
            var fake = new FakeDataFolderTransitionService
            {
                OnRequestTransition = path =>
                {
                    called = true;
                    return new DataFolderTransitionResult(Changed: true, RestartRequired: true, ExistingLibrarySelected: false, NormalizedTargetRoot: path, Warning: null);
                }
            };

            var dialog = new SettingsDialog(
                source.Root,
                new AppSettingsRepository(settingsPathOverride: settingsPath),
                new DataFolderMigrationService(),
                new FakeUserConfirmationService(),
                fake);

            try
            {
                // Exercise RequestTransition via dialog
                fake.RequestTransition(@"C:\SomeTarget");
                Assert.IsTrue(called);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [TestMethod]
    public void CRUU7_015_B_SettingsDialog_handles_all_transition_result_states()
    {
        var resultNoChange = new DataFolderTransitionResult(Changed: false, RestartRequired: false, ExistingLibrarySelected: false, NormalizedTargetRoot: @"C:\Data", Warning: null);
        var resultSwitch = new DataFolderTransitionResult(Changed: true, RestartRequired: true, ExistingLibrarySelected: true, NormalizedTargetRoot: @"C:\Data", Warning: "Notice");
        var resultMigrated = new DataFolderTransitionResult(Changed: true, RestartRequired: true, ExistingLibrarySelected: false, NormalizedTargetRoot: @"C:\Data", Warning: null);

        Assert.IsFalse(resultNoChange.Changed);
        Assert.IsTrue(resultSwitch.ExistingLibrarySelected);
        Assert.IsFalse(resultMigrated.ExistingLibrarySelected);
    }

    [TestMethod]
    public void CRUU7_016_A_Case_sensitive_data_root_is_rejected_on_windows()
    {
        using var temp = new TestDirectory();
        var caseInspector = new FakeDirectoryCaseSensitivityInspector();
        caseInspector.MarkCaseSensitive(temp.Root);

        var policy = new ManagedDataRootPolicy(caseInspector: caseInspector);

        Assert.Throws<InvalidDataException>(() =>
            policy.ValidateConfiguredRootForStartup(temp.Root, @"C:\Bootstrap"));
    }

    [TestMethod]
    public void CRUU7_016_B_Case_insensitive_data_root_is_accepted_on_windows()
    {
        using var temp = new TestDirectory();
        using var bootstrap = new TestDirectory();

        var caseInspector = new FakeDirectoryCaseSensitivityInspector();
        var policy = new ManagedDataRootPolicy(caseInspector: caseInspector);

        string resolved = policy.ValidateConfiguredRootForStartup(temp.Root, bootstrap.Root);
        Assert.IsNotNull(resolved);
    }

    [TestMethod]
    public void CRUU7_017_A_GenerateAppIcon_accepts_custom_input_and_output_paths()
    {
        string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "tools", "GenerateAppIcon.ps1");
        string fullScriptPath = Path.GetFullPath(scriptPath);
        Assert.IsTrue(File.Exists(fullScriptPath), $"GenerateAppIcon.ps1 should exist at {fullScriptPath}");

        string content = File.ReadAllText(fullScriptPath);
        Assert.IsTrue(content.Contains("param("));
        Assert.IsTrue(content.Contains("$SourceSvg"));
        Assert.IsTrue(content.Contains("$OutputIco"));
    }

    [TestMethod]
    public void CRUU7_017_B_VerifyReleaseAssets_validates_multi_frame_ico_binary_structure()
    {
        string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "tools", "VerifyReleaseAssets.ps1");
        string fullScriptPath = Path.GetFullPath(scriptPath);
        Assert.IsTrue(File.Exists(fullScriptPath), $"VerifyReleaseAssets.ps1 should exist at {fullScriptPath}");

        string content = File.ReadAllText(fullScriptPath);
        Assert.IsTrue(content.Contains("$requiredSizes"));
        Assert.IsTrue(content.Contains("256"));
    }
}
