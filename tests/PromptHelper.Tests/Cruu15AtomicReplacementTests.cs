using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

/// <summary>
/// CRUU15-001 through CRUU15-004: every durable promotion is bound to the object it created,
/// and every expectation-bound replacement holds that expectation until the swap consumes it.
/// </summary>
/// <remarks>
/// The barrier tests here inject at the exact cut the CRUU14 design could not defend — after
/// the expected content has been proven and the replacement staged, immediately before the
/// swap — and they do it against the real production primitive, not a stand-in. A test that
/// changes the file <i>before</i> the CAS call proves nothing about that window.
/// </remarks>
[TestClass]
public sealed class Cruu15AtomicReplacementTests
{
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static LibraryDocument CreateDoc(params PromptRecord[] prompts) =>
        new()
        {
            SchemaVersion = 1,
            Categories = [],
            Prompts = [.. prompts]
        };

    /// <summary>What a concurrent update injected at the pre-swap barrier actually did.</summary>
    private sealed record BarrierOutcome(bool MutationLanded, Exception? MutationRejection, Exception? OperationFailure)
    {
        public bool BarrierFired => MutationLanded || MutationRejection is not null;
    }

    /// <summary>
    /// Runs <paramref name="action"/> with a concurrent mutation injected at the exact cut the
    /// CRUU14 design could not defend: after the expected content has been proven and the
    /// replacement staged, immediately before the swap.
    /// </summary>
    private static BarrierOutcome RunWithBarrierMutation(string barrierTargetSuffix, Action<string> mutate, Action action)
    {
        bool mutationLanded = false;
        Exception? mutationRejection = null;
        Exception? operationFailure = null;

        WindowsAtomicExpectedFileReplacer.PreSwapBarrierForTests = target =>
        {
            if (!target.EndsWith(barrierTargetSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                mutate(target);
                mutationLanded = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                mutationRejection = ex;
            }
        };

        try
        {
            action();
        }
        catch (Exception ex)
        {
            operationFailure = ex;
        }
        finally
        {
            WindowsAtomicExpectedFileReplacer.PreSwapBarrierForTests = null;
        }

        return new BarrierOutcome(mutationLanded, mutationRejection, operationFailure);
    }

    /// <summary>
    /// Asserts the property the barrier exists to establish: a concurrent update attempted at
    /// the last possible moment is never silently lost. Exactly one of two things may happen,
    /// and both are checked.
    /// </summary>
    /// <list type="number">
    /// <item>The update is <b>refused</b>. The expected object is held under an exclusion that
    /// denies in-place writes and rename-replacement for the whole operation, so the concurrent
    /// writer fails at its own call site and knows it. Nothing was written, so nothing was
    /// lost, and our replacement lands.</item>
    /// <item>The update <b>lands</b> (the case that is reachable whenever there is no object to
    /// hold — an expected-missing target). Then the swap must fail closed and leave the
    /// concurrent content exactly as it is.</item>
    /// </list>
    /// <remarks>
    /// The outcome CRUU14 permitted — the update lands and is then overwritten — is what these
    /// assertions exclude.
    /// </remarks>
    private static void AssertConcurrentUpdateWasNotLost(
        BarrierOutcome outcome,
        string targetPath,
        byte[] concurrentContent,
        byte[] ourContent)
    {
        Assert.IsTrue(outcome.BarrierFired,
            "The pre-swap barrier never fired, so this test proved nothing about that window.");

        byte[] onDisk = File.ReadAllBytes(targetPath);

        if (outcome.MutationLanded)
        {
            Assert.IsNotNull(outcome.OperationFailure,
                "A concurrent update that landed at the barrier must make the replacement fail closed.");
            CollectionAssert.AreEqual(concurrentContent, onDisk,
                "The concurrent update landed, so it must survive untouched.");
            return;
        }

        Assert.IsNotNull(outcome.MutationRejection,
            "The concurrent update neither landed nor was refused, so the exclusion was not exercised.");
        Assert.IsNull(outcome.OperationFailure,
            "Nothing changed, so the replacement had no reason to fail: " + outcome.OperationFailure);
        CollectionAssert.AreEqual(ourContent, onDisk,
            "The concurrent update was refused, so our replacement is the one that landed.");
    }

    /// <summary>
    /// A concurrent updater that plays by the rules: it publishes its change by atomically
    /// renaming a fully written file over the target, exactly as another well-behaved writer
    /// would. This is the only kind of concurrent update the authority handle deliberately
    /// permits (it denies in-place writes outright), so it is the one the swap has to detect.
    /// </summary>
    private static void AtomicExternalReplace(string target, byte[] content)
    {
        string scratch = Path.Combine(
            Path.GetDirectoryName(target)!,
            $"external-{Guid.NewGuid():N}.tmp");

        File.WriteAllBytes(scratch, content);
        File.Move(scratch, target, overwrite: true);
    }

    // ==========================================
    // CRUU15-001: migration manifest staging is owned, never path-promoted
    // ==========================================

    [TestMethod]
    public void CRUU15_001_Preexisting_manifest_stage_CreateNew_failure_never_deletes_foreign_file()
    {
        using var temp = new TestDirectory();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");
        Guid attemptId = Guid.NewGuid();

        MigrationAttemptManifest manifest = BuildReadyManifest(temp.Root, attemptId);
        manifest.Phase = MigrationManifestPhase.Copying;
        var repo = new MigrationManifestRepository();
        repo.CreateInitialCopyingManifestDurable(markerPath, manifest);
        manifest.Phase = MigrationManifestPhase.ReadyToCommit;

        // Something already occupies the deterministic stage pathname.
        string stagePath = Path.Combine(temp.Root, $".prompthelper-migration.stage-{attemptId:N}.tmp");
        byte[] foreign = Encoding.UTF8.GetBytes("foreign content that this attempt never created");
        File.WriteAllBytes(stagePath, foreign);

        Assert.ThrowsExactly<IOException>(() => repo.WriteReadyManifestDurable(markerPath, manifest));

        Assert.IsTrue(File.Exists(stagePath), "A pre-existing file at the stage path must never be deleted.");
        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(stagePath));
    }

