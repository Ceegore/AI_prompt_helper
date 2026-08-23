using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PromptHelper.Services;

/// <summary>
/// Exact-object authority for the migration marker lifecycle. The final marker pathname is
/// never exposed until a complete candidate is durable, and every replace/delete operation
/// requires the NTFS identity recorded when that candidate was created.
/// </summary>
internal static class WindowsMigrationMarkerAuthority
{
    internal static Action<string>? BeforeReadyPublishForTests;
    internal static Action<string>? BeforeReadyCandidateDeleteForTests;

    public static void CreateInitial(
        string physicalRoot,
        string markerPath,
        Guid attemptId,
        MigrationManifestPhase phase,
        ReadOnlySpan<byte> bytes,
        IOwnedArtifactJournal journal)
    {
        ProductionRuntimeEvidence.Hit("WindowsMigrationMarkerAuthority.CreateInitial");
        string root = NormalizeRootAndMarker(physicalRoot, markerPath, out string marker);
        string stagePath = Path.Combine(root, $".prompthelper-migration.initial-{attemptId:N}.tmp");
        Guid authorityId = Guid.NewGuid();
        string hash = Sha(bytes);

        using WindowsOwnedDurableStage stage =
            WindowsOwnedDurableStage.CreateCrashAtomicBootstrapUnderRoot(stagePath, root);
        ProductionCrashCut.Hit("WindowsMigrationMarkerAuthority.InitialAfterCreateBeforeWrite");

        bool promoted = false;
        try
        {
            if (ProductionCrashCut.IsArmed("WindowsMigrationMarkerAuthority.InitialDuringWrite"))
            {
                int partialLength = Math.Max(1, bytes.Length / 2);
                stage.Write(bytes[..partialLength]);
                ProductionCrashCut.Hit("WindowsMigrationMarkerAuthority.InitialDuringWrite");
            }

            stage.Write(bytes);
            ProductionCrashCut.Hit("WindowsMigrationMarkerAuthority.InitialAfterWriteBeforeFlush");
            stage.FlushDurable();
            ProductionCrashCut.Hit("WindowsMigrationMarkerAuthority.InitialAfterFlushBeforeCommit");

            journal.Record(root, MarkerRecord(
                root,
                authorityId,
                attemptId,
                OwnedArtifactPhase.MarkerPrepared,
                stagePath,
                marker,
                stage.Identity,
                bytes.Length,
                hash));
            stage.PersistAfterDurableClaim();

            stage.PromoteNoOverwriteExact(marker);
            promoted = true;
            ProductionCrashCut.Hit("WindowsMigrationMarkerAuthority.InitialAfterCommit");

            journal.Record(root, MarkerRecord(
                root,
                authorityId,
                attemptId,
                phase == MigrationManifestPhase.ReadyToCommit
                    ? OwnedArtifactPhase.MarkerPublishedReady
                    : OwnedArtifactPhase.MarkerPublishedCopying,
                stagePath,
                marker,
                stage.Identity,
                bytes.Length,
                hash));
        }
        catch
        {
            if (!promoted)
            {
                TryDelete(stage);
            }

            // Once promotion occurred the complete marker and its prepared identity record
            // are recovery authority. Never erase that committed state because a later phase
            // append failed.
            throw;
        }
    }

