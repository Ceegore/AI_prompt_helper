using System;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;

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
/// <item>Durably record the pre-image about to be created <b>together with the candidate's
/// hash and length</b>, so a crash inside the two-rename window below is recoverable by
/// evidence rather than by guessing.</item>
/// <item>Rename the authority object aside — the exclusion is consumed here, by the exact
/// object that was verified.</item>
/// <item>Promote the stage into the vacated name with <i>no-overwrite</i> semantics. If
/// anything at all occupies the target by then, this fails and nothing is destroyed.</item>
/// <item>Record that the candidate is published, then delete the pre-image.</item>
/// </list>
/// If step 6 fails, the pre-image is renamed back; if that also fails the pre-image is left on
/// disk with its journal record intact. No path in this method ever destroys an object it did
/// not create or prove.</para>
/// <para><b>ExpectedMissing.</b> A stage is promoted with no-overwrite semantics, so "must
/// still be missing" is enforced by the promotion itself.</para>
/// <para><b>Why the candidate hash is recorded (CRUU16-001).</b> The pre-image is the only
/// durable copy of the last committed state while the swap is in flight. Recovery must be able
/// to tell "our candidate reached the target" from "some file happens to be at the target"
/// before it retires that copy. Recording the candidate's content up front is what makes that
/// decidable; the previous design inferred commit from pathname occupancy and could therefore
/// destroy committed data after a crash.</para>
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
    /// the real production primitive. Never set outside tests.
    /// </summary>
    internal static Action<string>? PreSwapBarrierForTests;

    /// <summary>
    /// Fired after the pre-image has been moved aside but before the candidate is promoted —
    /// the crash window CRUU16-001 is about. Tests use it to simulate a process death at
    /// exactly that cut. Never set outside tests.
    /// </summary>
    internal static Action<string>? BetweenRenamesForTests;

    public void ReplaceIfExpected(
        string physicalRoot,
        string targetPath,
        ExpectedFileState expected,
        ReadOnlySpan<byte> candidateBytes,
        DurableFileClass fileClass)
        => ReplaceIfExpected(physicalRoot, targetPath, expected, candidateBytes, fileClass, recordOwnership: true);

    /// <summary>
    /// <paramref name="recordOwnership"/> is false only for the ownership ledger itself, which
    /// cannot appear in its own records without recursing. A crash during that particular swap
    /// therefore leaves an unproven orphan, which reconciliation preserves rather than deletes.
    /// </summary>
    internal void ReplaceIfExpected(
        string physicalRoot,
        string targetPath,
        ExpectedFileState expected,
        ReadOnlySpan<byte> candidateBytes,
        DurableFileClass fileClass,
        bool recordOwnership)
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
            ReplaceExpectingMissing(physicalRoot, directory, fullTarget, candidateBytes, fileClass, recordOwnership);
            return;
        }

        ReplaceExpectingPresent(
            physicalRoot,
            directory,
            fullTarget,
            expected.ExpectedSha256Hex!,
            candidateBytes,
            fileClass,
            recordOwnership);
    }

    private void ReplaceExpectingMissing(
        string physicalRoot,
        string directory,
        string fullTarget,
        ReadOnlySpan<byte> candidateBytes,
        DurableFileClass fileClass,
        bool recordOwnership)
    {
        Guid operationId = Guid.NewGuid();
        string stagePath = Path.Combine(
            directory,
            $".prompthelper-tmp-{WindowsDurableAtomicFileWriter.GetClassTag(fileClass)}-{Guid.NewGuid():N}.tmp");

        using var stage = WindowsOwnedDurableStage.CreateNewUnderRoot(stagePath, physicalRoot);

        if (recordOwnership)
        {
            _ownedArtifacts.Record(physicalRoot, new OwnedArtifactRecord(
                operationId,
                OwnedArtifactKind.Stage,
                OwnedArtifactPhase.Claimed,
                Relative(physicalRoot, stagePath),
                stage.Identity));
        }

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
        DurableFileClass fileClass,
        bool recordOwnership)
    {
        using WindowsExpectedTargetAuthority? authority =
            WindowsExpectedTargetAuthority.Open(fullTarget, physicalRoot);

        if (authority is null)
        {
            throw new StaleExpectedFileException(
                $"'{fullTarget}' was expected to exist with a known content, but it is gone. Reload before editing.");
        }

        authority.AssertContentMatches(expectedSha256Hex);

        Guid operationId = Guid.NewGuid();
        string targetFileName = Path.GetFileName(fullTarget);
        string candidateSha256Hex = Convert.ToHexStringLower(SHA256.HashData(candidateBytes));
        long candidateLength = candidateBytes.Length;

        string stagePath = Path.Combine(
            directory,
            $".prompthelper-tmp-{WindowsDurableAtomicFileWriter.GetClassTag(fileClass)}-{Guid.NewGuid():N}.tmp");
        string preimagePath = Path.Combine(
            directory,
            $".prompthelper-preimage-{targetFileName}-{Guid.NewGuid():N}.tmp");

        using var stage = WindowsOwnedDurableStage.CreateNewUnderRoot(stagePath, physicalRoot);

        if (recordOwnership)
        {
            _ownedArtifacts.Record(physicalRoot, new OwnedArtifactRecord(
                operationId,
                OwnedArtifactKind.Stage,
                OwnedArtifactPhase.Claimed,
                Relative(physicalRoot, stagePath),
                stage.Identity));
        }

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

        // Claim the pre-image durably *before* it exists, carrying the candidate's identity so
        // recovery can prove which side of the swap actually completed.
        if (recordOwnership)
        {
            _ownedArtifacts.Record(physicalRoot, new OwnedArtifactRecord(
                operationId,
                OwnedArtifactKind.CasPreimage,
                OwnedArtifactPhase.PreimageSidelined,
                Relative(physicalRoot, preimagePath),
                authority.Identity,
                Relative(physicalRoot, fullTarget),
                candidateSha256Hex,
                candidateLength));
        }

        if (!authority.RenameExactNoOverwrite(preimagePath, out int sidelineError))
        {
            DeleteStageQuietly(stage);

            if (sidelineError is ERROR_ACCESS_DENIED or ERROR_SHARING_VIOLATION)
            {
                throw new StaleExpectedFileException(
                    $"'{fullTarget}' changed outside the current state. Reload before editing.",
                    new Win32Exception(sidelineError));
            }

            throw new IOException(
                $"Unable to move the current '{fullTarget}' aside for atomic replacement.",
                new Win32Exception(sidelineError));
        }

        BetweenRenamesForTests?.Invoke(fullTarget);

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

            throw new IOException(
                $"'{fullTarget}' was replaced by another process during an atomic update. The previous content was preserved as '{Path.GetFileName(preimagePath)}'.",
                promoteFailure);
        }

        // The candidate is published. Recording that before retiring the pre-image means a
        // crash in between is unambiguous on restart rather than merely probable.
        if (recordOwnership)
        {
            _ownedArtifacts.Record(physicalRoot, new OwnedArtifactRecord(
                operationId,
                OwnedArtifactKind.CasPreimage,
                OwnedArtifactPhase.CandidatePublished,
                Relative(physicalRoot, preimagePath),
                authority.Identity,
                Relative(physicalRoot, fullTarget),
                candidateSha256Hex,
                candidateLength));
        }

        authority.DeleteExact();
    }

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

    private static string Relative(string physicalRoot, string fullPath) =>
        Path.GetRelativePath(Path.GetFullPath(physicalRoot), Path.GetFullPath(fullPath));
}
