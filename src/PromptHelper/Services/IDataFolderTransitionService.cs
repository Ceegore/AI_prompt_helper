namespace PromptHelper.Services;

public interface IDataFolderTransitionService
{
    DataFolderTransitionResult RequestTransition(string candidateRoot);

    DataFolderTransitionResult RequestTransition(string candidateRoot, bool useDarkMode) =>
        RequestTransition(candidateRoot);
}
