using System;
using System.Collections.Generic;
using System.IO;
using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class FakeCapabilityFileOps : ICapabilityFileOps
{
    private readonly ICapabilityFileOps _inner = new DefaultCapabilityFileOps();

    public Func<string, Stream>? OnCreateNew { get; set; }
    public Action<Stream>? OnFlushToDisk { get; set; }
    public Action<string, string, string?>? OnReplace { get; set; }
    public Func<string, bool>? OnFileExists { get; set; }
    public Func<string, bool>? OnDirectoryExists { get; set; }
    public Func<string, IReadOnlyList<string>>? OnEnumerateEntries { get; set; }
    public Func<string, string, IReadOnlyList<string>>? OnEnumerateFiles { get; set; }
    public Action<string>? OnDeleteFile { get; set; }
    public Action<string>? OnDeleteDirectory { get; set; }

    public Stream CreateNew(string path) => OnCreateNew?.Invoke(path) ?? _inner.CreateNew(path);
    public void FlushToDisk(Stream stream)
    {
        if (OnFlushToDisk != null)
        {
            OnFlushToDisk(stream);
            return;
        }
        _inner.FlushToDisk(stream);
    }

    public void Replace(string sourceFileName, string destinationFileName, string? destinationBackupFileName)
    {
        if (OnReplace != null)
        {
            OnReplace(sourceFileName, destinationFileName, destinationBackupFileName);
            return;
        }
        _inner.Replace(sourceFileName, destinationFileName, destinationBackupFileName);
    }

    public bool FileExists(string path) => OnFileExists?.Invoke(path) ?? _inner.FileExists(path);
    public bool DirectoryExists(string path) => OnDirectoryExists?.Invoke(path) ?? _inner.DirectoryExists(path);
    public IReadOnlyList<string> EnumerateEntries(string path) => OnEnumerateEntries?.Invoke(path) ?? _inner.EnumerateEntries(path);
    public IReadOnlyList<string> EnumerateFiles(string path, string searchPattern) => OnEnumerateFiles?.Invoke(path, searchPattern) ?? _inner.EnumerateFiles(path, searchPattern);

    public void DeleteFile(string path)
    {
        if (OnDeleteFile != null)
        {
            OnDeleteFile(path);
            return;
        }
        _inner.DeleteFile(path);
    }

    public void DeleteDirectory(string path)
    {
        if (OnDeleteDirectory != null)
        {
            OnDeleteDirectory(path);
            return;
        }
        _inner.DeleteDirectory(path);
    }
}
