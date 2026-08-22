using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;
using PromptHelper.ViewModels;

namespace PromptHelper.Tests;

[TestClass]
public sealed class Cruu9ComprehensiveVerificationTests
{
    private static string GetRepositoryRoot()
    {
        string? current = AppDomain.CurrentDomain.BaseDirectory;
        while (current != null)
        {
            if (File.Exists(Path.Combine(current, "PromptHelper.slnx")) || Directory.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }
            current = Path.GetDirectoryName(current);
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static void SeedValidLibrary(string root, out LibraryDocument doc)
    {
        Directory.CreateDirectory(root);
        string promptsDir = Path.Combine(root, "prompts");
        Directory.CreateDirectory(promptsDir);
        Directory.CreateDirectory(Path.Combine(root, "recovery"));

        Guid promptId = Guid.NewGuid();
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

    private static void CreateJunction(string linkPath, string targetPath)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"");
        ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 30_000);
        if (!run.Exited || run.ExitCode != 0)
        {
            throw new InvalidOperationException($"mklink failed: {run.StandardError}");
        }
    }

    // ==========================================
    // CRUU9-001: Managed Physical Tree Containment
    // ==========================================

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU9_001_Empty_prompts_junction_outside_target_is_rejected()
    {
        using var target = new TestDirectory();
        using var outside = new TestDirectory();

        string promptsLink = Path.Combine(target.Root, "prompts");
        CreateJunction(promptsLink, outside.Root);

        var validator = new ManagedTreeTopologyValidator();
        Assert.Throws<InvalidDataException>(() => validator.ValidateManagedTree(target.Root));
    }

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU9_001_Empty_recovery_junction_outside_target_is_rejected()
    {
        using var target = new TestDirectory();
        using var outside = new TestDirectory();

        string recoveryLink = Path.Combine(target.Root, "recovery");
        CreateJunction(recoveryLink, outside.Root);

        var validator = new ManagedTreeTopologyValidator();
        Assert.Throws<InvalidDataException>(() => validator.ValidateManagedTree(target.Root));
    }

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU9_001_Migration_never_writes_through_prompts_junction()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var outside = new TestDirectory();

        SeedValidLibrary(source.Root, out _);

        string promptsLink = Path.Combine(target.Root, "prompts");
        CreateJunction(promptsLink, outside.Root);

        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourcePayloadSnapshot(source.Root);
        var manifest = MigrationManifestBuilder.BuildCopying(source.Root, target.Root, snapshot, Guid.NewGuid());

        using var tx = new DataFolderMigrationService.MigrationTargetTransaction();
        Assert.Throws<InvalidDataException>(() =>
            migration.CopySnapshotToTarget(source.Root, target.Root, snapshot, manifest, tx));

