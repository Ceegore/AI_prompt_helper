using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

    public PromptRepository(AppPaths paths)
        : this(paths, new WindowsDurableAtomicFileWriter(), new FileDeleter())
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

    public async Task<string> ReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        string path = _paths.GetPromptPath(id);
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            return StrictUtf8Text.Decode(bytes, $"prompt body '{id}'");
        }
        catch (DirectoryNotFoundException)
        {
            throw new FileNotFoundException("Prompt file does not exist.", path);
        }
    }

    public async Task<(string Text, bool IsTruncated, long FileSizeBytes)> ReadPreviewAsync(
        Guid id,
        int maxCharacters,
        CancellationToken cancellationToken = default)
    {
        if (maxCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        }

        string path = _paths.GetPromptPath(id);
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            long fileSizeBytes = stream.Length;
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: false);

            char[] buffer = new char[maxCharacters + 1];
            int read = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
            bool truncated = read > maxCharacters;
            int length = Math.Min(read, maxCharacters);
            int start = length > 0 && buffer[0] == '\uFEFF' ? 1 : 0;

            return (new string(buffer, start, length - start), truncated, fileSizeBytes);
        }
        catch (DirectoryNotFoundException)
        {
            throw new FileNotFoundException("Prompt file does not exist.", path);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException($"Invalid UTF-8 in prompt body '{id}'.", ex);
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
