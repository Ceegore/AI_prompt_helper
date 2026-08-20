using System;

namespace PromptHelper.Services;

public sealed class UnsupportedSettingsSchemaException : Exception
{
    public UnsupportedSettingsSchemaException(int schemaVersion)
        : base($"Unsupported settings schema version: {schemaVersion}.")
    {
        SchemaVersion = schemaVersion;
    }

    public int SchemaVersion { get; }
}
