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

    public void MoveNoOverwriteWriteThrough(string source, string destination)
    {
        Trace.Add($"MoveWriteThrough:{Path.GetFileName(source)}->{Path.GetFileName(destination)}");
        _inner.MoveNoOverwriteWriteThrough(source, destination);
    }

    public IEnumerable<string> EnumeratePromptFiles(string directory)
    {
        Trace.Add("EnumeratePromptFiles");
        return _inner.EnumeratePromptFiles(directory);
    }

    public bool FileExists(string path) => _inner.FileExists(path);
    public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
    public void DeleteFile(string path) => _inner.DeleteFile(path);
    public void DeleteDirectory(string path) => _inner.DeleteDirectory(path);
    public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern = "*") => _inner.EnumerateFiles(directory, searchPattern);
    public IReadOnlyList<string> EnumerateEntries(string directory) => _inner.EnumerateEntries(directory);
}
