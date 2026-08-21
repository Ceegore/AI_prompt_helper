using System;
using System.IO;

namespace PromptHelper.Services;

public sealed class MigrationRecoveryException : IOException
{
    public string TargetRoot { get; }
    public string Operation { get; }

    public MigrationRecoveryException(
        string targetRoot,
        string operation,
        Exception inner)
        : base(
            $"Migration recovery failed during '{operation}' for '{targetRoot}': {inner.Message}",
            inner)
    {
        TargetRoot = targetRoot;
        Operation = operation;
    }
}
