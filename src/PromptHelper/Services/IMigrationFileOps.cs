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

internal interface IMigrationFileOps
{
    byte[] ReadAllBytes(string path);
    Stream CreateNewFile(string path);
    Stream OpenRead(string path);
    void FlushToDisk(Stream stream);

    /// <summary>
    /// Creates an owned stage at <paramref name="path"/> whose handle is retained through
    /// promotion or deletion (CRUU15-002). Fails if anything already occupies that pathname,
    /// and leaves such an object untouched.
    /// </summary>
    IOwnedFileStage CreateOwnedStage(string path);

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

    IEnumerable<string> EnumeratePromptFiles(string directory);
    bool FileExists(string path);
    bool DirectoryExists(string path);
    StrictPathProbe ProbePath(string path);
    void DeleteFile(string path);
    void DeleteDirectory(string path);
    IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern = "*");
    IReadOnlyList<string> EnumerateEntries(string directory);
}

internal sealed class DefaultMigrationFileOps : IMigrationFileOps
{
    private readonly StrictPathAuthority _strictPathAuthority = new();

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

    private readonly IOwnedArtifactJournal _ownedArtifacts = new WindowsOwnedArtifactJournal();

    public IOwnedFileStage CreateOwnedStage(string path)
    {
        var stage = new OwnedMigrationStage(WindowsOwnedDurableStage.CreateNew(path));
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
            OwnedArtifactKind.Stage,
            Path.GetRelativePath(root, full),
            identity,
            RestoreRelativePath: null));
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
        var failures = new List<TempCleanupFailure>();
        OwnedArtifactReconciler.Reconcile(physicalRoot, _ownedArtifacts, failures);

        if (failures.Count > 0)
        {
            throw new IOException(
                "Transient ownership records could not be settled for " +
                $"'{physicalRoot}': {string.Join("; ", failures.Select(f => $"{f.Path}: {f.ErrorMessage}"))}");
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

    public void DeleteFile(string path)
    {
        StrictPathProbe probe = ProbePath(path);
        if (probe.Kind == StrictPathKind.File)
        {
            File.Delete(path);
        }
        else if (probe.Kind == StrictPathKind.Directory)
        {
            throw new InvalidOperationException($"Expected a file but found a directory at '{path}'.");
        }
    }

    public void DeleteDirectory(string path)
    {
        StrictPathProbe probe = ProbePath(path);
        if (probe.Kind == StrictPathKind.Directory)
        {
            Directory.Delete(path);
        }
        else if (probe.Kind == StrictPathKind.File)
        {
            throw new InvalidOperationException($"Expected a directory but found a file at '{path}'.");
        }
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
