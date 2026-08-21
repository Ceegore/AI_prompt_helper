using System;

namespace PromptHelper.Services;

public sealed class TargetInspectionUnstableException : Exception
{
    public TargetInspectionUnstableException(string message)
        : base(message)
    {
    }

    public TargetInspectionUnstableException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
