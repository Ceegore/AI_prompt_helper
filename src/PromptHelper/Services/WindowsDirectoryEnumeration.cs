using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>One entry as reported by the directory object itself, not by a pathname lookup.</summary>
internal readonly record struct DirectoryEntry(string Name, uint FileAttributes)
{
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;

    public bool IsDirectory => (FileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
    public bool IsReparsePoint => (FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
}

/// <summary>
/// Lists a directory through a retained handle to the directory object.
/// </summary>
/// <remarks>
/// CRUU15-008: <c>Directory.GetFiles</c>/<c>GetDirectories</c> re-resolve the pathname on every
/// call, so a "verified" directory and the directory that actually got listed were only
/// assumed to be the same object. Enumerating with <c>GetFileInformationByHandleEx</c> binds
/// the listing to the handle whose identity, reparse status and physical location were already
/// proven, and returns each entry's attributes as the directory object reported them — so a
/// caller can detect an entry that changed type between the listing and its own probe.
/// </remarks>
internal static class WindowsDirectoryEnumeration
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;
    private const int FileAttributeTagInfoClass = 9;
    private const int FileFullDirectoryInfoClass = 14;
    private const int FileFullDirectoryRestartInfoClass = 15;
    private const int ERROR_NO_MORE_FILES = 18;
    private const int ERROR_FILE_NOT_FOUND = 2;
    private const int ERROR_PATH_NOT_FOUND = 3;

    // FILE_FULL_DIR_INFO: NextEntryOffset(4) FileIndex(4) CreationTime(8) LastAccessTime(8)
    // LastWriteTime(8) ChangeTime(8) EndOfFile(8) AllocationSize(8) FileAttributes(4)
    // FileNameLength(4) EaSize(4) FileName[1]. Every member is naturally aligned at its
    // declared offset, so the flexible name array starts at a fixed 68 bytes with no padding
    // ambiguity (unlike FILE_ID_BOTH_DIR_INFO, whose CCHAR/WCHAR/LARGE_INTEGER sequence needs
    // two separate alignment fixups).
    private const int NextEntryOffsetOffset = 0;
    private const int FileAttributesOffset = 56;
    private const int FileNameLengthOffset = 60;
    private const int FileNameOffset = 68;

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

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetFileInformationByHandleEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleExBuffer(
        SafeFileHandle hFile,
        int fileInformationClass,
        IntPtr lpFileInformation,
        uint dwBufferSize);

    /// <summary>
    /// Lists <paramref name="directoryPath"/> from a handle proven to be a genuine non-reparse
    /// directory. Returns null only when the directory genuinely does not exist. "." and ".."
    /// are excluded.
    /// </summary>
    public static IReadOnlyList<DirectoryEntry>? ListStrict(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        SafeFileHandle handle = CreateFileW(
            directoryPath,
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
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

            throw new IOException($"Unable to open directory '{directoryPath}'.", new Win32Exception(error));
        }

        using (handle)
        {
            if (!GetFileInformationByHandleEx(
                    handle,
                    FileAttributeTagInfoClass,
                    out FILE_ATTRIBUTE_TAG_INFO tagInfo,
                    (uint)Marshal.SizeOf<FILE_ATTRIBUTE_TAG_INFO>()))
            {
                throw new IOException(
                    $"Unable to inspect attributes for directory '{directoryPath}'.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }

            if ((tagInfo.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
            {
                throw new InvalidDataException($"Refusing to enumerate a reparse-point directory: '{directoryPath}'.");
            }

            if ((tagInfo.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
            {
                throw new InvalidDataException($"Expected a directory but found a file: '{directoryPath}'.");
            }

            return ReadEntries(handle, directoryPath);
        }
    }

    private static List<DirectoryEntry> ReadEntries(SafeFileHandle handle, string directoryPath)
    {
        var entries = new List<DirectoryEntry>();

        const int bufferSize = 64 * 1024;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            int infoClass = FileFullDirectoryRestartInfoClass;

            while (true)
            {
                if (!GetFileInformationByHandleExBuffer(handle, infoClass, buffer, bufferSize))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == ERROR_NO_MORE_FILES)
                    {
                        break;
                    }

                    throw new IOException(
                        $"Unable to enumerate directory '{directoryPath}'.",
                        new Win32Exception(error));
                }

                infoClass = FileFullDirectoryInfoClass;

                IntPtr current = buffer;
                while (true)
                {
                    int nextOffset = Marshal.ReadInt32(current, NextEntryOffsetOffset);
                    uint attributes = unchecked((uint)Marshal.ReadInt32(current, FileAttributesOffset));
                    int nameLength = Marshal.ReadInt32(current, FileNameLengthOffset);

                    string name = Marshal.PtrToStringUni(current + FileNameOffset, nameLength / 2)
                        ?? string.Empty;

                    if (name is not ("." or ".."))
                    {
                        entries.Add(new DirectoryEntry(name, attributes));
                    }

                    if (nextOffset == 0)
                    {
                        break;
                    }

                    current += nextOffset;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return entries;
    }
}