    public static void ReplaceIfExpected(
        string physicalRoot,
        string markerPath,
        Guid attemptId,
        ReadOnlySpan<byte> expectedBytes,
        ReadOnlySpan<byte> candidateBytes,
        IOwnedArtifactJournal journal)
    {
        ProductionRuntimeEvidence.Hit("WindowsMigrationMarkerAuthority.ReplaceIfExpected");
        string root = NormalizeRootAndMarker(physicalRoot, markerPath, out string marker);
        OwnedArtifactJournalSnapshot snapshot = journal.Read(root);

        using WindowsExpectedTargetAuthority current =
            WindowsExpectedTargetAuthority.Open(marker, root)
            ?? throw new StaleExpectedFileException(
                $"The Copying migration marker disappeared before Ready publication: '{marker}'.");

        OwnedArtifactRecord currentRecord = RequireAuthority(
            snapshot,
            root,
            marker,
            attemptId,
            current.Identity,
            expectedBytes,
            phase => phase is OwnedArtifactPhase.MarkerPrepared or
                OwnedArtifactPhase.MarkerPublishedCopying or
                OwnedArtifactPhase.MarkerCopyingRetirePrepared);
        current.AssertContentMatches(Sha(expectedBytes));

        string stagePath = Path.Combine(root, $".prompthelper-migration.stage-{attemptId:N}.tmp");
        string preimagePath = Path.Combine(root, $".prompthelper-migration.copying-{attemptId:N}.tmp");
        Guid candidateAuthorityId = Guid.NewGuid();
        string candidateHash = Sha(candidateBytes);

        using WindowsOwnedDurableStage candidate =
            WindowsOwnedDurableStage.CreateCrashAtomicBootstrapUnderRoot(stagePath, root);
        bool currentSidelined = false;
        bool candidatePublished = false;
        try
        {
            candidate.Write(candidateBytes);
            candidate.FlushDurable();
            journal.Record(root, MarkerRecord(
                root,
                candidateAuthorityId,
                attemptId,
                OwnedArtifactPhase.MarkerReadyPrepared,
                stagePath,
                marker,
                candidate.Identity,
                candidateBytes.Length,
                candidateHash));
            candidate.PersistAfterDurableClaim();
            BeforeReadyPublishForTests?.Invoke(stagePath);

            journal.Record(root, MarkerRecord(
                root,
                currentRecord.OperationId,
                attemptId,
                OwnedArtifactPhase.MarkerCopyingRetirePrepared,
                preimagePath,
                marker,
                current.Identity,
                expectedBytes.Length,
                Sha(expectedBytes)));

            if (!current.RenameExactNoOverwrite(preimagePath, out int sidelineError))
            {
                throw new IOException(
                    $"Unable to sideline the exact Copying migration marker '{marker}'.",
                    new System.ComponentModel.Win32Exception(sidelineError));
            }
            currentSidelined = true;
            ProductionCrashCut.Hit("WindowsMigrationMarkerAuthority.AfterCopyingSidelineBeforeReadyPublish");

            candidate.PromoteNoOverwriteExact(marker);
            candidatePublished = true;
            ProductionCrashCut.Hit("WindowsMigrationMarkerAuthority.AfterReadyPublishBeforeAuthorityAdvance");

            journal.Record(root, MarkerRecord(
                root,
                candidateAuthorityId,
                attemptId,
                OwnedArtifactPhase.MarkerPublishedReady,
                stagePath,
                marker,
                candidate.Identity,
                candidateBytes.Length,
                candidateHash));

            current.DeleteExact();
        }
        catch (Exception primaryFailure)
        {
            if (!candidatePublished)
            {
                if (currentSidelined)
                {
                    _ = current.RenameExactNoOverwrite(marker, out _);
                }
                Exception? cleanupFailure = TryDeleteReadyCandidate(candidate, stagePath);
                if (cleanupFailure is not null)
                {
                    throw new ManifestWriteCleanupException(
                        marker,
                        stagePath,
                        primaryFailure,
                        cleanupFailure);
                }
            }

            throw;
        }
    }

    public static void AssertCurrent(
        string physicalRoot,
        string markerPath,
        Guid attemptId,
        ReadOnlySpan<byte> expectedBytes,
        IOwnedArtifactJournal journal)
    {
        ProductionRuntimeEvidence.Hit("WindowsMigrationMarkerAuthority.AssertCurrent");
        string root = NormalizeRootAndMarker(physicalRoot, markerPath, out string marker);
        using WindowsExpectedTargetAuthority current =
            WindowsExpectedTargetAuthority.Open(marker, root)
            ?? throw new InvalidDataException($"The migration marker disappeared: '{marker}'.");

        _ = RequireAuthority(
            journal.Read(root),
            root,
            marker,
            attemptId,
            current.Identity,
            expectedBytes,
            phase => phase is OwnedArtifactPhase.MarkerPrepared or
                OwnedArtifactPhase.MarkerPublishedCopying or
                OwnedArtifactPhase.MarkerReadyPrepared or
                OwnedArtifactPhase.MarkerPublishedReady);
        current.AssertContentMatches(Sha(expectedBytes));
    }

