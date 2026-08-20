using System;
using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class FakeClipboardService : IClipboardService
{
    public string? LastCopiedText { get; private set; }
    public Exception? Failure { get; set; }

    public void CopyText(string text)
    {
        if (Failure != null)
        {
            throw Failure;
        }

        LastCopiedText = text;
    }
}
