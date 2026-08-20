using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PromptHelper.Services;

public sealed record MigrationRollbackFailure(
    string Path,
    string Operation,
    string Message);

public sealed record MigrationRollbackResult(
    IReadOnlyList<MigrationRollbackFailure> Failures)
{
    public bool Success => Failures.Count == 0;
}

public sealed class MigrationRollbackException : IOException
{
    public MigrationRollbackException(
        Exception original,
        string targetRoot,
        IReadOnlyList<MigrationRollbackFailure> failures)
        : base(
            BuildMessage(targetRoot, failures),
            original)
    {
        TargetRoot = targetRoot;
        Failures = failures;
    }

    public string TargetRoot { get; }
    public IReadOnlyList<MigrationRollbackFailure> Failures { get; }

    private static string BuildMessage(
        string targetRoot,
        IReadOnlyList<MigrationRollbackFailure> failures)
    {
        string details = string.Join(
            Environment.NewLine,
            failures.Select(x =>
                $"- {x.Operation}: {x.Path}: {x.Message}"));

        return
            "The data-folder transition failed and Prompt Helper could not " +
            "fully clean the target folder. The active source library and " +
            "settings were not switched. Review this target before retrying:" +
            Environment.NewLine +
            targetRoot +
            Environment.NewLine +
            details;
    }
}
