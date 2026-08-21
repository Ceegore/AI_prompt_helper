using System.Collections.Generic;
using System.IO;

namespace PromptHelper.Services;

internal interface ICapabilityFileOps
{
    Stream CreateNew(string path);
    void FlushToDisk(Stream stream);
    void Replace(string sourceFileName, string destinationFileName, string? destinationBackupFileName);
    bool FileExists(string path);
    bool DirectoryExists(string path);
    IReadOnlyList<string> EnumerateEntries(string path);
    void DeleteFile(string path);
    void DeleteDirectory(string path);
}

internal sealed class DefaultCapabilityFileOps : ICapabilityFileOps
{
    public Stream CreateNew(string path)
    {
        return new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);
    }

    public void FlushToDisk(Stream stream)
    {
        if (stream is FileStream fs)
        {
            fs.Flush(flushToDisk: true);
        }
        else
        {
            stream.Flush();
        }
    }

    public void Replace(string sourceFileName, string destinationFileName, string? destinationBackupFileName)
    {
        File.Replace(sourceFileName, destinationFileName, destinationBackupFileName);
    }

    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IReadOnlyList<string> EnumerateEntries(string path)
    {
        if (!Directory.Exists(path))
        {
            return [];
        }

        var list = new List<string>();
        foreach (string entry in Directory.EnumerateFileSystemEntries(path))
        {
            list.Add(entry);
        }
        return list;
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path);
        }
    }
}
