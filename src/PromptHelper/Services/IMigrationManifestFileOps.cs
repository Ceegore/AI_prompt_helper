using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace PromptHelper.Services;

public interface IMigrationManifestFileOps
{
    Stream CreateNew(string path);
    void FlushToDisk(Stream stream);

    /// <summary>
    /// Creates an owned stage at <paramref name="path"/>, proven from its retained handle to be
    /// physically inside <paramref name="physicalRoot"/> (CRUU16-007). Fails if anything already
    /// occupies that pathname — and a pre-existing object there is <b>preserved</b>, never
    /// adopted and never deleted, because this invocation did not create it.
    /// </summary>
    IOwnedFileStage CreateOwnedStage(string physicalRoot, string path);

    bool FileExists(string path);
    byte[] ReadAllBytes(string path);
}

public sealed class DefaultMigrationManifestFileOps : IMigrationManifestFileOps
{
    private readonly IOwnedArtifactJournal _ownedArtifacts;
    private readonly StrictPathAuthority _strictPathAuthority = new();

    public DefaultMigrationManifestFileOps()
        : this(null)
    {
    }

    internal DefaultMigrationManifestFileOps(IOwnedArtifactJournal? ownedArtifacts)
    {
        _ownedArtifacts = ownedArtifacts ?? new WindowsOwnedArtifactJournal();
    }

    public Stream CreateNew(string path)
    {
        return new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);
    }

    public void FlushToDisk(Stream stream)
    {
        if (stream is not FileStream fs)
        {
            throw new InvalidOperationException("Durable manifest flush requires a FileStream.");
        }

        fs.Flush(flushToDisk: true);
    }

    public IOwnedFileStage CreateOwnedStage(string physicalRoot, string path)
    {
        ProductionRuntimeEvidence.Hit("DefaultMigrationManifestFileOps.CreateOwnedStage");
        WindowsOwnedDurableStage durableStage =
            WindowsOwnedDurableStage.CreateCrashAtomicBootstrapUnderRoot(path, physicalRoot);
        var stage = new OwnedManifestStage(durableStage);
        ProductionCrashCut.Hit("DefaultMigrationManifestFileOps.AfterCreateBeforeFirstClaim");
        try
        {
            DefaultMigrationFileOps.RecordStageOwnership(_ownedArtifacts, path, stage.IdentityToken);
            durableStage.PersistAfterDurableClaim();
            return stage;
        }
        catch (Exception recordFailure)
        {
            try
            {
                stage.DeleteExact();
            }
            catch (Exception cleanupFailure)
            {
                stage.Dispose();
                throw new IOException(
                    $"Manifest stage ownership could not be recorded and exact cleanup failed for '{path}'.",
                    new AggregateException(recordFailure, cleanupFailure));
            }

            stage.Dispose();
            throw;
        }
    }

    public bool FileExists(string path) => _strictPathAuthority.Probe(path).Kind == StrictPathKind.File;

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    private sealed class OwnedManifestStage : IOwnedFileStage
    {
        private readonly WindowsOwnedDurableStage _stage;

        public OwnedManifestStage(WindowsOwnedDurableStage stage) => _stage = stage;

        public string IdentityToken => _stage.Identity.ToToken();

        public void Write(ReadOnlySpan<byte> bytes) => _stage.Write(bytes);
        public void FlushDurable() => _stage.FlushDurable();
        public void PromoteReplaceExact(string targetPath) => _stage.PromoteReplaceExact(targetPath);
        public void PromoteNoOverwriteExact(string targetPath) => _stage.PromoteNoOverwriteExact(targetPath);
        public void DeleteExact() => _stage.DeleteExact();
        public void Dispose() => _stage.Dispose();
    }
}
