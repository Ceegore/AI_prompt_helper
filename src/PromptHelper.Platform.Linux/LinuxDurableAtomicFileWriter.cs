namespace PromptHelper.Services;

internal sealed class LinuxDurableAtomicFileWriter : IDurableAtomicFileWriter
{
    internal static Action<string>? BeforeCreatePromotionForTests;

    public void ReplaceDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        LinuxNativeFileSystem.EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        string fullTarget = Path.GetFullPath(targetPath);
        string directory = RequireParentDirectory(fullTarget);
        Directory.CreateDirectory(directory);

        string stagePath = CreateStagePath(directory, fileClass);
        using FileStream stage =
            LinuxNativeFileSystem.CreateExclusiveStage(stagePath);

        stage.Write(bytes);
        stage.Flush(flushToDisk: true);

        LinuxNativeFileSystem.RenameReplace(stagePath, fullTarget);
        LinuxNativeFileSystem.FlushDirectory(directory);
    }

    public void CreateNewDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        LinuxNativeFileSystem.EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        string fullTarget = Path.GetFullPath(targetPath);
        string directory = RequireParentDirectory(fullTarget);
        Directory.CreateDirectory(directory);

        // Fast-path the normal "already exists" case without creating a stage. Correctness
        // does not depend on this probe: RENAME_NOREPLACE below is the atomic gate.
        if (File.Exists(fullTarget) || Directory.Exists(fullTarget))
        {
            throw new IOException($"Target already exists: '{fullTarget}'.");
        }

        string stagePath = CreateStagePath(directory, fileClass);
        using FileStream stage =
            LinuxNativeFileSystem.CreateExclusiveStage(stagePath);

        stage.Write(bytes);
        stage.Flush(flushToDisk: true);

        BeforeCreatePromotionForTests?.Invoke(fullTarget);

        if (!LinuxNativeFileSystem.TryRenameNoReplace(
                stagePath,
                fullTarget,
                out int error))
        {
            if (error == LinuxNativeFileSystem.ErrorExists)
            {
                throw new IOException(
                    $"Target was created before the durable publish completed and was preserved: '{fullTarget}'.");
            }

            throw LinuxNativeFileSystem.NativeIOException(
                $"Unable to atomically create '{fullTarget}' from its durable Linux stage.",
                error);
        }

        LinuxNativeFileSystem.FlushDirectory(directory);
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
}
