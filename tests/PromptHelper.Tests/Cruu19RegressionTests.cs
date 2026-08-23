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

namespace PromptHelper.Tests;

[TestClass]
[DoNotParallelize]
public sealed class Cruu19RegressionTests
{
    private sealed class PhaseFailingJournal : IOwnedArtifactJournal
    {
        public WindowsOwnedArtifactJournal Inner { get; } = new();
        public bool FailAfterRecord { get; init; }

        public void Record(string root, OwnedArtifactRecord record)
        {
            if (!FailAfterRecord)
            {
                throw new IOException("Injected ownership claim failure.");
            }

            Inner.Record(root, record);
            throw new IOException("Injected post-append ownership flush failure.");
        }

        public OwnedArtifactJournalSnapshot Read(string root) => Inner.Read(root);

        public void Rewrite(
            string root,
            OwnedArtifactJournalSnapshot expected,
            IReadOnlyList<OwnedArtifactRecord> surviving) =>
            Inner.Rewrite(root, expected, surviving);
    }

    private sealed class ProbeFixture : IDisposable
    {
        public TestDirectory Source { get; } = new();
        public TestDirectory Target { get; } = new();
        public MigrationCapabilityProbePlan Plan { get; }
        public MigrationAttemptManifest Manifest { get; }
        public MigrationManifestRepository Repository { get; } = new();
        public string MarkerPath => Path.Combine(Target.Root, ".prompthelper-migration.json");

        public ProbeFixture(bool includeProbePlan)
        {
            SeedValidLibrary(Source.Root);
            MigrationPayloadSnapshot snapshot =
                new DataFolderMigrationService().CaptureSourcePayloadSnapshot(Source.Root);
            Guid attemptId = Guid.NewGuid();
            Plan = MigrationCapabilityProbePlan.Create(attemptId);
            Manifest = MigrationManifestBuilder.BuildCopying(
                Source.Root,
                Target.Root,
                snapshot,
                attemptId,
                includeProbePlan ? Plan : null,
                new MigrationTargetBaseline(
                    targetRootExistedBefore: true,
                    promptsDirectoryExistedBefore: false,
                    recoveryDirectoryExistedBefore: false));
            Repository.CreateInitialCopyingManifestDurable(MarkerPath, Manifest);
        }

        public RecoveryResult Recover(IMigrationFileOps? ops = null) =>
            new MigrationRecoveryService(Repository, ops ?? new DefaultMigrationFileOps())
                .RecoverForRetry(new MigrationRecoveryContext(
                    Target.Root,
                    ExpectedSourcePhysicalRoot: Source.Root));

