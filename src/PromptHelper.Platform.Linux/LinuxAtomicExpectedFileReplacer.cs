using System.ComponentModel;
using System.Security.Cryptography;

namespace PromptHelper.Services;

/// <summary>
/// Linux compare-and-swap implementation with durable ownership bookkeeping.
///
/// Linux cannot hold a pathname under the same deny-delete/deny-write semantics used by the
/// Windows implementation. The transaction therefore records enough exact-object and content
/// authority before every durable cut to make restart recovery deterministic and fail-closed.
/// </summary>
internal sealed class LinuxAtomicExpectedFileReplacer : IAtomicExpectedFileReplacer
{
    private readonly IOwnedArtifactJournal _ownedArtifacts;

    internal static Action<string>? PreSwapBarrierForTests;
    internal static Action<string>? AfterPreparedRecordForTests;
    internal static Action<string>? AfterSidelineBeforePhaseRecordForTests;
    internal static Action<string>? BetweenRenamesForTests;
    internal static Action<string>? BeforeCandidatePromotionForTests;
    internal static Action<string>? AfterCandidatePublishBeforePhaseRecordForTests;

    public LinuxAtomicExpectedFileReplacer(
        IOwnedArtifactJournal? ownedArtifacts = null)
    {
        _ownedArtifacts = ownedArtifacts ?? new LinuxOwnedArtifactJournal();
    }

    public void ReplaceIfExpected(
        string physicalRoot,
        string targetPath,
        ExpectedFileState expected,
        ReadOnlySpan<byte> candidateBytes,
        DurableFileClass fileClass)
    {
        LinuxNativeFileSystem.EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(expected);

        string fullRoot =
            new LinuxPhysicalPathResolver()
                .ResolveWithNearestExistingAncestor(physicalRoot);
        string fullTarget = Path.GetFullPath(targetPath);
        string directory = Path.GetDirectoryName(fullTarget)
            ?? throw new ArgumentException(
                $"Invalid directory for target path '{targetPath}'.",
                nameof(targetPath));

        Directory.CreateDirectory(directory);
        AssertDirectoryInsideRoot(directory, fullRoot);

        if (expected.Kind == ExpectedFileStateKind.Missing)
        {
            ReplaceExpectingMissing(
                fullRoot,
                directory,
                fullTarget,
                candidateBytes,
                fileClass);
            return;
        }

        ReplaceExpectingPresent(
            fullRoot,
            directory,
            fullTarget,
            expected,
            candidateBytes,
            fileClass);
    }

    private void ReplaceExpectingMissing(
        string physicalRoot,
        string directory,
        string fullTarget,
        ReadOnlySpan<byte> candidateBytes,
        DurableFileClass fileClass)
    {
        string stagePath = CreateStagePath(directory, fileClass);
        using FileStream stage =
            LinuxNativeFileSystem.CreateExclusiveStage(stagePath);

        RecordStageOwnership(physicalRoot, stagePath, stage);

        stage.Write(candidateBytes);
        stage.Flush(flushToDisk: true);

        PreSwapBarrierForTests?.Invoke(fullTarget);

        if (!LinuxNativeFileSystem.TryRenameNoReplace(
                stagePath,
                fullTarget,
                out int error))
        {
            if (error == LinuxNativeFileSystem.ErrorExists)
            {
                throw new StaleExpectedFileException(
                    $"'{fullTarget}' was expected not to exist, but another object reached that pathname first. It was preserved.",
                    new Win32Exception(error));
            }

            throw LinuxNativeFileSystem.NativeIOException(
                $"Unable to publish expected-missing target '{fullTarget}'.",
                error);
        }

        LinuxNativeFileSystem.FlushDirectory(directory);
    }

