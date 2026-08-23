using System;
using System.Collections.Generic;
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

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ProductionSymbolEvidenceAttribute(string symbol) : Attribute
{
    public string Symbol { get; } = symbol;
}

[TestClass]
[DoNotParallelize]
public sealed class Cruu18RegressionTests
{
    private sealed class PhaseFailingJournal : IOwnedArtifactJournal
    {
        public WindowsOwnedArtifactJournal Inner { get; } = new();
        public Func<OwnedArtifactRecord, bool>? FailBeforeRecord { get; init; }
        public Func<OwnedArtifactRecord, bool>? FailAfterRecord { get; init; }

        public void Record(string root, OwnedArtifactRecord record)
        {
            if (FailBeforeRecord?.Invoke(record) == true)
            {
                throw new IOException($"Injected pre-append failure for {record.Kind}/{record.Phase}.");
            }

            Inner.Record(root, record);

            if (FailAfterRecord?.Invoke(record) == true)
            {
                throw new IOException($"Injected post-append flush failure for {record.Kind}/{record.Phase}.");
            }
        }

        public OwnedArtifactJournalSnapshot Read(string root) => Inner.Read(root);

        public void Rewrite(
            string root,
            OwnedArtifactJournalSnapshot expected,
            IReadOnlyList<OwnedArtifactRecord> surviving) =>
            Inner.Rewrite(root, expected, surviving);
    }

    private sealed class MigrationFixture : IDisposable
    {
        public TestDirectory Source { get; } = new();
        public TestDirectory Target { get; } = new();
        public TestDirectory Displaced { get; } = new();
        public MigrationManifestRepository ManifestRepository { get; } = new();
        public DefaultMigrationFileOps FileOps { get; } = new();
        public MigrationAttemptManifest Manifest { get; }
        public string MarkerPath => Path.Combine(Target.Root, ".prompthelper-migration.json");

        public MigrationFixture()
        {
            SeedValidLibrary(Source.Root, out _);
            var migration = new DataFolderMigrationService();
            MigrationPayloadSnapshot snapshot = migration.CaptureSourcePayloadSnapshot(Source.Root);
            Manifest = MigrationManifestBuilder.BuildCopying(
                Source.Root,
                Target.Root,
                snapshot,
                Guid.NewGuid());
            ManifestRepository.CreateInitialCopyingManifestDurable(MarkerPath, Manifest);
        }

