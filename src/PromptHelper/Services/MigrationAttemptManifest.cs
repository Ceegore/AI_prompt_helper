using System;
using System.Collections.Generic;

namespace PromptHelper.Services;

public enum MigrationManifestPhase
{
    Copying,
    ReadyToCommit
}

public sealed class MigrationAttemptManifest
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid AttemptId { get; set; }
    public string SourcePhysicalRoot { get; set; } = string.Empty;
    public string TargetPhysicalRoot { get; set; } = string.Empty;
    public string SourceLibrarySha256Hex { get; set; } = string.Empty;
    public MigrationManifestPhase Phase { get; set; }
    public List<MigrationManifestArtifact> Artifacts { get; set; } = [];
    public List<MigrationControlArtifact> ControlArtifacts { get; set; } = [];
    public MigrationTargetBaseline? TargetBaseline { get; set; }
}

public sealed class MigrationTargetBaseline
{
    public bool TargetRootExistedBefore { get; set; }
    public bool PromptsDirectoryExistedBefore { get; set; }
    public bool RecoveryDirectoryExistedBefore { get; set; }
}

public sealed class MigrationManifestArtifact
{
    public string RelativePath { get; set; } = string.Empty;
    public string TempRelativePath { get; set; } = string.Empty;
    public string Sha256Hex { get; set; } = string.Empty;
    public long Length { get; set; }
    public MigrationPayloadRole Role { get; set; }
}
