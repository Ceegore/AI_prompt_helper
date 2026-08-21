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
    public Action<string, string>? OnMoveNoOverwriteWriteThrough { get; set; }
    public Action<string, string>? OnMoveNoOverwrite
    {
        get => OnMoveNoOverwriteWriteThrough;
        set => OnMoveNoOverwriteWriteThrough = value;
    }
    public Func<string, IEnumerable<string>>? OnEnumeratePromptFiles { get; set; }
    public Func<string, bool>? OnFileExists { get; set; }
    public Func<string, bool>? OnDirectoryExists { get; set; }
    public Action<string>? OnDeleteFile { get; set; }
    public Action<string>? OnDeleteDirectory { get; set; }
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

    public void MoveNoOverwriteWriteThrough(string source, string destination)
    {
        if (OnMoveNoOverwriteWriteThrough != null)
        {
            OnMoveNoOverwriteWriteThrough(source, destination);
            return;
        }

        _inner.MoveNoOverwriteWriteThrough(source, destination);
    }

    public IEnumerable<string> EnumeratePromptFiles(string directory)
    {
        if (OnEnumeratePromptFiles != null)
        {
            return OnEnumeratePromptFiles(directory);
        }

        return _inner.EnumeratePromptFiles(directory);
    }

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
