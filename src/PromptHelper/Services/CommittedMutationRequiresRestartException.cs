using System;
using System.IO;

namespace PromptHelper.Services;

public enum LibraryMutationMetadataState
{
    Missing,
    OldOnly,
    NewOnly,
    OldAndNewSameBytes,
    Other
}

public class CommittedMutationRequiresRestartException : IOException
{
    public Guid OperationId { get; }

    public CommittedMutationRequiresRestartException(
        Guid operationId,
        string message,
        Exception? inner = null)
        : base(message, inner)
    {
        OperationId = operationId;
    }
}

/// <summary>
/// The atomic replacement crossed its filesystem point of no return, but durable recovery
/// bookkeeping or cleanup did not finish. Callers must treat the mutation as committed and
/// prevent another mutation until startup reconciliation runs.
/// </summary>
public sealed class CommittedAtomicReplacementRequiresRestartException
    : CommittedMutationRequiresRestartException
{
    public string TargetPath { get; }

    public CommittedAtomicReplacementRequiresRestartException(
        Guid operationId,
        string targetPath,
        string message,
        Exception inner)
        : base(operationId, message, inner)
    {
        TargetPath = targetPath;
    }
}
