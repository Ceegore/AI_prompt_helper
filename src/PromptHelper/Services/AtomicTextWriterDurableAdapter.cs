using System;
using System.IO;

namespace PromptHelper.Services;

internal sealed class AtomicTextWriterDurableAdapter : IDurableAtomicFileWriter
{
    private readonly IAtomicTextWriter _writer;

    public AtomicTextWriterDurableAdapter(IAtomicTextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public void ReplaceDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        string text = StrictUtf8Text.Decode(bytes, targetPath);
        _writer.Write(targetPath, text);
    }

    public void CreateNewDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        if (File.Exists(targetPath))
        {
            throw new IOException($"Target file already exists: '{targetPath}'.");
        }

        string text = StrictUtf8Text.Decode(bytes, targetPath);
        _writer.Write(targetPath, text);
    }
}

internal sealed class FileDeleterVerifiedAdapter : IVerifiedArtifactDeleter
{
    private readonly IFileDeleter _deleter;

    public FileDeleterVerifiedAdapter(IFileDeleter deleter)
    {
        _deleter = deleter ?? throw new ArgumentNullException(nameof(deleter));
    }

    public void VerifyAndDelete(
        string physicalRoot,
        string filePath,
        long expectedLength,
        string expectedSha256Hex)
    {
        _deleter.DeleteIfExists(filePath);
    }
}

internal sealed class AtomicTextWriterSettingsDurableAdapter : IDurableSettingsFileWriter
{
    private readonly IAtomicTextWriter _writer;

    public AtomicTextWriterSettingsDurableAdapter(IAtomicTextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public void WriteDurable(string targetPath, string content)
    {
        _writer.Write(targetPath, content);
    }
}
