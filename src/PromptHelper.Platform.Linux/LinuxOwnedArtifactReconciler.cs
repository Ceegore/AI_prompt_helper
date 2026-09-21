using System.Security.Cryptography;

namespace PromptHelper.Services;

/// <summary>
/// Linux startup recovery for ownership-journal claims that are already implemented by the
/// Linux platform: durable-write stages and compare-and-swap pre-images.
///
/// Unsupported ownership kinds fail closed and remain in the journal. This deliberately keeps
/// migration/capability authority out of Linux product composition until their Linux primitives
/// are implemented as well.
/// </summary>
internal static class LinuxOwnedArtifactReconciler
{
    private const string LinuxIdentityScheme = "linux-statx-v1";

    internal sealed record Result(
        IReadOnlySet<string> ProvenOwnedPaths,
        IReadOnlyList<ReconciliationOutcome> Outcomes)
    {
        public bool HasFatal =>
            Outcomes.Any(outcome =>
                outcome.Severity == ReconciliationSeverity.Fatal);
    }

    public static Result Reconcile(
        string physicalRoot,
        IOwnedArtifactJournal journal)
    {
        LinuxNativeFileSystem.EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);
        ArgumentNullException.ThrowIfNull(journal);

        string root =
            new LinuxPhysicalPathResolver()
                .ResolveWithNearestExistingAncestor(physicalRoot);

        var proven =
            new HashSet<string>(StringComparer.Ordinal);
        var outcomes =
            new List<ReconciliationOutcome>();

        OwnedArtifactJournalSnapshot snapshot;
        try
        {
            snapshot = journal.Read(root);
        }
        catch (OwnedArtifactJournalCorruptException ex)
        {
            outcomes.Add(Fatal(
                "OWNERSHIP_JOURNAL_CORRUPT",
                OwnedArtifactJournalPaths.GetJournalPath(root),
                ex.Message));
            return new Result(proven, outcomes);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            outcomes.Add(Fatal(
                "OWNERSHIP_JOURNAL_UNREADABLE",
                OwnedArtifactJournalPaths.GetJournalPath(root),
                ex.Message));
            return new Result(proven, outcomes);
        }

        if (snapshot.Records.Count == 0)
        {
            if (snapshot.Exists)
            {
                TryRewrite(
                    root,
                    journal,
                    snapshot,
                    [],
                    outcomes);
            }

            return new Result(proven, outcomes);
        }

        var surviving =
            new List<OwnedArtifactRecord>();

        // A foreign-platform identity is not comparable to a Linux statx identity. Keeping
        // the complete ledger is the only safe action; this also protects a data folder moved
        // from Windows while an old Windows transaction was still in flight.
        OwnedArtifactRecord? unsupportedIdentity =
            snapshot.Records.FirstOrDefault(record =>
                !string.Equals(
                    record.Identity.Scheme,
                    LinuxIdentityScheme,
                    StringComparison.Ordinal));

        if (unsupportedIdentity is not null)
        {
            outcomes.Add(Fatal(
                "OWNERSHIP_IDENTITY_SCHEME_UNSUPPORTED",
                OwnedArtifactJournalPaths.GetJournalPath(root),
                $"Recovery record uses identity scheme '{unsupportedIdentity.Identity.Scheme}', which cannot be proven on Linux. The ledger and all artifacts were preserved."));
            return new Result(proven, outcomes);
        }

        foreach (IGrouping<Guid, OwnedArtifactRecord> transaction in
                 snapshot.Records
                     .Where(record =>
                         record.Kind == OwnedArtifactKind.CasPreimage)
                     .GroupBy(record => record.OperationId))
        {
            OwnedArtifactRecord latest =
                transaction
                    .OrderByDescending(record => record.Phase)
                    .First();

            ResolveCas(
                root,
                latest,
                outcomes,
                surviving,
                proven);
        }

        foreach (OwnedArtifactRecord record in
                 snapshot.Records.Where(record =>
                     record.Kind == OwnedArtifactKind.Stage))
        {
            ResolveStage(
                root,
                record,
                outcomes,
                surviving,
                proven);
        }

        foreach (OwnedArtifactRecord record in
                 snapshot.Records.Where(record =>
                     record.Kind is not OwnedArtifactKind.Stage and
                     not OwnedArtifactKind.CasPreimage))
        {
            outcomes.Add(Fatal(
                "OWNERSHIP_KIND_NOT_IMPLEMENTED_ON_LINUX",
                ResolveRelative(root, record.RelativePath),
                $"Linux recovery does not yet implement ownership kind '{record.Kind}'. The record and artifact were preserved."));
            surviving.Add(record);
        }

        if (!outcomes.Any(outcome =>
                outcome.Severity == ReconciliationSeverity.Fatal))
        {
            TryRewrite(
                root,
                journal,
                snapshot,
                surviving,
                outcomes);
        }

