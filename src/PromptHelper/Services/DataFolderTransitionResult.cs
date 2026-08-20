namespace PromptHelper.Services;

public sealed record DataFolderTransitionResult(
    bool Changed,
    bool RestartRequired,
    bool ExistingLibrarySelected,
    string NormalizedTargetRoot,
    string? Warning);
