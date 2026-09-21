namespace PromptHelper.Services;

/// <summary>What a durably-recorded artifact is, and therefore how recovery may treat it.</summary>
internal enum OwnedArtifactKind
{
    Stage,
    CasPreimage,
    MigrationFinal,
    MigrationArtifact,
    MigrationDirectory,
    CapabilityProbe,
    MigrationMarker
}

/// <summary>How far a durable operation had got when the record was appended.</summary>
internal enum OwnedArtifactPhase
{
    Claimed,
    Prepared,
    PreimageSidelined,
    CandidatePublished,

    ProbeCreatedClaimed = 10,
    ProbeContentDurable = 11,
    ProbeRenamePrepared = 12,
    ProbeRenamed = 13,
    ProbeRetired = 14,

    MarkerPrepared = 20,
    MarkerPublishedCopying = 21,
    MarkerCopyingRetirePrepared = 22,
    MarkerReadyPrepared = 23,
    MarkerPublishedReady = 24,
    MarkerRetirePrepared = 25
}

/// <summary>
/// One durably-recorded ownership claim. Identity is platform-tagged so the same recovery
/// contract can safely represent Windows NTFS identities and Linux statx identities.
/// </summary>
internal sealed record OwnedArtifactRecord(
    Guid OperationId,
    OwnedArtifactKind Kind,
    OwnedArtifactPhase Phase,
    string RelativePath,
    FileObjectIdentity Identity,
    string? RestoreRelativePath = null,
    string? CandidateSha256Hex = null,
    long CandidateLength = -1,
    Guid? MarkerAttemptId = null);

internal sealed class OwnedArtifactJournalCorruptException : IOException
{
    public OwnedArtifactJournalCorruptException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

internal sealed record OwnedArtifactJournalSnapshot(
    IReadOnlyList<OwnedArtifactRecord> Records,
    FileObjectIdentity? Identity,
    string? Sha256Hex)
{
    public static OwnedArtifactJournalSnapshot Absent { get; } = new([], null, null);

    public bool Exists => Identity is not null;
}

internal static class OwnedArtifactJournalPaths
{
    public const string JournalFileName = ".prompthelper-owned.log";

    public static string GetJournalPath(string root) =>
        Path.Combine(root, JournalFileName);
}

internal interface IOwnedArtifactJournal
{
    void Record(string root, OwnedArtifactRecord record);

    OwnedArtifactJournalSnapshot Read(string root);

    void Rewrite(
        string root,
        OwnedArtifactJournalSnapshot expected,
        IReadOnlyList<OwnedArtifactRecord> surviving);
}
