using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>
/// Opens an existing file and retains the handle from that open through whatever the caller
/// does with it — including, if the caller decides to, deletion. This exists so journal/marker
/// retirement can read-validate-delete the *same* object instead of "read by path, validate,
/// probe the path again, then <c>File.Delete(path)</c>" — a foreign object substituted at that
/// path after validation but before the final delete would otherwise be destroyed in place of
/// the one that was actually validated. See CRUU14-005.
/// </summary>
internal sealed class WindowsHandleBoundFile : IDisposable
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint DELETE = 0x00010000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const int FileDispositionInfoClass = 4;
    private const int ERROR_FILE_NOT_FOUND = 2;
    private const int ERROR_PATH_NOT_FOUND = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_DISPOSITION_INFO
    {
        [MarshalAs(UnmanagedType.U1)]
        public bool DeleteFile;
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

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetFileInformationByHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandleDisposition(
        SafeFileHandle hFile,
        int fileInformationClass,
        ref FILE_DISPOSITION_INFO lpFileInformation,
        uint dwBufferSize);

    private readonly SafeFileHandle _handle;
    private bool _disposed;

    public string Path { get; }

    private WindowsHandleBoundFile(string path, SafeFileHandle handle)
    {
        Path = path;
        _handle = handle;
    }

    /// <summary>Opens the file at <paramref name="path"/>, or returns null if it does not exist.</summary>
    public static WindowsHandleBoundFile? OpenExistingOrNull(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        SafeFileHandle handle = CreateFileW(
            path,
            GENERIC_READ | DELETE,
            FILE_SHARE_READ | FILE_SHARE_DELETE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
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

        return new WindowsHandleBoundFile(path, handle);
    }

    public byte[] ReadAllBytes()
    {
        long length = RandomAccess.GetLength(_handle);
        byte[] bytes = new byte[length];
        RandomAccess.Read(_handle, bytes, 0);
        return bytes;
    }

    /// <summary>Deletes the exact object this handle refers to (POSIX-unlink semantics).</summary>
    public void DeleteExact()
    {
        var dispInfo = new FILE_DISPOSITION_INFO { DeleteFile = true };
        if (!SetFileInformationByHandleDisposition(
            _handle,
            FileDispositionInfoClass,
            ref dispInfo,
            (uint)Marshal.SizeOf<FILE_DISPOSITION_INFO>()))
        {
            throw new IOException(
                $"Failed to mark '{Path}' for deletion.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

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
