using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>What a durably-recorded artifact is, and therefore how recovery may treat it.</summary>
internal enum OwnedArtifactKind
{
    /// <summary>A staging file for a durable write. Safe to destroy once identity is proven.</summary>
    Stage,

    /// <summary>
    /// The previous committed content of a compare-and-swap target, renamed aside so the staged
    /// replacement could take its name. Never destroyed without proof that the candidate
    /// replacement actually reached the target (CRUU16-001).
    /// </summary>
    CasPreimage,

    /// <summary>
    /// A migration payload object under its final name. Carries the identity it was created
    /// with, so rollback and retry can destroy it only if it is still that exact object
    /// (CRUU16-005).
    /// </summary>
    MigrationFinal,

    /// <summary>
    /// One migration payload object whose exact identity may be found at either its staging
    /// path or final path. Both locations are recorded before publication.
    /// </summary>
    MigrationArtifact,

    /// <summary>
    /// A directory created by the current migration attempt. Its NTFS identity, rather than
    /// its pathname or emptiness, is the authority for rollback and retry deletion.
    /// </summary>
    MigrationDirectory,

    /// <summary>
    /// An ephemeral capability probe whose exact identity may move between two paths that
    /// were both durably declared before the first rename.
    /// </summary>
    CapabilityProbe
}

/// <summary>How far a durable operation had got when the record was appended.</summary>
internal enum OwnedArtifactPhase
{
    /// <summary>The artifact has been created and claimed; nothing has been published yet.</summary>
    Claimed,

    /// <summary>
    /// All authority needed for a compare-and-swap is durable, but the old target has not yet
    /// been moved. Recovery must inspect the recorded old identity to determine whether the
    /// rename happened before the next phase record landed.
    /// </summary>
    Prepared,

    /// <summary>A compare-and-swap has moved the previous committed object aside.</summary>
    PreimageSidelined,

    /// <summary>A compare-and-swap has published its candidate under the target name.</summary>
    CandidatePublished,

    /// <summary>The probe identity and every legal recovery path are durable; bytes may be partial.</summary>
    ProbeCreatedClaimed = 10,

    /// <summary>The complete expected probe bytes have crossed a durable flush barrier.</summary>
    ProbeContentDurable = 11,

    /// <summary>A rename is about to occur; both its source and destination are already declared.</summary>
    ProbeRenamePrepared = 12,

    /// <summary>The exact probe handle completed its rename.</summary>
    ProbeRenamed = 13,

    /// <summary>The exact probe handle was marked for deletion.</summary>
    ProbeRetired = 14
}

/// <summary>
/// One durably recorded claim. Paths are relative to the data root the journal belongs to.
/// </summary>
/// <remarks>
/// For a <see cref="OwnedArtifactKind.CasPreimage"/> record the candidate's content hash and
/// length are recorded <i>before</i> the swap begins. That is what lets recovery distinguish
/// "our candidate is at the target" from "some file happens to be at the target" — the
/// distinction CRUU16-001 showed the previous design could not make, and without which the
/// pre-image (the only durable copy of the last committed state) could be deleted after a
/// crash.
/// </remarks>
internal sealed record OwnedArtifactRecord(
    Guid OperationId,
    OwnedArtifactKind Kind,
    OwnedArtifactPhase Phase,
    string RelativePath,
    WindowsFileIdentity Identity,
    string? RestoreRelativePath = null,
    string? CandidateSha256Hex = null,
    long CandidateLength = -1);

