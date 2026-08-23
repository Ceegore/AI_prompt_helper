using System;
using System.IO;
using Microsoft.Win32.SafeHandles;
using PromptHelper.Services;

namespace PromptHelper.Tests;

/// <summary>
/// Records the ownership claim a real interrupted operation would have left behind, so a test
/// can seed a leftover artifact that recovery is <i>entitled</i> to clean up.
/// </summary>
/// <remarks>
/// Since CRUU15-006/CRUU15-007 nothing is auto-destroyed on the strength of its pathname, so a
/// test that drops a file at a declared temp path and expects it to disappear is describing
/// foreign data, not a leftover. Seeding through here is what makes the artifact this
/// application's own.
/// </remarks>
internal static class OwnedArtifactTestSupport
{
    public static void ClaimOwnership(
        string root,
        string fullPath,
        OwnedArtifactKind kind = OwnedArtifactKind.Stage,
        string? restoreRelativePath = null)
    {
        string fullRoot = Path.GetFullPath(root);
        string full = Path.GetFullPath(fullPath);

        using SafeFileHandle handle = File.OpenHandle(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        new WindowsOwnedArtifactJournal().Record(
            fullRoot,
            new OwnedArtifactRecord(
                Guid.NewGuid(),
                kind,
                OwnedArtifactPhase.Claimed,
                Path.GetRelativePath(fullRoot, full),
                WindowsFileIdentity.FromHandle(handle),
                restoreRelativePath));
    }

    /// <summary>
    /// Records the ownership a migration would have transferred to a payload object when it
    /// promoted it, so recovery and rollback are entitled to remove it again (CRUU16-005).
    /// </summary>
    public static void ClaimPromotedFinal(string root, string fullPath)
    {
        string fullRoot = Path.GetFullPath(root);
        string full = Path.GetFullPath(fullPath);

        using SafeFileHandle handle = File.OpenHandle(
            full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        new WindowsOwnedArtifactJournal().Record(
            fullRoot,
            new OwnedArtifactRecord(
                Guid.NewGuid(),
                OwnedArtifactKind.MigrationFinal,
                OwnedArtifactPhase.CandidatePublished,
                Path.GetRelativePath(fullRoot, full),
                WindowsFileIdentity.FromHandle(handle)));
    }
}
