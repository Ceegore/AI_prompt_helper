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
    public Action<string, string, bool>? OnCopyFile { get; set; }

    public byte[] ReadAllBytes(string path)
    {
        if (OnReadAllBytes != null)
        {
            return OnReadAllBytes(path);
        }

        return _inner.ReadAllBytes(path);
    }

    public void CopyFile(string source, string destination, bool overwrite)
    {
        if (OnCopyFile != null)
        {
            OnCopyFile(source, destination, overwrite);
            return;
        }

        _inner.CopyFile(source, destination, overwrite);
    }

    public IEnumerable<string> EnumeratePromptFiles(string directory)
    {
        return _inner.EnumeratePromptFiles(directory);
    }
}