/// <summary>
/// The ownership ledger could not be trusted. Because the ledger authorizes deletion and
/// compare-and-swap restoration, a ledger that cannot be parsed exactly is not a reason to
/// carry on with less information — it is a reason to stop (CRUU16-002).
/// </summary>
internal sealed class OwnedArtifactJournalCorruptException : IOException
{
    public OwnedArtifactJournalCorruptException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>What a read of the ledger returned, bound to the exact object it was read from.</summary>
internal sealed record OwnedArtifactJournalSnapshot(
    IReadOnlyList<OwnedArtifactRecord> Records,
    WindowsFileIdentity? Identity,
    string? Sha256Hex)
{
    public static OwnedArtifactJournalSnapshot Absent { get; } = new([], null, null);

    public bool Exists => Identity is not null;
}

/// <summary>
/// The durable provenance authority for transient artifacts.
/// </summary>
/// <remarks>
/// <para>A pathname is not ownership. What is ownership is the object identity recorded at the
/// moment this process created the object — volume serial plus NTFS file ID, which no later
/// substitution can reproduce.</para>
/// <para>Because this ledger authorizes automatic destruction, it is held to the same standard
/// as the data it protects (CRUU16-003): it is opened without following reparse points, proven
/// to resolve inside the data root, parsed strictly, and rewritten only against the exact
/// object that was read.</para>
/// </remarks>
internal interface IOwnedArtifactJournal
{
    /// <summary>Durably records a claim for an artifact inside <paramref name="root"/>.</summary>
    void Record(string root, OwnedArtifactRecord record);

    /// <summary>
    /// Reads the ledger for <paramref name="root"/>, bound to the object it was read from.
    /// </summary>
    /// <exception cref="OwnedArtifactJournalCorruptException">
    /// The ledger exists but cannot be parsed exactly.
    /// </exception>
    OwnedArtifactJournalSnapshot Read(string root);

    /// <summary>
    /// Replaces the ledger with exactly <paramref name="surviving"/>, but only if the object at
    /// the ledger's pathname is still the one <paramref name="expected"/> was read from.
    /// </summary>
    void Rewrite(string root, OwnedArtifactJournalSnapshot expected, IReadOnlyList<OwnedArtifactRecord> surviving);
}

/// <summary>
/// Append-only, one ledger per data root. Records are single lines, each carrying a checksum
/// over its own fields, so a torn final append is distinguishable from a corrupted record.
/// </summary>
internal sealed class WindowsOwnedArtifactJournal : IOwnedArtifactJournal
{
    internal const string JournalFileName = ".prompthelper-owned.log";
    private const string RecordVersion = "3";

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint DELETE = 0x00010000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint OPEN_ALWAYS = 4;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_ATTRIBUTE_HIDDEN = 0x00000002;
    private const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;
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
    private static extern bool FlushFileBuffers(SafeFileHandle hFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        int fileInformationClass,
        out FILE_ATTRIBUTE_TAG_INFO fileInformation,
        uint bufferSize);

    public static string GetJournalPath(string root) => Path.Combine(root, JournalFileName);

