using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>How a transient artifact found on disk relates to this application.</summary>
internal enum ArtifactProvenance
{
    /// <summary>A durable record exists and the object at that pathname still has the recorded identity.</summary>
    JournalOwned,

    /// <summary>Matches a current temp-name grammar but nothing proves this process created it.</summary>
    UnprovenCurrentFormat,

    /// <summary>Matches only a superseded naming convention, with no identity signal at all.</summary>
    LegacyUnverifiable,

    /// <summary>A record exists for this pathname but the object there is a different object.</summary>
    Foreign
}

/// <summary>
/// How serious a reconciliation problem is. The distinction matters because these outcomes
/// decide whether the application may go on to load state (CRUU16-004).
/// </summary>
internal enum ReconciliationSeverity
{
    /// <summary>Something was left alone on purpose. Normal, and worth reporting only as information.</summary>
    Notice,

    /// <summary>A cleanup could not be completed. Nothing committed is at risk.</summary>
    Warning,

    /// <summary>
    /// Committed state may be unaccounted for: an interrupted compare-and-swap could not be
    /// resolved, the ownership ledger is unreadable, or an authority check failed. Continuing
    /// would mean interpreting an in-flight crash window as ordinary state.
    /// </summary>
    Fatal
}

/// <summary>One thing reconciliation found, with the severity that decides what happens next.</summary>
internal sealed record ReconciliationOutcome(
    ReconciliationSeverity Severity,
    string Code,
    string Path,
    string Message);

