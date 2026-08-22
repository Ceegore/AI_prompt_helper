using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>
/// Opens the current target with <c>FILE_SHARE_READ</c> only — denying every other process
/// both write access and delete/rename-replace access for the (short) duration this handle is
/// held — and verifies its exact hash from that handle before closing it. This is a stronger,
/// OS-enforced check than a plain <c>File.ReadAllBytes</c> comparison, but callers still
/// perform the actual replacement as a separate step through their own (test-doubled) durable
/// writer, so the exclusion does not extend through that write. See CRUU14-002/CRUU14-003 for
/// the reasoning and the resulting limits.
/// </summary>
internal sealed class WindowsExpectedFileCasReplacer : IExpectedFileCasReplacer
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    public void VerifyCurrentMatches(string targetPath, string expectedSha256Hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256Hex);

        using SafeFileHandle verifyHandle = CreateFileW(
            targetPath,
            GENERIC_READ,
            FILE_SHARE_READ,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        if (verifyHandle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            throw new StaleExpectedFileException(
                $"Unable to open '{targetPath}' to verify its current content: {new Win32Exception(error).Message}",
                new Win32Exception(error));
        }

        long actualLength = RandomAccess.GetLength(verifyHandle);
        byte[] actualBytes = new byte[actualLength];
        RandomAccess.Read(verifyHandle, actualBytes, 0);
        string actualHex = Convert.ToHexStringLower(SHA256.HashData(actualBytes));
        if (!string.Equals(actualHex, expectedSha256Hex, StringComparison.OrdinalIgnoreCase))
        {
            throw new StaleExpectedFileException(
                $"'{targetPath}' changed outside the current state. Reload before editing.");
        }
    }
}
