using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PromptHelper.Models;
using PromptHelper.Services;

string cut = Environment.GetEnvironmentVariable("PROMPTHELPER_CRASH_CUT")
    ?? throw new InvalidOperationException("PROMPTHELPER_CRASH_CUT is required.");
string root = Environment.GetEnvironmentVariable("PROMPTHELPER_CRASH_ROOT")
    ?? throw new InvalidOperationException("PROMPTHELPER_CRASH_ROOT is required.");
string signal = Environment.GetEnvironmentVariable("PROMPTHELPER_CRASH_SIGNAL")
    ?? throw new InvalidOperationException("PROMPTHELPER_CRASH_SIGNAL is required.");
byte[] createBytes = Encoding.UTF8.GetBytes("create");
byte[] replaceBytes = Encoding.UTF8.GetBytes("replace");
var productionSymbols = new HashSet<string>(StringComparer.Ordinal);
ProductionRuntimeEvidence.SinkForTests = symbol => productionSymbols.Add(symbol);

ProductionCrashCut.SinkForTests = observed =>
{
    if (!string.Equals(observed, cut, StringComparison.Ordinal))
    {
        return;
    }

    string signalStage = signal + $".writing-{Environment.ProcessId}";
    using (var stream = new FileStream(signalStage, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            cut = observed,
            productionSymbols = productionSymbols.OrderBy(value => value).ToArray()
        });
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }
    File.Move(signalStage, signal);

    Thread.Sleep(Timeout.Infinite);
};

string? crashOperation = Environment.GetEnvironmentVariable("PROMPTHELPER_CRASH_OPERATION");
if (string.Equals(crashOperation, "real-transition", StringComparison.Ordinal))
{
    RunRealTransition();
    throw new InvalidOperationException(
        $"Real transition returned without reaching hard-crash cut '{cut}'.");
}
if (string.Equals(crashOperation, "ready-transition", StringComparison.Ordinal))
{
    RunReadyTransition();
    throw new InvalidOperationException(
        $"Ready transition returned without reaching hard-crash cut '{cut}'.");
}

switch (cut)
{
    case "WindowsMigrationMarkerAuthority.InitialAfterCreateBeforeWrite":
    case "WindowsMigrationMarkerAuthority.InitialDuringWrite":
    case "WindowsMigrationMarkerAuthority.InitialAfterWriteBeforeFlush":
    case "WindowsMigrationMarkerAuthority.InitialAfterFlushBeforeCommit":
    case "WindowsMigrationMarkerAuthority.InitialAfterCommit":
        CreateInitialMarker();
        break;

    case "WindowsAtomicExpectedFileReplacer.AfterCreateBeforeFirstClaim":
        byte[] oldBytes = File.ReadAllBytes(Path.Combine(root, "library.json"));
        new WindowsAtomicExpectedFileReplacer().ReplaceIfExpected(
            root,
            Path.Combine(root, "library.json"),
            ExpectedFileState.Present(Convert.ToHexStringLower(SHA256.HashData(oldBytes))),
            Encoding.UTF8.GetBytes("new"),
            DurableFileClass.LibraryMetadata);
        break;

    case "DefaultMigrationFileOps.AfterCreateBeforeFirstClaim":
    case "WindowsOwnedArtifactJournal.AfterPartialFirstAppend":
        _ = new DefaultMigrationFileOps().CreateOwnedStage(
            root,
            Path.Combine(root, "payload-stage.tmp"));
        break;

    case "DefaultMigrationManifestFileOps.AfterCreateBeforeFirstClaim":
        _ = new DefaultMigrationManifestFileOps().CreateOwnedStage(
            root,
            Path.Combine(root, "manifest-stage.tmp"));
        break;

    case "DefaultCapabilityFileOps.AfterCreateBeforeFirstClaim":
        _ = CreateProbe();
        break;

    case "WindowsOwnedDirectoryCreator.AfterCreateBeforeFirstClaim":
        _ = new WindowsOwnedDirectoryCreator().TryCreateOwned(Path.Combine(root, "prompts"));
        break;

    case "Cruu20.ProbeAfterClaimBeforeWrite":
        _ = CreateProbe();
        ProductionCrashCut.Hit(cut);
        break;

    case "Cruu20.ProbeDuringPartialWrite":
    {
        IOwnedCapabilityProbe probe = CreateProbe();
        probe.Write(createBytes.AsSpan(0, 2));
        ProductionCrashCut.Hit(cut);
        break;
    }

    case "Cruu20.ProbeAfterWriteBeforeFlush":
    {
        IOwnedCapabilityProbe probe = CreateProbe();
        probe.Write(createBytes);
        ProductionCrashCut.Hit(cut);
        break;
    }

    case "DefaultCapabilityFileOps.AfterRenameToDisplacedBeforeRecord":
    {
        IOwnedCapabilityProbe probe = CreateProbe();
        probe.Write(createBytes);
        probe.FlushDurable();
        probe.RenameNoOverwriteRetainingOwnership(Path.Combine(root, "probe-displaced.tmp"));
        break;
    }

    case "DefaultCapabilityFileOps.AfterRenameToCurrentBeforeRecord":
    {
        IOwnedCapabilityProbe probe = new DefaultCapabilityFileOps().CreateOwnedProbe(
            root,
            Path.Combine(root, "probe-replacement.tmp"),
            Path.Combine(root, "probe-current.tmp"),
            replaceBytes,
            recordDurableOwnership: true);
        probe.Write(replaceBytes);
        probe.FlushDurable();
        probe.RenameNoOverwriteRetainingOwnership(Path.Combine(root, "probe-current.tmp"));
        break;
    }

    default:
        throw new InvalidOperationException($"Unknown hard-crash cut '{cut}'.");
}

