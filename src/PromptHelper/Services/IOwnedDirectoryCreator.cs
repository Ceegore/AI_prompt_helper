using System;
using System.IO;

namespace PromptHelper.Services;

internal interface IOwnedDirectoryCreator
{
    OwnedDirectoryCreationResult TryCreateOwned(string path);
}

internal sealed record OwnedDirectoryClaim(string Path, WindowsFileIdentity Identity);

internal sealed record OwnedDirectoryCreationResult(
    DirectoryCreateOutcome Outcome,
    OwnedDirectoryClaim? Claim);

internal sealed class WindowsOwnedDirectoryCreator : IOwnedDirectoryCreator
{
    private readonly IReservationFileOps _ops;
    private readonly IOwnedArtifactJournal _ownedArtifacts;

    public WindowsOwnedDirectoryCreator(
        IReservationFileOps? ops = null,
        IOwnedArtifactJournal? ownedArtifacts = null)
    {
        _ops = ops ?? new DefaultReservationFileOps();
        _ownedArtifacts = ownedArtifacts ?? new WindowsOwnedArtifactJournal();
    }

    public OwnedDirectoryCreationResult TryCreateOwned(string path)
    {
        DirectoryCreateOutcome outcome = _ops.TryCreateDirectoryOwned(path);
        if (outcome == DirectoryCreateOutcome.AlreadyExists)
        {
            return new OwnedDirectoryCreationResult(outcome, null);
        }

        string fullPath = System.IO.Path.GetFullPath(path);
        string parent = System.IO.Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Created directory has no parent: '{path}'.");

        using WindowsRetirableDirectory directory =
            WindowsRetirableDirectory.OpenExistingOrNull(fullPath, parent)
            ?? throw new IOException($"Newly-created directory disappeared before it could be claimed: '{path}'.");

        WindowsFileIdentity identity = directory.Identity;

        // Only attempt-owned children of the migration root need restart authority. The root
        // itself is owned by TargetRootReservation and cannot carry a journal outside itself.
        string name = System.IO.Path.GetFileName(fullPath.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar));
        bool requiresDurableClaim =
            string.Equals(name, "prompts", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "recovery", StringComparison.OrdinalIgnoreCase);

        if (requiresDurableClaim)
        {
            string root = parent;
            try
            {
                ProductionRuntimeEvidence.Hit("WindowsOwnedDirectoryCreator.RecordCreationIdentity");
                _ownedArtifacts.Record(root, new OwnedArtifactRecord(
                    Guid.NewGuid(),
                    OwnedArtifactKind.MigrationDirectory,
                    OwnedArtifactPhase.Claimed,
                    System.IO.Path.GetRelativePath(root, fullPath),
                    identity));
            }
            catch (Exception recordFailure)
            {
                try
                {
                    directory.DeleteExact();
                }
                catch (Exception cleanupFailure)
                {
                    throw new IOException(
                        $"Directory ownership could not be recorded and exact cleanup failed for '{path}'.",
                        new AggregateException(recordFailure, cleanupFailure));
                }

                throw;
            }
        }

        return new OwnedDirectoryCreationResult(
            outcome,
            new OwnedDirectoryClaim(fullPath, identity));
    }
}
