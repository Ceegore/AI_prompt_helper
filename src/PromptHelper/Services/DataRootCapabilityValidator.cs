using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PromptHelper.Infrastructure;
using PromptHelper.Models;

namespace PromptHelper.Services;

internal sealed record ExistingLibraryCapabilityContext(
    string MetadataPath,
    LibraryDocument Document);

public sealed class DataRootCapabilityValidator
{
    private readonly IAtomicTextWriter _writer;

    public DataRootCapabilityValidator(IAtomicTextWriter? writer = null)
    {
        _writer = writer ?? new AtomicTextWriter();
    }

    internal void ValidateWritable(
        string root,
        ICreatedPathJournal? journal = null,
        ExistingLibraryCapabilityContext? existing = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
            journal?.TrackCreatedDirectory(root);
        }

        ProbeLocation(root, journal);

        string promptsDir = Path.Combine(root, "prompts");
        if (Directory.Exists(promptsDir))
        {
            ProbeLocation(promptsDir, journal);
        }

        if (existing != null)
        {
            ValidateExistingManagedFiles(root, existing);
        }
    }

    public void ValidateWritable(string root)
    {
        ValidateWritable(root, null, null);
    }

    private void ValidateExistingManagedFiles(
        string root,
        ExistingLibraryCapabilityContext existing)
    {
        var pathsToCheck = new List<string>();

        if (!string.IsNullOrWhiteSpace(existing.MetadataPath) && File.Exists(existing.MetadataPath))
        {
            pathsToCheck.Add(existing.MetadataPath);
        }

        string backupPath = Path.Combine(root, "library.backup.json");
        if (File.Exists(backupPath))
        {
            pathsToCheck.Add(backupPath);
        }

        string promptsDir = Path.Combine(root, "prompts");
        if (existing.Document?.Prompts != null)
        {
            foreach (PromptRecord prompt in existing.Document.Prompts)
            {
                string promptPath = Path.Combine(promptsDir, $"{prompt.Id:N}.md");
                if (File.Exists(promptPath))
                {
                    pathsToCheck.Add(promptPath);
                }
            }
        }

        foreach (string filePath in pathsToCheck)
        {
            FileAttributes attributes = File.GetAttributes(filePath);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                throw new UnauthorizedAccessException(
                    $"Managed Prompt Helper file is read-only: '{filePath}'.");
            }

            try
            {
                using FileStream stream = new(
                    filePath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.Read);

                if (!stream.CanWrite)
                {
                    throw new UnauthorizedAccessException(
                        $"Managed Prompt Helper file is not writable: '{filePath}'.");
                }
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                throw new UnauthorizedAccessException(
                    $"Managed Prompt Helper file cannot be opened for writing: '{filePath}'. {ex.Message}",
                    ex);
            }
        }
    }

    private void ProbeLocation(string directory, ICreatedPathJournal? journal)
    {
        string probeDir = Path.Combine(
            directory,
            $".prompthelper-write-probe-{Guid.NewGuid():N}");

        string probeFile = Path.Combine(probeDir, "probe.txt");
        bool dirCreated = false;
        bool fileCreated = false;

        try
        {
            Directory.CreateDirectory(probeDir);
            dirCreated = true;
            journal?.TrackCreatedDirectory(probeDir);

            _writer.Write(probeFile, "create");
            fileCreated = true;
            journal?.TrackCreatedFile(probeFile);

            _writer.Write(probeFile, "replace"); // exercises File.Replace

            File.Delete(probeFile);
            fileCreated = false;

            Directory.Delete(probeDir);
            dirCreated = false;
        }
        catch (Exception ex)
        {
            var cleanupFailures = new List<MigrationRollbackFailure>();

            if (fileCreated)
            {
                try
                {
                    if (File.Exists(probeFile))
                    {
                        File.Delete(probeFile);
                    }
                }
                catch (Exception deleteEx)
                {
                    cleanupFailures.Add(new MigrationRollbackFailure(probeFile, "DeleteProbeFile", deleteEx.Message));
                }
            }

            if (dirCreated)
            {
                try
                {
                    if (Directory.Exists(probeDir) &&
                        !Directory.EnumerateFileSystemEntries(probeDir).Any())
                    {
                        Directory.Delete(probeDir);
                    }
                }
                catch (Exception deleteEx)
                {
                    cleanupFailures.Add(new MigrationRollbackFailure(probeDir, "DeleteProbeDirectory", deleteEx.Message));
                }
            }

            if (cleanupFailures.Count > 0 && journal == null)
            {
                throw new DataRootCapabilityProbeException(directory, ex, cleanupFailures);
            }

            throw;
        }
    }
}
