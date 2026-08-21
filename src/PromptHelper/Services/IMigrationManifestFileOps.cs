using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace PromptHelper.Services;

public interface IMigrationManifestFileOps
{
    Stream CreateNew(string path);
    void FlushToDisk(Stream stream);
    void MoveNoOverwriteWriteThrough(string source, string destination);
    void ReplaceWriteThrough(string source, string destination);
    bool FileExists(string path);
    void DeleteFile(string path);
    byte[] ReadAllBytes(string path);
}

public sealed class DefaultMigrationManifestFileOps : IMigrationManifestFileOps
{
    private const uint MOVEFILE_REPLACE_EXISTING = 0x00000001;
    private const uint MOVEFILE_WRITE_THROUGH = 0x00000008;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool MoveFileExW(
        string lpExistingFileName,
        string lpNewFileName,
        uint dwFlags);

    public Stream CreateNew(string path)
    {
        return new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);
    }

    public void FlushToDisk(Stream stream)
    {
        if (stream is not FileStream fs)
        {
            throw new InvalidOperationException("Durable manifest flush requires a FileStream.");
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

    public void ReplaceWriteThrough(string source, string destination)
    {
        if (!MoveFileExW(source, destination, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private readonly StrictPathAuthority _strictPathAuthority = new();

    public bool FileExists(string path) => _strictPathAuthority.Probe(path).Kind == StrictPathKind.File;

    public void DeleteFile(string path)
    {
        StrictPathProbe probe = _strictPathAuthority.Probe(path);
        if (probe.Kind == StrictPathKind.File)
        {
            File.Delete(path);
        }
        else if (probe.Kind == StrictPathKind.Directory)
        {
            throw new InvalidOperationException($"Expected a file but found a directory at '{path}'.");
        }
    }

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
}
