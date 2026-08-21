using System;
using System.Collections.Generic;
using System.IO;
using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class FakeReservationFileOps : IReservationFileOps
{
    private readonly IReservationFileOps _inner = new DefaultReservationFileOps();

    public Func<string, bool>? OnFileExists { get; set; }
    public Func<string, bool>? OnDirectoryExists { get; set; }
    public Func<string, IReadOnlyList<string>>? OnEnumerateEntries { get; set; }
    public Action<string>? OnDeleteFile { get; set; }
    public Action<string>? OnDeleteDirectory { get; set; }
    public Action<string>? OnCreateDirectory { get; set; }

    public bool FileExists(string path) => OnFileExists?.Invoke(path) ?? _inner.FileExists(path);
    public bool DirectoryExists(string path) => OnDirectoryExists?.Invoke(path) ?? _inner.DirectoryExists(path);
    public IReadOnlyList<string> EnumerateEntries(string path) => OnEnumerateEntries?.Invoke(path) ?? _inner.EnumerateEntries(path);

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

    public void CreateDirectory(string path)
    {
        if (OnCreateDirectory != null)
        {
            OnCreateDirectory(path);
            return;
        }
        _inner.CreateDirectory(path);
    }
}
