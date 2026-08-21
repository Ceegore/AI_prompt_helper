using System;

namespace PromptHelper.Services;

public sealed class SettingsReadException : Exception
{
    public SettingsReadException(string path, Exception innerException)
        : base($"Failed to read settings from '{path}'.", innerException)
    {
        Path = path;
    }

    public SettingsReadException(string path, string message, Exception innerException)
        : base(message, innerException)
    {
        Path = path;
    }

    public string Path { get; }
}
