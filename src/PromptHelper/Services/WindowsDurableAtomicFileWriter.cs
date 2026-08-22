using System;
using System.IO;

namespace PromptHelper.Services;

internal sealed class WindowsDurableAtomicFileWriter : IDurableAtomicFileWriter
{
    public void ReplaceDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        string temp = CreateOwnedTempPath(targetPath, fileClass);

        // CRUU14-001: the staging handle is retained from creation through promotion (or,
        // on failure, through handle-bound deletion). There is no window where "the temp
        // path" is closed and later re-opened by path, so a foreign object substituted at
        // that path in between can never be promoted or destroyed in its place.
        using var stage = WindowsOwnedDurableStage.CreateNew(temp);
        try
        {
            stage.Write(bytes);
            stage.FlushDurable();
            stage.PromoteReplaceExact(targetPath);
        }
        catch (Exception primary)
        {
            try
            {
                stage.DeleteExact();
            }
            catch (Exception cleanup)
            {
                throw new IOException(
                    $"Durable write failed for '{targetPath}' and staging cleanup also failed for '{temp}': {primary.Message} | Cleanup: {cleanup.Message}",
                    primary);
            }

            throw;
        }
    }

    public void CreateNewDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        string temp = CreateOwnedTempPath(targetPath, fileClass);

        using var stage = WindowsOwnedDurableStage.CreateNew(temp);
        try
        {
            stage.Write(bytes);
            stage.FlushDurable();
            stage.PromoteNoOverwriteExact(targetPath);
        }
        catch (Exception primary)
        {
            try
            {
                stage.DeleteExact();
            }
            catch (Exception cleanup)
            {
                throw new IOException(
                    $"Durable create failed for '{targetPath}' and staging cleanup also failed for '{temp}': {primary.Message} | Cleanup: {cleanup.Message}",
                    primary);
            }

            throw;
        }
    }

    internal static string CreateOwnedTempPath(string targetPath, DurableFileClass fileClass)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(targetPath))
            ?? throw new ArgumentException($"Invalid directory for target path '{targetPath}'.", nameof(targetPath));

        Directory.CreateDirectory(dir);

        string tag = GetClassTag(fileClass);
        string name = $".prompthelper-tmp-{tag}-{Guid.NewGuid():N}.tmp";
        return Path.Combine(dir, name);
    }

    public static string GetClassTag(DurableFileClass fileClass) => fileClass switch
    {
        DurableFileClass.Settings => "settings",
        DurableFileClass.LibraryMetadata => "library",
        DurableFileClass.PromptBody => "prompt",
        DurableFileClass.RecoveryArtifact => "recovery",
        DurableFileClass.InitializationControl => "init",
        DurableFileClass.MigrationControl => "migration",
        DurableFileClass.MutationControl => "mutation",
        _ => throw new ArgumentOutOfRangeException(nameof(fileClass), fileClass, "Unknown durable file class.")
    };
}
