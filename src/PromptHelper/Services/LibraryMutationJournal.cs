using System;

namespace PromptHelper.Services;

public enum LibraryMutationKind
{
    CreatePrompt,
    EditPrompt,
    DeletePrompt,
    DuplicatePrompt
}

public enum LibraryMutationPhase
{
    Prepared,
    RecoveryBodyDurable,
    BodyDurable,
    MetadataDurable,
    BodyDeleted
}

public sealed class LibraryMutationJournal
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid OperationId { get; set; }
    public LibraryMutationKind Kind { get; set; }
    public LibraryMutationPhase Phase { get; set; }
    public Guid PromptId { get; set; }
    public string BodyRelativePath { get; set; } = string.Empty;
    public string OldLibrarySha256Hex { get; set; } = string.Empty;
    public string NewLibrarySha256Hex { get; set; } = string.Empty;
    public long? OldBodyLength { get; set; }
    public string? OldBodySha256Hex { get; set; }
    public long? NewBodyLength { get; set; }
    public string? NewBodySha256Hex { get; set; }
    public string? RecoveryBodyRelativePath { get; set; }
}
