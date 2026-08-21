using System;
using System.Collections.Generic;
using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class FakeDirectoryCaseSensitivityInspector : IDirectoryCaseSensitivityInspector
{
    private readonly HashSet<string> _caseSensitiveDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failingDirectories = new(StringComparer.OrdinalIgnoreCase);

    public void MarkCaseSensitive(string directory)
    {
        _caseSensitiveDirectories.Add(PathIdentity.NormalizeForComparison(directory));
    }

    public void MarkInspectionFailure(string directory)
    {
        _failingDirectories.Add(PathIdentity.NormalizeForComparison(directory));
    }

    public DirectoryCaseSensitivityState Inspect(string existingDirectory)
    {
        string norm = PathIdentity.NormalizeForComparison(existingDirectory);
        if (_failingDirectories.Contains(norm))
        {
            throw new DirectoryCaseSensitivityInspectionException(existingDirectory, 5 /* ERROR_ACCESS_DENIED */);
        }

        return _caseSensitiveDirectories.Contains(norm)
            ? DirectoryCaseSensitivityState.CaseSensitive
            : DirectoryCaseSensitivityState.CaseInsensitive;
    }
}
