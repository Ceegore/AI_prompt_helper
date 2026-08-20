using System;
using System.IO;

namespace PromptHelper.Services;

public sealed class TargetRootReservation : IDisposable
{
    private readonly AppInstanceLock _lock;
    private readonly string _lockPath;
    private readonly bool _deleteLockFileOnDispose;
    private bool _disposed;

    private TargetRootReservation(
        AppInstanceLock @lock,
        string lockPath,
        bool deleteLockFileOnDispose)
    {
        _lock = @lock;
        _lockPath = lockPath;
        _deleteLockFileOnDispose = deleteLockFileOnDispose;
    }

    public static TargetRootReservation? TryAcquire(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        Directory.CreateDirectory(root);

        string lockPath = Path.Combine(root, ".app.lock");
        bool existedBefore = File.Exists(lockPath);

        AppInstanceLock? @lock = AppInstanceLock.TryAcquire(lockPath);

        if (@lock is null)
        {
            return null;
        }

        return new TargetRootReservation(
            @lock,
            lockPath,
            deleteLockFileOnDispose: !existedBefore);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lock.Dispose();

        if (_deleteLockFileOnDispose)
        {
            try
            {
                File.Delete(_lockPath);
            }
            catch
            {
                // Stale unlocked lock files are safe.
            }
        }
    }
}
