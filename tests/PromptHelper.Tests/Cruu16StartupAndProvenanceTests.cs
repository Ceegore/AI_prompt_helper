using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

/// <summary>
/// CRUU16-004 through CRUU16-007: unresolved recovery stops startup, migration ownership
/// survives promotion, rollback destroys nothing it cannot prove, and root-bound staging is a
/// property of every production writer rather than of one call site.
/// </summary>
[TestClass]
public sealed class Cruu16StartupAndProvenanceTests
{
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string JournalPath(string root) => Path.Combine(root, ".prompthelper-owned.log");

    private sealed class InterruptedSwap : Exception;

    /// <summary>Leaves the on-disk state a crash between the two CAS renames would produce.</summary>
    private static void InterruptSwap(string root, string target, byte[] candidate, string expectedHash)
    {
        WindowsAtomicExpectedFileReplacer.BetweenRenamesForTests = _ => throw new InterruptedSwap();
        try
        {
            Assert.ThrowsExactly<InterruptedSwap>(() =>
                new WindowsAtomicExpectedFileReplacer().ReplaceIfExpected(
                    root, target, ExpectedFileState.Present(expectedHash), candidate, DurableFileClass.Settings));
        }
        finally
        {
            WindowsAtomicExpectedFileReplacer.BetweenRenamesForTests = null;
        }
    }

    private static void CorruptJournal(string root)
    {
        using var stream = new FileStream(JournalPath(root), FileMode.Append, FileAccess.Write, FileShare.None);
        stream.Write(Encoding.UTF8.GetBytes("2|corrupt|stage|claimed|x|y|||0000000000000000\n"));
    }

    // ==========================================
    // CRUU16-004: unresolved recovery is fatal, benign preservation is not
    // ==========================================