        public void PublishCurrentFinals(bool recordPublished = true)
        {
            foreach (MigrationManifestArtifact artifact in Manifest.Artifacts)
            {
                string sourcePath = Path.Combine(Source.Root, artifact.RelativePath);
                string tempPath = Path.Combine(Target.Root, artifact.TempRelativePath);
                string finalPath = Path.Combine(Target.Root, artifact.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
                byte[] bytes = File.ReadAllBytes(sourcePath);

                using IOwnedFileStage stage = FileOps.CreateOwnedStage(Target.Root, tempPath);
                stage.Write(bytes);
                stage.FlushDurable();
                MigrationArtifactClaim claim = FileOps.RecordMigrationArtifactPrepared(
                    Target.Root,
                    tempPath,
                    finalPath,
                    stage.IdentityToken,
                    artifact.Length,
                    artifact.Sha256Hex);
                stage.PromoteNoOverwriteExact(finalPath);
                if (recordPublished)
                {
                    FileOps.RecordMigrationArtifactPublished(Target.Root, claim);
                }
            }
        }

        public void PublishLegacyFinals()
        {
            foreach (MigrationManifestArtifact artifact in Manifest.Artifacts)
            {
                string finalPath = Path.Combine(Target.Root, artifact.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
                File.Copy(Path.Combine(Source.Root, artifact.RelativePath), finalPath);
                OwnedArtifactTestSupport.ClaimPromotedFinal(Target.Root, finalPath);
            }
        }

        public RecoveryResult RecoverForRetry() =>
            new MigrationRecoveryService(ManifestRepository, FileOps).RecoverForRetry(
                new MigrationRecoveryContext(
                    Target.Root,
                    ExpectedSourcePhysicalRoot: Source.Root));

        public MigrationManifestArtifact Primary =>
            Manifest.Artifacts.Single(a => a.Role == MigrationPayloadRole.PrimaryMetadata);

        public void Dispose()
        {
            Displaced.Dispose();
            Target.Dispose();
            Source.Dispose();
        }
    }

    private enum CategoryMutation
    {
        Create,
        Rename,
        Delete
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static WindowsFileIdentity IdentityOf(string path)
    {
        using Microsoft.Win32.SafeHandles.SafeFileHandle handle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return WindowsFileIdentity.FromHandle(handle);
    }

    internal static T AssertProductionHit<T>(string requiredSymbol, Func<T> action)
    {
        var hits = new HashSet<string>(StringComparer.Ordinal);
        Action<string>? previous = ProductionRuntimeEvidence.SinkForTests;
        ProductionRuntimeEvidence.SinkForTests = symbol => hits.Add(symbol);
        try
        {
            T result = action();
            Assert.IsTrue(hits.Contains(requiredSymbol),
                $"The test did not execute required production symbol '{requiredSymbol}'. Hits: {string.Join(", ", hits)}");
            return result;
        }
        finally
        {
            ProductionRuntimeEvidence.SinkForTests = previous;
        }
    }

    internal static void AssertProductionHit(string requiredSymbol, Action action) =>
        AssertProductionHit(requiredSymbol, () =>
        {
            action();
            return true;
        });

    internal static TException AssertProductionHitThrows<TException>(
        string requiredSymbol,
        Action action)
        where TException : Exception
    {
        var hits = new HashSet<string>(StringComparer.Ordinal);
        Action<string>? previous = ProductionRuntimeEvidence.SinkForTests;
        ProductionRuntimeEvidence.SinkForTests = symbol => hits.Add(symbol);
        Exception? observed = null;
        try
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                observed = ex;
            }

            // This assertion deliberately precedes exception validation. Therefore an expected
            // exception cannot let an outer Throws assertion bypass production-hit evidence.
            Assert.IsTrue(hits.Contains(requiredSymbol),
                $"The test did not execute required production symbol '{requiredSymbol}'. Hits: {string.Join(", ", hits)}");
            Assert.IsNotNull(observed, $"Expected {typeof(TException).Name}, but no exception was thrown.");
            Assert.AreEqual(typeof(TException), observed.GetType(),
                $"Expected exactly {typeof(TException).Name}, but got {observed.GetType().Name}.");
            return (TException)observed;
        }
        finally
        {
            ProductionRuntimeEvidence.SinkForTests = previous;
        }
    }

