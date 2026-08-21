namespace PromptHelper.Services;

public interface IDataFolderTransitionService
{
    DataFolderTransitionResult RequestTransition(string candidateRoot);
}
