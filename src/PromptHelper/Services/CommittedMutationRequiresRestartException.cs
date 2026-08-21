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

public sealed class CommittedMutationRequiresRestartException : IOException
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
