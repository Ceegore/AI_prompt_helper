using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

internal sealed class ManagedTargetOperationLease : IDisposable
{
    private readonly List<SafeFileHandle> _handles = [];
    private bool _disposed;

    public string PhysicalRoot { get; }

    private ManagedTargetOperationLease(string physicalRoot)
    {
        PhysicalRoot = PathIdentity.NormalizeForComparison(physicalRoot);
    }

    public static ManagedTargetOperationLease Acquire(
        string physicalRoot,
        bool promptsMayBeMissing,
        bool recoveryMayBeMissing,
        IStrictDirectoryOpener? opener = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);

        var activeOpener = opener ?? new WindowsStrictDirectoryOpener();
        var lease = new ManagedTargetOperationLease(physicalRoot);

        try
        {
            lease.AddRequired(physicalRoot, activeOpener);

            lease.AddOptionalOrRequired(
                Path.Combine(physicalRoot, "prompts"),
                promptsMayBeMissing,
                activeOpener);

            lease.AddOptionalOrRequired(
                Path.Combine(physicalRoot, "recovery"),
                recoveryMayBeMissing,
                activeOpener);

            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Acquires a lease covering only the target root itself, not its "prompts"/"recovery"
    /// children. Use this when interrupted-retry cleanup may still need to delete those
    /// children (a deny-delete lease on a directory blocks its own deletion), then bind them
    /// with <see cref="BindManagedChild"/> once their final state for this operation is known.
    /// </summary>
    public static ManagedTargetOperationLease AcquireRootOnly(
        string physicalRoot,
        IStrictDirectoryOpener? opener = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);

        var activeOpener = opener ?? new WindowsStrictDirectoryOpener();
        var lease = new ManagedTargetOperationLease(physicalRoot);

        try
        {
            lease.AddRequired(physicalRoot, activeOpener);
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Binds an additional managed child directory (e.g. "prompts" or "recovery" created or
    /// confirmed after retry cleanup) into an already-acquired lease, verifying its identity
    /// the same way as the directories acquired up front.
    /// </summary>
    public void BindManagedChild(string expectedPhysicalPath, IStrictDirectoryOpener? opener = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPhysicalPath);
        ObjectDisposedException.ThrowIf(_disposed, this);

        AddRequired(expectedPhysicalPath, opener ?? new WindowsStrictDirectoryOpener());
    }

    /// <summary>
    /// Binds <paramref name="expectedPhysicalPath"/> into the lease if it currently exists as
    /// a directory; a no-op otherwise. Use after an operation that may or may not have created
    /// the directory (e.g. migration copy with no files for that role).
    /// </summary>
    public void BindManagedChildIfPresent(string expectedPhysicalPath, IStrictDirectoryOpener? opener = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPhysicalPath);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Directory.Exists(expectedPhysicalPath))
        {
            BindManagedChild(expectedPhysicalPath, opener);
        }
    }

    private void AddRequired(
        string expectedPhysicalPath,
        IStrictDirectoryOpener opener)
    {
        SafeFileHandle handle = opener.OpenManagedNodeLease(expectedPhysicalPath);
        try
        {
            string final = WindowsFinalPathHelper.GetNormalizedDosPath(handle);
            string expected = PathIdentity.NormalizeForComparison(expectedPhysicalPath);

            if (!PathIdentity.Equals(final, expected))
            {
                throw new InvalidDataException(
                    $"Target operation lease opened unexpected node. Expected='{expected}', Actual='{final}'.");
            }

            _handles.Add(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private void AddOptionalOrRequired(
        string expectedPhysicalPath,
        bool mayBeMissing,
        IStrictDirectoryOpener opener)
    {
        if (mayBeMissing && !Directory.Exists(expectedPhysicalPath))
        {
            return;
        }

        AddRequired(expectedPhysicalPath, opener);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (SafeFileHandle handle in _handles)
        {
            handle.Dispose();
        }

        _handles.Clear();
    }
}
