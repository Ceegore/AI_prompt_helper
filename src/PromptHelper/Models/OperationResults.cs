namespace PromptHelper.Models;

public sealed record DefaultLibraryPackage(
    LibraryDocument Document,
    IReadOnlyDictionary<Guid, string> PromptContents);

public sealed record CommitResult(
    bool BackupSynchronized,
    string? Warning);

public sealed record StartupResult(
    LibraryDocument Document,
    bool RecoveredFromBackup,
    string? Warning);

public sealed record OperationResult(
    string? Warning = null);

public sealed record OperationResult<T>(
    T Value,
    string? Warning = null);

public sealed record PromptDisplayRecord(
    Guid Id,
    string? Title,
    string Content,
    bool IsContentAvailable,
    string? LoadError);

public sealed record DataFolderChangeResult(
    string NormalizedTargetRoot,
    bool ExistingLibraryFound,
    bool Copied,
    string? Warning = null);

public sealed record DestinationRecord(
    Guid? CategoryId,
    string DisplayPath);

public sealed record BreadcrumbRecord(
    Guid? CategoryId,
    string Name);

public sealed record SettingsLoadResult(
    AppSettings Settings,
    bool RecoveredFromBackup,
    string? Warning);

public sealed record SettingsSaveResult(
    string? Warning);