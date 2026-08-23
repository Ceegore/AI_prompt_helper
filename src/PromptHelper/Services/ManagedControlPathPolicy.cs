using System;
using System.IO;

namespace PromptHelper.Services;

internal static class ManagedControlPathPolicy
{
    public static bool IsReservedRootControl(
        string relativePath,
        bool targetIsBootstrapRoot)
    {
        return IsReservedEphemeralRootControl(relativePath) ||
               IsPersistentManagedControl(relativePath) ||
               (targetIsBootstrapRoot && IsPersistentBootstrapControl(relativePath));
    }

    /// <summary>
    /// Managed state that legitimately persists in <i>any</i> data root, not just the bootstrap
    /// one. The ownership ledger belongs here: since CRUU16-005 it carries the identity of
    /// migrated payload objects for as long as those objects exist, so it outlives the
    /// migration that created them and must not be mistaken for in-flight control state.
    /// </summary>
    public static bool IsPersistentManagedControl(string relativePath)
    {
        string p = NormalizeRelative(relativePath);

        if (p.Contains(Path.DirectorySeparatorChar) || p.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        return EqualsName(p, WindowsOwnedArtifactJournal.JournalFileName);
    }

    /// <summary>
    /// Reserved control names that are always ephemeral migration/mutation state: they must
    /// never persist across a completed migration, regardless of whether the target root is
    /// the bootstrap root.
    /// </summary>
    public static bool IsReservedEphemeralRootControl(string relativePath)
    {
        string p = NormalizeRelative(relativePath);

        if (p.Contains(Path.DirectorySeparatorChar) || p.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        return EqualsName(p, ".app.lock") ||
               EqualsName(p, ".prompthelper-migration.json") ||
               EqualsName(p, ".prompthelper-library-mutation.json") ||
               EqualsName(p, "initializing.marker");
    }

    /// <summary>
    /// Names that are legitimate, persistent bootstrap state (application settings) and are
    /// only ever expected when the target root is exactly the bootstrap root. Unlike ephemeral
    /// controls, these are allowed to remain through and after a completed migration.
    /// </summary>
    public static bool IsPersistentBootstrapControl(string relativePath)
    {
        string p = NormalizeRelative(relativePath);

        if (p.Contains(Path.DirectorySeparatorChar) || p.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        return EqualsName(p, ".settings.lock") ||
               EqualsName(p, "settings.json") ||
               EqualsName(p, "settings.backup.json");
    }

    private static string NormalizeRelative(string path)
    {
        return path.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool EqualsName(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
