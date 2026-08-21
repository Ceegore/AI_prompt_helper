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
            string rel = Path.GetRelativePath(targetPhysicalRoot, entry);
            string name = Path.GetFileName(entry);

            // Exact root .app.lock is allowed in all targets (stale or active reservation)
            if (string.Equals(rel, ".app.lock", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ops.DirectoryExists(entry))
            {
                if (string.Equals(name, "prompts", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "recovery", StringComparison.OrdinalIgnoreCase))
                {
                    // Managed directories must not be reparse points and must be empty
                    StrictPathProbe probe = ops.ProbePath(entry);
                    if (probe.Kind != StrictPathKind.Directory)
                    {
                        unexpected.Add(rel);
                        continue;
                    }

                    if (probe.Attributes.HasValue && (probe.Attributes.Value & FileAttributes.ReparsePoint) != 0)
                    {
                        unexpected.Add(rel);
                        continue;
                    }

                    if (ops.EnumerateEntries(entry).Count == 0)
                    {
                        continue;
                    }
                    else
                    {
                        unexpected.Add(rel);
                        continue;
                    }
                }

                unexpected.Add(rel);
                continue;
            }

            // If it's exact bootstrap root, allow bootstrap settings and locks:
            if (isBootstrap)
            {
                if (string.Equals(rel, "settings.json", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rel, "settings.backup.json", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rel, ".settings.lock", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            unexpected.Add(rel);
        }

        return new EmptyTargetBaselineInspection(unexpected.Count == 0, unexpected);
    }
}
