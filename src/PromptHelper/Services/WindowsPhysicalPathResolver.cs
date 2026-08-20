using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

public sealed class WindowsPhysicalPathResolver : IPhysicalPathResolver
{
    private const uint FileFlagBackupSemantics = 0x02000000;

    public string ResolveWithNearestExistingAncestor(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string full = Path.GetFullPath(path);

        if (Directory.Exists(full))
        {
            return ResolveExistingDirectory(full);
        }

        var remainder = new Stack<string>();
        DirectoryInfo? current = new(full);

        while (current is not null && !current.Exists)
        {
            remainder.Push(current.Name);
            current = current.Parent;
        }

        if (current is null)
        {
            throw new DirectoryNotFoundException(
                $"Could not find an existing ancestor for '{full}'.");
        }

        string resolved = ResolveExistingDirectory(current.FullName);

        while (remainder.Count > 0)
        {
            resolved = Path.Combine(resolved, remainder.Pop());
        }

        return Path.GetFullPath(resolved);
    }

    private static string ResolveExistingDirectory(string directory)
    {
        using SafeFileHandle handle = CreateFileW(
            directory,
            desiredAccess: 0,
            shareMode:
                FileShare.Read |
                FileShare.Write |
                FileShare.Delete,
            securityAttributes: IntPtr.Zero,
            creationDisposition: FileMode.Open,
            flagsAndAttributes: FileFlagBackupSemantics,
            templateFile: IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Could not resolve physical directory path '{directory}'.");
        }

        var buffer = new StringBuilder(1024);

        uint length = GetFinalPathNameByHandleW(
            handle,
            buffer,
            (uint)buffer.Capacity,
            0);

        if (length == 0)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Could not resolve final path for '{directory}'.");
        }

        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder(checked((int)length + 1));

            length = GetFinalPathNameByHandleW(
                handle,
                buffer,
                (uint)buffer.Capacity,
                0);

            if (length == 0)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"Could not resolve final path for '{directory}'.");
            }
        }

        return StripExtendedPrefix(buffer.ToString());
    }

    private static string StripExtendedPrefix(string path)
    {
        const string uncPrefix = @"\\?\UNC\";
        const string normalPrefix = @"\\?\";

        if (path.StartsWith(
            uncPrefix,
            StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[uncPrefix.Length..];
        }

        if (path.StartsWith(
            normalPrefix,
            StringComparison.OrdinalIgnoreCase))
        {
            return path[normalPrefix.Length..];
        }

        return path;
    }

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        FileMode creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle file,
        StringBuilder filePath,
        uint filePathLength,
        uint flags);
}
