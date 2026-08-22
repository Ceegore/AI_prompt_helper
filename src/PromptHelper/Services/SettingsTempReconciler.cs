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

/// <summary>
/// CRUU14-010: a filename that matches the current settings-temp naming convention is not
/// ownership evidence by itself — a foreign file could occupy that exact pathname after a
/// crash, a staging-file replacement, or unrelated interference. Files matching the current
/// convention (GUID-suffixed, generated fresh by this process for every write) are deleted
/// through <see cref="IVerifiedArtifactDeleter.VerifyIdentityAndDelete"/>, which at least
/// refuses a reparse point or a path that resolves outside the settings directory. Files
/// matching only the *legacy* naming convention (predating that scheme, with no comparable
/// identity signal) are preserved rather than deleted — there is no formally justified policy
/// here for auto-destroying them, so the safe default is to leave them for manual review.
/// </summary>
internal static class SettingsTempReconciler
{
    public static TempReconciliationResult Reconcile(
        string settingsPath,
        string backupPath,
        IVerifiedArtifactDeleter? verifiedDeleter = null)
    {
        string? root = Path.GetDirectoryName(settingsPath);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return new TempReconciliationResult([]);
        }

        var deleter = verifiedDeleter ?? new WindowsVerifiedArtifactDeleter();
        var failures = new List<TempCleanupFailure>();

        foreach (string path in Directory.GetFiles(root))
        {
            string name = Path.GetFileName(path);

            if (SettingsTempName.TryParse(name, out _))
            {
                try
                {
                    deleter.VerifyIdentityAndDelete(root, path);
                }
                catch (Exception ex)
                {
                    failures.Add(new TempCleanupFailure(path, ex.Message));
                }
            }

            // Legacy-format temps (SettingsTempName.TryParseLegacySettingsTemp) are
            // intentionally left alone: preserved for manual review rather than deleted on a
            // filename match with no ownership proof behind it.
        }

        return new TempReconciliationResult(failures);
    }
}
