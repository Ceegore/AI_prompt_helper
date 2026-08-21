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
    void DeleteFile(string path);
    void DeleteDirectory(string path);
    IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern = "*");
    IReadOnlyList<string> EnumerateEntries(string directory);
}

internal sealed class DefaultMigrationFileOps : IMigrationFileOps
{
    private const uint MOVEFILE_WRITE_THROUGH = 0x00000008;

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
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.md");
    }

    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path);
        }
    }

    public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern = "*")
    {
        if (!Directory.Exists(directory))
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
        if (!Directory.Exists(directory))
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
