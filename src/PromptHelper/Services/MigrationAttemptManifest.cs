using System;
using System.Collections.Generic;

namespace PromptHelper.Services;

internal enum MigrationManifestPhase
{
    Copying,
    ReadyToCommit
}

internal sealed class MigrationAttemptManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid AttemptId { get; set; }
    public string SourcePhysicalRoot { get; set; } = string.Empty;
    public string TargetPhysicalRoot { get; set; } = string.Empty;
    public string SourceLibrarySha256Hex { get; set; } = string.Empty;
    public MigrationManifestPhase Phase { get; set; }
    public List<MigrationManifestArtifact> Artifacts { get; set; } = [];
}

internal sealed class MigrationManifestArtifact
{
    public string RelativePath { get; set; } = string.Empty;
    public string Sha256Hex { get; set; } = string.Empty;
    public long Length { get; set; }
    public MigrationPayloadRole Role { get; set; }
}
