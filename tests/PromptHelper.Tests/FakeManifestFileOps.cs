using System;
using System.Collections.Generic;
using System.IO;
using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class FakeManifestFileOps : IMigrationManifestFileOps
{
    private readonly IMigrationManifestFileOps _inner = new DefaultMigrationManifestFileOps();

    public List<string> Trace { get; } = [];

    public Func<string, Stream>? OnCreateNew { get; set; }
    public Action<Stream>? OnFlushToDisk { get; set; }
    public Action<string, string>? OnMoveNoOverwriteWriteThrough { get; set; }
    public Action<string, string>? OnReplaceWriteThrough { get; set; }
    public Func<string, bool>? OnFileExists { get; set; }
    public Action<string>? OnDeleteFile { get; set; }
    public Func<string, byte[]>? OnReadAllBytes { get; set; }

    public Stream CreateNew(string path)
    {
        Trace.Add($"CreateNew:{Path.GetFileName(path)}");
        return OnCreateNew?.Invoke(path) ?? _inner.CreateNew(path);
    }

    public void FlushToDisk(Stream stream)
    {
        Trace.Add("FlushToDisk");
        if (OnFlushToDisk != null)
        {
            OnFlushToDisk(stream);
            return;
        }
        _inner.FlushToDisk(stream);
    }

    public void CreateInitialMarkerCrashAtomic(
        string physicalRoot,
        string markerPath,
        Guid attemptId,
        MigrationManifestPhase phase,
        ReadOnlySpan<byte> bytes)
    {
        Trace.Add($"CreateInitialMarkerCrashAtomic:{Path.GetFileName(markerPath)}");
        _inner.CreateInitialMarkerCrashAtomic(
            physicalRoot,
            markerPath,
            attemptId,
            phase,
            bytes);
    }

    public void ReplaceMarkerIfExpected(
        string physicalRoot,
        string markerPath,
        Guid attemptId,
        ReadOnlySpan<byte> expectedBytes,
        ReadOnlySpan<byte> candidateBytes)
    {
        Trace.Add($"ReplaceMarkerIfExpected:{Path.GetFileName(markerPath)}");
        Action<string>? previousPublish = WindowsMigrationMarkerAuthority.BeforeReadyPublishForTests;
        Action<string>? previousDelete = WindowsMigrationMarkerAuthority.BeforeReadyCandidateDeleteForTests;
        try
        {
            if (OnReplaceWriteThrough is not null)
            {
                WindowsMigrationMarkerAuthority.BeforeReadyPublishForTests = stagePath =>
                {
                    Trace.Add($"ReplaceWriteThrough:{Path.GetFileName(stagePath)}->{Path.GetFileName(markerPath)}");
                    OnReplaceWriteThrough(stagePath, markerPath);
                };
            }
            if (OnDeleteFile is not null)
            {
                WindowsMigrationMarkerAuthority.BeforeReadyCandidateDeleteForTests = stagePath =>
                {
                    Trace.Add($"DeleteFile:{Path.GetFileName(stagePath)}");
                    OnDeleteFile(stagePath);
                };
            }

            _inner.ReplaceMarkerIfExpected(
                physicalRoot,
                markerPath,
                attemptId,
                expectedBytes,
                candidateBytes);
        }
        finally
        {
            WindowsMigrationMarkerAuthority.BeforeReadyPublishForTests = previousPublish;
            WindowsMigrationMarkerAuthority.BeforeReadyCandidateDeleteForTests = previousDelete;
        }
    }

    public void AssertMarkerAuthority(
        string physicalRoot,
        string markerPath,
        Guid attemptId,
        ReadOnlySpan<byte> expectedBytes) =>
        _inner.AssertMarkerAuthority(physicalRoot, markerPath, attemptId, expectedBytes);

    public void DeleteMarkerIfAuthorized(
        string physicalRoot,
        string markerPath,
        Guid attemptId,
        ReadOnlySpan<byte> expectedBytes) =>
        _inner.DeleteMarkerIfAuthorized(physicalRoot, markerPath, attemptId, expectedBytes);

    public IOwnedFileStage CreateOwnedStage(string physicalRoot, string path)
    {
        Trace.Add($"CreateOwnedStage:{Path.GetFileName(path)}");

        var stage = new FakeOwnedFileStage(_inner.CreateOwnedStage(physicalRoot, path));

        if (OnMoveNoOverwriteWriteThrough != null)
        {
            Action<string, string> hook = OnMoveNoOverwriteWriteThrough;
            stage.OnPromoteNoOverwriteExact = target =>
            {
                Trace.Add($"MoveWriteThrough:{Path.GetFileName(path)}->{Path.GetFileName(target)}");
                hook(path, target);
            };
        }

        if (OnReplaceWriteThrough != null)
        {
            Action<string, string> hook = OnReplaceWriteThrough;
            stage.OnPromoteReplaceExact = target =>
            {
                Trace.Add($"ReplaceWriteThrough:{Path.GetFileName(path)}->{Path.GetFileName(target)}");
                hook(path, target);
            };
        }

        if (OnDeleteFile != null)
        {
            Action<string> hook = OnDeleteFile;
            stage.OnDeleteExact = () =>
            {
                Trace.Add($"DeleteFile:{Path.GetFileName(path)}");
                hook(path);
            };
        }

        return stage;
    }

    public bool FileExists(string path) => OnFileExists?.Invoke(path) ?? _inner.FileExists(path);

    public byte[] ReadAllBytes(string path) => OnReadAllBytes?.Invoke(path) ?? _inner.ReadAllBytes(path);
}
