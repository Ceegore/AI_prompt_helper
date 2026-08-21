using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PromptHelper.Services;

public sealed class TargetRootReservationAcquireException : IOException
{
    public string RootPath { get; }
    public IReadOnlyList<MigrationRollbackFailure> CleanupFailures { get; }

    public TargetRootReservationAcquireException(
        string rootPath,
        Exception innerException,
        IReadOnlyList<MigrationRollbackFailure> cleanupFailures)
        : base(
            $"Failed to acquire target root reservation for '{rootPath}' and cleanup of created directories failed: {innerException.Message} | " +
            string.Join(", ", cleanupFailures.Select(f => $"{f.Operation} on {f.Path}: {f.Message}")),
            innerException)
    {
        RootPath = rootPath;
        CleanupFailures = cleanupFailures;
    }
}
