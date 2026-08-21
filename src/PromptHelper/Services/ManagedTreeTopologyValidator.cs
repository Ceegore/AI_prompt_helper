using System;
using System.IO;

namespace PromptHelper.Services;

internal enum ManagedTreeValidationMode
{
    PreCreation,
    RuntimeRequired
}

internal sealed class ManagedTreeTopologyValidator
{
    private readonly IPhysicalPathResolver _resolver;
    private readonly StrictPathAuthority _paths;
    private readonly IDirectoryCaseSensitivityInspector _caseInspector;

    public ManagedTreeTopologyValidator(
        IPhysicalPathResolver? resolver = null,
        StrictPathAuthority? paths = null,
        IDirectoryCaseSensitivityInspector? caseInspector = null)
    {
        _resolver = resolver ?? new WindowsPhysicalPathResolver();
        _paths = paths ?? new StrictPathAuthority();
        _caseInspector = caseInspector ?? new WindowsDirectoryCaseSensitivityInspector();
    }

    public void ValidateManagedTree(
        string physicalRoot,
        ManagedTreeValidationMode mode = ManagedTreeValidationMode.RuntimeRequired)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);

        string root = PathIdentity.NormalizeForComparison(physicalRoot);
        ValidateChild(root, "prompts", ManagedTreeValidationMode.PreCreation);
        ValidateChild(root, "recovery", ManagedTreeValidationMode.PreCreation);

        if (mode == ManagedTreeValidationMode.RuntimeRequired)
        {
            ValidateChild(root, "prompts", ManagedTreeValidationMode.RuntimeRequired);
            ValidateChild(root, "recovery", ManagedTreeValidationMode.RuntimeRequired);
        }
    }

    public void ValidateManagedDirectory(
        string physicalRoot,
        string childName,
        ManagedTreeValidationMode mode = ManagedTreeValidationMode.RuntimeRequired)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(childName);

        string root = PathIdentity.NormalizeForComparison(physicalRoot);
        ValidateChild(root, childName, mode);
    }

    private void ValidateChild(
        string physicalRoot,
        string childName,
        ManagedTreeValidationMode mode)
    {
        string child = Path.Combine(physicalRoot, childName);

        StrictPathProbe probe = _paths.Probe(child);

        switch (probe.Kind)
        {
            case StrictPathKind.Missing:
                if (mode == ManagedTreeValidationMode.PreCreation)
                {
                    return;
                }

                throw new DirectoryNotFoundException(
                    $"Required managed directory is missing: '{child}'.");

            case StrictPathKind.File:
                throw new InvalidDataException(
                    $"Managed path must be a directory: '{child}'.");

            case StrictPathKind.Directory:
                break;

            default:
                throw new InvalidOperationException(
                    $"Unexpected path state: {probe.Kind}.");
        }

        if ((probe.Attributes!.Value & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Managed directory must not be a reparse point: '{child}'.");
        }

        string physicalChild = _resolver.ResolveWithNearestExistingAncestor(child);

        if (!PathIdentity.Equals(physicalChild, child))
        {
            throw new InvalidDataException(
                $"Managed directory resolves to unexpected physical path. " +
                $"Expected='{child}', Actual='{physicalChild}'.");
        }

        if (_caseInspector.Inspect(child) == DirectoryCaseSensitivityState.CaseSensitive)
        {
            throw new InvalidOperationException(
                $"Managed directory is case-sensitive: '{child}'.");
        }
    }
}
