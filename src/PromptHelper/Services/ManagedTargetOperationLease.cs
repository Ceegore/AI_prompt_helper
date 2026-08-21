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
