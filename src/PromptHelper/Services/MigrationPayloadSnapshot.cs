using System;
using System.Collections.Generic;
using PromptHelper.Models;

namespace PromptHelper.Services;

public enum MigrationPayloadRole
{
    PrimaryMetadata,
    SafetyBackup,
    PromptBody,
    OrphanPromptBody,
    RecoveryArtifact
}

internal sealed record MigrationPayloadFile(
    string RelativePath,
    MigrationPayloadRole Role,
    long Length,
    byte[] Sha256);

internal sealed record MigrationPayloadSnapshot(
    LibraryDocument ActiveDocument,
    IReadOnlyList<MigrationPayloadFile> Files,
    IReadOnlySet<string> RelativePathSet);
