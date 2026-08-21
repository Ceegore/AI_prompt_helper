using System;
using System.Collections.Generic;
using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class FakeDirectoryCaseSensitivityInspector : IDirectoryCaseSensitivityInspector
{
    private readonly HashSet<string> _caseSensitiveDirectories = new(StringComparer.OrdinalIgnoreCase);

    public void MarkCaseSensitive(string directory)
    {
        _caseSensitiveDirectories.Add(PathIdentity.NormalizeForComparison(directory));
    }

    public bool IsCaseSensitive(string existingDirectory)
    {
        return _caseSensitiveDirectories.Contains(PathIdentity.NormalizeForComparison(existingDirectory));
    }
}
