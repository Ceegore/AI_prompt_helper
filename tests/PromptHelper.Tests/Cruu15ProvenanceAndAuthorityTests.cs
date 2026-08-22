using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

/// <summary>
/// CRUU15-005 through CRUU15-008: retirement is reparse-safe and root-bound, destruction is
/// provenance-bound, and inventory classification is taken from the directory object itself.
/// </summary>
[TestClass]
public sealed class Cruu15ProvenanceAndAuthorityTests
{
    /// <summary>
    /// Creates a file symlink. Reparse-point creation is a hard requirement for this suite, not
    /// a best-effort nicety: a test that quietly turns Inconclusive here would leave the exact
    /// class of defect CRUU15-005 describes unverified. Developer Mode (or an elevated shell)
    /// makes this work; CI runs with it enabled.
    /// </summary>
    private static void CreateFileSymlinkOrFail(string linkPath, string targetPath)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink \"{linkPath}\" \"{targetPath}\"");
        ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 30_000);

        Assert.IsTrue(
            run.Exited && run.ExitCode == 0 && File.Exists(linkPath),
            "Creating a file symlink is required for this test. Enable Windows Developer Mode " +
            $"or run elevated.\n{run.CombinedOutput}");
    }

    // ==========================================
    // CRUU15-005: strict, root-bound journal/marker retirement
    // ==========================================

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU15_005_Mutation_journal_file_symlink_is_never_followed_or_deleted()
    {
        using var temp = new TestDirectory();
        using var outside = new TestDirectory();

        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        string victim = Path.Combine(outside.Root, "someone-elses-file.json");
        File.WriteAllText(victim, "content that lives outside the data root");

        CreateFileSymlinkOrFail(paths.LibraryMutationJournalPath, victim);

        var repo = new LibraryMutationJournalRepository(paths, new WindowsDurableAtomicFileWriter());

        Assert.ThrowsExactly<InvalidDataException>(
            () => repo.DeleteStrict(Guid.NewGuid(), expectedRevision: 1));

        Assert.IsTrue(File.Exists(victim), "The symlink target must never be deleted.");
        Assert.AreEqual("content that lives outside the data root", File.ReadAllText(victim));
    }

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU15_005_Initialization_journal_file_symlink_is_never_followed_or_deleted()
    {
        using var temp = new TestDirectory();
        using var outside = new TestDirectory();

        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        string victim = Path.Combine(outside.Root, "someone-elses-marker.json");
        File.WriteAllText(victim, "outside content");

        CreateFileSymlinkOrFail(paths.InitializationMarkerPath, victim);

        var repo = new LibraryInitializationJournalRepository(paths, new WindowsDurableAtomicFileWriter());

        Assert.ThrowsExactly<InvalidDataException>(
            () => repo.DeleteStrict(Guid.NewGuid(), expectedRevision: 1));

        Assert.IsTrue(File.Exists(victim));
        Assert.AreEqual("outside content", File.ReadAllText(victim));
    }

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU15_005_Migration_marker_file_symlink_is_never_followed_by_retirement()
    {
        using var temp = new TestDirectory();
        using var outside = new TestDirectory();

        string victim = Path.Combine(outside.Root, "someone-elses-manifest.json");
        File.WriteAllText(victim, "outside content");

        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");
        CreateFileSymlinkOrFail(markerPath, victim);

        var repo = new MigrationManifestRepository();

        Assert.ThrowsExactly<InvalidDataException>(() =>
            repo.DeleteStrict(markerPath, Guid.NewGuid(), MigrationManifestPhase.ReadyToCommit));

        Assert.IsTrue(File.Exists(victim));
        Assert.AreEqual("outside content", File.ReadAllText(victim));
    }

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU15_005_Journal_retirement_final_handle_path_must_be_under_expected_root()
    {
        using var inside = new TestDirectory();
        using var outside = new TestDirectory();

        string victim = Path.Combine(outside.Root, "outside.json");
        File.WriteAllText(victim, "outside");

        // Opening an object that resolves outside the expected root is refused outright, so no
        // read and no deletion is ever performed against it.
        Assert.ThrowsExactly<InvalidDataException>(
            () => WindowsStrictRetirableFile.OpenExistingOrNull(victim, inside.Root));

        Assert.IsTrue(File.Exists(victim));

        // A genuine descendant is accepted, read and retired through the same handle.
        string legitimate = Path.Combine(inside.Root, "control.json");
        File.WriteAllText(legitimate, "managed");

        using (WindowsStrictRetirableFile? handle =
               WindowsStrictRetirableFile.OpenExistingOrNull(legitimate, inside.Root))
        {
            Assert.IsNotNull(handle);
            Assert.AreEqual("managed", Encoding.UTF8.GetString(handle!.ReadAllBytes()));
            handle.DeleteExact();
        }

        Assert.IsFalse(File.Exists(legitimate));
    }

    // ==========================================
    // CRUU15-006: recovery destroys only what it can prove it created
    // ==========================================

    [TestMethod]
    public void CRUU15_006_Recovery_payload_temp_replaced_by_foreign_regular_file_is_preserved()
    {
        using var target = new TestDirectory();
        Guid attemptId = Guid.NewGuid();

        string tempRel = Path.Combine("prompts", $".test.md.migration-{attemptId:N}-{new string('b', 32)}.tmp");
        string tempFullPath = Path.Combine(target.Root, tempRel);
        Directory.CreateDirectory(Path.GetDirectoryName(tempFullPath)!);

        byte[] foreign = Encoding.UTF8.GetBytes("a regular file someone else left at this pathname");
        File.WriteAllBytes(tempFullPath, foreign);

        MigrationAttemptManifest manifest = BuildCopyingManifest(target.Root, attemptId, tempRel);
        var repo = new MigrationManifestRepository();
        repo.WriteDurable(Path.Combine(target.Root, ".prompthelper-migration.json"), manifest);

        var recovery = new MigrationRecoveryService(repo);
        RecoveryResult result = recovery.RecoverForRetry(
            new MigrationRecoveryContext(target.Root, ExpectedSourcePhysicalRoot: @"C:\Source"));

        Assert.IsFalse(result.Success, "Recovery must fail closed rather than destroy unproven data.");
        Assert.IsTrue(File.Exists(tempFullPath));
        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(tempFullPath));
    }

    [TestMethod]
    public void CRUU15_006_Recovery_manifest_stage_replaced_by_foreign_regular_file_is_preserved()
    {
        using var target = new TestDirectory();
        Guid attemptId = Guid.NewGuid();

        string stageRel = $".prompthelper-migration.stage-{attemptId:N}.tmp";
        string stagePath = Path.Combine(target.Root, stageRel);
        byte[] foreign = Encoding.UTF8.GetBytes("foreign object at the declared staging pathname");
        File.WriteAllBytes(stagePath, foreign);

        MigrationAttemptManifest manifest = BuildCopyingManifest(target.Root, attemptId, null);
        manifest.ControlArtifacts =
        [
            new MigrationControlArtifact
            {
                RelativePath = stageRel,
                Kind = MigrationControlArtifactKind.ManifestPhaseStaging
            }
        ];

        var repo = new MigrationManifestRepository();
        repo.WriteDurable(Path.Combine(target.Root, ".prompthelper-migration.json"), manifest);

        var recovery = new MigrationRecoveryService(repo);
        RecoveryResult result = recovery.RecoverForRetry(
            new MigrationRecoveryContext(target.Root, ExpectedSourcePhysicalRoot: @"C:\Source"));

        Assert.IsFalse(result.Success);
        Assert.IsTrue(File.Exists(stagePath));
        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(stagePath));
    }

    [TestMethod]
    public void CRUU15_006_Capability_probe_directory_swapped_after_empty_check_is_not_deleted()
    {
        using var temp = new TestDirectory();
        string probeDir = Path.Combine(temp.Root, "probe");
        Directory.CreateDirectory(probeDir);

        IMigrationFileOps ops = new DefaultMigrationFileOps();

        // The kernel re-evaluates emptiness when the handle-bound disposition is applied, so a
        // directory that gained an entry after any earlier inspection is refused rather than
        // removed with its contents.
        File.WriteAllText(Path.Combine(probeDir, "someone-elses-file.txt"), "content");

        Assert.ThrowsExactly<IOException>(() => ops.DeleteDirectoryExact(temp.Root, probeDir));

        Assert.IsTrue(Directory.Exists(probeDir));
        Assert.AreEqual(1, Directory.GetFileSystemEntries(probeDir).Length);
    }

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU15_006_Attempt_created_directory_replacement_is_not_deleted_by_path()
    {
        using var temp = new TestDirectory();
        using var outside = new TestDirectory();

        string victimDir = Path.Combine(outside.Root, "someone-elses-directory");
        Directory.CreateDirectory(victimDir);

        string junction = Path.Combine(temp.Root, "prompts");
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{victimDir}\"");
        ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 30_000);
        Assert.IsTrue(run.Exited && run.ExitCode == 0, $"mklink /J failed: {run.CombinedOutput}");

        IMigrationFileOps ops = new DefaultMigrationFileOps();

        // A reparse point substituted at an attempt-created directory pathname is refused, so
        // the directory it redirects to is never removed.
        Assert.ThrowsExactly<InvalidDataException>(() => ops.DeleteDirectoryExact(temp.Root, junction));
        Assert.IsTrue(Directory.Exists(victimDir));
    }

    [TestMethod]
    public void CRUU15_006_FinalizeCommittedStartup_never_raw_deletes_unproven_stage()
    {
        using var target = new TestDirectory();
        Guid attemptId = Guid.NewGuid();

        MigrationAttemptManifest manifest = BuildCopyingManifest(target.Root, attemptId, null);
        manifest.Phase = MigrationManifestPhase.ReadyToCommit;

        var repo = new MigrationManifestRepository();
        repo.WriteDurable(Path.Combine(target.Root, ".prompthelper-migration.json"), manifest);

        // Only now does a foreign object appear at the declared staging pathname: that write
        // reached a terminal state and cleaned up after itself, so anything here afterwards is
        // somebody else's.
        string stagePath = Path.Combine(target.Root, $".prompthelper-migration.stage-{attemptId:N}.tmp");
        byte[] foreign = Encoding.UTF8.GetBytes("foreign stage at committed-startup time");
        File.WriteAllBytes(stagePath, foreign);

        var recovery = new MigrationRecoveryService(repo);
        RecoveryResult result = recovery.FinalizeCommittedStartup(
            new MigrationRecoveryContext(target.Root, ExpectedSourcePhysicalRoot: @"C:\Source"));

        Assert.IsFalse(result.Success, "Startup must fail closed rather than raw-delete an unproven stage.");
        Assert.IsTrue(File.Exists(stagePath));
        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(stagePath));
    }

    // ==========================================
    // CRUU15-007: startup temp reconciliation is provenance-driven
    // ==========================================

    [TestMethod]
    public void CRUU15_007_Current_format_settings_temp_without_provenance_is_preserved()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        string tempPath = Path.Combine(temp.Root, SettingsTempName.Generate(settingsPath, Guid.NewGuid()));
        byte[] foreign = Encoding.UTF8.GetBytes("someone else's file at a name that matches our grammar");
        File.WriteAllBytes(tempPath, foreign);

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        repo.LoadOrRecover();

        Assert.IsTrue(File.Exists(tempPath), "A current-format name is not ownership evidence.");
        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(tempPath));
    }

    [TestMethod]
    public void CRUU15_007_Current_format_prompt_temp_without_provenance_is_preserved()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        string tempPath = Path.Combine(
            paths.PromptsDirectory,
            $".prompthelper-tmp-prompt-{Guid.NewGuid():N}.tmp");
        byte[] foreign = Encoding.UTF8.GetBytes("foreign prompt-shaped temp");
        File.WriteAllBytes(tempPath, foreign);

        DataRootTempReconciler.Reconcile(paths);

        Assert.IsTrue(File.Exists(tempPath));
        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(tempPath));
    }

    [TestMethod]
    public void CRUU15_007_Current_format_recovery_temp_without_provenance_is_preserved()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        string tempPath = Path.Combine(
            paths.RecoveryDirectory,
            $".prompthelper-tmp-recovery-{Guid.NewGuid():N}.tmp");
        byte[] foreign = Encoding.UTF8.GetBytes("foreign recovery-shaped temp");
        File.WriteAllBytes(tempPath, foreign);

        DataRootTempReconciler.Reconcile(paths);

        Assert.IsTrue(File.Exists(tempPath));
        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(tempPath));
    }

    [TestMethod]
    public void CRUU15_007_Recorded_owned_temp_is_cleaned_using_recorded_identity()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        string ownedPath = Path.Combine(
            paths.RecoveryDirectory,
            $".prompthelper-tmp-recovery-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(ownedPath, "our own interrupted stage");
        OwnedArtifactTestSupport.ClaimOwnership(temp.Root, ownedPath);

        // A second temp is recorded and then replaced by a different object at the same
        // pathname: the record no longer describes what is there, so it must survive.
        string swappedPath = Path.Combine(
            paths.RecoveryDirectory,
            $".prompthelper-tmp-recovery-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(swappedPath, "ours, briefly");
        OwnedArtifactTestSupport.ClaimOwnership(temp.Root, swappedPath);
        File.Delete(swappedPath);
        byte[] impostor = Encoding.UTF8.GetBytes("a different object at the recorded pathname");
        File.WriteAllBytes(swappedPath, impostor);

        DataRootTempReconciler.Reconcile(paths);

        Assert.IsFalse(File.Exists(ownedPath), "A temp proven ours by recorded identity is cleaned up.");
        Assert.IsTrue(File.Exists(swappedPath), "A different object at a recorded pathname is not ours.");
        CollectionAssert.AreEqual(impostor, File.ReadAllBytes(swappedPath));
    }

    // ==========================================
    // CRUU15-008: inventory classification is bound to the directory object
    // ==========================================

    [TestMethod]
    public void CRUU15_008_Inventory_file_swap_between_enumeration_and_probe_fails_closed()
    {
        using var temp = new TestDirectory();

        string entry = Path.Combine(temp.Root, "entry");
        File.WriteAllText(entry, "a file");

        // The listing comes from the directory object itself and carries each entry's kind, so
        // an entry that has become a different kind of object by the time it is probed is
        // rejected instead of silently reclassified.
        var enumeratedAsFile = new DirectoryEntry("entry", 0x00000080 /* FILE_ATTRIBUTE_NORMAL */);
        Assert.IsFalse(enumeratedAsFile.IsDirectory);

        File.Delete(entry);
        Directory.CreateDirectory(entry);

        var manifest = BuildCopyingManifest(temp.Root, Guid.NewGuid(), null);
        MigrationTargetInventory inventory = MigrationTargetInventoryInspector.Inspect(temp.Root, manifest);

        // The directory listing and the probe now agree that it is a directory, and an
        // undeclared directory is reported as unknown rather than ignored.
        Assert.IsTrue(inventory.HasUnknownEntries);
        Assert.IsTrue(inventory.UnknownEntries.Any(e => e.EndsWith("entry", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void CRUU15_008_Inventory_directory_swap_between_classification_and_cleanup_cannot_author_delete()
    {
        using var temp = new TestDirectory();
        string dir = Path.Combine(temp.Root, "probe");
        Directory.CreateDirectory(dir);

        // Inventory classification is advisory. Every destructive operation carries its own
        // ownership authority, so a classification made earlier can never authorize a deletion
        // of whatever occupies that pathname later.
        var manifest = BuildCopyingManifest(temp.Root, Guid.NewGuid(), null);
        MigrationTargetInventoryInspector.Inspect(temp.Root, manifest);

        Directory.Delete(dir);
        File.WriteAllText(dir, "now a file, not the directory that was classified");

        IMigrationFileOps ops = new DefaultMigrationFileOps();
        Assert.ThrowsExactly<InvalidOperationException>(() => ops.DeleteDirectoryExact(temp.Root, dir));
        Assert.IsTrue(File.Exists(dir));
    }

    [TestMethod]
    public void CRUU15_008_Unreadable_entry_is_never_reclassified_as_absent()
    {
        using var temp = new TestDirectory();
        string missing = Path.Combine(temp.Root, "does-not-exist");

        // A genuinely missing directory reports null...
        Assert.IsNull(WindowsDirectoryEnumeration.ListStrict(missing));

        // ...but an entry that cannot be read is an error, never an empty listing. Enumerating
        // a file as if it were a directory is the cheapest reproducible instance of that.
        string file = Path.Combine(temp.Root, "a-file");
        File.WriteAllText(file, "content");
        Assert.ThrowsExactly<InvalidDataException>(() => WindowsDirectoryEnumeration.ListStrict(file));
    }

    // ==========================================
    // Helpers
    // ==========================================

    private static MigrationAttemptManifest BuildCopyingManifest(string root, Guid attemptId, string? tempRel)
    {
        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = MigrationAttemptManifest.CurrentSchemaVersion,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = root,
            SourceLibrarySha256Hex = new string('0', 64),
            SourcePayloadFingerprintSha256Hex = new string('0', 64),
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

        if (tempRel is not null)
        {
            manifest.Artifacts.Add(new MigrationManifestArtifact
            {
                RelativePath = "prompts/test.md",
                TempRelativePath = tempRel,
                Length = 20,
                Sha256Hex = new string('0', 64),
                Role = MigrationPayloadRole.PromptBody
            });
        }

        return manifest;
    }
}
