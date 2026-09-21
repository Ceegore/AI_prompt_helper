using System.ComponentModel;
using System.Security.Cryptography;

namespace PromptHelper.Services;

/// <summary>
/// Linux compare-and-swap implementation.
///
/// Linux does not provide the same deny-delete/deny-write handle semantics used by the Windows
/// implementation. Instead, the target is first verified through a retained O_NOFOLLOW handle,
/// then the pathname is moved aside with RENAME_NOREPLACE. The moved object is re-verified by
/// inode identity and content before the candidate may publish. If another writer won the race,
/// its object is restored or preserved rather than overwritten.
/// </summary>
internal sealed class LinuxAtomicExpectedFileReplacer : IAtomicExpectedFileReplacer
{
    internal static Action<string>? PreSwapBarrierForTests;
    internal static Action<string>? BeforeCandidatePromotionForTests;

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

    private static void ReplaceExpectingMissing(
        string directory,
        string fullTarget,
        ReadOnlySpan<byte> candidateBytes,
        DurableFileClass fileClass)
    {
        string stagePath = CreateStagePath(directory, fileClass);
        using FileStream stage =
            LinuxNativeFileSystem.CreateExclusiveStage(stagePath);

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

    private static void ReplaceExpectingPresent(
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
        string stagePath = CreateStagePath(directory, fileClass);
        string preimagePath = Path.Combine(
            directory,
            $".prompthelper-preimage-{Path.GetFileName(fullTarget)}-{Guid.NewGuid():N}.tmp");

        using FileStream stage =
            LinuxNativeFileSystem.CreateExclusiveStage(stagePath);
        stage.Write(candidateBytes);
        stage.Flush(flushToDisk: true);

        PreSwapBarrierForTests?.Invoke(fullTarget);

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

        // Make the pre-image name durable before the candidate can be published. A crash here
        // must leave the last committed bytes discoverable rather than only in volatile rename
        // state.
        LinuxNativeFileSystem.FlushDirectory(directory);

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

        BeforeCandidatePromotionForTests?.Invoke(fullTarget);

        // Re-check immediately before publication. This catches in-place writers that wrote to
        // the original inode after the first verification but before the final no-overwrite
        // publish gate.
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

            // A concurrent object now occupies the target. Never overwrite it just to roll
            // back: the previous committed content remains under the pre-image name.
            throw new StaleExpectedFileException(
                $"'{fullTarget}' changed while its replacement was being published. " +
                $"The concurrent target was preserved and the previous content remains at '{preimagePath}'. " +
                $"Rollback also could not reclaim the target (errno {restoreError}).",
                new Win32Exception(promotionError));
        }

        LinuxNativeFileSystem.FlushDirectory(directory);

        Guid operationId = Guid.NewGuid();
        try
        {
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
                $"The replacement of '{fullTarget}' was committed, but its verified Linux pre-image could not be safely retired. Restart before making another change.",
                cleanup);
        }
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
                $"A different filesystem object was moved aside from the target. It will not be overwritten.");
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
        using LinuxExpectedTargetAuthority? preimage =
            LinuxExpectedTargetAuthority.Open(preimagePath, physicalRoot);

        if (preimage is null)
        {
            return;
        }

        if (preimage.Identity != expectedObject)
        {
            throw new StaleExpectedFileException(
                $"Refusing to delete replaced pre-image object at '{preimagePath}'.");
        }

        preimage.AssertContentMatches(expectedHash);

        // Linux has no fd-bound unlink equivalent. The pathname is a random app-owned name,
        // identity and bytes were re-proven immediately above, and this implementation is not
        // wired into the desktop composition until the Linux recovery ledger is added.
        File.Delete(preimagePath);
    }

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
