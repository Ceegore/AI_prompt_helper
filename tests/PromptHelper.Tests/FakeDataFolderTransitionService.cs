using System;
using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class FakeDataFolderTransitionService : IDataFolderTransitionService
{
    public Func<string, DataFolderTransitionResult>? OnRequestTransition { get; set; }
    public Func<string, bool, DataFolderTransitionResult>? OnRequestTransitionWithTheme { get; set; }

    public DataFolderTransitionResult RequestTransition(string candidateRoot)
    {
        if (OnRequestTransition != null)
        {
            return OnRequestTransition(candidateRoot);
        }

        return new DataFolderTransitionResult(
            Changed: false,
            RestartRequired: false,
            ExistingLibrarySelected: false,
            NormalizedTargetRoot: candidateRoot,
            Warning: null);
    }

    public DataFolderTransitionResult RequestTransition(string candidateRoot, bool useDarkMode)
    {
        if (OnRequestTransitionWithTheme != null)
        {
            return OnRequestTransitionWithTheme(candidateRoot, useDarkMode);
        }

        return RequestTransition(candidateRoot);
    }
}
