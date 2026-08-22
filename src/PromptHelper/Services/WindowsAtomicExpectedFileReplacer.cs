using System;
using System.ComponentModel;
using System.IO;

namespace PromptHelper.Services;

/// <summary>
/// The production compare-and-swap. See <see cref="IAtomicExpectedFileReplacer"/> for the
/// contract and <see cref="WindowsExpectedTargetAuthority"/> for why the sequence below is
/// shaped the way it is.
/// </summary>
/// <remarks>
/// <para><b>ExpectedPresent.</b>
/// <list type="number">
/// <item>Take exclusive authority over the object at the target (denies every other writer and
/// every other renamer/deleter for the whole operation).</item>
/// <item>Prove its hash from that retained handle.</item>
/// <item>Stage the replacement in an owned, root-verified, write-through stage.</item>
/// <item>Durably record ownership of the pre-image about to be created, so a crash inside the
/// two-rename window below is recoverable from identity rather than guesswork.</item>
/// <item>Rename the authority object aside — the exclusion is consumed here, by the exact
/// object that was verified.</item>
/// <item>Promote the stage into the vacated name with <i>no-overwrite</i> semantics. If
/// anything at all occupies the target by then, this fails and nothing is destroyed.</item>
/// <item>Delete the pre-image through its retained handle.</item>
/// </list>
/// If step 6 fails, the pre-image is renamed back; if that also fails the pre-image is left on
/// disk with its journal record intact. No path in this method ever destroys an object it did
/// not create or prove.</para>
/// <para><b>ExpectedMissing.</b> A stage is promoted with no-overwrite semantics, so "must
/// still be missing" is enforced by the promotion itself. An earlier
/// <c>File.Exists</c>-style probe would only have proven the file was missing at some point in
/// the past (CRUU15-004).</para>
/// </remarks>
internal sealed class WindowsAtomicExpectedFileReplacer : IAtomicExpectedFileReplacer
{
    private const int ERROR_FILE_EXISTS = 80;
    private const int ERROR_ALREADY_EXISTS = 183;
    private const int ERROR_ACCESS_DENIED = 5;
    private const int ERROR_SHARING_VIOLATION = 32;

    private readonly IOwnedArtifactJournal _ownedArtifacts;

    public WindowsAtomicExpectedFileReplacer(IOwnedArtifactJournal? ownedArtifacts = null)
    {
        _ownedArtifacts = ownedArtifacts ?? new WindowsOwnedArtifactJournal();
    }

    /// <summary>
    /// Fired once the expected authority is held and the replacement is staged, immediately
    /// before the atomic swap begins. Exists so acceptance tests can inject a concurrent
    /// mutation at the exact barrier the CRUU14 design could not defend, while still running
    /// the real production primitive — a test double here would prove nothing about the
    /// primitive's atomicity. Never set outside tests.
    /// </summary>
    internal static Action<string>? PreSwapBarrierForTests;

    public void ReplaceIfExpected(
        string physicalRoot,
        string targetPath,
        ExpectedFileState expected,
        ReadOnlySpan<byte> candidateBytes,
        DurableFileClass fileClass)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(expected);

        string fullTarget = Path.GetFullPath(targetPath);
        string directory = Path.GetDirectoryName(fullTarget)
            ?? throw new ArgumentException($"Invalid directory for target path '{targetPath}'.", nameof(targetPath));

        Directory.CreateDirectory(directory);

        if (expected.Kind == ExpectedFileStateKind.Missing)
        {
            ReplaceExpectingMissing(physicalRoot, directory, fullTarget, candidateBytes, fileClass);
            return;
        }

        ReplaceExpectingPresent(
            physicalRoot,
            directory,
            fullTarget,
            expected.ExpectedSha256Hex!,
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
        string stagePath = Path.Combine(
            directory,
            $".prompthelper-tmp-{WindowsDurableAtomicFileWriter.GetClassTag(fileClass)}-{Guid.NewGuid():N}.tmp");

        using var stage = WindowsOwnedDurableStage.CreateNewUnderRoot(stagePath, physicalRoot);
        RecordOwnership(physicalRoot, new OwnedArtifactRecord(
            OwnedArtifactKind.Stage,
            Relative(physicalRoot, stagePath),
            stage.Identity,
            RestoreRelativePath: null));

        try
        {
            stage.Write(candidateBytes);
            stage.FlushDurable();

            PreSwapBarrierForTests?.Invoke(fullTarget);

            stage.PromoteNoOverwriteExact(fullTarget);
        }
        catch (Exception ex)
        {
            DeleteStageQuietly(stage);

            if (IsNameOccupied(ex))
            {
                throw new StaleExpectedFileException(
                    $"'{fullTarget}' was expected not to exist, but something was created there first. It was preserved.",
                    ex);
            }

            throw;
        }
    }

