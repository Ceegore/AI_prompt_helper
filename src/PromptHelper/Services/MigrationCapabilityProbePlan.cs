using System;
using System.IO;

namespace PromptHelper.Services;

internal sealed record CapabilityFileProbePlan(
    string CurrentRelativePath,
    string ReplacementRelativePath);

internal sealed record MigrationCapabilityProbePlan(
    CapabilityFileProbePlan RootProbe,
    CapabilityFileProbePlan PromptsProbe)
{
    public static MigrationCapabilityProbePlan Create(Guid attemptId)
    {
        return new(
            new CapabilityFileProbePlan(
                $".prompthelper-probe-{attemptId:N}-root-current.tmp",
                $".prompthelper-probe-{attemptId:N}-root-replacement.tmp"),
            new CapabilityFileProbePlan(
                Path.Combine("prompts", $".prompthelper-probe-{attemptId:N}-prompts-current.tmp"),
                Path.Combine("prompts", $".prompthelper-probe-{attemptId:N}-prompts-replacement.tmp")));
    }
}
