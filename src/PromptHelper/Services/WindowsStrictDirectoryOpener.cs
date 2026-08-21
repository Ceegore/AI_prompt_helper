using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

internal sealed class WindowsStrictDirectoryOpener : IStrictDirectoryOpener
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;

    private const int ERROR_FILE_NOT_FOUND = 2;
    private const int ERROR_PATH_NOT_FOUND = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public long ftCreationTime;
        public long ftLastAccessTime;
        public long ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    public DirectoryOpenResult OpenDirectoryStrict(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        SafeFileHandle handle = CreateFileW(
            path,
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND)
            {
                return new DirectoryOpenResult(DirectoryOpenState.Missing, null);
            }

            throw new IOException($"Failed to open directory '{path}'. Native error: {error}", new Win32Exception(error));
        }

        if (!GetFileInformationByHandle(handle, out var info))
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException($"Failed to query handle information for directory '{path}'. Native error: {error}", new Win32Exception(error));
        }

        if ((info.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
        {
            handle.Dispose();
            throw new InvalidDataException($"Expected directory but opened non-directory: '{path}'.");
        }

        return new DirectoryOpenResult(DirectoryOpenState.Opened, handle);
    }

    public SafeFileHandle OpenManagedNodeLease(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        SafeFileHandle handle = CreateFileW(
            path,
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE, // NO FILE_SHARE_DELETE
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            throw new IOException(
                $"Failed to acquire tree lease on directory '{path}'.",
                new Win32Exception(error));
        }

        if (!GetFileInformationByHandle(handle, out var info))
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException($"Failed to query lease handle information for '{path}'.", new Win32Exception(error));
        }

        if ((info.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
        {
            handle.Dispose();
            throw new InvalidDataException($"Expected directory but opened non-directory: '{path}'.");
        }

        if ((info.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
        {
            handle.Dispose();
            throw new InvalidDataException($"Managed directory node is a reparse point: '{path}'.");
        }

        return handle;
    }
}
