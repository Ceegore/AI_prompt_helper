using System;
using System.IO;

namespace PromptHelper.Services;

public static class DataRootBootstrapValidator
{
    private static readonly StrictPathAuthority _strictPathAuthority = new();

    public static void ValidateConfiguredRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        StrictPathProbe rootProbe = _strictPathAuthority.Probe(root);
        if (rootProbe.Kind != StrictPathKind.Directory)
        {
            throw new ConfiguredDataFolderUnavailableException(
                root,
                "The configured data folder does not exist.");
        }

        string primary = Path.Combine(root, "library.json");
        string backup = Path.Combine(root, "library.backup.json");

        if (_strictPathAuthority.Probe(primary).Kind != StrictPathKind.File &&
            _strictPathAuthority.Probe(backup).Kind != StrictPathKind.File)
        {
            throw new ConfiguredDataFolderUnavailableException(
                root,
                "The configured data folder does not contain library.json or library.backup.json.");
        }
    }
}
