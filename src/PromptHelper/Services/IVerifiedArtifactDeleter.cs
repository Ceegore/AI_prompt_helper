using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

public interface IVerifiedArtifactDeleter
{
    void VerifyAndDelete(string physicalRoot, string path, long expectedLength, string expectedSha256Hex);

    /// <summary>
    /// Deletes a manifest-owned file using the same reparse-point and strict-descendant-path
    /// identity checks as <see cref="VerifyAndDelete"/>, but without requiring the content to
    /// match an expected hash/length. Use only for artifacts whose content is legitimately
    /// allowed to be incomplete or partially written (e.g. an in-progress payload temp file
    /// interrupted mid-copy), where identity (not content) is the safety property that matters.
    /// </summary>
    void VerifyIdentityAndDelete(string physicalRoot, string path);
}

public sealed class WindowsVerifiedArtifactDeleter : IVerifiedArtifactDeleter
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint DELETE = 0x00010000;
    private const uint FILE_SHARE_NONE = 0x00000000;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;
    private const int FileDispositionInfo = 4;
    private const int FileAttributeTagInfo = 9;
    private const int ERROR_FILE_NOT_FOUND = 2;
    private const int ERROR_PATH_NOT_FOUND = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_DISPOSITION_INFO
    {
        [MarshalAs(UnmanagedType.U1)]
        public bool DeleteFile;
    }

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
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle hFile,
        int FileInformationClass,
        ref FILE_DISPOSITION_INFO lpFileInformation,
        uint dwBufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        int fileInformationClass,
        out FILE_ATTRIBUTE_TAG_INFO fileInformation,
        uint bufferSize);

    public void VerifyAndDelete(string physicalRoot, string path, long expectedLength, string expectedSha256Hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256Hex);

        OpenForVerifiedDelete(physicalRoot, path, out SafeFileHandle? handle);
        if (handle is null)
        {
            return;
        }

        using (handle)
        {
            using var stream = new FileStream(handle, FileAccess.Read, 4096, isAsync: false);
            long currentLength = stream.Length;
            if (currentLength != expectedLength)
            {
                throw new InvalidDataException(
                    $"Artifact '{path}' length mismatch before deletion. Expected {expectedLength} bytes, found {currentLength} bytes.");
            }

            byte[] currentHash = SHA256.HashData(stream);
            string currentHex = Convert.ToHexStringLower(currentHash);
            if (!string.Equals(currentHex, expectedSha256Hex, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Artifact '{path}' SHA-256 hash mismatch before deletion. Expected {expectedSha256Hex}, found {currentHex}.");
            }

            MarkForDeletion(handle, path);
        }
    }

    public void VerifyIdentityAndDelete(string physicalRoot, string path)
    {
        OpenForVerifiedDelete(physicalRoot, path, out SafeFileHandle? handle);
        if (handle is null)
        {
            return;
        }

        using (handle)
        {
            MarkForDeletion(handle, path);
        }
    }

    private static void OpenForVerifiedDelete(string physicalRoot, string path, out SafeFileHandle? handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        SafeFileHandle opened = CreateFileW(
            path,
            GENERIC_READ | DELETE,
            FILE_SHARE_NONE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);

        if (opened.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            opened.Dispose();
            if (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND)
            {
                // Truly missing
                handle = null;
                return;
            }

            throw new IOException(
                $"Unable to open file for verified deletion '{path}'.",
                new Win32Exception(error));
        }

        if (!GetFileInformationByHandleEx(
                opened,
                FileAttributeTagInfo,
                out FILE_ATTRIBUTE_TAG_INFO tagInfo,
                (uint)Marshal.SizeOf<FILE_ATTRIBUTE_TAG_INFO>()))
        {
            int error = Marshal.GetLastWin32Error();
            opened.Dispose();
            throw new IOException(
                $"Unable to inspect opened artifact attributes for '{path}'.",
                new Win32Exception(error));
        }

        if ((tagInfo.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
        {
            opened.Dispose();
            throw new InvalidDataException(
                $"Recovery refuses to delete reparse artifact '{path}'.");
        }

        string finalPath = WindowsFinalPathHelper.GetNormalizedDosPath(opened);
        try
        {
            WindowsFinalPathHelper.AssertStrictDescendantFile(physicalRoot, finalPath);
        }
        catch
        {
            opened.Dispose();
            throw;
        }

        handle = opened;
    }

    private static void MarkForDeletion(SafeFileHandle handle, string path)
    {
        var dispInfo = new FILE_DISPOSITION_INFO { DeleteFile = true };
        if (!SetFileInformationByHandle(
            handle,
            FileDispositionInfo,
            ref dispInfo,
            (uint)Marshal.SizeOf<FILE_DISPOSITION_INFO>()))
        {
            throw new IOException(
                $"Unable to mark file for deletion '{path}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }
}
