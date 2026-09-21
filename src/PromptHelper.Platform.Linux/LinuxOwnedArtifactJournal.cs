using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>
/// Linux implementation of the durable ownership ledger. The ledger remains append-only while
/// live claims exist. Once every claim is retired the exact ledger object is truncated through
/// its retained file descriptor rather than deleted by pathname, avoiding a last-moment
/// substitution window on Linux where there is no handle-bound unlink equivalent.
/// </summary>
internal sealed class LinuxOwnedArtifactJournal : IOwnedArtifactJournal
{
    private const string RecordVersion = "6";
    private const string LinuxIdentityScheme = "linux-statx-v1";
    private const string WindowsIdentityScheme = "windows-file-id-v1";

    public void Record(string root, OwnedArtifactRecord record)
    {
        LinuxNativeFileSystem.EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(record);

        string fullRoot = ResolveRoot(root);
        string journalPath = OwnedArtifactJournalPaths.GetJournalPath(fullRoot);
        byte[] line = Encoding.UTF8.GetBytes(Serialize(record) + "\n");

        SafeFileHandle handle =
            LinuxNativeFileSystem.OpenReadWriteNoFollowOrCreate(journalPath);

        using (handle)
        {
            AssertRegularFileUnderRoot(handle, journalPath, fullRoot);

            long offset = RandomAccess.GetLength(handle);
            RandomAccess.Write(handle, line, offset);
            RandomAccess.FlushToDisk(handle);
        }

        // The directory entry for a newly-created ledger is part of the durability contract.
        // Flushing on every append is conservative and keeps the first-creation case simple.
        LinuxNativeFileSystem.FlushDirectory(fullRoot);
    }

    public OwnedArtifactJournalSnapshot Read(string root)
    {
        LinuxNativeFileSystem.EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        string fullRoot = ResolveRoot(root);
        string journalPath = OwnedArtifactJournalPaths.GetJournalPath(fullRoot);

        LinuxExpectedTargetAuthority? authority;
        try
        {
            authority = LinuxExpectedTargetAuthority.Open(journalPath, fullRoot);
        }
        catch (InvalidDataException ex)
        {
            throw new OwnedArtifactJournalCorruptException(
                $"Refusing unsafe Linux ownership journal '{journalPath}'.", ex);
        }

        if (authority is null)
        {
            return OwnedArtifactJournalSnapshot.Absent;
        }

        using (authority)
        {
            byte[] raw = authority.ReadAllBytes();
            return new OwnedArtifactJournalSnapshot(
                Parse(raw, journalPath),
                authority.Identity.ToObjectIdentity(),
                Convert.ToHexStringLower(SHA256.HashData(raw)));
        }
    }

    public void Rewrite(
        string root,
        OwnedArtifactJournalSnapshot expected,
        IReadOnlyList<OwnedArtifactRecord> surviving)
    {
        LinuxNativeFileSystem.EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(surviving);

        if (!expected.Exists)
        {
            return;
        }

        string fullRoot = ResolveRoot(root);
        string journalPath = OwnedArtifactJournalPaths.GetJournalPath(fullRoot);

        SafeFileHandle? handle;
        try
        {
            handle = LinuxNativeFileSystem.OpenReadWriteNoFollowOrNull(journalPath);
        }
        catch (InvalidDataException ex)
        {
            throw new StaleExpectedFileException(
                $"The ownership journal '{journalPath}' is no longer the file that was read.",
                ex);
        }

        if (handle is null)
        {
            if (surviving.Count == 0)
            {
                return;
            }

            throw new StaleExpectedFileException(
                $"The ownership journal '{journalPath}' disappeared after it was read.");
        }

        using (handle)
        {
            AssertRegularFileUnderRoot(handle, journalPath, fullRoot);

            LinuxFileIdentity actualIdentity = LinuxFileIdentity.FromHandle(handle);
            if (actualIdentity.ToObjectIdentity() != expected.Identity)
            {
                if (surviving.Count == 0)
                {
                    // The expected ledger is already gone. Something else now owns the
                    // pathname, and must be preserved.
                    return;
                }

                throw new StaleExpectedFileException(
                    $"The ownership journal '{journalPath}' was replaced after it was read. The replacement was preserved.");
            }

            byte[] current = ReadAllBytes(handle, journalPath);
            string actualHash =
                Convert.ToHexStringLower(SHA256.HashData(current));
            if (!string.Equals(
                    actualHash,
                    expected.Sha256Hex,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new StaleExpectedFileException(
                    $"The ownership journal '{journalPath}' changed after it was read.");
            }

            if (surviving.Count != 0)
            {
                // Keep append-only history while any live authority remains. This mirrors the
                // Windows implementation and avoids rewriting the provenance authority in place.
                return;
            }

            // Linux has no reliable handle-bound unlink for regular files. Truncating the exact
            // file descriptor is fail-closed and removes every deletion authority without ever
            // acting on a pathname that may have been substituted.
            using var stream = new FileStream(
                handle,
                FileAccess.ReadWrite,
                bufferSize: 4096,
                isAsync: false);
            stream.SetLength(0);
            stream.Flush(flushToDisk: true);
        }

        LinuxNativeFileSystem.FlushDirectory(fullRoot);
    }

    private static IReadOnlyList<OwnedArtifactRecord> Parse(
        byte[] raw,
        string journalPath)
    {
        if (raw.Length == 0)
        {
            return [];
        }

        string text;
        try
        {
            text = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(raw);
        }
        catch (DecoderFallbackException ex)
        {
            throw new OwnedArtifactJournalCorruptException(
                $"The ownership journal '{journalPath}' is not valid UTF-8.", ex);
        }

        string[] lines = text.Split('\n');
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
                    "It was preserved and destructive recovery cannot continue.");
            }

