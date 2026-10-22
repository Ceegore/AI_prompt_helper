using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

internal interface IVolumeCapabilityNativeApi
{
    /// <summary>Opens a directory handle suitable for metadata queries, or an invalid handle.</summary>
    SafeFileHandle OpenDirectoryHandle(string path);

    /// <summary>Attempts to read the 128-bit file ID of the open object.</summary>
    bool TryReadFileId(SafeFileHandle handle, out int win32Error);

    /// <summary>Reads the filesystem name backing the open object ("NTFS", "exFAT", ...).</summary>
    bool TryGetFileSystemName(SafeFileHandle handle, out string? fileSystemName);
}

internal sealed class DefaultVolumeCapabilityNativeApi : IVolumeCapabilityNativeApi
{
    private const uint FILE_READ_ATTRIBUTES = 0x0080;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const int FileIdInfoClass = 18;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_ID_INFO
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
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
        out FILE_ID_INFO lpFileInformation,
        uint dwBufferSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationByHandleW(
        SafeFileHandle hFile,
        [Out] char[]? lpVolumeNameBuffer,
        uint nVolumeNameSize,
        IntPtr lpVolumeSerialNumber,
        IntPtr lpMaximumComponentLength,
        IntPtr lpFileSystemFlags,
        [Out] char[]? lpFileSystemNameBuffer,
        uint nFileSystemNameSize);

    public SafeFileHandle OpenDirectoryHandle(string path)
    {
        return CreateFileW(
            path,
            FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);
    }

    public bool TryReadFileId(SafeFileHandle handle, out int win32Error)
    {
        if (GetFileInformationByHandleEx(
                handle,
                FileIdInfoClass,
                out FILE_ID_INFO _,
                (uint)Marshal.SizeOf<FILE_ID_INFO>()))
        {
            win32Error = 0;
            return true;
        }

        win32Error = Marshal.GetLastWin32Error();
        return false;
    }

    public bool TryGetFileSystemName(SafeFileHandle handle, out string? fileSystemName)
    {
        var buffer = new char[261];

        if (!GetVolumeInformationByHandleW(
                handle,
                null,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                buffer,
                (uint)buffer.Length))
        {
            fileSystemName = null;
            return false;
        }

        int length = Array.IndexOf(buffer, '\0');
        if (length < 0)
        {
            length = buffer.Length;
        }

        fileSystemName = new string(buffer, 0, length);
        return fileSystemName.Length > 0;
    }
}

/// <summary>
/// Determines, from the volume itself, whether a candidate data folder can support the
/// file-identity model Prompt Helper's durability guarantee is built on.
///
/// The check is empirical rather than a filesystem-name allowlist: it reads an actual file ID
/// from a real directory handle. A name list would both reject filesystems that do support
/// identity and, worse, accept ones that do not. Measured 2026-10-22: NTFS directory handles
/// return a file ID at the volume root and in a created subdirectory; exFAT fails with
/// ERROR_INVALID_PARAMETER (87) for both, and the volume reports no
/// FILE_SUPPORTS_OPEN_BY_FILE_ID.
/// </summary>
public sealed class WindowsDataRootVolumeCapabilityInspector : IDataRootVolumeCapabilityInspector
{
    private const int ERROR_INVALID_FUNCTION = 1;
    private const int ERROR_NOT_SUPPORTED = 50;
    private const int ERROR_INVALID_PARAMETER = 87;

    private readonly IVolumeCapabilityNativeApi _nativeApi;

    public WindowsDataRootVolumeCapabilityInspector()
        : this(null)
    {
    }

    internal WindowsDataRootVolumeCapabilityInspector(IVolumeCapabilityNativeApi? nativeApi)
    {
        _nativeApi = nativeApi ?? new DefaultVolumeCapabilityNativeApi();
    }

    public DataRootVolumeCapabilities Inspect(string existingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(existingDirectory);

        ProductionRuntimeEvidence.Hit("WindowsDataRootVolumeCapabilityInspector.Inspect");

        using SafeFileHandle handle = _nativeApi.OpenDirectoryHandle(existingDirectory);

        if (handle.IsInvalid)
        {
            throw new DataRootVolumeCapabilityInspectionException(
                existingDirectory,
                Marshal.GetLastWin32Error());
        }

        _nativeApi.TryGetFileSystemName(handle, out string? fileSystemName);

        if (_nativeApi.TryReadFileId(handle, out int idError))
        {
            return new DataRootVolumeCapabilities(fileSystemName, SupportsFileIdentity: true);
        }

        // Only a "this filesystem has no such feature" rejection is a capability verdict.
        // Anything else (a sharing violation, access denied, a transient device error) says
        // nothing about the volume, so it fails closed as an inspection failure rather than
        // telling the user to go buy a different drive.
        if (idError is ERROR_INVALID_FUNCTION or ERROR_NOT_SUPPORTED or ERROR_INVALID_PARAMETER)
        {
            return new DataRootVolumeCapabilities(fileSystemName, SupportsFileIdentity: false);
        }

        throw new DataRootVolumeCapabilityInspectionException(existingDirectory, idError);
    }
}
