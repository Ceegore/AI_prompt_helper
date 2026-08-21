using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

public interface IVerifiedArtifactDeleter
{
    void VerifyAndDelete(string path, long expectedLength, string expectedSha256Hex);
}

public sealed class WindowsVerifiedArtifactDeleter : IVerifiedArtifactDeleter
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint DELETE = 0x00010000;
    private const uint FILE_SHARE_NONE = 0x00000000;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const int FileDispositionInfo = 4;

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
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle hFile,
        int FileInformationClass,
        ref FILE_DISPOSITION_INFO lpFileInformation,
        uint dwBufferSize);

    public void VerifyAndDelete(string path, long expectedLength, string expectedSha256Hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256Hex);

        if (!File.Exists(path))
        {
            return;
        }

        using SafeFileHandle handle = CreateFileW(
            path,
            GENERIC_READ | DELETE,
            FILE_SHARE_NONE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new IOException(
                $"Unable to open file for verified deletion '{path}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

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
