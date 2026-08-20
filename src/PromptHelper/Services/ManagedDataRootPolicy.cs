using System;
using System.IO;

namespace PromptHelper.Services;

public sealed class ManagedDataRootPolicy
{
    private readonly IPhysicalPathResolver _resolver;

    public ManagedDataRootPolicy(IPhysicalPathResolver? resolver = null)
    {
        _resolver = resolver ?? new WindowsPhysicalPathResolver();
    }

    public string ValidateConfiguredRootForStartup(
        string configuredRoot,
        string bootstrapRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapRoot);

        string lexical = Path.GetFullPath(configuredRoot);

        if (DataRootTopologyValidator.IsVolumeRootSafe(lexical))
        {
            throw new InvalidDataException(
                "A drive or volume root cannot be used as the Prompt Helper data folder.");
        }

        string physicalTarget =
            _resolver.ResolveWithNearestExistingAncestor(lexical);

        string physicalBootstrap =
            _resolver.ResolveWithNearestExistingAncestor(
                Path.GetFullPath(bootstrapRoot));

        if (!PathIdentity.Equals(physicalTarget, physicalBootstrap) &&
            (PathIdentity.IsStrictDescendant(
                 physicalTarget,
                 physicalBootstrap) ||
             PathIdentity.IsStrictDescendant(
                 physicalBootstrap,
                 physicalTarget)))
        {
            throw new InvalidDataException(
                "The configured data folder overlaps the Prompt Helper bootstrap settings folder.");
        }

        return physicalTarget;
    }

    public void ValidateDisjointOrSame(
        string currentRoot,
        string targetRoot,
        string? defaultBootstrapRoot = null)
    {
        DataRootTopologyValidator.ValidateDisjointOrSame(
            currentRoot,
            targetRoot,
            defaultBootstrapRoot,
            _resolver);
    }
}