        return new Result(proven, outcomes);
    }

    private static void ResolveStage(
        string root,
        OwnedArtifactRecord record,
        List<ReconciliationOutcome> outcomes,
        List<OwnedArtifactRecord> surviving,
        HashSet<string> proven)
    {
        string path = ResolveRelative(root, record.RelativePath);

        try
        {
            LinuxExactRetirementOutcome result =
                LinuxExactFileRetirement.Retire(
                    root,
                    path,
                    record.Identity);

            switch (result)
            {
                case LinuxExactRetirementOutcome.Retired:
                    proven.Add(path);
                    outcomes.Add(Notice(
                        "STAGE_RETIRED",
                        path,
                        "A proven-owned interrupted Linux staging file was retired."));
                    break;

                case LinuxExactRetirementOutcome.Missing:
                    break;

                case LinuxExactRetirementOutcome.ForeignPreserved:
                    outcomes.Add(Notice(
                        "STAGE_REPLACED",
                        path,
                        "A different object occupies the recorded stage path and was preserved."));
                    break;

                case LinuxExactRetirementOutcome.ConflictPreserved:
                    outcomes.Add(Fatal(
                        "STAGE_RETIRE_CONFLICT",
                        path,
                        "A foreign replacement was preserved during exact stage retirement but could not be restored to its original pathname."));
                    surviving.Add(record);
                    break;
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            StaleExpectedFileException)
        {
            outcomes.Add(new ReconciliationOutcome(
                ReconciliationSeverity.Warning,
                "STAGE_RETIRE_FAILED",
                path,
                ex.Message));
            surviving.Add(record);
        }
    }

    private static void ResolveCas(
        string root,
        OwnedArtifactRecord record,
        List<ReconciliationOutcome> outcomes,
        List<OwnedArtifactRecord> surviving,
        HashSet<string> proven)
    {
        if (record.RestoreRelativePath is null ||
            record.CandidateSha256Hex is null ||
            record.CandidateLength < 0)
        {
            outcomes.Add(Fatal(
                "CAS_AUTHORITY_INCOMPLETE",
                ResolveRelative(root, record.RelativePath),
                "The Linux CAS recovery record lacks target or candidate authority."));
            surviving.Add(record);
            return;
        }

        string preimagePath =
            ResolveRelative(root, record.RelativePath);
        string targetPath =
            ResolveRelative(root, record.RestoreRelativePath);

        if (record.Phase == OwnedArtifactPhase.CandidatePublished)
        {
            RetireCommittedPreimage(
                root,
                preimagePath,
                record,
                outcomes,
                surviving,
                proven);
            return;
        }

        FileState preimage =
            Inspect(
                root,
                preimagePath,
                record.Identity,
                record.PreviousSha256Hex,
                expectedLength: -1);

        FileState target =
            Inspect(
                root,
                targetPath,
                record.Identity,
                record.PreviousSha256Hex,
                expectedLength: -1);

        bool targetMatchesCandidate =
            target.Exists &&
            target.Matches(
                record.CandidateSha256Hex,
                record.CandidateLength);

        bool targetMatchesOld =
            target.Exists &&
            target.IdentityMatches &&
            (record.PreviousSha256Hex is null ||
             target.ContentMatchesExpected);

        bool preimageMatchesOld =
            preimage.Exists &&
            preimage.IdentityMatches &&
            (record.PreviousSha256Hex is null ||
             preimage.ContentMatchesExpected);

        // Prepared is durable before the first rename. If the exact old object is still at the
        // target, no destructive part of the swap occurred.
        if (record.Phase == OwnedArtifactPhase.Prepared &&
            targetMatchesOld &&
            !preimageMatchesOld)
        {
            if (preimage.Exists)
            {
                outcomes.Add(Notice(
                    "CAS_PREIMAGE_FOREIGN",
                    preimagePath,
                    "A foreign object occupies the declared pre-image path and was preserved; the original target never moved."));
            }

            return;
        }

        // Promotion failure may restore the pre-image after the Sidelined phase was recorded.
        if (record.Phase == OwnedArtifactPhase.PreimageSidelined &&
            targetMatchesOld &&
            !preimageMatchesOld)
        {
            return;
        }

        if (preimageMatchesOld)
        {
            if (!target.Exists)
            {
                if (TryRestoreExactPreimage(
                        root,
                        preimagePath,
                        targetPath,
                        record,
                        out string? restoreError))
                {
                    proven.Add(targetPath);
                    outcomes.Add(Notice(
                        "CAS_PREIMAGE_RESTORED",
                        targetPath,
                        "An interrupted Linux atomic replacement was rolled back to the last committed content."));
                    return;
                }

                outcomes.Add(Fatal(
                    "CAS_PREIMAGE_RESTORE_FAILED",
                    preimagePath,
                    restoreError ??
                    "The previous committed content could not be restored."));
                surviving.Add(record);
                return;
            }

            if (targetMatchesCandidate)
            {
                LinuxExactRetirementOutcome retirement =
                    LinuxExactFileRetirement.Retire(
                        root,
                        preimagePath,
                        record.Identity,
                        record.PreviousSha256Hex);

                if (retirement == LinuxExactRetirementOutcome.Retired ||
                    retirement == LinuxExactRetirementOutcome.Missing)
                {
                    proven.Add(preimagePath);
                    outcomes.Add(Notice(
                        "CAS_COMPLETED_DURING_RECOVERY",
                        targetPath,
                        "The candidate was already published; its previous committed pre-image was retired."));
                    return;
                }

                outcomes.Add(Fatal(
                    "CAS_PREIMAGE_RETIRE_CONFLICT",
                    preimagePath,
                    "The candidate appears published, but the recorded pre-image could not be retired without risking a foreign object."));
                surviving.Add(record);
                return;
            }

            outcomes.Add(Fatal(
                "CAS_AMBIGUOUS",
                targetPath,
                "A different object occupies the target while the exact previous committed content survives at the pre-image path. Both were preserved."));
            surviving.Add(record);
            return;
        }

        // With no exact pre-image, a Sidelined record plus candidate bytes is still ambiguous:
        // this implementation never retires the pre-image before CandidatePublished is durable.
        if (targetMatchesCandidate)
        {
            outcomes.Add(Fatal(
                "CAS_PREIMAGE_MISSING",
                targetPath,
                "Candidate bytes are present, but the exact previous committed pre-image is unavailable before a committed phase record. Automatic recovery cannot prove the transaction completed."));
            surviving.Add(record);
            return;
        }

        if (preimage.Exists)
        {
            outcomes.Add(Fatal(
                "CAS_PREIMAGE_REPLACED",
                preimagePath,
                "A different object occupies the recorded pre-image path. It was preserved."));
        }
        else
        {
            outcomes.Add(Fatal(
                target.Exists
                    ? "CAS_AMBIGUOUS"
                    : "CAS_UNRECOVERABLE",
                target.Exists ? targetPath : preimagePath,
                target.Exists
                    ? "Neither the target nor the pre-image can be proven as the recorded previous committed object."
                    : "Both the target and the recorded previous committed pre-image are unavailable."));
        }

        surviving.Add(record);
    }

    private static void RetireCommittedPreimage(
        string root,
        string preimagePath,
        OwnedArtifactRecord record,
        List<ReconciliationOutcome> outcomes,
        List<OwnedArtifactRecord> surviving,
        HashSet<string> proven)
    {
        try
        {
            LinuxExactRetirementOutcome result =
                LinuxExactFileRetirement.Retire(
                    root,
                    preimagePath,
                    record.Identity,
                    record.PreviousSha256Hex);

            if (result is
                LinuxExactRetirementOutcome.Retired or
                LinuxExactRetirementOutcome.Missing)
            {
                if (result == LinuxExactRetirementOutcome.Retired)
                {
                    proven.Add(preimagePath);
                }

                return;
            }

            // Once CandidatePublished is durable, later target evolution is valid and the
            // transaction must never be reopened. A foreign object at the old pre-image name
            // is not transaction authority and is simply preserved.
            outcomes.Add(Notice(
                "CAS_PREIMAGE_REPLACED_AFTER_COMMIT",
                preimagePath,
                "A different object occupies a completed transaction's old pre-image path and was preserved."));
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            StaleExpectedFileException)
        {
            outcomes.Add(new ReconciliationOutcome(
                ReconciliationSeverity.Warning,
                "CAS_PREIMAGE_RETIRE_FAILED",
                preimagePath,
                ex.Message));
            surviving.Add(record);
        }
    }

    private static bool TryRestoreExactPreimage(
        string root,
        string preimagePath,
        string targetPath,
        OwnedArtifactRecord record,
        out string? error)
    {
        error = null;

        using LinuxExpectedTargetAuthority? original =
            LinuxExpectedTargetAuthority.Open(preimagePath, root);

        if (original is null ||
            original.Identity.ToObjectIdentity() != record.Identity)
        {
            error = "The recorded pre-image disappeared or changed identity.";
            return false;
        }

        if (record.PreviousSha256Hex is not null)
        {
            original.AssertContentMatches(record.PreviousSha256Hex);
        }

        string directory = Path.GetDirectoryName(preimagePath)
            ?? throw new InvalidOperationException(
                $"Pre-image has no parent: '{preimagePath}'.");

        string recoveryClaim = Path.Combine(
            directory,
            $".prompthelper-recovery-claim-{Guid.NewGuid():N}.tmp");

        if (!LinuxNativeFileSystem.TryRenameNoReplace(
                preimagePath,
                recoveryClaim,
                out int claimError))
        {
            error =
                $"Unable to claim the exact pre-image for recovery (errno {claimError}).";
            return false;
        }

        LinuxNativeFileSystem.FlushDirectory(directory);

        using LinuxExpectedTargetAuthority? claimed =
            LinuxExpectedTargetAuthority.Open(recoveryClaim, root);

        if (claimed is null ||
            claimed.Identity.ToObjectIdentity() != record.Identity)
        {
            _ = LinuxNativeFileSystem.TryRenameNoReplace(
                recoveryClaim,
                preimagePath,
                out _);
            LinuxNativeFileSystem.FlushDirectory(directory);
            error =
                "The object moved into the recovery claim is not the recorded pre-image; it was preserved.";
            return false;
        }

        if (record.PreviousSha256Hex is not null)
        {
            claimed.AssertContentMatches(record.PreviousSha256Hex);
        }

        if (!LinuxNativeFileSystem.TryRenameNoReplace(
                recoveryClaim,
                targetPath,
                out int restoreError))
        {
            _ = LinuxNativeFileSystem.TryRenameNoReplace(
                recoveryClaim,
                preimagePath,
                out _);
            LinuxNativeFileSystem.FlushDirectory(directory);
            error =
                $"The target was occupied before recovery could restore the pre-image (errno {restoreError}).";
            return false;
        }

        LinuxNativeFileSystem.FlushDirectory(directory);

        using LinuxExpectedTargetAuthority? restored =
            LinuxExpectedTargetAuthority.Open(targetPath, root);

        if (restored is null ||
            restored.Identity.ToObjectIdentity() != record.Identity)
        {
            error =
                "The restored target does not have the recorded pre-image identity.";
            return false;
        }

        if (record.PreviousSha256Hex is not null)
        {
            restored.AssertContentMatches(record.PreviousSha256Hex);
        }

        return true;
    }

    private static FileState Inspect(
        string root,
        string path,
        FileObjectIdentity expectedIdentity,
        string? expectedSha256Hex,
        long expectedLength)
    {
        try
        {
            using LinuxExpectedTargetAuthority? authority =
                LinuxExpectedTargetAuthority.Open(path, root);

            if (authority is null)
            {
                return FileState.Missing;
            }

            byte[] bytes = authority.ReadAllBytes();
            string hash =
                Convert.ToHexStringLower(SHA256.HashData(bytes));

            return new FileState(
                true,
                authority.Identity.ToObjectIdentity() == expectedIdentity,
                expectedSha256Hex is not null &&
                string.Equals(
                    hash,
                    expectedSha256Hex,
                    StringComparison.OrdinalIgnoreCase),
                bytes.LongLength,
                hash);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            throw new IOException(
                $"Unable to inspect Linux recovery path '{path}'.",
                ex);
        }
    }

    private readonly record struct FileState(
        bool Exists,
        bool IdentityMatches,
        bool ContentMatchesExpected,
        long Length,
        string? Sha256Hex)
    {
        public static FileState Missing { get; } =
            new(false, false, false, -1, null);

        public bool Matches(
            string expectedSha256Hex,
            long expectedLength) =>
            Exists &&
            Length == expectedLength &&
            string.Equals(
                Sha256Hex,
                expectedSha256Hex,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveRelative(
        string root,
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException(
                $"Ownership record contains an unsafe relative path '{relativePath}'.");
        }

        string full =
            Path.GetFullPath(Path.Combine(root, relativePath));

        if (!PathIdentity.IsStrictDescendant(full, root))
        {
            throw new InvalidDataException(
                $"Ownership record escapes the managed root: '{relativePath}'.");
        }

        return full;
    }

    private static void TryRewrite(
        string root,
        IOwnedArtifactJournal journal,
        OwnedArtifactJournalSnapshot snapshot,
        IReadOnlyList<OwnedArtifactRecord> surviving,
        List<ReconciliationOutcome> outcomes)
    {
        try
        {
            journal.Rewrite(root, snapshot, surviving);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            StaleExpectedFileException)
        {
            outcomes.Add(new ReconciliationOutcome(
                ReconciliationSeverity.Warning,
                "OWNERSHIP_JOURNAL_REWRITE_FAILED",
                OwnedArtifactJournalPaths.GetJournalPath(root),
                ex.Message));
        }
    }

    private static ReconciliationOutcome Fatal(
        string code,
        string path,
        string message) =>
        new(
            ReconciliationSeverity.Fatal,
            code,
            path,
            message);

    private static ReconciliationOutcome Notice(
        string code,
        string path,
        string message) =>
        new(
            ReconciliationSeverity.Notice,
            code,
            path,
            message);
}
