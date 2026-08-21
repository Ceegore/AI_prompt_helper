using System.IO;
using PromptHelper.Services;

namespace PromptHelper.Tests;

public sealed class FaultInjectingAtomicTextWriter : IAtomicTextWriter, IDurableSettingsFileWriter, IDurableAtomicFileWriter
{
    private readonly IAtomicTextWriter _inner;
    private int _callNumber;

    public FaultInjectingAtomicTextWriter(IAtomicTextWriter inner)
    {
        _inner = inner;
    }

    public Func<string, int, bool>? ShouldFail { get; set; }
    public Func<string, int, Exception?>? FailureFactory { get; set; }

    public int CallCount => _callNumber;

    public void Write(string targetPath, string content)
    {
        _callNumber++;

        if (FailureFactory != null)
        {
            var ex = FailureFactory(targetPath, _callNumber);
            if (ex != null)
            {
                throw ex;
            }
        }
        else if (ShouldFail?.Invoke(targetPath, _callNumber) == true)
        {
            throw new IOException("Injected write failure.");
        }

        _inner.Write(targetPath, content);
    }

    void IDurableSettingsFileWriter.WriteDurable(string targetPath, string content)
    {
        Write(targetPath, content);
    }

    void IDurableAtomicFileWriter.ReplaceDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        string text = StrictUtf8Text.Decode(bytes, targetPath);
        Write(targetPath, text);
    }

    void IDurableAtomicFileWriter.CreateNewDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        if (File.Exists(targetPath))
        {
            throw new IOException($"Target file already exists: '{targetPath}'.");
        }

        string text = StrictUtf8Text.Decode(bytes, targetPath);
        Write(targetPath, text);
    }
}