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
    public Func<string, DirectoryCreateOutcome>? OnTryCreateDirectoryOwned { get; set; }

    public Func<string, StrictPathProbe>? OnProbePath { get; set; }

    public bool FileExists(string path) => OnFileExists?.Invoke(path) ?? _inner.FileExists(path);
    public bool DirectoryExists(string path) => OnDirectoryExists?.Invoke(path) ?? _inner.DirectoryExists(path);
    public StrictPathProbe ProbePath(string path) => OnProbePath?.Invoke(path) ?? new StrictPathAuthority().Probe(path);
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

    public DirectoryCreateOutcome TryCreateDirectoryOwned(string path)
    {
        if (OnTryCreateDirectoryOwned != null)
        {
            return OnTryCreateDirectoryOwned(path);
        }
        return _inner.TryCreateDirectoryOwned(path);
    }
}