    private void ReplaceExpectingPresent(
        string physicalRoot,
        string directory,
        string fullTarget,
        ExpectedFileState expected,
        ReadOnlySpan<byte> candidateBytes,
        DurableFileClass fileClass)
    {
        using LinuxExpectedTargetAuthority? authority =
            LinuxExpectedTargetAuthority.Open(fullTarget, physicalRoot);

        if (authority is null)
        {
            throw new StaleExpectedFileException(
                $"'{fullTarget}' was expected to exist with known content, but it is gone. Reload before editing.");
        }

        string expectedHash = expected.ExpectedSha256Hex!;
        authority.AssertContentMatches(expectedHash);

        if (expected.ExpectedIdentity is FileObjectIdentity expectedIdentity)
        {
            authority.AssertIdentityMatches(expectedIdentity);
        }

        LinuxFileIdentity expectedObject = authority.Identity;
        FileObjectIdentity expectedObjectIdentity =
            expectedObject.ToObjectIdentity();

        string stagePath = CreateStagePath(directory, fileClass);
        string preimagePath = Path.Combine(
            directory,
            $".prompthelper-preimage-{Path.GetFileName(fullTarget)}-{Guid.NewGuid():N}.tmp");

        byte[] candidateCopy = candidateBytes.ToArray();
        string candidateHash =
            Convert.ToHexStringLower(SHA256.HashData(candidateCopy));
        long candidateLength = candidateCopy.LongLength;
        Guid operationId = Guid.NewGuid();

        using FileStream stage =
            LinuxNativeFileSystem.CreateExclusiveStage(stagePath);

        RecordStageOwnership(physicalRoot, stagePath, stage);

        stage.Write(candidateCopy);
        stage.Flush(flushToDisk: true);

        PreSwapBarrierForTests?.Invoke(fullTarget);

        // Durable authority exists before the first rename. A crash from this point onward can
        // distinguish "rename never started" from a sidelined pre-image.
        _ownedArtifacts.Record(
            physicalRoot,
            new OwnedArtifactRecord(
                operationId,
                OwnedArtifactKind.CasPreimage,
                OwnedArtifactPhase.Prepared,
                Relative(physicalRoot, preimagePath),
                expectedObjectIdentity,
                Relative(physicalRoot, fullTarget),
                candidateHash,
                candidateLength,
                MarkerAttemptId: null,
                PreviousSha256Hex: expectedHash));

        AfterPreparedRecordForTests?.Invoke(fullTarget);

        if (!LinuxNativeFileSystem.TryRenameNoReplace(
                fullTarget,
                preimagePath,
                out int sidelineError))
        {
            if (sidelineError is
                LinuxNativeFileSystem.ErrorNoEntry or
                LinuxNativeFileSystem.ErrorNotDirectory)
            {
                throw new StaleExpectedFileException(
                    $"'{fullTarget}' disappeared before it could be atomically replaced.",
                    new Win32Exception(sidelineError));
            }

            throw LinuxNativeFileSystem.NativeIOException(
                $"Unable to move '{fullTarget}' aside for atomic replacement.",
                sidelineError);
        }

        LinuxNativeFileSystem.FlushDirectory(directory);

        AfterSidelineBeforePhaseRecordForTests?.Invoke(fullTarget);

        try
        {
            VerifySidelinedObject(
                preimagePath,
                physicalRoot,
                expectedObject,
                expectedHash);
        }
        catch (StaleExpectedFileException stale)
        {
            RestoreSidelinedRaceOrPreserve(
                preimagePath,
                fullTarget,
                directory,
                stale);
            throw;
        }

        try
        {
            _ownedArtifacts.Record(
                physicalRoot,
                new OwnedArtifactRecord(
                    operationId,
                    OwnedArtifactKind.CasPreimage,
                    OwnedArtifactPhase.PreimageSidelined,
                    Relative(physicalRoot, preimagePath),
                    expectedObjectIdentity,
                    Relative(physicalRoot, fullTarget),
                    candidateHash,
                    candidateLength,
                    MarkerAttemptId: null,
                    PreviousSha256Hex: expectedHash));
        }
        catch
        {
            // Candidate was not published. Restore the same inode if the target is still free;
            // otherwise keep both objects for restart diagnostics.
            _ = LinuxNativeFileSystem.TryRenameNoReplace(
                preimagePath,
                fullTarget,
                out _);
            LinuxNativeFileSystem.FlushDirectory(directory);
            throw;
        }

        BetweenRenamesForTests?.Invoke(fullTarget);
        BeforeCandidatePromotionForTests?.Invoke(fullTarget);

        // Catch a writer that kept an fd to the old inode and changed it after the sideline.
        try
        {
            VerifySidelinedObject(
                preimagePath,
                physicalRoot,
                expectedObject,
                expectedHash);
        }
        catch (StaleExpectedFileException stale)
        {
            RestoreSidelinedRaceOrPreserve(
                preimagePath,
                fullTarget,
                directory,
                stale);
            throw;
        }

        if (!LinuxNativeFileSystem.TryRenameNoReplace(
                stagePath,
                fullTarget,
                out int promotionError))
        {
            bool restored = LinuxNativeFileSystem.TryRenameNoReplace(
                preimagePath,
                fullTarget,
                out int restoreError);

            if (restored)
            {
                LinuxNativeFileSystem.FlushDirectory(directory);
                throw new StaleExpectedFileException(
                    $"'{fullTarget}' changed while its replacement was being published. The previous content was restored.",
                    new Win32Exception(promotionError));
            }

            throw new StaleExpectedFileException(
                $"'{fullTarget}' changed while its replacement was being published. " +
                $"The concurrent target was preserved and the previous content remains at '{preimagePath}'. " +
                $"Rollback also could not reclaim the target (errno {restoreError}).",
                new Win32Exception(promotionError));
        }

        LinuxNativeFileSystem.FlushDirectory(directory);

        AfterCandidatePublishBeforePhaseRecordForTests?.Invoke(fullTarget);

        try
        {
            _ownedArtifacts.Record(
                physicalRoot,
                new OwnedArtifactRecord(
                    operationId,
                    OwnedArtifactKind.CasPreimage,
                    OwnedArtifactPhase.CandidatePublished,
                    Relative(physicalRoot, preimagePath),
                    expectedObjectIdentity,
                    Relative(physicalRoot, fullTarget),
                    candidateHash,
                    candidateLength,
                    MarkerAttemptId: null,
                    PreviousSha256Hex: expectedHash));

            RetireVerifiedPreimage(
                preimagePath,
                physicalRoot,
                expectedObject,
                expectedHash);
            LinuxNativeFileSystem.FlushDirectory(directory);
        }
        catch (Exception cleanup) when (
            cleanup is IOException or
            UnauthorizedAccessException or
            StaleExpectedFileException)
        {
            throw new CommittedAtomicReplacementRequiresRestartException(
                operationId,
                fullTarget,
                $"The replacement of '{fullTarget}' was committed, but durable Linux recovery bookkeeping or verified pre-image cleanup did not finish. Restart before making another change.",
                cleanup);
        }
    }

