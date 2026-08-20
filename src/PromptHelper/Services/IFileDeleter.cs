namespace PromptHelper.Services;

public interface IFileDeleter
{
    void DeleteIfExists(string path);
}