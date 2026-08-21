using System;
using System.Collections.Generic;

namespace PromptHelper.Services;

public sealed record MigrationRecoveryContext(
    string TargetPhysicalRoot,
    string? BootstrapPhysicalRoot = null,
    string? ExpectedSourcePhysicalRoot = null)
{
    public bool IsExactBootstrapRoot =>
        !string.IsNullOrWhiteSpace(BootstrapPhysicalRoot) &&
        PathIdentity.Equals(TargetPhysicalRoot, BootstrapPhysicalRoot);

    public IReadOnlySet<string> AllowedPersistentRelativePaths
    {
        get
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (IsExactBootstrapRoot)
            {
                set.Add("settings.json");
                set.Add("settings.backup.json");
                set.Add(".settings.lock");
            }
            return set;
        }
    }
}
