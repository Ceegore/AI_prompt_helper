using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>
/// A staging file created and owned by this process from the moment it is created through
/// promotion or deletion. The handle obtained at creation is retained for the entire
/// lifetime of the object and is the *only* thing ever renamed or deleted — there is never a
/// window where "the staging path" is closed and later re-opened by path, which is exactly
/// the gap that let a foreign object replace a staging file between flush/close and a
/// path-based <c>MoveFileExW</c> promotion (CRUU14-001). Promotion uses a handle-bound rename
/// (<c>SetFileInformationByHandle</c> / <c>FileRenameInfo</c>) and cleanup uses a handle-bound
/// delete (<c>FileDispositionInfo</c>), so both the "commit" and the "abort" path stay bound
/// to the exact object this process created.
/// </summary>
internal sealed class WindowsOwnedDurableStage : IDisposable
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint DELETE = 0x00010000;
    private const uint FILE_SHARE_NONE = 0x00000000;
    private const uint CREATE_NEW = 1;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const int FileRenameInfoClass = 3;
    private const int FileDispositionInfoClass = 4;
    private const int FileAttributeTagInfoClass = 9;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_ATTRIBUTE_TAG_INFO
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushFileBuffers(SafeFileHandle hFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        int fileInformationClass,
        out FILE_ATTRIBUTE_TAG_INFO fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetFileInformationByHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandleRename(
        SafeFileHandle hFile,
        int fileInformationClass,
        byte[] lpFileInformation,
        uint dwBufferSize);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetFileInformationByHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandleDisposition(
        SafeFileHandle hFile,
        int fileInformationClass,
        ref FILE_DISPOSITION_INFO lpFileInformation,
        uint dwBufferSize);

    private SafeFileHandle _handle;
    private bool _terminal;
    private bool _disposed;

    public string StagingPath { get; }

    private WindowsOwnedDurableStage(string stagingPath, SafeFileHandle handle)
    {
        StagingPath = stagingPath;
        _handle = handle;
    }

    /// <summary>
    /// Creates a brand-new staging file (fails if one already exists at this path) and
    /// retains its handle.
    /// </summary>
    public static WindowsOwnedDurableStage CreateNew(string stagingPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingPath);

        SafeFileHandle handle = CreateFileW(
            stagingPath,
            GENERIC_READ | GENERIC_WRITE | DELETE,
            FILE_SHARE_NONE,
            IntPtr.Zero,
            CREATE_NEW,
            FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException(
                $"Failed to create owned staging file '{stagingPath}'.",
                new Win32Exception(error));
        }

        return new WindowsOwnedDurableStage(stagingPath, handle);
    }

    public void Write(ReadOnlySpan<byte> bytes)
    {
        RandomAccess.Write(_handle, bytes, 0);
    }

    public void FlushDurable()
    {
        if (!FlushFileBuffers(_handle))
        {
            throw new IOException(
                $"FlushFileBuffers failed for staging file '{StagingPath}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    /// <summary>
    /// Verifies, from the retained handle, that the staged object is a genuine non-reparse
    /// file whose final physical path resolves as a strict descendant of
    /// <paramref name="physicalRoot"/>.
    /// </summary>
    public void AssertNonReparseAndUnderRoot(string physicalRoot)
    {
        if (!GetFileInformationByHandleEx(
                _handle,
                FileAttributeTagInfoClass,
                out FILE_ATTRIBUTE_TAG_INFO tagInfo,
                (uint)Marshal.SizeOf<FILE_ATTRIBUTE_TAG_INFO>()))
        {
            throw new IOException(
                $"Unable to inspect staging file attributes for '{StagingPath}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        if ((tagInfo.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
        {
            throw new InvalidDataException(
                $"Staging file unexpectedly became a reparse point: '{StagingPath}'.");
        }

        string finalPath = WindowsFinalPathHelper.GetNormalizedDosPath(_handle);
        WindowsFinalPathHelper.AssertStrictDescendantFile(physicalRoot, finalPath);
    }

    /// <summary>Handle-bound rename of the exact staged object onto <paramref name="targetPath"/>, replacing any existing file there.</summary>
    public void PromoteReplaceExact(string targetPath) => Promote(targetPath, replaceIfExists: true);

    /// <summary>Handle-bound rename of the exact staged object onto <paramref name="targetPath"/>; fails if a file already exists there.</summary>
    public void PromoteNoOverwriteExact(string targetPath) => Promote(targetPath, replaceIfExists: false);

    private void Promote(string targetPath, bool replaceIfExists)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_terminal)
        {
            throw new InvalidOperationException("Staging file has already been promoted or deleted.");
        }

        string fullTarget = Path.GetFullPath(targetPath);
        byte[] nameBytes = Encoding.Unicode.GetBytes(fullTarget);

        // FILE_RENAME_INFO's real native header is 20 bytes on 64-bit Windows: 1-byte
        // ReplaceIfExists + 7 bytes alignment padding + 8-byte HANDLE RootDirectory + 4-byte
        // DWORD FileNameLength, with the flexible WCHAR FileName[] array starting immediately
        // after at offset 20 — NOT Marshal.SizeOf<FILE_RENAME_INFO_HEADER>(), which reports
        // 24 because the CLR marshaler pads a struct's *trailing* size to its own alignment
        // (for array-of-struct layout) whereas a struct with a real trailing flexible array
        // member has no such padding before that array. Using the padded size here previously
        // wrote the file name 4 bytes too late, corrupting FileNameLength and the name buffer
        // and making every promotion fail with ERROR_INVALID_NAME.
        const int RenameInfoHeaderSize = 20;
        if (!Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException(
                "WindowsOwnedDurableStage's FILE_RENAME_INFO layout assumes a 64-bit process.");
        }

        byte[] buffer = new byte[RenameInfoHeaderSize + nameBytes.Length];
        buffer[0] = replaceIfExists ? (byte)1 : (byte)0;
        // Bytes 1-7: alignment padding before RootDirectory, left zero.
        // Bytes 8-15: RootDirectory HANDLE = NULL (FileName is a full path), left zero.
        BitConverter.GetBytes((uint)nameBytes.Length).CopyTo(buffer, 16);
        nameBytes.CopyTo(buffer, RenameInfoHeaderSize);

        if (!SetFileInformationByHandleRename(_handle, FileRenameInfoClass, buffer, (uint)buffer.Length))
        {
            int error = Marshal.GetLastWin32Error();
            throw new IOException(
                $"Failed to atomically promote owned staging file '{StagingPath}' to '{fullTarget}'.",
                new Win32Exception(error));
        }

        _terminal = true;
    }

    /// <summary>
    /// Marks the exact staged object for deletion through the retained handle (POSIX-unlink
    /// semantics: it disappears once the handle closes). Never deletes by re-opening the
    /// staging path — the object could have been replaced by then.
    /// </summary>
    public void DeleteExact()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_terminal)
        {
            return;
        }

        var dispInfo = new FILE_DISPOSITION_INFO { DeleteFile = true };
        if (!SetFileInformationByHandleDisposition(
            _handle,
            FileDispositionInfoClass,
            ref dispInfo,
            (uint)Marshal.SizeOf<FILE_DISPOSITION_INFO>()))
        {
            throw new IOException(
                $"Failed to mark owned staging file for deletion: '{StagingPath}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        _terminal = true;
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
