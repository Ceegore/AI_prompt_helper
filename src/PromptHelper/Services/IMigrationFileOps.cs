using System.Collections.Generic;
using System.IO;

namespace PromptHelper.Services;

internal interface IMigrationFileOps
{
    byte[] ReadAllBytes(string path);
    Stream CreateNewFile(string path);
    Stream OpenRead(string path);
    void MoveNoOverwrite(string source, string destination);
    IEnumerable<string> EnumeratePromptFiles(string directory);
}

internal sealed class DefaultMigrationFileOps : IMigrationFileOps
{
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public Stream CreateNewFile(string path)
    {
        return new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);
    }

    public Stream OpenRead(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
    }

    public void MoveNoOverwrite(string source, string destination)
    {
        File.Move(source, destination, overwrite: false);
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