    [TestMethod]
    public void CRUU16_004_Settings_load_aborts_when_CAS_preimage_restore_fails()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        byte[] committed = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"dataRootPath\":\"\"}");
        File.WriteAllBytes(settingsPath, committed);

        InterruptSwap(temp.Root, settingsPath,
            Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"dataRootPath\":\"x\"}"), Hash(committed));

        // Something occupies the settings pathname before restart, so the interrupted swap
        // cannot be resolved: the last committed settings are in the pre-image.
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"planted\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);

        // Reading on would treat a crash window as ordinary state.
        Assert.ThrowsExactly<UnresolvedRecoveryStateException>(() => repo.LoadOrRecover());

        Assert.AreEqual(1, Directory.GetFiles(temp.Root, ".prompthelper-preimage-*").Length,
            "The last committed settings must still be on disk.");
    }

    [TestMethod]
    public void CRUU16_004_Settings_load_aborts_when_ownership_journal_is_corrupt()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"\"}");

        string stage = Path.Combine(temp.Root, $".prompthelper-tmp-settings-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(stage, "staged");
        OwnedArtifactTestSupport.ClaimOwnership(temp.Root, stage);
        CorruptJournal(temp.Root);

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);

        Assert.ThrowsExactly<UnresolvedRecoveryStateException>(() => repo.LoadOrRecover());
    }

    [TestMethod]
    public void CRUU16_004_Data_root_startup_aborts_on_unresolved_CAS_recovery()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        string target = Path.Combine(temp.Root, "library.json");
        byte[] committed = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"categories\":[],\"prompts\":[]}");
        File.WriteAllBytes(target, committed);

        InterruptSwap(temp.Root, target, Encoding.UTF8.GetBytes("candidate"), Hash(committed));
        File.WriteAllText(target, "planted by something else");

        TempReconciliationResult result = DataRootTempReconciler.Reconcile(paths);

        Assert.ThrowsExactly<UnresolvedRecoveryStateException>(() => result.ThrowIfUnresolved());
    }

    [TestMethod]
    public void CRUU16_004_Data_root_startup_aborts_on_ownership_authority_violation()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        string stage = Path.Combine(temp.Root, $".prompthelper-tmp-library-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(stage, "staged");
        OwnedArtifactTestSupport.ClaimOwnership(temp.Root, stage);
        CorruptJournal(temp.Root);

        TempReconciliationResult result = DataRootTempReconciler.Reconcile(paths);

        Assert.ThrowsExactly<UnresolvedRecoveryStateException>(() => result.ThrowIfUnresolved());
    }

    [TestMethod]
    public void CRUU16_004_Unproven_temp_preservation_remains_nonfatal_warning()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        // A current-format name with no ownership record: preserved, and entirely routine.
        File.WriteAllText(
            Path.Combine(temp.Root, $".prompthelper-tmp-library-{Guid.NewGuid():N}.tmp"),
            "someone else's file");

        TempReconciliationResult result = DataRootTempReconciler.Reconcile(paths);

        result.ThrowIfUnresolved();
        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.Preserved.Any(a => a.Provenance == ArtifactProvenance.UnprovenCurrentFormat));
    }

    [TestMethod]
    public void CRUU16_004_Benign_cleanup_failure_does_not_get_conflated_with_committed_state_recovery()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        string owned = Path.Combine(paths.RecoveryDirectory, $".prompthelper-tmp-recovery-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(owned, "ours, but locked");
        OwnedArtifactTestSupport.ClaimOwnership(temp.Root, owned);

        using var hold = new FileStream(owned, FileMode.Open, FileAccess.Read, FileShare.None);

        TempReconciliationResult result = DataRootTempReconciler.Reconcile(paths);

        // Reported, but never escalated: a stage that could not be tidied up puts no committed
        // content at risk, and stopping startup for it would be wrong.
        Assert.IsFalse(result.Success);
        result.ThrowIfUnresolved();
        Assert.IsFalse(result.HasFatal);
    }

    // ==========================================
    // CRUU16-005: migration ownership survives promotion
    // ==========================================

    [TestMethod]
    public void CRUU16_005_Migration_promotion_transfers_identity_from_temp_to_final()
    {
        using var temp = new TestDirectory();
        string stagePath = Path.Combine(temp.Root, "payload.stage.tmp");
        string finalPath = Path.Combine(temp.Root, "payload.md");

        IMigrationFileOps ops = new DefaultMigrationFileOps();
        string identity;
        using (IOwnedFileStage stage = ops.CreateOwnedStage(temp.Root, stagePath))
        {
            identity = stage.IdentityToken;
            stage.Write(Encoding.UTF8.GetBytes("payload"));
            stage.FlushDurable();
            stage.PromoteNoOverwriteExact(finalPath);
        }

        ops.RecordPromotedFinal(temp.Root, finalPath, identity);

        OwnedArtifactRecord record = new WindowsOwnedArtifactJournal()
            .Read(temp.Root).Records
            .Single(r => r.Kind == OwnedArtifactKind.MigrationFinal);

        Assert.AreEqual("payload.md", record.RelativePath);
        Assert.AreEqual(identity, record.Identity.ToToken(),
            "The promoted object keeps the identity it was created with, so provenance survives the rename.");
    }

    [TestMethod]
    public void CRUU16_005_Migration_final_replaced_by_foreign_same_bytes_is_preserved()
    {
        using var temp = new TestDirectory();
        string finalPath = Path.Combine(temp.Root, "payload.md");
        byte[] payload = Encoding.UTF8.GetBytes("payload");

        IMigrationFileOps ops = new DefaultMigrationFileOps();
        string stagePath = Path.Combine(temp.Root, "payload.stage.tmp");
        using (IOwnedFileStage stage = ops.CreateOwnedStage(temp.Root, stagePath))
        {
            stage.Write(payload);
            stage.FlushDurable();
            stage.PromoteNoOverwriteExact(finalPath);
            ops.RecordPromotedFinal(temp.Root, finalPath, stage.IdentityToken);
        }

        // Someone replaces the final with a different object holding identical bytes.
        File.Delete(finalPath);
        File.WriteAllBytes(finalPath, payload);

        Assert.AreEqual(
            ArtifactCleanupOutcome.PreservedUnproven,
            ops.DeleteOwnedFinalIfProven(temp.Root, finalPath),
            "Identical content is not identity.");
        Assert.IsTrue(File.Exists(finalPath));
    }

    [TestMethod]
    public void CRUU16_005_Retry_final_delete_requires_recorded_file_identity()
    {
        using var temp = new TestDirectory();
        string finalPath = Path.Combine(temp.Root, "payload.md");
        IMigrationFileOps ops = new DefaultMigrationFileOps();

        // No record at all: nothing authorizes deletion.
        File.WriteAllText(finalPath, "content");
        Assert.AreEqual(
            ArtifactCleanupOutcome.PreservedUnproven,
            ops.DeleteOwnedFinalIfProven(temp.Root, finalPath));
        Assert.IsTrue(File.Exists(finalPath));

        // With the record, the exact object may be removed.
        OwnedArtifactTestSupport.ClaimPromotedFinal(temp.Root, finalPath);
        Assert.AreEqual(
            ArtifactCleanupOutcome.DeletedProvenOwned,
            ops.DeleteOwnedFinalIfProven(temp.Root, finalPath));
        Assert.IsFalse(File.Exists(finalPath));
    }

    [TestMethod]
    public void CRUU16_005_Inprocess_rollback_final_delete_requires_recorded_file_identity()
    {
        // The transaction's owned-file bookkeeping carries the promoted object's identity, and
        // rollback proves it through a single handle that also performs the deletion.
        Type ownedFile = typeof(DataFolderMigrationService).GetNestedTypes()
            .Single(t => t.Name == "MigrationOwnedFile");

        Assert.IsNotNull(ownedFile.GetProperty("FinalIdentityToken"),
            "Rollback cannot require an identity it never recorded.");

        Assert.IsNotNull(
            typeof(IVerifiedArtifactDeleter).GetMethod("TryVerifyIdentityContentAndDelete"),
            "Identity, content and deletion must be provable through one retained handle.");
    }

    [TestMethod]
    public void CRUU16_005_Final_hash_match_alone_never_authorizes_automatic_deletion()
    {
        using var temp = new TestDirectory();
        string finalPath = Path.Combine(temp.Root, "payload.md");
        byte[] payload = Encoding.UTF8.GetBytes("payload");
        File.WriteAllBytes(finalPath, payload);

        OwnedArtifactTestSupport.ClaimPromotedFinal(temp.Root, finalPath);

        // Same bytes, different object.
        File.Delete(finalPath);
        File.WriteAllBytes(finalPath, payload);

        var deleter = new WindowsVerifiedArtifactDeleter();
        OwnedArtifactRecord record = new WindowsOwnedArtifactJournal()
            .Read(temp.Root).Records
            .Single(r => r.Kind == OwnedArtifactKind.MigrationFinal);

        bool deleted = deleter.TryVerifyIdentityContentAndDelete(
            temp.Root, finalPath, payload.Length, Hash(payload), record.Identity.ToToken());

        Assert.IsFalse(deleted, "Matching length and hash must not be enough.");
        Assert.IsTrue(File.Exists(finalPath));
    }

    // ==========================================
    // CRUU16-006: no pathname-based destruction remains in migration
    // ==========================================

    [TestMethod]
    public void CRUU16_006_Inprocess_rollback_swapped_attempt_directory_is_never_deleted()
    {
        using var temp = new TestDirectory();
        string dir = Path.Combine(temp.Root, "attempt-created");
        Directory.CreateDirectory(dir);

        // The directory is replaced by a file after the attempt tracked it.
        Directory.Delete(dir);
        File.WriteAllText(dir, "now a file");

        IMigrationFileOps ops = new DefaultMigrationFileOps();
        Assert.ThrowsExactly<InvalidOperationException>(() => ops.DeleteDirectoryExact(temp.Root, dir));
        Assert.IsTrue(File.Exists(dir));
    }

    [TestMethod]
    public void CRUU16_006_Inprocess_rollback_directory_is_removed_through_exact_handle()
    {
        using var temp = new TestDirectory();
        string dir = Path.Combine(temp.Root, "attempt-created");
        Directory.CreateDirectory(dir);

        IMigrationFileOps ops = new DefaultMigrationFileOps();
        ops.DeleteDirectoryExact(temp.Root, dir);
        Assert.IsFalse(Directory.Exists(dir));

        // A directory that gained an entry is refused: the kernel re-checks emptiness when the
        // handle-bound disposition is applied.
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "someone-elses-file.txt"), "content");
        Assert.ThrowsExactly<IOException>(() => ops.DeleteDirectoryExact(temp.Root, dir));
        Assert.IsTrue(Directory.Exists(dir));
    }

    [TestMethod]
    public void CRUU16_006_IMigrationFileOps_exposes_no_raw_DeleteFile()
    {
        string[] members = typeof(IMigrationFileOps).GetMethods().Select(m => m.Name).ToArray();
        CollectionAssert.DoesNotContain(members, "DeleteFile");
    }

    [TestMethod]
    public void CRUU16_006_IMigrationFileOps_exposes_no_raw_DeleteDirectory()
    {
        string[] members = typeof(IMigrationFileOps).GetMethods().Select(m => m.Name).ToArray();
        CollectionAssert.DoesNotContain(members, "DeleteDirectory");
    }

    [TestMethod]
    public void CRUU16_006_Production_migration_services_contain_no_path_based_destructive_fallback()
    {
        // Raw File.Delete / Directory.Delete in migration production code is the escape hatch
        // the whole ownership model exists to remove; leaving one available is how it comes back.
        string[] files =
        [
            "DataFolderMigrationService.cs",
            "MigrationRecoveryService.cs",
            "IMigrationFileOps.cs",
            "IMigrationManifestFileOps.cs",
            "MigrationManifestRepository.cs",
        ];

        var offenders = new System.Collections.Generic.List<string>();
        foreach (string name in files)
        {
            string source = File.ReadAllText(
                RepositoryTestPaths.RequireFile("src", "PromptHelper", "Services", name));

            if (source.Contains("File.Delete(", StringComparison.Ordinal) ||
                source.Contains("Directory.Delete(", StringComparison.Ordinal))
            {
                offenders.Add(name);
            }
        }

        Assert.AreEqual(0, offenders.Count,
            "Path-based destruction survives in: " + string.Join(", ", offenders));
    }

    // ==========================================
    // CRUU16-007: root binding is a property of every production stage
    // ==========================================

    [TestMethod]
    public void CRUU16_007_WindowsDurableAtomicFileWriter_uses_root_bound_stage()
        => AssertStageIsRootBound(
            "WindowsDurableAtomicFileWriter.cs",
            root =>
            {
                string target = Path.Combine(root, "library.json");
                new WindowsDurableAtomicFileWriter().ReplaceDurable(
                    target, Encoding.UTF8.GetBytes("content"), DurableFileClass.LibraryMetadata);
                Assert.AreEqual("content", File.ReadAllText(target));
            });

    [TestMethod]
    public void CRUU16_007_WindowsDurableSettingsFileWriter_uses_root_bound_stage()
        => AssertStageIsRootBound(
            "WindowsDurableSettingsFileWriter.cs",
            root =>
            {
                string target = Path.Combine(root, "settings.json");
                new WindowsDurableSettingsFileWriter().WriteDurable(target, "{}");
                Assert.AreEqual("{}", File.ReadAllText(target));
            });

    [TestMethod]
    public void CRUU16_007_Migration_payload_stage_is_root_bound_in_real_copy_path()
    {
        using var temp = new TestDirectory();
        using var outside = new TestDirectory();

        IMigrationFileOps ops = new DefaultMigrationFileOps();

        // A stage outside the bound root is refused by the real production entry point.
        string escaping = Path.Combine(outside.Root, "escaping.tmp");
        Assert.ThrowsExactly<InvalidDataException>(() => ops.CreateOwnedStage(temp.Root, escaping));
        Assert.IsFalse(File.Exists(escaping), "A refused stage must not be left behind.");

        using IOwnedFileStage ok = ops.CreateOwnedStage(temp.Root, Path.Combine(temp.Root, "ok.tmp"));
        Assert.IsNotNull(ok.IdentityToken);
    }

    [TestMethod]
    public void CRUU16_007_Migration_manifest_stage_is_root_bound_in_real_write_path()
    {
        using var temp = new TestDirectory();
        using var outside = new TestDirectory();

        IMigrationManifestFileOps ops = new DefaultMigrationManifestFileOps();

        string escaping = Path.Combine(outside.Root, "escaping.tmp");
        Assert.ThrowsExactly<InvalidDataException>(() => ops.CreateOwnedStage(temp.Root, escaping));
        Assert.IsFalse(File.Exists(escaping));
    }

    [TestMethod]
    public void CRUU16_007_Ownership_journal_rewrite_stage_is_root_bound()
    {
        // The rewrite delegates to the audited compare-and-swap, which creates its stage with
        // CreateNewUnderRoot; nothing in the journal reaches for a bare, unbound stage.
        string source = File.ReadAllText(
            RepositoryTestPaths.RequireFile("src", "PromptHelper", "Services", "IOwnedArtifactJournal.cs"));

        Assert.IsFalse(source.Contains("WindowsOwnedDurableStage.CreateNew(", StringComparison.Ordinal),
            "The ledger rewrite must not create an unbound stage.");
        StringAssert.Contains(source, "WindowsAtomicExpectedFileReplacer");
    }

    [TestMethod]
    public void CRUU16_007_Helper_only_CreateNewUnderRoot_test_cannot_satisfy_production_wiring_gate()
    {
        // The gate is structural: no managed persistence path may call the unbound factory.
        // Exercising the helper directly proves the helper works, not that anything uses it.
        string servicesDir = Path.Combine(RepositoryTestPaths.Root, "src", "PromptHelper", "Services");
        var offenders = new System.Collections.Generic.List<string>();

        foreach (string file in Directory.GetFiles(servicesDir, "*.cs"))
        {
            if (Path.GetFileName(file) == "WindowsOwnedDurableStage.cs")
            {
                continue;
            }

            if (File.ReadAllText(file).Contains("WindowsOwnedDurableStage.CreateNew(", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.AreEqual(0, offenders.Count,
            "Unbound stage creation survives in: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// Runs a real writer and proves it staged inside the directory it wrote to, by driving the
    /// production entry point rather than the helper it happens to call.
    /// </summary>
    private static void AssertStageIsRootBound(string sourceFileName, Action<string> write)
    {
        using var temp = new TestDirectory();

        write(temp.Root);

        Assert.AreEqual(0, Directory.GetFiles(temp.Root, ".prompthelper-tmp-*").Length,
            "The stage must be terminal by the time the write returns.");
        Assert.AreEqual(0, Directory.GetFiles(temp.Root, ".*.tmp").Length,
            "No staging residue of any naming convention may survive a successful write.");

        string source = File.ReadAllText(
            RepositoryTestPaths.RequireFile("src", "PromptHelper", "Services", sourceFileName));

        Assert.IsFalse(source.Contains("WindowsOwnedDurableStage.CreateNew(", StringComparison.Ordinal),
            $"{sourceFileName} must not create an unbound stage.");
    }
}
