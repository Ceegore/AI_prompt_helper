using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

internal static class LinuxNativeFileSystem
{
    internal const int ErrorNoEntry = 2;
    internal const int ErrorInterrupted = 4;
    internal const int ErrorExists = 17;
    internal const int ErrorNotDirectory = 20;
    internal const int ErrorLoop = 40;

    private const int AtFdcwd = -100;
    private const uint RenameNoReplace = 1;

    private const int OpenReadOnly = 0;
    private const int OpenWriteOnly = 0x0001;
    private const int OpenReadWrite = 0x0002;
    private const int OpenCreate = 0x0040;
    private const int OpenExclusive = 0x0080;
    private const int OpenDirectory = 0x10000;
    private const int OpenNoFollow = 0x20000;
    private const int OpenCloseOnExec = 0x80000;
    private const uint OwnerReadWrite = 0x180; // 0600

    [DllImport("libc", SetLastError = true, EntryPoint = "open")]
    private static extern int openReadOnly(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags);

    [DllImport("libc", SetLastError = true, EntryPoint = "open")]
    private static extern int openCreate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mode);

    [DllImport("libc", SetLastError = true, EntryPoint = "rename")]
    private static extern int rename(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);

    [DllImport("libc", SetLastError = true, EntryPoint = "renameat2")]
    private static extern int renameat2(
        int oldDirectoryFd,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath,
        int newDirectoryFd,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath,
        uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int fsync(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    public static FileStream CreateExclusiveStage(string stagePath)
    {
        EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(stagePath);

        int fd = openCreate(
            stagePath,
            OpenWriteOnly | OpenCreate | OpenExclusive | OpenNoFollow | OpenCloseOnExec,
            OwnerReadWrite);

        if (fd < 0)
        {
            int error = Marshal.GetLastPInvokeError();
            throw NativeIOException(
                $"Unable to create exclusive Linux stage '{stagePath}'.",
                error);
        }

        var handle = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        try
        {
            return new FileStream(
                handle,
                FileAccess.Write,
                bufferSize: 16 * 1024,
                isAsync: false);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public static SafeFileHandle? OpenReadWriteNoFollowOrNull(string path)
    {
        EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        while (true)
        {
            int fd = openReadOnly(
                path,
                OpenReadWrite | OpenNoFollow | OpenCloseOnExec);

            if (fd >= 0)
            {
                return new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
            }

            int error = Marshal.GetLastPInvokeError();
            if (error == ErrorInterrupted)
            {
                continue;
            }

            if (error is ErrorNoEntry or ErrorNotDirectory)
            {
                return null;
            }

            if (error == ErrorLoop)
            {
                throw new InvalidDataException(
                    $"Refusing to follow a symbolic link at '{path}'.");
            }

            throw NativeIOException(
                $"Unable to open Linux file '{path}' for read/write without following links.",
                error);
        }
    }

    public static SafeFileHandle OpenReadWriteNoFollowOrCreate(string path)
    {
        EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        while (true)
        {
            int fd = openCreate(
                path,
                OpenReadWrite | OpenCreate | OpenNoFollow | OpenCloseOnExec,
                OwnerReadWrite);

            if (fd >= 0)
            {
                return new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
            }

            int error = Marshal.GetLastPInvokeError();
            if (error == ErrorInterrupted)
            {
                continue;
            }

            if (error == ErrorLoop)
            {
                throw new InvalidDataException(
                    $"Refusing to follow a symbolic link at '{path}'.");
            }

            throw NativeIOException(
                $"Unable to open Linux file '{path}' for read/write without following links.",
                error);
        }
    }

    public static SafeFileHandle? OpenReadOnlyNoFollowOrNull(string path)
    {
        EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        while (true)
        {
            int fd = openReadOnly(
                path,
                OpenReadOnly | OpenNoFollow | OpenCloseOnExec);

            if (fd >= 0)
            {
                return new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
            }

            int error = Marshal.GetLastPInvokeError();
            if (error == ErrorInterrupted)
            {
                continue;
            }

            if (error is ErrorNoEntry or ErrorNotDirectory)
            {
                return null;
            }

            if (error == ErrorLoop)
            {
                throw new InvalidDataException(
                    $"Refusing to follow a symbolic link at '{path}'.");
            }

            throw NativeIOException(
                $"Unable to open Linux file '{path}' without following links.",
                error);
        }
    }

    public static void RenameReplace(string sourcePath, string targetPath)
    {
        EnsureLinux();

        if (rename(sourcePath, targetPath) != 0)
        {
            int error = Marshal.GetLastPInvokeError();
            throw NativeIOException(
                $"Unable to atomically rename '{sourcePath}' to '{targetPath}'.",
                error);
        }
    }

    public static bool TryRenameNoReplace(
        string sourcePath,
        string targetPath,
        out int nativeError)
    {
        EnsureLinux();

        int result;
        try
        {
            result = renameat2(
                AtFdcwd,
                sourcePath,
                AtFdcwd,
                targetPath,
                RenameNoReplace);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new PlatformNotSupportedException(
                "This Linux libc does not expose renameat2, which Prompt Helper requires for fail-closed atomic renames.",
                ex);
        }

        if (result == 0)
        {
            nativeError = 0;
            return true;
        }

        nativeError = Marshal.GetLastPInvokeError();
        return false;
    }

    public static void FlushDirectory(string directory)
    {
        EnsureLinux();

        int fd = openReadOnly(
            directory,
            OpenReadOnly | OpenDirectory | OpenCloseOnExec);

        if (fd < 0)
        {
            int error = Marshal.GetLastPInvokeError();
            throw NativeIOException(
                $"Unable to open Linux directory '{directory}' for durability flush.",
                error);
        }

        try
        {
            if (fsync(fd) != 0)
            {
                int error = Marshal.GetLastPInvokeError();
                throw NativeIOException(
                    $"Unable to durably flush Linux directory '{directory}'.",
                    error);
            }
        }
        finally
        {
            _ = close(fd);
        }
    }

    public static IOException NativeIOException(string message, int error) =>
        new($"{message} (errno {error}).", new Win32Exception(error));

    public static void EnsureLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "Linux filesystem primitives can only be used on Linux.");
        }
    }
}
