using System;
using System.Linq;

namespace PromptHelper.Services;

internal static class WarningCombiner
{
    public static string? Combine(params string?[] warnings)
    {
        if (warnings is null || warnings.Length == 0)
        {
            return null;
        }

        string[] values = warnings
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return values.Length == 0
            ? null
            : string.Join("\r\n\r\n", values);
    }
}
