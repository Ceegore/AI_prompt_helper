using System.IO;
using PromptHelper.Services;

namespace PromptHelper.Tests;

public sealed class FaultInjectingFileDeleter : IFileDeleter
{
    public bool Fail { get; set; }

    public void DeleteIfExists(string path)
    {
        if (Fail)
        {
            throw new IOException("Injected delete failure.");
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}