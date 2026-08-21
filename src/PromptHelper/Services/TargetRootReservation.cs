using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PromptHelper.Services;

public sealed record TargetReservationCleanupResult(
    IReadOnlyList<MigrationRollbackFailure> Failures)
{
    public bool Success => Failures.Count == 0;

    public string? ToWarning()
    {
        if (Success)
        {
            return null;
        }

        var lines = Failures.Select(f =>
            $"Reservation cleanup could not complete operation '{f.Operation}' on '{f.Path}': {f.Message}");

        return string.Join("\r\n", lines);
    }
}

public sealed class TargetRootReservation : IDisposable
{
    private readonly AppInstanceLock _lock;
    private readonly string _lockPath;
    private readonly string _rootPath;
    private readonly bool _deleteLockFileOnDispose;
    private readonly bool _deleteRootIfStillEmptyOnDispose;
    private readonly IReservationFileOps _fileOps;
    private TargetReservationCleanupResult? _releaseResult;

    private TargetRootReservation(
        AppInstanceLock @lock,
        string rootPath,
        string lockPath,
        bool deleteLockFileOnDispose,
        bool deleteRootIfStillEmptyOnDispose,
        IReservationFileOps fileOps)
    {
        _lock = @lock;
        _rootPath = rootPath;
        _lockPath = lockPath;
        _deleteLockFileOnDispose = deleteLockFileOnDispose;
        _deleteRootIfStillEmptyOnDispose = deleteRootIfStillEmptyOnDispose;
        _fileOps = fileOps;
    }

    public static TargetRootReservation? TryAcquire(string root)
    {
        return TryAcquire(root, null);
    }

    internal static TargetRootReservation? TryAcquire(string root, IReservationFileOps? fileOps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        IReservationFileOps ops = fileOps ?? new DefaultReservationFileOps();
        bool rootExistedBefore = ops.DirectoryExists(root);

        if (!rootExistedBefore)
        {
            Directory.CreateDirectory(root);
        }

        string lockPath = Path.Combine(root, ".app.lock");
        bool lockExistedBefore = ops.FileExists(lockPath);

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
                    if (ops.DirectoryExists(root) && ops.EnumerateEntries(root).Count == 0)
                    {
                        ops.DeleteDirectory(root);
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
                    if (ops.DirectoryExists(root) && ops.EnumerateEntries(root).Count == 0)
                    {
                        ops.DeleteDirectory(root);
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
            deleteRootIfStillEmptyOnDispose: !rootExistedBefore,
            fileOps: ops);
    }

    public TargetReservationCleanupResult Release()
    {
        if (_releaseResult is not null)
        {
            return _releaseResult;
        }

        var failures = new List<MigrationRollbackFailure>();

        try
        {
            _lock.Dispose();
        }
        catch (Exception ex)
        {
            failures.Add(new MigrationRollbackFailure(
                _lockPath,
                "ReleaseLockHandle",
                ex.Message));
        }

        if (_deleteLockFileOnDispose)
        {
            try
            {
                if (_fileOps.FileExists(_lockPath))
                {
                    _fileOps.DeleteFile(_lockPath);
                }
            }
            catch (Exception ex)
            {
                failures.Add(new MigrationRollbackFailure(
                    _lockPath,
                    "DeleteReservationLockFile",
                    ex.Message));
            }
        }

        if (_deleteRootIfStillEmptyOnDispose)
        {
            try
            {
                if (_fileOps.DirectoryExists(_rootPath) &&
                    _fileOps.EnumerateEntries(_rootPath).Count == 0)
                {
                    _fileOps.DeleteDirectory(_rootPath);
                }
            }
            catch (Exception ex)
            {
                failures.Add(new MigrationRollbackFailure(
                    _rootPath,
                    "DeleteEmptyRoot",
                    ex.Message));
            }
        }

        _releaseResult = new TargetReservationCleanupResult(failures);
        return _releaseResult;
    }

    public void Dispose()
    {
        Release();
    }
}
