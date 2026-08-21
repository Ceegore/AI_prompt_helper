using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace PromptHelper.Services;

public sealed class SettingsMutationLease : IDisposable
{
    private FileStream? _stream;
    private bool _disposed;

    private SettingsMutationLease(FileStream stream)
    {
        _stream = stream;
    }

    public static SettingsMutationLease Acquire(string lockPath, int timeoutMs = 5000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);

        string? dir = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var sw = Stopwatch.StartNew();
        int attempt = 0;

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
            catch (IOException) when (sw.ElapsedMilliseconds < timeoutMs)
            {
                attempt++;
                int delay = Math.Min(10 * attempt, 100);
                Thread.Sleep(delay);
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
