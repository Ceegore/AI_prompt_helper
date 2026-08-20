using System;

namespace PromptHelper.Services;

public sealed class ConfiguredDataFolderUnavailableException : Exception
{
    public ConfiguredDataFolderUnavailableException(string path, string reason)
        : base($"{reason} Configured data folder: {path}")
    {
        DataFolderPath = path;
    }

    public string DataFolderPath { get; }
}
