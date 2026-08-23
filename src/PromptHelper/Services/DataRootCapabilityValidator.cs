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

    internal static Action<string, string>? BeforeCurrentSidelineForTests;
    internal static Action<string, string>? BeforeReplacementPromotionForTests;
    internal static Action<string>? BeforeFinalRetirementForTests;

    internal DataRootCapabilityValidator(ICapabilityFileOps? fileOps = null, IVerifiedArtifactDeleter? verifiedDeleter = null)
    {
        _fileOps = fileOps ?? new DefaultCapabilityFileOps();
        _ = verifiedDeleter; // retained only for source compatibility with older test constructors
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
        ProductionRuntimeEvidence.Hit("DataRootCapabilityValidator.ValidateWritable");
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        if (!_fileOps.DirectoryExists(root))
        {
            Directory.CreateDirectory(root);
            if (journal is not null)
            {
                string parent = Path.GetDirectoryName(root)
                    ?? throw new InvalidOperationException($"Created data root has no parent: '{root}'.");
                using WindowsRetirableDirectory createdRoot =
                    WindowsRetirableDirectory.OpenExistingOrNull(root, parent)
                    ?? throw new IOException($"Created data root disappeared before it could be claimed: '{root}'.");
                journal.TrackCreatedDirectory(root, createdRoot.Identity.ToToken());
            }
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
        ProbeOwnedTransaction(
            root,
            Path.Combine(root, plan.CurrentRelativePath),
            Path.Combine(root, plan.ReplacementRelativePath),
            Path.Combine(root, plan.DisplacedRelativePath),
            journal,
            recordDurableOwnership: true);
    }

    private void ProbeLocation(string directory, ICreatedPathJournal? journal)
    {
        string nonce = Guid.NewGuid().ToString("N");
        ProbeOwnedTransaction(
            directory,
            Path.Combine(directory, $".prompthelper-capability-{nonce}-current.tmp"),
            Path.Combine(directory, $".prompthelper-capability-{nonce}-replacement.tmp"),
            Path.Combine(directory, $".prompthelper-capability-{nonce}-displaced.tmp"),
            journal,
            recordDurableOwnership: false);
    }

    private void ProbeOwnedTransaction(
        string physicalRoot,
        string currentFile,
        string replacementFile,
        string displacedFile,
        ICreatedPathJournal? journal,
        bool recordDurableOwnership)
    {
        byte[] currentBytes = Encoding.UTF8.GetBytes("create");
        byte[] replacementBytes = Encoding.UTF8.GetBytes("replace");
        IOwnedCapabilityProbe? current = null;
        IOwnedCapabilityProbe? replacement = null;
        bool currentRetired = false;
        bool replacementRetired = false;

        try
        {
            current = _fileOps.CreateOwnedProbe(
                physicalRoot,
                currentFile,
                currentBytes,
                recordDurableOwnership);
            journal?.TrackCreatedFile(currentFile);
            current.Write(currentBytes);
            current.FlushDurable();

            replacement = _fileOps.CreateOwnedProbe(
                physicalRoot,
                replacementFile,
                replacementBytes,
                recordDurableOwnership);
            journal?.TrackCreatedFile(replacementFile);
            replacement.Write(replacementBytes);
            replacement.FlushDurable();

            BeforeCurrentSidelineForTests?.Invoke(currentFile, displacedFile);
            current.RenameNoOverwriteRetainingOwnership(displacedFile);

            BeforeReplacementPromotionForTests?.Invoke(replacementFile, currentFile);
            replacement.RenameNoOverwriteRetainingOwnership(currentFile);

            BeforeFinalRetirementForTests?.Invoke(currentFile);
            current.DeleteExact();
            currentRetired = true;
            replacement.DeleteExact();
            replacementRetired = true;

            if (recordDurableOwnership)
            {
                _fileOps.RetireSettledOwnership(physicalRoot);
            }
        }
        catch (Exception ex)
        {
            var cleanupFailures = new List<MigrationRollbackFailure>();
            DeleteOwnedProbe(current, currentRetired, currentFile, "DeleteProbeCurrentFile", cleanupFailures);
            DeleteOwnedProbe(replacement, replacementRetired, replacementFile, "DeleteProbeReplacementFile", cleanupFailures);

            if (recordDurableOwnership && cleanupFailures.Count == 0)
            {
                try
                {
                    _fileOps.RetireSettledOwnership(physicalRoot);
                }
                catch (Exception reconcileEx)
                {
                    cleanupFailures.Add(new MigrationRollbackFailure(
                        physicalRoot,
                        "RetireProbeOwnership",
                        reconcileEx.Message));
                }
            }

            if (cleanupFailures.Count > 0)
            {
                throw new DataRootCapabilityProbeException(physicalRoot, ex, cleanupFailures);
            }

            throw;
        }
        finally
        {
            replacement?.Dispose();
            current?.Dispose();
        }
    }

    private static void DeleteOwnedProbe(
        IOwnedCapabilityProbe? probe,
        bool alreadyRetired,
        string path,
        string operation,
        List<MigrationRollbackFailure> failures)
    {
        if (probe is null || alreadyRetired)
        {
            return;
        }

        try
        {
            probe.DeleteExact();
        }
        catch (Exception deleteEx)
        {
            failures.Add(new MigrationRollbackFailure(path, operation, deleteEx.Message));
        }
    }
}
