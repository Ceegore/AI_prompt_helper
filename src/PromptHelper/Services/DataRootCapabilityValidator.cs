using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PromptHelper.Infrastructure;
using PromptHelper.Models;

namespace PromptHelper.Services;

internal sealed record ExistingLibraryCapabilityContext(
    DataFolderMigrationService.TargetLibraryKind Kind,
    string? PrimaryMetadataPath,
    string? SafetyBackupPath,
    LibraryDocument Document);

public sealed class DataRootCapabilityValidator
{
    private readonly ICapabilityFileOps _fileOps;

    internal DataRootCapabilityValidator(ICapabilityFileOps? fileOps = null)
    {
        _fileOps = fileOps ?? new DefaultCapabilityFileOps();
    }

    public DataRootCapabilityValidator()
        : this((ICapabilityFileOps?)null)
    {
    }

    internal CapabilityValidationResult ValidateWritable(
        string root,
        ICreatedPathJournal? journal = null,
        ExistingLibraryCapabilityContext? existing = null,
        MigrationCapabilityProbePlan? probePlan = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        if (!_fileOps.DirectoryExists(root))
        {
            Directory.CreateDirectory(root);
            journal?.TrackCreatedDirectory(root);
        }

        if (probePlan != null)
        {
            ProbeLocationWithPlan(root, probePlan.RootProbe, journal);

            string promptsDir = Path.Combine(root, "prompts");
            if (probePlan.PromptsProbe != null && _fileOps.DirectoryExists(promptsDir))
            {
                ProbeLocationWithPlan(root, probePlan.PromptsProbe, journal);
            }
        }
        else
        {
            ProbeLocation(root, journal);

            string promptsDir = Path.Combine(root, "prompts");
            if (_fileOps.DirectoryExists(promptsDir))
            {
                ProbeLocation(promptsDir, journal);
            }
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
        return ValidateWritable(root, null, null, null);
    }

    private string? ValidateExistingManagedFiles(
        string root,
        ExistingLibraryCapabilityContext existing)
    {
        if (existing.Kind == DataFolderMigrationService.TargetLibraryKind.ValidPrimary)
        {
            // 1. Primary metadata MUST be writable
            if (!string.IsNullOrWhiteSpace(existing.PrimaryMetadataPath) && _fileOps.FileExists(existing.PrimaryMetadataPath))
            {
                AssertFileWritable(existing.PrimaryMetadataPath, "Primary library metadata");
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
            string backupPath = existing.SafetyBackupPath ?? Path.Combine(root, "library.backup.json");
            if (_fileOps.FileExists(backupPath))
            {
                string backupJson = File.ReadAllText(backupPath);
                LibraryMetadataCompatibility compat = LibraryRepository.InspectCompatibility(backupJson);

                if (compat is not LibraryMetadataCompatibility.Future)
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

        if (existing.Kind == DataFolderMigrationService.TargetLibraryKind.RecoverableBackupOnly)
        {
            // 1. Safety backup must be readable
            if (!string.IsNullOrWhiteSpace(existing.SafetyBackupPath) && _fileOps.FileExists(existing.SafetyBackupPath))
            {
                AssertFileReadable(existing.SafetyBackupPath, "Safety backup");
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

            // Read-only safety backup in backup-only target is soft warning:
            string backupPath = existing.SafetyBackupPath ?? Path.Combine(root, "library.backup.json");
            if (_fileOps.FileExists(backupPath))
            {
                try
                {
                    AssertFileWritable(backupPath, "Safety backup");
                }
                catch (UnauthorizedAccessException)
                {
                    return "Prompt Helper recovered its library from safety backup, but library.backup.json is read-only. Further backup updates will not be written.";
                }
            }

            return null;
        }

        return null;
    }

    private static void AssertFileReadable(string filePath, string description)
    {
        try
        {
            using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException(
                $"{description} cannot be opened for reading: '{filePath}'. {ex.Message}",
                ex);
        }
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

    private void ProbeLocationWithPlan(
        string root,
        CapabilityFileProbePlan plan,
        ICreatedPathJournal? journal)
    {
        string currentFile = Path.Combine(root, plan.CurrentRelativePath);
        string replacementFile = Path.Combine(root, plan.ReplacementRelativePath);

        bool currentCreated = false;
        bool replacementCreated = false;

        try
        {
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
            replacementCreated = false;

            _fileOps.DeleteFile(currentFile);
            currentCreated = false;
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

            if (cleanupFailures.Count > 0)
            {
                throw new DataRootCapabilityProbeException(
                    root,
                    ex,
                    cleanupFailures);
            }

            throw;
        }
    }

    private void ProbeLocation(string directory, ICreatedPathJournal? journal)
    {
        // Clean stale probe files first
        try
        {
            foreach (string file in _fileOps.EnumerateFiles(directory, ".prompthelper-capability-*.tmp"))
            {
                try
                {
                    _fileOps.DeleteFile(file);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        string nonce = Guid.NewGuid().ToString("N");
        string currentFile = Path.Combine(directory, $".prompthelper-capability-{nonce}-current.tmp");
        string replacementFile = Path.Combine(directory, $".prompthelper-capability-{nonce}-replacement.tmp");

        bool currentCreated = false;
        bool replacementCreated = false;

        try
        {
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
            replacementCreated = false;

            _fileOps.DeleteFile(currentFile);
            currentCreated = false;
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

            if (cleanupFailures.Count > 0 && journal == null)
            {
                throw new DataRootCapabilityProbeException(directory, ex, cleanupFailures);
            }

            throw;
        }
    }
}
