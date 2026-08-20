using System.IO;
using PromptHelper.Services;

namespace PromptHelper.Tests;

public sealed class FaultInjectingAtomicTextWriter : IAtomicTextWriter
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
}