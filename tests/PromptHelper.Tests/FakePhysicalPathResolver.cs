using System;
using System.Collections.Generic;
using System.IO;
using PromptHelper.Services;

namespace PromptHelper.Tests;

public sealed class FakePhysicalPathResolver : IPhysicalPathResolver
{
    private readonly Dictionary<string, string> _mappings = new(StringComparer.OrdinalIgnoreCase);
    private int _callCount;

    public Exception? Failure { get; set; }
    public Func<string, int, string?>? DynamicResolver { get; set; }

    public void AddMapping(string alias, string target)
    {
        _mappings[PathIdentity.NormalizeForComparison(alias)] = PathIdentity.NormalizeForComparison(target);
    }

    public string ResolveWithNearestExistingAncestor(string path)
    {
        _callCount++;

        if (Failure is not null)
        {
            throw Failure;
        }

        if (DynamicResolver is not null)
        {
            string? dynamicResult = DynamicResolver(path, _callCount);
            if (dynamicResult != null)
            {
                return PathIdentity.NormalizeForComparison(dynamicResult);
            }
        }

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
