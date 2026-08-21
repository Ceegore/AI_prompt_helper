using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace PromptHelper.Services;

internal enum DirectoryCreateOutcome
{
    CreatedByCaller,
    AlreadyExists
}

internal interface IReservationFileOps
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    StrictPathProbe ProbePath(string path);
    IReadOnlyList<string> EnumerateEntries(string path);
    void DeleteFile(string path);
    void DeleteDirectory(string path);
    DirectoryCreateOutcome TryCreateDirectoryOwned(string path);
}

internal sealed class DefaultReservationFileOps : IReservationFileOps
{
    private const int ERROR_ALREADY_EXISTS = 183;
    private readonly StrictPathAuthority _strictPathAuthority = new();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateDirectoryW(
        string lpPathName,
        IntPtr lpSecurityAttributes);

    public bool FileExists(string path) => ProbePath(path).Kind == StrictPathKind.File;
    public bool DirectoryExists(string path) => ProbePath(path).Kind == StrictPathKind.Directory;

    public StrictPathProbe ProbePath(string path)
    {
        return _strictPathAuthority.Probe(path);
    }

    public IReadOnlyList<string> EnumerateEntries(string path)
    {
        StrictPathProbe probe = ProbePath(path);
        if (probe.Kind != StrictPathKind.Directory)
        {
            return [];
        }

        var list = new List<string>();
        foreach (string entry in Directory.EnumerateFileSystemEntries(path))
        {
            list.Add(entry);
        }
        return list;
    }

    public void DeleteFile(string path)
    {
        StrictPathProbe probe = ProbePath(path);
        if (probe.Kind == StrictPathKind.File)
        {
            File.Delete(path);
        }
        else if (probe.Kind == StrictPathKind.Directory)
        {
            throw new InvalidOperationException($"Expected a file but found a directory at '{path}'.");
        }
    }

    public void DeleteDirectory(string path)
    {
        StrictPathProbe probe = ProbePath(path);
        if (probe.Kind == StrictPathKind.Directory)
        {
            Directory.Delete(path);
        }
        else if (probe.Kind == StrictPathKind.File)
        {
            throw new InvalidOperationException($"Expected a directory but found a file at '{path}'.");
        }
    }

    public DirectoryCreateOutcome TryCreateDirectoryOwned(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (CreateDirectoryW(path, IntPtr.Zero))
        {
            return DirectoryCreateOutcome.CreatedByCaller;
        }

        int error = Marshal.GetLastWin32Error();
        if (error == ERROR_ALREADY_EXISTS)
        {
            return DirectoryCreateOutcome.AlreadyExists;
        }

        throw new IOException(
            $"Failed to create directory '{path}'.",
            new Win32Exception(error));
    }
}
