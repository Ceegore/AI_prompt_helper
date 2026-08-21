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
    public Action<string, string>? OnMoveNoOverwrite { get; set; }
    public Func<string, IEnumerable<string>>? OnEnumeratePromptFiles { get; set; }

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

    public void MoveNoOverwrite(string source, string destination)
    {
        if (OnMoveNoOverwrite != null)
        {
            OnMoveNoOverwrite(source, destination);
            return;
        }

        _inner.MoveNoOverwrite(source, destination);
    }

    public IEnumerable<string> EnumeratePromptFiles(string directory)
    {
        if (OnEnumeratePromptFiles != null)
        {
            return OnEnumeratePromptFiles(directory);
        }

        return _inner.EnumeratePromptFiles(directory);
    }
}