    public void Record(string root, OwnedArtifactRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(record);

        string fullRoot = Path.GetFullPath(root);
        string journalPath = GetJournalPath(fullRoot);
        byte[] line = Encoding.UTF8.GetBytes(Serialize(record) + "\n");

        // OPEN_ALWAYS with OPEN_REPARSE_POINT: a symlink planted at the ledger's pathname is
        // opened as the reparse point itself and then rejected, rather than silently appending
        // this application's ownership claims to a file somewhere else (CRUU16-003).
        using SafeFileHandle handle = CreateFileW(
            journalPath,
            GENERIC_WRITE,
            FILE_SHARE_READ,
            IntPtr.Zero,
            OPEN_ALWAYS,
            FILE_ATTRIBUTE_NORMAL | FILE_ATTRIBUTE_HIDDEN | FILE_FLAG_WRITE_THROUGH | FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new IOException(
                $"Unable to record artifact ownership in '{journalPath}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        AssertNonReparseUnderRoot(handle, journalPath, fullRoot);

        long offset = RandomAccess.GetLength(handle);
        const string tornFirstAppendCut =
            "WindowsOwnedArtifactJournal.AfterPartialFirstAppend";
        if (offset == 0 && ProductionCrashCut.IsArmed(tornFirstAppendCut))
        {
            int partialLength = Math.Max(1, line.Length / 2);
            RandomAccess.Write(handle, line.AsSpan(0, partialLength), offset);
            ProductionCrashCut.Hit(tornFirstAppendCut);
            throw new IOException("The armed torn-append crash cut returned without process termination.");
        }

        RandomAccess.Write(handle, line, offset);

        if (!FlushFileBuffers(handle))
        {
            throw new IOException(
                $"Unable to flush the artifact ownership journal '{journalPath}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    public OwnedArtifactJournalSnapshot Read(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        string fullRoot = Path.GetFullPath(root);
        string journalPath = GetJournalPath(fullRoot);

        SafeFileHandle handle = CreateFileW(
            journalPath,
            GENERIC_READ,
            FILE_SHARE_READ,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND)
            {
                return OwnedArtifactJournalSnapshot.Absent;
            }

            throw new IOException($"Unable to read '{journalPath}'.", new Win32Exception(error));
        }

        using (handle)
        {
            AssertNonReparseUnderRoot(handle, journalPath, fullRoot);

            long length = RandomAccess.GetLength(handle);
            byte[] raw = new byte[length];
            int read = 0;
            while (read < raw.Length)
            {
                int n = RandomAccess.Read(handle, raw.AsSpan(read), read);
                if (n <= 0)
                {
                    throw new OwnedArtifactJournalCorruptException(
                        $"Unexpected end of data reading the ownership journal '{journalPath}'.");
                }

                read += n;
            }

            return new OwnedArtifactJournalSnapshot(
                Parse(raw, journalPath),
                WindowsFileIdentity.FromHandle(handle),
                Convert.ToHexStringLower(SHA256.HashData(raw)));
        }
    }

    /// <summary>
    /// Strict, fail-closed parsing (CRUU16-002). Every newline-terminated record must parse and
    /// match its own checksum; a malformed one means the ledger is corrupt, not that one claim
    /// can be quietly dropped. Only a trailing fragment with no terminating newline — the one
    /// shape a torn append can actually produce — is discarded.
    /// </summary>
    private static IReadOnlyList<OwnedArtifactRecord> Parse(byte[] raw, string journalPath)
    {
        if (raw.Length == 0)
        {
            return [];
        }

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(raw);
        }
        catch (DecoderFallbackException ex)
        {
            throw new OwnedArtifactJournalCorruptException(
                $"The ownership journal '{journalPath}' is not valid UTF-8.", ex);
        }

        string[] lines = text.Split('\n');

        // A complete ledger ends with a newline, so the final split element is empty. Anything
        // else there is an append that did not finish.
        int completeCount = lines.Length - 1;

        var records = new List<OwnedArtifactRecord>(completeCount);
        for (int i = 0; i < completeCount; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
            {
                throw new OwnedArtifactJournalCorruptException(
                    $"The ownership journal '{journalPath}' contains an empty record at line {i + 1}.");
            }

            if (!TryDeserialize(line, out OwnedArtifactRecord? record))
            {
                throw new OwnedArtifactJournalCorruptException(
                    $"The ownership journal '{journalPath}' contains a malformed record at line {i + 1}. " +
                    "It is preserved as-is; destructive reconciliation cannot proceed against an unreadable ledger.");
            }

            records.Add(record);
        }

        return records;
    }

    public void Rewrite(
        string root,
        OwnedArtifactJournalSnapshot expected,
        IReadOnlyList<OwnedArtifactRecord> surviving)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(surviving);

        string fullRoot = Path.GetFullPath(root);
        string journalPath = GetJournalPath(fullRoot);

        if (!expected.Exists)
        {
            // Nothing was read, so there is nothing of ours to replace. Anything at the
            // pathname now belongs to somebody else.
            return;
        }

        if (surviving.Count == 0)
        {
            // CRUU16-003: bound to the object that was read. A ledger replaced between the read
            // and here is a foreign object, and deleting it would destroy someone else's file.
            using WindowsExpectedTargetAuthority? authority =
                WindowsExpectedTargetAuthority.Open(journalPath, fullRoot);

            if (authority is null || authority.Identity != expected.Identity)
            {
                return;
            }

            authority.AssertContentMatches(expected.Sha256Hex!);
            authority.DeleteExact();
            return;
        }

        // A provenance authority cannot safely use its own two-rename protocol to compact
        // itself: a crash after sidelining the old ledger would leave no discoverable ledger
        // capable of explaining that sideline. Keep the journal append-only while live claims
        // remain. Dead history is harmless (CAS phases are grouped by operation ID and missing
        // artifacts are ignored), and the exact ledger is retired once no live claim survives.
        //
        // Still bind this decision to both the identity and bytes that were read. A same-byte
        // replacement is a foreign object and must not be treated as the snapshot's ledger.
        using WindowsExpectedTargetAuthority? retained =
            WindowsExpectedTargetAuthority.Open(journalPath, fullRoot);

        if (retained is null || retained.Identity != expected.Identity)
        {
            throw new StaleExpectedFileException(
                $"The ownership journal '{journalPath}' was replaced after it was read. The replacement was preserved.");
        }

        retained.AssertContentMatches(expected.Sha256Hex!);
    }

    private static void AssertNonReparseUnderRoot(SafeFileHandle handle, string journalPath, string root)
    {
        if (!GetFileInformationByHandleEx(
                handle,
                FileAttributeTagInfoClass,
                out FILE_ATTRIBUTE_TAG_INFO tagInfo,
                (uint)Marshal.SizeOf<FILE_ATTRIBUTE_TAG_INFO>()))
        {
            throw new IOException(
                $"Unable to inspect attributes for the ownership journal '{journalPath}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        if ((tagInfo.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
        {
            throw new OwnedArtifactJournalCorruptException(
                $"Refusing to use a reparse point as the ownership journal: '{journalPath}'.");
        }

        string finalPath = WindowsFinalPathHelper.GetNormalizedDosPath(handle);
        WindowsFinalPathHelper.AssertStrictDescendantFile(root, finalPath);

        if (!PathIdentity.Equals(finalPath, PathIdentity.NormalizeForComparison(journalPath)))
        {
            throw new OwnedArtifactJournalCorruptException(
                $"The ownership journal resolved to '{finalPath}' rather than its managed location '{journalPath}'.");
        }
    }

    private static string Serialize(OwnedArtifactRecord record)
    {
        // Preserve the deployed v2 wire shape for every legacy artifact kind. Only the new
        // capability-probe state machine requires v3, so ordinary CAS and migration journals
        // remain readable by the previous binary.
        string recordVersion = record.Kind == OwnedArtifactKind.CapabilityProbe
            ? RecordVersion
            : "2";
        string body = string.Join(
            '|',
            recordVersion,
            record.OperationId.ToString("N"),
            record.Kind switch
            {
                OwnedArtifactKind.Stage => "stage",
                OwnedArtifactKind.CasPreimage => "preimage",
                OwnedArtifactKind.MigrationFinal => "final",
                OwnedArtifactKind.MigrationArtifact => "migration",
                OwnedArtifactKind.MigrationDirectory => "directory",
                OwnedArtifactKind.CapabilityProbe => "probe",
                _ => throw new ArgumentOutOfRangeException(nameof(record))
            },
            record.Phase switch
            {
                OwnedArtifactPhase.Claimed => "claimed",
                OwnedArtifactPhase.Prepared => "prepared",
                OwnedArtifactPhase.PreimageSidelined => "sidelined",
                OwnedArtifactPhase.CandidatePublished => "published",
                OwnedArtifactPhase.ProbeCreatedClaimed => "probe-created",
                OwnedArtifactPhase.ProbeContentDurable => "probe-durable",
                OwnedArtifactPhase.ProbeRenamePrepared => "probe-rename-prepared",
                OwnedArtifactPhase.ProbeRenamed => "probe-renamed",
                OwnedArtifactPhase.ProbeRetired => "probe-retired",
                _ => throw new ArgumentOutOfRangeException(nameof(record))
            },
            record.Identity.ToToken(),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(record.RelativePath)),
            record.RestoreRelativePath is null
                ? string.Empty
                : Convert.ToBase64String(Encoding.UTF8.GetBytes(record.RestoreRelativePath)),
            record.CandidateSha256Hex ?? string.Empty,
            record.CandidateLength.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return body + "|" + Checksum(body);
    }

    private static string Checksum(string body) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)))[..16];

