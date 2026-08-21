using System;
using System.IO;

namespace PromptHelper.Services;

public sealed record LifecycleJournalPresence(
    bool Migration,
    bool Mutation,
    bool Initialization)
{
    public int Count =>
        (Migration ? 1 : 0) +
        (Mutation ? 1 : 0) +
        (Initialization ? 1 : 0);
}

internal sealed class RecoveryJournalConflictDetector
{
    private readonly StrictPathAuthority _paths;

    public RecoveryJournalConflictDetector(StrictPathAuthority? paths = null)
    {
        _paths = paths ?? new StrictPathAuthority();
    }

    public LifecycleJournalPresence Inspect(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        bool migration = RequireFileOrMissing(paths.MigrationMarkerPath);
        bool mutation = RequireFileOrMissing(paths.LibraryMutationJournalPath);
        bool init = RequireFileOrMissing(paths.InitializationMarkerPath);

        return new LifecycleJournalPresence(
            migration,
            mutation,
            init);
    }

    public void EnsureNoConflicts(AppPaths paths)
    {
        LifecycleJournalPresence presence = Inspect(paths);
        if (presence.Count > 1)
        {
            throw new InvalidDataException(
                "Multiple interrupted Prompt Helper transaction journals exist. Automatic recovery was stopped to protect data.");
        }
    }

    private bool RequireFileOrMissing(string path)
    {
        StrictPathProbe probe = _paths.Probe(path);

        return probe.Kind switch
        {
            StrictPathKind.Missing => false,
            StrictPathKind.File => true,
            StrictPathKind.Directory => throw new InvalidDataException($"Lifecycle marker is a directory: '{path}'."),
            _ => throw new InvalidOperationException()
        };
    }
}
