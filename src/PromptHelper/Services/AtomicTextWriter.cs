using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PromptHelper.Services;

public sealed class AtomicTextWriter : IAtomicTextWriter, IDurableAtomicFileWriter, IDurableSettingsFileWriter
{
    private const uint MOVEFILE_REPLACE_EXISTING = 0x00000001;
    private const uint MOVEFILE_WRITE_THROUGH = 0x00000008;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool MoveFileExW(
        string lpExistingFileName,
        string lpNewFileName,
        uint dwFlags);

    private readonly StrictPathAuthority _strictPathAuthority = new();
    private readonly WindowsDurableAtomicFileWriter _durableWriter = new();

    public void Write(string targetPath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(content);

        string directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Target path has no directory.");

        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(false, true))) // throwOnInvalidBytes: true
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (!MoveFileExW(tempPath, targetPath, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
            {
                throw new IOException(
                    $"Failed to atomically promote file '{tempPath}' to '{targetPath}'.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }
        }
        finally
        {
            try
            {
                if (_strictPathAuthority.Probe(tempPath).Kind == StrictPathKind.File)
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Explicit best-effort temp cleanup only.
            }
        }
    }

    void IDurableSettingsFileWriter.WriteDurable(string targetPath, string content)
    {
        Write(targetPath, content);
    }

    void IDurableAtomicFileWriter.ReplaceDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        _durableWriter.ReplaceDurable(targetPath, bytes, fileClass);
    }

    void IDurableAtomicFileWriter.CreateNewDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        _durableWriter.CreateNewDurable(targetPath, bytes, fileClass);
    }
}