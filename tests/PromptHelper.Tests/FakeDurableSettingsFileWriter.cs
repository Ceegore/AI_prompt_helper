using System;
using System.Text;
using PromptHelper.Services;

namespace PromptHelper.Tests;

/// <summary>
/// Fails a settings write. Implements <see cref="IAtomicExpectedFileReplacer"/> as well,
/// because expectation-bound settings commits go through the atomic compare-and-swap rather
/// than a bare durable writer (CRUU15-004) — this keeps a single injection point, and forwards
/// to the real primitive when no fault is injected so the semantics under test stay the
/// production ones.
/// </summary>
internal sealed class FakeDurableSettingsFileWriter : IDurableSettingsFileWriter, IAtomicExpectedFileReplacer
{
    private readonly IDurableSettingsFileWriter _inner = new WindowsDurableSettingsFileWriter();
    private readonly IAtomicExpectedFileReplacer _innerReplacer = new WindowsAtomicExpectedFileReplacer();

    public Action<string, string>? OnWriteDurable { get; set; }

    public void WriteDurable(string targetPath, string content)
    {
        OnWriteDurable?.Invoke(targetPath, content);
        _inner.WriteDurable(targetPath, content);
    }

    public void ReplaceIfExpected(
        string physicalRoot,
        string targetPath,
        ExpectedFileState expected,
        ReadOnlySpan<byte> candidateBytes,
        DurableFileClass fileClass)
    {
        if (OnWriteDurable != null)
        {
            OnWriteDurable(targetPath, Encoding.UTF8.GetString(candidateBytes));
        }

        _innerReplacer.ReplaceIfExpected(physicalRoot, targetPath, expected, candidateBytes, fileClass);
    }
}
