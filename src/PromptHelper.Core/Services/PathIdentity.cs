using System;
using System.IO;

namespace PromptHelper.Services;

public static class PathIdentity
{
    private static StringComparison PlatformComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static bool Equals(string left, string right)
        => string.Equals(
            NormalizeForComparison(left),
            NormalizeForComparison(right),
            PlatformComparison);

    public static bool IsStrictDescendant(
        string candidate,
        string parent)
    {
        string child = NormalizeForComparison(candidate);
        string ancestor = NormalizeForComparison(parent);

        if (Equals(child, ancestor))
        {
            return false;
        }

        string prefix = EnsureTrailingSeparator(ancestor);

        return child.StartsWith(
            prefix,
            PlatformComparison);
    }

    public static string NormalizeForComparison(string path)
    {
        string full = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(full);

        if (root is not null &&
            string.Equals(
                full,
                root,
                PlatformComparison))
        {
            return root;
        }

        return full.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) ||
            path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}
