using System.Security.Cryptography;
using System.Text;
using PromptHelper.Services;

string cut = Environment.GetEnvironmentVariable("PROMPTHELPER_CRASH_CUT")
    ?? throw new InvalidOperationException("PROMPTHELPER_CRASH_CUT is required.");
string root = Environment.GetEnvironmentVariable("PROMPTHELPER_CRASH_ROOT")
    ?? throw new InvalidOperationException("PROMPTHELPER_CRASH_ROOT is required.");
string signal = Environment.GetEnvironmentVariable("PROMPTHELPER_CRASH_SIGNAL")
    ?? throw new InvalidOperationException("PROMPTHELPER_CRASH_SIGNAL is required.");
byte[] createBytes = Encoding.UTF8.GetBytes("create");
byte[] replaceBytes = Encoding.UTF8.GetBytes("replace");

ProductionCrashCut.SinkForTests = observed =>
{
    if (!string.Equals(observed, cut, StringComparison.Ordinal))
    {
        return;
    }

    using (var stream = new FileStream(signal, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
    {
        byte[] bytes = Encoding.UTF8.GetBytes(observed);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    Thread.Sleep(Timeout.Infinite);
};

switch (cut)
{
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
