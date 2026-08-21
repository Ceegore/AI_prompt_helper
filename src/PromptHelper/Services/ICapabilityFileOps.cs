using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PromptHelper.Services;

internal interface ICapabilityFileOps
{
    Stream CreateNew(string path);
    void FlushToDisk(Stream stream);
    void Replace(string sourceFileName, string destinationFileName, string? destinationBackupFileName);
    bool FileExists(string path);
    bool DirectoryExists(string path);
    IReadOnlyList<string> EnumerateEntries(string path);
    IReadOnlyList<string> EnumerateFiles(string path, string searchPattern);
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

    private readonly StrictPathAuthority _authority = new();

    public bool FileExists(string path) => _authority.Probe(path).Kind == StrictPathKind.File;
    public bool DirectoryExists(string path) => _authority.Probe(path).Kind == StrictPathKind.Directory;

    public IReadOnlyList<string> EnumerateEntries(string path)
    {
        if (_authority.Probe(path).Kind != StrictPathKind.Directory)
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

    public IReadOnlyList<string> EnumerateFiles(string path, string searchPattern)
    {
        if (_authority.Probe(path).Kind != StrictPathKind.Directory)
        {
            return [];
        }

        return Directory.EnumerateFiles(path, searchPattern).ToList();
    }

    public void DeleteFile(string path)
    {
        if (_authority.Probe(path).Kind == StrictPathKind.File)
        {
            File.Delete(path);
        }
    }

    public void DeleteDirectory(string path)
    {
        if (_authority.Probe(path).Kind == StrictPathKind.Directory)
        {
            Directory.Delete(path);
        }
    }
}
