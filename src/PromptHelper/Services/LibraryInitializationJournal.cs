using System;

namespace PromptHelper.Services;

public enum LibraryInitializationPhase
{
    CreatingDefaults,
    MetadataDurable
}

public sealed class LibraryInitializationJournal
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public long Revision { get; set; } = 0;
    public Guid InitializationId { get; set; }
    public LibraryInitializationPhase Phase { get; set; }
}
