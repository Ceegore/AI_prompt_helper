using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

internal interface ICaseSensitivityNativeApi
{
    SafeFileHandle OpenDirectoryHandle(string path);
    bool QueryCaseSensitiveInfo(SafeFileHandle handle, out uint flags, out int win32Error);

    /// <summary>
    /// Reads the filesystem name backing an open directory handle (for example "NTFS",
    /// "ReFS", "exFAT", "FAT32"). Returns false when the name cannot be determined.
    /// </summary>
    bool TryGetFileSystemName(SafeFileHandle handle, out string? fileSystemName);
}

internal sealed class DefaultCaseSensitivityNativeApi : ICaseSensitivityNativeApi
{
    private const uint FILE_READ_ATTRIBUTES = 0x0080;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const int FileCaseSensitiveInfo = 23;

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

    public bool QueryCaseSensitiveInfo(SafeFileHandle handle, out uint flags, out int win32Error)
    {
        bool ok = GetFileInformationByHandleEx(
            handle,
            FileCaseSensitiveInfo,
            out FILE_CASE_SENSITIVE_INFORMATION info,
            (uint)Marshal.SizeOf<FILE_CASE_SENSITIVE_INFORMATION>());

        if (ok)
        {
            flags = info.Flags;
            win32Error = 0;
            return true;
        }

        flags = 0;
        win32Error = Marshal.GetLastWin32Error();
        return false;
    }

    public bool TryGetFileSystemName(SafeFileHandle handle, out string? fileSystemName)
    {
        // MAX_PATH + 1 is the documented ceiling for the filesystem name buffer.
        var buffer = new char[261];

        bool ok = GetVolumeInformationByHandleW(
            handle,
            null,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            buffer,
            (uint)buffer.Length);

        if (!ok)
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

public sealed class WindowsDirectoryCaseSensitivityInspector : IDirectoryCaseSensitivityInspector
{
    private const uint FILE_CS_FLAG_CASE_SENSITIVE_DIR = 0x00000001;

    private const int ERROR_INVALID_FUNCTION = 1;
    private const int ERROR_NOT_SUPPORTED = 50;
    private const int ERROR_INVALID_PARAMETER = 87;

    // Per-directory case sensitivity is an NTFS/ReFS feature. Only these filesystems can
    // answer FileCaseSensitiveInfo, and therefore only these can hold a case-sensitive
    // directory. The query failing on any other filesystem means "the feature does not
    // exist here", not "the answer is unknown".
    private static readonly string[] CaseSensitivityCapableFileSystems = ["NTFS", "ReFS"];

    private readonly ICaseSensitivityNativeApi _nativeApi;

    public WindowsDirectoryCaseSensitivityInspector()
        : this(null)
    {
    }

    internal WindowsDirectoryCaseSensitivityInspector(ICaseSensitivityNativeApi? nativeApi)
    {
        _nativeApi = nativeApi ?? new DefaultCaseSensitivityNativeApi();
    }

    public DirectoryCaseSensitivityState Inspect(string existingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(existingDirectory);

        ProductionRuntimeEvidence.Hit("WindowsDirectoryCaseSensitivityInspector.Inspect");

        using SafeFileHandle handle = _nativeApi.OpenDirectoryHandle(existingDirectory);

        if (handle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            throw new DirectoryCaseSensitivityInspectionException(existingDirectory, err);
        }

        if (!_nativeApi.QueryCaseSensitiveInfo(handle, out uint flags, out int queryError))
        {
            // A filesystem that does not implement per-directory case sensitivity rejects
            // this query outright - measured: exFAT returns 87 (ERROR_INVALID_PARAMETER) for
            // every directory on the volume, root or not. Such a volume cannot contain a
            // case-sensitive directory at all, so it already satisfies the case-insensitive
            // requirement. Treating the rejection as an inspection failure instead rejected
            // every exFAT and FAT32 volume - USB sticks, SD cards, most large external drives -
            // as a Prompt Helper data folder, which is the opposite of what the check is for.
            //
            // NTFS and ReFS do implement the query, so a failure there is a genuine anomaly
            // and still fails closed.
            if (IsFeatureUnsupportedError(queryError) &&
                !IsCaseSensitivityCapableFileSystem(handle))
            {
                return DirectoryCaseSensitivityState.CaseInsensitive;
            }

            throw new DirectoryCaseSensitivityInspectionException(existingDirectory, queryError);
        }

        return (flags & FILE_CS_FLAG_CASE_SENSITIVE_DIR) != 0
            ? DirectoryCaseSensitivityState.CaseSensitive
            : DirectoryCaseSensitivityState.CaseInsensitive;
    }

    private static bool IsFeatureUnsupportedError(int win32Error)
    {
        return win32Error is ERROR_INVALID_FUNCTION or ERROR_NOT_SUPPORTED or ERROR_INVALID_PARAMETER;
    }

    private bool IsCaseSensitivityCapableFileSystem(SafeFileHandle handle)
    {
        // Fails closed: when the filesystem cannot be identified, assume it is one that
        // supports the query, so the caller still sees the original inspection failure
        // rather than an unverified "case-insensitive" answer.
        if (!_nativeApi.TryGetFileSystemName(handle, out string? fileSystemName) ||
            string.IsNullOrEmpty(fileSystemName))
        {
            return true;
        }

        foreach (string capable in CaseSensitivityCapableFileSystems)
        {
            if (string.Equals(fileSystemName, capable, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
