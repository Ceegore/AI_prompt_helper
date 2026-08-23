using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace PromptHelper.Services;

/// <summary>What a provenance-bound cleanup attempt actually did.</summary>
internal enum ArtifactCleanupOutcome
{
    /// <summary>Nothing was there.</summary>
    Missing,

    /// <summary>A durable ownership record proved the object, and it was removed.</summary>
    DeletedProvenOwned,

    /// <summary>
    /// Something is there, but nothing proves this application created it. It was left
    /// untouched — the caller must fail closed rather than assume it can be destroyed.
    /// </summary>
    PreservedUnproven
}

/// <summary>
/// Durable authority for one migration payload rename. The claim is self-contained so both
/// pre- and post-publication journal records describe the same operation and exact object.
/// </summary>
internal sealed record MigrationArtifactClaim(
    Guid OperationId,
    string TempPath,
    string FinalPath,
    WindowsFileIdentity Identity,
    long ExpectedLength,
    string ExpectedSha256Hex);

internal interface IMigrationFileOps
{
    byte[] ReadAllBytes(string path);
    Stream CreateNewFile(string path);
    Stream OpenRead(string path);
    void FlushToDisk(Stream stream);

    /// <summary>
    /// Creates an owned stage at <paramref name="path"/> whose handle is retained through
    /// promotion or deletion, proven from that handle to be physically inside
    /// <paramref name="physicalRoot"/> before anything is written (CRUU15-002/CRUU16-007).
    /// Fails if anything already occupies the pathname, and leaves such an object untouched.
    /// </summary>
    IOwnedFileStage CreateOwnedStage(string physicalRoot, string path);

    /// <summary>
    /// Durably records both possible locations and exact identity before final publication.
    /// </summary>
    MigrationArtifactClaim RecordMigrationArtifactPrepared(
        string physicalRoot,
        string tempPath,
        string finalPath,
        string identityToken,
        long expectedLength,
        string expectedSha256Hex);

    /// <summary>Advances a pre-publication claim after the exact object reaches its final name.</summary>
    void RecordMigrationArtifactPublished(string physicalRoot, MigrationArtifactClaim claim);

    /// <summary>
    /// Deletes a promoted payload object only if a durable record proves this attempt created
    /// the exact object now at that pathname.
    /// </summary>
    ArtifactCleanupOutcome DeleteOwnedFinalIfProven(string physicalRoot, string path);

    /// <summary>
    /// Deletes the object at <paramref name="path"/> only if a durable ownership record proves
    /// this application created it (CRUU15-006). An object with no such proof is preserved and
    /// reported, never destroyed on the strength of its pathname alone.
    /// </summary>
    ArtifactCleanupOutcome DeleteOwnedFileIfProven(string physicalRoot, string path);

    /// <summary>
    /// Removes the exact directory object at <paramref name="path"/> through a single retained
    /// handle, so the directory that was inspected and the directory that is removed are
    /// provably the same object. Fails if it is not empty when the removal is applied.
    /// </summary>
    void DeleteDirectoryExact(string physicalRoot, string path);

    /// <summary>
    /// Settles the ownership journal for <paramref name="physicalRoot"/>: proven-owned
    /// leftovers are destroyed, interrupted atomic replacements are restored, dead claims are
    /// dropped, and the journal file itself is removed once nothing is left to claim. Called at
    /// the points where the managed tree must contain no in-flight control state.
    /// </summary>
    void RetireOwnedArtifacts(string physicalRoot);

    /// <summary>
    /// Retires only the deletion authority for migration finals after the settings point of
    /// no return. The published files remain untouched; releasing their claims allows the
    /// append-only ledger to disappear instead of accumulating dead history indefinitely.
    /// </summary>
    void RetireCommittedMigrationArtifacts(string physicalRoot);

    IEnumerable<string> EnumeratePromptFiles(string directory);
    bool FileExists(string path);
    bool DirectoryExists(string path);
    StrictPathProbe ProbePath(string path);
    IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern = "*");
    IReadOnlyList<string> EnumerateEntries(string directory);
}

