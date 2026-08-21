using System;
using System.IO;

namespace PromptHelper.Services;

internal sealed record CapabilityProbeLocationPlan(
    string DirectoryRelativePath,
    string CurrentFileRelativePath,
    string ReplacementFileRelativePath);

internal sealed record MigrationCapabilityProbePlan(
    CapabilityProbeLocationPlan RootProbe,
    CapabilityProbeLocationPlan? PromptsProbe)
{
    public static MigrationCapabilityProbePlan Create(Guid attemptId)
    {
        string rootDir = $".prompthelper-write-probe-{attemptId:N}-root";
        string rootCurrent = Path.Combine(rootDir, "probe-current.txt");
        string rootReplacement = Path.Combine(rootDir, "probe-replacement.tmp");

        string promptsDir = Path.Combine("prompts", $".prompthelper-write-probe-{attemptId:N}-prompts");
        string promptsCurrent = Path.Combine(promptsDir, "probe-current.txt");
        string promptsReplacement = Path.Combine(promptsDir, "probe-replacement.tmp");

        return new MigrationCapabilityProbePlan(
            new CapabilityProbeLocationPlan(rootDir, rootCurrent, rootReplacement),
            new CapabilityProbeLocationPlan(promptsDir, promptsCurrent, promptsReplacement));
    }
}
