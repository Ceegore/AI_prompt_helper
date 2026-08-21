using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

internal static class WindowsFinalPathHelper
{
    private const uint FILE_NAME_NORMALIZED = 0x0;
    private const uint VOLUME_NAME_DOS = 0x0;

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    public static string GetNormalizedDosPath(
        SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (handle.IsInvalid)
        {
            throw new ArgumentException(
                "Handle is invalid.",
                nameof(handle));
        }

        int capacity = 512;

        while (true)
        {
            var buffer =
                new StringBuilder(capacity);

            uint result =
                GetFinalPathNameByHandleW(
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
