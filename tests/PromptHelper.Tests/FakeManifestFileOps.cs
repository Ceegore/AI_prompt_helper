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

    public IOwnedFileStage CreateOwnedStage(string path)
    {
        Trace.Add($"CreateOwnedStage:{Path.GetFileName(path)}");

        var stage = new FakeOwnedFileStage(_inner.CreateOwnedStage(path));

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
