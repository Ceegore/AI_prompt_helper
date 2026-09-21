using System;

namespace PromptHelper.Services;

/// <summary>The state a caller requires the target to be in for its replacement to be legal.</summary>
public enum ExpectedFileStateKind
{
    /// <summary>The target must currently be a file whose exact bytes hash to the expected digest.</summary>
    Present,

    /// <summary>The target must currently not exist. Enforced by no-overwrite promotion, never by an earlier probe.</summary>
    Missing
}

/// <summary>
/// The precondition an atomic replacement is bound to. See
/// <see cref="IAtomicExpectedFileReplacer"/> for how it is enforced.
/// </summary>
public sealed class ExpectedFileState
{
    private ExpectedFileState(
        ExpectedFileStateKind kind,
        string? expectedSha256Hex,
        FileObjectIdentity? expectedIdentity)
    {
        Kind = kind;
        ExpectedSha256Hex = expectedSha256Hex;
        ExpectedIdentity = expectedIdentity;
    }

    public ExpectedFileStateKind Kind { get; }

    /// <summary>Non-null exactly when <see cref="Kind"/> is <see cref="ExpectedFileStateKind.Present"/>.</summary>
    public string? ExpectedSha256Hex { get; }

    /// <summary>
    /// Optional exact-object authority. When present, byte-equivalent replacement objects are
    /// stale rather than interchangeable with the object the caller originally read.
    /// </summary>
    internal FileObjectIdentity? ExpectedIdentity { get; }

    public static ExpectedFileState Present(string expectedSha256Hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256Hex);
        return new ExpectedFileState(ExpectedFileStateKind.Present, expectedSha256Hex, null);
    }

    internal static ExpectedFileState Present(
        string expectedSha256Hex,
        FileObjectIdentity expectedIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256Hex);
        return new ExpectedFileState(
            ExpectedFileStateKind.Present,
            expectedSha256Hex,
            expectedIdentity);
    }

    public static ExpectedFileState Missing { get; } = new(ExpectedFileStateKind.Missing, null, null);
}

/// <summary>
/// A genuine compare-and-swap over a file: the expected current state is proven and the
/// implementation must keep that proof bound to the exact object until publication either
/// consumes it or fails closed. Platforms may enforce this with a retained exclusion or with
/// a reversible identity-verified rename protocol; a detached "verify, then later replace"
/// sequence is not sufficient.
/// </summary>
/// <remarks>
/// <para>Both expectations fail *closed*: if the expectation no longer holds, the operation
/// throws <see cref="StaleExpectedFileException"/> and whatever is currently at the target is
/// left exactly as it is. Nothing at the target is ever destroyed unless it was proven to be
/// the expected object.</para>
/// </remarks>
internal interface IAtomicExpectedFileReplacer
{
    /// <summary>
    /// Atomically publishes <paramref name="candidateBytes"/> at <paramref name="targetPath"/>
    /// if and only if <paramref name="expected"/> still describes the target.
    /// </summary>
    /// <param name="physicalRoot">
    /// The data root that both the target and the staging artifact must physically resolve
    /// inside; proven from retained handles, not from pathname arithmetic.
    /// </param>
    /// <exception cref="StaleExpectedFileException">The expectation no longer holds.</exception>
    void ReplaceIfExpected(
        string physicalRoot,
        string targetPath,
        ExpectedFileState expected,
        ReadOnlySpan<byte> candidateBytes,
        DurableFileClass fileClass);
}

/// <summary>Thrown when the object at the expected path no longer matches the bound precondition.</summary>
public sealed class StaleExpectedFileException : InvalidOperationException
{
    public StaleExpectedFileException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
