using System.IO;
using System.Security;
using PromptHelper.Models;

namespace PromptHelper.Services;

public sealed class DataFolderMigrationService
{
    public DataFolderChangeResult PrepareTarget(string currentRoot, string selectedRoot)
    {
        if (string.IsNullOrWhiteSpace(selectedRoot))
        {
            throw new ArgumentException("Selected data folder path cannot be empty or whitespace.", nameof(selectedRoot));
        }

        string normalizedTarget = Path.GetFullPath(selectedRoot.Trim());
        string normalizedCurrent = Path.GetFullPath((currentRoot ?? string.Empty).Trim());

        string cleanTarget = normalizedTarget.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string cleanCurrent = normalizedCurrent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(cleanTarget, cleanCurrent, StringComparison.OrdinalIgnoreCase))
        {
            return new DataFolderChangeResult(normalizedTarget, ExistingLibraryFound: false, Copied: false);
        }

        if (File.Exists(normalizedTarget))
        {
            throw new ArgumentException($"Selected path is a file, not a directory: {normalizedTarget}", nameof(selectedRoot));
        }

        Directory.CreateDirectory(normalizedTarget);

        string targetLibraryPath = Path.Combine(normalizedTarget, "library.json");
        if (File.Exists(targetLibraryPath))
        {
            ValidateExistingTargetLibrary(normalizedTarget, targetLibraryPath);
            return new DataFolderChangeResult(normalizedTarget, ExistingLibraryFound: true, Copied: false);
        }

        CopyAndValidateLibrary(normalizedCurrent, normalizedTarget, targetLibraryPath);
        return new DataFolderChangeResult(normalizedTarget, ExistingLibraryFound: false, Copied: true);
    }

    private static void ValidateExistingTargetLibrary(string targetRoot, string targetLibraryPath)
    {
        string json = File.ReadAllText(targetLibraryPath);
        LibraryDocument document;
        try
        {
            document = LibraryRepository.InspectAndDeserialize(json);
            LibraryValidator.Validate(document);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or ArgumentException)
        {
            throw new InvalidDataException($"Target library at '{targetLibraryPath}' is invalid: {ex.Message}", ex);
        }

        string targetPromptsDir = Path.Combine(targetRoot, "prompts");
        foreach (var prompt in document.Prompts)
        {
            string promptPath = Path.Combine(targetPromptsDir, $"{prompt.Id:N}.md");
            if (!File.Exists(promptPath))
            {
                throw new InvalidDataException($"Target library references prompt file '{prompt.Id:N}.md' which does not exist in '{targetPromptsDir}'.");
            }
        }
    }

    private static void CopyAndValidateLibrary(string currentRoot, string targetRoot, string targetLibraryPath)
    {
        string targetPromptsDir = Path.Combine(targetRoot, "prompts");
        string targetRecoveryDir = Path.Combine(targetRoot, "recovery");

        Directory.CreateDirectory(targetPromptsDir);
        Directory.CreateDirectory(targetRecoveryDir);

        var createdFiles = new List<string>();

        try
        {
            string currentLibPath = Path.Combine(currentRoot, "library.json");
            if (File.Exists(currentLibPath))
            {
                CopyFileNoOverwrite(currentLibPath, targetLibraryPath, createdFiles);
            }

            string currentBackupPath = Path.Combine(currentRoot, "library.backup.json");
            if (File.Exists(currentBackupPath))
            {
                string targetBackupPath = Path.Combine(targetRoot, "library.backup.json");
                CopyFileNoOverwrite(currentBackupPath, targetBackupPath, createdFiles);
            }

            string currentPromptsDir = Path.Combine(currentRoot, "prompts");
            if (Directory.Exists(currentPromptsDir))
            {
                foreach (string promptFile in Directory.EnumerateFiles(currentPromptsDir, "*.md", SearchOption.TopDirectoryOnly))
                {
                    string fileName = Path.GetFileName(promptFile);
                    string destPromptPath = Path.Combine(targetPromptsDir, fileName);
                    CopyFileNoOverwrite(promptFile, destPromptPath, createdFiles);
                }
            }

            string currentRecoveryDir = Path.Combine(currentRoot, "recovery");
            if (Directory.Exists(currentRecoveryDir))
            {
                foreach (string recoveryFile in Directory.EnumerateFiles(currentRecoveryDir, "*", SearchOption.TopDirectoryOnly))
                {
                    string fileName = Path.GetFileName(recoveryFile);
                    string destRecoveryPath = Path.Combine(targetRecoveryDir, fileName);
                    CopyFileNoOverwrite(recoveryFile, destRecoveryPath, createdFiles);
                }
            }

            if (File.Exists(targetLibraryPath))
            {
                ValidateExistingTargetLibrary(targetRoot, targetLibraryPath);
            }
        }
        catch
        {
            foreach (var createdFile in createdFiles)
            {
                try
                {
                    if (File.Exists(createdFile))
                    {
                        File.Delete(createdFile);
                    }
                }
                catch
                {
                    // Best effort cleanup of files created during failed migration
                }
            }

            throw;
        }
    }

    private static void CopyFileNoOverwrite(string sourcePath, string destPath, List<string> createdFiles)
    {
        if (File.Exists(destPath))
        {
            throw new IOException($"Target file collision: '{destPath}' already exists.");
        }

        File.Copy(sourcePath, destPath, overwrite: false);
        createdFiles.Add(destPath);
    }
}
