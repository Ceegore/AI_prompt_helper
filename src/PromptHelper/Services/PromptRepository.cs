using System.IO;
using PromptHelper.Infrastructure;

namespace PromptHelper.Services;

public sealed class PromptRepository
{
    private readonly AppPaths _paths;
    private readonly IAtomicTextWriter _writer;
    private readonly IFileDeleter _deleter;

    public PromptRepository(
        AppPaths paths,
        IAtomicTextWriter writer,
        IFileDeleter deleter)
    {
        _paths = paths;
        _writer = writer;
        _deleter = deleter;
    }

    public bool Exists(Guid id) =>
        File.Exists(_paths.GetPromptPath(id));

    public string Read(Guid id) =>
        File.ReadAllText(_paths.GetPromptPath(id));

    public void Create(Guid id, string content)
    {
        string path = _paths.GetPromptPath(id);

        if (File.Exists(path))
        {
            throw new InvalidOperationException($"Prompt file already exists: {id}");
        }

        _writer.Write(path, content);
    }

    public void Update(Guid id, string content)
    {
        string path = _paths.GetPromptPath(id);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Prompt file does not exist.", path);
        }

        _writer.Write(path, content);
    }

    public void DeleteIfExists(Guid id)
    {
        _deleter.DeleteIfExists(_paths.GetPromptPath(id));
    }

    public IReadOnlyList<string> EnumeratePromptFiles()
    {
        try
        {
            return Directory.EnumerateFiles(
                _paths.PromptsDirectory,
                "*.md",
                SearchOption.TopDirectoryOnly).ToList();
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }
}