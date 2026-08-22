using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>How a transient artifact found on disk relates to this application (CRUU15-007).</summary>
internal enum ArtifactProvenance
{
    /// <summary>A durable ownership record exists and the object at that pathname still has the recorded identity.</summary>
    JournalOwned,

    /// <summary>Matches a current temp-name grammar but nothing proves this process created it.</summary>
    UnprovenCurrentFormat,

    /// <summary>Matches only a superseded naming convention, with no identity signal at all.</summary>
    LegacyUnverifiable,

    /// <summary>A record exists for this pathname but the object there is a different object.</summary>
    Foreign
}

/// <summary>
/// Startup reconciliation for artifacts claimed in an <see cref="IOwnedArtifactJournal"/>.
/// </summary>
/// <remarks>
/// <para>Two jobs, both driven purely by recorded identity:</para>
/// <list type="bullet">
/// <item><b>Restore.</b> A <see cref="OwnedArtifactKind.CasPreimage"/> record whose object is
/// still present while its replacement target is missing means the process died between the
/// two renames of an atomic replacement. That pre-image holds the last committed content, so
/// it is renamed back — a non-destructive, no-overwrite operation that only ever fills a hole.</item>
/// <item><b>Clean.</b> Anything whose recorded identity still matches is this process's own
/// leftover and is deleted through the same handle that proved it.</item>
/// </list>
/// <para>Records whose object is missing, or whose object has a different identity, are simply
/// dropped: the pathname is left untouched, because a matching name is not evidence of
/// ownership.</para>
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

    /// <summary>
    /// Reconciles every ownership record for <paramref name="physicalRoot"/>, and returns the
    /// full paths that were proven owned so a subsequent name-grammar sweep can tell
    /// <see cref="ArtifactProvenance.JournalOwned"/> from
    /// <see cref="ArtifactProvenance.UnprovenCurrentFormat"/>.
    /// </summary>
    public static IReadOnlySet<string> Reconcile(
        string physicalRoot,
        IOwnedArtifactJournal journal,
        List<TempCleanupFailure> failures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(failures);

        string root = Path.GetFullPath(physicalRoot);
        var proven = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<OwnedArtifactRecord> records;
        try
        {
            records = journal.Read(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(new TempCleanupFailure(
                WindowsOwnedArtifactJournal.GetJournalPath(root),
                ex.Message));
            return proven;
        }

        if (records.Count == 0)
        {
            return proven;
        }

        var surviving = new List<OwnedArtifactRecord>();

        foreach (OwnedArtifactRecord record in records)
        {
            string artifactPath = Path.GetFullPath(Path.Combine(root, record.RelativePath));

            SafeFileHandle? handle;
            try
            {
                handle = OpenExactNonReparse(artifactPath, root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                failures.Add(new TempCleanupFailure(artifactPath, ex.Message));
                surviving.Add(record);
                continue;
            }

            if (handle is null)
            {
                // The artifact is gone; the claim is dead. Nothing to preserve.
                continue;
            }

            using (handle)
            {
                if (WindowsFileIdentity.FromHandle(handle) != record.Identity)
                {
                    // Something else occupies our pathname. Preserve it and forget the claim:
                    // the object we owned no longer exists under this name.
                    continue;
                }

                proven.Add(artifactPath);

                if (record.Kind == OwnedArtifactKind.CasPreimage && record.RestoreRelativePath is not null)
                {
                    if (TryRestorePreimage(root, record, handle, failures, out bool restored))
                    {
                        if (!restored)
                        {
                            surviving.Add(record);
                        }

                        continue;
                    }
                }

                try
                {
                    WindowsHandleDeletion.MarkForDeletion(handle, artifactPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failures.Add(new TempCleanupFailure(artifactPath, ex.Message));
                    surviving.Add(record);
                }
            }
        }

        try
        {
            journal.Rewrite(root, surviving);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(new TempCleanupFailure(
                WindowsOwnedArtifactJournal.GetJournalPath(root),
                ex.Message));
        }

        return proven;
    }

    /// <summary>
    /// Returns true when the record was handled as a pre-image (whether or not the restore
    /// itself succeeded), so the caller must not fall through to deletion.
    /// </summary>
    private static bool TryRestorePreimage(
        string root,
        OwnedArtifactRecord record,
        SafeFileHandle handle,
        List<TempCleanupFailure> failures,
        out bool restored)
    {
        restored = false;
        string restorePath = Path.GetFullPath(Path.Combine(root, record.RestoreRelativePath!));
        string artifactPath = Path.GetFullPath(Path.Combine(root, record.RelativePath));

        bool targetPresent;
        try
        {
            targetPresent = new StrictPathAuthority().Probe(restorePath).Kind != StrictPathKind.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(new TempCleanupFailure(artifactPath, ex.Message));
            return true;
        }

        if (targetPresent)
        {
            // The replacement landed before the crash. The pre-image is superseded and is
            // ours by recorded identity, so the caller may destroy it.
            return false;
        }

        // The atomic replacement was interrupted between moving the old object aside and
        // promoting the new one. This object is the last committed content.
        if (WindowsHandleDeletion.TryRenameNoOverwrite(handle, restorePath, out int error))
        {
            restored = true;
            return true;
        }

        failures.Add(new TempCleanupFailure(
            artifactPath,
            $"An interrupted atomic update left the previous content here, and it could not be restored to '{record.RestoreRelativePath}': {new Win32Exception(error).Message}"));
        return true;
    }

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
