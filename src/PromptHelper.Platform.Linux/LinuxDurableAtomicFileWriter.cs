using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

internal sealed class LinuxDurableAtomicFileWriter : IDurableAtomicFileWriter
{
    internal static Action<string>? BeforeCreatePromotionForTests;
    private const int AtFdcwd = -100;
    private const uint RenameNoReplace = 1;

    private const int OpenReadOnly = 0;
    private const int OpenWriteOnly = 0x0001;
    private const int OpenCreate = 0x0040;
    private const int OpenExclusive = 0x0080;
    private const int OpenDirectory = 0x10000;
    private const int OpenNoFollow = 0x20000;
    private const int OpenCloseOnExec = 0x80000;
    private const uint OwnerReadWrite = 0x180; // 0600

    private const int ErrorExists = 17;

    [DllImport("libc", SetLastError = true, EntryPoint = "rename")]
    private static extern int rename(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);

    [DllImport("libc", SetLastError = true, EntryPoint = "renameat2")]
    private static extern int renameat2(
        int oldDirectoryFd,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath,
        int newDirectoryFd,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath,
        uint flags);

    [DllImport("libc", SetLastError = true, EntryPoint = "open")]
    private static extern int openDirectory(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags);

    [DllImport("libc", SetLastError = true, EntryPoint = "open")]
    private static extern int openStage(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mode);

    [DllImport("libc", SetLastError = true)]
    private static extern int fsync(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    public void ReplaceDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        string fullTarget = Path.GetFullPath(targetPath);
        string directory = RequireParentDirectory(fullTarget);
        Directory.CreateDirectory(directory);

        string stagePath = CreateStagePath(directory, fileClass);
        using FileStream stage = CreateStage(stagePath);

        try
        {
            stage.Write(bytes);
            stage.Flush(flushToDisk: true);

            if (rename(stagePath, fullTarget) != 0)
            {
                int error = Marshal.GetLastPInvokeError();
                throw NativeIOException(
                    $"Unable to atomically replace '{fullTarget}' from its durable Linux stage.",
                    error);
            }

            FlushDirectory(directory);
        }
        catch
        {
            // Once promotion succeeds the stage pathname no longer exists. Before promotion,
            // leaving an unguessably named app-owned stage behind is safer than deleting by
            // pathname after an error: Linux has no handle-bound unlink/rename primitive
            // equivalent to the Windows authority handles used by the legacy implementation.
            // Startup reconciliation recognizes this exact .prompthelper-tmp-* pattern.
            throw;
        }
    }

    public void CreateNewDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        string fullTarget = Path.GetFullPath(targetPath);
        string directory = RequireParentDirectory(fullTarget);
        Directory.CreateDirectory(directory);

        // Fast-path the normal "already exists" case without creating a stage. Correctness
        // does not depend on this probe: renameat2(RENAME_NOREPLACE) below is the atomic gate.
        if (File.Exists(fullTarget) || Directory.Exists(fullTarget))
        {
            throw new IOException($"Target already exists: '{fullTarget}'.");
        }

        string stagePath = CreateStagePath(directory, fileClass);
        using FileStream stage = CreateStage(stagePath);

        stage.Write(bytes);
        stage.Flush(flushToDisk: true);

        BeforeCreatePromotionForTests?.Invoke(fullTarget);

        int result;
        try
        {
            result = renameat2(
                AtFdcwd,
                stagePath,
                AtFdcwd,
                fullTarget,
                RenameNoReplace);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new PlatformNotSupportedException(
                "This Linux libc does not expose renameat2, which Prompt Helper requires for atomic no-overwrite durable creates.",
                ex);
        }

        if (result != 0)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error == ErrorExists)
            {
                throw new IOException(
                    $"Target was created before the durable publish completed and was preserved: '{fullTarget}'.",
                    new Win32Exception(error));
            }

            throw NativeIOException(
                $"Unable to atomically create '{fullTarget}' from its durable Linux stage.",
                error);
        }

        FlushDirectory(directory);
    }

    private static FileStream CreateStage(string stagePath)
    {
        int fd = openStage(
            stagePath,
            OpenWriteOnly | OpenCreate | OpenExclusive | OpenNoFollow | OpenCloseOnExec,
            OwnerReadWrite);

        if (fd < 0)
        {
            int error = Marshal.GetLastPInvokeError();
            throw NativeIOException(
                $"Unable to create exclusive Linux durable stage '{stagePath}'.",
                error);
        }

        var handle = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        try
        {
            return new FileStream(
                handle,
                FileAccess.Write,
                bufferSize: 16 * 1024,
                isAsync: false);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static string CreateStagePath(
        string directory,
        DurableFileClass fileClass)
    {
        string tag = DurableFileNaming.GetClassTag(fileClass);
        return Path.Combine(
            directory,
            $".prompthelper-tmp-{tag}-{Guid.NewGuid():N}.tmp");
    }

    private static string RequireParentDirectory(string fullTarget)
    {
        return Path.GetDirectoryName(fullTarget)
            ?? throw new ArgumentException(
                $"Invalid directory for target path '{fullTarget}'.",
                nameof(fullTarget));
    }

    private static void FlushDirectory(string directory)
    {
        int fd = openDirectory(
            directory,
            OpenReadOnly | OpenDirectory | OpenCloseOnExec);

        if (fd < 0)
        {
            int error = Marshal.GetLastPInvokeError();
            throw NativeIOException(
                $"Unable to open Linux directory '{directory}' for durability flush.",
                error);
        }

        try
        {
            if (fsync(fd) != 0)
            {
                int error = Marshal.GetLastPInvokeError();
                throw NativeIOException(
                    $"Unable to durably flush Linux directory '{directory}'.",
                    error);
            }
        }
        finally
        {
            _ = close(fd);
        }
    }

    private static IOException NativeIOException(string message, int error) =>
        new($"{message} (errno {error}).", new Win32Exception(error));

    private static void EnsureLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "LinuxDurableAtomicFileWriter can only be used on Linux.");
        }
    }
}
