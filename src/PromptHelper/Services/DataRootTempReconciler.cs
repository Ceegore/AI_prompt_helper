using System;
using System.Collections.Generic;
using System.IO;

namespace PromptHelper.Services;

/// <summary>
/// CRUU14-010: a filename that matches a Prompt Helper temp-file naming convention is not
/// ownership evidence by itself — see <see cref="SettingsTempReconciler"/> for the same
/// reasoning applied to settings temps. Files matching the current, class-tagged naming
/// convention (<see cref="DurableTempReconciler.TryParseDurableTemp"/>) are deleted through
/// <see cref="IVerifiedArtifactDeleter.VerifyIdentityAndDelete"/> instead of a raw
/// <c>File.Delete</c>, so a reparse point or an escape outside the data root at that pathname
/// is refused rather than destroyed. Files matching only a *legacy* naming convention
/// (predating the current scheme) are preserved rather than deleted — there is no formally
/// justified policy here for auto-destroying them.
/// </summary>
internal static class DataRootTempReconciler
{
    public static TempReconciliationResult Reconcile(
        AppPaths paths,
        bool isBootstrapRoot = false,
        IVerifiedArtifactDeleter? verifiedDeleter = null)
    {
        var failures = new List<TempCleanupFailure>();
        string dataRoot = paths.RootDirectory;

        if (!Directory.Exists(dataRoot))
        {
            return new TempReconciliationResult(failures);
        }

        var deleter = verifiedDeleter ?? new WindowsVerifiedArtifactDeleter();

        bool hasActiveMigration = File.Exists(paths.MigrationMarkerPath);
        bool hasActiveMutation = File.Exists(paths.LibraryMutationJournalPath);
        bool hasActiveInit = File.Exists(paths.InitializationMarkerPath);

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

                TryVerifiedDelete(deleter, dataRoot, file, failures);
            }

            // Legacy data-root temps (DurableTempReconciler.TryParseLegacyDataRootTemp) are
            // intentionally left alone: preserved for manual review rather than deleted on a
            // filename match with no ownership proof behind it.
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
                    TryVerifiedDelete(deleter, dataRoot, file, failures);
                }

                // Legacy prompt temps (DurableTempReconciler.TryParseLegacyPromptTemp) are
                // preserved for the same reason as legacy data-root temps above.
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

                    TryVerifiedDelete(deleter, dataRoot, file, failures);
                }
            }
        }

        return new TempReconciliationResult(failures);
    }

    private static void TryVerifiedDelete(
        IVerifiedArtifactDeleter deleter,
        string physicalRoot,
        string path,
        List<TempCleanupFailure> failures)
    {
        try
        {
            deleter.VerifyIdentityAndDelete(physicalRoot, path);
        }
        catch (Exception ex)
        {
            failures.Add(new TempCleanupFailure(path, ex.Message));
        }
    }
}
