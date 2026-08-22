using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>
/// Opens a file with the same reparse-point-safe authority used elsewhere for verified
/// deletion (<see cref="WindowsVerifiedArtifactDeleter"/>) and strict directory opening
/// (<see cref="WindowsStrictDirectoryOpener"/>), for callers that need to *read* a file
/// (e.g. a commit lease) with the same guarantee: the handle refers to a genuine, non-reparse
/// file whose final physical path is a strict descendant of an expected root — not whatever a
/// reparse point at that path happens to redirect to. See CRUU14-008.
/// </summary>
internal static class WindowsStrictFileOpener
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;
    private const int FileAttributeTagInfoClass = 9;

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

    /// <summary>
    /// Opens <paramref name="path"/> read-only, rejects it if it is a reparse point, and
    /// asserts that its final physical path is a strict descendant of
    /// <paramref name="physicalRoot"/>. Throws on any failure; the caller owns the returned
    /// handle's disposal.
    /// </summary>
    public static SafeFileHandle OpenNonReparseUnderRoot(string path, string physicalRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);

        SafeFileHandle handle = CreateFileW(
            path,
            GENERIC_READ,
            FILE_SHARE_READ,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException($"Unable to open '{path}'.", new Win32Exception(error));
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
                throw new InvalidDataException($"Refusing to open reparse-point file: '{path}'.");
            }

            string finalPath = WindowsFinalPathHelper.GetNormalizedDosPath(handle);
            WindowsFinalPathHelper.AssertStrictDescendantFile(physicalRoot, finalPath);

            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }
}
