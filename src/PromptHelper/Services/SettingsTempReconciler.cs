using System;
using System.Collections.Generic;
using System.IO;

namespace PromptHelper.Services;

public sealed record TempCleanupFailure(
    string Path,
    string ErrorMessage);

public sealed record TempReconciliationResult(
    IReadOnlyList<TempCleanupFailure> Failures)
{
    public bool Success => Failures.Count == 0;
}

internal static class SettingsTempReconciler
{
    public static TempReconciliationResult Reconcile(
        string settingsPath,
        string backupPath)
    {
        string? root = Path.GetDirectoryName(settingsPath);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return new TempReconciliationResult([]);
        }

        var failures = new List<TempCleanupFailure>();

        foreach (string path in Directory.GetFiles(root))
        {
            string name = Path.GetFileName(path);

            bool owned = SettingsTempName.TryParse(name, out _) ||
                         SettingsTempName.TryParseLegacySettingsTemp(name);

            if (!owned)
            {
                continue;
            }

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

        return new TempReconciliationResult(failures);
    }
}
