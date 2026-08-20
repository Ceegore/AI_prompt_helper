using System.Collections.Generic;
using System.IO;

namespace PromptHelper.Services;

internal interface IMigrationFileOps
{
    byte[] ReadAllBytes(string path);
    void CopyFile(string source, string destination, bool overwrite);
    IEnumerable<string> EnumeratePromptFiles(string directory);
}

internal sealed class DefaultMigrationFileOps : IMigrationFileOps
{
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void CopyFile(string source, string destination, bool overwrite)
    {
        File.Copy(source, destination, overwrite);
    }

    public IEnumerable<string> EnumeratePromptFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.md");
    }
}
