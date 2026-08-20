using System.IO;

namespace PromptHelper.Services;

public sealed class FileDeleter : IFileDeleter
{
    public void DeleteIfExists(string path)
    {
        File.Delete(path);
    }
}