/// <summary>
/// Startup reconciliation for artifacts claimed in an <see cref="IOwnedArtifactJournal"/>.
/// </summary>
/// <remarks>
/// <para>The compare-and-swap recovery matrix is the important part. A pre-image is the only
/// durable copy of the last committed state while a swap is in flight, so retiring one requires
/// proof that the candidate replacement actually reached the target — not merely that the
/// target pathname is occupied. Each recorded transaction resolves to exactly one of:</para>
/// <list type="bullet">
/// <item>target missing → restore the pre-image (non-destructive; fills a hole).</item>
/// <item>target holds the recorded candidate → the swap committed; retire the pre-image.</item>
/// <item>target holds something else → preserve both and fail closed.</item>
/// <item>pre-image gone, target holds the candidate → completed; drop the stale record.</item>
/// <item>neither recoverable → fatal.</item>
/// </list>
/// </remarks>
internal static class OwnedArtifactReconciler
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint DELETE = 0x00010000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;
    private const int FileAttributeTagInfoClass = 9;
    private const int ERROR_FILE_NOT_FOUND = 2;
    private const int ERROR_PATH_NOT_FOUND = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_ATTRIBUTE_TAG_INFO
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        int fileInformationClass,
        out FILE_ATTRIBUTE_TAG_INFO fileInformation,
        uint bufferSize);

    /// <summary>What a reconciliation pass concluded.</summary>
    internal sealed record Result(
        IReadOnlySet<string> ProvenOwnedPaths,
        IReadOnlyList<ReconciliationOutcome> Outcomes)
    {
        public bool HasFatal => Outcomes.Any(o => o.Severity == ReconciliationSeverity.Fatal);
    }

    public static Result Reconcile(
        string physicalRoot,
        IOwnedArtifactJournal journal,
        bool retireCommittedMigrationArtifacts = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);
        ArgumentNullException.ThrowIfNull(journal);

        string root = Path.GetFullPath(physicalRoot);
        var proven = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outcomes = new List<ReconciliationOutcome>();

        OwnedArtifactJournalSnapshot snapshot;
        try
        {
            snapshot = journal.Read(root);
        }
        catch (OwnedArtifactJournalCorruptException ex)
        {
            // CRUU16-002/004: an unreadable ledger is not a licence to reconcile with less
            // information. Nothing is compacted, nothing is destroyed, and startup stops.
            outcomes.Add(new ReconciliationOutcome(
                ReconciliationSeverity.Fatal,
                "OWNERSHIP_JOURNAL_CORRUPT",
                WindowsOwnedArtifactJournal.GetJournalPath(root),
                ex.Message));
            return new Result(proven, outcomes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            outcomes.Add(new ReconciliationOutcome(
                ReconciliationSeverity.Fatal,
                "OWNERSHIP_JOURNAL_UNREADABLE",
                WindowsOwnedArtifactJournal.GetJournalPath(root),
                ex.Message));
            return new Result(proven, outcomes);
        }

        if (snapshot.Records.Count == 0)
        {
            // A first append may be torn by process death. Parsing correctly yields no
            // authority, but the exact journal object still needs retirement or every later
            // retry would carry unexplained control residue forever.
            if (snapshot.Exists)
            {
                try
                {
                    journal.Rewrite(root, snapshot, []);
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException or StaleExpectedFileException)
                {
                    outcomes.Add(new ReconciliationOutcome(
                        ReconciliationSeverity.Warning,
                        "EMPTY_OWNERSHIP_JOURNAL_RETIRE_FAILED",
                        WindowsOwnedArtifactJournal.GetJournalPath(root),
                        ex.Message));
                }
            }
            return new Result(proven, outcomes);
        }

        var surviving = new List<OwnedArtifactRecord>();

        // A compare-and-swap appends more than one record for the same operation; the latest
        // phase is the authority on how far it got.
        foreach (IGrouping<Guid, OwnedArtifactRecord> transaction in
                 snapshot.Records.Where(r => r.Kind == OwnedArtifactKind.CasPreimage)
                                 .GroupBy(r => r.OperationId))
        {
            OwnedArtifactRecord record = transaction.OrderByDescending(r => r.Phase).First();
            ResolveCasTransaction(root, record, outcomes, surviving, proven);
        }

        // A migration artifact also advances through multiple records. Physical identity at
        // either recorded path is authoritative even when the post-publication phase append
        // did not land.
        foreach (IGrouping<Guid, OwnedArtifactRecord> transaction in
                 snapshot.Records.Where(r => r.Kind == OwnedArtifactKind.MigrationArtifact)
                                 .GroupBy(r => r.OperationId))
        {
            OwnedArtifactRecord record = transaction.OrderByDescending(r => r.Phase).First();
            ResolveMigrationArtifact(
                root,
                record,
                outcomes,
                surviving,
                proven,
                retireCommittedMigrationArtifacts);
        }

        foreach (IGrouping<Guid, OwnedArtifactRecord> transaction in
                 snapshot.Records.Where(r => r.Kind == OwnedArtifactKind.CapabilityProbe)
                                 .GroupBy(r => r.OperationId))
        {
            OwnedArtifactRecord record = transaction.OrderByDescending(r => r.Phase).First();
            ResolveCapabilityProbe(root, record, outcomes, surviving, proven);
        }

        foreach (OwnedArtifactRecord record in
                 snapshot.Records.Where(r =>
                      r.Kind != OwnedArtifactKind.CasPreimage &&
                      r.Kind != OwnedArtifactKind.MigrationArtifact &&
                      r.Kind != OwnedArtifactKind.CapabilityProbe))
        {
            ResolveOwnedArtifact(
                root,
                record,
                outcomes,
                surviving,
                proven,
                retireCommittedMigrationArtifacts);
        }

        // Never compact the ledger when any authority question is unresolved: it is the
        // evidence a person would need to sort the situation out by hand.
        if (!outcomes.Any(o => o.Severity == ReconciliationSeverity.Fatal))
        {
            try
            {
                journal.Rewrite(root, snapshot, surviving);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or StaleExpectedFileException)
            {
                outcomes.Add(new ReconciliationOutcome(
                    ReconciliationSeverity.Warning,
                    "OWNERSHIP_JOURNAL_COMPACTION_FAILED",
                    WindowsOwnedArtifactJournal.GetJournalPath(root),
                    ex.Message));
            }
        }

        return new Result(proven, outcomes);
    }

    private static void ResolveCapabilityProbe(
        string root,
        OwnedArtifactRecord record,
        List<ReconciliationOutcome> outcomes,
        List<OwnedArtifactRecord> surviving,
        HashSet<string> proven)
    {
        string[] candidates = [record.RelativePath, record.RestoreRelativePath ?? string.Empty];
        foreach (string relative in candidates.Where(path => path.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string path = Path.GetFullPath(Path.Combine(root, relative));
            SafeFileHandle? handle;
            try
            {
                handle = OpenExactNonReparse(path, root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                outcomes.Add(new ReconciliationOutcome(
                    ReconciliationSeverity.Warning,
                    "CAPABILITY_PROBE_UNREADABLE",
                    path,
                    ex.Message));
                surviving.Add(record);
                return;
            }

            if (handle is null)
            {
                continue;
            }

            using (handle)
            {
                if (WindowsFileIdentity.FromHandle(handle) != record.Identity)
                {
                    continue;
                }

                proven.Add(path);
                bool requiresContent = record.Phase >= OwnedArtifactPhase.ProbeContentDurable;
                if (requiresContent &&
                    (record.CandidateSha256Hex is null ||
                     !ProvenanceBoundCleanup.MatchesExpectedContent(
                         handle,
                         record.CandidateLength,
                         record.CandidateSha256Hex)))
                {
                    outcomes.Add(new ReconciliationOutcome(
                        ReconciliationSeverity.Fatal,
                        "CAPABILITY_PROBE_DURABLE_CONTENT_MISMATCH",
                        path,
                        "The exact owned probe survived after its content-durable phase with different bytes."));
                    surviving.Add(record);
                    return;
                }

                try
                {
                    WindowsHandleDeletion.MarkForDeletion(handle, path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    outcomes.Add(new ReconciliationOutcome(
                        ReconciliationSeverity.Warning,
                        "CAPABILITY_PROBE_CLEANUP_FAILED",
                        path,
                        ex.Message));
                    surviving.Add(record);
                }
                return;
            }
        }

        // Neither predeclared path contains this identity. Any occupants are foreign and are
        // preserved; the stale claim can no longer authorize a deletion and is retired.
    }

    /// <summary>
    /// The CRUU16-001 recovery matrix. Target pathname occupancy is never treated as proof that
    /// the swap committed; the recorded phase and the recorded candidate content are.
    /// </summary>
    /// <remarks>
    /// The phase matters as much as the objects on disk. Once
    /// <see cref="OwnedArtifactPhase.CandidatePublished"/> has been durably recorded, the swap
    /// is known to have completed, and the target is free to have been updated many times
    /// since by later operations — comparing its current content against this transaction's
    /// candidate would then be meaningless. Only a transaction that stopped at
    /// <see cref="OwnedArtifactPhase.PreimageSidelined"/> is genuinely in flight, and only
    /// there does the candidate hash decide the outcome.
    /// </remarks>
    private static void ResolveCasTransaction(
        string root,
        OwnedArtifactRecord record,
        List<ReconciliationOutcome> outcomes,
        List<OwnedArtifactRecord> surviving,
        HashSet<string> proven)
    {
        string preimagePath = Path.GetFullPath(Path.Combine(root, record.RelativePath));
        string targetPath = Path.GetFullPath(Path.Combine(root, record.RestoreRelativePath!));

        SafeFileHandle? preimage;
        try
        {
            preimage = OpenExactNonReparse(preimagePath, root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            outcomes.Add(Fatal("CAS_PREIMAGE_UNREADABLE", preimagePath, ex.Message));
            surviving.Add(record);
            return;
        }

        using (preimage)
        {
            bool preimageIsOurs = preimage is not null &&
                                  WindowsFileIdentity.FromHandle(preimage) == record.Identity;

            if (preimage is not null && !preimageIsOurs)
            {
                outcomes.Add(new ReconciliationOutcome(
                    ReconciliationSeverity.Notice,
                    "CAS_PREIMAGE_REPLACED",
                    preimagePath,
                    "A different object occupies a recorded pre-image pathname; it was preserved."));
            }

            // The swap is known to have completed. Nothing is at risk, whatever the target
            // holds now — later updates are expected to have moved it on.
            if (record.Phase == OwnedArtifactPhase.CandidatePublished)
            {
                if (!preimageIsOurs)
                {
                    return;
                }

                proven.Add(preimagePath);
                RetirePreimage(preimage!, preimagePath, record, outcomes, surviving);
                return;
            }

            // Interrupted before the publish was recorded: this is the case where the
            // pre-image may still be the last committed content.
            TargetState target = InspectTarget(targetPath, root, record);

            if (target.Unreadable)
            {
                outcomes.Add(Fatal("CAS_TARGET_UNREADABLE", targetPath, target.Error!));
                surviving.Add(record);
                return;
            }

            // Prepared is deliberately durable before the sideline rename. If the exact old
            // object is still at the target, the swap never started (or was restored after a
            // failed phase advance). A missing/foreign pre-image pathname is irrelevant in
            // that state and is preserved.
            if (record.Phase is OwnedArtifactPhase.Prepared or OwnedArtifactPhase.PreimageSidelined &&
                target.MatchesRecordedOldIdentity &&
                !preimageIsOurs)
            {
                return;
            }

            if (!preimageIsOurs)
            {
                if (target.MatchesCandidate)
                {
                    // Published, and the pre-image already retired, before the phase record
                    // landed. Complete.
                    return;
                }

                outcomes.Add(Fatal(
                    target.Exists ? "CAS_AMBIGUOUS" : "CAS_UNRECOVERABLE",
                    targetPath,
                    target.Exists
                        ? $"'{record.RestoreRelativePath}' does not contain the update this process was applying, " +
                          "and its recorded previous content is unavailable. Both objects were preserved."
                        : $"An interrupted update left '{record.RestoreRelativePath}' missing and its recorded " +
                          "previous content is no longer available. No data was destroyed by this process, but the " +
                          "state cannot be resolved automatically."));
                surviving.Add(record);
                return;
            }

            if (!target.Exists)
            {
                // Interrupted between the two renames. The pre-image is the last committed
                // content; restoring it only ever fills a hole.
                if (WindowsHandleDeletion.TryRenameNoOverwrite(preimage!, targetPath, out int error))
                {
                    outcomes.Add(new ReconciliationOutcome(
                        ReconciliationSeverity.Notice,
                        "CAS_PREIMAGE_RESTORED",
                        targetPath,
                        "An interrupted update was rolled back to the last committed content."));
                    return;
                }

                outcomes.Add(Fatal(
                    "CAS_PREIMAGE_RESTORE_FAILED",
                    preimagePath,
                    $"The last committed content of '{record.RestoreRelativePath}' is held here and could not be " +
                    $"restored: {new Win32Exception(error).Message}"));
                surviving.Add(record);
                return;
            }

            if (target.MatchesCandidate)
            {
                // Proven: the object at the target is byte-for-byte the candidate this
                // transaction recorded before it began. The pre-image is superseded.
                proven.Add(preimagePath);
                RetirePreimage(preimage!, preimagePath, record, outcomes, surviving);
                return;
            }

            // Occupied by something that is neither the previous content nor the candidate.
            // This is the case the CRUU15 implementation silently resolved by deleting the
            // pre-image (CRUU16-001).
            outcomes.Add(Fatal(
                "CAS_AMBIGUOUS",
                targetPath,
                $"'{record.RestoreRelativePath}' was changed by something else while an update was in flight. " +
                $"Both it and the previous content (preserved as '{record.RelativePath}') were left untouched."));
            surviving.Add(record);
        }
    }

    private static void RetirePreimage(
        SafeFileHandle preimage,
        string preimagePath,
        OwnedArtifactRecord record,
        List<ReconciliationOutcome> outcomes,
        List<OwnedArtifactRecord> surviving)
    {
        try
        {
            WindowsHandleDeletion.MarkForDeletion(preimage, preimagePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            outcomes.Add(new ReconciliationOutcome(
                ReconciliationSeverity.Warning,
                "CAS_PREIMAGE_RETIRE_FAILED",
                preimagePath,
                ex.Message));
            surviving.Add(record);
        }
    }

    private readonly record struct TargetState(
        bool Exists,
        bool MatchesCandidate,
        bool MatchesRecordedOldIdentity,
        bool Unreadable,
        string? Error);

    private static TargetState InspectTarget(string targetPath, string root, OwnedArtifactRecord record)
    {
        SafeFileHandle? handle;
        try
        {
            handle = OpenExactNonReparse(targetPath, root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new TargetState(true, false, false, true, ex.Message);
        }

        if (handle is null)
        {
            return new TargetState(false, false, false, false, null);
        }

        using (handle)
        {
            try
            {
                long length = RandomAccess.GetLength(handle);
                if (record.CandidateLength >= 0 && length != record.CandidateLength)
                {
                    return new TargetState(
                        true,
                        false,
                        WindowsFileIdentity.FromHandle(handle) == record.Identity,
                        false,
                        null);
                }

                byte[] bytes = new byte[length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = RandomAccess.Read(handle, bytes.AsSpan(read), read);
                    if (n <= 0)
                    {
                        return new TargetState(true, false, false, true, "Unexpected end of data.");
                    }

                    read += n;
                }

                string actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
                bool matches = record.CandidateSha256Hex is not null &&
                               string.Equals(actual, record.CandidateSha256Hex, StringComparison.OrdinalIgnoreCase);

                return new TargetState(
                    true,
                    matches,
                    WindowsFileIdentity.FromHandle(handle) == record.Identity,
                    false,
                    null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new TargetState(true, false, false, true, ex.Message);
            }
        }
    }

    private static void ResolveOwnedArtifact(
        string root,
        OwnedArtifactRecord record,
        List<ReconciliationOutcome> outcomes,
        List<OwnedArtifactRecord> surviving,
        HashSet<string> proven,
        bool retireCommittedMigrationArtifacts)
    {
        string artifactPath = Path.GetFullPath(Path.Combine(root, record.RelativePath));

        if (record.Kind == OwnedArtifactKind.MigrationDirectory)
        {
            ResolveOwnedDirectory(
                root,
                artifactPath,
                record,
                outcomes,
                surviving,
                proven,
                retireCommittedMigrationArtifacts);
            return;
        }

        SafeFileHandle? handle;
        try
        {
            handle = OpenExactNonReparse(artifactPath, root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            outcomes.Add(new ReconciliationOutcome(
                ReconciliationSeverity.Warning,
                "OWNED_ARTIFACT_UNREADABLE",
                artifactPath,
                ex.Message));
            surviving.Add(record);
            return;
        }

        if (handle is null)
        {
            // Gone; the claim is dead.
            return;
        }

        using (handle)
        {
            if (WindowsFileIdentity.FromHandle(handle) != record.Identity)
            {
                // Something else occupies our pathname. Preserve it and forget the claim.
                return;
            }

            proven.Add(artifactPath);

            if (record.Kind == OwnedArtifactKind.MigrationFinal)
            {
                // A migrated final is live data, not a leftover: the record exists so that
                // rollback and retry can prove identity before deleting it (CRUU16-005).
                // Once the settings commit is irrevocable it is ordinary user data and must
                // no longer remain deletion-authorized forever merely to pin an append-only
                // ledger. The file itself is never deleted here.
                if (!retireCommittedMigrationArtifacts)
                {
                    surviving.Add(record);
                }
                return;
            }

            try
            {
                WindowsHandleDeletion.MarkForDeletion(handle, artifactPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                outcomes.Add(new ReconciliationOutcome(
                    ReconciliationSeverity.Warning,
                    "OWNED_ARTIFACT_CLEANUP_FAILED",
                    artifactPath,
                    ex.Message));
                surviving.Add(record);
            }
        }
    }

    private static void ResolveOwnedDirectory(
        string root,
        string directoryPath,
        OwnedArtifactRecord record,
        List<ReconciliationOutcome> outcomes,
        List<OwnedArtifactRecord> surviving,
        HashSet<string> proven,
        bool retireCommittedMigrationArtifacts)
    {
        try
        {
            using WindowsRetirableDirectory? directory =
                WindowsRetirableDirectory.OpenExistingOrNull(
                    directoryPath,
                    root,
                    requireDeleteAccess: false);
            if (directory is null)
            {
                return;
            }

            if (directory.Identity != record.Identity)
            {
                // A different directory now owns the pathname. Preserve it and retire our
                // stale authority so it can never authorize deletion of the replacement.
                return;
            }

            proven.Add(directoryPath);
            // A live attempt directory is data-bearing structure, like MigrationFinal: keep
            // its deletion authority for rollback/retry, but never remove it during routine
            // transient cleanup. Once commit is irrevocable, retire only the authority.
            if (!retireCommittedMigrationArtifacts)
            {
                surviving.Add(record);
            }
        }
        catch (InvalidOperationException)
        {
            // The directory was replaced by a non-directory object. That object is foreign;
            // preserve it and retire the stale directory claim so it can never become delete
            // authority if the pathname later changes type again.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            outcomes.Add(new ReconciliationOutcome(
                ReconciliationSeverity.Warning,
                "OWNED_DIRECTORY_UNREADABLE",
                directoryPath,
                ex.Message));
            surviving.Add(record);
        }
    }

    private static void ResolveMigrationArtifact(
        string root,
        OwnedArtifactRecord record,
        List<ReconciliationOutcome> outcomes,
        List<OwnedArtifactRecord> surviving,
        HashSet<string> proven,
        bool retireCommittedMigrationArtifacts)
    {
        string tempPath = Path.GetFullPath(Path.Combine(root, record.RelativePath));
        string finalPath = Path.GetFullPath(Path.Combine(root, record.RestoreRelativePath!));
        bool keepClaim = false;

        // First settle an exact stage if the rename never happened. The handle both proves and
        // deletes the same object; a byte-equivalent substitution is preserved.
        SafeFileHandle? temp = null;
        try
        {
            temp = OpenExactNonReparse(tempPath, root);
            if (temp is not null && WindowsFileIdentity.FromHandle(temp) == record.Identity)
            {
                proven.Add(tempPath);
                try
                {
                    WindowsHandleDeletion.MarkForDeletion(temp, tempPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    outcomes.Add(new ReconciliationOutcome(
                        ReconciliationSeverity.Warning,
                        "MIGRATION_STAGE_CLEANUP_FAILED",
                        tempPath,
                        ex.Message));
                    keepClaim = true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            outcomes.Add(Fatal("MIGRATION_STAGE_UNREADABLE", tempPath, ex.Message));
            keepClaim = true;
        }
        finally
        {
            temp?.Dispose();
        }

        // Publication preserves file identity. This check therefore recovers the exact final
        // even when the FinalPublished append failed or the process died immediately after the
        // rename. The final is live migration data and remains claimed for retry/rollback.
        SafeFileHandle? final = null;
        try
        {
            final = OpenExactNonReparse(finalPath, root);
            if (final is not null && WindowsFileIdentity.FromHandle(final) == record.Identity)
            {
                if (MatchesRecordedContent(final, record, out string? mismatch))
                {
                    proven.Add(finalPath);
                    keepClaim = !retireCommittedMigrationArtifacts;
                }
                else
                {
                    keepClaim = true;
                    outcomes.Add(Fatal(
                        "MIGRATION_FINAL_CONTENT_MISMATCH",
                        finalPath,
                        mismatch!));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            outcomes.Add(Fatal("MIGRATION_FINAL_UNREADABLE", finalPath, ex.Message));
            keepClaim = true;
        }
        finally
        {
            final?.Dispose();
        }

        if (keepClaim)
        {
            surviving.Add(record);
        }
    }

    private static bool MatchesRecordedContent(
        SafeFileHandle handle,
        OwnedArtifactRecord record,
        out string? mismatch)
    {
        mismatch = null;
        if (record.CandidateLength < 0 || record.CandidateSha256Hex is null)
        {
            mismatch = "The migration ownership record lacks the expected content authority.";
            return false;
        }

        long length = RandomAccess.GetLength(handle);
        if (length != record.CandidateLength)
        {
            mismatch = $"Expected {record.CandidateLength} bytes, found {length}. The object was preserved.";
            return false;
        }

        if (!ProvenanceBoundCleanup.MatchesExpectedContent(
                handle,
                record.CandidateLength,
                record.CandidateSha256Hex))
        {
            mismatch = $"Expected SHA-256 {record.CandidateSha256Hex}; the current bytes do not match. The object was preserved.";
            return false;
        }

        return true;
    }

    private static ReconciliationOutcome Fatal(string code, string path, string message) =>
        new(ReconciliationSeverity.Fatal, code, path, message);

    private static SafeFileHandle? OpenExactNonReparse(string path, string physicalRoot)
    {
        SafeFileHandle handle = CreateFileW(
            path,
            GENERIC_READ | DELETE,
            FILE_SHARE_READ,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND)
            {
                return null;
            }

            throw new IOException($"Unable to open owned artifact '{path}'.", new Win32Exception(error));
        }

        try
        {
            if (!GetFileInformationByHandleEx(
                    handle,
                    FileAttributeTagInfoClass,
                    out FILE_ATTRIBUTE_TAG_INFO tagInfo,
                    (uint)Marshal.SizeOf<FILE_ATTRIBUTE_TAG_INFO>()))
            {
                throw new IOException(
                    $"Unable to inspect owned artifact attributes for '{path}'.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }

            if ((tagInfo.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
            {
                throw new InvalidDataException($"Refusing to act on reparse-point artifact '{path}'.");
            }

            string finalPath = WindowsFinalPathHelper.GetNormalizedDosPath(handle);
            WindowsFinalPathHelper.AssertStrictDescendantFile(physicalRoot, finalPath);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }
}
