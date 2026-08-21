using System;
using System.IO;

namespace PromptHelper.Services;

internal static class RecoveryBaselineVerifier
{
    public static void AssertRestored(
        string targetRoot,
        MigrationTargetInventory inventory,
        IAuthorityFileOps authorityOps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(authorityOps);

        if (inventory.HasUnknownEntries)
        {
            throw new InvalidDataException(
                "Recovery target contains unknown entries: " +
                string.Join(", ", inventory.UnknownEntries));
        }

        foreach (string rel in inventory.FinalArtifacts)
        {
            AssertMissing(targetRoot, rel, authorityOps);
        }

        foreach (string rel in inventory.PayloadTemps)
        {
            AssertMissing(targetRoot, rel, authorityOps);
        }

        foreach (string rel in inventory.DeclaredControls)
        {
            AssertMissing(targetRoot, rel, authorityOps);
        }
    }

    private static void AssertMissing(string targetRoot, string relativePath, IAuthorityFileOps authorityOps)
    {
        string full = Path.Combine(targetRoot, relativePath.Replace('/', '\\'));
        StrictFilePresence presence = authorityOps.GetPresenceStrict(full);
        if (presence != StrictFilePresence.Missing)
        {
            throw new InvalidDataException(
                $"Recovery target still contains unremoved artifact: '{relativePath}'.");
        }
    }
}