    private void ReplaceExpectingPresent(
        string physicalRoot,
        string directory,
        string fullTarget,
        string expectedSha256Hex,
        ReadOnlySpan<byte> candidateBytes,
        DurableFileClass fileClass)
    {
        using WindowsExpectedTargetAuthority? authority =
            WindowsExpectedTargetAuthority.Open(fullTarget, physicalRoot);

        if (authority is null)
        {
            throw new StaleExpectedFileException(
                $"'{fullTarget}' was expected to exist with a known content, but it is gone. Reload before editing.");
        }

        authority.AssertContentMatches(expectedSha256Hex);

        string targetFileName = Path.GetFileName(fullTarget);
        string stagePath = Path.Combine(
            directory,
            $".prompthelper-tmp-{WindowsDurableAtomicFileWriter.GetClassTag(fileClass)}-{Guid.NewGuid():N}.tmp");
        string preimagePath = Path.Combine(
            directory,
            $".prompthelper-preimage-{targetFileName}-{Guid.NewGuid():N}.tmp");

        using var stage = WindowsOwnedDurableStage.CreateNewUnderRoot(stagePath, physicalRoot);
        RecordOwnership(physicalRoot, new OwnedArtifactRecord(
            OwnedArtifactKind.Stage,
            Relative(physicalRoot, stagePath),
            stage.Identity,
            RestoreRelativePath: null));

        try
        {
            stage.Write(candidateBytes);
            stage.FlushDurable();
        }
        catch
        {
            DeleteStageQuietly(stage);
            throw;
        }

        PreSwapBarrierForTests?.Invoke(fullTarget);

        // Claim the pre-image durably *before* it exists: a crash between the two renames
        // below leaves the committed data under the pre-image name, and recovery must be able
        // to prove that object is ours and restore it (see CasPreimageReconciler).
        RecordOwnership(physicalRoot, new OwnedArtifactRecord(
            OwnedArtifactKind.CasPreimage,
            Relative(physicalRoot, preimagePath),
            authority.Identity,
            RestoreRelativePath: Relative(physicalRoot, fullTarget)));

        if (!authority.RenameExactNoOverwrite(preimagePath, out int sidelineError))
        {
            DeleteStageQuietly(stage);

            if (sidelineError is ERROR_ACCESS_DENIED or ERROR_SHARING_VIOLATION)
            {
                // The verified object is no longer linked at the target, or another handle
                // blocks its rename: the expectation cannot be honoured atomically.
                throw new StaleExpectedFileException(
                    $"'{fullTarget}' changed outside the current state. Reload before editing.",
                    new Win32Exception(sidelineError));
            }

            throw new IOException(
                $"Unable to move the current '{fullTarget}' aside for atomic replacement.",
                new Win32Exception(sidelineError));
        }

        try
        {
            stage.PromoteNoOverwriteExact(fullTarget);
        }
        catch (Exception promoteFailure)
        {
            DeleteStageQuietly(stage);

            if (authority.RenameExactNoOverwrite(fullTarget, out _))
            {
                if (IsNameOccupied(promoteFailure))
                {
                    throw new StaleExpectedFileException(
                        $"'{fullTarget}' changed outside the current state. Reload before editing.",
                        promoteFailure);
                }

                throw;
            }

            // Restoring failed too, which means something occupies the target name. The
            // pre-image is deliberately left on disk with its ownership record: it holds the
            // previous committed content and destroying it here would lose data.
            throw new IOException(
                $"'{fullTarget}' was replaced by another process during an atomic update. The previous content was preserved as '{Path.GetFileName(preimagePath)}'.",
                promoteFailure);
        }

        // The swap is committed. The pre-image is ours by construction (same retained handle),
        // so it can be destroyed without consulting the journal.
        authority.DeleteExact();
    }

    private void RecordOwnership(string physicalRoot, OwnedArtifactRecord record)
    {
        _ownedArtifacts.Record(physicalRoot, record);
    }

    private static string Relative(string physicalRoot, string fullPath) =>
        Path.GetRelativePath(Path.GetFullPath(physicalRoot), Path.GetFullPath(fullPath));

    private static void DeleteStageQuietly(WindowsOwnedDurableStage stage)
    {
        try
        {
            stage.DeleteExact();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The caller is already failing; a leaked stage is recoverable from its
            // ownership record, an obscured root cause is not.
        }
    }

    private static bool IsNameOccupied(Exception ex)
    {
        return ex is IOException io &&
               io.InnerException is Win32Exception win32 &&
               win32.NativeErrorCode is ERROR_FILE_EXISTS or ERROR_ALREADY_EXISTS or ERROR_ACCESS_DENIED;
    }
}
