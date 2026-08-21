using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PromptHelper.Services;

public sealed class DataRootCapabilityProbeException : IOException
{
    public DataRootCapabilityProbeException(
        string root,
        Exception original,
        IReadOnlyList<MigrationRollbackFailure> cleanupFailures)
        : base(
            BuildMessage(root, cleanupFailures),
            original)
    {
        Root = root;
        CleanupFailures = cleanupFailures;
    }

    public string Root { get; }
    public IReadOnlyList<MigrationRollbackFailure> CleanupFailures { get; }

    private static string BuildMessage(
        string root,
        IReadOnlyList<MigrationRollbackFailure> cleanupFailures)
    {
        string details = string.Join(
            Environment.NewLine,
            cleanupFailures.Select(x => $"- {x.Operation}: {x.Path}: {x.Message}"));

        return
            $"Capability validation failed in '{root}', and probe cleanup could not " +
            "remove all probe artifacts:" +
            Environment.NewLine +
            details;
    }
}
