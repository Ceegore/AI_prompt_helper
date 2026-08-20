using System;
using System.Collections.Generic;
using System.IO;
using PromptHelper.Services;

namespace PromptHelper.Tests;

public sealed class FakePhysicalPathResolver : IPhysicalPathResolver
{
    private readonly Dictionary<string, string> _mappings = new(StringComparer.OrdinalIgnoreCase);

    public void AddMapping(string alias, string target)
    {
        _mappings[PathIdentity.NormalizeForComparison(alias)] = PathIdentity.NormalizeForComparison(target);
    }

    public string ResolveWithNearestExistingAncestor(string path)
    {
        string normalized = PathIdentity.NormalizeForComparison(path);

        foreach (var kvp in _mappings)
        {
            if (PathIdentity.Equals(normalized, kvp.Key))
            {
                return kvp.Value;
            }

            if (PathIdentity.IsStrictDescendant(normalized, kvp.Key))
            {
                string relative = Path.GetRelativePath(kvp.Key, normalized);
                return Path.Combine(kvp.Value, relative);
            }
        }

        return normalized;
    }
}
