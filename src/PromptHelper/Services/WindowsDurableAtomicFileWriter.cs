using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace PromptHelper.Services;

internal sealed class WindowsDurableAtomicFileWriter : IDurableAtomicFileWriter
{
    private const uint MOVEFILE_REPLACE_EXISTING = 0x00000001;
    private const uint MOVEFILE_WRITE_THROUGH = 0x00000008;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(string lpExistingFileName, string lpNewFileName, uint dwFlags);

    public void ReplaceDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        string temp = CreateOwnedTempPath(targetPath, fileClass);
        bool promoted = false;

        try
        {
            using (var stream = new FileStream(
                temp,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            MoveDurable(temp, targetPath, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
            promoted = true;
        }
        finally
        {
            if (!promoted)
            {
                BestEffortDelete(temp);
            }
        }
    }

    public void CreateNewDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        string temp = CreateOwnedTempPath(targetPath, fileClass);
        bool promoted = false;

        try
        {
            using (var stream = new FileStream(
                temp,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            MoveDurable(temp, targetPath, MOVEFILE_WRITE_THROUGH);
            promoted = true;
        }
        finally
        {
            if (!promoted)
            {
                BestEffortDelete(temp);
            }
        }
    }

    private static void MoveDurable(string source, string destination, uint flags)
    {
        if (!MoveFileExW(source, destination, flags))
        {
            int error = Marshal.GetLastWin32Error();
            throw new IOException(
                $"Failed to atomically promote temp file '{source}' to '{destination}' with flags 0x{flags:X}.",
                new Win32Exception(error));
        }
    }

    private static string CreateOwnedTempPath(string targetPath, DurableFileClass fileClass)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(targetPath))
            ?? throw new ArgumentException($"Invalid directory for target path '{targetPath}'.", nameof(targetPath));

        Directory.CreateDirectory(dir);

        string tag = GetClassTag(fileClass);
        string name = $".prompthelper-tmp-{tag}-{Guid.NewGuid():N}.tmp";
        return Path.Combine(dir, name);
    }

    public static string GetClassTag(DurableFileClass fileClass) => fileClass switch
    {
        DurableFileClass.Settings => "settings",
        DurableFileClass.LibraryMetadata => "library",
        DurableFileClass.PromptBody => "prompt",
        DurableFileClass.RecoveryArtifact => "recovery",
        DurableFileClass.InitializationControl => "init",
        DurableFileClass.MigrationControl => "migration",
        DurableFileClass.MutationControl => "mutation",
        _ => throw new ArgumentOutOfRangeException(nameof(fileClass), fileClass, "Unknown durable file class.")
    };

    private static void BestEffortDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort in failure rollback
        }
    }
}
