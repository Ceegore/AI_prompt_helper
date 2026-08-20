using System;
using System.IO;

namespace PromptHelper.Services;

public static class DataRootTopologyValidator
{
    public static bool IsStrictDescendant(string candidate, string parent)
    {
        string candidateFull = Normalize(candidate);
        string parentFull = Normalize(parent);

        string parentPrefix = parentFull + Path.DirectorySeparatorChar;
        return candidateFull.StartsWith(
            parentPrefix,
            StringComparison.OrdinalIgnoreCase);
    }

    public static void ValidateDisjointOrSame(
        string currentRoot,
        string targetRoot,
        string? defaultBootstrapRoot = null)
    {
        string current = Normalize(currentRoot);
        string target = Normalize(targetRoot);

        if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (IsStrictDescendant(target, current) ||
            IsStrictDescendant(current, target))
        {
            throw new InvalidOperationException(
                "The current and target data folders cannot contain one another.");
        }

        if (IsVolumeRoot(target))
        {
            throw new InvalidOperationException(
                "A root volume or drive cannot be selected as a Prompt Helper data folder.");
        }

        string bootstrap = Normalize(defaultBootstrapRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PromptHelper"));

        if (!string.Equals(target, bootstrap, StringComparison.OrdinalIgnoreCase))
        {
            if (IsStrictDescendant(target, bootstrap) || IsStrictDescendant(bootstrap, target))
            {
                throw new InvalidOperationException(
                    "A custom data folder cannot be inside or contain the Prompt Helper bootstrap settings folder.");
            }
        }
    }

    public static bool IsVolumeRoot(string path)
    {
        string full = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(full);

        return root is not null &&
            string.Equals(
                full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string path)
        => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
