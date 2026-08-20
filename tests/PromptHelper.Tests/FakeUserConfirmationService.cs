using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class FakeUserConfirmationService : IUserConfirmationService
{
    public bool ConfirmationResult { get; set; } = true;
    public string? LastPromptedPath { get; private set; }
    public string? LastWarning { get; private set; }
    public int PromptCount { get; private set; }

    public bool ConfirmExistingLibrarySwitch(string targetPath, string? warning)
    {
        LastPromptedPath = targetPath;
        LastWarning = warning;
        PromptCount++;
        return ConfirmationResult;
    }
}
