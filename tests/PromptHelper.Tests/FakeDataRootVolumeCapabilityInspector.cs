using System;
using System.Collections.Generic;
using PromptHelper.Services;

namespace PromptHelper.Tests;

/// <summary>
/// Reports whatever volume capabilities a test asks for, so a test about something else does
/// not silently depend on the filesystem of whatever the developer has mounted. Directories
/// are case-insensitive-capable and identity-capable unless marked otherwise.
/// </summary>
internal sealed class FakeDataRootVolumeCapabilityInspector : IDataRootVolumeCapabilityInspector
{
    private readonly Dictionary<string, string> _withoutFileIdentity = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failing = new(StringComparer.OrdinalIgnoreCase);

    public string DefaultFileSystemName { get; set; } = "NTFS";

    /// <summary>Marks a directory as sitting on a volume with no file-ID support (exFAT, FAT32).</summary>
    public void MarkWithoutFileIdentity(string directory, string fileSystemName = "exFAT")
    {
        _withoutFileIdentity[PathIdentity.NormalizeForComparison(directory)] = fileSystemName;
    }

    /// <summary>Marks a directory whose capabilities cannot be determined at all.</summary>
    public void MarkInspectionFailure(string directory)
    {
        _failing.Add(PathIdentity.NormalizeForComparison(directory));
    }

    public DataRootVolumeCapabilities Inspect(string existingDirectory)
    {
        string norm = PathIdentity.NormalizeForComparison(existingDirectory);

        if (_failing.Contains(norm))
        {
            throw new DataRootVolumeCapabilityInspectionException(existingDirectory, 5 /* ERROR_ACCESS_DENIED */);
        }

        if (_withoutFileIdentity.TryGetValue(norm, out string? fileSystemName))
        {
            return new DataRootVolumeCapabilities(fileSystemName, SupportsFileIdentity: false);
        }

        return new DataRootVolumeCapabilities(DefaultFileSystemName, SupportsFileIdentity: true);
    }
}
