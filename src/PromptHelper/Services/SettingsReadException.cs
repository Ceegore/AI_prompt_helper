using System;

namespace PromptHelper.Services;

public sealed class SettingsReadException : Exception
{
    public SettingsReadException(string path, Exception innerException)
        : base($"Failed to read settings from '{path}'.", innerException)
    {
        Path = path;
    }

    public string Path { get; }
}
