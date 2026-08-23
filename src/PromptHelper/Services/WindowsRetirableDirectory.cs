using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>
/// A directory opened once and removed through that same handle.
/// </summary>
/// <remarks>
/// <para>CRUU15-006: the previous pattern was "does the directory exist? is it empty? then
/// <c>Directory.Delete(path)</c>" — three independent pathname lookups, so the directory that
/// was proven empty and the directory that got removed were not provably the same object. Here
/// the handle obtained at open is the only thing ever removed.</para>
/// <para>Emptiness needs no separate check to be safe: <c>FileDispositionInfo</c> on a
/// directory handle fails with <c>ERROR_DIR_NOT_EMPTY</c> if the directory has any entries at
/// the moment the deletion is applied, so the kernel re-evaluates it atomically with the
/// removal rather than trusting an earlier enumeration.</para>
/// </remarks>
internal sealed class WindowsRetirableDirectory : IDisposable
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint DELETE = 0x00010000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
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

    /// <summary>The final physical path of the exact directory object, resolved from the retained handle.</summary>
    public string FinalPhysicalPath { get; }

    public WindowsFileIdentity Identity => WindowsFileIdentity.FromHandle(_handle);

    /// <summary>Native handle for a synchronous relative NtCreateFile operation.</summary>
    internal IntPtr DangerousHandle => _handle.DangerousGetHandle();

    private WindowsRetirableDirectory(string path, string finalPhysicalPath, SafeFileHandle handle)
    {
        Path = path;
        FinalPhysicalPath = finalPhysicalPath;
        _handle = handle;
    }

    /// <summary>
    /// Opens <paramref name="path"/> as a genuine, non-reparse directory whose final physical
    /// path is a strict descendant of <paramref name="expectedPhysicalRoot"/>. Returns null
    /// only when the path genuinely does not exist.
    /// </summary>
    public static WindowsRetirableDirectory? OpenExistingOrNull(
        string path,
        string expectedPhysicalRoot,
        bool requireDeleteAccess = true,
        bool allowRootItself = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPhysicalRoot);

        SafeFileHandle handle = CreateFileW(
            path,
            requireDeleteAccess ? GENERIC_READ | DELETE : GENERIC_READ,
            requireDeleteAccess
                ? FILE_SHARE_READ | FILE_SHARE_DELETE
                : FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND)
            {
                return null;
            }

            throw new IOException($"Failed to open directory '{path}'.", new Win32Exception(error));
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
                    $"Unable to inspect attributes for directory '{path}'.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }

            if ((tagInfo.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
            {
                throw new InvalidDataException($"Refusing to retire a reparse-point directory: '{path}'.");
            }

            if ((tagInfo.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
            {
                throw new InvalidOperationException($"Expected a directory but found a file at '{path}'.");
            }

            string finalPath = WindowsFinalPathHelper.GetNormalizedDosPath(handle);
            string root = PathIdentity.NormalizeForComparison(expectedPhysicalRoot);
            string normalized = PathIdentity.NormalizeForComparison(finalPath);
            if (!(allowRootItself && PathIdentity.Equals(normalized, root)) &&
                !PathIdentity.IsStrictDescendant(normalized, root))
            {
                throw new InvalidDataException(
                    $"Directory resolved outside the bound data root. Root='{root}', Directory='{normalized}'.");
            }

            return new WindowsRetirableDirectory(path, finalPath, handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Removes the exact directory this handle refers to. Throws if it is not empty at the
    /// moment the removal is applied.
    /// </summary>
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
