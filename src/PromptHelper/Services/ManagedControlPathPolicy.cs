using System;
using System.IO;

namespace PromptHelper.Services;

internal static class ManagedControlPathPolicy
{
    public static bool IsReservedRootControl(
        string relativePath,
        bool targetIsBootstrapRoot)
    {
        string p = NormalizeRelative(relativePath);

        if (p.Contains(Path.DirectorySeparatorChar) || p.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        if (EqualsName(p, ".app.lock") ||
            EqualsName(p, ".prompthelper-migration.json") ||
            EqualsName(p, ".prompthelper-library-mutation.json") ||
            EqualsName(p, "initializing.marker"))
        {
            return true;
        }

        if (targetIsBootstrapRoot &&
            (EqualsName(p, ".settings.lock") ||
             EqualsName(p, "settings.json") ||
             EqualsName(p, "settings.backup.json")))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeRelative(string path)
    {
        return path.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool EqualsName(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
