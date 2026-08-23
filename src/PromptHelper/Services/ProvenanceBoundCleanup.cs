using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>
/// Destroys a transient artifact only when a durable ownership record proves this application
/// created the exact object now sitting at that pathname (CRUU15-006).
/// </summary>
/// <remarks>
/// The reparse-point and strict-descendant checks that recovery already performed proved the
/// object is a regular file in the right place. They did not prove it is <i>ours</i>. After a
/// crash any process can leave a regular file at a pathname the manifest declares, and
/// deleting it because it is "a regular file at a declared path" destroys foreign data. The
/// only thing that separates the two is the identity recorded when the object was created.
/// </remarks>
internal static class ProvenanceBoundCleanup
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

    public static ArtifactCleanupOutcome DeleteFileIfProven(
        string physicalRoot,
        string path,
        IOwnedArtifactJournal journal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(journal);

        string fullPath = Path.GetFullPath(path);
        string relativePath = Path.GetRelativePath(Path.GetFullPath(physicalRoot), fullPath);

        SafeFileHandle? handle = OpenExactNonReparse(fullPath, physicalRoot);
        if (handle is null)
        {
            return ArtifactCleanupOutcome.Missing;
        }

        using (handle)
        {
            WindowsFileIdentity actual = WindowsFileIdentity.FromHandle(handle);

            bool proven = false;
            foreach (OwnedArtifactRecord record in journal.Read(physicalRoot).Records)
            {
                if (string.Equals(record.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase) &&
                    record.Identity == actual)
                {
                    proven = true;
                    break;
                }
            }

            if (!proven)
            {
                return ArtifactCleanupOutcome.PreservedUnproven;
            }

            WindowsHandleDeletion.MarkForDeletion(handle, fullPath);
            return ArtifactCleanupOutcome.DeletedProvenOwned;
        }
    }

    /// <summary>
    /// Deletes a migration final only when the current or legacy ownership record proves the
    /// exact object and the manifest proves the exact expected bytes. Current
    /// <see cref="OwnedArtifactKind.MigrationArtifact"/> records name the final in
    /// <see cref="OwnedArtifactRecord.RestoreRelativePath"/>; legacy MigrationFinal records
    /// named it in RelativePath. Retry recovery must understand both protocols.
    /// </summary>
    public static ArtifactCleanupOutcome DeleteMigrationFinalIfProven(
        string physicalRoot,
        string path,
        long expectedLength,
        string expectedSha256Hex,
        IOwnedArtifactJournal journal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256Hex);
        ArgumentNullException.ThrowIfNull(journal);

        string fullRoot = Path.GetFullPath(physicalRoot);
        string fullPath = Path.GetFullPath(path);
        string relativePath = Path.GetRelativePath(fullRoot, fullPath);

        SafeFileHandle? handle = OpenExactNonReparse(fullPath, fullRoot);
        if (handle is null)
        {
            return ArtifactCleanupOutcome.Missing;
        }

        using (handle)
        {
            WindowsFileIdentity actual = WindowsFileIdentity.FromHandle(handle);
            OwnedArtifactRecord? authority = journal.Read(fullRoot).Records.FirstOrDefault(record =>
                record.Identity == actual &&
                ((record.Kind == OwnedArtifactKind.MigrationArtifact &&
                  string.Equals(record.RestoreRelativePath, relativePath, StringComparison.OrdinalIgnoreCase)) ||
                 (record.Kind == OwnedArtifactKind.MigrationFinal &&
                  string.Equals(record.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))));

            if (authority is null)
            {
                return ArtifactCleanupOutcome.PreservedUnproven;
            }

            // A current record carries its own content authority. It must agree with the
            // manifest rather than allowing either authority to silently override the other.
            if (authority.Kind == OwnedArtifactKind.MigrationArtifact &&
                (authority.CandidateLength != expectedLength ||
                 !string.Equals(
                     authority.CandidateSha256Hex,
                     expectedSha256Hex,
                     StringComparison.OrdinalIgnoreCase)))
            {
                return ArtifactCleanupOutcome.PreservedUnproven;
            }

            if (!MatchesExpectedContent(handle, expectedLength, expectedSha256Hex))
            {
                return ArtifactCleanupOutcome.PreservedUnproven;
            }

            WindowsHandleDeletion.MarkForDeletion(handle, fullPath);
            return ArtifactCleanupOutcome.DeletedProvenOwned;
        }
    }

    public static ArtifactCleanupOutcome DeleteCapabilityProbeIfProven(
        string physicalRoot,
        string path,
        long expectedLength,
        string expectedSha256Hex,
        long? alternateExpectedLength,
        string? alternateExpectedSha256Hex,
        IOwnedArtifactJournal journal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256Hex);
        ArgumentNullException.ThrowIfNull(journal);

        string fullRoot = Path.GetFullPath(physicalRoot);
        string fullPath = Path.GetFullPath(path);
        string relativePath = Path.GetRelativePath(fullRoot, fullPath);
        SafeFileHandle? handle = OpenExactNonReparse(fullPath, fullRoot);
        if (handle is null)
        {
            return ArtifactCleanupOutcome.Missing;
        }

        using (handle)
        {
            WindowsFileIdentity actualIdentity = WindowsFileIdentity.FromHandle(handle);
            OwnedArtifactRecord? authority = journal.Read(fullRoot).Records.FirstOrDefault(record =>
                record.Kind == OwnedArtifactKind.Stage &&
                record.Identity == actualIdentity &&
                string.Equals(record.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

            if (authority is null || authority.CandidateSha256Hex is null)
            {
                return ArtifactCleanupOutcome.PreservedUnproven;
            }

            bool primaryAuthority =
                authority.CandidateLength == expectedLength &&
                string.Equals(authority.CandidateSha256Hex, expectedSha256Hex, StringComparison.OrdinalIgnoreCase);
            bool alternateAuthority =
                alternateExpectedLength is not null &&
                alternateExpectedSha256Hex is not null &&
                authority.CandidateLength == alternateExpectedLength.Value &&
                string.Equals(
                    authority.CandidateSha256Hex,
                    alternateExpectedSha256Hex,
                    StringComparison.OrdinalIgnoreCase);

            if ((!primaryAuthority && !alternateAuthority) ||
                !MatchesExpectedContent(
                    handle,
                    authority.CandidateLength,
                    authority.CandidateSha256Hex))
            {
                return ArtifactCleanupOutcome.PreservedUnproven;
            }

            WindowsHandleDeletion.MarkForDeletion(handle, fullPath);
            return ArtifactCleanupOutcome.DeletedProvenOwned;
        }
    }

    public static ArtifactCleanupOutcome DeleteDirectoryIfProven(
        string physicalRoot,
        string path,
        IOwnedArtifactJournal journal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(journal);

        string fullRoot = Path.GetFullPath(physicalRoot);
        string fullPath = Path.GetFullPath(path);
        string relativePath = Path.GetRelativePath(fullRoot, fullPath);

        using WindowsRetirableDirectory? directory =
            WindowsRetirableDirectory.OpenExistingOrNull(fullPath, fullRoot);
        if (directory is null)
        {
            return ArtifactCleanupOutcome.Missing;
        }

        bool proven = journal.Read(fullRoot).Records.Any(record =>
            record.Kind == OwnedArtifactKind.MigrationDirectory &&
            record.Identity == directory.Identity &&
            string.Equals(record.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

        if (!proven)
        {
            return ArtifactCleanupOutcome.PreservedUnproven;
        }

        directory.DeleteExact();
        return ArtifactCleanupOutcome.DeletedProvenOwned;
    }

    internal static bool MatchesExpectedContent(
        SafeFileHandle handle,
        long expectedLength,
        string expectedSha256Hex)
    {
        long length = RandomAccess.GetLength(handle);
        if (length != expectedLength || length > int.MaxValue)
        {
            return false;
        }

        byte[] bytes = new byte[(int)length];
        int read = 0;
        while (read < bytes.Length)
        {
            int count = RandomAccess.Read(handle, bytes.AsSpan(read), read);
            if (count <= 0)
            {
                return false;
            }

            read += count;
        }

        string actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return string.Equals(actual, expectedSha256Hex, StringComparison.OrdinalIgnoreCase);
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

            throw new IOException($"Unable to open '{path}' for provenance-bound cleanup.", new Win32Exception(error));
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
                    $"Unable to inspect attributes for '{path}'.",
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
