using System;
using System.Collections.Generic;
using System.IO;
using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class RecordingMigrationFileOps : IMigrationFileOps
{
    private readonly IMigrationFileOps _inner;

    public RecordingMigrationFileOps(IMigrationFileOps? inner = null)
    {
        _inner = inner ?? new DefaultMigrationFileOps();
    }

    public List<string> Trace { get; } = [];

    public byte[] ReadAllBytes(string path)
    {
        Trace.Add($"ReadAllBytes:{Path.GetFileName(path)}");
        return _inner.ReadAllBytes(path);
    }

    public Stream CreateNewFile(string path)
    {
        Trace.Add($"CreateNewFile:{Path.GetFileName(path)}");
        return _inner.CreateNewFile(path);
    }

    public Stream OpenRead(string path)
    {
        Trace.Add($"OpenRead:{Path.GetFileName(path)}");
        return _inner.OpenRead(path);
    }

    public void FlushToDisk(Stream stream)
    {
        Trace.Add("FlushToDisk");
        _inner.FlushToDisk(stream);
    }

    public IOwnedFileStage CreateOwnedStage(string physicalRoot, string path)
    {
        Trace.Add($"CreateNewFile:{Path.GetFileName(path)}");
        return new TracingStage(_inner.CreateOwnedStage(physicalRoot, path), path, Trace);
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

    public void RecordMigrationArtifactPublished(string physicalRoot, MigrationArtifactClaim claim) =>
        _inner.RecordMigrationArtifactPublished(physicalRoot, claim);

    public ArtifactCleanupOutcome DeleteOwnedFinalIfProven(
        string physicalRoot,
        string path,
        long expectedLength,
        string expectedSha256Hex)
        => _inner.DeleteOwnedFinalIfProven(
            physicalRoot,
            path,
            expectedLength,
            expectedSha256Hex);

    public ArtifactCleanupOutcome DeleteOwnedCapabilityProbeIfProven(
        string physicalRoot,
        string path,
        long expectedLength,
        string expectedSha256Hex,
        long? alternateExpectedLength,
        string? alternateExpectedSha256Hex)
        => _inner.DeleteOwnedCapabilityProbeIfProven(
            physicalRoot,
            path,
            expectedLength,
            expectedSha256Hex,
            alternateExpectedLength,
            alternateExpectedSha256Hex);

    public IEnumerable<string> EnumeratePromptFiles(string directory)
    {
        Trace.Add("EnumeratePromptFiles");
        return _inner.EnumeratePromptFiles(directory);
    }

    public bool FileExists(string path)
    {
        return _inner.FileExists(path);
    }

    public bool DirectoryExists(string path)
    {
        return _inner.DirectoryExists(path);
    }

    public StrictPathProbe ProbePath(string path)
    {
        return _inner.ProbePath(path);
    }


    public ArtifactCleanupOutcome DeleteOwnedFileIfProven(string physicalRoot, string path)
        => _inner.DeleteOwnedFileIfProven(physicalRoot, path);

    public ArtifactCleanupOutcome DeleteOwnedDirectoryIfProven(string physicalRoot, string path)
        => _inner.DeleteOwnedDirectoryIfProven(physicalRoot, path);

    public void DeleteDirectoryExact(string physicalRoot, string path)
        => _inner.DeleteDirectoryExact(physicalRoot, path);

    public void RetireOwnedArtifacts(string physicalRoot)
        => _inner.RetireOwnedArtifacts(physicalRoot);

    public void RetireCommittedMigrationArtifacts(string physicalRoot)
        => _inner.RetireCommittedMigrationArtifacts(physicalRoot);

    public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern = "*") => _inner.EnumerateFiles(directory, searchPattern);
    public IReadOnlyList<string> EnumerateEntries(string directory) => _inner.EnumerateEntries(directory);

    private sealed class TracingStage : IOwnedFileStage
    {
        private readonly IOwnedFileStage _stage;
        private readonly string _path;
        private readonly List<string> _trace;

        public TracingStage(IOwnedFileStage stage, string path, List<string> trace)
        {
            _stage = stage;
            _path = path;
            _trace = trace;
        }

        public string IdentityToken => _stage.IdentityToken;

        public void Write(ReadOnlySpan<byte> bytes) => _stage.Write(bytes);

        public void FlushDurable()
        {
            _trace.Add("FlushToDisk");
            _stage.FlushDurable();
        }

        public void PromoteReplaceExact(string targetPath)
        {
            _trace.Add($"MoveWriteThrough:{Path.GetFileName(_path)}->{Path.GetFileName(targetPath)}");
            _stage.PromoteReplaceExact(targetPath);
        }

        public void PromoteNoOverwriteExact(string targetPath)
        {
            _trace.Add($"MoveWriteThrough:{Path.GetFileName(_path)}->{Path.GetFileName(targetPath)}");
            _stage.PromoteNoOverwriteExact(targetPath);
        }

        public void DeleteExact() => _stage.DeleteExact();

        public void Dispose() => _stage.Dispose();
    }
}
