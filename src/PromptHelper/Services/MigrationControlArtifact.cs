using System;

namespace PromptHelper.Services;

public enum MigrationControlArtifactKind
{
    CapabilityProbeDirectory,
    CapabilityProbeFile,
    ManifestPhaseStaging
}

public sealed class MigrationControlArtifact
{
    public string RelativePath { get; set; } = string.Empty;
    public MigrationControlArtifactKind Kind { get; set; }
}
