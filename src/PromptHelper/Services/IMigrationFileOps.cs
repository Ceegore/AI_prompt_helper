using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace PromptHelper.Services;

internal interface IMigrationFileOps
{
    byte[] ReadAllBytes(string path);
    Stream CreateNewFile(string path);
    Stream OpenRead(string path);
    void FlushToDisk(Stream stream);
    void MoveNoOverwriteWriteThrough(string source, string destination);
    IEnumerable<string> EnumeratePromptFiles(string directory);
    bool FileExists(string path);
    bool DirectoryExists(string path);
    StrictPathProbe ProbePath(string path);
    void DeleteFile(string path);
    void DeleteDirectory(string path);
    IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern = "*");
    IReadOnlyList<string> EnumerateEntries(string directory);
}

internal sealed class DefaultMigrationFileOps : IMigrationFileOps
{
    private const uint MOVEFILE_WRITE_THROUGH = 0x00000008;
    private readonly StrictPathAuthority _strictPathAuthority = new();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool MoveFileExW(
        string lpExistingFileName,
        string lpNewFileName,
        uint dwFlags);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public Stream CreateNewFile(string path)
    {
        return new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);
    }

    public Stream OpenRead(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
    }

    public void FlushToDisk(Stream stream)
    {
        if (stream is not FileStream fs)
        {
            throw new InvalidOperationException("Durable migration flush requires a FileStream.");
        }

        fs.Flush(flushToDisk: true);
    }

    public void MoveNoOverwriteWriteThrough(string source, string destination)
    {
        if (!MoveFileExW(source, destination, MOVEFILE_WRITE_THROUGH))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public IEnumerable<string> EnumeratePromptFiles(string directory)
    {
        StrictPathProbe probe = ProbePath(directory);
        if (probe.Kind != StrictPathKind.Directory)
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.md");
    }

    // Retained for interfaces that haven't migrated completely, but implemented strictly
    public bool FileExists(string path) => ProbePath(path).Kind == StrictPathKind.File;
    public bool DirectoryExists(string path) => ProbePath(path).Kind == StrictPathKind.Directory;

    public StrictPathProbe ProbePath(string path)
    {
        return _strictPathAuthority.Probe(path);
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

    public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern = "*")
    {
        StrictPathProbe probe = ProbePath(directory);
        if (probe.Kind != StrictPathKind.Directory)
        {
            return [];
        }

        var list = new List<string>();
        foreach (string file in Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly))
        {
            list.Add(file);
        }
        return list;
    }

    public IReadOnlyList<string> EnumerateEntries(string directory)
    {
        StrictPathProbe probe = ProbePath(directory);
        if (probe.Kind != StrictPathKind.Directory)
        {
            return [];
        }

        var list = new List<string>();
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            list.Add(entry);
        }
        return list;
    }
}
