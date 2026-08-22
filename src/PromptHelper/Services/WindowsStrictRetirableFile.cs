using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>
/// The single authority for retiring a journal or marker: open once, read and validate from
/// that exact handle, delete that exact object.
/// </summary>
/// <remarks>
/// <para>CRUU14-005 established the same-handle half of this — no more "validate one pathname,
/// then delete whatever a second lookup finds". CRUU15-005 adds the half that was missing:
/// same-handle deletion is only safe if the handle is also proven to denote the object at the
/// <i>intended location</i>. The previous implementation opened without
/// <c>FILE_FLAG_OPEN_REPARSE_POINT</c>, so a symlink at the journal path was silently
/// followed and the exact-object deletion that followed destroyed the exact object the
/// symlink pointed at — anywhere on the machine.</para>
/// <para>An opened file therefore has to satisfy all four properties before anything is read
/// from it or deleted: opened without following reparse points, not itself a reparse point,
/// final physical path resolved from the handle, and that path a strict descendant of the
/// expected data root.</para>
/// </remarks>
internal sealed class WindowsStrictRetirableFile : IDisposable
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint DELETE = 0x00010000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;
    private const int FileAttributeTagInfoClass = 9;
    private const int ERROR_FILE_NOT_FOUND = 2;
    private const int ERROR_PATH_NOT_FOUND = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_ATTRIBUTE_TAG_INFO
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        int fileInformationClass,
        out FILE_ATTRIBUTE_TAG_INFO fileInformation,
        uint bufferSize);

    private readonly SafeFileHandle _handle;
    private bool _disposed;

    public string Path { get; }

    /// <summary>The final physical path of the exact object, resolved from the retained handle.</summary>
    public string FinalPhysicalPath { get; }

    private WindowsStrictRetirableFile(string path, string finalPhysicalPath, SafeFileHandle handle)
    {
        Path = path;
        FinalPhysicalPath = finalPhysicalPath;
        _handle = handle;
    }

    /// <summary>
    /// Opens <paramref name="path"/> for read-and-retire, proving it is a genuine non-reparse
    /// file physically inside <paramref name="expectedPhysicalRoot"/>. Returns null only when
    /// the path genuinely does not exist.
    /// </summary>
    public static WindowsStrictRetirableFile? OpenExistingOrNull(string path, string expectedPhysicalRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPhysicalRoot);

        SafeFileHandle handle = CreateFileW(
            path,
            GENERIC_READ | DELETE,
            FILE_SHARE_READ | FILE_SHARE_DELETE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND)
            {
                return null;
            }

            throw new IOException($"Failed to open '{path}'.", new Win32Exception(error));
        }

        try
        {
            if (!GetFileInformationByHandleEx(
                    handle,
                    FileAttributeTagInfoClass,
                    out FILE_ATTRIBUTE_TAG_INFO tagInfo,
                    (uint)Marshal.SizeOf<FILE_ATTRIBUTE_TAG_INFO>()))
            {
                throw new IOException(
                    $"Unable to inspect attributes for '{path}'.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }

            if ((tagInfo.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
            {
                throw new InvalidDataException(
                    $"Refusing to read or retire a reparse-point control file: '{path}'.");
            }

            string finalPath = WindowsFinalPathHelper.GetNormalizedDosPath(handle);
            WindowsFinalPathHelper.AssertStrictDescendantFile(expectedPhysicalRoot, finalPath);

            return new WindowsStrictRetirableFile(path, finalPath, handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public byte[] ReadAllBytes()
    {
        long length = RandomAccess.GetLength(_handle);
        byte[] bytes = new byte[length];
        int read = 0;
        while (read < bytes.Length)
        {
            int n = RandomAccess.Read(_handle, bytes.AsSpan(read), read);
            if (n <= 0)
            {
                throw new IOException($"Unexpected end of data reading '{Path}'.");
            }

            read += n;
        }

        return bytes;
    }

    /// <summary>Deletes the exact object this handle refers to (POSIX-unlink semantics).</summary>
    public void DeleteExact() => WindowsHandleDeletion.MarkForDeletion(_handle, Path);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle.Dispose();
    }
}