    private void RecordStageOwnership(
        string physicalRoot,
        string stagePath,
        FileStream stage)
    {
        LinuxFileIdentity identity =
            LinuxFileIdentity.FromHandle(stage.SafeFileHandle);

        _ownedArtifacts.Record(
            physicalRoot,
            new OwnedArtifactRecord(
                Guid.NewGuid(),
                OwnedArtifactKind.Stage,
                OwnedArtifactPhase.Claimed,
                Relative(physicalRoot, stagePath),
                identity.ToObjectIdentity()));
    }

    private static void VerifySidelinedObject(
        string preimagePath,
        string physicalRoot,
        LinuxFileIdentity expectedObject,
        string expectedHash)
    {
        using LinuxExpectedTargetAuthority? moved =
            LinuxExpectedTargetAuthority.Open(preimagePath, physicalRoot);

        if (moved is null)
        {
            throw new StaleExpectedFileException(
                $"The expected object disappeared after it was moved aside to '{preimagePath}'.");
        }

        if (moved.Identity != expectedObject)
        {
            throw new StaleExpectedFileException(
                "A different filesystem object was moved aside from the target. It will not be overwritten.");
        }

        moved.AssertContentMatches(expectedHash);
    }

    private static void RestoreSidelinedRaceOrPreserve(
        string preimagePath,
        string fullTarget,
        string directory,
        Exception race)
    {
        if (LinuxNativeFileSystem.TryRenameNoReplace(
                preimagePath,
                fullTarget,
                out int restoreError))
        {
            LinuxNativeFileSystem.FlushDirectory(directory);
            return;
        }

        throw new StaleExpectedFileException(
            $"A concurrent change was detected while replacing '{fullTarget}'. " +
            $"The changed object is preserved at '{preimagePath}', and another object now occupies the target " +
            $"(rollback errno {restoreError}).",
            race);
    }

    private static void RetireVerifiedPreimage(
        string preimagePath,
        string physicalRoot,
        LinuxFileIdentity expectedObject,
        string expectedHash)
    {
        LinuxExactRetirementOutcome outcome =
            LinuxExactFileRetirement.Retire(
                physicalRoot,
                preimagePath,
                expectedObject.ToObjectIdentity(),
                expectedHash);

        if (outcome is
            LinuxExactRetirementOutcome.Retired or
            LinuxExactRetirementOutcome.Missing)
        {
            return;
        }

        throw new StaleExpectedFileException(
            $"The verified Linux pre-image at '{preimagePath}' could not be retired without risking a foreign replacement.");
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path);

    private static string CreateStagePath(
        string directory,
        DurableFileClass fileClass) =>
        Path.Combine(
            directory,
            $".prompthelper-tmp-{DurableFileNaming.GetClassTag(fileClass)}-{Guid.NewGuid():N}.tmp");

    private static void AssertDirectoryInsideRoot(
        string directory,
        string physicalRoot)
    {
        string resolvedDirectory =
            new LinuxPhysicalPathResolver()
                .ResolveWithNearestExistingAncestor(directory);

        if (!PathIdentity.Equals(resolvedDirectory, physicalRoot) &&
            !PathIdentity.IsStrictDescendant(resolvedDirectory, physicalRoot))
        {
            throw new InvalidDataException(
                $"Refusing target directory outside managed Linux data root. Root: '{physicalRoot}', directory: '{resolvedDirectory}'.");
        }
    }
}
