using System;
using System.IO;

namespace PromptHelper.Services;

public static class DataRootBootstrapValidator
{
    public static void ValidateConfiguredRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        if (!Directory.Exists(root))
        {
            throw new ConfiguredDataFolderUnavailableException(
                root,
                "The configured data folder does not exist.");
        }

        string primary = Path.Combine(root, "library.json");
        string backup = Path.Combine(root, "library.backup.json");

        if (!File.Exists(primary) && !File.Exists(backup))
        {
            throw new ConfiguredDataFolderUnavailableException(
                root,
                "The configured data folder does not contain library.json or library.backup.json.");
        }
    }
}
