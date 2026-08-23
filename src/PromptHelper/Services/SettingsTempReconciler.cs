using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PromptHelper.Services;

public sealed record TempCleanupFailure(
    string Path,
    string ErrorMessage);

/// <summary>
/// A transient artifact deliberately left on disk, with the reason it was not destroyed.
/// </summary>
internal sealed record PreservedArtifact(
    string Path,
    ArtifactProvenance Provenance);

/// <summary>
/// Raised when reconciliation cannot account for committed state.
/// </summary>
/// <remarks>
/// CRUU16-004: an interrupted compare-and-swap that could not be resolved, an unreadable
/// ownership ledger, or a failed authority check are not stale-temp cleanup problems. Carrying
/// on would mean reading a crash window as ordinary state - for instance treating a
/// momentarily absent settings.json as "no settings yet" and rebuilding defaults over content
/// that is sitting in a pre-image waiting to be restored.
/// </remarks>
public sealed class UnresolvedRecoveryStateException : IOException
{
    public UnresolvedRecoveryStateException(string message)
        : base(message)
    {
    }
}

public sealed record TempReconciliationResult(
    IReadOnlyList<TempCleanupFailure> Failures)
{
    internal TempReconciliationResult(
        IReadOnlyList<TempCleanupFailure> failures,
        IReadOnlyList<PreservedArtifact> preserved,
        IReadOnlyList<ReconciliationOutcome> outcomes)
        : this(failures)
    {
        Preserved = preserved;
        Outcomes = outcomes;
    }

    internal IReadOnlyList<PreservedArtifact> Preserved { get; init; } = [];

    internal IReadOnlyList<ReconciliationOutcome> Outcomes { get; init; } = [];

    public bool Success => Failures.Count == 0;

    internal bool HasFatal => Outcomes.Any(o => o.Severity == ReconciliationSeverity.Fatal);

    /// <summary>
    /// Stops the caller before it consumes application state if anything committed is
    /// unaccounted for.
    /// </summary>
    internal void ThrowIfUnresolved()
    {
        ReconciliationOutcome[] fatal = [.. Outcomes.Where(o => o.Severity == ReconciliationSeverity.Fatal)];
        if (fatal.Length == 0)
        {
            return;
        }

        throw new UnresolvedRecoveryStateException(
            "Prompt Helper found an interrupted update it cannot resolve automatically, and stopped before " +
            "reading any data so that nothing is lost. Nothing was deleted. Details:\n  " +
            string.Join("\n  ", fatal.Select(o => $"[{o.Code}] {o.Path}: {o.Message}")));
    }
}

/// <summary>
/// Startup reconciliation of leftover settings staging files. Only artifacts an
/// <see cref="IOwnedArtifactJournal"/> proves this application created - matched by the object
/// identity recorded at creation - are destroyed. A current-format filename holding an
/// unrecorded object is preserved, because a filename has never been evidence of ownership.
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
            return new TempReconciliationResult([], [], []);
        }

        IOwnedArtifactJournal journal = ownedArtifacts ?? new WindowsOwnedArtifactJournal();
        var failures = new List<TempCleanupFailure>();
        var preserved = new List<PreservedArtifact>();

        OwnedArtifactReconciler.Result result = OwnedArtifactReconciler.Reconcile(root, journal);

        foreach (ReconciliationOutcome outcome in result.Outcomes)
        {
            if (outcome.Severity != ReconciliationSeverity.Notice)
            {
                failures.Add(new TempCleanupFailure(outcome.Path, outcome.Message));
            }
        }

        foreach (string path in Directory.GetFiles(root))
        {
            string name = Path.GetFileName(path);

            if (SettingsTempName.TryParse(name, out _) ||
                (DurableTempReconciler.TryParseDurableTemp(name, out DurableFileClass fileClass) &&
                 fileClass == DurableFileClass.Settings))
            {
                preserved.Add(new PreservedArtifact(
                    path,
                    result.ProvenOwnedPaths.Contains(Path.GetFullPath(path))
                        ? ArtifactProvenance.JournalOwned
                        : ArtifactProvenance.UnprovenCurrentFormat));
            }

            // Legacy-format temps (SettingsTempName.TryParseLegacySettingsTemp) are preserved
            // for the same reason.
        }

        return new TempReconciliationResult(failures, preserved, result.Outcomes);
    }
}
