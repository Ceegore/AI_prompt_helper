using System.IO;

namespace PromptHelper.Services;

public sealed class AppInstanceLock : IDisposable
{
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    private readonly FileStream _stream;

    private AppInstanceLock(FileStream stream)
    {
        _stream = stream;
    }

    public static AppInstanceLock? TryAcquire(string lockPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);

        try
        {
            string? dir = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            FileStream stream = new(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

            return new AppInstanceLock(stream);
        }
        catch (IOException ex) when (IsSharingOrLockViolation(ex))
        {
            return null;
        }
    }

    public static bool IsExistingLockHeld(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        string lockPath = Path.Combine(root, ".app.lock");
        if (!File.Exists(lockPath))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                lockPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);

            return false;
        }
        catch (IOException ex) when (IsSharingOrLockViolation(ex))
        {
            return true;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }

    private static bool IsSharingOrLockViolation(IOException ex)
    {
        int win32Code = ex.HResult & 0xFFFF;

        return win32Code is ErrorSharingViolation or ErrorLockViolation;
    }
}