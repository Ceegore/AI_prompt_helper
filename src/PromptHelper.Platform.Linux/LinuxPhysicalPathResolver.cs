using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PromptHelper.Services;

public sealed class LinuxPhysicalPathResolver : IPhysicalPathResolver
{
    private const int ErrorNoEntry = 2;
    private const int ErrorNotDirectory = 20;

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr realpath(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        IntPtr resolvedPath);

    [DllImport("libc")]
    private static extern void free(IntPtr pointer);

    public string ResolveWithNearestExistingAncestor(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "LinuxPhysicalPathResolver can only be used on Linux.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string full = Path.GetFullPath(path);
        string current = full;
        var suffix = new Stack<string>();

        while (true)
        {
            if (TryResolve(current, out string? resolved, out int nativeError))
            {
                while (suffix.Count > 0)
                {
                    resolved = Path.Combine(resolved!, suffix.Pop());
                }

                return Path.GetFullPath(resolved!);
            }

            if (nativeError is not ErrorNoEntry and not ErrorNotDirectory)
            {
                throw new IOException(
                    $"Unable to resolve physical Linux path '{current}' (errno {nativeError}).",
                    new Win32Exception(nativeError));
            }

            string trimmed = current.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            string name = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(name))
            {
                throw new DirectoryNotFoundException(
                    $"No accessible ancestor for '{full}'.");
            }

            suffix.Push(name);

            string? parent = Path.GetDirectoryName(trimmed);
            if (string.IsNullOrEmpty(parent) || PathIdentity.Equals(parent, current))
            {
                throw new DirectoryNotFoundException(
                    $"No accessible ancestor for '{full}'.");
            }

            current = parent;
        }
    }

    private static bool TryResolve(
        string path,
        out string? resolved,
        out int nativeError)
    {
        IntPtr pointer = realpath(path, IntPtr.Zero);
        if (pointer == IntPtr.Zero)
        {
            resolved = null;
            nativeError = Marshal.GetLastPInvokeError();
            return false;
        }

        try
        {
            resolved = Marshal.PtrToStringUTF8(pointer)
                ?? throw new IOException($"realpath returned an empty result for '{path}'.");
            nativeError = 0;
            return true;
        }
        finally
        {
            free(pointer);
        }
    }
}