    [TestMethod]
    public void CRUU15_001_Manifest_stage_replacement_after_flush_before_promotion_is_never_promoted()
    {
        using var temp = new TestDirectory();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");
        Guid attemptId = Guid.NewGuid();
        MigrationAttemptManifest manifest = BuildReadyManifest(temp.Root, attemptId);
        manifest.Phase = MigrationManifestPhase.Copying;
        new MigrationManifestRepository().CreateInitialCopyingManifestDurable(markerPath, manifest);
        manifest.Phase = MigrationManifestPhase.ReadyToCommit;

        string stagePath = Path.Combine(temp.Root, $".prompthelper-migration.stage-{attemptId:N}.tmp");

        // Try to substitute the stage object between the flush and the promotion. The stage is
        // held open exclusively for exactly that window, so this cannot succeed; what matters
        // is that the bytes that reach the marker are the ones this call wrote.
        var ops = new FakeManifestFileOps();
        bool substitutionRejected = false;
        ops.OnReplaceWriteThrough = (src, dst) =>
        {
            try
            {
                File.WriteAllBytes(stagePath, Encoding.UTF8.GetBytes("substituted stage content"));
            }
            catch (IOException)
            {
                substitutionRejected = true;
            }

            throw new SubstitutionAttempted();
        };

        var repo = new MigrationManifestRepository(ops);
        Assert.ThrowsExactly<SubstitutionAttempted>(() => repo.WriteReadyManifestDurable(markerPath, manifest));

        Assert.IsTrue(substitutionRejected,
            "The staged object must stay exclusively owned from flush through promotion.");
    }

    [TestMethod]
    public void CRUU15_001_Ready_marker_bytes_are_revalidated_after_phase_promotion_before_settings_commit()
    {
        using var temp = new TestDirectory();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");
        Guid attemptId = Guid.NewGuid();
        MigrationAttemptManifest manifest = BuildReadyManifest(temp.Root, attemptId);

        var repo = new MigrationManifestRepository();
        repo.WriteReadyManifestDurable(markerPath, manifest);

        // The marker as persisted is accepted...
        repo.AssertPersistedMarkerMatches(markerPath, manifest);

        // ...and a foreign attempt cannot replace it merely by targeting the same pathname.
        MigrationAttemptManifest other = BuildReadyManifest(temp.Root, Guid.NewGuid());
        Assert.ThrowsExactly<StaleExpectedFileException>(() => repo.WriteReadyManifestDurable(markerPath, other));
        repo.AssertPersistedMarkerMatches(markerPath, manifest);
    }

