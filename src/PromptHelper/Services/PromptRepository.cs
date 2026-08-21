using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PromptHelper.Infrastructure;

namespace PromptHelper.Services;

public sealed class PromptRepository
{
    private readonly AppPaths _paths;
    private readonly IDurableAtomicFileWriter _durableWriter;
    private readonly IFileDeleter _deleter;

    internal PromptRepository(
        AppPaths paths,
        IDurableAtomicFileWriter durableWriter,
        IFileDeleter deleter)
    {
        _paths = paths;
        _durableWriter = durableWriter;
        _deleter = deleter;
    }

    public PromptRepository(
        AppPaths paths,
        IAtomicTextWriter writer,
        IFileDeleter deleter) : this(paths, new AtomicTextWriterDurableAdapter(writer), deleter)
    {
    }

    internal IFileDeleter Deleter => _deleter;
    internal IDurableAtomicFileWriter DurableWriter => _durableWriter;
    internal AppPaths Paths => _paths;

    public bool Exists(Guid id)
    {
        string path = _paths.GetPromptPath(id);
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    public string Read(Guid id)
    {
        string path = _paths.GetPromptPath(id);
        try
        {
            return StrictUtf8Text.ReadAllText(path, $"prompt body '{id}'");
        }
        catch (DirectoryNotFoundException)
        {
            throw new FileNotFoundException("Prompt file does not exist.", path);
        }
    }

    public byte[] ReadBytesStrict(Guid id)
    {
        string path = _paths.GetPromptPath(id);
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (DirectoryNotFoundException)
        {
            throw new FileNotFoundException("Prompt file does not exist.", path);
        }
    }

    public void Create(Guid id, string content)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Prompt ID cannot be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(content);

        string path = _paths.GetPromptPath(id);
        byte[] bytes = StrictUtf8Text.Encode(content);

        _durableWriter.CreateNewDurable(
            path,
            bytes,
            DurableFileClass.PromptBody);
    }

    public void Update(Guid id, string content)
    {
        string path = _paths.GetPromptPath(id);

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (FileNotFoundException)
        {
            throw new FileNotFoundException("Prompt file does not exist.", path);
        }
        catch (DirectoryNotFoundException)
        {
            throw new FileNotFoundException("Prompt file does not exist.", path);
        }

        byte[] bytes = StrictUtf8Text.Encode(content);
        _durableWriter.ReplaceDurable(path, bytes, DurableFileClass.PromptBody);
    }

    public void DeleteIfExists(Guid id)
    {
        _deleter.DeleteIfExists(_paths.GetPromptPath(id));
    }

    public IReadOnlyList<string> EnumeratePromptFiles()
    {
        return EnumeratePromptFilesStrict();
    }

    public IReadOnlyList<string> EnumeratePromptFilesStrict()
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