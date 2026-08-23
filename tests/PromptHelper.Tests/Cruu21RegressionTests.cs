using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
public sealed class Cruu21RegressionTests
{
    private static readonly string[] InitialMarkerCuts =
    [
        "WindowsMigrationMarkerAuthority.InitialAfterCreateBeforeWrite",
        "WindowsMigrationMarkerAuthority.InitialDuringWrite",
        "WindowsMigrationMarkerAuthority.InitialAfterWriteBeforeFlush",
        "WindowsMigrationMarkerAuthority.InitialAfterFlushBeforeCommit",
        "WindowsMigrationMarkerAuthority.InitialAfterCommit"
    ];

    // ------------------------------ CRUU21-001 -----------------------------------------

    [TestMethod]
    [HardCrashEvidence("WindowsMigrationMarkerAuthority.InitialAfterCreateBeforeWrite", "WindowsMigrationMarkerAuthority.CreateInitial")]
    [ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
    public void CRUU21_001_Hard_crash_after_initial_marker_create_before_write_leaves_no_final_marker()
    {
        using MarkerCrashRun run = RunMarkerCrash(InitialMarkerCuts[0]);
        Assert.IsFalse(File.Exists(run.MarkerPath));
        AssertRecoverySucceeds(run);
    }

    [TestMethod]
    [HardCrashEvidence("WindowsMigrationMarkerAuthority.InitialDuringWrite", "WindowsMigrationMarkerAuthority.CreateInitial")]
    [ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
    public void CRUU21_001_Hard_crash_during_initial_Copying_marker_write_leaves_no_truncated_final_marker()
    {
        using MarkerCrashRun run = RunMarkerCrash(InitialMarkerCuts[1]);
        Assert.IsFalse(File.Exists(run.MarkerPath));
        AssertRecoverySucceeds(run);
    }

    [TestMethod]
    [HardCrashEvidence("WindowsMigrationMarkerAuthority.InitialAfterWriteBeforeFlush", "WindowsMigrationMarkerAuthority.CreateInitial")]
    [ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
    public void CRUU21_001_Hard_crash_after_initial_marker_write_before_flush_is_retryable()
    {
        using MarkerCrashRun run = RunMarkerCrash(InitialMarkerCuts[2]);
        Assert.IsFalse(File.Exists(run.MarkerPath));
        AssertRecoverySucceeds(run);
    }

    [TestMethod]
    [HardCrashEvidence("WindowsMigrationMarkerAuthority.InitialAfterCommit", "WindowsMigrationMarkerAuthority.CreateInitial")]
    [ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
    public void CRUU21_001_Hard_crash_after_initial_marker_commit_leaves_strictly_parseable_Copying_marker()
    {
        using MarkerCrashRun run = RunMarkerCrash(InitialMarkerCuts[4]);
        MigrationAttemptManifest manifest = new MigrationManifestRepository().TryReadStrict(run.MarkerPath)!;
        Assert.IsNotNull(manifest);
        Assert.AreEqual(MigrationManifestPhase.Copying, manifest.Phase);
        AssertRecoverySucceeds(run);
    }

    [TestMethod]
    [ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
    public void CRUU21_001_Initial_Copying_marker_has_no_partial_authoritative_final_state()
    {
        foreach (string cut in InitialMarkerCuts)
        {
            using MarkerCrashRun run = RunMarkerCrash(cut);
            if (File.Exists(run.MarkerPath))
            {
                Assert.AreEqual(
                    MigrationManifestPhase.Copying,
                    new MigrationManifestRepository().TryReadStrict(run.MarkerPath)!.Phase);
            }
            AssertRecoverySucceeds(run);
        }
    }

    [TestMethod]
    [HardCrashEvidence("WindowsMigrationMarkerAuthority.InitialAfterFlushBeforeCommit", "WindowsMigrationMarkerAuthority.CreateInitial")]
    [ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
    public void CRUU21_001_Initial_marker_crash_before_payload_copy_does_not_wedge_target()
    {
        using MarkerCrashRun run = RunMarkerCrash(InitialMarkerCuts[3]);
        AssertRecoverySucceeds(run);
        Assert.IsFalse(File.Exists(run.MarkerPath));
        Assert.AreEqual(0, Directory.GetFiles(run.TargetRoot).Length);
    }

    [TestMethod]
    [ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
    public void CRUU21_001_Real_transition_retry_recovers_each_initial_marker_crash_cut()
    {
        foreach (string cut in InitialMarkerCuts)
        {
            using MarkerCrashRun run = RunMarkerCrash(cut, operation: "real-transition");
            AssertRecoverySucceeds(run);
            Assert.IsFalse(File.Exists(run.MarkerPath));
        }
    }

    // ------------------------------ CRUU21-002 -----------------------------------------

    [TestMethod]
    [ProductionSymbolEvidence("WindowsMigrationMarkerAuthority.ReplaceIfExpected")]
    public void CRUU21_002_Ready_marker_update_rejects_same_path_foreign_replacement()
    {
        using var fixture = new MarkerFixture();
        fixture.PublishCopying();
        byte[] foreign = Encoding.UTF8.GetBytes("foreign marker");
        ReplaceObject(fixture.MarkerPath, foreign);

        Assert.ThrowsExactly<StaleExpectedFileException>(fixture.PublishReady);
        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(fixture.MarkerPath));
    }

    [TestMethod]
    [ProductionSymbolEvidence("WindowsMigrationMarkerAuthority.ReplaceIfExpected")]
    public void CRUU21_002_Ready_marker_update_preserves_same_bytes_different_identity_replacement()
    {
        using var fixture = new MarkerFixture();
        fixture.PublishCopying();
        byte[] sameBytes = File.ReadAllBytes(fixture.MarkerPath);
        WindowsFileIdentity original = Identity(fixture.MarkerPath, fixture.Target.Root);
        ReplaceObject(fixture.MarkerPath, sameBytes);
        Assert.AreNotEqual(original, Identity(fixture.MarkerPath, fixture.Target.Root));

        Assert.ThrowsExactly<StaleExpectedFileException>(fixture.PublishReady);
        CollectionAssert.AreEqual(sameBytes, File.ReadAllBytes(fixture.MarkerPath));
    }

    [TestMethod]
    [ProductionSymbolEvidence("WindowsMigrationMarkerAuthority.ReplaceIfExpected")]
    public void CRUU21_002_Ready_marker_update_requires_exact_Copying_marker_identity()
    {
        using var fixture = new MarkerFixture();
        fixture.PublishCopying();
        ReplaceObject(fixture.MarkerPath, File.ReadAllBytes(fixture.MarkerPath));
        Assert.ThrowsExactly<StaleExpectedFileException>(fixture.PublishReady);
    }

    [TestMethod]
    [ProductionSymbolEvidence("WindowsMigrationMarkerAuthority.DeleteCurrent")]
    public void CRUU21_002_DeleteStrict_preserves_same_attempt_phase_foreign_marker_identity()
    {
        using var fixture = new MarkerFixture();
        fixture.PublishReadyDirect();
        byte[] bytes = File.ReadAllBytes(fixture.MarkerPath);
        ReplaceObject(fixture.MarkerPath, bytes);

        Assert.ThrowsExactly<StaleExpectedFileException>(() => fixture.Repository.DeleteStrict(
            fixture.MarkerPath,
            fixture.Manifest.AttemptId,
            MigrationManifestPhase.ReadyToCommit));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(fixture.MarkerPath));
    }

    [TestMethod]
    [ProductionSymbolEvidence("WindowsMigrationMarkerAuthority.DeleteCurrent")]
    public void CRUU21_002_DeleteStrict_preserves_byte_identical_foreign_marker_identity()
    {
        using var fixture = new MarkerFixture();
        fixture.PublishReadyDirect();
        byte[] bytes = File.ReadAllBytes(fixture.MarkerPath);
        WindowsFileIdentity owned = Identity(fixture.MarkerPath, fixture.Target.Root);
        ReplaceObject(fixture.MarkerPath, bytes);
        Assert.AreNotEqual(owned, Identity(fixture.MarkerPath, fixture.Target.Root));

        Assert.ThrowsExactly<StaleExpectedFileException>(() => fixture.Repository.DeleteStrict(
            fixture.MarkerPath,
            fixture.Manifest.AttemptId,
            fixture.Manifest.Phase));
        Assert.IsTrue(File.Exists(fixture.MarkerPath));
    }

    [TestMethod]
    [ProductionSymbolEvidence("WindowsMigrationMarkerAuthority.DeleteCurrent")]
    public void CRUU21_002_DeleteStrict_requires_durable_marker_identity_not_attempt_phase_only()
    {
        using var fixture = new MarkerFixture();
        fixture.PublishReadyDirect();
        ReplaceObject(fixture.MarkerPath, File.ReadAllBytes(fixture.MarkerPath));
        Assert.ThrowsExactly<StaleExpectedFileException>(() => fixture.Repository.DeleteStrict(
            fixture.MarkerPath,
            fixture.Manifest.AttemptId,
            fixture.Manifest.Phase));
    }

    [TestMethod]
    public void CRUU21_002_Copying_marker_identity_survives_restart()
    {
        using var fixture = new MarkerFixture();
        fixture.PublishCopying();
        WindowsFileIdentity identity = Identity(fixture.MarkerPath, fixture.Target.Root);

        OwnedArtifactReconciler.Result result = OwnedArtifactReconciler.Reconcile(
            fixture.Target.Root,
            new WindowsOwnedArtifactJournal());
        Assert.IsFalse(result.HasFatal);
        Assert.AreEqual(identity, Identity(fixture.MarkerPath, fixture.Target.Root));
        fixture.Repository.AssertPersistedMarkerMatches(fixture.MarkerPath, fixture.Manifest);
    }

    [TestMethod]
    public void CRUU21_002_Ready_marker_identity_survives_restart()
    {
        using var fixture = new MarkerFixture();
        fixture.PublishCopying();
        fixture.PublishReady();
        WindowsFileIdentity identity = Identity(fixture.MarkerPath, fixture.Target.Root);

        OwnedArtifactReconciler.Result result = OwnedArtifactReconciler.Reconcile(
            fixture.Target.Root,
            new WindowsOwnedArtifactJournal());
        Assert.IsFalse(result.HasFatal);
        Assert.AreEqual(identity, Identity(fixture.MarkerPath, fixture.Target.Root));
        fixture.Repository.AssertPersistedMarkerMatches(fixture.MarkerPath, fixture.Manifest);
    }

    [TestMethod]
    public void CRUU21_002_Marker_identity_authority_advances_atomically_Copying_to_Ready()
    {
        using var fixture = new MarkerFixture();
        fixture.PublishCopying();
        WindowsFileIdentity copying = Identity(fixture.MarkerPath, fixture.Target.Root);
        fixture.PublishReady();
        WindowsFileIdentity ready = Identity(fixture.MarkerPath, fixture.Target.Root);
        Assert.AreNotEqual(copying, ready);

        OwnedArtifactRecord authority = new WindowsOwnedArtifactJournal().Read(fixture.Target.Root).Records
            .Where(record => record.Kind == OwnedArtifactKind.MigrationMarker &&
                             record.MarkerAttemptId == fixture.Manifest.AttemptId)
            .OrderByDescending(record => record.Phase)
            .First();
        Assert.AreEqual(OwnedArtifactPhase.MarkerPublishedReady, authority.Phase);
        Assert.AreEqual(ready, authority.Identity);

        using (MarkerCrashRun interrupted = RunMarkerCrash(
                   "WindowsMigrationMarkerAuthority.AfterCopyingSidelineBeforeReadyPublish",
                   operation: "ready-transition"))
        {
            OwnedArtifactReconciler.Result rollback = OwnedArtifactReconciler.Reconcile(
                interrupted.TargetRoot,
                new WindowsOwnedArtifactJournal());
            Assert.IsFalse(rollback.HasFatal);
            Assert.AreEqual(
                MigrationManifestPhase.Copying,
                new MigrationManifestRepository().TryReadStrict(interrupted.MarkerPath)!.Phase);
        }

        using (MarkerCrashRun committed = RunMarkerCrash(
                   "WindowsMigrationMarkerAuthority.AfterReadyPublishBeforeAuthorityAdvance",
                   operation: "ready-transition"))
        {
            OwnedArtifactReconciler.Result completion = OwnedArtifactReconciler.Reconcile(
                committed.TargetRoot,
                new WindowsOwnedArtifactJournal());
            Assert.IsFalse(completion.HasFatal);
            Assert.AreEqual(
                MigrationManifestPhase.ReadyToCommit,
                new MigrationManifestRepository().TryReadStrict(committed.MarkerPath)!.Phase);
        }
    }

    [TestMethod]
    public void CRUU21_002_No_marker_write_path_unconditionally_replaces_current_marker_path()
    {
        string repositorySource = File.ReadAllText(Path.Combine(
            RepositoryTestPaths.Root,
            "src",
            "PromptHelper",
            "Services",
            "MigrationManifestRepository.cs"));
        Assert.IsFalse(repositorySource.Contains(
            "PromoteReplaceExact(markerPath)",
            StringComparison.Ordinal));
        StringAssert.Contains(repositorySource, "ReplaceMarkerIfExpected");
    }

    // ------------------------------ CRUU21-003 -----------------------------------------

    [TestMethod]
    [ProductionSymbolEvidence("WindowsMigrationMarkerAuthority.CreateInitial")]
    public void CRUU21_003_Release_claim_distinguishes_process_kill_from_power_loss()
    {
        using var fixture = new MarkerFixture();
        fixture.PublishCopying();
        string readme = File.ReadAllText(Path.Combine(RepositoryTestPaths.Root, "README.md"));
        StringAssert.Contains(readme, "Process termination");
        StringAssert.Contains(readme, "physical power loss");
        StringAssert.Contains(readme, "outside the verified automatic-recovery guarantee");
        StringAssert.Contains(readme, "must not be presented as proof of power-loss durability");
    }

    // ------------------------------ CRUU21-004 -----------------------------------------

    [TestMethod]
    public void CRUU21_004_CRUU20_001_each_hard_crash_sentinel_has_child_cut_authority()
    {
        MethodInfo[] methods = HardCrashMethods(typeof(Cruu20RegressionTests), "CRUU20_001_Crash_");
        Assert.AreEqual(5, methods.Length);
        Assert.IsTrue(methods.All(method =>
            method.GetCustomAttribute<HardCrashEvidenceAttribute>()!.ChildProductionSymbols
                .Contains("DefaultCapabilityFileOps.CreateOwnedProbe")));
    }

    [TestMethod]
    public void CRUU21_004_CRUU20_001_retry_sentinels_execute_real_MigrationRecoveryService()
    {
        MethodInfo[] methods = HardCrashMethods(typeof(Cruu20RegressionTests), "CRUU20_001_Crash_");
        Assert.IsTrue(methods.All(method => method
            .GetCustomAttributes<ProductionSymbolEvidenceAttribute>()
            .Any(attribute => attribute.Symbol == "MigrationRecoveryService.RecoverForRetry")));
    }

    [TestMethod]
    public void CRUU21_004_CRUU20_002_each_kill_sentinel_is_runtime_bound_to_its_production_creator()
    {
        MethodInfo[] methods = HardCrashMethods(typeof(Cruu20RegressionTests), "CRUU20_002_");
        Assert.AreEqual(6, methods.Length);
        Assert.IsTrue(methods.All(method =>
            method.GetCustomAttribute<HardCrashEvidenceAttribute>()!.ChildProductionSymbols.Count > 0));
    }

    [TestMethod]
    public void CRUU21_004_Normal_success_test_cannot_substitute_for_hard_crash_runtime_authority()
    {
        using JsonDocument map = ReadCoverageMap();
        string[] mapped = map.RootElement.GetProperty("requiredCrashEvidence")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        Assert.IsFalse(mapped.Contains("CRUU20_002_First_claim_protocol_is_crash_atomic_not_only_exception_safe"));
        Assert.IsTrue(mapped.All(name => FindTest(name).GetCustomAttribute<HardCrashEvidenceAttribute>() is not null));
    }

    [TestMethod]
    [ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
    public void CRUU21_004_CrashHarness_signal_contains_exact_production_cut_identity()
    {
        using MarkerCrashRun run = RunMarkerCrash(InitialMarkerCuts[0]);
        Assert.AreEqual(InitialMarkerCuts[0], run.Evidence.Cut);
        CollectionAssert.Contains(
            run.Evidence.ProductionSymbols,
            "WindowsMigrationMarkerAuthority.CreateInitial");
        AssertRecoverySucceeds(run);
    }

    [TestMethod]
    public void CRUU21_004_Hard_crash_evidence_map_is_per_sentinel_not_only_per_finding()
    {
        using JsonDocument map = ReadCoverageMap();
        foreach (JsonProperty entry in map.RootElement.GetProperty("requiredCrashEvidence").EnumerateObject())
        {
            MethodInfo method = FindTest(entry.Name);
            HardCrashEvidenceAttribute declared = method.GetCustomAttribute<HardCrashEvidenceAttribute>()!;
            Assert.AreEqual(entry.Value.GetProperty("cut").GetString(), declared.Cut);
            CollectionAssert.AreEquivalent(
                entry.Value.GetProperty("childProductionSymbols").EnumerateArray()
                    .Select(element => element.GetString()!).ToArray(),
                declared.ChildProductionSymbols.ToArray());
        }
    }

    [TestMethod]
    public void CRUU21_004_Release_gate_validates_child_and_parent_crash_evidence()
    {
        string gate = File.ReadAllText(Path.Combine(
            RepositoryTestPaths.Root,
            "tools",
            "VerifyFindingCoverage.ps1"));
        StringAssert.Contains(gate, "requiredCrashEvidence");
        StringAssert.Contains(gate, "childProductionSymbols");
        StringAssert.Contains(gate, "parentProductionSymbols");

        string workflow = File.ReadAllText(Path.Combine(
            RepositoryTestPaths.Root,
            ".github",
            "workflows",
            "release.yml"));
        StringAssert.Contains(workflow, "VerifyFindingCoverage.ps1");
        StringAssert.Contains(workflow, "VerifyTestEvidence.ps1");
    }

    private static void AssertRecoverySucceeds(MarkerCrashRun run)
    {
        RecoveryResult result = new MigrationRecoveryService().RecoverForRetry(
            new MigrationRecoveryContext(
                run.TargetRoot,
                ExpectedSourcePhysicalRoot: run.SourceRoot));
        Assert.IsTrue(result.Success, result.ErrorMessage);
    }

    private static MarkerCrashRun RunMarkerCrash(string cut, string? operation = null)
    {
        var directory = new TestDirectory();
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
        start.Environment["PROMPTHELPER_CRASH_CUT"] = cut;
        start.Environment["PROMPTHELPER_CRASH_ROOT"] = directory.Root;
        start.Environment["PROMPTHELPER_CRASH_SIGNAL"] = signal;
        if (operation is not null)
        {
            start.Environment["PROMPTHELPER_CRASH_OPERATION"] = operation;
        }

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start hard-crash child.");
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
        Assert.IsTrue(process.WaitForExit(10_000));
        Assert.AreNotEqual(0, process.ExitCode);
        WaitForHandles(directory.Root, signal);

        CrashSignal evidence = JsonSerializer.Deserialize<CrashSignal>(File.ReadAllBytes(signal))!;
        Assert.AreEqual(cut, evidence.Cut);
        CollectionAssert.Contains(
            evidence.ProductionSymbols,
            "WindowsMigrationMarkerAuthority.CreateInitial");
        File.Delete(signal);
        return new MarkerCrashRun(directory, evidence);
    }

    private static void WaitForHandles(string root, string signal)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            bool released = true;
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                         .Where(file => !string.Equals(file, signal, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    released = false;
                    break;
                }
            }
            if (released)
            {
                return;
            }
            Thread.Sleep(25);
        }
        Assert.Fail("Killed crash-harness process retained filesystem handles.");
    }

    private static WindowsFileIdentity Identity(string path, string root)
    {
        using WindowsExpectedTargetAuthority authority =
            WindowsExpectedTargetAuthority.Open(path, root)!;
        return authority.Identity;
    }

    private static void ReplaceObject(string target, byte[] bytes)
    {
        string scratch = Path.Combine(Path.GetDirectoryName(target)!, $"foreign-{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(scratch, bytes);
        File.Move(scratch, target, overwrite: true);
    }

    private static MethodInfo[] HardCrashMethods(Type type, string prefix) =>
        type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name.StartsWith(prefix, StringComparison.Ordinal) &&
                             method.GetCustomAttribute<HardCrashEvidenceAttribute>() is not null)
            .ToArray();

    private static MethodInfo FindTest(string name) =>
        typeof(Cruu20RegressionTests).GetMethod(name) ??
        typeof(Cruu21RegressionTests).GetMethod(name) ??
        throw new AssertFailedException($"Mapped hard-crash sentinel '{name}' does not exist.");

    private static JsonDocument ReadCoverageMap() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
        RepositoryTestPaths.Root,
        "tools",
        "FindingCoverageMap.json")));

    private sealed class MarkerFixture : IDisposable
    {
        public TestDirectory Source { get; } = new();
        public TestDirectory Target { get; } = new();
        public MigrationManifestRepository Repository { get; } = new();
        public string MarkerPath => Path.Combine(Target.Root, ".prompthelper-migration.json");
        public MigrationAttemptManifest Manifest { get; }

        public MarkerFixture()
        {
            Manifest = new MigrationAttemptManifest
            {
                SchemaVersion = MigrationAttemptManifest.CurrentSchemaVersion,
                AttemptId = Guid.NewGuid(),
                SourcePhysicalRoot = Source.Root,
                TargetPhysicalRoot = Target.Root,
                SourceLibrarySha256Hex = new string('0', 64),
                SourcePayloadFingerprintSha256Hex = new string('0', 64),
                Phase = MigrationManifestPhase.Copying,
                Artifacts = [],
                ControlArtifacts = [],
                TargetBaseline = new MigrationTargetBaseline(true, true, true)
            };
        }

        public void PublishCopying()
        {
            Manifest.Phase = MigrationManifestPhase.Copying;
            Repository.CreateInitialCopyingManifestDurable(MarkerPath, Manifest);
        }

        public void PublishReady()
        {
            Manifest.Phase = MigrationManifestPhase.ReadyToCommit;
            Repository.WriteReadyManifestDurable(MarkerPath, Manifest);
        }

        public void PublishReadyDirect()
        {
            Manifest.Phase = MigrationManifestPhase.ReadyToCommit;
            Repository.WriteReadyManifestDurable(MarkerPath, Manifest);
        }

        public void Dispose()
        {
            Target.Dispose();
            Source.Dispose();
        }
    }

    private sealed class MarkerCrashRun(TestDirectory directory, CrashSignal evidence) : IDisposable
    {
        public string SourceRoot => Path.Combine(directory.Root, "source");
        public string TargetRoot => Path.Combine(directory.Root, "target");
        public string MarkerPath => Path.Combine(TargetRoot, ".prompthelper-migration.json");
        public CrashSignal Evidence { get; } = evidence;
        public void Dispose() => directory.Dispose();
    }

    private sealed class CrashSignal
    {
        [JsonPropertyName("cut")]
        public string Cut { get; set; } = string.Empty;

        [JsonPropertyName("productionSymbols")]
        public string[] ProductionSymbols { get; set; } = [];
    }
}
