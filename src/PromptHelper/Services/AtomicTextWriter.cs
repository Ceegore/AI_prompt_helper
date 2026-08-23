using System;
using System.IO;
using System.Text;

namespace PromptHelper.Services;

public sealed class AtomicTextWriter : IAtomicTextWriter, IDurableAtomicFileWriter, IDurableSettingsFileWriter
{
    private readonly WindowsDurableAtomicFileWriter _durableWriter = new();

    public void Write(string targetPath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(content);

        string directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Target path has no directory.");

        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        // CRUU15-001/002: the staging handle is retained from creation through promotion (or,
        // on failure, through handle-bound deletion). This used to close the temp stream and
        // then promote the *pathname* with a path-based rename, with a path-based delete for
        // cleanup - so it promoted whatever object occupied that name at rename time and
        // deleted whatever occupied it at cleanup time, neither of which is necessarily the
        // object it wrote. There is no pathname-addressed promotion left in this codebase.
        using var stage = WindowsOwnedDurableStage.CreateNewInResolvedParent(tempPath);
        try
        {
            stage.Write(new UTF8Encoding(false, true).GetBytes(content));
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
                    $"Atomic write failed for '{targetPath}' and staging cleanup also failed for '{tempPath}': {primary.Message} | Cleanup: {cleanup.Message}",
                    primary);
            }

            throw;
        }
    }

    void IDurableSettingsFileWriter.WriteDurable(string targetPath, string content)
    {
        Write(targetPath, content);
    }

    void IDurableAtomicFileWriter.ReplaceDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        _durableWriter.ReplaceDurable(targetPath, bytes, fileClass);
    }

    void IDurableAtomicFileWriter.CreateNewDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        _durableWriter.CreateNewDurable(targetPath, bytes, fileClass);
    }
}