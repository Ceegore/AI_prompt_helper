using System;
using System.Collections.Generic;
using System.IO;

namespace PromptHelper.Services;

internal static class DataRootTempReconciler
{
    public static TempReconciliationResult Reconcile(
        AppPaths paths,
        bool isBootstrapRoot = false)
    {
        var failures = new List<TempCleanupFailure>();
        string dataRoot = paths.RootDirectory;

        if (!Directory.Exists(dataRoot))
        {
            return new TempReconciliationResult(failures);
        }

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

                TryDeleteFile(file, failures);
            }
            else if (DurableTempReconciler.TryParseLegacyDataRootTemp(name, out _))
            {
                TryDeleteFile(file, failures);
            }
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
                    TryDeleteFile(file, failures);
                }
                else if (DurableTempReconciler.TryParseLegacyPromptTemp(name))
                {
                    TryDeleteFile(file, failures);
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

                    TryDeleteFile(file, failures);
                }
            }
        }

        return new TempReconciliationResult(failures);
    }

    private static void TryDeleteFile(string path, List<TempCleanupFailure> failures)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            failures.Add(new TempCleanupFailure(path, ex.Message));
        }
    }
}
