using System;
using System.Collections.Generic;
using System.IO;

namespace PromptHelper.Services;

public sealed record TempCleanupFailure(
    string Path,
    string ErrorMessage);

/// <summary>
/// A transient artifact that was deliberately left on disk, with the reason it was not
/// destroyed. Reported rather than silently ignored so a data root that accumulates unproven
/// files is visible instead of invisible.
/// </summary>
internal sealed record PreservedArtifact(
    string Path,
    ArtifactProvenance Provenance);

public sealed record TempReconciliationResult(
    IReadOnlyList<TempCleanupFailure> Failures)
{
    internal TempReconciliationResult(
        IReadOnlyList<TempCleanupFailure> failures,
        IReadOnlyList<PreservedArtifact> preserved)
        : this(failures)
    {
        Preserved = preserved;
    }

    internal IReadOnlyList<PreservedArtifact> Preserved { get; init; } = [];

    public bool Success => Failures.Count == 0;
}

/// <summary>
/// Startup reconciliation of leftover settings staging files. Same rule as
/// <see cref="DataRootTempReconciler"/>: only artifacts an <see cref="IOwnedArtifactJournal"/>
/// proves this application created - matched by the object identity recorded at creation - are
/// destroyed. A current-format filename holding an unrecorded object is preserved, because a
/// filename has never been evidence of ownership (CRUU15-007).
/// </summary>
internal static class SettingsTempReconciler
{
    public static TempReconciliationResult Reconcile(
        string settingsPath,
        string backupPath,
        IVerifiedArtifactDeleter? verifiedDeleter = null,
        IOwnedArtifactJournal? ownedArtifacts = null)
    {
        string? root = Path.GetDirectoryName(settingsPath);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return new TempReconciliationResult([], []);
        }

        IOwnedArtifactJournal journal = ownedArtifacts ?? new WindowsOwnedArtifactJournal();
        var failures = new List<TempCleanupFailure>();
        var preserved = new List<PreservedArtifact>();

        IReadOnlySet<string> proven = OwnedArtifactReconciler.Reconcile(root, journal, failures);

        foreach (string path in Directory.GetFiles(root))
        {
            string name = Path.GetFileName(path);

            if (SettingsTempName.TryParse(name, out _) ||
                (DurableTempReconciler.TryParseDurableTemp(name, out DurableFileClass fileClass) &&
                 fileClass == DurableFileClass.Settings))
            {
                preserved.Add(new PreservedArtifact(
                    path,
                    proven.Contains(Path.GetFullPath(path))
                        ? ArtifactProvenance.JournalOwned
                        : ArtifactProvenance.UnprovenCurrentFormat));
            }

            // Legacy-format temps (SettingsTempName.TryParseLegacySettingsTemp) are preserved
            // for the same reason.
        }

        return new TempReconciliationResult(failures, preserved);
    }
}
