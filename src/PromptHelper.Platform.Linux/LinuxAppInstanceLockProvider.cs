using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

public sealed class LinuxAppInstanceLockProvider : IAppInstanceLockProvider
{
    private const int OpenReadWrite = 0x0002;
    private const int OpenCreate = 0x0040;
    private const int OpenNoFollow = 0x20000;
    private const int OpenCloseOnExec = 0x80000;
    private const uint OwnerReadWrite = 0x180; // 0600

    private const int LockExclusive = 2;
    private const int LockNonBlocking = 4;
    private const int LockUnlock = 8;

    private const int ErrorInterrupted = 4;
    private const int ErrorWouldBlock = 11;

    [DllImport("libc", SetLastError = true, EntryPoint = "open")]
    private static extern int open(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mode);

    [DllImport("libc", SetLastError = true)]
    private static extern int flock(int fd, int operation);

    public IAppInstanceLease? TryAcquire(string lockPath)
    {
        EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);

        string fullPath = Path.GetFullPath(lockPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        SafeFileHandle handle = OpenLockFile(fullPath);
        try
        {
            while (true)
            {
                int result = flock(
                    handle.DangerousGetHandle().ToInt32(),
                    LockExclusive | LockNonBlocking);

                if (result == 0)
                {
                    return new LinuxAppInstanceLease(handle);
                }

                int error = Marshal.GetLastPInvokeError();
                if (error == ErrorInterrupted)
                {
                    continue;
                }

                if (error == ErrorWouldBlock)
                {
                    handle.Dispose();
                    return null;
                }

                throw new IOException(
                    $"Unable to acquire Prompt Helper instance lock '{fullPath}' (errno {error}).",
                    new Win32Exception(error));
            }
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public bool IsExistingLockHeld(string root)
    {
        EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        string lockPath = Path.Combine(Path.GetFullPath(root), ".app.lock");
        if (!File.Exists(lockPath))
        {
            return false;
        }

        using IAppInstanceLease? lease = TryAcquire(lockPath);
        return lease is null;
    }

    private static SafeFileHandle OpenLockFile(string fullPath)
    {
        while (true)
        {
            int fd = open(
                fullPath,
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

            throw new IOException(
                $"Unable to open Prompt Helper instance lock '{fullPath}' (errno {error}).",
                new Win32Exception(error));
        }
    }

    private static void EnsureLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "LinuxAppInstanceLockProvider can only be used on Linux.");
        }
    }

    private sealed class LinuxAppInstanceLease : IAppInstanceLease
    {
        private SafeFileHandle? _handle;

        public LinuxAppInstanceLease(SafeFileHandle handle)
        {
            _handle = handle;
        }

        public void Dispose()
        {
            SafeFileHandle? handle = Interlocked.Exchange(ref _handle, null);
            if (handle is null)
            {
                return;
            }

            try
            {
                _ = flock(
                    handle.DangerousGetHandle().ToInt32(),
                    LockUnlock);
            }
            finally
            {
                handle.Dispose();
            }
        }
    }
}
