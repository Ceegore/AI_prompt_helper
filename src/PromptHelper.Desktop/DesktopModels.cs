using PromptHelper.Models;

namespace PromptHelper.Desktop;

public sealed record CategoryDisplay(Guid Id, string Name);

public sealed record BreadcrumbDisplay(Guid? CategoryId, string Name);

public sealed record PromptDisplay(
    Guid Id,
    string Title,
    string Preview,
    string? CustomTitle);

public sealed record RecentPromptDisplay(
    Guid Id,
    string Title,
    string Preview);

public sealed record DestinationDisplay(
    Guid? CategoryId,
    string DisplayPath)
{
    public override string ToString() => DisplayPath;

    public static DestinationDisplay From(DestinationRecord source) =>
        new(source.CategoryId, source.DisplayPath);
}

public sealed record PromptDialogResult(
    string Content,
    string? Title);

public sealed record MoveDialogResult(
    Guid? CategoryId);

public sealed record SettingsDialogResult(
    string? DataRootPath,
    bool UseDarkMode);
