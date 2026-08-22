using System.IO;
using PromptHelper.Services;

namespace PromptHelper.Tests;

/// <summary>
/// Fails a chosen durable write. It also implements <see cref="IAtomicExpectedFileReplacer"/>
/// so it remains the single injection point now that expectation-bound commits go through the
/// atomic compare-and-swap instead of a bare writer: when the fault does not fire, the call is
/// forwarded to the real <see cref="WindowsAtomicExpectedFileReplacer"/>, so the semantics
/// under test are the production ones and not a simplified stand-in.
/// </summary>
public sealed class FaultInjectingAtomicTextWriter
    : IAtomicTextWriter, IDurableSettingsFileWriter, IDurableAtomicFileWriter, IAtomicExpectedFileReplacer
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

    void IAtomicExpectedFileReplacer.ReplaceIfExpected(
        string physicalRoot,
        string targetPath,
        ExpectedFileState expected,
        ReadOnlySpan<byte> candidateBytes,
        DurableFileClass fileClass)
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

        new WindowsAtomicExpectedFileReplacer().ReplaceIfExpected(
            physicalRoot,
            targetPath,
            expected,
            candidateBytes,
            fileClass);
    }
}
