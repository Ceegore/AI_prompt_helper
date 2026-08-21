using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace PromptHelper.Services;

public sealed class SettingsMutationLease : IDisposable
{
    private const int ERROR_SHARING_VIOLATION = 32;
    private const int ERROR_LOCK_VIOLATION = 33;

    private FileStream? _stream;
    private bool _disposed;

    private SettingsMutationLease(FileStream stream)
    {
        _stream = stream;
    }

    public static SettingsMutationLease Acquire(string lockPath, SettingsLeasePolicy? policy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);
        SettingsLeasePolicy leasePolicy = policy ?? SettingsLeasePolicy.Default;

        string? dir = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var sw = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);

                return new SettingsMutationLease(stream);
            }
            catch (IOException ex) when (IsContention(ex) && sw.Elapsed < leasePolicy.Timeout)
            {
                Thread.Sleep(leasePolicy.RetryDelay);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    $"Unable to acquire exclusive settings mutation lease on '{lockPath}'. " +
                    "Another Prompt Helper process or settings operation may be running.",
                    ex);
            }
        }
    }

    public static SettingsMutationLease Acquire(string lockPath, int timeoutMs)
    {
        return Acquire(lockPath, new SettingsLeasePolicy(TimeSpan.FromMilliseconds(timeoutMs), TimeSpan.FromMilliseconds(25)));
    }

    public static SettingsMutationLease? TryAcquire(string lockPath, SettingsLeasePolicy? policy = null)
    {
        try
        {
            return Acquire(lockPath, policy);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsContention(IOException ex)
    {
        int code = ex.HResult & 0xFFFF;
        return code is ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream?.Dispose();
        _stream = null;
    }
}
