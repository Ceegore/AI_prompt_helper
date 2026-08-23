using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[ProductionEvidenceTestClass]
[DoNotParallelize]
public sealed class Cruu20RegressionTests
{
    private static readonly byte[] CreateBytes = Encoding.UTF8.GetBytes("create");
    private static readonly byte[] ReplaceBytes = Encoding.UTF8.GetBytes("replace");
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class CrashRun : IDisposable
    {
        private readonly TestDirectory _directory;
        public CrashRun(TestDirectory directory) => _directory = directory;
        public string Root => _directory.Root;
        public void Dispose() => _directory.Dispose();
    }

    private sealed class ManifestFixture : IDisposable
    {
        public TestDirectory Source { get; } = new();
        public TestDirectory Target { get; } = new();
        public string MarkerPath => Path.Combine(Target.Root, ".prompthelper-migration.json");

        public MigrationAttemptManifest Manifest(
            int schemaVersion,
            IReadOnlyList<MigrationControlArtifact>? controls = null,
            MigrationTargetBaseline? baseline = null) =>
            new()
            {
                SchemaVersion = schemaVersion,
                AttemptId = Guid.NewGuid(),
                SourcePhysicalRoot = Source.Root,
                TargetPhysicalRoot = Target.Root,
                SourceLibrarySha256Hex = new string('0', 64),
                SourcePayloadFingerprintSha256Hex = new string('0', 64),
                Phase = MigrationManifestPhase.Copying,
                Artifacts = [],
                ControlArtifacts = controls?.ToList() ?? [],
                TargetBaseline = baseline ?? new MigrationTargetBaseline(true, true, true)
            };

        public void Write(MigrationAttemptManifest manifest) =>
            new MigrationManifestRepository().CreateInitialCopyingManifestDurable(MarkerPath, manifest);

        public RecoveryResult Recover() => new MigrationRecoveryService().RecoverForRetry(
            new MigrationRecoveryContext(
                Target.Root,
                ExpectedSourcePhysicalRoot: Source.Root));

        public void Dispose()
        {
            Target.Dispose();
            Source.Dispose();
        }
    }

    // ------------------------------ CRUU20-001 -----------------------------------------

    [TestMethod]
    public void CRUU20_001_Crash_after_probe_claim_before_full_write_is_recoverable()
    {
        using CrashRun run = RunCrash("Cruu20.ProbeAfterClaimBeforeWrite");
        Assert.AreEqual(ArtifactCleanupOutcome.DeletedProvenOwned,
            DeleteProbe(run.Root, "probe-current.tmp", CreateBytes));
        Assert.IsFalse(File.Exists(Path.Combine(run.Root, "probe-current.tmp")));
    }

    [TestMethod]
    public void CRUU20_001_Crash_during_probe_write_partial_content_is_recoverable()
    {
        using CrashRun run = RunCrash("Cruu20.ProbeDuringPartialWrite");
        string path = Path.Combine(run.Root, "probe-current.tmp");
        Assert.AreEqual(2, new FileInfo(path).Length);
        Assert.AreEqual(ArtifactCleanupOutcome.DeletedProvenOwned,
            DeleteProbe(run.Root, "probe-current.tmp", CreateBytes));
    }

    [TestMethod]
    public void CRUU20_001_Crash_after_probe_data_write_before_flush_is_recoverable()
    {
        using CrashRun run = RunCrash("Cruu20.ProbeAfterWriteBeforeFlush");
        Assert.AreEqual(CreateBytes.Length, new FileInfo(Path.Combine(run.Root, "probe-current.tmp")).Length);
        Assert.AreEqual(ArtifactCleanupOutcome.DeletedProvenOwned,
            DeleteProbe(run.Root, "probe-current.tmp", CreateBytes));
    }

    [TestMethod]
    public void CRUU20_001_Crash_after_current_probe_rename_before_new_location_record_is_recoverable()
    {
        using CrashRun run = RunCrash("DefaultCapabilityFileOps.AfterRenameToDisplacedBeforeRecord");
        Assert.IsFalse(File.Exists(Path.Combine(run.Root, "probe-current.tmp")));
        Assert.AreEqual(ArtifactCleanupOutcome.DeletedProvenOwned,
            DeleteProbe(run.Root, "probe-displaced.tmp", CreateBytes));
    }

    [TestMethod]
    public void CRUU20_001_Crash_after_replacement_probe_rename_before_new_location_record_is_recoverable()
    {
        using CrashRun run = RunCrash("DefaultCapabilityFileOps.AfterRenameToCurrentBeforeRecord");
        Assert.IsFalse(File.Exists(Path.Combine(run.Root, "probe-replacement.tmp")));
        Assert.AreEqual(ArtifactCleanupOutcome.DeletedProvenOwned,
            DeleteProbe(run.Root, "probe-current.tmp", CreateBytes, ReplaceBytes));
    }

