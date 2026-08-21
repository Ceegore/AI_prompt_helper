using System;
using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class FakeDataFolderTransitionService : IDataFolderTransitionService
{
    public Func<string, DataFolderTransitionResult>? OnRequestTransition { get; set; }

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
}