        // Assert outside directory remains empty
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(outside.Root).Count());
    }

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU9_001_Retry_never_deletes_through_prompts_junction()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var outside = new TestDirectory();

        string outsideFile = Path.Combine(outside.Root, "important.txt");
        byte[] outsideBytes = Encoding.UTF8.GetBytes("Important external data");
        File.WriteAllBytes(outsideFile, outsideBytes);

        string promptsLink = Path.Combine(target.Root, "prompts");
        CreateJunction(promptsLink, outside.Root);

        SeedValidLibrary(source.Root, out _);
        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourcePayloadSnapshot(source.Root);
        var manifest = MigrationManifestBuilder.BuildCopying(source.Root, target.Root, snapshot, Guid.NewGuid());

        string markerPath = Path.Combine(target.Root, ".prompthelper-migration.json");
        var repo = new MigrationManifestRepository();
        repo.CreateInitialCopyingManifestDurable(markerPath, manifest);

        var recovery = new MigrationRecoveryService();
        var context = new MigrationRecoveryContext(target.Root, ExpectedSourcePhysicalRoot: source.Root);

        var result = recovery.RecoverForRetry(context);
        Assert.IsFalse(result.Success);

        // Outside file preserved byte-for-byte
        Assert.IsTrue(File.Exists(outsideFile));
        CollectionAssert.AreEqual(outsideBytes, File.ReadAllBytes(outsideFile));
    }

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU9_001_Retry_never_deletes_outside_bound_root()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        using var outside = new TestDirectory();

        string outsideFile = Path.Combine(outside.Root, "important.txt");
        byte[] outsideBytes = Encoding.UTF8.GetBytes("Protected external file");
        File.WriteAllBytes(outsideFile, outsideBytes);

        var deleter = new WindowsVerifiedArtifactDeleter();
        // Trying to delete outside file while bound to target.Root must fail closed
        Assert.Throws<InvalidDataException>(() =>
            deleter.VerifyAndDelete(target.Root, outsideFile, outsideBytes.Length, Convert.ToHexStringLower(SHA256.HashData(outsideBytes))));

        Assert.IsTrue(File.Exists(outsideFile));
    }

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU9_001_Committed_startup_rejects_managed_child_reparse()
    {
        using var target = new TestDirectory();
        using var outside = new TestDirectory();

        string promptsLink = Path.Combine(target.Root, "prompts");
        CreateJunction(promptsLink, outside.Root);

        var recovery = new MigrationRecoveryService();
        var context = new MigrationRecoveryContext(target.Root);

        var result = recovery.FinalizeCommittedStartup(context);
        Assert.IsFalse(result.Success);
        Assert.IsInstanceOfType<MigrationRecoveryException>(result.Error);
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_001_Normal_managed_directories_are_accepted()
    {
        using var temp = new TestDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Root, "prompts"));
        Directory.CreateDirectory(Path.Combine(temp.Root, "recovery"));

        var validator = new ManagedTreeTopologyValidator();
        validator.ValidateManagedTree(temp.Root);
    }

    // ==========================================
    // CRUU9-002: Strict File Authority
    // ==========================================

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_002_Unreadable_marker_is_not_treated_as_missing()
    {
        using var temp = new TestDirectory();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");
        File.WriteAllText(markerPath, "test");

        using var locked = new FileStream(markerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var repo = new MigrationManifestRepository();
        Assert.Throws<InvalidDataException>(() => repo.TryReadStrict(markerPath));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_002_Unreadable_final_is_not_treated_as_missing()
    {
        using var temp = new TestDirectory();
        string finalPath = Path.Combine(temp.Root, "library.json");
        File.WriteAllText(finalPath, "test");

        using var locked = new FileStream(finalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Throws<IOException>(() => StrictFileAuthority.GetPresence(finalPath));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_002_Unreadable_temp_is_not_treated_as_missing()
    {
        using var temp = new TestDirectory();
        string tempPath = Path.Combine(temp.Root, ".library.json.tmp");
        File.WriteAllText(tempPath, "temp data");

        using var locked = new FileStream(tempPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Throws<IOException>(() => StrictFileAuthority.GetPresence(tempPath));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_002_Verified_deleter_access_denied_is_not_noop()
    {
        using var temp = new TestDirectory();
        string file = Path.Combine(temp.Root, "test.txt");
        File.WriteAllBytes(file, "hello"u8.ToArray());

        using var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var deleter = new WindowsVerifiedArtifactDeleter();
        Assert.Throws<IOException>(() =>
            deleter.VerifyAndDelete(temp.Root, file, 5, "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824"));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_002_App_startup_does_not_skip_unreadable_marker()
    {
        using var temp = new TestDirectory();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");
        File.WriteAllText(markerPath, "test");

        using var locked = new FileStream(markerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var recovery = new MigrationRecoveryService();
        var context = new MigrationRecoveryContext(temp.Root);
        var result = recovery.FinalizeCommittedStartup(context);

        Assert.IsFalse(result.Success);
        Assert.IsInstanceOfType<MigrationRecoveryException>(result.Error);
    }

    // ==========================================
    // CRUU9-003: TempRelativePath Grammar
    // ==========================================

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_003_Temp_in_different_directory_rejected()
    {
        using var temp = new TestDirectory();
        Guid attemptId = Guid.NewGuid();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 3,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = temp.Root,
            SourceLibrarySha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "prompts/p1.md",
                    TempRelativePath = $".p1.md.migration-{attemptId:N}-{new string('a', 32)}.tmp", // Missing prompts/ directory!
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PromptBody
                },
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-{new string('a', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PrimaryMetadata
                }
            ]
        };

        var repo = new MigrationManifestRepository();
        Assert.Throws<InvalidDataException>(() => repo.CreateInitialCopyingManifestDurable(markerPath, manifest));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_003_Temp_without_attempt_id_rejected()
    {
        using var temp = new TestDirectory();
        Guid attemptId = Guid.NewGuid();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 3,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = temp.Root,
            SourceLibrarySha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = ".library.json.tmp", // No attemptId
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PrimaryMetadata
                }
            ]
        };

        var repo = new MigrationManifestRepository();
        Assert.Throws<InvalidDataException>(() => repo.CreateInitialCopyingManifestDurable(markerPath, manifest));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_003_Temp_with_other_attempt_id_rejected()
    {
        using var temp = new TestDirectory();
        Guid attemptId = Guid.NewGuid();
        Guid otherAttemptId = Guid.NewGuid();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 3,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = temp.Root,
            SourceLibrarySha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{otherAttemptId:N}-{new string('a', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PrimaryMetadata
                }
            ]
        };

        var repo = new MigrationManifestRepository();
        Assert.Throws<InvalidDataException>(() => repo.CreateInitialCopyingManifestDurable(markerPath, manifest));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_003_Temp_with_short_nonce_rejected()
    {
        using var temp = new TestDirectory();
        Guid attemptId = Guid.NewGuid();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 3,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = temp.Root,
            SourceLibrarySha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-12345.tmp", // short nonce
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PrimaryMetadata
                }
            ]
        };

        var repo = new MigrationManifestRepository();
        Assert.Throws<InvalidDataException>(() => repo.CreateInitialCopyingManifestDurable(markerPath, manifest));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_003_Temp_with_nonhex_nonce_rejected()
    {
        using var temp = new TestDirectory();
        Guid attemptId = Guid.NewGuid();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 3,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = temp.Root,
            SourceLibrarySha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz.tmp", // non-hex
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PrimaryMetadata
                }
            ]
        };

        var repo = new MigrationManifestRepository();
        Assert.Throws<InvalidDataException>(() => repo.CreateInitialCopyingManifestDurable(markerPath, manifest));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_003_Production_temp_grammar_accepted()
    {
        using var temp = new TestDirectory();
        Guid attemptId = Guid.NewGuid();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 3,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = temp.Root,
            SourceLibrarySha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-{new string('a', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PrimaryMetadata
                }
            ]
        };

        var repo = new MigrationManifestRepository();
        repo.CreateInitialCopyingManifestDurable(markerPath, manifest);

        var read = repo.TryReadStrict(markerPath);
        Assert.IsNotNull(read);
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_003_Arbitrary_prompt_cannot_be_declared_as_temp()
    {
        using var temp = new TestDirectory();
        Guid attemptId = Guid.NewGuid();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 3,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = temp.Root,
            SourceLibrarySha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "prompts/p1.md",
                    TempRelativePath = "prompts/user_file.md", // arbitrary name
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PromptBody
                },
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-{new string('a', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PrimaryMetadata
                }
            ]
        };

        var repo = new MigrationManifestRepository();
        Assert.Throws<InvalidDataException>(() => repo.CreateInitialCopyingManifestDurable(markerPath, manifest));
    }

    // ==========================================
    // CRUU9-004: Unified Collision Prevention
    // ==========================================

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_004_Temp_A_equal_Final_B_rejected()
    {
        using var temp = new TestDirectory();
        Guid attemptId = Guid.NewGuid();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 3,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = temp.Root,
            SourceLibrarySha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-{new string('a', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PrimaryMetadata
                },
                new MigrationManifestArtifact
                {
                    RelativePath = $".library.json.migration-{attemptId:N}-{new string('a', 32)}.tmp", // Final matches A's temp!
                    TempRelativePath = $".temp2.migration-{attemptId:N}-{new string('b', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.SafetyBackup
                }
            ]
        };

        var repo = new MigrationManifestRepository();
        Assert.Throws<InvalidDataException>(() => repo.CreateInitialCopyingManifestDurable(markerPath, manifest));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_004_Final_A_equal_Temp_B_rejected()
    {
        using var temp = new TestDirectory();
        Guid attemptId = Guid.NewGuid();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 3,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = temp.Root,
            SourceLibrarySha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-{new string('a', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PrimaryMetadata
                },
                new MigrationManifestArtifact
                {
                    RelativePath = "prompts/p1.md",
                    TempRelativePath = "library.json", // Temp matches A's final!
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PromptBody
                }
            ]
        };

        var repo = new MigrationManifestRepository();
        Assert.Throws<InvalidDataException>(() => repo.CreateInitialCopyingManifestDurable(markerPath, manifest));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_004_Two_temps_equal_rejected()
    {
        using var temp = new TestDirectory();
        Guid attemptId = Guid.NewGuid();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 3,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = temp.Root,
            SourceLibrarySha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-{new string('a', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PrimaryMetadata
                },
                new MigrationManifestArtifact
                {
                    RelativePath = "library.backup.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-{new string('a', 32)}.tmp", // duplicate temp!
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.SafetyBackup
                }
            ]
        };

        var repo = new MigrationManifestRepository();
        Assert.Throws<InvalidDataException>(() => repo.CreateInitialCopyingManifestDurable(markerPath, manifest));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_004_Two_finals_equal_rejected()
    {
        using var temp = new TestDirectory();
        Guid attemptId = Guid.NewGuid();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 3,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = temp.Root,
            SourceLibrarySha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-{new string('a', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PrimaryMetadata
                },
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json", // duplicate final!
                    TempRelativePath = $".library.json.migration-{attemptId:N}-{new string('b', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.SafetyBackup
                }
            ]
        };

        var repo = new MigrationManifestRepository();
        Assert.Throws<InvalidDataException>(() => repo.CreateInitialCopyingManifestDurable(markerPath, manifest));
    }

    // ==========================================
    // CRUU9-005: Source Identity in Recovery
    // ==========================================

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_005_Same_source_interrupted_retry_can_clean()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourcePayloadSnapshot(source.Root);
        var manifest = MigrationManifestBuilder.BuildCopying(source.Root, target.Root, snapshot, Guid.NewGuid());

        string markerPath = Path.Combine(target.Root, ".prompthelper-migration.json");
        var repo = new MigrationManifestRepository();
        repo.CreateInitialCopyingManifestDurable(markerPath, manifest);

        var recovery = new MigrationRecoveryService();
        var context = new MigrationRecoveryContext(target.Root, ExpectedSourcePhysicalRoot: source.Root);

        var result = recovery.RecoverForRetry(context);
        Assert.IsTrue(result.Success);
        Assert.IsFalse(File.Exists(markerPath));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_005_Different_source_interrupted_retry_fails_closed()
    {
        using var realSource = new TestDirectory();
        using var otherSource = new TestDirectory();
        using var target = new TestDirectory();

        SeedValidLibrary(realSource.Root, out _);
        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourcePayloadSnapshot(realSource.Root);
        var manifest = MigrationManifestBuilder.BuildCopying(otherSource.Root, target.Root, snapshot, Guid.NewGuid());

        string markerPath = Path.Combine(target.Root, ".prompthelper-migration.json");
        var repo = new MigrationManifestRepository();
        repo.CreateInitialCopyingManifestDurable(markerPath, manifest);

        var recovery = new MigrationRecoveryService();
        // Trying to recover with realSource when manifest specifies otherSource must fail
        var context = new MigrationRecoveryContext(target.Root, ExpectedSourcePhysicalRoot: realSource.Root);

        var result = recovery.RecoverForRetry(context);
        Assert.IsFalse(result.Success);
        Assert.IsInstanceOfType<MigrationRecoveryException>(result.Error);
        Assert.IsTrue(File.Exists(markerPath)); // Marker preserved
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_005_Different_source_files_preserved_byte_exact()
    {
        using var realSource = new TestDirectory();
        using var otherSource = new TestDirectory();
        using var target = new TestDirectory();

        SeedValidLibrary(realSource.Root, out _);
        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourcePayloadSnapshot(realSource.Root);
        var manifest = MigrationManifestBuilder.BuildCopying(otherSource.Root, target.Root, snapshot, Guid.NewGuid());

        string markerPath = Path.Combine(target.Root, ".prompthelper-migration.json");
        var repo = new MigrationManifestRepository();
        repo.CreateInitialCopyingManifestDurable(markerPath, manifest);

        string targetPayload = Path.Combine(target.Root, "library.json");
        byte[] payloadBytes = Encoding.UTF8.GetBytes("Untouchable payload from other source");
        File.WriteAllBytes(targetPayload, payloadBytes);

        var recovery = new MigrationRecoveryService();
        var context = new MigrationRecoveryContext(target.Root, ExpectedSourcePhysicalRoot: realSource.Root);
        recovery.RecoverForRetry(context);

        Assert.IsTrue(File.Exists(targetPayload));
        CollectionAssert.AreEqual(payloadBytes, File.ReadAllBytes(targetPayload));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_005_Committed_startup_does_not_require_source_to_exist()
    {
        using var target = new TestDirectory();
        SeedValidLibrary(target.Root, out _);

        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourcePayloadSnapshot(target.Root);
        var manifest = MigrationManifestBuilder.BuildCopying(@"C:\NonExistentSource", target.Root, snapshot, Guid.NewGuid());
        manifest.Phase = MigrationManifestPhase.ReadyToCommit;

        string markerPath = Path.Combine(target.Root, ".prompthelper-migration.json");
        var repo = new MigrationManifestRepository();
        repo.WriteReadyManifestDurable(markerPath, manifest);

        var recovery = new MigrationRecoveryService();
        var context = new MigrationRecoveryContext(target.Root, ExpectedSourcePhysicalRoot: null);

        var result = recovery.FinalizeCommittedStartup(context);
        Assert.IsTrue(result.Success);
        Assert.IsFalse(File.Exists(markerPath));
    }

    // ==========================================
    // CRUU9-006 & CRUU9-007: Controls & Staging
    // ==========================================

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_006_Crash_after_probe_dir_creation_recovers()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourcePayloadSnapshot(source.Root);
        Guid attemptId = Guid.NewGuid();
        var probePlan = MigrationCapabilityProbePlan.Create(attemptId);
        var manifest = MigrationManifestBuilder.BuildCopying(source.Root, target.Root, snapshot, attemptId, probePlan);

        string markerPath = Path.Combine(target.Root, ".prompthelper-migration.json");
        var repo = new MigrationManifestRepository();
        repo.CreateInitialCopyingManifestDurable(markerPath, manifest);

        // Inject probe file residue matching what a real crashed attempt would have left:
        // the manifest records the probe's exact expected content ("create"), and recovery
        // now verifies that hash/length before deleting a declared control, so the fixture
        // must match it to simulate a real crash rather than a foreign-file substitution.
        string probeFile = Path.Combine(target.Root, probePlan.RootProbe.CurrentRelativePath);
        File.WriteAllText(probeFile, "create");

        var recovery = new MigrationRecoveryService();
        var context = new MigrationRecoveryContext(target.Root, ExpectedSourcePhysicalRoot: source.Root);
        var result = recovery.RecoverForRetry(context);

        Assert.IsTrue(result.Success);
        Assert.IsFalse(File.Exists(probeFile));
        Assert.IsFalse(File.Exists(markerPath));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_007_Crash_during_ready_stage_is_recoverable()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourcePayloadSnapshot(source.Root);
        Guid attemptId = Guid.NewGuid();
        var manifest = MigrationManifestBuilder.BuildCopying(source.Root, target.Root, snapshot, attemptId);

        string markerPath = Path.Combine(target.Root, ".prompthelper-migration.json");
        var repo = new MigrationManifestRepository();
        repo.CreateInitialCopyingManifestDurable(markerPath, manifest);

        // Inject Ready stage file residue, with the ownership record the interrupted attempt
        // would have written when it created the stage (CRUU15-006): without one, recovery
        // has no proof the file is ours and must preserve it.
        string stagePath = Path.Combine(target.Root, $".prompthelper-migration.stage-{attemptId:N}.tmp");
        File.WriteAllText(stagePath, "partial stage data");
        OwnedArtifactTestSupport.ClaimOwnership(target.Root, stagePath);

        var recovery = new MigrationRecoveryService();
        var context = new MigrationRecoveryContext(target.Root, ExpectedSourcePhysicalRoot: source.Root);
        var result = recovery.RecoverForRetry(context);

        Assert.IsTrue(result.Success);
        Assert.IsFalse(File.Exists(stagePath));
        Assert.IsFalse(File.Exists(markerPath));
    }

    // ==========================================
    // CRUU9-008 & CRUU9-009: Durable Settings Writer
    // ==========================================

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU9_008_Settings_primary_uses_write_through_promotion()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");

        var writer = new WindowsDurableSettingsFileWriter();
        writer.WriteDurable(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"\"}");

        Assert.IsTrue(File.Exists(settingsPath));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_009_Stale_primary_settings_temp_cleaned_under_lease()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        string staleTemp = Path.Combine(temp.Root, SettingsTempName.Generate(settingsPath, Guid.NewGuid()));
        File.WriteAllText(staleTemp, "stale temp content");

        // The interrupted write's ownership record is what entitles reconciliation to destroy
        // this file; a matching filename alone never does (CRUU15-007).
        OwnedArtifactTestSupport.ClaimOwnership(temp.Root, staleTemp);

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        repo.LoadOrRecover();

        Assert.IsFalse(File.Exists(staleTemp));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_009_Invalid_similar_filename_not_deleted()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        string userFile = Path.Combine(temp.Root, ".prompthelper-settings-custom-user.tmp");
        byte[] userBytes = Encoding.UTF8.GetBytes("User file that should never be deleted");
        File.WriteAllBytes(userFile, userBytes);

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        repo.LoadOrRecover();

        Assert.IsTrue(File.Exists(userFile));
        CollectionAssert.AreEqual(userBytes, File.ReadAllBytes(userFile));
    }

    // ==========================================
    // CRUU9-010: Terminal Recovery Verification
    // ==========================================

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_010_New_foreign_entry_after_cleanup_preserves_marker()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();

        SeedValidLibrary(source.Root, out _);
        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourcePayloadSnapshot(source.Root);
        Guid attemptId = Guid.NewGuid();
        var manifest = MigrationManifestBuilder.BuildCopying(source.Root, target.Root, snapshot, attemptId);

        string markerPath = Path.Combine(target.Root, ".prompthelper-migration.json");
        var repo = new MigrationManifestRepository();
        repo.CreateInitialCopyingManifestDurable(markerPath, manifest);

        // Inject foreign file during recovery
        var fakeDeleter = new FakeVerifiedArtifactDeleter
        {
            OnVerifyAndDelete = (root, path, length, sha) =>
            {
                File.WriteAllText(Path.Combine(target.Root, "foreign.txt"), "foreign data");
                File.Delete(path);
            }
        };

        var recovery = new MigrationRecoveryService(verifiedDeleter: fakeDeleter);
        var context = new MigrationRecoveryContext(target.Root, ExpectedSourcePhysicalRoot: source.Root);
        var result = recovery.RecoverForRetry(context);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(File.Exists(markerPath)); // Marker preserved
    }

    // ==========================================
    // CRUU9-012 & CRUU9-013 & CRUU9-014: Reservation
    // ==========================================

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_012_New_target_success_has_no_false_cleanup_warning()
    {
        using var temp = new TestDirectory();
        string newTarget = Path.Combine(temp.Root, "a", "b", "c");

        using var reservation = TargetRootReservation.TryAcquire(newTarget);
        Assert.IsNotNull(reservation);

        // Create files in committed root
        File.WriteAllText(Path.Combine(newTarget, "library.json"), "{}");

        reservation.CommitRootOwnership();
        var result = reservation.Release();

        Assert.IsTrue(result.Success);
        Assert.IsNull(result.ToWarning());
        Assert.IsTrue(Directory.Exists(newTarget));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_014_Stale_root_app_lock_does_not_make_target_occupied()
    {
        using var temp = new TestDirectory();
        string lockFile = Path.Combine(temp.Root, ".app.lock");
        File.WriteAllText(lockFile, ""); // Unheld lock file

        var inspection = EmptyTargetBaselineInspector.Inspect(temp.Root, null, isReservationActive: false);
        Assert.IsTrue(inspection.IsAcceptable);
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_014_Held_root_app_lock_blocks_transition()
    {
        using var temp = new TestDirectory();
        string lockFile = Path.Combine(temp.Root, ".app.lock");
        using var heldLock = AppInstanceLock.TryAcquire(lockFile);
        Assert.IsNotNull(heldLock);

        using var reservation = TargetRootReservation.TryAcquire(temp.Root);
        Assert.IsNull(reservation);
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_016_Nested_app_lock_is_unknown()
    {
        using var temp = new TestDirectory();
        string promptsDir = Path.Combine(temp.Root, "prompts");
        Directory.CreateDirectory(promptsDir);
        File.WriteAllText(Path.Combine(promptsDir, ".app.lock"), ""); // Nested .app.lock

        var inspection = EmptyTargetBaselineInspector.Inspect(temp.Root, null);
        Assert.IsFalse(inspection.IsAcceptable);
        Assert.IsTrue(inspection.UnexpectedEntries.Count > 0);
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_017_Ready_gate_rejects_declared_temp()
    {
        using var temp = new TestDirectory();
        SeedValidLibrary(temp.Root, out _);

        var migration = new DataFolderMigrationService();
        var snapshot = migration.CaptureSourcePayloadSnapshot(temp.Root);
        Guid attemptId = Guid.NewGuid();
        var manifest = MigrationManifestBuilder.BuildCopying(temp.Root, temp.Root, snapshot, attemptId);

        // Inject temp file
        string tempPath = Path.Combine(temp.Root, manifest.Artifacts[0].TempRelativePath);
        File.WriteAllText(tempPath, "temporary");

        var gate = new MigrationReadyGate();
        Assert.Throws<InvalidDataException>(() => gate.AssertReady(temp.Root, temp.Root, manifest, snapshot));
    }

    // ==========================================
    // CRUU9-020: Unconditional Postcommit Process Boundary
    // ==========================================

    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void CRUU9_020_RestartRequired_true_Result_null_shuts_down()
    {
        WpfTestHost.Invoke(() =>
        {
            using var temp = new TestDirectory();
            SeedValidLibrary(temp.Root, out var doc);
            var paths = new AppPaths(temp.Root);
            var writer = new AtomicTextWriter();
            var deleter = new FileDeleter();
            var libRepo = new LibraryRepository(paths, writer);
            var promptRepo = new PromptRepository(paths, writer, deleter);
            var libService = new PromptLibraryService(doc, libRepo, promptRepo);
            var lifetime = new FakeApplicationLifetime();
            var vm = new MainViewModel(libService, promptRepo, temp.Root);
            var window = new MainWindow(vm, new FakeClipboardService(), applicationLifetime: lifetime, showRestartMessage: (_, _) => { });

            window.CompleteSettingsDialog(dialogResult: null, restartRequired: true);

            Assert.IsTrue(lifetime.ShutdownRequested);
        });
    }

    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void CRUU9_020_Restart_message_failure_still_requests_shutdown()
    {
        WpfTestHost.Invoke(() =>
        {
            using var temp = new TestDirectory();
            SeedValidLibrary(temp.Root, out var doc);
            var paths = new AppPaths(temp.Root);
            var writer = new AtomicTextWriter();
            var deleter = new FileDeleter();
            var libRepo = new LibraryRepository(paths, writer);
            var promptRepo = new PromptRepository(paths, writer, deleter);
            var libService = new PromptLibraryService(doc, libRepo, promptRepo);
            var lifetime = new FakeApplicationLifetime();
            var vm = new MainViewModel(libService, promptRepo, temp.Root);
            // Simulate message display failure/exception
            var window = new MainWindow(vm, new FakeClipboardService(), applicationLifetime: lifetime, showRestartMessage: (_, _) => throw new InvalidOperationException("Dialog error"));

            try
            {
                window.CompleteSettingsDialog(dialogResult: true, restartRequired: true);
            }
            catch (InvalidOperationException)
            {
            }

            Assert.IsTrue(lifetime.ShutdownRequested, "RequestShutdown must be called even if message display fails");
        });
    }

    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void CRUU9_020_RestartRequired_false_does_not_shutdown()
    {
        WpfTestHost.Invoke(() =>
        {
            using var temp = new TestDirectory();
            SeedValidLibrary(temp.Root, out var doc);
            var paths = new AppPaths(temp.Root);
            var writer = new AtomicTextWriter();
            var deleter = new FileDeleter();
            var libRepo = new LibraryRepository(paths, writer);
            var promptRepo = new PromptRepository(paths, writer, deleter);
            var libService = new PromptLibraryService(doc, libRepo, promptRepo);
            var lifetime = new FakeApplicationLifetime();
            var vm = new MainViewModel(libService, promptRepo, temp.Root);
            var window = new MainWindow(vm, new FakeClipboardService(), applicationLifetime: lifetime, showRestartMessage: (_, _) => { });

            window.CompleteSettingsDialog(dialogResult: true, restartRequired: false);

            Assert.IsFalse(lifetime.ShutdownRequested);
        });
    }

    // ==========================================
    // CRUU9-021 & CRUU9-022: Evidence Parsing
    // ==========================================

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_021_VerifyTestEvidence_accepts_valid_fixture_TRX()
    {
        using var temp = new TestDirectory();
        string trxPath = Path.Combine(temp.Root, "test.trx");
        string trxXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun id="1" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <ResultSummary outcome="Completed">
                <Counters total="2" passed="2" failed="0" error="0" timeout="0" aborted="0" />
              </ResultSummary>
              <Results>
                <UnitTestResult testName="CRUU9_001_Empty_prompts_junction_outside_target_is_rejected" outcome="Passed" />
                <UnitTestResult testName="CRUU9_002_Unreadable_marker_is_not_treated_as_missing" outcome="Passed" />
              </Results>
            </TestRun>
            """;
        File.WriteAllText(trxPath, trxXml);

        string scriptPath = Path.Combine(GetRepositoryRoot(), "tools", "VerifyTestEvidence.ps1");

        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -TrxPath \"{trxPath}\" -RequiredTests CRUU9_001_Empty_prompts_junction_outside_target_is_rejected")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 60_000);
        Assert.IsTrue(run.Exited, "VerifyTestEvidence.ps1 timed out.");
        Assert.AreEqual(0, run.ExitCode, $"Script failed with exit code {run.ExitCode}.\nStdout: {run.StandardOutput}\nStderr: {run.StandardError}");
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_021_VerifyTestEvidence_rejects_missing_sentinel_TRX()
    {
        using var temp = new TestDirectory();
        string trxPath = Path.Combine(temp.Root, "test.trx");
        string trxXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun id="1" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <ResultSummary outcome="Completed">
                <Counters total="1" passed="1" failed="0" error="0" timeout="0" aborted="0" />
              </ResultSummary>
              <Results>
                <UnitTestResult testName="SomeOtherTest" outcome="Passed" />
              </Results>
            </TestRun>
            """;
        File.WriteAllText(trxPath, trxXml);

        string scriptPath = Path.Combine(GetRepositoryRoot(), "tools", "VerifyTestEvidence.ps1");

        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -TrxPath \"{trxPath}\" -RequiredTests NonExistentSentinelTest");
        ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 60_000);
        Assert.IsTrue(run.Exited, "VerifyTestEvidence.ps1 timed out.");
        Assert.AreNotEqual(0, run.ExitCode);
    }

    // ==========================================
    // CRUU9-024: Strict JSON Authority
    // ==========================================

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_024_Duplicate_targetPhysicalRoot_rejected()
    {
        using var temp = new TestDirectory();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");
        string json = """
            {
              "schemaVersion": 3,
              "attemptId": "d7486e92-36c5-4b47-920f-561b369c0993",
              "sourcePhysicalRoot": "C:\\Source",
              "targetPhysicalRoot": "C:\\Target1",
              "targetPhysicalRoot": "C:\\Target2",
              "sourceLibrarySha256Hex": "0000000000000000000000000000000000000000000000000000000000000000",
              "phase": "Copying",
              "artifacts": []
            }
            """;
        File.WriteAllText(markerPath, json);

        var repo = new MigrationManifestRepository();
        Assert.Throws<InvalidDataException>(() => repo.TryReadStrict(markerPath));
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU9_024_Invalid_UTF8_manifest_rejected()
    {
        using var temp = new TestDirectory();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");
        byte[] invalidUtf8 = [0x7B, 0x22, 0xFF, 0xFE, 0x7D]; // Invalid UTF-8 sequence
        File.WriteAllBytes(markerPath, invalidUtf8);

        var repo = new MigrationManifestRepository();
        Assert.Throws<InvalidDataException>(() => repo.TryReadStrict(markerPath));
    }
}