    [TestMethod]
    [ProductionSymbolEvidence("DefaultCapabilityFileOps.CreateOwnedProbe")]
    public void CRUU20_001_Probe_claim_predeclares_all_recovery_locations_before_first_rename()
    {
        using var temp = new TestDirectory();
        string initial = Path.Combine(temp.Root, "probe-current.tmp");
        string alternate = Path.Combine(temp.Root, "probe-displaced.tmp");
        using IOwnedCapabilityProbe probe = new DefaultCapabilityFileOps().CreateOwnedProbe(
            temp.Root, initial, alternate, CreateBytes, recordDurableOwnership: true);

        OwnedArtifactRecord first = new WindowsOwnedArtifactJournal().Read(temp.Root).Records.Single();
        Assert.AreEqual(OwnedArtifactKind.CapabilityProbe, first.Kind);
        Assert.AreEqual(OwnedArtifactPhase.ProbeCreatedClaimed, first.Phase);
        Assert.AreEqual(Path.GetRelativePath(temp.Root, initial), first.RelativePath);
        Assert.AreEqual(Path.GetRelativePath(temp.Root, alternate), first.RestoreRelativePath);
        probe.DeleteExact();

        using var outside = new TestDirectory();
        string rejectedInitial = Path.Combine(temp.Root, "rejected-current.tmp");
        Assert.ThrowsExactly<InvalidDataException>(() =>
            new DefaultCapabilityFileOps().CreateOwnedProbe(
                temp.Root,
                rejectedInitial,
                Path.Combine(outside.Root, "outside.tmp"),
                CreateBytes,
                recordDurableOwnership: true));
        Assert.IsFalse(File.Exists(rejectedInitial));
    }

