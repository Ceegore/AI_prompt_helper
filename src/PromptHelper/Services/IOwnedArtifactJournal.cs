using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>What a durably-recorded transient artifact is, and therefore how recovery may treat it.</summary>
internal enum OwnedArtifactKind
{
    /// <summary>A staging file for a durable write. Safe to destroy on recovery once identity is proven.</summary>
    Stage,

    /// <summary>
    /// The previous content of a compare-and-swap target, renamed aside so the staged
    /// replacement could take its name. If the target is missing this is the *only* copy of the
    /// committed data and must be restored, not destroyed.
    /// </summary>
    CasPreimage
}

/// <summary>
/// One durably recorded claim of ownership over a transient artifact. Paths are relative to
/// the data root the journal belongs to, so a single journal covers the whole managed tree.
/// </summary>
internal sealed record OwnedArtifactRecord(
    OwnedArtifactKind Kind,
    string RelativePath,
    WindowsFileIdentity Identity,
    string? RestoreRelativePath);

/// <summary>
/// The durable provenance authority for transient artifacts (CRUU15-006/CRUU15-007).
/// </summary>
/// <remarks>
/// <para>A pathname is not ownership. A GUID in a filename solves collisions, not provenance:
/// after a crash any process can leave a file at a pathname that matches Prompt Helper's temp
/// grammar exactly, and a reconciler that deletes "whatever regular file is currently there"
/// destroys foreign data. What *is* ownership is the object identity recorded at the moment
/// this process created the object — volume serial plus NTFS file ID, which no later
/// substitution can reproduce.</para>
/// <para>Ownership is claimed by appending a record here, flushed to disk, immediately after
/// the artifact is created and while its handle is still held. Recovery may auto-destroy an
/// artifact only when a record exists <b>and</b> the object currently at that pathname still
/// has the recorded identity. Everything else is preserved.</para>
/// </remarks>
internal interface IOwnedArtifactJournal
{
    /// <summary>Durably records ownership of an artifact inside <paramref name="root"/>.</summary>
    void Record(string root, OwnedArtifactRecord record);

    /// <summary>Reads every ownership record for <paramref name="root"/>. Never throws for a missing journal.</summary>
    IReadOnlyList<OwnedArtifactRecord> Read(string root);

    /// <summary>Rewrites the journal for <paramref name="root"/> to exactly <paramref name="surviving"/>.</summary>
    void Rewrite(string root, IReadOnlyList<OwnedArtifactRecord> surviving);
}

/// <summary>
/// Append-only, one journal file per data root. Records are single lines so a torn tail (a
/// crash mid-append) discards only the incomplete final record instead of invalidating the
/// file. The journal is itself reserved ephemeral control state
/// (<see cref="ManagedControlPathPolicy.IsReservedEphemeralRootControl"/>), so the managed-tree
/// invariants recognise it rather than reporting it as a foreign entry.
/// </summary>
internal sealed class WindowsOwnedArtifactJournal : IOwnedArtifactJournal
{
    internal const string JournalFileName = ".prompthelper-owned.log";

    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint OPEN_ALWAYS = 4;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_ATTRIBUTE_HIDDEN = 0x00000002;
    private const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;

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

    public static string GetJournalPath(string root) => Path.Combine(root, JournalFileName);

