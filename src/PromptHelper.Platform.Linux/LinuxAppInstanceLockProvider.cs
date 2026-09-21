using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PromptHelper.Services;

public sealed class LinuxAppInstanceLockProvider : IAppInstanceLockProvider
{
    private const int LockExclusive = 2;
    private const int LockNonBlocking = 4;
    private const int LockUnlock = 8;

    private const int ErrorInterrupted = 4;
    private const int ErrorWouldBlock = 11;

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

        var stream = new FileStream(
            fullPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);

        try
        {
            while (true)
            {
                int result = flock(
                    stream.SafeFileHandle.DangerousGetHandle().ToInt32(),
                    LockExclusive | LockNonBlocking);

                if (result == 0)
                {
                    return new LinuxAppInstanceLease(stream);
                }

                int error = Marshal.GetLastPInvokeError();
                if (error == ErrorInterrupted)
                {
                    continue;
                }

                if (error == ErrorWouldBlock)
                {
                    stream.Dispose();
                    return null;
                }

                throw new IOException(
                    $"Unable to acquire Prompt Helper instance lock '{fullPath}' (errno {error}).",
                    new Win32Exception(error));
            }
        }
        catch
        {
            stream.Dispose();
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
        private FileStream? _stream;

        public LinuxAppInstanceLease(FileStream stream)
        {
            _stream = stream;
        }

        public void Dispose()
        {
            FileStream? stream = Interlocked.Exchange(ref _stream, null);
            if (stream is null)
            {
                return;
            }

            try
            {
                _ = flock(
                    stream.SafeFileHandle.DangerousGetHandle().ToInt32(),
                    LockUnlock);
            }
            finally
            {
                stream.Dispose();
            }
        }
    }
}
