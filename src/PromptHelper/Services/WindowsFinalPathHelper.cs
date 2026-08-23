using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

public interface IFinalPathNativeApi
{
    uint GetFinalPathNameByHandle(
        SafeFileHandle handle,
        StringBuilder buffer,
        uint bufferLength,
        uint flags);
}

internal sealed class WindowsFinalPathNativeApi : IFinalPathNativeApi
{
    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    public uint GetFinalPathNameByHandle(
        SafeFileHandle handle,
        StringBuilder buffer,
        uint bufferLength,
        uint flags)
    {
        return GetFinalPathNameByHandleW(handle, buffer, bufferLength, flags);
    }
}

internal static class WindowsFinalPathHelper
{
    private const uint FILE_NAME_NORMALIZED = 0x0;
    private const uint VOLUME_NAME_DOS = 0x0;

    private static readonly IFinalPathNativeApi DefaultNativeApi = new WindowsFinalPathNativeApi();

    public static string GetNormalizedDosPath(
        SafeFileHandle handle,
        IFinalPathNativeApi? nativeApi = null)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (handle.IsInvalid)
        {
            throw new ArgumentException(
                "Handle is invalid.",
                nameof(handle));
        }

        var api = nativeApi ?? DefaultNativeApi;
        int capacity = 512;

        while (true)
        {
            var buffer =
                new StringBuilder(capacity);

            uint result =
                api.GetFinalPathNameByHandle(
                    handle,
                    buffer,
                    (uint)capacity,
                    FILE_NAME_NORMALIZED |
                    VOLUME_NAME_DOS);

            if (result == 0)
            {
                throw new IOException(
                    "GetFinalPathNameByHandleW failed.",
                    new Win32Exception(
                        Marshal.GetLastWin32Error()));
            }

            // API returns required buffer size when current buffer is too small.
            if (result >= capacity)
            {
                capacity =
                    checked((int)result + 1);
                continue;
            }

            string raw =
                buffer.ToString();

            string dosPath;

            if (raw.StartsWith(
                    @"\\?\UNC\",
                    StringComparison.OrdinalIgnoreCase))
            {
                dosPath =
                    @"\\" + raw.Substring(8);
            }
            else if (raw.StartsWith(
                         @"\\?\",
                         StringComparison.Ordinal))
            {
                dosPath = raw.Substring(4);
            }
            else
            {
                dosPath = raw;
            }

            return PathIdentity
                .NormalizeForComparison(dosPath);
        }
    }

    private const uint GENERIC_READ_ACCESS = 0x80000000;
    private const uint SHARE_ALL = 0x00000007;
    private const uint OPEN_EXISTING_DISPOSITION = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS_FLAG = 0x02000000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    /// <summary>
    /// The physical path a directory actually resolves to, following any junction or symlink
    /// on the way. Used to bind a staging file to its real parent: comparing against a lexical
    /// pathname would reject every legitimate data root that happens to sit behind a junction,
    /// while comparing against nothing at all would bind the stage to no root whatsoever.
    /// </summary>
    public static string ResolveDirectoryPhysicalPath(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        using SafeFileHandle handle = CreateFileW(
            directory,
            GENERIC_READ_ACCESS,
            SHARE_ALL,
            IntPtr.Zero,
            OPEN_EXISTING_DISPOSITION,
            FILE_FLAG_BACKUP_SEMANTICS_FLAG,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new IOException(
                $"Unable to resolve the physical path of '{directory}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        return GetNormalizedDosPath(handle);
    }

    public static void AssertStrictDescendantFile(
        string physicalRoot,
        string finalFilePath)
    {
        string root =
            PathIdentity.NormalizeForComparison(
                physicalRoot);

        string file =
            PathIdentity.NormalizeForComparison(
                finalFilePath);

        // A file artifact can never equal the data-root directory.
        if (!PathIdentity.IsStrictDescendant(
                file,
                root))
        {
            throw new InvalidDataException(
                $"Opened artifact resolved outside the " +
                $"bound data root. Root='{root}', " +
                $"File='{file}'.");
        }
    }
}
