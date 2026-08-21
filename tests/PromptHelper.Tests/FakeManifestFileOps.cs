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

    public void MoveNoOverwriteWriteThrough(string source, string destination)
    {
        Trace.Add($"MoveWriteThrough:{Path.GetFileName(source)}->{Path.GetFileName(destination)}");
        if (OnMoveNoOverwriteWriteThrough != null)
        {
            OnMoveNoOverwriteWriteThrough(source, destination);
            return;
        }
        _inner.MoveNoOverwriteWriteThrough(source, destination);
    }

    public void ReplaceWriteThrough(string source, string destination)
    {
        Trace.Add($"ReplaceWriteThrough:{Path.GetFileName(source)}->{Path.GetFileName(destination)}");
        if (OnReplaceWriteThrough != null)
        {
            OnReplaceWriteThrough(source, destination);
            return;
        }
        _inner.ReplaceWriteThrough(source, destination);
    }

    public bool FileExists(string path) => OnFileExists?.Invoke(path) ?? _inner.FileExists(path);

    public void DeleteFile(string path)
    {
        Trace.Add($"DeleteFile:{Path.GetFileName(path)}");
        if (OnDeleteFile != null)
        {
            OnDeleteFile(path);
            return;
        }
        _inner.DeleteFile(path);
    }

    public byte[] ReadAllBytes(string path) => OnReadAllBytes?.Invoke(path) ?? _inner.ReadAllBytes(path);
}
