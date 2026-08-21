using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

internal sealed class ManagedDataRootSessionLease : IDisposable
{
    private readonly StrictPathAuthority _strictPathAuthority = new();
    private readonly IManagedDirectoryHandleApi _api;
    private readonly List<SafeFileHandle> _handles = [];
    private bool _disposed;

    private ManagedDataRootSessionLease(IManagedDirectoryHandleApi api)
    {
        _api = api;
    }

    public static ManagedDataRootSessionLease Acquire(string physicalRoot, IManagedDirectoryHandleApi? api = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);

        var lease = new ManagedDataRootSessionLease(api ?? new WindowsManagedDirectoryHandleApi());

        try
        {
            lease.TryAddDirectoryHandle(physicalRoot);

            string promptsDir = Path.Combine(physicalRoot, "prompts");
            lease.TryAddDirectoryHandle(promptsDir);

            string recoveryDir = Path.Combine(physicalRoot, "recovery");
            lease.TryAddDirectoryHandle(recoveryDir);

            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private void TryAddDirectoryHandle(string path)
    {
        StrictPathProbe probe = _strictPathAuthority.Probe(path);
        
        if (probe.Kind != StrictPathKind.Directory)
        {
            return;
        }

        SafeFileHandle handle = _api.OpenManagedDirectoryWithoutDeleteShare(path);

        if (handle.IsInvalid)
        {
            throw _api.CreateLastError(path);
        }

        _handles.Add(handle);
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