throw new InvalidOperationException(
    $"Hard-crash cut '{cut}' returned instead of blocking for parent termination.");

IOwnedCapabilityProbe CreateProbe() =>
    new DefaultCapabilityFileOps().CreateOwnedProbe(
        root,
        Path.Combine(root, "probe-current.tmp"),
        Path.Combine(root, "probe-displaced.tmp"),
        createBytes,
        recordDurableOwnership: true);

void CreateInitialMarker()
{
    string source = Path.Combine(root, "source");
    string target = Path.Combine(root, "target");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(target);
    var manifest = new MigrationAttemptManifest
    {
        SchemaVersion = MigrationAttemptManifest.CurrentSchemaVersion,
        AttemptId = Guid.NewGuid(),
        SourcePhysicalRoot = source,
        TargetPhysicalRoot = target,
        SourceLibrarySha256Hex = new string('0', 64),
        SourcePayloadFingerprintSha256Hex = new string('0', 64),
        Phase = MigrationManifestPhase.Copying,
        Artifacts = [],
        ControlArtifacts = [],
        TargetBaseline = new MigrationTargetBaseline(true, true, true)
    };
    new MigrationManifestRepository().CreateInitialCopyingManifestDurable(
        Path.Combine(target, ".prompthelper-migration.json"),
        manifest);
}

void RunRealTransition()
{
    string source = Path.Combine(root, "source");
    string target = Path.Combine(root, "target");
    string settingsDirectory = Path.Combine(root, "settings");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(target);
    Directory.CreateDirectory(settingsDirectory);

    var paths = new AppPaths(source);
    paths.EnsureDataDirectories();
    new LibraryRepository(paths, new WindowsDurableAtomicFileWriter())
        .Commit(new LibraryDocument());

    string settingsPath = Path.Combine(settingsDirectory, "settings.json");
    var settings = new AppSettingsRepository(settingsPathOverride: settingsPath);
    settings.Save(new AppSettings
    {
        SchemaVersion = AppSettings.CurrentSchemaVersion,
        DataRootPath = source
    });

    var coordinator = new DataFolderTransitionCoordinator(
        source,
        settings,
        new DataFolderMigrationService(),
        new AlwaysConfirm());
    _ = coordinator.RequestTransition(target);
}

void RunReadyTransition()
{
    string source = Path.Combine(root, "source");
    string target = Path.Combine(root, "target");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(target);
    var manifest = new MigrationAttemptManifest
    {
        SchemaVersion = MigrationAttemptManifest.CurrentSchemaVersion,
        AttemptId = Guid.NewGuid(),
        SourcePhysicalRoot = source,
        TargetPhysicalRoot = target,
        SourceLibrarySha256Hex = new string('0', 64),
        SourcePayloadFingerprintSha256Hex = new string('0', 64),
        Phase = MigrationManifestPhase.Copying,
        Artifacts = [],
        ControlArtifacts = [],
        TargetBaseline = new MigrationTargetBaseline(true, true, true)
    };
    string marker = Path.Combine(target, ".prompthelper-migration.json");
    var repository = new MigrationManifestRepository();
    repository.CreateInitialCopyingManifestDurable(marker, manifest);
    manifest.Phase = MigrationManifestPhase.ReadyToCommit;
    repository.WriteReadyManifestDurable(marker, manifest);
}

sealed class AlwaysConfirm : IUserConfirmationService
{
    public bool Confirm(string message, string title) => true;
    public bool ConfirmExistingLibrarySwitch(string targetPath, string? warning) => true;
    public void ShowInformation(string message, string title) { }
    public void ShowWarning(string message, string title) { }
}
