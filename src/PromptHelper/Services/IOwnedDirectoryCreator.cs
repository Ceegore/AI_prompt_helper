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
        ProductionRuntimeEvidence.Hit("WindowsOwnedDirectoryCreator.TryCreateOwned");
        string fullPath = System.IO.Path.GetFullPath(path);
        string parent = System.IO.Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Created directory has no parent: '{path}'.");
        string name = System.IO.Path.GetFileName(fullPath.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar));
        bool requiresDurableClaim =
            string.Equals(name, "prompts", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "recovery", StringComparison.OrdinalIgnoreCase);

        if (requiresDurableClaim)
        {
            return TryCreateCrashAtomicClaim(fullPath, parent);
        }

        DirectoryCreateOutcome outcome = _ops.TryCreateDirectoryOwned(path);
        if (outcome == DirectoryCreateOutcome.AlreadyExists)
        {
            return new OwnedDirectoryCreationResult(outcome, null);
        }

        using WindowsRetirableDirectory directory =
            WindowsRetirableDirectory.OpenExistingOrNull(fullPath, parent)
            ?? throw new IOException($"Newly-created directory disappeared before it could be claimed: '{path}'.");

        WindowsFileIdentity identity = directory.Identity;

        return new OwnedDirectoryCreationResult(
            outcome,
            new OwnedDirectoryClaim(fullPath, identity));
    }

    private OwnedDirectoryCreationResult TryCreateCrashAtomicClaim(string fullPath, string parent)
    {
        using WindowsCrashAtomicDirectoryBootstrap? directory =
            WindowsCrashAtomicDirectoryBootstrap.CreateNewOrNull(fullPath, parent);
        if (directory is null)
        {
            return new OwnedDirectoryCreationResult(DirectoryCreateOutcome.AlreadyExists, null);
        }

        ProductionCrashCut.Hit("WindowsOwnedDirectoryCreator.AfterCreateBeforeFirstClaim");
        WindowsFileIdentity identity = directory.Identity;
        try
        {
            ProductionRuntimeEvidence.Hit("WindowsOwnedDirectoryCreator.RecordCreationIdentity");
            _ownedArtifacts.Record(parent, new OwnedArtifactRecord(
                Guid.NewGuid(),
                OwnedArtifactKind.MigrationDirectory,
                OwnedArtifactPhase.Claimed,
                System.IO.Path.GetRelativePath(parent, fullPath),
                identity));
            directory.PersistAfterDurableClaim();
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
                    $"Directory ownership could not be recorded and exact cleanup failed for '{fullPath}'.",
                    new AggregateException(recordFailure, cleanupFailure));
            }

            throw;
        }

        return new OwnedDirectoryCreationResult(
            DirectoryCreateOutcome.CreatedByCaller,
            new OwnedDirectoryClaim(fullPath, identity));
    }
}
