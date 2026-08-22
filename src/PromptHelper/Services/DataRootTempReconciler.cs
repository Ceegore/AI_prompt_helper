using System;
using System.Collections.Generic;
using System.IO;

namespace PromptHelper.Services;

/// <summary>
/// Startup reconciliation of leftover staging files inside the data root.
/// </summary>
/// <remarks>
/// <para>CRUU15-007: a filename is not ownership, and neither is "a filename in the current
/// grammar that currently holds a regular non-reparse file inside the data root". That
/// combination proves the object is well-formed and well-located; it proves nothing about who
/// created it. After a crash, any process can leave a file at exactly such a pathname, and the
/// previous implementation deleted it.</para>
/// <para>So the only artifacts destroyed here are the ones an
/// <see cref="IOwnedArtifactJournal"/> proves this application created, matched by the object
/// identity recorded when it was created (<see cref="ArtifactProvenance.JournalOwned"/>).
/// Everything else - current-format files with no record
/// (<see cref="ArtifactProvenance.UnprovenCurrentFormat"/>), legacy names
/// (<see cref="ArtifactProvenance.LegacyUnverifiable"/>), and objects that replaced a recorded
/// one (<see cref="ArtifactProvenance.Foreign"/>) - is preserved and reported, never
/// auto-destroyed.</para>
/// </remarks>
internal static class DataRootTempReconciler
{
    public static TempReconciliationResult Reconcile(
        AppPaths paths,
        bool isBootstrapRoot = false,
        IVerifiedArtifactDeleter? verifiedDeleter = null,
        IOwnedArtifactJournal? ownedArtifacts = null)
    {
        var failures = new List<TempCleanupFailure>();
        var preserved = new List<PreservedArtifact>();
        string dataRoot = paths.RootDirectory;

        if (!Directory.Exists(dataRoot))
        {
            return new TempReconciliationResult(failures, preserved);
        }

        IOwnedArtifactJournal journal = ownedArtifacts ?? new WindowsOwnedArtifactJournal();

        bool hasActiveMigration = File.Exists(paths.MigrationMarkerPath);
        bool hasActiveMutation = File.Exists(paths.LibraryMutationJournalPath);
        bool hasActiveInit = File.Exists(paths.InitializationMarkerPath);

        // Restore interrupted atomic replacements and destroy proven-owned leftovers before
        // classifying whatever remains.
        IReadOnlySet<string> proven =
            OwnedArtifactReconciler.Reconcile(dataRoot, journal, failures);

        // 1. Root directory
        foreach (string file in Directory.GetFiles(dataRoot))
        {
            string name = Path.GetFileName(file);

            if (DurableTempReconciler.TryParseDurableTemp(name, out var fileClass))
            {
                if (fileClass == DurableFileClass.Settings)
                {
                    continue; // Settings temps are managed strictly by SettingsTempReconciler
                }

                if (fileClass == DurableFileClass.MigrationControl && hasActiveMigration)
                {
                    continue; // Recovery-owned
                }

                if (fileClass == DurableFileClass.MutationControl && hasActiveMutation)
                {
                    continue; // Recovery-owned
                }

                if (fileClass == DurableFileClass.InitializationControl && hasActiveInit)
                {
                    continue; // Recovery-owned
                }

                RecordUnproven(file, proven, preserved);
            }

            // Legacy data-root temps (DurableTempReconciler.TryParseLegacyDataRootTemp) are
            // preserved for the same reason: a name is not provenance.
        }

        // 2. Prompts directory
        string promptsDir = paths.PromptsDirectory;
        if (Directory.Exists(promptsDir))
        {
            foreach (string file in Directory.GetFiles(promptsDir))
            {
                string name = Path.GetFileName(file);
                if (DurableTempReconciler.TryParseDurableTemp(name, out var fileClass) && fileClass == DurableFileClass.PromptBody)
                {
                    RecordUnproven(file, proven, preserved);
                }
            }
        }

        // 3. Recovery directory (CRUU12-007)
        string recoveryDir = paths.RecoveryDirectory;
        if (Directory.Exists(recoveryDir))
        {
            foreach (string file in Directory.GetFiles(recoveryDir))
            {
                string name = Path.GetFileName(file);
                if (DurableTempReconciler.TryParseDurableTemp(name, out var fileClass) && fileClass == DurableFileClass.RecoveryArtifact)
                {
                    if (hasActiveMutation)
                    {
                        continue; // Active mutation recovery owns recovery temps
                    }

                    RecordUnproven(file, proven, preserved);
                }
            }
        }

        return new TempReconciliationResult(failures, preserved);
    }

    /// <summary>
    /// Anything still present after journal-driven reconciliation was, by definition, not
    /// proven owned. It is left exactly where it is and reported.
    /// </summary>
    private static void RecordUnproven(
        string fullPath,
        IReadOnlySet<string> proven,
        List<PreservedArtifact> preserved)
    {
        preserved.Add(new PreservedArtifact(
            fullPath,
            proven.Contains(Path.GetFullPath(fullPath))
                ? ArtifactProvenance.JournalOwned
                : ArtifactProvenance.UnprovenCurrentFormat));
    }
}