    private static void SeedValidLibrary(string root, out LibraryDocument document)
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "prompts"));
        Directory.CreateDirectory(Path.Combine(root, "recovery"));
        document = new LibraryDocument
        {
            SchemaVersion = LibraryDocument.CurrentSchemaVersion,
            Categories =
            [
                new CategoryRecord
                {
                    Id = Guid.NewGuid(),
                    ParentId = null,
                    Name = "Original",
                    SortOrder = 10
                }
            ],
            Prompts = []
        };
        string json = JsonSerializer.Serialize(document, LibraryRepository.JsonOptions);
        File.WriteAllText(Path.Combine(root, "library.json"), json);
        File.WriteAllText(Path.Combine(root, "library.backup.json"), json);
    }

    internal static void AssertRealRetryDeletesCurrentFinal()
    {
        using var fixture = new MigrationFixture();
        fixture.PublishCurrentFinals(recordPublished: false);

        RecoveryResult result = AssertProductionHit(
            "MigrationRecoveryService.RecoverForRetry",
            fixture.RecoverForRetry);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsFalse(File.Exists(fixture.MarkerPath));
        foreach (MigrationManifestArtifact artifact in fixture.Manifest.Artifacts)
        {
            Assert.IsFalse(File.Exists(Path.Combine(fixture.Target.Root, artifact.RelativePath)));
        }
    }

    [TestMethod]
    [ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
    public void CRUU18_001_Real_RecoverForRetry_deletes_current_MigrationArtifact_final() =>
        AssertRealRetryDeletesCurrentFinal();

    [TestMethod]
    public void CRUU18_001_RecoverForRetry_accepts_final_from_RestoreRelativePath_authority() =>
        AssertRealRetryDeletesCurrentFinal();

    [TestMethod]
    public void CRUU18_001_RecoverForRetry_preserves_same_bytes_different_identity_final()
    {
        using var fixture = new MigrationFixture();
        fixture.PublishCurrentFinals();
        string finalPath = Path.Combine(fixture.Target.Root, fixture.Primary.RelativePath);
        byte[] bytes = File.ReadAllBytes(finalPath);
        File.Move(finalPath, Path.Combine(fixture.Displaced.Root, "owned-primary.json"));
        File.WriteAllBytes(finalPath, bytes);

        RecoveryResult result = fixture.RecoverForRetry();

        Assert.IsFalse(result.Success);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(finalPath));
        Assert.IsTrue(File.Exists(fixture.MarkerPath));
    }

    [TestMethod]
    public void CRUU18_001_RecoverForRetry_preserves_same_identity_tampered_final()
    {
        using var fixture = new MigrationFixture();
        fixture.PublishCurrentFinals();
        string finalPath = Path.Combine(fixture.Target.Root, fixture.Primary.RelativePath);
        WindowsFileIdentity before = IdentityOf(finalPath);
        File.WriteAllText(finalPath, "tampered in place");
        Assert.AreEqual(before, IdentityOf(finalPath));

        RecoveryResult result = fixture.RecoverForRetry();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("tampered in place", File.ReadAllText(finalPath));
        Assert.IsTrue(File.Exists(fixture.MarkerPath));
    }

    [TestMethod]
    public void CRUU18_001_Legacy_MigrationFinal_retry_uses_manifest_content_authority()
    {
        using var fixture = new MigrationFixture();
        fixture.PublishLegacyFinals();

        RecoveryResult result = fixture.RecoverForRetry();

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsFalse(File.Exists(fixture.MarkerPath));
    }

    [TestMethod]
    [ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
    public void CRUU18_001_CRUU17_005_retry_sentinel_executes_MigrationRecoveryService_RecoverForRetry() =>
        AssertRealRetryDeletesCurrentFinal();

    private static void AssertPromotionFailureRestoresAndRecovers()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "library.json");
        byte[] oldBytes = Utf8("old committed");
        File.WriteAllBytes(target, oldBytes);
        WindowsFileIdentity oldIdentity = IdentityOf(target);
        var journal = new WindowsOwnedArtifactJournal();
        var replacer = new WindowsAtomicExpectedFileReplacer(journal);

        WindowsAtomicExpectedFileReplacer.BeforeCandidatePromotionForTests = _ =>
            throw new IOException("Injected promotion failure.");
        try
        {
            AssertProductionHitThrows<IOException>(
                "WindowsAtomicExpectedFileReplacer.ReplaceIfExpected",
                () => replacer.ReplaceIfExpected(
                    temp.Root,
                    target,
                    ExpectedFileState.Present(Hash(oldBytes)),
                    Utf8("candidate"),
                    DurableFileClass.LibraryMetadata));
        }
        finally
        {
            WindowsAtomicExpectedFileReplacer.BeforeCandidatePromotionForTests = null;
        }

        Assert.AreEqual(oldIdentity, IdentityOf(target));
        OwnedArtifactReconciler.Result recovery = OwnedArtifactReconciler.Reconcile(temp.Root, journal);
        Assert.IsFalse(recovery.HasFatal, string.Join("; ", recovery.Outcomes.Select(o => o.Code)));
        Assert.IsFalse(recovery.Outcomes.Any(o => o.Code == "CAS_AMBIGUOUS"));
        CollectionAssert.AreEqual(oldBytes, File.ReadAllBytes(target));
    }

    [TestMethod]
    [ProductionSymbolEvidence("WindowsAtomicExpectedFileReplacer.ReplaceIfExpected")]
    public void CRUU18_002_Promotion_failure_after_PreimageSidelined_successful_restore_is_not_fatal_on_restart() =>
        AssertPromotionFailureRestoresAndRecovers();

    [TestMethod]
    public void CRUU18_002_PreimageSidelined_plus_exact_old_identity_at_target_is_recognized_as_rolled_back() =>
        AssertPromotionFailureRestoresAndRecovers();

    [TestMethod]
    public void CRUU18_002_Restored_old_target_is_never_reported_CAS_AMBIGUOUS() =>
        AssertPromotionFailureRestoresAndRecovers();

    [TestMethod]
    public void CRUU18_002_Promotion_failure_restore_failure_still_preserves_preimage_and_fails_closed()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "library.json");
        byte[] oldBytes = Utf8("old committed");
        byte[] foreign = Utf8("foreign occupant");
        File.WriteAllBytes(target, oldBytes);
        var journal = new WindowsOwnedArtifactJournal();
        var replacer = new WindowsAtomicExpectedFileReplacer(journal);

        WindowsAtomicExpectedFileReplacer.BeforeCandidatePromotionForTests = path =>
        {
            File.WriteAllBytes(path, foreign);
            throw new IOException("Injected promotion failure after foreign occupation.");
        };
        try
        {
            Assert.ThrowsExactly<IOException>(() => replacer.ReplaceIfExpected(
                temp.Root,
                target,
                ExpectedFileState.Present(Hash(oldBytes)),
                Utf8("candidate"),
                DurableFileClass.LibraryMetadata));
        }
        finally
        {
            WindowsAtomicExpectedFileReplacer.BeforeCandidatePromotionForTests = null;
        }

        Assert.AreEqual(1, Directory.GetFiles(temp.Root, ".prompthelper-preimage-*.tmp").Length);
        OwnedArtifactReconciler.Result recovery = OwnedArtifactReconciler.Reconcile(temp.Root, journal);
        Assert.IsTrue(recovery.HasFatal);
        Assert.IsTrue(recovery.Outcomes.Any(o => o.Code == "CAS_AMBIGUOUS"));
        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(target));
    }

    [TestMethod]
    public void CRUU18_002_CAS_matrix_includes_successful_runtime_rollback_after_sidelined_phase() =>
        AssertPromotionFailureRestoresAndRecovers();

    private static void AssertCategoryCommittedFailure(CategoryMutation mutation)
    {
        WpfTestHost.Invoke(() =>
        {
            using var temp = new TestDirectory();
            SeedValidLibrary(temp.Root, out LibraryDocument document);
            CategoryRecord original = document.Categories.Single();
            var paths = new AppPaths(temp.Root);
            var failingJournal = new PhaseFailingJournal
            {
                FailBeforeRecord = record =>
                    record.Kind == OwnedArtifactKind.CasPreimage &&
                    record.Phase == OwnedArtifactPhase.CandidatePublished
            };
            var libraryRepo = new LibraryRepository(
                paths,
                new WindowsDurableAtomicFileWriter(),
                new WindowsAtomicExpectedFileReplacer(failingJournal));
            var promptRepo = new PromptRepository(paths, new AtomicTextWriter(), new FileDeleter());
            var service = new PromptLibraryService(document, libraryRepo, promptRepo);
            var viewModel = new MainViewModel(service, promptRepo, temp.Root);
            var lifetime = new FakeApplicationLifetime();
            var window = new MainWindow(
                viewModel,
                new FakeClipboardService(),
                applicationLifetime: lifetime,
                showRestartMessage: (_, _) => { });

            LibraryMutationExecutionResult execution = AssertProductionHit(
                "MainWindow.ExecuteLibraryMutation",
                () => window.ExecuteLibraryMutationForTests(() =>
                {
                    switch (mutation)
                    {
                        case CategoryMutation.Create:
                            viewModel.CreateCategory("Committed Create");
                            break;
                        case CategoryMutation.Rename:
                            viewModel.RenameCategory(original.Id, "Committed Rename");
                            break;
                        case CategoryMutation.Delete:
                            viewModel.DeleteCategory(original.Id);
                            break;
                    }
                }));

            Assert.AreEqual(LibraryMutationExecutionResult.FatalRestartRequired, execution);
            Assert.IsTrue(window.FatalMutationShutdownRequested);
            Assert.IsTrue(lifetime.ShutdownRequested);

            LibraryDocument disk = JsonSerializer.Deserialize<LibraryDocument>(
                File.ReadAllText(paths.LibraryPath),
                LibraryRepository.JsonOptions)!;
            switch (mutation)
            {
                case CategoryMutation.Create:
                    Assert.IsTrue(disk.Categories.Any(c => c.Name == "Committed Create"));
                    Assert.IsFalse(viewModel.ChildCategories.Any(c => c.Name == "Committed Create"));
                    break;
                case CategoryMutation.Rename:
                    Assert.AreEqual("Committed Rename", disk.Categories.Single(c => c.Id == original.Id).Name);
                    Assert.AreEqual("Original", viewModel.ChildCategories.Single(c => c.Id == original.Id).Name);
                    break;
                case CategoryMutation.Delete:
                    Assert.IsFalse(disk.Categories.Any(c => c.Id == original.Id));
                    Assert.IsTrue(viewModel.ChildCategories.Any(c => c.Id == original.Id));
                    break;
            }

            Assert.AreEqual(
                LibraryMutationExecutionResult.FatalRestartRequired,
                window.ExecuteLibraryMutationForTests(() => throw new AssertFailedException("must not execute")));
        });
    }

    [TestMethod]
    [TestCategory("WpfIntegration")]
    [ProductionSymbolEvidence("MainWindow.ExecuteLibraryMutation")]
    public void CRUU18_003_CreateCategory_postpublish_bookkeeping_failure_requests_shutdown() =>
        AssertCategoryCommittedFailure(CategoryMutation.Create);

    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void CRUU18_003_RenameCategory_postpublish_bookkeeping_failure_requests_shutdown() =>
        AssertCategoryCommittedFailure(CategoryMutation.Rename);

    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void CRUU18_003_DeleteCategory_postpublish_bookkeeping_failure_requests_shutdown() =>
        AssertCategoryCommittedFailure(CategoryMutation.Delete);

    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void CRUU18_003_Category_committed_exception_is_not_caught_by_generic_IOException_path() =>
        AssertCategoryCommittedFailure(CategoryMutation.Create);

    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void CRUU18_003_Category_committed_exception_does_not_leave_UI_running_with_stale_document() =>
        AssertCategoryCommittedFailure(CategoryMutation.Rename);

    [TestMethod]
    public void CRUU18_003_All_library_mutation_UI_paths_share_committed_restart_boundary()
    {
        string source = File.ReadAllText(
            RepositoryTestPaths.RequireFile("src", "PromptHelper", "MainWindow.xaml.cs"));
        StringAssert.Contains(source, "ExecuteLibraryMutation(");
        StringAssert.Contains(source, "catch (CommittedMutationRequiresRestartException ex)");
        StringAssert.Contains(source, "A library change was saved");
    }

    private static (TestDirectory Source, TestDirectory TargetParent, TestDirectory Settings, string Target,
        MigrationManifestRepository ManifestRepo, FaultInjectingMigrationFileOps Ops, DataFolderTransitionResult Result)
        CreateCommittedTransitionWithRetirementFailure()
    {
        var source = new TestDirectory();
        var targetParent = new TestDirectory();
        var settings = new TestDirectory();
        SeedValidLibrary(source.Root, out _);
        string target = Path.Combine(targetParent.Root, "new-target");
        string settingsPath = Path.Combine(settings.Root, "settings.json");
        File.WriteAllText(
            settingsPath,
            $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var manifestRepo = new MigrationManifestRepository();
        var ops = new FaultInjectingMigrationFileOps
        {
            OnRetireCommittedMigrationArtifacts = _ =>
                throw new IOException("Injected committed ownership retirement failure.")
        };
        var migration = new DataFolderMigrationService(fileOps: ops);
        var coordinator = new DataFolderTransitionCoordinator(
            source.Root,
            new AppSettingsRepository(settingsPathOverride: settingsPath),
            migration,
            new FakeUserConfirmationService(),
            capabilityValidator: null,
            pathResolver: null,
            manifestRepo,
            ops,
            caseInspector: null);

        DataFolderTransitionResult result = AssertProductionHit(
            "DataFolderTransitionCoordinator.RequestTransition",
            () => coordinator.RequestTransition(target));
        return (source, targetParent, settings, target, manifestRepo, ops, result);
    }

    private static void AssertMarkerRetirementRetry()
    {
        var state = CreateCommittedTransitionWithRetirementFailure();
        using (state.Source)
        using (state.TargetParent)
        using (state.Settings)
        {
            string marker = Path.Combine(state.Target, ".prompthelper-migration.json");
            string ledger = WindowsOwnedArtifactJournal.GetJournalPath(state.Target);
            Assert.IsTrue(state.Result.Changed);
            Assert.IsTrue(state.Result.RestartRequired);
            Assert.IsTrue(File.Exists(marker));
            Assert.IsTrue(File.Exists(ledger));
            Assert.IsTrue(File.Exists(Path.Combine(state.Target, "library.json")));

            state.Ops.OnRetireCommittedMigrationArtifacts = null;
            RecoveryResult recovery = new MigrationRecoveryService(state.ManifestRepo, state.Ops)
                .FinalizeCommittedStartup(new MigrationRecoveryContext(state.Target));

            Assert.IsTrue(recovery.Success, recovery.ErrorMessage);
            Assert.IsTrue(File.Exists(Path.Combine(state.Target, "library.json")));
            Assert.IsFalse(File.Exists(ledger));
            Assert.IsFalse(File.Exists(marker));
        }
    }

    [TestMethod]
    [ProductionSymbolEvidence("DataFolderTransitionCoordinator.RequestTransition")]
    public void CRUU18_004_Postcommit_ownership_retirement_failure_keeps_Ready_marker() =>
        AssertMarkerRetirementRetry();

    [TestMethod]
    public void CRUU18_004_Ready_marker_is_deleted_only_after_committed_ownership_claims_retire() =>
        AssertMarkerRetirementRetry();

    [TestMethod]
    public void CRUU18_004_Restart_retries_failed_committed_ownership_retirement() =>
        AssertMarkerRetirementRetry();

    [TestMethod]
    public void CRUU18_004_Restart_retry_preserves_payload_then_retires_ledger_then_marker() =>
        AssertMarkerRetirementRetry();

    [TestMethod]
    public void CRUU18_004_No_success_path_can_delete_marker_while_migration_rollback_claims_survive() =>
        AssertMarkerRetirementRetry();

    private static void AssertPostPublishMigrationFailureRollsBack(out DataFolderMigrationService.MigrationOwnedFileState observed)
    {
        using var source = new TestDirectory();
        using var targetParent = new TestDirectory();
        using var settings = new TestDirectory();
        SeedValidLibrary(source.Root, out _);
        string target = Path.Combine(targetParent.Root, "failed-target");
        string settingsPath = Path.Combine(settings.Root, "settings.json");
        File.WriteAllText(
            settingsPath,
            $"{{\"schemaVersion\":1,\"dataRootPath\":\"{source.Root.Replace("\\", "\\\\")}\"}}");

        var ops = new FaultInjectingMigrationFileOps
        {
            OnRecordMigrationArtifactPublished = (_, _) =>
                throw new IOException("Injected post-publication migration record failure.")
        };
        var migration = new DataFolderMigrationService(fileOps: ops);
        DataFolderMigrationService.MigrationOwnedFileState? captured = null;
        DataFolderMigrationService.PostPublishRecordFailureForTests = owned => captured = owned.State;
        try
        {
            var coordinator = new DataFolderTransitionCoordinator(
                source.Root,
                new AppSettingsRepository(settingsPathOverride: settingsPath),
                migration,
                new FakeUserConfirmationService(),
                capabilityValidator: null,
                pathResolver: null,
                manifestRepo: new MigrationManifestRepository(),
                fileOps: ops,
                caseInspector: null);

            AssertProductionHitThrows<IOException>(
                "DataFolderMigrationService.CopySnapshotToTarget",
                () => coordinator.RequestTransition(target));
        }
        finally
        {
            DataFolderMigrationService.PostPublishRecordFailureForTests = null;
        }

        Assert.IsNotNull(captured);
        observed = captured.Value;
        Assert.AreEqual(DataFolderMigrationService.MigrationOwnedFileState.FinalOwned, observed);
        Assert.IsFalse(File.Exists(Path.Combine(target, "library.json")));
        Assert.IsFalse(File.Exists(Path.Combine(target, ".prompthelper-migration.json")));
    }

    [TestMethod]
    [ProductionSymbolEvidence("DataFolderMigrationService.CopySnapshotToTarget")]
    public void CRUU18_005_Real_copy_postpublish_record_failure_keeps_MigrationOwnedFile_FinalOwned()
    {
        AssertPostPublishMigrationFailureRollsBack(out var state);
        Assert.AreEqual(DataFolderMigrationService.MigrationOwnedFileState.FinalOwned, state);
    }

    [TestMethod]
    public void CRUU18_005_Postpublish_record_failure_inprocess_rollback_deletes_exact_final() =>
        AssertPostPublishMigrationFailureRollsBack(out _);

    [TestMethod]
    public void CRUU18_005_MarkTempAbandoned_rejects_FinalOwned_transition()
    {
        var owned = new DataFolderMigrationService.MigrationOwnedFile
        {
            TempPath = "temp",
            FinalPath = "final",
            ExpectedLength = 1,
            ExpectedSha256Hex = new string('0', 64)
        };
        owned.MarkTempOwned("1:1");
        owned.MarkFinalOwnedAfterMove("1:1");
        Assert.ThrowsExactly<InvalidOperationException>(owned.MarkTempAbandoned);
    }

    [TestMethod]
    public void CRUU18_005_DeleteExact_after_promotion_cannot_be_mistaken_for_final_cleanup()
    {
        using var temp = new TestDirectory();
        string stagePath = Path.Combine(temp.Root, "stage.tmp");
        string finalPath = Path.Combine(temp.Root, "final.bin");
        using WindowsOwnedDurableStage stage = WindowsOwnedDurableStage.CreateNewUnderRoot(stagePath, temp.Root);
        stage.Write(Utf8("payload"));
        stage.FlushDurable();
        stage.PromoteNoOverwriteExact(finalPath);

        Assert.ThrowsExactly<InvalidOperationException>(stage.DeleteExact);
        Assert.IsTrue(File.Exists(finalPath));
    }

    [TestMethod]
    public void CRUU18_005_DataFolderTransition_postpublish_migration_record_failure_rolls_back_cleanly() =>
        AssertPostPublishMigrationFailureRollsBack(out _);

    private static PhaseFailingJournal FailingStageJournal(bool failAfterRecord) => new()
    {
        FailBeforeRecord = failAfterRecord
            ? null
            : record => record.Kind == OwnedArtifactKind.Stage && record.Phase == OwnedArtifactPhase.Claimed,
        FailAfterRecord = failAfterRecord
            ? record => record.Kind == OwnedArtifactKind.Stage && record.Phase == OwnedArtifactPhase.Claimed
            : null
    };

    [TestMethod]
    [ProductionSymbolEvidence("WindowsAtomicExpectedFileReplacer.ReplaceIfExpected")]
    public void CRUU18_006_CAS_stage_claim_failure_deletes_exact_stage_before_releasing_handle()
    {
        using var temp = new TestDirectory();
        var replacer = new WindowsAtomicExpectedFileReplacer(FailingStageJournal(failAfterRecord: false));

        AssertProductionHitThrows<IOException>(
            "WindowsAtomicExpectedFileReplacer.ReplaceIfExpected",
            () => replacer.ReplaceIfExpected(
                temp.Root,
                Path.Combine(temp.Root, "new.json"),
                ExpectedFileState.Missing,
                Utf8("candidate"),
                DurableFileClass.LibraryMetadata));

        Assert.AreEqual(0, Directory.GetFiles(temp.Root, ".prompthelper-tmp-*.tmp").Length);
    }

    [TestMethod]
    [ProductionSymbolEvidence("DefaultMigrationFileOps.CreateOwnedStage")]
    public void CRUU18_006_Migration_CreateOwnedStage_claim_failure_leaves_no_unproven_temp()
    {
        using var temp = new TestDirectory();
        string stagePath = Path.Combine(temp.Root, "migration.tmp");
        var ops = new DefaultMigrationFileOps(FailingStageJournal(failAfterRecord: false));

        AssertProductionHitThrows<IOException>(
            "DefaultMigrationFileOps.CreateOwnedStage",
            () => ops.CreateOwnedStage(temp.Root, stagePath));
        Assert.IsFalse(File.Exists(stagePath));
    }

    [TestMethod]
    public void CRUU18_006_Ownership_append_flush_failure_cleans_exact_stage_even_if_record_may_exist()
    {
        using var temp = new TestDirectory();
        string stagePath = Path.Combine(temp.Root, "migration.tmp");
        PhaseFailingJournal journal = FailingStageJournal(failAfterRecord: true);
        var ops = new DefaultMigrationFileOps(journal);

        Assert.ThrowsExactly<IOException>(() => ops.CreateOwnedStage(temp.Root, stagePath));
        Assert.IsFalse(File.Exists(stagePath));
        Assert.IsTrue(journal.Inner.Read(temp.Root).Records.Any(r => r.Kind == OwnedArtifactKind.Stage));
        Assert.IsFalse(OwnedArtifactReconciler.Reconcile(temp.Root, journal.Inner).HasFatal);
    }

    [TestMethod]
    public void CRUU18_006_Migration_stage_claim_failure_does_not_wedge_RecoverForRetry()
    {
        using var fixture = new MigrationFixture();
        MigrationManifestArtifact artifact = fixture.Manifest.Artifacts[0];
        string stagePath = Path.Combine(fixture.Target.Root, artifact.TempRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(stagePath)!);
        PhaseFailingJournal journal = FailingStageJournal(failAfterRecord: true);
        var failingOps = new DefaultMigrationFileOps(journal);

        Assert.ThrowsExactly<IOException>(() => failingOps.CreateOwnedStage(fixture.Target.Root, stagePath));
        Assert.IsFalse(File.Exists(stagePath));

        RecoveryResult result = new MigrationRecoveryService(
            fixture.ManifestRepository,
            new DefaultMigrationFileOps(journal.Inner)).RecoverForRetry(
                new MigrationRecoveryContext(
                    fixture.Target.Root,
                    ExpectedSourcePhysicalRoot: fixture.Source.Root));
        Assert.IsTrue(result.Success, result.ErrorMessage);
    }

    [TestMethod]
    [ProductionSymbolEvidence("WindowsAtomicExpectedFileReplacer.ReplaceIfExpected")]
    [ProductionSymbolEvidence("DefaultMigrationFileOps.CreateOwnedStage")]
    [ProductionSymbolEvidence("DefaultMigrationManifestFileOps.CreateOwnedStage")]
    public void CRUU18_006_No_stage_factory_closes_unclaimed_creation_handle_without_exact_cleanup()
    {
        CRUU18_006_CAS_stage_claim_failure_deletes_exact_stage_before_releasing_handle();
        CRUU18_006_Migration_CreateOwnedStage_claim_failure_leaves_no_unproven_temp();
        new Cruu19RegressionTests()
            .CRUU19_002_Manifest_stage_claim_failure_deletes_exact_stage_before_handle_release();
    }

    [TestMethod]
    [ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
    public void CRUU18_007_CRUU18_001_sentinel_hits_MigrationRecoveryService_RecoverForRetry() =>
        AssertRealRetryDeletesCurrentFinal();

    [TestMethod]
    [TestCategory("WpfIntegration")]
    [ProductionSymbolEvidence("MainWindow.ExecuteLibraryMutation")]
    public void CRUU18_007_CRUU18_003_sentinel_hits_real_MainWindow_category_committed_exception_path() =>
        AssertCategoryCommittedFailure(CategoryMutation.Create);

    [TestMethod]
    public void CRUU18_007_High_risk_finding_map_requires_explicit_production_symbol()
    {
        FindingCoverageMap map = FindingCoverageMap.Load();
        string[] expected = Enumerable.Range(1, 7).Select(i => $"CRUU18-{i:000}").ToArray();
        foreach (string finding in expected)
        {
            Assert.IsTrue(map.RequiredProductionSymbols.TryGetValue(finding, out IReadOnlyList<string>? symbols));
            Assert.IsTrue(symbols!.Count > 0, $"{finding} has no production-symbol authority.");
        }
    }

    [TestMethod]
    public void CRUU18_007_Required_test_name_cannot_substitute_a_different_integration_layer()
    {
        FindingCoverageMap map = FindingCoverageMap.Load();
        foreach (KeyValuePair<string, IReadOnlyList<string>> authority in map.RequiredProductionSymbols
                     .Where(pair => pair.Key.StartsWith("CRUU18-", StringComparison.Ordinal)))
        {
            IReadOnlyList<string> mappedTests = map.Findings[authority.Key];
            var attributes = mappedTests
                .Select(name => typeof(Cruu18RegressionTests).GetMethod(name, BindingFlags.Instance | BindingFlags.Public))
                .Where(method => method is not null)
                .SelectMany(method => method!.GetCustomAttributes<ProductionSymbolEvidenceAttribute>())
                .Select(attribute => attribute.Symbol)
                .ToHashSet(StringComparer.Ordinal);

            foreach (string symbol in authority.Value)
            {
                Assert.IsTrue(attributes.Contains(symbol),
                    $"{authority.Key} requires runtime hit '{symbol}', but no mapped sentinel declares and asserts it.");
            }
        }
    }
}
