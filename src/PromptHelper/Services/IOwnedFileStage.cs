using System;

namespace PromptHelper.Services;

/// <summary>
/// A staging file owned — by retained handle — from creation through promotion or deletion.
/// This is the single injectable shape of a durable promotion in this codebase: the manifest
/// repository and the migration payload copier used to close their stage and then promote it
/// by pathname with <c>MoveFileExW</c>, which meant they promoted whatever object occupied
/// that pathname at rename time rather than the one they wrote, and deleted whatever occupied
/// it at cleanup time rather than the one they created (CRUU15-001/CRUU15-002). Nothing in
/// this interface exposes a pathname-addressed promotion or deletion, so no implementation
/// behind it can reintroduce that gap.
/// </summary>
public interface IOwnedFileStage : IDisposable
{
    /// <summary>
    /// The exact on-disk identity of the staged object, so ownership can be durably recorded
    /// and later proven by a recovery pass in a different process rather than inferred from
    /// the pathname (CRUU15-006).
    /// </summary>
    string IdentityToken { get; }

    void Write(ReadOnlySpan<byte> bytes);
    void FlushDurable();

    /// <summary>Promotes the exact staged object onto <paramref name="targetPath"/>, replacing an existing file there.</summary>
    void PromoteReplaceExact(string targetPath);

    /// <summary>Promotes the exact staged object onto <paramref name="targetPath"/>; fails if a file already exists there.</summary>
    void PromoteNoOverwriteExact(string targetPath);

    /// <summary>Deletes the exact staged object through the retained handle.</summary>
    void DeleteExact();
}
