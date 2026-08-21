using System;
using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class FakeDurableSettingsFileWriter : IDurableSettingsFileWriter
{
    private readonly IDurableSettingsFileWriter _inner = new WindowsDurableSettingsFileWriter();

    public Action<string, string>? OnWriteDurable { get; set; }

    public void WriteDurable(string targetPath, string content)
    {
        OnWriteDurable?.Invoke(targetPath, content);
        _inner.WriteDurable(targetPath, content);
    }
}
