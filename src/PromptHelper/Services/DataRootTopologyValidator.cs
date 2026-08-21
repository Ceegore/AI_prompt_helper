using System;
using System.ComponentModel;
using System.IO;

namespace PromptHelper.Services;

public static class DataRootTopologyValidator
{
    public static bool IsStrictDescendant(string candidate, string parent)
    {
        return PathIdentity.IsStrictDescendant(candidate, parent);
    }

    internal static string FindNearestExistingDirectoryStrict(string path, IStrictDirectoryOpener? opener = null)
    {
        var effectiveOpener = opener ?? new WindowsStrictDirectoryOpener();
        string current = Path.GetFullPath(path);

        while (true)
        {
            DirectoryOpenResult result = effectiveOpener.OpenDirectoryStrict(current);

            if (result.State == DirectoryOpenState.Opened)
            {
                result.Handle!.Dispose();
                return current;
            }

            string trimmed = current.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            string? parent = Path.GetDirectoryName(trimmed);

            if (string.IsNullOrEmpty(parent) || PathIdentity.Equals(parent, current))
            {
                throw new DirectoryNotFoundException(
                    $"No accessible existing directory ancestor exists for '{path}'.");
            }

            current = parent;
        }
    }

    public static string FindNearestExistingDirectory(string path)
    {
        return FindNearestExistingDirectoryStrict(path);
    }

    public static DataRootRelationship ValidateTransition(
        string currentRoot,
        string targetRoot,
        string bootstrapRoot,
        IPhysicalPathResolver? resolver = null,
        IDirectoryCaseSensitivityInspector? caseInspector = null)
    {
        string lexicalCurrent = PathIdentity.NormalizeForComparison(currentRoot);
        string lexicalTarget = PathIdentity.NormalizeForComparison(targetRoot);
        string lexicalBootstrap = PathIdentity.NormalizeForComparison(bootstrapRoot);

        if (IsVolumeRootSafe(lexicalTarget))
        {
            throw new InvalidOperationException(
                "A root volume or drive cannot be selected as a Prompt Helper data folder.");
        }

        var caseSensitivityInspector = caseInspector ?? new WindowsDirectoryCaseSensitivityInspector();
        string nearestTargetDir = FindNearestExistingDirectory(lexicalTarget);
        try
        {
            if (caseSensitivityInspector.Inspect(nearestTargetDir) == DirectoryCaseSensitivityState.CaseSensitive)
            {
                throw new InvalidOperationException(
                    $"Case-sensitive directory '{nearestTargetDir}' cannot be used as a Prompt Helper data folder. Case-sensitive directories are not supported.");
            }
        }
        catch (DirectoryCaseSensitivityInspectionException ex)
        {
            throw new InvalidOperationException(
                $"Failed to verify case-insensitivity for target directory '{nearestTargetDir}': {ex.Message}", ex);
        }

        var physicalResolver = resolver ?? new WindowsPhysicalPathResolver();
        string physicalCurrent = ResolvePhysicalOrThrow(physicalResolver, lexicalCurrent, "current data folder");
        string physicalTarget = ResolvePhysicalOrThrow(physicalResolver, lexicalTarget, "target data folder");
        string physicalBootstrap = ResolvePhysicalOrThrow(physicalResolver, lexicalBootstrap, "bootstrap settings folder");

        if (IsVolumeRootSafe(physicalTarget))
        {
            throw new InvalidOperationException(
                "The selected data folder resolves to a drive or share root. Choose a dedicated subdirectory instead.");
        }

        string nearestPhysicalTargetDir = FindNearestExistingDirectory(physicalTarget);
        try
        {
            if (caseSensitivityInspector.Inspect(nearestPhysicalTargetDir) == DirectoryCaseSensitivityState.CaseSensitive)
            {
                throw new InvalidOperationException(
                    $"Case-sensitive directory '{nearestPhysicalTargetDir}' cannot be used as a Prompt Helper data folder. Case-sensitive directories are not supported.");
            }
        }
        catch (DirectoryCaseSensitivityInspectionException ex)
        {
            throw new InvalidOperationException(
                $"Failed to verify case-insensitivity for physical target directory '{nearestPhysicalTargetDir}': {ex.Message}", ex);
        }

        bool same = PathIdentity.Equals(physicalCurrent, physicalTarget);
        if (same)
        {
            return new DataRootRelationship(
                lexicalCurrent,
                lexicalTarget,
                physicalCurrent,
                physicalTarget,
                SamePhysicalRoot: true);
        }

        if (PathIdentity.IsStrictDescendant(physicalTarget, physicalCurrent) ||
            PathIdentity.IsStrictDescendant(physicalCurrent, physicalTarget))
        {
            throw new InvalidOperationException(
                "The current and target data folders cannot contain one another.");
        }

        if (!PathIdentity.Equals(physicalTarget, physicalBootstrap))
        {
            if (PathIdentity.IsStrictDescendant(physicalTarget, physicalBootstrap) ||
                PathIdentity.IsStrictDescendant(physicalBootstrap, physicalTarget))
            {
                throw new InvalidOperationException(
                    "A custom data folder cannot be inside or contain the Prompt Helper bootstrap settings folder.");
            }
        }

        return new DataRootRelationship(
            lexicalCurrent,
            lexicalTarget,
            physicalCurrent,
            physicalTarget,
            SamePhysicalRoot: false);
    }

    public static void ValidateDisjointOrSame(
        string currentRoot,
        string targetRoot,
        string? defaultBootstrapRoot = null,
        IPhysicalPathResolver? resolver = null,
        IDirectoryCaseSensitivityInspector? caseInspector = null)
    {
        string bootstrap = defaultBootstrapRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PromptHelper");

        ValidateTransition(currentRoot, targetRoot, bootstrap, resolver, caseInspector);
    }

    public static string ResolvePhysicalOrThrow(
        IPhysicalPathResolver resolver,
        string path,
        string role)
    {
        try
        {
            return PathIdentity.NormalizeForComparison(
                resolver.ResolveWithNearestExistingAncestor(path));
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            Win32Exception or
            ArgumentException or
            NotSupportedException)
        {
            throw new InvalidOperationException(
                $"Prompt Helper could not safely resolve the physical {role} path " +
                $"'{path}'. The data-folder operation was cancelled.",
                ex);
        }
    }

    public static bool IsVolumeRoot(string path)
    {
        return IsVolumeRootSafe(path);
    }

    public static bool IsVolumeRootSafe(string path)
    {
        string full = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(full);

        if (root is null)
        {
            return false;
        }

        string fullNormalized = PathIdentity.NormalizeForComparison(full);
        string rootNormalized = PathIdentity.NormalizeForComparison(root);

        return string.Equals(fullNormalized, rootNormalized, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string path)
        => PathIdentity.NormalizeForComparison(path);
}
