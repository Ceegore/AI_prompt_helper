using System;
using System.Collections.Generic;
using System.IO;
using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class FaultInjectingMigrationFileOps : IMigrationFileOps
{
    private readonly IMigrationFileOps _inner;

    public FaultInjectingMigrationFileOps(IMigrationFileOps? inner = null)
    {
        _inner = inner ?? new DefaultMigrationFileOps();
    }

    public Func<string, byte[]>? OnReadAllBytes { get; set; }
    public Func<string, Stream>? OnCreateNewFile { get; set; }
    public Func<string, Stream>? OnOpenRead { get; set; }
    public Action<Stream>? OnFlushToDisk { get; set; }
    /// <summary>
    /// Invoked instead of the handle-bound promotion of a staged payload file. The third
    /// argument performs the real promotion, so a test can fail before it, run it and then
    /// interfere, or skip it entirely. The staging object itself is never addressable by
    /// pathname any more, which is the whole point of the owned-stage design (CRUU15-002).
    /// </summary>
    public Action<string, string, Action>? OnPromoteStage { get; set; }
    /// <summary>
    /// Runs after real promotion and after the retained creation handle is released. This is
    /// the first point at which an external actor can actually modify the published pathname.
    /// </summary>
    public Action<string, string>? OnPromoteStageAfterHandleRelease { get; set; }
    public Func<string, IEnumerable<string>>? OnEnumeratePromptFiles { get; set; }
    public Func<string, bool>? OnFileExists { get; set; }
    public Func<string, bool>? OnDirectoryExists { get; set; }
    public Action<string>? OnDeleteFile { get; set; }
    public Action<string>? OnDeleteDirectory { get; set; }
    public Action<string, MigrationArtifactClaim>? OnRecordMigrationArtifactPublished { get; set; }
    public Action<string>? OnRetireCommittedMigrationArtifacts { get; set; }
    public Func<string, string, IReadOnlyList<string>>? OnEnumerateFiles { get; set; }
    public Func<string, IReadOnlyList<string>>? OnEnumerateEntries { get; set; }

    public byte[] ReadAllBytes(string path)
    {
        if (OnReadAllBytes != null)
        {
            return OnReadAllBytes(path);
        }

        return _inner.ReadAllBytes(path);
    }

    public Stream CreateNewFile(string path)
    {
        if (OnCreateNewFile != null)
        {
            return OnCreateNewFile(path);
        }

        return _inner.CreateNewFile(path);
    }

    public Stream OpenRead(string path)
    {
        if (OnOpenRead != null)
        {
            return OnOpenRead(path);
        }

        return _inner.OpenRead(path);
    }

    public void FlushToDisk(Stream stream)
    {
        if (OnFlushToDisk != null)
        {
            OnFlushToDisk(stream);
            return;
        }

        _inner.FlushToDisk(stream);
    }

    public IOwnedFileStage CreateOwnedStage(string physicalRoot, string path)
    {
        IOwnedFileStage inner = _inner.CreateOwnedStage(physicalRoot, path);
        var stage = new FakeOwnedFileStage(inner);

        if (OnFlushToDisk != null)
        {
            Action<Stream> flushHook = OnFlushToDisk;
            stage.OnFlushDurable = () => flushHook(Stream.Null);
        }

        if (OnPromoteStage != null)
        {
            Action<string, string, Action> hook = OnPromoteStage;
            stage.OnPromoteNoOverwriteExact =
                target => hook(path, target, () => inner.PromoteNoOverwriteExact(target));
            stage.OnPromoteReplaceExact =
                target => hook(path, target, () => inner.PromoteReplaceExact(target));
        }

        if (OnPromoteStageAfterHandleRelease != null)
        {
            Action<string, string> hook = OnPromoteStageAfterHandleRelease;
            stage.OnPromoteNoOverwriteExact = target =>
            {
                inner.PromoteNoOverwriteExact(target);
                inner.Dispose();
                hook(path, target);
            };
            stage.OnPromoteReplaceExact = target =>
            {
                inner.PromoteReplaceExact(target);
                inner.Dispose();
                hook(path, target);
            };
        }

        return stage;
    }

    public MigrationArtifactClaim RecordMigrationArtifactPrepared(
        string physicalRoot,
        string tempPath,
        string finalPath,
        string identityToken,
        long expectedLength,
        string expectedSha256Hex) =>
        _inner.RecordMigrationArtifactPrepared(
            physicalRoot,
            tempPath,
            finalPath,
            identityToken,
            expectedLength,
            expectedSha256Hex);

    public void RecordMigrationArtifactPublished(string physicalRoot, MigrationArtifactClaim claim)
    {
        if (OnRecordMigrationArtifactPublished != null)
        {
            OnRecordMigrationArtifactPublished(physicalRoot, claim);
            return;
        }

        _inner.RecordMigrationArtifactPublished(physicalRoot, claim);
    }

    public ArtifactCleanupOutcome DeleteOwnedFinalIfProven(
        string physicalRoot,
        string path,
        long expectedLength,
        string expectedSha256Hex)
    {
        if (OnDeleteFile != null)
        {
            OnDeleteFile(path);
            return ArtifactCleanupOutcome.DeletedProvenOwned;
        }

        return _inner.DeleteOwnedFinalIfProven(
            physicalRoot,
            path,
            expectedLength,
            expectedSha256Hex);
    }

    public ArtifactCleanupOutcome DeleteOwnedFileIfProven(string physicalRoot, string path)
    {
        if (OnDeleteFile != null)
        {
            OnDeleteFile(path);
            return ArtifactCleanupOutcome.DeletedProvenOwned;
        }

        return _inner.DeleteOwnedFileIfProven(physicalRoot, path);
    }

    public void DeleteDirectoryExact(string physicalRoot, string path)
    {
        if (OnDeleteDirectory != null)
        {
            OnDeleteDirectory(path);
            return;
        }

        _inner.DeleteDirectoryExact(physicalRoot, path);
    }

    public void RetireOwnedArtifacts(string physicalRoot) => _inner.RetireOwnedArtifacts(physicalRoot);

    public void RetireCommittedMigrationArtifacts(string physicalRoot)
    {
        if (OnRetireCommittedMigrationArtifacts != null)
        {
            OnRetireCommittedMigrationArtifacts(physicalRoot);
            return;
        }

        _inner.RetireCommittedMigrationArtifacts(physicalRoot);
    }

    public IEnumerable<string> EnumeratePromptFiles(string directory)
    {
        if (OnEnumeratePromptFiles != null)
        {
            return OnEnumeratePromptFiles(directory);
        }

        return _inner.EnumeratePromptFiles(directory);
    }

    public Func<string, StrictPathProbe>? OnProbePath { get; set; }

    public bool FileExists(string path)
    {
        if (OnFileExists != null)
        {
            return OnFileExists(path);
        }

        return _inner.FileExists(path);
    }

    public bool DirectoryExists(string path)
    {
        if (OnDirectoryExists != null)
        {
            return OnDirectoryExists(path);
        }

        return _inner.DirectoryExists(path);
    }

    public StrictPathProbe ProbePath(string path)
    {
        if (OnProbePath != null)
        {
            return OnProbePath(path);
        }

        return _inner.ProbePath(path);
    }

    public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern = "*")
    {
        if (OnEnumerateFiles != null)
        {
            return OnEnumerateFiles(directory, searchPattern);
        }

        return _inner.EnumerateFiles(directory, searchPattern);
    }

    public IReadOnlyList<string> EnumerateEntries(string directory)
    {
        if (OnEnumerateEntries != null)
        {
            return OnEnumerateEntries(directory);
        }

        return _inner.EnumerateEntries(directory);
    }
}
