using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

public sealed class WindowsDirectoryCaseSensitivityInspector : IDirectoryCaseSensitivityInspector
{
    private const uint FILE_READ_ATTRIBUTES = 0x0080;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const int FileCaseSensitiveInfo = 23;
    private const uint FILE_CS_FLAG_CASE_SENSITIVE_DIR = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_CASE_SENSITIVE_INFORMATION
    {
        public uint Flags;
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
        int FileInformationClass,
        out FILE_CASE_SENSITIVE_INFORMATION lpFileInformation,
        uint dwBufferSize);

    public bool IsCaseSensitive(string existingDirectory)
    {
        if (string.IsNullOrWhiteSpace(existingDirectory) || !Directory.Exists(existingDirectory))
        {
            return false;
        }

        using SafeFileHandle handle = CreateFileW(
            existingDirectory,
            FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return false;
        }

        if (!GetFileInformationByHandleEx(
                handle,
                FileCaseSensitiveInfo,
                out FILE_CASE_SENSITIVE_INFORMATION info,
                (uint)Marshal.SizeOf<FILE_CASE_SENSITIVE_INFORMATION>()))
        {
            return false;
        }

        return (info.Flags & FILE_CS_FLAG_CASE_SENSITIVE_DIR) != 0;
    }
}
