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
    /// Creates an owned stage at <paramref name="path"/>. Fails if anything already occupies
    /// that pathname — and a pre-existing object there is <b>preserved</b>, never adopted and
    /// never deleted, because this invocation did not create it.
    /// </summary>
    IOwnedFileStage CreateOwnedStage(string path);

    bool FileExists(string path);
    byte[] ReadAllBytes(string path);
}

public sealed class DefaultMigrationManifestFileOps : IMigrationManifestFileOps
{
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

    private readonly IOwnedArtifactJournal _ownedArtifacts = new WindowsOwnedArtifactJournal();

    public IOwnedFileStage CreateOwnedStage(string path)
    {
        var stage = new OwnedManifestStage(WindowsOwnedDurableStage.CreateNew(path));
        try
        {
            DefaultMigrationFileOps.RecordStageOwnership(_ownedArtifacts, path, stage.IdentityToken);
            return stage;
        }
        catch
        {
            stage.Dispose();
            throw;
        }
    }

    private readonly StrictPathAuthority _strictPathAuthority = new();

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