            records.Add(record);
        }

        return records;
    }

    private static string Serialize(OwnedArtifactRecord record)
    {
        string body = string.Join('|',
            RecordVersion,
            record.OperationId.ToString("N"),
            SerializeKind(record.Kind),
            SerializePhase(record.Phase),
            ToBase64(record.Identity.Scheme),
            ToBase64(record.Identity.Value),
            ToBase64(record.RelativePath),
            record.RestoreRelativePath is null
                ? string.Empty
                : ToBase64(record.RestoreRelativePath),
            record.CandidateSha256Hex ?? string.Empty,
            record.CandidateLength.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            record.MarkerAttemptId?.ToString("N") ?? string.Empty,
            record.PreviousSha256Hex ?? string.Empty);

        return body + "|" + Checksum(body);
    }

    private static bool TryDeserialize(
        string line,
        out OwnedArtifactRecord record)
    {
        record = null!;

        int lastSeparator = line.LastIndexOf('|');
        if (lastSeparator <= 0)
        {
            return false;
        }

        string body = line[..lastSeparator];
        if (!string.Equals(
                line[(lastSeparator + 1)..],
                Checksum(body),
                StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = body.Split('|');
        if (parts.Length == 12 && parts[0] == RecordVersion)
        {
            return TryDeserializeLinux(parts, hasPreviousHash: true, out record);
        }

        if (parts.Length == 11 && parts[0] == "5")
        {
            return TryDeserializeLinux(parts, hasPreviousHash: false, out record);
        }

        // Windows journal v2/v3/v4 records are understood as opaque foreign-platform
        // identities. Linux recovery may then fail closed on the unsupported identity scheme
        // instead of treating the old journal as corrupt or silently dropping it.
        bool legacyWindows =
            parts.Length == 9 && (parts[0] == "2" || parts[0] == "3");
        bool currentWindows =
            parts.Length == 10 && parts[0] == "4";

        return (legacyWindows || currentWindows) &&
               TryDeserializeWindows(parts, currentWindows, out record);
    }

    private static bool TryDeserializeLinux(
        string[] parts,
        bool hasPreviousHash,
        out OwnedArtifactRecord record)
    {
        record = null!;

        if (!TryCommonHeader(parts, out Guid operationId, out OwnedArtifactKind kind, out OwnedArtifactPhase phase))
        {
            return false;
        }

        string scheme;
        string value;
        string relativePath;
        string? restoreRelativePath;
        try
        {
            scheme = FromBase64(parts[4]);
            value = FromBase64(parts[5]);
            relativePath = FromBase64(parts[6]);
            restoreRelativePath =
                parts[7].Length == 0 ? null : FromBase64(parts[7]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(scheme) ||
            string.IsNullOrWhiteSpace(value) ||
            !IsSafeRelativePath(relativePath) ||
            (restoreRelativePath is not null &&
             !IsSafeRelativePath(restoreRelativePath)) ||
            !TryCandidate(parts[8], parts[9], out string? candidateSha, out long candidateLength) ||
            !TryMarkerAttempt(kind, parts[10], out Guid? markerAttemptId))
        {
            return false;
        }

        string? previousSha = null;
        if (hasPreviousHash)
        {
            previousSha = parts[11].Length == 0 ? null : parts[11];
            if (previousSha is not null &&
                (previousSha.Length != 64 || !previousSha.All(Uri.IsHexDigit)))
            {
                return false;
            }
        }

        record = new OwnedArtifactRecord(
            operationId,
            kind,
            phase,
            relativePath,
            new FileObjectIdentity(scheme, value),
            restoreRelativePath,
            candidateSha,
            candidateLength,
            markerAttemptId,
            previousSha);
        return true;
    }

    private static bool TryDeserializeWindows(
        string[] parts,
        bool markerShape,
        out OwnedArtifactRecord record)
    {
        record = null!;

        if (!TryCommonHeader(parts, out Guid operationId, out OwnedArtifactKind kind, out OwnedArtifactPhase phase) ||
            !IsWindowsIdentityToken(parts[4]))
        {
            return false;
        }

        string relativePath;
        string? restoreRelativePath;
        try
        {
            relativePath = FromBase64(parts[5]);
            restoreRelativePath =
                parts[6].Length == 0 ? null : FromBase64(parts[6]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (!IsSafeRelativePath(relativePath) ||
            (restoreRelativePath is not null &&
             !IsSafeRelativePath(restoreRelativePath)) ||
            !TryCandidate(parts[7], parts[8], out string? candidateSha, out long candidateLength))
        {
            return false;
        }

        Guid? markerAttemptId = null;
        if (markerShape)
        {
            if (!TryMarkerAttempt(kind, parts[9], out markerAttemptId))
            {
                return false;
            }
        }
        else if (kind == OwnedArtifactKind.MigrationMarker)
        {
            return false;
        }

        record = new OwnedArtifactRecord(
            operationId,
            kind,
            phase,
            relativePath,
            new FileObjectIdentity(WindowsIdentityScheme, parts[4]),
            restoreRelativePath,
            candidateSha,
            candidateLength,
            markerAttemptId);
        return true;
    }

    private static bool TryCommonHeader(
        string[] parts,
        out Guid operationId,
        out OwnedArtifactKind kind,
        out OwnedArtifactPhase phase)
    {
        operationId = default;
        kind = default;
        phase = default;

        if (!Guid.TryParseExact(parts[1], "N", out operationId) ||
            !TryParseKind(parts[2], out kind) ||
            !TryParsePhase(parts[3], out phase))
        {
            return false;
        }

        return true;
    }

    private static bool TryCandidate(
        string shaField,
        string lengthField,
        out string? candidateSha,
        out long candidateLength)
    {
        candidateSha = shaField.Length == 0 ? null : shaField;
        candidateLength = -1;

        if (candidateSha is not null &&
            (candidateSha.Length != 64 || !candidateSha.All(Uri.IsHexDigit)))
        {
            return false;
        }

        return long.TryParse(
            lengthField,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out candidateLength);
    }

    private static bool TryMarkerAttempt(
        OwnedArtifactKind kind,
        string field,
        out Guid? markerAttemptId)
    {
        markerAttemptId = null;

        if (kind == OwnedArtifactKind.MigrationMarker)
        {
            if (!Guid.TryParseExact(field, "N", out Guid parsed))
            {
                return false;
            }

            markerAttemptId = parsed;
            return true;
        }

        return field.Length == 0;
    }

    private static string SerializeKind(OwnedArtifactKind kind) => kind switch
    {
        OwnedArtifactKind.Stage => "stage",
        OwnedArtifactKind.CasPreimage => "preimage",
        OwnedArtifactKind.MigrationFinal => "final",
        OwnedArtifactKind.MigrationArtifact => "migration",
        OwnedArtifactKind.MigrationDirectory => "directory",
        OwnedArtifactKind.CapabilityProbe => "probe",
        OwnedArtifactKind.MigrationMarker => "marker",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static bool TryParseKind(string value, out OwnedArtifactKind kind)
    {
        kind = value switch
        {
            "stage" => OwnedArtifactKind.Stage,
            "preimage" => OwnedArtifactKind.CasPreimage,
            "final" => OwnedArtifactKind.MigrationFinal,
            "migration" => OwnedArtifactKind.MigrationArtifact,
            "directory" => OwnedArtifactKind.MigrationDirectory,
            "probe" => OwnedArtifactKind.CapabilityProbe,
            "marker" => OwnedArtifactKind.MigrationMarker,
            _ => (OwnedArtifactKind)(-1)
        };

        return Enum.IsDefined(kind);
    }

    private static string SerializePhase(OwnedArtifactPhase phase) => phase switch
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
        OwnedArtifactPhase.MarkerPrepared => "marker-prepared",
        OwnedArtifactPhase.MarkerPublishedCopying => "marker-copying",
        OwnedArtifactPhase.MarkerCopyingRetirePrepared => "marker-retire-prepared",
        OwnedArtifactPhase.MarkerReadyPrepared => "marker-ready-prepared",
        OwnedArtifactPhase.MarkerPublishedReady => "marker-ready",
        OwnedArtifactPhase.MarkerRetirePrepared => "marker-retire",
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    private static bool TryParsePhase(
        string value,
        out OwnedArtifactPhase phase)
    {
        phase = value switch
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
            "marker-prepared" => OwnedArtifactPhase.MarkerPrepared,
            "marker-copying" => OwnedArtifactPhase.MarkerPublishedCopying,
            "marker-retire-prepared" => OwnedArtifactPhase.MarkerCopyingRetirePrepared,
            "marker-ready-prepared" => OwnedArtifactPhase.MarkerReadyPrepared,
            "marker-ready" => OwnedArtifactPhase.MarkerPublishedReady,
            "marker-retire" => OwnedArtifactPhase.MarkerRetirePrepared,
            _ => (OwnedArtifactPhase)(-1)
        };

        return Enum.IsDefined(phase);
    }

    private static bool IsWindowsIdentityToken(string token)
    {
        string[] parts = token.Split(':');
        return parts.Length == 3 &&
               parts[0].Length == 8 &&
               parts[1].Length == 16 &&
               parts[2].Length == 16 &&
               parts.All(part => part.All(Uri.IsHexDigit));
    }

    private static string Checksum(string body) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(body)))[..16];

    private static string ToBase64(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string FromBase64(string value) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(value));

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

        string normalized = relativePath.Replace('\\', '/');
        return normalized.Split('/').All(segment =>
            segment.Length > 0 &&
            segment != "." &&
            segment != "..");
    }

    private static byte[] ReadAllBytes(
        SafeFileHandle handle,
        string path)
    {
        long length = RandomAccess.GetLength(handle);
        if (length > int.MaxValue)
        {
            throw new IOException(
                $"Ownership journal '{path}' is too large to verify.");
        }

        byte[] bytes = new byte[(int)length];
        int read = 0;
        while (read < bytes.Length)
        {
            int count =
                RandomAccess.Read(handle, bytes.AsSpan(read), read);
            if (count <= 0)
            {
                throw new IOException(
                    $"Unexpected end of data reading ownership journal '{path}'.");
            }

            read += count;
        }

        return bytes;
    }

    private static string ResolveRoot(string root)
    {
        string full = Path.GetFullPath(root);
        return new LinuxPhysicalPathResolver()
            .ResolveWithNearestExistingAncestor(full);
    }

    private static void AssertRegularFileUnderRoot(
        SafeFileHandle handle,
        string openedPath,
        string physicalRoot)
    {
        LinuxFileIdentity.AssertRegularFile(handle, openedPath);

        string procHandlePath =
            $"/proc/self/fd/{handle.DangerousGetHandle().ToInt32()}";
        FileSystemInfo? resolved =
            File.ResolveLinkTarget(procHandlePath, returnFinalTarget: true);

        if (resolved is null)
        {
            throw new IOException(
                $"Unable to resolve physical path for ownership journal '{openedPath}'.");
        }

        string finalPath = Path.GetFullPath(resolved.FullName);
        if (!PathIdentity.IsStrictDescendant(finalPath, physicalRoot))
        {
            throw new InvalidDataException(
                $"Ownership journal escaped the managed data root. Root: '{physicalRoot}', file: '{finalPath}'.");
        }
    }
}