    public void Record(string root, OwnedArtifactRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(record);

        byte[] line = Encoding.UTF8.GetBytes(Serialize(record) + "\n");
        string journalPath = GetJournalPath(root);

        using SafeFileHandle handle = CreateFileW(
            journalPath,
            GENERIC_WRITE,
            FILE_SHARE_READ,
            IntPtr.Zero,
            OPEN_ALWAYS,
            FILE_ATTRIBUTE_NORMAL | FILE_ATTRIBUTE_HIDDEN | FILE_FLAG_WRITE_THROUGH,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            throw new IOException(
                $"Unable to record artifact ownership in '{journalPath}'.",
                new Win32Exception(error));
        }

        long offset = RandomAccess.GetLength(handle);
        RandomAccess.Write(handle, line, offset);

        if (!FlushFileBuffers(handle))
        {
            throw new IOException(
                $"Unable to flush the artifact ownership journal '{journalPath}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    public IReadOnlyList<OwnedArtifactRecord> Read(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        string journalPath = GetJournalPath(root);
        byte[] raw;
        try
        {
            raw = File.ReadAllBytes(journalPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }

        var records = new List<OwnedArtifactRecord>();
        foreach (string line in Encoding.UTF8.GetString(raw).Split('\n'))
        {
            if (TryDeserialize(line, out OwnedArtifactRecord? record))
            {
                records.Add(record);
            }
        }

        return records;
    }

    public void Rewrite(string root, IReadOnlyList<OwnedArtifactRecord> surviving)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(surviving);

        string journalPath = GetJournalPath(root);

        if (surviving.Count == 0)
        {
            try
            {
                new WindowsVerifiedArtifactDeleter().VerifyIdentityAndDelete(root, journalPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // A journal that cannot be removed is harmless: every record in it is dead.
            }

            return;
        }

        var builder = new StringBuilder();
        foreach (OwnedArtifactRecord record in surviving)
        {
            builder.Append(Serialize(record)).Append('\n');
        }

        // The journal is itself a durable artifact, but it must not recurse back into
        // ownership recording, so it is promoted through a bare owned stage.
        string stagePath = Path.Combine(
            root,
            $".prompthelper-tmp-mutation-{Guid.NewGuid():N}.tmp");

        using var stage = WindowsOwnedDurableStage.CreateNew(stagePath);
        try
        {
            stage.Write(Encoding.UTF8.GetBytes(builder.ToString()));
            stage.FlushDurable();
            stage.PromoteReplaceExact(journalPath);
        }
        catch
        {
            try
            {
                stage.DeleteExact();
            }
            catch
            {
                // Surfaced through the primary failure.
            }

            throw;
        }
    }

    private static string Serialize(OwnedArtifactRecord record) =>
        string.Join(
            '|',
            "1",
            record.Kind switch
            {
                OwnedArtifactKind.Stage => "stage",
                OwnedArtifactKind.CasPreimage => "preimage",
                _ => throw new ArgumentOutOfRangeException(nameof(record))
            },
            record.Identity.ToToken(),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(record.RelativePath)),
            record.RestoreRelativePath is null
                ? string.Empty
                : Convert.ToBase64String(Encoding.UTF8.GetBytes(record.RestoreRelativePath)));

    private static bool TryDeserialize(string line, out OwnedArtifactRecord record)
    {
        record = null!;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        string[] parts = line.Trim().Split('|');
        if (parts.Length != 5 || parts[0] != "1")
        {
            return false;
        }

        OwnedArtifactKind kind = parts[1] switch
        {
            "stage" => OwnedArtifactKind.Stage,
            "preimage" => OwnedArtifactKind.CasPreimage,
            _ => (OwnedArtifactKind)(-1)
        };

        if (!Enum.IsDefined(kind) ||
            !WindowsFileIdentity.TryParseToken(parts[2], out WindowsFileIdentity identity))
        {
            return false;
        }

        string relativePath;
        string? restoreRelativePath;
        try
        {
            relativePath = Encoding.UTF8.GetString(Convert.FromBase64String(parts[3]));
            restoreRelativePath = parts[4].Length == 0
                ? null
                : Encoding.UTF8.GetString(Convert.FromBase64String(parts[4]));
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

        record = new OwnedArtifactRecord(kind, relativePath, identity, restoreRelativePath);
        return true;
    }

    /// <summary>
    /// A journal record is a file on disk that an attacker who can write the data root could
    /// also craft, so its path is never trusted to stay inside the root: it must be relative,
    /// separator-normalised and free of traversal segments before anything opens it.
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
