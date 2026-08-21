using System;
using System.IO;

namespace PromptHelper.Services;

public sealed class AppPaths
{
    public AppPaths(string? rootOverride = null)
    {
        RootDirectory = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PromptHelper");
    }

    public string RootDirectory { get; }

    public string LockPath => Path.Combine(RootDirectory, ".app.lock");

    public string InitializationMarkerPath => Path.Combine(RootDirectory, "initializing.marker");

    public string MigrationMarkerPath => Path.Combine(RootDirectory, ".prompthelper-migration.json");

    public string LibraryMutationJournalPath => Path.Combine(RootDirectory, ".prompthelper-library-mutation.json");

    public string LibraryPath => Path.Combine(RootDirectory, "library.json");

    public string LibraryBackupPath => Path.Combine(RootDirectory, "library.backup.json");

    public string PromptsDirectory => Path.Combine(RootDirectory, "prompts");

    public string RecoveryDirectory => Path.Combine(RootDirectory, "recovery");

    public string GetPromptPath(Guid id) => Path.Combine(PromptsDirectory, $"{id:N}.md");

    public string GetMutationRecoveryBodyPath(Guid operationId, Guid promptId) =>
        Path.Combine(RecoveryDirectory, $"mutation-{operationId:N}-old-{promptId:N}.md");

    public void EnsureRootDirectory()
    {
        Directory.CreateDirectory(RootDirectory);
    }

    public void EnsureDataDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(PromptsDirectory);
        Directory.CreateDirectory(RecoveryDirectory);
    }
}