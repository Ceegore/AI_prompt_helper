using System;
using PromptHelper.Services;

namespace PromptHelper.Tests;

/// <summary>
/// Wraps a real <see cref="IOwnedFileStage"/> so a test can fail one step of a durable
/// promotion without replacing the promotion itself. The staging file is genuinely created and
/// genuinely handle-bound; only the chosen step is diverted.
/// </summary>
internal sealed class FakeOwnedFileStage : IOwnedFileStage
{
    private readonly IOwnedFileStage _inner;
    private readonly string _identityToken;

    public FakeOwnedFileStage(IOwnedFileStage inner)
    {
        _inner = inner;
        _identityToken = inner.IdentityToken;
    }

    public Action<ReadOnlyMemory<byte>>? OnWrite { get; set; }
    public Action? OnFlushDurable { get; set; }
    public Action<string>? OnPromoteReplaceExact { get; set; }
    public Action<string>? OnPromoteNoOverwriteExact { get; set; }
    public Action? OnDeleteExact { get; set; }

    // Keep the creation identity available to the caller even when a fault-injection seam
    // deliberately releases the inner handle immediately after promotion.
    public string IdentityToken => _identityToken;

    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (OnWrite != null)
        {
            OnWrite(bytes.ToArray());
            return;
        }

        _inner.Write(bytes);
    }

    public void FlushDurable()
    {
        if (OnFlushDurable != null)
        {
            OnFlushDurable();
            return;
        }

        _inner.FlushDurable();
    }

    public void PromoteReplaceExact(string targetPath)
    {
        if (OnPromoteReplaceExact != null)
        {
            OnPromoteReplaceExact(targetPath);
            return;
        }

        _inner.PromoteReplaceExact(targetPath);
    }

    public void PromoteNoOverwriteExact(string targetPath)
    {
        if (OnPromoteNoOverwriteExact != null)
        {
            OnPromoteNoOverwriteExact(targetPath);
            return;
        }

        _inner.PromoteNoOverwriteExact(targetPath);
    }

    public void DeleteExact()
    {
        if (OnDeleteExact != null)
        {
            OnDeleteExact();
            return;
        }

        _inner.DeleteExact();
    }

    public void Dispose() => _inner.Dispose();
}