    private static bool TryDeserialize(string line, out OwnedArtifactRecord record)
    {
        record = null!;

        int lastSeparator = line.LastIndexOf('|');
        if (lastSeparator <= 0)
        {
            return false;
        }

        string body = line[..lastSeparator];
        if (!string.Equals(line[(lastSeparator + 1)..], Checksum(body), StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = body.Split('|');
        if (parts.Length != 9 || (parts[0] != "2" && parts[0] != RecordVersion))
        {
            return false;
        }

        if (!Guid.TryParseExact(parts[1], "N", out Guid operationId))
        {
            return false;
        }

        OwnedArtifactKind kind = parts[2] switch
        {
            "stage" => OwnedArtifactKind.Stage,
            "preimage" => OwnedArtifactKind.CasPreimage,
            "final" => OwnedArtifactKind.MigrationFinal,
            "migration" => OwnedArtifactKind.MigrationArtifact,
            "directory" => OwnedArtifactKind.MigrationDirectory,
            "probe" => OwnedArtifactKind.CapabilityProbe,
            _ => (OwnedArtifactKind)(-1)
        };

        OwnedArtifactPhase phase = parts[3] switch
        {
            "claimed" => OwnedArtifactPhase.Claimed,
            "prepared" => OwnedArtifactPhase.Prepared,
            "sidelined" => OwnedArtifactPhase.PreimageSidelined,
            "published" => OwnedArtifactPhase.CandidatePublished,
            "probe-created" => OwnedArtifactPhase.ProbeCreatedClaimed,
            "probe-durable" => OwnedArtifactPhase.ProbeContentDurable,
            "probe-rename-prepared" => OwnedArtifactPhase.ProbeRenamePrepared,
            "probe-renamed" => OwnedArtifactPhase.ProbeRenamed,
            "probe-retired" => OwnedArtifactPhase.ProbeRetired,
            _ => (OwnedArtifactPhase)(-1)
        };

        if (!Enum.IsDefined(kind) ||
            !Enum.IsDefined(phase) ||
            !WindowsFileIdentity.TryParseToken(parts[4], out WindowsFileIdentity identity))
        {
            return false;
        }

        string relativePath;
        string? restoreRelativePath;
        try
        {
            relativePath = Encoding.UTF8.GetString(Convert.FromBase64String(parts[5]));
            restoreRelativePath = parts[6].Length == 0
                ? null
                : Encoding.UTF8.GetString(Convert.FromBase64String(parts[6]));
        }
        catch (FormatException)
        {
            return false;
        }

        if (!IsSafeRelativePath(relativePath) ||
            (restoreRelativePath is not null && !IsSafeRelativePath(restoreRelativePath)))
        {
            return false;
        }

        string? candidateSha = parts[7].Length == 0 ? null : parts[7];
        if (candidateSha is not null &&
            (candidateSha.Length != 64 || !candidateSha.All(Uri.IsHexDigit)))
        {
            return false;
        }

        if (!long.TryParse(parts[8], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out long candidateLength))
        {
            return false;
        }

        record = new OwnedArtifactRecord(
            operationId,
            kind,
            phase,
            relativePath,
            identity,
            restoreRelativePath,
            candidateSha,
            candidateLength);
        return true;
    }

    /// <summary>
    /// A journal record is a file on disk that an attacker who can write the data root could
    /// also craft, so its path is never trusted to stay inside the root.
    /// </summary>
    private static bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath) ||
            Path.IsPathRooted(relativePath) ||
            relativePath.StartsWith('/') ||
            relativePath.StartsWith('\\') ||
            relativePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return false;
        }

        foreach (string segment in relativePath.Split('\\', '/'))
        {
            if (segment.Length == 0 ||
                segment is "." or ".." ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }
        }

        return true;
    }
}