    [TestMethod]
    public void CRUU15_001_Failed_ready_promotion_preserves_foreign_stage_and_copying_marker()
    {
        using var temp = new TestDirectory();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");
        Guid attemptId = Guid.NewGuid();

        MigrationAttemptManifest copying = BuildReadyManifest(temp.Root, attemptId);
        copying.Phase = MigrationManifestPhase.Copying;
        var repo = new MigrationManifestRepository();
        repo.CreateInitialCopyingManifestDurable(markerPath, copying);
        byte[] copyingBytes = File.ReadAllBytes(markerPath);

        string stagePath = Path.Combine(temp.Root, $".prompthelper-migration.stage-{attemptId:N}.tmp");
        byte[] foreign = Encoding.UTF8.GetBytes("foreign stage");
        File.WriteAllBytes(stagePath, foreign);

        MigrationAttemptManifest ready = BuildReadyManifest(temp.Root, attemptId);
        Assert.ThrowsExactly<IOException>(() => repo.WriteReadyManifestDurable(markerPath, ready));

        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(stagePath));
        CollectionAssert.AreEqual(copyingBytes, File.ReadAllBytes(markerPath));
    }

    [TestMethod]
    public void CRUU15_001_Ready_manifest_promotion_uses_owned_handle_not_path_MoveFileEx()
    {
        // The injectable file-ops surface is the enforcement point: if no pathname-addressed
        // promotion or deletion is reachable through it, no implementation behind it can
        // reintroduce the closed-stage/path-rename gap.
        string[] memberNames = typeof(IMigrationManifestFileOps).GetMethods().Select(m => m.Name).ToArray();

        CollectionAssert.DoesNotContain(memberNames, "MoveNoOverwriteWriteThrough");
        CollectionAssert.DoesNotContain(memberNames, "ReplaceWriteThrough");
        CollectionAssert.DoesNotContain(memberNames, "DeleteFile");
        CollectionAssert.Contains(memberNames, "CreateOwnedStage");

        string[] stageMembers = typeof(IOwnedFileStage).GetMethods().Select(m => m.Name).ToArray();
        CollectionAssert.Contains(stageMembers, "PromoteReplaceExact");
        CollectionAssert.Contains(stageMembers, "DeleteExact");

        // And nothing in the production assembly promotes a staging file by pathname any more.
        // WindowsOwnedDurableStage is the sole promotion primitive; a second implementation
        // reintroducing MoveFileExW is exactly how CRUU14 fixed one writer and left three.
        var offenders = new List<string>();
        string servicesDir = Path.Combine(
            RepositoryTestPaths.Root, "src", "PromptHelper", "Services");

        foreach (string file in Directory.GetFiles(servicesDir, "*.cs"))
        {
            if (Path.GetFileName(file) is "WindowsOwnedDurableStage.cs" or "IOwnedFileStage.cs")
            {
                continue;
            }

            // The declaration, not a mention of it in prose: what matters is whether a type
            // can still perform a pathname-addressed rename.
            if (File.ReadAllText(file).Contains("extern bool MoveFileExW", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.AreEqual(0, offenders.Count,
            "Path-based promotion survives in: " + string.Join(", ", offenders));

        // Behavioral proof for the writer that used to close its temp and rename the pathname:
        // the object it promotes is the object it created.
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "written.txt");
        new AtomicTextWriter().Write(target, "content");

        Assert.AreEqual("content", File.ReadAllText(target));
        Assert.AreEqual(0, Directory.GetFiles(temp.Root, "*.tmp").Length,
            "The staging object must be terminal by the time the write returns.");
    }

    // ==========================================
    // CRUU15-002: migration payload staging keeps object ownership to promotion
    // ==========================================

    [TestMethod]
    public void CRUU15_002_Migration_payload_stage_replacement_after_flush_cannot_be_promoted()
    {
        using var temp = new TestDirectory();
        string stagePath = Path.Combine(temp.Root, "payload.stage.tmp");
        string finalPath = Path.Combine(temp.Root, "payload.md");
        byte[] ours = Encoding.UTF8.GetBytes("the bytes this attempt wrote");

        IMigrationFileOps ops = new DefaultMigrationFileOps();
        using (IOwnedFileStage stage = ops.CreateOwnedStage(temp.Root, stagePath))
        {
            stage.Write(ours);
            stage.FlushDurable();

            // A second process attempts to substitute the staged object after the flush.
            Assert.ThrowsExactly<IOException>(
                () => File.WriteAllBytes(stagePath, Encoding.UTF8.GetBytes("substituted")));

            stage.PromoteNoOverwriteExact(finalPath);
        }

        CollectionAssert.AreEqual(ours, File.ReadAllBytes(finalPath));
    }

    [TestMethod]
    public void CRUU15_002_Migration_payload_foreign_same_bytes_does_not_become_attempt_owned_by_path()
    {
        using var temp = new TestDirectory();
        string stagePath = Path.Combine(temp.Root, "payload.stage.tmp");

        // A foreign object occupies the declared stage pathname with byte-identical content.
        File.WriteAllBytes(stagePath, Encoding.UTF8.GetBytes("identical"));

        IMigrationFileOps ops = new DefaultMigrationFileOps();

        // Ownership is never adopted from a pathname: creating the stage fails outright rather
        // than taking over what is already there.
        Assert.ThrowsExactly<IOException>(() => ops.CreateOwnedStage(temp.Root, stagePath));

        // And provenance-bound cleanup refuses to destroy it, identical bytes notwithstanding.
        Assert.AreEqual(
            ArtifactCleanupOutcome.PreservedUnproven,
            ops.DeleteOwnedFileIfProven(temp.Root, stagePath));
        Assert.IsTrue(File.Exists(stagePath));
    }

    [TestMethod]
    public void CRUU15_002_Migration_payload_promotion_is_same_handle_from_create_through_final_name()
    {
        using var temp = new TestDirectory();
        string stagePath = Path.Combine(temp.Root, "payload.stage.tmp");
        string finalPath = Path.Combine(temp.Root, "payload.md");

        IMigrationFileOps ops = new DefaultMigrationFileOps();
        string identityAtCreation;
        using (IOwnedFileStage stage = ops.CreateOwnedStage(temp.Root, stagePath))
        {
            identityAtCreation = stage.IdentityToken;
            stage.Write(Encoding.UTF8.GetBytes("payload"));
            stage.FlushDurable();
            stage.PromoteNoOverwriteExact(finalPath);

            // Same handle, same object, now under the final name.
            Assert.AreEqual(identityAtCreation, stage.IdentityToken);
        }

        using Microsoft.Win32.SafeHandles.SafeFileHandle promoted =
            File.OpenHandle(finalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Assert.AreEqual(identityAtCreation, WindowsFileIdentity.FromHandle(promoted).ToToken());

        // The interface exposes no pathname-addressed promotion at all.
        string[] memberNames = typeof(IMigrationFileOps).GetMethods().Select(m => m.Name).ToArray();
        CollectionAssert.DoesNotContain(memberNames, "MoveNoOverwriteWriteThrough");
    }

    // ==========================================
    // CRUU15-003: the CAS holds its expectation through the write
    // ==========================================

    [TestMethod]
    public void CRUU15_003_Primary_changes_after_CAS_hash_before_atomic_replace_is_preserved()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        var repo = new LibraryRepository(paths, new WindowsDurableAtomicFileWriter());
        repo.Commit(CanonicalLibraryPackage.Create(CreateDoc()));

        string expectedHash = Hash(File.ReadAllBytes(paths.LibraryPath));
        byte[] external = CanonicalLibraryPackage
            .Create(CreateDoc(new PromptRecord { Id = Guid.NewGuid(), Title = "Someone else", CategoryId = null }))
            .CanonicalBytes.ToArray();

        var ours = CanonicalLibraryPackage.Create(
            CreateDoc(new PromptRecord { Id = Guid.NewGuid(), Title = "Ours", CategoryId = null }));

        BarrierOutcome outcome = RunWithBarrierMutation(
            "library.json",
            target => AtomicExternalReplace(target, external),
            () => repo.CommitIfPrimaryUnchanged(ours, expectedHash));

        AssertConcurrentUpdateWasNotLost(
            outcome,
            paths.LibraryPath,
            external,
            ours.CanonicalBytes.ToArray());
    }

    [TestMethod]
    public void CRUU15_003_Category_create_change_at_post_CAS_prewrite_barrier_aborts()
        => AssertLibraryMutationAbortsAtBarrier(service => service.CreateCategory(null, "New Category"));

    [TestMethod]
    public void CRUU15_003_Category_rename_change_at_post_CAS_prewrite_barrier_aborts()
        => AssertLibraryMutationAbortsAtBarrier(
            setup: service => service.CreateCategory(null, "Renamable").Value.Id,
            mutate: (service, categoryId) => service.RenameCategory(categoryId, "Renamed"));

    [TestMethod]
    public void CRUU15_003_Category_delete_change_at_post_CAS_prewrite_barrier_aborts()
        => AssertLibraryMutationAbortsAtBarrier(
            setup: service => service.CreateCategory(null, "Deletable").Value.Id,
            mutate: (service, categoryId) => service.DeleteCategory(categoryId));

    [TestMethod]
    public void CRUU15_003_Move_prompt_change_at_post_CAS_prewrite_barrier_aborts()
        => AssertLibraryMutationAbortsAtBarrier(
            setup: service =>
            {
                Guid categoryId = service.CreateCategory(null, "Destination").Value.Id;
                Guid promptId = service.CreatePrompt(null, "body", "Movable").Value.Id;
                return (promptId, categoryId);
            },
            mutate: (service, ids) => service.MovePrompt(ids.promptId, ids.categoryId));

    [TestMethod]
    public void CRUU15_003_Edit_body_change_at_post_CAS_prewrite_barrier_aborts()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        PromptLibraryService service = BuildService(paths);
        PromptRecord prompt = service.CreatePrompt(null, "original body", "Editable").Value;
        string bodyPath = paths.GetPromptPath(prompt.Id);

        byte[] externalBody = Encoding.UTF8.GetBytes("a concurrent edit of the same prompt");

        BarrierOutcome outcome = RunWithBarrierMutation(
            Path.GetFileName(bodyPath),
            target => AtomicExternalReplace(target, externalBody),
            () => service.EditPrompt(prompt.Id, "our new body", "Editable"));

        AssertConcurrentUpdateWasNotLost(
            outcome,
            bodyPath,
            externalBody,
            Encoding.UTF8.GetBytes("our new body"));
    }

    [TestMethod]
    public void CRUU15_003_Body_only_edit_primary_change_after_last_check_never_gets_overwritten()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        PromptLibraryService service = BuildService(paths);
        PromptRecord prompt = service.CreatePrompt(null, "original body", "Body only").Value;

        // A body-only edit keeps the title, so the serialized library is byte-identical before
        // and after: the primary write is content-neutral and must never flatten a concurrent
        // change to library.json.
        byte[] external = CanonicalLibraryPackage
            .Create(CreateDoc(new PromptRecord { Id = Guid.NewGuid(), Title = "Someone else", CategoryId = null }))
            .CanonicalBytes.ToArray();

        byte[] unchangedLibrary = File.ReadAllBytes(paths.LibraryPath);

        BarrierOutcome outcome = RunWithBarrierMutation(
            "library.json",
            target => AtomicExternalReplace(target, external),
            () => service.EditPrompt(prompt.Id, "a new body only", prompt.Title));

        Assert.IsTrue(outcome.BarrierFired,
            "The pre-swap barrier never fired, so this test proved nothing about that window.");

        if (outcome.MutationLanded)
        {
            CollectionAssert.AreEqual(external, File.ReadAllBytes(paths.LibraryPath),
                "A content-neutral write must never flatten a concurrent change to library.json.");
        }
        else
        {
            // The exclusion refused the concurrent replacement outright, so library.json is
            // byte-identical to what it was — which is exactly what a body-only edit means.
            CollectionAssert.AreEqual(unchangedLibrary, File.ReadAllBytes(paths.LibraryPath));
        }

        // Either way the body edit itself is the real payload and stays committed.
        Assert.AreEqual("a new body only", File.ReadAllText(paths.GetPromptPath(prompt.Id)));
    }

    // ==========================================
    // CRUU15-004: expected-missing and backup preservation are enforced by the swap
    // ==========================================

    [TestMethod]
    public void CRUU15_004_Settings_primary_expected_missing_foreign_create_before_write_is_preserved()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "settings.json");
        byte[] foreign = Encoding.UTF8.GetBytes("{\"created\":\"by someone else\"}");

        var replacer = new WindowsAtomicExpectedFileReplacer();

        BarrierOutcome outcome = RunWithBarrierMutation(
            "settings.json",
            t => File.WriteAllBytes(t, foreign),
            () => replacer.ReplaceIfExpected(
                temp.Root,
                target,
                ExpectedFileState.Missing,
                Encoding.UTF8.GetBytes("{\"ours\":true}"),
                DurableFileClass.Settings));

        // There is no object to hold an exclusion over, so this update genuinely lands — and
        // the no-overwrite promotion is what has to refuse to destroy it.
        Assert.IsTrue(outcome.MutationLanded);
        Assert.IsInstanceOfType<StaleExpectedFileException>(outcome.OperationFailure);
        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(target));
    }

    [TestMethod]
    public void CRUU15_004_Settings_backup_expected_missing_future_schema_create_is_preserved()
    {
        using var temp = new TestDirectory();
        string backup = Path.Combine(temp.Root, "settings.backup.json");
        byte[] futureSchema = Encoding.UTF8.GetBytes("{\"schemaVersion\":999,\"dataRootPath\":\"\"}");

        var replacer = new WindowsAtomicExpectedFileReplacer();

        BarrierOutcome outcome = RunWithBarrierMutation(
            "settings.backup.json",
            t => File.WriteAllBytes(t, futureSchema),
            () => replacer.ReplaceIfExpected(
                temp.Root,
                backup,
                ExpectedFileState.Missing,
                Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"dataRootPath\":\"\"}"),
                DurableFileClass.Settings));

        Assert.IsTrue(outcome.MutationLanded);
        Assert.IsInstanceOfType<StaleExpectedFileException>(outcome.OperationFailure);
        CollectionAssert.AreEqual(futureSchema, File.ReadAllBytes(backup),
            "A newer-schema backup created after the preservation decision must survive it.");
    }

    [TestMethod]
    public void CRUU15_004_Settings_existing_change_at_post_verify_prewrite_barrier_is_preserved()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        SettingsWritePrecondition precondition = repo.CapturePrecondition();

        byte[] external = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"dataRootPath\":\"" +
                                                 temp.Root.Replace("\\", "\\\\") + "\"}");

        BarrierOutcome outcome = RunWithBarrierMutation(
            "settings.json",
            t => AtomicExternalReplace(t, external),
            () => repo.SaveIfUnchanged(new AppSettings { SchemaVersion = 1, DataRootPath = temp.Root }, precondition));

        Assert.IsTrue(outcome.BarrierFired,
            "The pre-swap barrier never fired, so this test proved nothing about that window.");

        if (outcome.MutationLanded)
        {
            Assert.IsNotNull(outcome.OperationFailure);
            CollectionAssert.AreEqual(external, File.ReadAllBytes(settingsPath));
        }
        else
        {
            Assert.IsNotNull(outcome.MutationRejection,
                "A settings write must hold its expected object under exclusion until the swap consumes it.");
        }
    }

    [TestMethod]
    public void CRUU15_004_Library_backup_future_schema_appearing_after_state_read_is_preserved()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        var repo = new LibraryRepository(paths, new WindowsDurableAtomicFileWriter());
        repo.Commit(CanonicalLibraryPackage.Create(CreateDoc()));

        byte[] futureBackup = Encoding.UTF8.GetBytes("{\"schemaVersion\":999,\"categories\":[],\"prompts\":[]}");

        var next = CanonicalLibraryPackage.Create(
            CreateDoc(new PromptRecord { Id = Guid.NewGuid(), Title = "Next", CategoryId = null }));

        CommitResult? result = null;
        BarrierOutcome outcome = RunWithBarrierMutation(
            "library.backup.json",
            t => AtomicExternalReplace(t, futureBackup),
            () => result = repo.Commit(next));

        Assert.IsTrue(outcome.BarrierFired,
            "The pre-swap barrier never fired, so this test proved nothing about that window.");
        Assert.IsNotNull(result);

        if (outcome.MutationLanded)
        {
            Assert.IsFalse(result!.BackupSynchronized,
                "A backup that changed after its state was read must not be reported as synchronized.");
            CollectionAssert.AreEqual(futureBackup, File.ReadAllBytes(paths.LibraryBackupPath),
                "The newer-schema backup that appeared must be preserved, not overwritten.");
        }
        else
        {
            Assert.IsNotNull(outcome.MutationRejection,
                "The backup must be held under exclusion from its state read until the swap consumes it.");
            CollectionAssert.AreEqual(next.CanonicalBytes.ToArray(), File.ReadAllBytes(paths.LibraryBackupPath));
        }
    }

    [TestMethod]
    public void CRUU15_004_Data_folder_settings_point_of_no_return_has_atomic_expected_state()
    {
        // The settings commit must express its precondition as an ExpectedFileState that the
        // replacement itself enforces, covering both "was present with these bytes" and "was
        // missing" — the latter is the case an earlier File.Exists could never enforce.
        Assert.AreEqual(ExpectedFileStateKind.Missing, ExpectedFileState.Missing.Kind);
        Assert.IsNull(ExpectedFileState.Missing.ExpectedSha256Hex);

        ExpectedFileState present = ExpectedFileState.Present(new string('a', 64));
        Assert.AreEqual(ExpectedFileStateKind.Present, present.Kind);
        Assert.IsNotNull(present.ExpectedSha256Hex);

        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "settings.json");
        var replacer = new WindowsAtomicExpectedFileReplacer();

        // ExpectedMissing over an existing file fails closed rather than replacing it.
        byte[] existing = Encoding.UTF8.GetBytes("already here");
        File.WriteAllBytes(target, existing);

        Assert.ThrowsExactly<StaleExpectedFileException>(() => replacer.ReplaceIfExpected(
            temp.Root,
            target,
            ExpectedFileState.Missing,
            Encoding.UTF8.GetBytes("ours"),
            DurableFileClass.Settings));

        CollectionAssert.AreEqual(existing, File.ReadAllBytes(target));
    }

    // ==========================================
    // Helpers
    // ==========================================

    private sealed class SubstitutionAttempted : Exception;

    private static PromptLibraryService BuildService(AppPaths paths)
    {
        var writer = new WindowsDurableAtomicFileWriter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, new FileDeleter());
        var doc = new LibraryDocument();
        libRepo.Commit(doc);
        return new PromptLibraryService(doc, libRepo, promptRepo);
    }

    private static void AssertLibraryMutationAbortsAtBarrier(Action<PromptLibraryService> mutate)
        => AssertLibraryMutationAbortsAtBarrier<object?>(_ => null, (service, _) => mutate(service));

    private static void AssertLibraryMutationAbortsAtBarrier<TState>(
        Func<PromptLibraryService, TState> setup,
        Action<PromptLibraryService, TState> mutate)
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        PromptLibraryService service = BuildService(paths);
        TState state = setup(service);

        byte[] external = CanonicalLibraryPackage
            .Create(CreateDoc(new PromptRecord { Id = Guid.NewGuid(), Title = "Someone else", CategoryId = null }))
            .CanonicalBytes.ToArray();

        byte[] before = File.ReadAllBytes(paths.LibraryPath);

        BarrierOutcome outcome = RunWithBarrierMutation(
            "library.json",
            target => AtomicExternalReplace(target, external),
            () => mutate(service, state));

        Assert.IsTrue(outcome.BarrierFired,
            "The pre-swap barrier never fired, so this test proved nothing about that window.");

        if (outcome.MutationLanded)
        {
            Assert.IsNotNull(outcome.OperationFailure,
                "A concurrent update that landed at the barrier must abort the mutation.");
            CollectionAssert.AreEqual(external, File.ReadAllBytes(paths.LibraryPath),
                "The concurrent update must survive the aborted mutation.");
        }
        else
        {
            Assert.IsNotNull(outcome.MutationRejection,
                "library.json must stay under exclusion from the expectation check until the swap consumes it.");
            CollectionAssert.AreNotEqual(external, File.ReadAllBytes(paths.LibraryPath),
                "A refused update must not have reached disk.");
            Assert.IsNull(outcome.OperationFailure,
                "Nothing changed, so the mutation had no reason to fail: " + outcome.OperationFailure);
            CollectionAssert.AreNotEqual(before, File.ReadAllBytes(paths.LibraryPath),
                "The mutation itself must still have been committed.");
        }
    }

    private static MigrationAttemptManifest BuildReadyManifest(string root, Guid attemptId) =>
        new()
        {
            SchemaVersion = MigrationAttemptManifest.CurrentSchemaVersion,
            AttemptId = attemptId,
            SourcePhysicalRoot = Path.Combine(root, "source"),
            TargetPhysicalRoot = root,
            SourceLibrarySha256Hex = new string('0', 64),
            SourcePayloadFingerprintSha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.ReadyToCommit,
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
}
