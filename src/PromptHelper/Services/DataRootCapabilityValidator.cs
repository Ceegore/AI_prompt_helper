using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using PromptHelper.Infrastructure;
using PromptHelper.Models;

namespace PromptHelper.Services;

internal sealed record ExistingLibraryCapabilityContext(
    string MetadataPath,
    LibraryDocument Document);

public sealed class DataRootCapabilityValidator
{
    private readonly ICapabilityFileOps _fileOps;

    internal DataRootCapabilityValidator(ICapabilityFileOps? fileOps = null)
    {
        _fileOps = fileOps ?? new DefaultCapabilityFileOps();
    }

    public DataRootCapabilityValidator(IAtomicTextWriter? writer)
        : this((ICapabilityFileOps?)null)
    {
    }

    public DataRootCapabilityValidator()
        : this((ICapabilityFileOps?)null)
    {
    }

    internal CapabilityValidationResult ValidateWritable(
        string root,
        ICreatedPathJournal? journal = null,
        ExistingLibraryCapabilityContext? existing = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        if (!_fileOps.DirectoryExists(root))
        {
            Directory.CreateDirectory(root);
            journal?.TrackCreatedDirectory(root);
        }

        ProbeLocation(root, journal);

        string promptsDir = Path.Combine(root, "prompts");
        if (_fileOps.DirectoryExists(promptsDir))
        {
            ProbeLocation(promptsDir, journal);
        }

        string? warning = null;
        if (existing != null)
        {
            warning = ValidateExistingManagedFiles(root, existing);
        }

        return new CapabilityValidationResult(warning);
    }

    public CapabilityValidationResult ValidateWritable(string root)
    {
        return ValidateWritable(root, null, null);
    }

    private string? ValidateExistingManagedFiles(
        string root,
        ExistingLibraryCapabilityContext existing)
    {
        // 1. Primary metadata MUST be writable
        if (!string.IsNullOrWhiteSpace(existing.MetadataPath) && _fileOps.FileExists(existing.MetadataPath))
        {
            AssertFileWritable(existing.MetadataPath, "Primary library metadata");
        }

        // 2. Active prompt bodies MUST be writable
        string promptsDir = Path.Combine(root, "prompts");
        if (existing.Document?.Prompts != null)
        {
            foreach (PromptRecord prompt in existing.Document.Prompts)
            {
                string promptPath = Path.Combine(promptsDir, $"{prompt.Id:N}.md");
                if (_fileOps.FileExists(promptPath))
                {
                    AssertFileWritable(promptPath, "Prompt body");
                }
            }
        }

        // 3. Safety backup policy: Future schema is preserved; read-only is a warning, not hard error
        string backupPath = Path.Combine(root, "library.backup.json");
        if (_fileOps.FileExists(backupPath))
        {
            bool isFutureBackup = false;
            try
            {
                string backupJson = File.ReadAllText(backupPath);
                using var doc = JsonDocument.Parse(backupJson);
                if (doc.RootElement.TryGetProperty("schemaVersion", out JsonElement verElement) &&
                    verElement.TryGetInt32(out int version) &&
                    version > LibraryDocument.CurrentSchemaVersion)
                {
                    isFutureBackup = true;
                }
            }
            catch
            {
                // If unparseable or error, treat as current inspection
            }

            if (!isFutureBackup)
            {
                try
                {
                    AssertFileWritable(backupPath, "Safety backup");
                }
                catch (UnauthorizedAccessException)
                {
                    return "Prompt Helper safety backup (library.backup.json) is read-only or not writable; safety backup synchronization will not occur.";
                }
            }
        }

        return null;
    }

    private static void AssertFileWritable(string filePath, string description)
    {
        FileAttributes attributes = File.GetAttributes(filePath);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            throw new UnauthorizedAccessException(
                $"{description} is read-only: '{filePath}'.");
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
                    $"{description} is not writable: '{filePath}'.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException(
                $"{description} cannot be opened for writing: '{filePath}'. {ex.Message}",
                ex);
        }
    }

    private void ProbeLocation(string directory, ICreatedPathJournal? journal)
    {
        string probeDir = Path.Combine(
            directory,
            $".prompthelper-write-probe-{Guid.NewGuid():N}");

        string currentFile = Path.Combine(probeDir, "probe-current.txt");
        string replacementFile = Path.Combine(probeDir, "probe-replacement.tmp");

        bool dirCreated = false;
        bool currentCreated = false;
        bool replacementCreated = false;

        try
        {
            Directory.CreateDirectory(probeDir);
            dirCreated = true;
            journal?.TrackCreatedDirectory(probeDir);

            using (Stream curStream = _fileOps.CreateNew(currentFile))
            {
                currentCreated = true;
                journal?.TrackCreatedFile(currentFile);
                byte[] data = Encoding.UTF8.GetBytes("create");
                curStream.Write(data, 0, data.Length);
                _fileOps.FlushToDisk(curStream);
            }

            using (Stream repStream = _fileOps.CreateNew(replacementFile))
            {
                replacementCreated = true;
                journal?.TrackCreatedFile(replacementFile);
                byte[] data = Encoding.UTF8.GetBytes("replace");
                repStream.Write(data, 0, data.Length);
                _fileOps.FlushToDisk(repStream);
            }

            _fileOps.Replace(replacementFile, currentFile, null);
            replacementCreated = false; // Replace moved replacementFile to currentFile

            _fileOps.DeleteFile(currentFile);
            currentCreated = false;

            IReadOnlyList<string> entries = _fileOps.EnumerateEntries(probeDir);
            if (entries.Count > 0)
            {
                throw new IOException($"Probe directory '{probeDir}' is unexpectedly non-empty after test.");
            }

            _fileOps.DeleteDirectory(probeDir);
            dirCreated = false;
        }
        catch (Exception ex)
        {
            var cleanupFailures = new List<MigrationRollbackFailure>();

            if (currentCreated)
            {
                try
                {
                    if (_fileOps.FileExists(currentFile))
                    {
                        _fileOps.DeleteFile(currentFile);
                    }
                }
                catch (Exception deleteEx)
                {
                    cleanupFailures.Add(new MigrationRollbackFailure(currentFile, "DeleteProbeCurrentFile", deleteEx.Message));
                }
            }

            if (replacementCreated)
            {
                try
                {
                    if (_fileOps.FileExists(replacementFile))
                    {
                        _fileOps.DeleteFile(replacementFile);
                    }
                }
                catch (Exception deleteEx)
                {
                    cleanupFailures.Add(new MigrationRollbackFailure(replacementFile, "DeleteProbeReplacementFile", deleteEx.Message));
                }
            }

            if (dirCreated)
            {
                try
                {
                    if (_fileOps.DirectoryExists(probeDir))
                    {
                        IReadOnlyList<string> remaining = _fileOps.EnumerateEntries(probeDir);
                        if (remaining.Count == 0)
                        {
                            _fileOps.DeleteDirectory(probeDir);
                        }
                        else
                        {
                            cleanupFailures.Add(new MigrationRollbackFailure(
                                probeDir,
                                "DeleteProbeDirectoryNonEmpty",
                                $"Directory is not empty (contains: {string.Join(", ", remaining.Select(Path.GetFileName))})"));
                        }
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
