using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>
/// An exclusive, retained authority over the object a caller expects to find at a path. From
/// the moment <see cref="Open"/> returns until <see cref="Dispose"/>, the object is open with
/// share mode <c>FILE_SHARE_READ</c> only: every other opener is denied write access *and*
/// delete/rename access, so neither an in-place modification nor an atomic replace-by-rename
/// of that object can happen behind the holder's back. Other readers are unaffected.
/// </summary>
/// <remarks>
/// <para>This is the missing half of the CRUU14 CAS: a hash check is worthless the instant the
/// handle behind it closes, so the handle that proved the expectation is also the handle that
/// gets renamed out of the way when the replacement lands (see
/// <see cref="WindowsAtomicExpectedFileReplacer"/>). The exclusion is never released and
/// re-acquired.</para>
/// <para>Windows refuses a rename-with-replace over a file that is open without
/// <c>FILE_SHARE_DELETE</c> — including <c>FILE_RENAME_INFO</c>'s POSIX-semantics variant,
/// which still fails with <c>ERROR_SHARING_VIOLATION</c>. So the replacement cannot overwrite
/// this object directly while the exclusion is held. It instead renames *this* object away
/// through this handle and then promotes the staged replacement into the vacated name with
/// no-overwrite semantics. That ordering is what makes the operation fail closed.</para>
/// </remarks>
internal sealed class WindowsExpectedTargetAuthority : IDisposable
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint DELETE = 0x00010000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;
    private const int FileAttributeTagInfoClass = 9;
    private const int FileRenameInfoClass = 3;
    private const int FileDispositionInfoClass = 4;
    private const int ERROR_FILE_NOT_FOUND = 2;
    private const int ERROR_PATH_NOT_FOUND = 3;

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

    private readonly SafeFileHandle _handle;
    private bool _disposed;

    /// <summary>The path this authority was opened through; the current name differs after <see cref="RenameExactNoOverwrite"/>.</summary>
    public string OpenedPath { get; }

    /// <summary>The final, physical path of the exact object, resolved from the retained handle.</summary>
    public string FinalPhysicalPath { get; }

    public WindowsFileIdentity Identity => WindowsFileIdentity.FromHandle(_handle);

    private WindowsExpectedTargetAuthority(string openedPath, string finalPhysicalPath, SafeFileHandle handle)
    {
        OpenedPath = openedPath;
        FinalPhysicalPath = finalPhysicalPath;
        _handle = handle;
    }

    /// <summary>
    /// Takes exclusive authority over the object currently at <paramref name="path"/>, proving
    /// it is a genuine non-reparse file physically inside <paramref name="physicalRoot"/>.
    /// Returns <c>null</c> only when the path genuinely does not exist.
    /// </summary>
    public static WindowsExpectedTargetAuthority? Open(string path, string physicalRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);

        // WRITE_THROUGH on a read-only handle is deliberate: the only write this handle ever
        // performs is the handle-bound rename that moves the expected object aside, and that
        // rename is a metadata change resulting from a request on this handle — exactly what
        // the flag makes NTFS write through (the same CRUU15-012 contract the staging handle
        // relies on). OPEN_REPARSE_POINT means a symlink at this path is refused rather than
        // followed.
        SafeFileHandle handle = CreateFileW(
            path,
            GENERIC_READ | DELETE,
            FILE_SHARE_READ,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_WRITE_THROUGH,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND)
            {
                return null;
            }

            throw new StaleExpectedFileException(
                $"Unable to take exclusive authority over '{path}': {new Win32Exception(error).Message}",
                new Win32Exception(error));
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
                throw new InvalidDataException($"Refusing to take authority over reparse-point file: '{path}'.");
            }

            string finalPath = WindowsFinalPathHelper.GetNormalizedDosPath(handle);
            WindowsFinalPathHelper.AssertStrictDescendantFile(physicalRoot, finalPath);

            return new WindowsExpectedTargetAuthority(path, finalPath, handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>Reads the exact bytes of the object under authority, from the retained handle.</summary>
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
                throw new IOException($"Unexpected end of data reading '{OpenedPath}'.");
            }

            read += n;
        }

        return bytes;
    }

    /// <summary>
    /// Proves the object under authority hashes to <paramref name="expectedSha256Hex"/>. The
    /// exclusion established by <see cref="Open"/> is still held when this returns and is not
    /// released until the replacement consumes it.
    /// </summary>
    public void AssertContentMatches(string expectedSha256Hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256Hex);

        string actual = Convert.ToHexStringLower(SHA256.HashData(ReadAllBytes()));
        if (!string.Equals(actual, expectedSha256Hex, StringComparison.OrdinalIgnoreCase))
        {
            throw new StaleExpectedFileException(
                $"'{OpenedPath}' changed outside the current state. Reload before editing.");
        }
    }

    /// <summary>
    /// Proves this retained handle is the exact filesystem object the caller previously read.
    /// Content equality is intentionally insufficient for authority-sensitive rewrites.
    /// </summary>
    public void AssertIdentityMatches(WindowsFileIdentity expectedIdentity)
    {
        if (Identity != expectedIdentity)
        {
            throw new StaleExpectedFileException(
                $"'{OpenedPath}' was replaced by a different filesystem object. Reload before editing.");
        }
    }

    /// <summary>
    /// Handle-bound rename of the exact object under authority to <paramref name="targetPath"/>,
    /// refusing to overwrite anything already there. Returns false with
    /// <paramref name="win32Error"/> set instead of throwing, so callers can distinguish
    /// "someone else already occupies the name" from a hard failure.
    /// </summary>
    public bool RenameExactNoOverwrite(string targetPath, out int win32Error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        if (!Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException(
                "WindowsExpectedTargetAuthority's FILE_RENAME_INFO layout assumes a 64-bit process.");
        }

        string fullTarget = Path.GetFullPath(targetPath);
        byte[] nameBytes = Encoding.Unicode.GetBytes(fullTarget);

        // See WindowsOwnedDurableStage.Promote for why this header is 20 bytes rather than
        // Marshal.SizeOf of a declared struct.
        const int RenameInfoHeaderSize = 20;
        byte[] buffer = new byte[RenameInfoHeaderSize + nameBytes.Length];
        buffer[0] = 0; // ReplaceIfExists = FALSE
        BitConverter.GetBytes((uint)nameBytes.Length).CopyTo(buffer, 16);
        nameBytes.CopyTo(buffer, RenameInfoHeaderSize);

        if (!SetFileInformationByHandleRename(_handle, FileRenameInfoClass, buffer, (uint)buffer.Length))
        {
            win32Error = Marshal.GetLastWin32Error();
            return false;
        }

        win32Error = 0;
        return true;
    }

    /// <summary>Deletes the exact object under authority through the retained handle.</summary>
    public void DeleteExact()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var dispInfo = new FILE_DISPOSITION_INFO { DeleteFile = true };
        if (!SetFileInformationByHandleDisposition(
            _handle,
            FileDispositionInfoClass,
            ref dispInfo,
            (uint)Marshal.SizeOf<FILE_DISPOSITION_INFO>()))
        {
            throw new IOException(
                $"Failed to mark '{OpenedPath}' for deletion.",
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
