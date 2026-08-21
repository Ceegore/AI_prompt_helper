using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace PromptHelper.Services;

internal interface IMigrationManifestFileOps
{
    Stream CreateNew(string path);
    void FlushToDisk(Stream stream);
    void MoveNoOverwriteWriteThrough(string source, string destination);
    void ReplaceWriteThrough(string source, string destination);
    bool FileExists(string path);
    void DeleteFile(string path);
    byte[] ReadAllBytes(string path);
}

internal sealed class DefaultMigrationManifestFileOps : IMigrationManifestFileOps
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

    public bool FileExists(string path) => File.Exists(path);

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
}
