using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>
/// Handle-bound namespace operations shared by every authority type in this codebase, so
/// "delete the exact object this handle refers to" and "rename the exact object this handle
/// refers to" exist once rather than once per class.
/// </summary>
internal static class WindowsHandleDeletion
{
    private const int FileRenameInfoClass = 3;
    private const int FileDispositionInfoClass = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_DISPOSITION_INFO
    {
        [MarshalAs(UnmanagedType.U1)]
        public bool DeleteFile;
    }

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetFileInformationByHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandleDisposition(
        SafeFileHandle hFile,
        int fileInformationClass,
        ref FILE_DISPOSITION_INFO lpFileInformation,
        uint dwBufferSize);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetFileInformationByHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandleRename(
        SafeFileHandle hFile,
        int fileInformationClass,
        byte[] lpFileInformation,
        uint dwBufferSize);

    public static void MarkForDeletion(SafeFileHandle handle, string pathForDiagnostics)
    {
        var dispInfo = new FILE_DISPOSITION_INFO { DeleteFile = true };
        if (!SetFileInformationByHandleDisposition(
            handle,
            FileDispositionInfoClass,
            ref dispInfo,
            (uint)Marshal.SizeOf<FILE_DISPOSITION_INFO>()))
        {
            throw new IOException(
                $"Unable to mark '{pathForDiagnostics}' for deletion.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    public static bool TryRenameNoOverwrite(SafeFileHandle handle, string targetPath, out int win32Error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        if (!Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException(
                "The FILE_RENAME_INFO layout used here assumes a 64-bit process.");
        }

        byte[] nameBytes = Encoding.Unicode.GetBytes(Path.GetFullPath(targetPath));

        // See WindowsOwnedDurableStage.Promote for why this header is 20 bytes.
        const int RenameInfoHeaderSize = 20;
        byte[] buffer = new byte[RenameInfoHeaderSize + nameBytes.Length];
        buffer[0] = 0; // ReplaceIfExists = FALSE
        BitConverter.GetBytes((uint)nameBytes.Length).CopyTo(buffer, 16);
        nameBytes.CopyTo(buffer, RenameInfoHeaderSize);

        if (!SetFileInformationByHandleRename(handle, FileRenameInfoClass, buffer, (uint)buffer.Length))
        {
            win32Error = Marshal.GetLastWin32Error();
            return false;
        }

        win32Error = 0;
        return true;
    }
}
