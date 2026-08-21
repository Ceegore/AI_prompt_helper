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
    private readonly IReadOnlyList<string> _createdDirectories;
    private readonly IReservationFileOps _fileOps;
    private TargetReservationCleanupResult? _releaseResult;

    private TargetRootReservation(
        AppInstanceLock @lock,
        string rootPath,
        string lockPath,
        bool deleteLockFileOnDispose,
        IReadOnlyList<string> createdDirectories,
        IReservationFileOps fileOps)
    {
        _lock = @lock;
        _rootPath = rootPath;
        _lockPath = lockPath;
        _deleteLockFileOnDispose = deleteLockFileOnDispose;
        _createdDirectories = createdDirectories;
        _fileOps = fileOps;
    }

    internal static IReadOnlyList<string> GetMissingDirectoryChain(string root, IReservationFileOps ops)
    {
        var chain = new List<string>();
        string current = Path.GetFullPath(root);

        while (!string.IsNullOrEmpty(current) && !ops.DirectoryExists(current))
        {
            chain.Add(current);
            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || PathIdentity.Equals(parent, current))
            {
                break;
            }
            current = parent;
        }

        // chain was collected from deepest to shallowest, reverse to shallowest -> deepest
        chain.Reverse();
        return chain;
    }

    public static TargetRootReservation? TryAcquire(string root)
    {
        return TryAcquire(root, null);
    }

    internal static TargetRootReservation? TryAcquire(string root, IReservationFileOps? fileOps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        IReservationFileOps ops = fileOps ?? new DefaultReservationFileOps();
        IReadOnlyList<string> createdDirectories = GetMissingDirectoryChain(root, ops);

        if (createdDirectories.Count > 0)
        {
            ops.CreateDirectory(root);
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
            if (createdDirectories.Count > 0)
            {
                CleanupCreatedDirectories(createdDirectories, ops, null);
            }
            throw;
        }

        if (@lock is null)
        {
            if (createdDirectories.Count > 0)
            {
                CleanupCreatedDirectories(createdDirectories, ops, null);
            }
            return null;
        }

        return new TargetRootReservation(
            @lock,
            rootPath: root,
            lockPath: lockPath,
            deleteLockFileOnDispose: !lockExistedBefore,
            createdDirectories: createdDirectories,
            fileOps: ops);
    }

    private static void CleanupCreatedDirectories(
        IReadOnlyList<string> createdDirs,
        IReservationFileOps ops,
        List<MigrationRollbackFailure>? failures)
    {
        // Delete deepest -> shallowest
        for (int i = createdDirs.Count - 1; i >= 0; i--)
        {
            string dir = createdDirs[i];
            try
            {
                if (ops.DirectoryExists(dir))
                {
                    if (ops.EnumerateEntries(dir).Count == 0)
                    {
                        ops.DeleteDirectory(dir);
                    }
                    else if (failures is not null)
                    {
                        failures.Add(new MigrationRollbackFailure(
                            dir,
                            "DeleteCreatedDirectory",
                            "Directory was created by transition attempt but is not empty."));
                    }
                }
            }
            catch (Exception ex)
            {
                failures?.Add(new MigrationRollbackFailure(
                    dir,
                    "DeleteCreatedDirectory",
                    ex.Message));
            }
        }
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

        if (_createdDirectories.Count > 0)
        {
            CleanupCreatedDirectories(_createdDirectories, _fileOps, failures);
        }

        _releaseResult = new TargetReservationCleanupResult(failures);
        return _releaseResult;
    }

    public void Dispose()
    {
        Release();
    }
}
