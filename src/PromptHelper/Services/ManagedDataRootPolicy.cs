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

        string physicalTarget;
        string physicalBootstrap;

        try
        {
            physicalTarget = DataRootTopologyValidator.ResolvePhysicalOrThrow(_resolver, lexical, "configured data folder");
            physicalBootstrap = DataRootTopologyValidator.ResolvePhysicalOrThrow(_resolver, Path.GetFullPath(bootstrapRoot), "bootstrap settings folder");
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }

        if (DataRootTopologyValidator.IsVolumeRootSafe(physicalTarget))
        {
            throw new InvalidDataException(
                "The configured data folder resolves to a drive or share root.");
        }

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

    public DataRootRelationship ValidateTransition(
        string currentRoot,
        string targetRoot,
        string? bootstrapRoot = null)
    {
        string bootstrap = bootstrapRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PromptHelper");

        return DataRootTopologyValidator.ValidateTransition(
            currentRoot,
            targetRoot,
            bootstrap,
            _resolver);
    }

    public void ValidateDisjointOrSame(
        string currentRoot,
        string targetRoot,
        string? defaultBootstrapRoot = null)
    {
        ValidateTransition(currentRoot, targetRoot, defaultBootstrapRoot);
    }
}