internal sealed class DefaultMigrationFileOps : IMigrationFileOps
{
    private readonly StrictPathAuthority _strictPathAuthority = new();
    private readonly IOwnedArtifactJournal _ownedArtifacts;

    public DefaultMigrationFileOps()
        : this(null)
    {
    }

    internal DefaultMigrationFileOps(IOwnedArtifactJournal? ownedArtifacts)
    {
        _ownedArtifacts = ownedArtifacts ?? new WindowsOwnedArtifactJournal();
    }

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public Stream CreateNewFile(string path)
    {
        return new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);
    }

    public Stream OpenRead(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
    }

    public void FlushToDisk(Stream stream)
    {
        if (stream is not FileStream fs)
        {
            throw new InvalidOperationException("Durable migration flush requires a FileStream.");
        }

        fs.Flush(flushToDisk: true);
    }

    public IOwnedFileStage CreateOwnedStage(string physicalRoot, string path)
    {
        var stage = new OwnedMigrationStage(
            WindowsOwnedDurableStage.CreateNewUnderRoot(path, physicalRoot));
        try
        {
            // Claim it durably while the handle is still held, so a crash before promotion
            // leaves behind an artifact whose provenance a later process can actually prove.
            RecordStageOwnership(_ownedArtifacts, path, stage.IdentityToken);
            return stage;
        }
        catch
        {
            stage.Dispose();
            throw;
        }
    }

    public MigrationArtifactClaim RecordMigrationArtifactPrepared(
        string physicalRoot,
        string tempPath,
        string finalPath,
        string identityToken,
        long expectedLength,
        string expectedSha256Hex)
    {
        string root = Path.GetFullPath(physicalRoot);
        string fullTemp = Path.GetFullPath(tempPath);
        string fullFinal = Path.GetFullPath(finalPath);

        if (!WindowsFileIdentity.TryParseToken(identityToken, out WindowsFileIdentity identity))
        {
            throw new InvalidOperationException(
                $"Migration stage produced an unparsable identity token for '{tempPath}'.");
        }

        var claim = new MigrationArtifactClaim(
            Guid.NewGuid(),
            fullTemp,
            fullFinal,
            identity,
            expectedLength,
            expectedSha256Hex);

        _ownedArtifacts.Record(root, ToRecord(root, claim, OwnedArtifactPhase.Claimed));
        return claim;
    }

    public void RecordMigrationArtifactPublished(string physicalRoot, MigrationArtifactClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        string root = Path.GetFullPath(physicalRoot);
        _ownedArtifacts.Record(root, ToRecord(root, claim, OwnedArtifactPhase.CandidatePublished));
    }

    private static OwnedArtifactRecord ToRecord(
        string root,
        MigrationArtifactClaim claim,
        OwnedArtifactPhase phase) =>
        new(
            claim.OperationId,
            OwnedArtifactKind.MigrationArtifact,
            phase,
            Path.GetRelativePath(root, claim.TempPath),
            claim.Identity,
            Path.GetRelativePath(root, claim.FinalPath),
            claim.ExpectedSha256Hex,
            claim.ExpectedLength);

    public ArtifactCleanupOutcome DeleteOwnedFinalIfProven(string physicalRoot, string path) =>
        ProvenanceBoundCleanup.DeleteFileIfProven(physicalRoot, path, _ownedArtifacts);

    /// <summary>
    /// Records a stage in the ownership journal of the data root it lives in. The root is
    /// derived by walking up from the stage until a directory holding managed control state is
    /// found, so a prompt-body stage under <c>prompts\</c> is recorded in the same journal as
    /// a root-level one.
    /// </summary>
    internal static void RecordStageOwnership(IOwnedArtifactJournal journal, string path, string identityToken)
    {
        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full)
            ?? throw new ArgumentException($"Stage path has no directory: '{path}'.", nameof(path));

        string root = ResolveJournalRoot(directory);

        if (!WindowsFileIdentity.TryParseToken(identityToken, out WindowsFileIdentity identity))
        {
            throw new InvalidOperationException($"Owned stage produced an unparsable identity token for '{path}'.");
        }

        journal.Record(root, new OwnedArtifactRecord(
            Guid.NewGuid(),
            OwnedArtifactKind.Stage,
            OwnedArtifactPhase.Claimed,
            Path.GetRelativePath(root, full),
            identity));
    }

    internal static string ResolveJournalRoot(string directory)
    {
        string name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(name, "prompts", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "recovery", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(directory) ?? directory;
        }

        return directory;
    }

    public ArtifactCleanupOutcome DeleteOwnedFileIfProven(string physicalRoot, string path) =>
        ProvenanceBoundCleanup.DeleteFileIfProven(physicalRoot, path, _ownedArtifacts);

    public void DeleteDirectoryExact(string physicalRoot, string path)
    {
        using WindowsRetirableDirectory? directory =
            WindowsRetirableDirectory.OpenExistingOrNull(path, physicalRoot);
        directory?.DeleteExact();
    }

    public void RetireOwnedArtifacts(string physicalRoot)
    {
        ReconcileOwnedArtifacts(physicalRoot, retireCommittedMigrationArtifacts: false);
    }

    public void RetireCommittedMigrationArtifacts(string physicalRoot)
    {
        ReconcileOwnedArtifacts(physicalRoot, retireCommittedMigrationArtifacts: true);
    }

    private void ReconcileOwnedArtifacts(string physicalRoot, bool retireCommittedMigrationArtifacts)
    {
        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(
                physicalRoot,
                _ownedArtifacts,
                retireCommittedMigrationArtifacts);

        ReconciliationOutcome[] problems =
            [.. result.Outcomes.Where(o => o.Severity != ReconciliationSeverity.Notice)];

        if (problems.Length > 0)
        {
            throw new IOException(
                "Transient ownership records could not be settled for " +
                $"'{physicalRoot}': {string.Join("; ", problems.Select(o => $"{o.Path}: {o.Message}"))}");
        }
    }

    public IEnumerable<string> EnumeratePromptFiles(string directory)
    {
        StrictPathProbe probe = ProbePath(directory);
        if (probe.Kind != StrictPathKind.Directory)
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.md");
    }

    // Retained for interfaces that haven't migrated completely, but implemented strictly
    public bool FileExists(string path) => ProbePath(path).Kind == StrictPathKind.File;
    public bool DirectoryExists(string path) => ProbePath(path).Kind == StrictPathKind.Directory;

    public StrictPathProbe ProbePath(string path)
    {
        return _strictPathAuthority.Probe(path);
    }

    public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern = "*")
    {
        StrictPathProbe probe = ProbePath(directory);
        if (probe.Kind != StrictPathKind.Directory)
        {
            return [];
        }

        var list = new List<string>();
        foreach (string file in Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly))
        {
            list.Add(file);
        }
        return list;
    }

    public IReadOnlyList<string> EnumerateEntries(string directory)
    {
        StrictPathProbe probe = ProbePath(directory);
        if (probe.Kind != StrictPathKind.Directory)
        {
            return [];
        }

        var list = new List<string>();
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            list.Add(entry);
        }
        return list;
    }

    private sealed class OwnedMigrationStage : IOwnedFileStage
    {
        private readonly WindowsOwnedDurableStage _stage;

        public OwnedMigrationStage(WindowsOwnedDurableStage stage) => _stage = stage;

        public string IdentityToken => _stage.Identity.ToToken();

        public void Write(ReadOnlySpan<byte> bytes) => _stage.Write(bytes);
        public void FlushDurable() => _stage.FlushDurable();
        public void PromoteReplaceExact(string targetPath) => _stage.PromoteReplaceExact(targetPath);
        public void PromoteNoOverwriteExact(string targetPath) => _stage.PromoteNoOverwriteExact(targetPath);
        public void DeleteExact() => _stage.DeleteExact();
        public void Dispose() => _stage.Dispose();
    }
}
