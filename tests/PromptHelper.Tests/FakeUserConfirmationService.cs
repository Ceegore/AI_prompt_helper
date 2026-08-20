using System;
using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class FakeUserConfirmationService : IUserConfirmationService
{
    public bool ConfirmationResult { get; set; } = true;
    public Action? OnConfirm { get; set; }
    public string? LastPromptedPath { get; private set; }
    public string? LastWarning { get; private set; }
    public string? LastMessage { get; private set; }
    public string? LastTitle { get; private set; }
    public int PromptCount { get; private set; }
    public int InfoCount { get; private set; }
    public int WarningCount { get; private set; }

    public bool Confirm(string message, string title)
    {
        LastMessage = message;
        LastTitle = title;
        PromptCount++;
        OnConfirm?.Invoke();
        return ConfirmationResult;
    }

    public bool ConfirmExistingLibrarySwitch(string targetPath, string? warning)
    {
        LastPromptedPath = targetPath;
        LastWarning = warning;
        PromptCount++;
        OnConfirm?.Invoke();
        return ConfirmationResult;
    }

    public void ShowInformation(string message, string title)
    {
        LastMessage = message;
        LastTitle = title;
        InfoCount++;
    }

    public void ShowWarning(string message, string title)
    {
        LastMessage = message;
        LastTitle = title;
        LastWarning = message;
        WarningCount++;
    }
}