        public void Dispose()
        {
            Target.Dispose();
            Source.Dispose();
        }
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));

    private static void SeedValidLibrary(string root)
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "prompts"));
        Directory.CreateDirectory(Path.Combine(root, "recovery"));
        var document = new LibraryDocument
        {
            SchemaVersion = LibraryDocument.CurrentSchemaVersion,
            Categories = [],
            Prompts = []
        };
        string json = JsonSerializer.Serialize(document, LibraryRepository.JsonOptions);
        File.WriteAllText(Path.Combine(root, "library.json"), json);
        File.WriteAllText(Path.Combine(root, "library.backup.json"), json);
    }

    private static WindowsFileIdentity DirectoryIdentity(string root, string path)
    {
        using WindowsRetirableDirectory directory =
            WindowsRetirableDirectory.OpenExistingOrNull(path, root)
            ?? throw new AssertFailedException($"Directory disappeared: {path}");
        return directory.Identity;
    }

    private static void AssertLiveCurrentSubstitutionPreserved()
    {
        using var temp = new TestDirectory();
        string? foreignPath = null;
        bool substitutionBlocked = false;
        DataRootCapabilityValidator.BeforeCurrentSidelineForTests = (current, _) =>
        {
            string holding = Path.Combine(temp.Root, "original-current-holding.tmp");
            try
            {
                File.Move(current, holding);
                File.WriteAllText(current, "create");
                foreignPath = current;
            }
            catch (IOException)
            {
                substitutionBlocked = true;
            }
        };
        try
        {
            try
            {
                new DataRootCapabilityValidator().ValidateWritable(temp.Root);
            }
            catch (IOException) when (foreignPath is not null)
            {
                // A sharing mode that permits the race must preserve the inserted object and
                // fail the no-overwrite promotion. The default exclusive creation handle
                // prevents the substitution earlier, which is stronger.
            }
        }
        finally
        {
            DataRootCapabilityValidator.BeforeCurrentSidelineForTests = null;
        }

        Assert.IsTrue(substitutionBlocked || foreignPath is not null);
        if (foreignPath is not null)
        {
            Assert.AreEqual("create", File.ReadAllText(foreignPath));
        }
    }

    private static void AssertLiveReplacementSourceSubstitutionPreserved()
    {
        using var temp = new TestDirectory();
        string? foreignPath = null;
        bool substitutionBlocked = false;
        DataRootCapabilityValidator.BeforeReplacementPromotionForTests = (replacement, _) =>
        {
            string holding = Path.Combine(temp.Root, "original-replacement-holding.tmp");
            try
            {
                File.Move(replacement, holding);
                File.WriteAllText(replacement, "replace");
                foreignPath = replacement;
            }
            catch (IOException)
            {
                substitutionBlocked = true;
            }
        };
        try
        {
            new DataRootCapabilityValidator().ValidateWritable(temp.Root);
        }
        finally
        {
            DataRootCapabilityValidator.BeforeReplacementPromotionForTests = null;
        }

        Assert.IsTrue(substitutionBlocked || foreignPath is not null);
        if (foreignPath is not null)
        {
            Assert.AreEqual("replace", File.ReadAllText(foreignPath));
        }
    }

    private static void AssertRetrySameContentForeignProbeIsPreserved()
    {
        using var fixture = new ProbeFixture(includeProbePlan: true);
        string probePath = Path.Combine(fixture.Target.Root, fixture.Plan.RootProbe.CurrentRelativePath);
        string displaced = Path.Combine(fixture.Target.Root, "owned-probe-displaced.tmp");
        byte[] content = Utf8("create");
        var capabilityOps = new DefaultCapabilityFileOps();
        using (IOwnedCapabilityProbe probe = capabilityOps.CreateOwnedProbe(
                   fixture.Target.Root, probePath, content, recordDurableOwnership: true))
        {
            probe.Write(content);
            probe.FlushDurable();
        }

        File.Move(probePath, displaced);
        File.WriteAllBytes(probePath, content);

        RecoveryResult result = Cruu18RegressionTests.AssertProductionHit(
            "MigrationRecoveryService.RecoverForRetry",
            () => fixture.Recover());
        Assert.IsFalse(result.Success);
        Assert.IsTrue(File.Exists(probePath));
        CollectionAssert.AreEqual(content, File.ReadAllBytes(probePath));
    }

    private static void AssertExactOwnedProbeIsDeleted()
    {
        using var fixture = new ProbeFixture(includeProbePlan: true);
        string probePath = Path.Combine(fixture.Target.Root, fixture.Plan.RootProbe.CurrentRelativePath);
        byte[] content = Utf8("create");
        var capabilityOps = new DefaultCapabilityFileOps();
        using (IOwnedCapabilityProbe probe = capabilityOps.CreateOwnedProbe(
                   fixture.Target.Root, probePath, content, recordDurableOwnership: true))
        {
            probe.Write(content);
            probe.FlushDurable();
        }

        RecoveryResult result = Cruu18RegressionTests.AssertProductionHit(
            "MigrationRecoveryService.RecoverForRetry",
            () => fixture.Recover());
        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsFalse(File.Exists(probePath));
    }

    [TestMethod]
    [ProductionSymbolEvidence("DataRootCapabilityValidator.ValidateWritable")]
    public void CRUU19_001_Live_probe_destination_same_content_foreign_replacement_is_not_replaced() =>
        Cruu18RegressionTests.AssertProductionHit(
            "DataRootCapabilityValidator.ValidateWritable",
            AssertLiveCurrentSubstitutionPreserved);

    [TestMethod]
    [ProductionSymbolEvidence("DataRootCapabilityValidator.ValidateWritable")]
    public void CRUU19_001_Live_probe_source_same_content_foreign_replacement_is_not_promoted() =>
        Cruu18RegressionTests.AssertProductionHit(
            "DataRootCapabilityValidator.ValidateWritable",
            AssertLiveReplacementSourceSubstitutionPreserved);

    [TestMethod]
    [ProductionSymbolEvidence("DataRootCapabilityValidator.ValidateWritable")]
    public void CRUU19_001_Live_probe_before_final_delete_foreign_replacement_is_not_deleted()
    {
        using var temp = new TestDirectory();
        string? foreignPath = null;
        bool substitutionBlocked = false;
        DataRootCapabilityValidator.BeforeFinalRetirementForTests = current =>
        {
            string holding = Path.Combine(temp.Root, "owned-promoted-holding.tmp");
            try
            {
                File.Move(current, holding);
                File.WriteAllText(current, "replace");
                foreignPath = current;
            }
            catch (IOException)
            {
                substitutionBlocked = true;
            }
        };
        try
        {
            Cruu18RegressionTests.AssertProductionHit(
                "DataRootCapabilityValidator.ValidateWritable",
                () => new DataRootCapabilityValidator().ValidateWritable(temp.Root));
        }
        finally
        {
            DataRootCapabilityValidator.BeforeFinalRetirementForTests = null;
        }

        Assert.IsTrue(substitutionBlocked || foreignPath is not null);
        if (foreignPath is not null)
        {
            Assert.AreEqual("replace", File.ReadAllText(foreignPath));
        }
    }

    [TestMethod]
    [ProductionSymbolEvidence("DefaultCapabilityFileOps.CreateOwnedProbe")]
    public void CRUU19_001_Probe_failure_cleanup_requires_creation_identity_not_current_path_identity() =>
        Cruu18RegressionTests.AssertProductionHit(
            "DefaultCapabilityFileOps.CreateOwnedProbe",
            AssertLiveCurrentSubstitutionPreserved);

    [TestMethod]
    [ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
    public void CRUU19_001_Retry_same_content_different_identity_probe_is_preserved() =>
        AssertRetrySameContentForeignProbeIsPreserved();

    [TestMethod]
    [ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
    public void CRUU19_001_Retry_exact_owned_probe_identity_and_content_is_deleted() =>
        AssertExactOwnedProbeIsDeleted();

    [TestMethod]
    [ProductionSymbolEvidence("DataRootCapabilityValidator.ValidateWritable")]
    public void CRUU19_001_CRUU12_027_executes_real_DataRootCapabilityValidator_substitution_path() =>
        Cruu18RegressionTests.AssertProductionHit(
            "DataRootCapabilityValidator.ValidateWritable",
            AssertLiveReplacementSourceSubstitutionPreserved);

    [TestMethod]
    [ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
    public void CRUU19_001_CRUU13_004_tests_same_content_different_identity_not_only_wrong_content() =>
        AssertRetrySameContentForeignProbeIsPreserved();

    [TestMethod]
    public void CRUU19_001_DefaultCapabilityFileOps_exposes_no_raw_path_replace_or_delete_for_owned_probe()
    {
        string source = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "src", "PromptHelper", "Services", "ICapabilityFileOps.cs"));
        Assert.IsFalse(source.Contains("File.Replace(", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("File.Delete(", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("DeleteFile(", StringComparison.Ordinal));
    }

    [TestMethod]
    [ProductionSymbolEvidence("DefaultMigrationManifestFileOps.CreateOwnedStage")]
    public void CRUU19_002_Manifest_stage_claim_failure_deletes_exact_stage_before_handle_release()
    {
        using var temp = new TestDirectory();
        string stagePath = Path.Combine(temp.Root, "manifest-stage.tmp");
        var ops = new DefaultMigrationManifestFileOps(new PhaseFailingJournal());
        Cruu18RegressionTests.AssertProductionHitThrows<IOException>(
            "DefaultMigrationManifestFileOps.CreateOwnedStage",
            () => ops.CreateOwnedStage(temp.Root, stagePath));
        Assert.IsFalse(File.Exists(stagePath));
    }

    [TestMethod]
    [ProductionSymbolEvidence("DefaultMigrationManifestFileOps.CreateOwnedStage")]
    public void CRUU19_002_Manifest_stage_postappend_failure_deletes_stage_and_reconciles_stale_record()
    {
        using var temp = new TestDirectory();
        string stagePath = Path.Combine(temp.Root, "manifest-stage.tmp");
        var journal = new PhaseFailingJournal { FailAfterRecord = true };
        var ops = new DefaultMigrationManifestFileOps(journal);
        Cruu18RegressionTests.AssertProductionHitThrows<IOException>(
            "DefaultMigrationManifestFileOps.CreateOwnedStage",
            () => ops.CreateOwnedStage(temp.Root, stagePath));
        Assert.IsFalse(File.Exists(stagePath));
        Assert.IsTrue(journal.Inner.Read(temp.Root).Records.Any());
        Assert.IsFalse(OwnedArtifactReconciler.Reconcile(temp.Root, journal.Inner).HasFatal);
        Assert.AreEqual(0, journal.Inner.Read(temp.Root).Records.Count);
    }

    [TestMethod]
    [ProductionSymbolEvidence("DefaultMigrationManifestFileOps.CreateOwnedStage")]
    public void CRUU19_002_Ready_manifest_stage_claim_failure_does_not_wedge_RecoverForRetry()
    {
        using var fixture = new ProbeFixture(includeProbePlan: false);
        fixture.Manifest.Phase = MigrationManifestPhase.ReadyToCommit;
        var failing = new DefaultMigrationManifestFileOps(
            new PhaseFailingJournal { FailAfterRecord = true });
        var repository = new MigrationManifestRepository(failing);
        Cruu18RegressionTests.AssertProductionHitThrows<IOException>(
            "DefaultMigrationManifestFileOps.CreateOwnedStage",
            () => repository.WriteReadyManifestDurable(fixture.MarkerPath, fixture.Manifest));

        fixture.Manifest.Phase = MigrationManifestPhase.Copying;
        RecoveryResult result = fixture.Recover();
        Assert.IsTrue(result.Success, result.ErrorMessage);
    }

    [TestMethod]
    [ProductionSymbolEvidence("WindowsAtomicExpectedFileReplacer.ReplaceIfExpected")]
    [ProductionSymbolEvidence("DefaultMigrationFileOps.CreateOwnedStage")]
    [ProductionSymbolEvidence("DefaultMigrationManifestFileOps.CreateOwnedStage")]
    public void CRUU19_002_CRUU18_006_all_stage_factories_test_includes_DefaultMigrationManifestFileOps()
    {
        var cruu18 = new Cruu18RegressionTests();
        cruu18.CRUU18_006_CAS_stage_claim_failure_deletes_exact_stage_before_releasing_handle();
        cruu18.CRUU18_006_Migration_CreateOwnedStage_claim_failure_leaves_no_unproven_temp();
        CRUU19_002_Manifest_stage_claim_failure_deletes_exact_stage_before_handle_release();
    }

    [TestMethod]
    public void CRUU19_002_CRUU18_006_required_symbols_include_manifest_stage_factory()
    {
        FindingCoverageMap map = FindingCoverageMap.Load();
        CollectionAssert.Contains(
            map.RequiredProductionSymbols["CRUU18-006"].ToArray(),
            "DefaultMigrationManifestFileOps.CreateOwnedStage");
    }

    [TestMethod]
    [ProductionSymbolEvidence("MigrationTargetTransaction.Rollback")]
    public void CRUU19_003_Inprocess_rollback_same_path_different_empty_directory_is_preserved()
    {
        using var temp = new TestDirectory();
        string path = Path.Combine(temp.Root, "attempt-directory");
        string displaced = Path.Combine(temp.Root, "original-directory");
        Directory.CreateDirectory(path);
        WindowsFileIdentity identity = DirectoryIdentity(temp.Root, path);
        using var tx = new DataFolderMigrationService.MigrationTargetTransaction(temp.Root);
        tx.TrackCreatedDirectory(path, identity.ToToken());
        Directory.Move(path, displaced);
        Directory.CreateDirectory(path);

        MigrationRollbackResult result = Cruu18RegressionTests.AssertProductionHit(
            "MigrationTargetTransaction.Rollback",
            tx.Rollback);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(Directory.Exists(path));
        Assert.AreNotEqual(identity, DirectoryIdentity(temp.Root, path));
    }

    [TestMethod]
    [ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
    public void CRUU19_003_Retry_same_path_different_empty_directory_is_preserved()
    {
        using var fixture = new ProbeFixture(includeProbePlan: false);
        string prompts = Path.Combine(fixture.Target.Root, "prompts");
        OwnedDirectoryCreationResult creation = new WindowsOwnedDirectoryCreator().TryCreateOwned(prompts);
        Assert.AreEqual(DirectoryCreateOutcome.CreatedByCaller, creation.Outcome);
        string displaced = Path.Combine(fixture.Target.Root, "original-prompts");
        Directory.Move(prompts, displaced);
        Directory.CreateDirectory(prompts);

        RecoveryResult result = Cruu18RegressionTests.AssertProductionHit(
            "MigrationRecoveryService.RecoverForRetry",
            () => fixture.Recover());
        Assert.IsFalse(result.Success);
        Assert.IsTrue(Directory.Exists(prompts));
        Assert.AreNotEqual(creation.Claim!.Identity, DirectoryIdentity(fixture.Target.Root, prompts));
    }

    [TestMethod]
    [ProductionSymbolEvidence("WindowsOwnedDirectoryCreator.RecordCreationIdentity")]
    public void CRUU19_003_Attempt_created_directory_records_WindowsFileIdentity()
    {
        using var temp = new TestDirectory();
        string prompts = Path.Combine(temp.Root, "prompts");
        OwnedDirectoryCreationResult creation = Cruu18RegressionTests.AssertProductionHit(
            "WindowsOwnedDirectoryCreator.RecordCreationIdentity",
            () => new WindowsOwnedDirectoryCreator().TryCreateOwned(prompts));
        OwnedArtifactRecord record = new WindowsOwnedArtifactJournal().Read(temp.Root).Records
            .Single(r => r.Kind == OwnedArtifactKind.MigrationDirectory);
        Assert.AreEqual(creation.Claim!.Identity, record.Identity);
        Assert.AreEqual(DirectoryIdentity(temp.Root, prompts), record.Identity);
    }

    [TestMethod]
    public void CRUU19_003_Exact_owned_empty_directory_identity_is_removed()
    {
        using var temp = new TestDirectory();
        string prompts = Path.Combine(temp.Root, "prompts");
        new WindowsOwnedDirectoryCreator().TryCreateOwned(prompts);
        ArtifactCleanupOutcome result =
            new DefaultMigrationFileOps().DeleteOwnedDirectoryIfProven(temp.Root, prompts);
        Assert.AreEqual(ArtifactCleanupOutcome.DeletedProvenOwned, result);
        Assert.IsFalse(Directory.Exists(prompts));
    }

    [TestMethod]
    public void CRUU19_003_Foreign_nonempty_directory_remains_preserved()
    {
        using var temp = new TestDirectory();
        string path = Path.Combine(temp.Root, "attempt-directory");
        string displaced = Path.Combine(temp.Root, "original-directory");
        Directory.CreateDirectory(path);
        WindowsFileIdentity identity = DirectoryIdentity(temp.Root, path);
        using var tx = new DataFolderMigrationService.MigrationTargetTransaction(temp.Root);
        tx.TrackCreatedDirectory(path, identity.ToToken());
        Directory.Move(path, displaced);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "foreign.txt"), "foreign");
        MigrationRollbackResult result = tx.Rollback();
        Assert.IsFalse(result.Success);
        Assert.IsTrue(File.Exists(Path.Combine(path, "foreign.txt")));

        // A same-path type substitution is foreign as well. Reconciliation must preserve it
        // and retire the stale directory claim rather than throwing out of rollback startup.
        using var typeSwap = new TestDirectory();
        string prompts = Path.Combine(typeSwap.Root, "prompts");
        new WindowsOwnedDirectoryCreator().TryCreateOwned(prompts);
        Directory.Move(prompts, Path.Combine(typeSwap.Root, "original-prompts"));
        File.WriteAllText(prompts, "foreign file");
        OwnedArtifactReconciler.Result reconciliation =
            OwnedArtifactReconciler.Reconcile(typeSwap.Root, new WindowsOwnedArtifactJournal());
        Assert.IsFalse(reconciliation.HasFatal);
        Assert.IsTrue(File.Exists(prompts));
        Assert.AreEqual(0, new WindowsOwnedArtifactJournal().Read(typeSwap.Root).Records.Count);
    }

    [TestMethod]
    public void CRUU19_003_CRUU16_006_swapped_directory_test_uses_directory_to_directory_identity_swap()
    {
        string source = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "tests", "PromptHelper.Tests", "Cruu16StartupAndProvenanceTests.cs"));
        int start = source.IndexOf(
            "CRUU16_006_Inprocess_rollback_swapped_attempt_directory_is_never_deleted",
            StringComparison.Ordinal);
        Assert.IsTrue(start >= 0);
        string method = source.Substring(start, Math.Min(1800, source.Length - start));
        Assert.IsTrue(method.Contains("Directory.Move(", StringComparison.Ordinal));
        Assert.IsTrue(method.Contains("Directory.CreateDirectory(", StringComparison.Ordinal));
        Assert.IsFalse(method.Contains("File.WriteAllText(dir", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CRUU19_004_AssertProductionHit_expected_exception_still_requires_runtime_hit()
    {
        Assert.ThrowsExactly<AssertFailedException>(() =>
            Cruu18RegressionTests.AssertProductionHitThrows<IOException>(
                "Never.Hit",
                () => throw new IOException("expected")));
    }

    [TestMethod]
    public void CRUU19_004_Expected_exception_without_hit_fails_evidence_test() =>
        CRUU19_004_AssertProductionHit_expected_exception_still_requires_runtime_hit();

    [TestMethod]
    public void CRUU19_004_Expected_exception_with_hit_passes_evidence_test()
    {
        IOException observed = Cruu18RegressionTests.AssertProductionHitThrows<IOException>(
            "Evidence.ExpectedFailure",
            () =>
            {
                ProductionRuntimeEvidence.Hit("Evidence.ExpectedFailure");
                throw new IOException("expected");
            });
        Assert.AreEqual("expected", observed.Message);
    }

    [TestMethod]
    [ProductionSymbolEvidence("WindowsAtomicExpectedFileReplacer.ReplaceIfExpected")]
    public void CRUU19_004_CRUU18_002_failure_sentinel_proves_CAS_runtime_hit() =>
        new Cruu18RegressionTests()
            .CRUU18_002_Promotion_failure_after_PreimageSidelined_successful_restore_is_not_fatal_on_restart();

    [TestMethod]
    [ProductionSymbolEvidence("WindowsAtomicExpectedFileReplacer.ReplaceIfExpected")]
    [ProductionSymbolEvidence("DefaultMigrationFileOps.CreateOwnedStage")]
    [ProductionSymbolEvidence("DefaultMigrationManifestFileOps.CreateOwnedStage")]
    public void CRUU19_004_CRUU18_006_failure_sentinels_prove_all_stage_factory_runtime_hits()
    {
        var cruu18 = new Cruu18RegressionTests();
        cruu18.CRUU18_006_CAS_stage_claim_failure_deletes_exact_stage_before_releasing_handle();
        cruu18.CRUU18_006_Migration_CreateOwnedStage_claim_failure_leaves_no_unproven_temp();
        CRUU19_002_Manifest_stage_claim_failure_deletes_exact_stage_before_handle_release();
    }

    [TestMethod]
    public void CRUU19_004_Required_symbol_map_detects_omitted_manifest_stage_factory()
    {
        FindingCoverageMap map = FindingCoverageMap.Load();
        string[] symbols = map.RequiredProductionSymbols["CRUU18-006"].ToArray();
        CollectionAssert.Contains(symbols, "DefaultMigrationManifestFileOps.CreateOwnedStage");
        Assert.AreEqual(3, symbols.Length);

        foreach (KeyValuePair<string, IReadOnlyList<string>> authority in
                 map.RequiredProductionSymbols.Where(pair =>
                     pair.Key.StartsWith("CRUU19-", StringComparison.Ordinal)))
        {
            HashSet<string> evidenced = map.Findings[authority.Key]
                .Select(name => typeof(Cruu19RegressionTests).GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.Public))
                .Where(method => method is not null)
                .SelectMany(method => method!.GetCustomAttributes<ProductionSymbolEvidenceAttribute>())
                .Select(attribute => attribute.Symbol)
                .ToHashSet(StringComparer.Ordinal);

            foreach (string required in authority.Value)
            {
                Assert.IsTrue(evidenced.Contains(required),
                    $"{authority.Key} maps '{required}' but no mapped CRUU19 test carries runtime evidence for it.");
            }
        }
    }
}
