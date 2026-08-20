using System;
using System.IO;

namespace PromptHelper.Services;

public static class DataRootTopologyValidator
{
    public static bool IsStrictDescendant(string candidate, string parent)
    {
        return PathIdentity.IsStrictDescendant(candidate, parent);
    }

    public static void ValidateDisjointOrSame(
        string currentRoot,
        string targetRoot,
        string? defaultBootstrapRoot = null,
        IPhysicalPathResolver? resolver = null)
    {
        string current = PathIdentity.NormalizeForComparison(currentRoot);
        string target = PathIdentity.NormalizeForComparison(targetRoot);

        if (PathIdentity.Equals(current, target))
        {
            return;
        }

        if (IsStrictDescendant(target, current) ||
            IsStrictDescendant(current, target))
        {
            throw new InvalidOperationException(
                "The current and target data folders cannot contain one another.");
        }

        if (IsVolumeRootSafe(target))
        {
            throw new InvalidOperationException(
                "A root volume or drive cannot be selected as a Prompt Helper data folder.");
        }

        string bootstrap = PathIdentity.NormalizeForComparison(defaultBootstrapRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PromptHelper"));

        if (!PathIdentity.Equals(target, bootstrap))
        {
            if (IsStrictDescendant(target, bootstrap) || IsStrictDescendant(bootstrap, target))
            {
                throw new InvalidOperationException(
                    "A custom data folder cannot be inside or contain the Prompt Helper bootstrap settings folder.");
            }
        }

        // Physical resolution check if resolver is available or provided
        var physicalResolver = resolver ?? new WindowsPhysicalPathResolver();
        try
        {
            string physCurrent = physicalResolver.ResolveWithNearestExistingAncestor(current);
            string physTarget = physicalResolver.ResolveWithNearestExistingAncestor(target);
            string physBootstrap = physicalResolver.ResolveWithNearestExistingAncestor(bootstrap);

            if (PathIdentity.Equals(physCurrent, physTarget))
            {
                return;
            }

            if (PathIdentity.IsStrictDescendant(physTarget, physCurrent) ||
                PathIdentity.IsStrictDescendant(physCurrent, physTarget))
            {
                throw new InvalidOperationException(
                    "The current and target data folders cannot physically contain one another.");
            }

            if (!PathIdentity.Equals(physTarget, physBootstrap))
            {
                if (PathIdentity.IsStrictDescendant(physTarget, physBootstrap) ||
                    PathIdentity.IsStrictDescendant(physBootstrap, physTarget))
                {
                    throw new InvalidOperationException(
                        "A custom data folder cannot physically be inside or contain the Prompt Helper bootstrap settings folder.");
                }
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            // If physical path resolution encounters non-fatal environment constraint, lexical check stands
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
