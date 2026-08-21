using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PromptHelper.Services;

public sealed record TargetReservationCleanupResult(
    IReadOnlyList<MigrationRollbackFailure> Failures)
{
    public bool Success => Failures.Count == 0;
}

public sealed class TargetRootReservation : IDisposable
{
    private readonly AppInstanceLock _lock;
    private readonly string _lockPath;
    private readonly string _rootPath;
    private readonly bool _deleteLockFileOnDispose;
    private readonly bool _deleteRootIfStillEmptyOnDispose;
    private bool _disposed;

    private TargetRootReservation(
        AppInstanceLock @lock,
        string rootPath,
        string lockPath,
        bool deleteLockFileOnDispose,
        bool deleteRootIfStillEmptyOnDispose)
    {
        _lock = @lock;
        _rootPath = rootPath;
        _lockPath = lockPath;
        _deleteLockFileOnDispose = deleteLockFileOnDispose;
        _deleteRootIfStillEmptyOnDispose = deleteRootIfStillEmptyOnDispose;
    }

    public static TargetRootReservation? TryAcquire(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        bool rootExistedBefore = Directory.Exists(root);

        if (!rootExistedBefore)
        {
            Directory.CreateDirectory(root);
        }

        string lockPath = Path.Combine(root, ".app.lock");
        bool lockExistedBefore = File.Exists(lockPath);

        AppInstanceLock? @lock;
        try
        {
            @lock = AppInstanceLock.TryAcquire(lockPath);
        }
        catch
        {
            if (!rootExistedBefore)
            {
                try
                {
                    if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any())
                    {
                        Directory.Delete(root);
                    }
                }
                catch
                {
                }
            }
            throw;
        }

        if (@lock is null)
        {
            if (!rootExistedBefore)
            {
                try
                {
                    if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any())
                    {
                        Directory.Delete(root);
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        return new TargetRootReservation(
            @lock,
            rootPath: root,
            lockPath: lockPath,
            deleteLockFileOnDispose: !lockExistedBefore,
            deleteRootIfStillEmptyOnDispose: !rootExistedBefore);
    }

    public TargetReservationCleanupResult Release()
    {
        if (_disposed)
        {
            return new TargetReservationCleanupResult([]);
        }

        _disposed = true;
        _lock.Dispose();

        var failures = new List<MigrationRollbackFailure>();

        if (_deleteLockFileOnDispose)
        {
            try
            {
                if (File.Exists(_lockPath))
                {
                    File.Delete(_lockPath);
                }
            }
            catch (Exception ex)
            {
                failures.Add(new MigrationRollbackFailure(_lockPath, "DeleteLockFile", ex.Message));
            }
        }

        if (_deleteRootIfStillEmptyOnDispose)
        {
            try
            {
                if (Directory.Exists(_rootPath) &&
                    !Directory.EnumerateFileSystemEntries(_rootPath).Any())
                {
                    Directory.Delete(_rootPath);
                }
            }
            catch (Exception ex)
            {
                failures.Add(new MigrationRollbackFailure(_rootPath, "DeleteEmptyRoot", ex.Message));
            }
        }

        return new TargetReservationCleanupResult(failures);
    }

    public void Dispose()
    {
        Release();
    }
}
