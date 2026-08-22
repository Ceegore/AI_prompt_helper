using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>
/// The volume-scoped identity of an open file object: the volume serial number plus the
/// 128-bit NTFS file ID. Two handles denote the same on-disk object if and only if these
/// match. This is the only thing in the codebase that constitutes *provenance* for a
/// transaction artifact — a pathname (however carefully constructed) can be occupied by a
/// foreign object after a crash, so "the file called X is ours" is never a safe inference,
/// while "the object with identity I is the one we created" is (CRUU15-006/CRUU15-007).
/// </summary>
internal readonly record struct WindowsFileIdentity(uint VolumeSerialNumber, ulong FileIdLow, ulong FileIdHigh)
{
    private const int FileIdInfoClass = 18;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_ID_INFO
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        int fileInformationClass,
        out FILE_ID_INFO fileInformation,
        uint bufferSize);

    public static WindowsFileIdentity FromHandle(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (!GetFileInformationByHandleEx(
                handle,
                FileIdInfoClass,
                out FILE_ID_INFO info,
                (uint)Marshal.SizeOf<FILE_ID_INFO>()))
        {
            throw new IOException(
                "Unable to read the file identity of an open handle.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        return new WindowsFileIdentity(
            unchecked((uint)info.VolumeSerialNumber),
            info.FileIdLow,
            info.FileIdHigh);
    }

    /// <summary>Round-trippable textual form used in durable provenance records.</summary>
    public string ToToken() => $"{VolumeSerialNumber:x8}:{FileIdLow:x16}:{FileIdHigh:x16}";

    public static bool TryParseToken(string? token, out WindowsFileIdentity identity)
    {
        identity = default;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        string[] parts = token.Split(':');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!uint.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint volume) ||
            !ulong.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ulong low) ||
            !ulong.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ulong high))
        {
            return false;
        }

        identity = new WindowsFileIdentity(volume, low, high);
        return true;
    }
}