    public static void DeleteCurrent(
        string physicalRoot,
        string markerPath,
        Guid attemptId,
        ReadOnlySpan<byte> expectedBytes,
        IOwnedArtifactJournal journal)
    {
        ProductionRuntimeEvidence.Hit("WindowsMigrationMarkerAuthority.DeleteCurrent");
        string root = NormalizeRootAndMarker(physicalRoot, markerPath, out string marker);
        OwnedArtifactJournalSnapshot snapshot = journal.Read(root);
        using WindowsExpectedTargetAuthority? current = WindowsExpectedTargetAuthority.Open(marker, root);
        if (current is null)
        {
            return;
        }

        OwnedArtifactRecord authorityRecord = RequireAuthority(
            snapshot,
            root,
            marker,
            attemptId,
            current.Identity,
            expectedBytes,
            phase => phase is OwnedArtifactPhase.MarkerPrepared or
                OwnedArtifactPhase.MarkerPublishedCopying or
                OwnedArtifactPhase.MarkerReadyPrepared or
                OwnedArtifactPhase.MarkerPublishedReady);
        current.AssertContentMatches(Sha(expectedBytes));
        journal.Record(root, MarkerRecord(
            root,
            authorityRecord.OperationId,
            attemptId,
            OwnedArtifactPhase.MarkerRetirePrepared,
            marker,
            marker,
            current.Identity,
            expectedBytes.Length,
            Sha(expectedBytes)));
        current.DeleteExact();
    }

    private static OwnedArtifactRecord RequireAuthority(
        OwnedArtifactJournalSnapshot snapshot,
        string root,
        string marker,
        Guid attemptId,
        WindowsFileIdentity identity,
        ReadOnlySpan<byte> expectedBytes,
        Func<OwnedArtifactPhase, bool> allowedPhase)
    {
        string relativeMarker = Path.GetRelativePath(root, marker);
        string hash = Sha(expectedBytes);
        int expectedLength = expectedBytes.Length;
        OwnedArtifactRecord? record = snapshot.Records
            .Where(record =>
                record.Kind == OwnedArtifactKind.MigrationMarker &&
                record.MarkerAttemptId == attemptId &&
                record.Identity == identity &&
                record.CandidateLength == expectedLength &&
                string.Equals(record.CandidateSha256Hex, hash, StringComparison.OrdinalIgnoreCase) &&
                allowedPhase(record.Phase) &&
                (string.Equals(record.RelativePath, relativeMarker, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(record.RestoreRelativePath, relativeMarker, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(record => record.Phase)
            .FirstOrDefault();

        return record ?? throw new StaleExpectedFileException(
            $"The migration marker at '{marker}' is not the exact object durably created by attempt {attemptId}. It was preserved.");
    }

    private static OwnedArtifactRecord MarkerRecord(
        string root,
        Guid authorityId,
        Guid attemptId,
        OwnedArtifactPhase phase,
        string primaryPath,
        string alternatePath,
        WindowsFileIdentity identity,
        long length,
        string sha256Hex) =>
        new(
            authorityId,
            OwnedArtifactKind.MigrationMarker,
            phase,
            Path.GetRelativePath(root, primaryPath),
            identity,
            Path.GetRelativePath(root, alternatePath),
            sha256Hex,
            length,
            attemptId);

    private static string NormalizeRootAndMarker(
        string physicalRoot,
        string markerPath,
        out string marker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPath);
        string root = Path.GetFullPath(physicalRoot);
        marker = Path.GetFullPath(markerPath);
        if (!PathIdentity.IsStrictDescendant(marker, root))
        {
            throw new InvalidDataException(
                $"Migration marker '{marker}' is not inside physical root '{root}'.");
        }
        return root;
    }

    private static string Sha(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void TryDelete(WindowsOwnedDurableStage stage)
    {
        try
        {
            stage.DeleteExact();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // The caller's primary failure remains authoritative. If a durable claim exists,
            // startup reconciliation retains exact authority for another cleanup attempt.
        }
    }

    private static Exception? TryDeleteReadyCandidate(
        WindowsOwnedDurableStage stage,
        string stagePath)
    {
        try
        {
            BeforeReadyCandidateDeleteForTests?.Invoke(stagePath);
            stage.DeleteExact();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return ex;
        }
    }
}