    [TestMethod]
    public void CRUU20_001_Probe_recovery_matrix_covers_every_durable_phase_and_location()
    {
        using var temp = new TestDirectory();
        string initial = Path.Combine(temp.Root, "probe-current.tmp");
        string alternate = Path.Combine(temp.Root, "probe-displaced.tmp");
        using IOwnedCapabilityProbe probe = new DefaultCapabilityFileOps().CreateOwnedProbe(
            temp.Root, initial, alternate, CreateBytes, recordDurableOwnership: true);
        probe.Write(CreateBytes);
        probe.FlushDurable();
        probe.RenameNoOverwriteRetainingOwnership(alternate);

        OwnedArtifactRecord[] records = new WindowsOwnedArtifactJournal().Read(temp.Root).Records
            .Where(record => record.Kind == OwnedArtifactKind.CapabilityProbe)
            .ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                OwnedArtifactPhase.ProbeCreatedClaimed,
                OwnedArtifactPhase.ProbeContentDurable,
                OwnedArtifactPhase.ProbeRenamePrepared,
                OwnedArtifactPhase.ProbeRenamed
            },
            records.Select(record => record.Phase).ToArray());
        Assert.IsTrue(records.All(record =>
            string.Equals(record.RestoreRelativePath, "probe-displaced.tmp", StringComparison.OrdinalIgnoreCase)));
        probe.DeleteExact();
    }

    [TestMethod]
    public void CRUU20_001_Partial_exact_owned_probe_does_not_become_PreservedUnproven()
    {
        using var temp = new TestDirectory();
        string initial = Path.Combine(temp.Root, "probe-current.tmp");
        using (IOwnedCapabilityProbe probe = new DefaultCapabilityFileOps().CreateOwnedProbe(
                   temp.Root,
                   initial,
                   Path.Combine(temp.Root, "probe-displaced.tmp"),
                   CreateBytes,
                   recordDurableOwnership: true))
        {
            probe.Write(CreateBytes.AsSpan(0, 2));
        }

        Assert.AreEqual(ArtifactCleanupOutcome.DeletedProvenOwned,
            DeleteProbe(temp.Root, "probe-current.tmp", CreateBytes));
    }

    [TestMethod]
    public void CRUU20_001_Foreign_partial_probe_same_path_is_still_preserved()
    {
        AssertForeignProbePreserved(Encoding.UTF8.GetBytes("cr"));
    }

    [TestMethod]
    public void CRUU20_001_Foreign_same_content_different_identity_is_still_preserved()
    {
        AssertForeignProbePreserved(CreateBytes);
    }

    // ------------------------------ CRUU20-002 -----------------------------------------

    [TestMethod]
    public void CRUU20_002_Hard_crash_between_CAS_stage_create_and_first_claim_leaves_no_unproven_stage()
    {
        using CrashRun run = RunCrash(
            "WindowsAtomicExpectedFileReplacer.AfterCreateBeforeFirstClaim",
            root => File.WriteAllBytes(Path.Combine(root, "library.json"), Encoding.UTF8.GetBytes("old")));
        Assert.AreEqual(0, Directory.GetFiles(run.Root, ".prompthelper-tmp-*.tmp").Length);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("old"), File.ReadAllBytes(Path.Combine(run.Root, "library.json")));
    }

    [TestMethod]
    public void CRUU20_002_Hard_crash_between_payload_stage_create_and_first_claim_leaves_no_unproven_temp()
    {
        using CrashRun run = RunCrash("DefaultMigrationFileOps.AfterCreateBeforeFirstClaim");
        Assert.IsFalse(File.Exists(Path.Combine(run.Root, "payload-stage.tmp")));
    }

    [TestMethod]
    public void CRUU20_002_Hard_crash_between_manifest_stage_create_and_first_claim_does_not_wedge_retry()
    {
        using CrashRun run = RunCrash("DefaultMigrationManifestFileOps.AfterCreateBeforeFirstClaim");
        Assert.IsFalse(File.Exists(Path.Combine(run.Root, "manifest-stage.tmp")));
    }

    [TestMethod]
    public void CRUU20_002_Hard_crash_between_probe_create_and_first_claim_does_not_wedge_retry()
    {
        using CrashRun run = RunCrash("DefaultCapabilityFileOps.AfterCreateBeforeFirstClaim");
        Assert.IsFalse(File.Exists(Path.Combine(run.Root, "probe-current.tmp")));
    }

    [TestMethod]
    public void CRUU20_002_Hard_crash_between_directory_create_and_identity_claim_does_not_wedge_retry()
    {
        using CrashRun run = RunCrash("WindowsOwnedDirectoryCreator.AfterCreateBeforeFirstClaim");
        using var source = new TestDirectory();
        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = MigrationAttemptManifest.CurrentSchemaVersion,
            AttemptId = Guid.NewGuid(),
            SourcePhysicalRoot = source.Root,
            TargetPhysicalRoot = run.Root,
            SourceLibrarySha256Hex = new string('0', 64),
            SourcePayloadFingerprintSha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.Copying,
            Artifacts = [],
            ControlArtifacts =
            [
                new MigrationControlArtifact
                {
                    RelativePath = ".placeholder",
                    Kind = MigrationControlArtifactKind.ManifestPhaseStaging
                }
            ],
            TargetBaseline = new MigrationTargetBaseline(true, false, true)
        };
        manifest.ControlArtifacts[0].RelativePath =
            $".prompthelper-migration.stage-{manifest.AttemptId:N}.tmp";
        string marker = Path.Combine(run.Root, ".prompthelper-migration.json");
        new MigrationManifestRepository().CreateInitialCopyingManifestDurable(marker, manifest);

        RecoveryResult result = new MigrationRecoveryService().RecoverForRetry(
            new MigrationRecoveryContext(run.Root, ExpectedSourcePhysicalRoot: source.Root));
        Assert.IsTrue(result.Success, result.ErrorMessage);
        if (Directory.Exists(Path.Combine(run.Root, "prompts")))
        {
            Assert.AreEqual(0, Directory.GetFileSystemEntries(Path.Combine(run.Root, "prompts")).Length);
        }
        Assert.IsFalse(File.Exists(marker));
    }

    [TestMethod]
    public void CRUU20_002_Torn_first_ownership_append_cannot_leave_live_unproven_artifact()
    {
        using CrashRun run = RunCrash("WindowsOwnedArtifactJournal.AfterPartialFirstAppend");
        Assert.IsFalse(File.Exists(Path.Combine(run.Root, "payload-stage.tmp")));
        string journalPath = WindowsOwnedArtifactJournal.GetJournalPath(run.Root);
        Assert.IsTrue(File.Exists(journalPath));
        OwnedArtifactReconciler.Result result = OwnedArtifactReconciler.Reconcile(
            run.Root,
            new WindowsOwnedArtifactJournal());
        Assert.IsFalse(result.HasFatal);
        Assert.IsFalse(File.Exists(journalPath));
    }

    [TestMethod]
    [ProductionSymbolEvidence("WindowsAtomicExpectedFileReplacer.ReplaceIfExpected")]
    [ProductionSymbolEvidence("DefaultMigrationFileOps.CreateOwnedStage")]
    [ProductionSymbolEvidence("DefaultMigrationManifestFileOps.CreateOwnedStage")]
    [ProductionSymbolEvidence("DefaultCapabilityFileOps.CreateOwnedProbe")]
    [ProductionSymbolEvidence("WindowsOwnedDirectoryCreator.RecordCreationIdentity")]
    public void CRUU20_002_First_claim_protocol_is_crash_atomic_not_only_exception_safe()
    {
        using (var cas = new TestDirectory())
        {
            string final = Path.Combine(cas.Root, "candidate.json");
            new WindowsAtomicExpectedFileReplacer().ReplaceIfExpected(
                cas.Root, final, ExpectedFileState.Missing, Encoding.UTF8.GetBytes("candidate"), DurableFileClass.LibraryMetadata);
            Assert.IsTrue(File.Exists(final));
        }

        using (var payload = new TestDirectory())
        {
            using IOwnedFileStage stage = new DefaultMigrationFileOps().CreateOwnedStage(
                payload.Root, Path.Combine(payload.Root, "payload.tmp"));
            stage.DeleteExact();
        }

        using (var manifest = new TestDirectory())
        {
            using IOwnedFileStage stage = new DefaultMigrationManifestFileOps().CreateOwnedStage(
                manifest.Root, Path.Combine(manifest.Root, "manifest.tmp"));
            stage.DeleteExact();
        }

        using (var probeRoot = new TestDirectory())
        {
            using IOwnedCapabilityProbe probe = new DefaultCapabilityFileOps().CreateOwnedProbe(
                probeRoot.Root,
                Path.Combine(probeRoot.Root, "probe-current.tmp"),
                Path.Combine(probeRoot.Root, "probe-displaced.tmp"),
                CreateBytes,
                recordDurableOwnership: true);
            probe.DeleteExact();
        }

        using (var directoryRoot = new TestDirectory())
        {
            string prompts = Path.Combine(directoryRoot.Root, "prompts");
            OwnedDirectoryCreationResult created = new WindowsOwnedDirectoryCreator().TryCreateOwned(prompts);
            Assert.AreEqual(DirectoryCreateOutcome.CreatedByCaller, created.Outcome);
            Assert.AreEqual(ArtifactCleanupOutcome.DeletedProvenOwned,
                new DefaultMigrationFileOps().DeleteOwnedDirectoryIfProven(directoryRoot.Root, prompts));
        }

        string stageSource = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "src", "PromptHelper", "Services", "WindowsOwnedDurableStage.cs"));
        string directorySource = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "src", "PromptHelper", "Services", "WindowsCrashAtomicDirectoryBootstrap.cs"));
        StringAssert.Contains(stageSource, "FILE_FLAG_DELETE_ON_CLOSE");
        StringAssert.Contains(stageSource, "PersistAfterDurableClaim");
        StringAssert.Contains(directorySource, "FILE_DELETE_ON_CLOSE");
        StringAssert.Contains(directorySource, "NtCreateFile");
    }

    // ------------------------------ CRUU20-003 -----------------------------------------

    [TestMethod]
    public void CRUU20_003_Current_schema_version_bumps_when_ownership_protocol_changes()
    {
        object? value = typeof(MigrationAttemptManifest)
            .GetField(nameof(MigrationAttemptManifest.CurrentSchemaVersion), BindingFlags.Public | BindingFlags.Static)!
            .GetRawConstantValue();
        Assert.AreEqual(5, value);
    }

    [TestMethod]
    [ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
    public void CRUU20_003_Parent_v4_interrupted_attempt_is_not_treated_as_v5_identity_protocol()
    {
        using var fixture = new ManifestFixture();
        MigrationAttemptManifest manifest = fixture.Manifest(4, LegacyProbeControls(Guid.Empty));
        RewriteControlAttemptId(manifest);
        fixture.Write(manifest);
        File.WriteAllBytes(
            Path.Combine(fixture.Target.Root, manifest.ControlArtifacts.Single(c => c.Kind == MigrationControlArtifactKind.CapabilityProbeFile).RelativePath),
            CreateBytes);

        RecoveryResult result = fixture.Recover();
        Assert.IsFalse(result.Success);
        Assert.AreEqual(MigrationRecoveryDisposition.LegacyManualCleanupRequired, result.Disposition);
    }

    [TestMethod]
    public void CRUU20_003_Parent_v4_attempt_created_dirs_have_explicit_legacy_recovery_outcome()
    {
        using var fixture = new ManifestFixture();
        MigrationAttemptManifest manifest = fixture.Manifest(
            4,
            controls: [],
            baseline: new MigrationTargetBaseline(true, false, true));
        fixture.Write(manifest);
        Directory.CreateDirectory(Path.Combine(fixture.Target.Root, "prompts"));

        RecoveryResult result = fixture.Recover();
        Assert.AreEqual(MigrationRecoveryDisposition.LegacyManualCleanupRequired, result.Disposition);
        Assert.IsTrue(Directory.Exists(Path.Combine(fixture.Target.Root, "prompts")));
    }

    [TestMethod]
    public void CRUU20_003_Parent_v4_probe_residue_has_explicit_legacy_recovery_outcome()
    {
        using var fixture = new ManifestFixture();
        MigrationAttemptManifest manifest = fixture.Manifest(4, LegacyProbeControls(Guid.Empty));
        RewriteControlAttemptId(manifest);
        fixture.Write(manifest);
        string path = Path.Combine(fixture.Target.Root,
            manifest.ControlArtifacts.Single(c => c.Kind == MigrationControlArtifactKind.CapabilityProbeFile).RelativePath);
        File.WriteAllBytes(path, CreateBytes);

        RecoveryResult result = fixture.Recover();
        Assert.AreEqual(MigrationRecoveryDisposition.LegacyManualCleanupRequired, result.Disposition);
        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    public void CRUU20_003_Legacy_v4_clean_attempt_can_retire_when_no_destructive_inference_is_needed()
    {
        using var fixture = new ManifestFixture();
        MigrationAttemptManifest manifest = fixture.Manifest(4, controls: []);
        fixture.Write(manifest);
        RecoveryResult result = fixture.Recover();
        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual(MigrationRecoveryDisposition.Completed, result.Disposition);
        Assert.IsFalse(File.Exists(fixture.MarkerPath));
    }

    [TestMethod]
    public void CRUU20_003_New_protocol_marker_roundtrips_as_v5()
    {
        using var fixture = new ManifestFixture();
        MigrationAttemptManifest manifest = fixture.Manifest(5, V5Controls(Guid.Empty));
        RewriteControlAttemptId(manifest);
        fixture.Write(manifest);
        MigrationAttemptManifest persisted = new MigrationManifestRepository().TryReadStrict(fixture.MarkerPath)!;
        Assert.AreEqual(5, persisted.SchemaVersion);
        Assert.AreEqual(7, persisted.ControlArtifacts.Count);
    }

    [TestMethod]
    public void CRUU20_003_v5_requires_displaced_probe_control()
    {
        using var fixture = new ManifestFixture();
        MigrationAttemptManifest manifest = fixture.Manifest(5, V5Controls(Guid.Empty));
        RewriteControlAttemptId(manifest);
        manifest.ControlArtifacts.RemoveAll(control =>
            control.RelativePath.Contains("root-displaced", StringComparison.OrdinalIgnoreCase));
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Write(manifest));
    }

    [TestMethod]
    public void CRUU20_003_v5_requires_consistent_alternate_probe_content_authority()
    {
        using var fixture = new ManifestFixture();
        MigrationAttemptManifest manifest = fixture.Manifest(5, V5Controls(Guid.Empty));
        RewriteControlAttemptId(manifest);
        MigrationControlArtifact current = manifest.ControlArtifacts.Single(control =>
            control.RelativePath.Contains("root-current", StringComparison.OrdinalIgnoreCase));
        current.AlternateExpectedSha256Hex = Sha(CreateBytes);
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Write(manifest));
    }

    [TestMethod]
    public void CRUU20_003_Old_v4_reader_rejects_v5_by_schema_not_same_version_unknown_member()
    {
        using var fixture = new ManifestFixture();
        MigrationAttemptManifest manifest = fixture.Manifest(5, V5Controls(Guid.Empty));
        RewriteControlAttemptId(manifest);
        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        });

        InvalidDataException ex = Assert.ThrowsExactly<InvalidDataException>(() => FrozenV4Read(json));
        StringAssert.Contains(ex.Message, "schema version 5");
        Assert.IsTrue(json.Contains("alternateExpectedLength", StringComparison.Ordinal));
    }

    // ------------------------------ CRUU20-004 -----------------------------------------

    [TestMethod]
    public void CRUU20_004_Attributed_test_without_runtime_hit_fails_automatically()
    {
        TestResult result = ExecuteEvidenceFixture(nameof(EvidenceWithoutHit));
        Assert.AreEqual(UnitTestOutcome.Failed, result.Outcome);
        StringAssert.Contains(result.TestFailureException!.Message, "Evidence.Symbol");
    }

    [TestMethod]
    public void CRUU20_004_Attributed_expected_exception_test_without_hit_fails_automatically()
    {
        TestResult result = ExecuteEvidenceFixture(
            nameof(EvidenceExpectedExceptionWithoutHit),
            expectedException: typeof(InvalidOperationException));
        Assert.AreEqual(UnitTestOutcome.Failed, result.Outcome);
        StringAssert.Contains(result.TestFailureException!.Message, "Evidence.ExpectedException");
    }

    [TestMethod]
    public void CRUU20_004_Attributed_test_with_required_hit_passes_automatically()
    {
        TestResult result = ExecuteEvidenceFixture(nameof(EvidenceWithHit));
        Assert.AreEqual(UnitTestOutcome.Passed, result.Outcome);
    }

    [TestMethod]
    public void CRUU20_004_Multi_symbol_attribute_requires_every_symbol_hit()
    {
        TestResult result = ExecuteEvidenceFixture(nameof(EvidenceMissingSecondHit));
        Assert.AreEqual(UnitTestOutcome.Failed, result.Outcome);
        StringAssert.Contains(result.TestFailureException!.Message, "Evidence.Second");
    }

    [TestMethod]
    public void CRUU20_004_Evidence_enforcement_does_not_depend_on_AssertProductionHit_helper()
    {
        TestResult result = ExecuteEvidenceFixture(nameof(EvidenceWithHit));
        Assert.AreEqual(UnitTestOutcome.Passed, result.Outcome);
    }

    [TestMethod]
    [ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
    public void CRUU20_004_All_ProductionSymbolEvidence_tests_use_automatic_runtime_harness()
    {
        using var temp = new TestDirectory();
        Assert.IsTrue(new MigrationRecoveryService().RecoverForRetry(
            new MigrationRecoveryContext(temp.Root, ExpectedSourcePhysicalRoot: temp.Root)).Success);

        var offenders = typeof(Cruu20RegressionTests).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.GetCustomAttribute<TestMethodAttribute>() is not null)
                .Where(method => method.GetCustomAttributes<ProductionSymbolEvidenceAttribute>().Any())
                .Where(_ => type.GetCustomAttribute<ProductionEvidenceTestClassAttribute>() is null)
                .Select(method => $"{type.FullName}.{method.Name}"))
            .ToArray();
        Assert.AreEqual(0, offenders.Length, string.Join(Environment.NewLine, offenders));

        FindingCoverageMap map = FindingCoverageMap.Load();
        foreach (KeyValuePair<string, IReadOnlyList<string>> authority in map.RequiredProductionSymbols
                     .Where(pair => pair.Key.StartsWith("CRUU20-", StringComparison.Ordinal)))
        {
            var declared = map.Findings[authority.Key]
                .Select(name => typeof(Cruu20RegressionTests).GetMethod(name, BindingFlags.Public | BindingFlags.Instance))
                .Where(method => method is not null)
                .SelectMany(method => method!.GetCustomAttributes<ProductionSymbolEvidenceAttribute>())
                .Select(attribute => attribute.Symbol)
                .ToHashSet(StringComparer.Ordinal);
            foreach (string symbol in authority.Value)
            {
                Assert.IsTrue(declared.Contains(symbol),
                    $"{authority.Key} requires automatic runtime evidence for '{symbol}'.");
            }
        }
    }

    [TestMethod]
    public void CRUU20_004_Nested_evidence_capture_restores_previous_sink()
    {
        var outerHits = new List<string>();
        Action<string> outer = outerHits.Add;
        ProductionRuntimeEvidence.SinkForTests = outer;
        try
        {
            TestResult result = ExecuteEvidenceFixture(nameof(EvidenceWithHit));
            Assert.AreEqual(UnitTestOutcome.Passed, result.Outcome);
            CollectionAssert.Contains(outerHits, "Evidence.Symbol");
            Assert.AreSame(outer, ProductionRuntimeEvidence.SinkForTests);
        }
        finally
        {
            ProductionRuntimeEvidence.SinkForTests = null;
        }
    }

    // ------------------------------ Helpers --------------------------------------------

    private static ArtifactCleanupOutcome DeleteProbe(
        string root,
        string relativePath,
        byte[] primary,
        byte[]? alternate = null)
    {
        var ops = new DefaultMigrationFileOps();
        return ops.DeleteOwnedCapabilityProbeIfProven(
            root,
            Path.Combine(root, relativePath),
            primary.LongLength,
            Sha(primary),
            alternate?.LongLength,
            alternate is null ? null : Sha(alternate));
    }

    private static void AssertForeignProbePreserved(byte[] foreignBytes)
    {
        using var temp = new TestDirectory();
        string initial = Path.Combine(temp.Root, "probe-current.tmp");
        string ownedAlternate = Path.Combine(temp.Root, "probe-displaced.tmp");
        using (IOwnedCapabilityProbe probe = new DefaultCapabilityFileOps().CreateOwnedProbe(
                   temp.Root, initial, ownedAlternate, CreateBytes, recordDurableOwnership: true))
        {
            probe.Write(CreateBytes);
            probe.FlushDurable();
        }
        File.Move(initial, ownedAlternate);
        File.WriteAllBytes(initial, foreignBytes);

        Assert.AreEqual(ArtifactCleanupOutcome.PreservedUnproven,
            DeleteProbe(temp.Root, "probe-current.tmp", CreateBytes));
        CollectionAssert.AreEqual(foreignBytes, File.ReadAllBytes(initial));
        Assert.AreEqual(ArtifactCleanupOutcome.DeletedProvenOwned,
            DeleteProbe(temp.Root, "probe-displaced.tmp", CreateBytes));
    }

    private static CrashRun RunCrash(string cut, Action<string>? prepare = null)
    {
        var directory = new TestDirectory();
        prepare?.Invoke(directory.Root);
        string signal = Path.Combine(directory.Root, "crash-ready.signal");

        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(
                RepositoryTestPaths.Root,
                "tests",
                "PromptHelper.CrashHarness",
                "bin",
                "Release",
                "net10.0-windows",
                "PromptHelper.CrashHarness.exe"),
            WorkingDirectory = RepositoryTestPaths.Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        Assert.IsTrue(File.Exists(start.FileName),
            $"Hard-crash helper is not built: '{start.FileName}'.");
        start.Environment["PROMPTHELPER_CRASH_CUT"] = cut;
        start.Environment["PROMPTHELPER_CRASH_ROOT"] = directory.Root;
        start.Environment["PROMPTHELPER_CRASH_SIGNAL"] = signal;

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start hard-crash child testhost.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();

        var timeout = Stopwatch.StartNew();
        while (!File.Exists(signal) && !process.HasExited && timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            Thread.Sleep(25);
        }

        if (!File.Exists(signal))
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            process.WaitForExit(10_000);
            string output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            directory.Dispose();
            Assert.Fail($"Hard-crash child did not reach '{cut}'. Output:{Environment.NewLine}{output}");
        }

        process.Kill(entireProcessTree: true);
        Assert.IsTrue(process.WaitForExit(10_000), "Hard-crash child process tree did not terminate.");
        Assert.AreNotEqual(0, process.ExitCode, "The child must be killed, not unwind through cleanup.");
        WaitForKilledProcessHandles(directory.Root, signal);
        File.Delete(signal);
        return new CrashRun(directory);
    }

    private static void WaitForKilledProcessHandles(string root, string signal)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            bool allReleased = true;
            foreach (string file in Directory.GetFiles(root).Where(path =>
                         !string.Equals(path, signal, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    using var stream = new FileStream(
                        file,
                        FileMode.Open,
                        FileAccess.ReadWrite,
                        FileShare.None);
                }
                catch (IOException)
                {
                    allReleased = false;
                    break;
                }
            }

            if (allReleased)
            {
                return;
            }
            Thread.Sleep(25);
        }

        Assert.Fail("A killed hard-crash child retained a filesystem handle for more than ten seconds.");
    }

    private static List<MigrationControlArtifact> LegacyProbeControls(Guid attemptId)
    {
        string id = attemptId == Guid.Empty ? new string('0', 32) : attemptId.ToString("N");
        return
        [
            new MigrationControlArtifact
            {
                RelativePath = $".prompthelper-probe-{id}-root-current.tmp",
                Kind = MigrationControlArtifactKind.CapabilityProbeFile,
                ExpectedLength = CreateBytes.LongLength,
                ExpectedSha256Hex = Sha(CreateBytes)
            }
        ];
    }

    private static List<MigrationControlArtifact> V5Controls(Guid attemptId)
    {
        string id = attemptId == Guid.Empty ? new string('0', 32) : attemptId.ToString("N");
        var controls = new List<MigrationControlArtifact>
        {
            new()
            {
                RelativePath = $".prompthelper-migration.stage-{id}.tmp",
                Kind = MigrationControlArtifactKind.ManifestPhaseStaging
            }
        };
        AddTriplet(controls, id, "root", string.Empty);
        AddTriplet(controls, id, "prompts", "prompts");
        return controls;
    }

    private static void AddTriplet(List<MigrationControlArtifact> controls, string id, string prefix, string directory)
    {
        string PathFor(string phase)
        {
            string file = $".prompthelper-probe-{id}-{prefix}-{phase}.tmp";
            return directory.Length == 0 ? file : Path.Combine(directory, file);
        }
        controls.Add(new MigrationControlArtifact
        {
            RelativePath = PathFor("current"),
            Kind = MigrationControlArtifactKind.CapabilityProbeFile,
            ExpectedLength = CreateBytes.LongLength,
            ExpectedSha256Hex = Sha(CreateBytes),
            AlternateExpectedLength = ReplaceBytes.LongLength,
            AlternateExpectedSha256Hex = Sha(ReplaceBytes)
        });
        controls.Add(new MigrationControlArtifact
        {
            RelativePath = PathFor("replacement"),
            Kind = MigrationControlArtifactKind.CapabilityProbeFile,
            ExpectedLength = ReplaceBytes.LongLength,
            ExpectedSha256Hex = Sha(ReplaceBytes)
        });
        controls.Add(new MigrationControlArtifact
        {
            RelativePath = PathFor("displaced"),
            Kind = MigrationControlArtifactKind.CapabilityProbeFile,
            ExpectedLength = CreateBytes.LongLength,
            ExpectedSha256Hex = Sha(CreateBytes)
        });
    }

    private static void RewriteControlAttemptId(MigrationAttemptManifest manifest)
    {
        string from = new string('0', 32);
        string to = manifest.AttemptId.ToString("N");
        foreach (MigrationControlArtifact control in manifest.ControlArtifacts)
        {
            control.RelativePath = control.RelativePath.Replace(from, to, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void FrozenV4Read(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        int version = document.RootElement.GetProperty("schemaVersion").GetInt32();
        if (version > 4 || version < 3)
        {
            throw new InvalidDataException($"Unsupported migration manifest schema version {version} for frozen v4 reader.");
        }
        throw new AssertFailedException("Frozen v4 reader unexpectedly accepted the newer schema.");
    }

    [ProductionSymbolEvidence("Evidence.Symbol")]
    private static void EvidenceWithoutHit() { }

    [ProductionSymbolEvidence("Evidence.ExpectedException")]
    private static void EvidenceExpectedExceptionWithoutHit() => throw new InvalidOperationException("expected");

    [ProductionSymbolEvidence("Evidence.Symbol")]
    private static void EvidenceWithHit() => ProductionRuntimeEvidence.Hit("Evidence.Symbol");

    [ProductionSymbolEvidence("Evidence.First")]
    [ProductionSymbolEvidence("Evidence.Second")]
    private static void EvidenceMissingSecondHit() => ProductionRuntimeEvidence.Hit("Evidence.First");

    private static TestResult ExecuteEvidenceFixture(string methodName, Type? expectedException = null)
    {
        MethodInfo method = typeof(Cruu20RegressionTests).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Evidence fixture '{methodName}' not found.");
        var fake = new FakeTestMethod(method, expectedException);
        var wrapper = new AutomaticProductionEvidenceTestMethodAttribute(new TestMethodAttribute());
        return wrapper.ExecuteAsync(fake).GetAwaiter().GetResult().Single();
    }

    private sealed class FakeTestMethod(MethodInfo method, Type? expectedException) : ITestMethod
    {
        public string TestMethodName => method.Name;
        public string TestClassName => method.DeclaringType!.FullName!;
        public Type ReturnType => method.ReturnType;
        public object[] Arguments => [];
        public ParameterInfo[] ParameterTypes => method.GetParameters();
        public MethodInfo MethodInfo => method;

        public Task<TestResult> InvokeAsync(object[]? arguments)
        {
            try
            {
                method.Invoke(null, arguments ?? []);
                return Task.FromResult(new TestResult { Outcome = UnitTestOutcome.Passed });
            }
            catch (TargetInvocationException ex) when (
                expectedException is not null &&
                ex.InnerException?.GetType() == expectedException)
            {
                return Task.FromResult(new TestResult { Outcome = UnitTestOutcome.Passed });
            }
            catch (TargetInvocationException ex)
            {
                return Task.FromResult(new TestResult
                {
                    Outcome = UnitTestOutcome.Failed,
                    TestFailureException = ex.InnerException ?? ex
                });
            }
        }

        public Attribute[] GetAllAttributes() => method.GetCustomAttributes().OfType<Attribute>().ToArray();
        public TAttributeType[] GetAttributes<TAttributeType>() where TAttributeType : Attribute =>
            method.GetCustomAttributes<TAttributeType>().ToArray();
    }
}
