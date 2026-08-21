using System;
using System.ComponentModel;
using System.IO;

namespace PromptHelper.Services;

public sealed class ManagedDataRootPolicy
{
    private readonly IPhysicalPathResolver _resolver;
    private readonly IDirectoryCaseSensitivityInspector _caseInspector;

    public ManagedDataRootPolicy(
        IPhysicalPathResolver? resolver = null,
        IDirectoryCaseSensitivityInspector? caseInspector = null)
    {
        _resolver = resolver ?? new WindowsPhysicalPathResolver();
        _caseInspector = caseInspector ?? new WindowsDirectoryCaseSensitivityInspector();
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

        string nearestDir = DataRootTopologyValidator.FindNearestExistingDirectory(lexical);
        if (_caseInspector.IsCaseSensitive(nearestDir))
        {
            throw new InvalidDataException(
                $"Case-sensitive directory '{nearestDir}' cannot be used as a Prompt Helper data folder.");
        }

        string physicalTarget;
        string physicalBootstrap;

        try
        {
            physicalTarget = DataRootTopologyValidator.ResolvePhysicalOrThrow(_resolver, lexical, "configured data folder");
        }
        catch (InvalidOperationException ex)
        {
            if (ex.InnerException is DirectoryNotFoundException or DriveNotFoundException ||
                (ex.InnerException is Win32Exception w32 &&
                 (w32.NativeErrorCode == 2 || w32.NativeErrorCode == 3 || w32.NativeErrorCode == 53 || w32.NativeErrorCode == 67)))
            {
                throw new ConfiguredDataFolderUnavailableException(
                    lexical,
                    $"The configured data folder could not be found or resolved: {ex.Message}");
            }

            throw new InvalidDataException(ex.Message, ex);
        }

        string nearestPhysicalDir = DataRootTopologyValidator.FindNearestExistingDirectory(physicalTarget);
        if (_caseInspector.IsCaseSensitive(nearestPhysicalDir))
        {
            throw new InvalidDataException(
                $"Case-sensitive directory '{nearestPhysicalDir}' cannot be used as a Prompt Helper data folder.");
        }

        try
        {
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
            _resolver,
            _caseInspector);
    }

    public void ValidateDisjointOrSame(
        string currentRoot,
        string targetRoot,
        string? defaultBootstrapRoot = null)
    {
        ValidateTransition(currentRoot, targetRoot, defaultBootstrapRoot);
    }
}
