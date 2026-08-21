using System;
using System.IO;

namespace PromptHelper.Services;

internal sealed class ManagedTreeTopologyValidator
{
    private readonly IPhysicalPathResolver _resolver;
    private readonly StrictPathAuthority _strictPathAuthority = new();

    public ManagedTreeTopologyValidator(IPhysicalPathResolver? resolver = null)
    {
        _resolver = resolver ?? new WindowsPhysicalPathResolver();
    }

    public void ValidateManagedTree(string physicalRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);

        string root = PathIdentity.NormalizeForComparison(physicalRoot);
        ValidateManagedDirectory(root, "prompts");
        ValidateManagedDirectory(root, "recovery");
    }

    public void ValidateManagedDirectory(string physicalRoot, string childName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(childName);

        string child = Path.Combine(physicalRoot, childName);
        StrictPathProbe probe = _strictPathAuthority.Probe(child);
        
        if (probe.Kind != StrictPathKind.Directory)
        {
            return;
        }

        if (probe.Attributes.HasValue && (probe.Attributes.Value & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Prompt Helper managed directory '{child}' is a reparse point. " +
                "Managed data directories may not be junctions or symbolic links.");
        }

        string actual = DataRootTopologyValidator.ResolvePhysicalOrThrow(
            _resolver,
            child,
            $"managed '{childName}' directory");

        string expected = PathIdentity.NormalizeForComparison(
            Path.Combine(physicalRoot, childName));

        if (!PathIdentity.Equals(actual, expected))
        {
            throw new InvalidDataException(
                $"Managed directory '{child}' resolves outside its expected " +
                $"physical location. Expected '{expected}', resolved '{actual}'.");
        }
    }
}
