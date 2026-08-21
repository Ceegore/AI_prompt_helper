using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

internal sealed class ManagedDataRootSessionLease : IDisposable
{
    private readonly IStrictDirectoryOpener _opener;
    private readonly StrictPathAuthority _authority;
    private readonly List<SafeFileHandle> _handles = [];
    private bool _disposed;

    private ManagedDataRootSessionLease(IStrictDirectoryOpener? opener = null, StrictPathAuthority? authority = null)
    {
        _opener = opener ?? new WindowsStrictDirectoryOpener();
        _authority = authority ?? new StrictPathAuthority();
    }

    public static ManagedDataRootSessionLease Acquire(
        string physicalRoot,
        IStrictDirectoryOpener? opener = null,
        StrictPathAuthority? authority = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);

        var effectiveOpener = opener ?? new WindowsStrictDirectoryOpener();
        var effectiveAuthority = authority ?? new StrictPathAuthority();
        var lease = new ManagedDataRootSessionLease(effectiveOpener, effectiveAuthority);

        try
        {
            foreach (string path in new[]
            {
                physicalRoot,
                Path.Combine(physicalRoot, "prompts"),
                Path.Combine(physicalRoot, "recovery")
            })
            {
                StrictPathProbe probe = effectiveAuthority.Probe(path);

                if (probe.Kind == StrictPathKind.Missing)
                {
                    throw new DirectoryNotFoundException(
                        $"Managed session directory missing: '{path}'.");
                }

                if (probe.Kind != StrictPathKind.Directory)
                {
                    throw new InvalidDataException(
                        $"Managed session path is not a directory: '{path}'.");
                }

                SafeFileHandle handle = effectiveOpener.OpenManagedNodeLease(path);
                try
                {
                    string final = WindowsFinalPathHelper.GetNormalizedDosPath(handle);
                    string expected = PathIdentity.NormalizeForComparison(path);
                    if (!PathIdentity.Equals(final, expected))
                    {
                        throw new InvalidDataException(
                            $"Managed lease opened unexpected physical node. Expected='{expected}', Actual='{final}'.");
                    }

                    lease._handles.Add(handle);
                }
                catch
                {
                    handle.Dispose();
                    throw;
                }
            }

            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
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
