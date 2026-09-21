using System;

namespace PromptHelper.Services;

internal enum DurableFileClass
{
    Settings,
    LibraryMetadata,
    PromptBody,
    RecoveryArtifact,
    InitializationControl,
    MigrationControl,
    MutationControl
}

internal interface IDurableAtomicFileWriter
{
    void ReplaceDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass);

    void CreateNewDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass);
}
