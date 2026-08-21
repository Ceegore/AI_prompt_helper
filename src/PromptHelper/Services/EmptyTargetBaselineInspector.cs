using System;
using System.Collections.Generic;
using System.IO;

namespace PromptHelper.Services;

internal sealed record EmptyTargetBaselineInspection(
    bool IsAcceptable,
    IReadOnlyList<string> UnexpectedEntries);

internal static class EmptyTargetBaselineInspector
{
    public static EmptyTargetBaselineInspection Inspect(
        string targetPhysicalRoot,
        string? bootstrapPhysicalRoot,
        bool isReservationActive = false,
        IMigrationFileOps? fileOps = null)
    {
        var ops = fileOps ?? new DefaultMigrationFileOps();

        if (!ops.DirectoryExists(targetPhysicalRoot))
        {
            return new EmptyTargetBaselineInspection(true, Array.Empty<string>());
        }

        bool isBootstrap = !string.IsNullOrWhiteSpace(bootstrapPhysicalRoot) &&
                           PathIdentity.Equals(targetPhysicalRoot, bootstrapPhysicalRoot);
        var unexpected = new List<string>();

        IReadOnlyList<string> entries = ops.EnumerateEntries(targetPhysicalRoot);
        foreach (string entry in entries)
        {
            string name = Path.GetFileName(entry);

            // Allowed in all targets:
            if (isReservationActive && string.Equals(name, ".app.lock", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ops.DirectoryExists(entry))
            {
                if (string.Equals(name, "prompts", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "recovery", StringComparison.OrdinalIgnoreCase))
                {
                    if (ops.EnumerateEntries(entry).Count == 0)
                    {
                        continue;
                    }
                    else
                    {
                        unexpected.Add(Path.GetRelativePath(targetPhysicalRoot, entry));
                        continue;
                    }
                }

                unexpected.Add(Path.GetRelativePath(targetPhysicalRoot, entry));
                continue;
            }

            // If it's exact bootstrap root, allow bootstrap settings and locks:
            if (isBootstrap)
            {
                if (string.Equals(name, "settings.json", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "settings.backup.json", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, ".settings.lock", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            unexpected.Add(Path.GetRelativePath(targetPhysicalRoot, entry));
        }

        return new EmptyTargetBaselineInspection(unexpected.Count == 0, unexpected);
    }
}
