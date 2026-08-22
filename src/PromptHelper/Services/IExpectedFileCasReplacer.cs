using System;

namespace PromptHelper.Services;

/// <summary>
/// A commit precondition primitive for CRUU14-002/CRUU14-003: verifies that the file
/// *currently* at a path still matches an expected hash, using a native handle opened with a
/// restrictive share mode that denies concurrent writers for the (short) duration of the
/// check itself — a stronger, OS-enforced check than a plain content read, even though the
/// actual replacement that follows is a separate call through the caller's own (test-doubled)
/// durable writer. A caller that wants the narrowest possible window should call this
/// immediately before that write, not once at the start of a longer operation.
/// </summary>
public interface IExpectedFileCasReplacer
{
    /// <summary>
    /// Throws <see cref="StaleExpectedFileException"/> if the file at <paramref name="targetPath"/>
    /// no longer matches <paramref name="expectedSha256Hex"/>.
    /// </summary>
    void VerifyCurrentMatches(string targetPath, string expectedSha256Hex);
}

/// <summary>Thrown when the object at the expected path no longer matches the bound CAS precondition.</summary>
public sealed class StaleExpectedFileException : InvalidOperationException
{
    public StaleExpectedFileException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
