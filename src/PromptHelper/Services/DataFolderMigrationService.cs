using System.IO;
using System.Security;
using System.Security.Cryptography;
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

        if (IsStrictDescendant(cleanTarget, cleanCurrent))
        {
            throw new InvalidOperationException("The target data folder cannot be inside the current data folder.");
        }

        if (File.Exists(normalizedTarget))
        {
            throw new ArgumentException($"Selected path is a file, not a directory: {normalizedTarget}", nameof(selectedRoot));
        }

        // Validate source library before modifying target
        var sourceDoc = ValidateLibraryRoot(normalizedCurrent, requirePrimaryLibrary: true);

        var createdDirs = new List<string>();
        if (!Directory.Exists(normalizedTarget))
        {
            Directory.CreateDirectory(normalizedTarget);
            createdDirs.Add(normalizedTarget);
        }

        string targetLibraryPath = Path.Combine(normalizedTarget, "library.json");
        if (File.Exists(targetLibraryPath))
        {
            ValidateExistingTargetLibrary(normalizedTarget, targetLibraryPath);
            return new DataFolderChangeResult(normalizedTarget, ExistingLibraryFound: true, Copied: false);
        }

        CopyAndValidateLibrary(normalizedCurrent, normalizedTarget, targetLibraryPath, createdDirs);
        return new DataFolderChangeResult(normalizedTarget, ExistingLibraryFound: false, Copied: true);
    }

    private static bool IsStrictDescendant(string candidate, string parent)
    {
        string parentWithSep = parent.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        string candidateFull = candidate.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        return candidateFull.StartsWith(
            parentWithSep,
            StringComparison.OrdinalIgnoreCase);
    }

    private static LibraryDocument ValidateLibraryRoot(string root, bool requirePrimaryLibrary)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Library directory does not exist: '{root}'");
        }

        string libraryPath = Path.Combine(root, "library.json");
        if (!File.Exists(libraryPath))
        {
            if (requirePrimaryLibrary)
            {
                throw new InvalidDataException($"Library directory does not contain library.json: '{root}'");
            }
            return new LibraryDocument();
        }

        string json = File.ReadAllText(libraryPath);
        LibraryDocument document;
        try
        {
            document = LibraryRepository.InspectAndDeserialize(json);
            LibraryValidator.Validate(document);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or ArgumentException)
        {
            throw new InvalidDataException($"Library metadata at '{libraryPath}' is invalid: {ex.Message}", ex);
        }

        string promptsDir = Path.Combine(root, "prompts");
        foreach (var prompt in document.Prompts)
        {
            string promptPath = Path.Combine(promptsDir, $"{prompt.Id:N}.md");
            if (!File.Exists(promptPath))
            {
                throw new InvalidDataException($"Library references prompt file '{prompt.Id:N}.md' which does not exist in '{promptsDir}'.");
            }

            try
            {
                using var stream = File.OpenRead(promptPath);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Prompt file '{promptPath}' cannot be read: {ex.Message}", ex);
            }
        }

        return document;
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

            try
            {
                using var stream = File.OpenRead(promptPath);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Target prompt file '{promptPath}' cannot be read: {ex.Message}", ex);
            }
        }
    }

    private static void CopyAndValidateLibrary(
        string currentRoot,
        string targetRoot,
        string targetLibraryPath,
        List<string> createdDirs)
    {
        string targetPromptsDir = Path.Combine(targetRoot, "prompts");
        string targetRecoveryDir = Path.Combine(targetRoot, "recovery");

        if (!Directory.Exists(targetPromptsDir))
        {
            Directory.CreateDirectory(targetPromptsDir);
            createdDirs.Add(targetPromptsDir);
        }

        if (!Directory.Exists(targetRecoveryDir))
        {
            Directory.CreateDirectory(targetRecoveryDir);
            createdDirs.Add(targetRecoveryDir);
        }

        string sourceLibraryPath = Path.Combine(currentRoot, "library.json");
        byte[] sourceLibraryBytes = File.ReadAllBytes(sourceLibraryPath);
        byte[] sourceHash = SHA256.HashData(sourceLibraryBytes);

        var createdFiles = new List<string>();

        try
        {
            CopyFileNoOverwrite(sourceLibraryPath, targetLibraryPath, createdFiles);

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

            // Verify source did not mutate concurrently during copy
            byte[] finalSourceHash = SHA256.HashData(File.ReadAllBytes(sourceLibraryPath));
            if (!sourceHash.AsSpan().SequenceEqual(finalSourceHash))
            {
                throw new IOException("Source library metadata changed during migration. Retry after it is stable.");
            }

            ValidateExistingTargetLibrary(targetRoot, targetLibraryPath);
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

            // Remove created directories deepest first if empty
            var orderedDirs = createdDirs.OrderByDescending(d => d.Length).ToList();
            foreach (var dir in orderedDirs)
            {
                try
                {
                    if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                    }
                }
                catch
                {
                    // Best effort directory cleanup
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
