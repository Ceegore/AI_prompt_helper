namespace PromptHelper.Services;

public sealed class UnsupportedLibrarySchemaException : Exception
{
    public UnsupportedLibrarySchemaException(int schemaVersion)
        : base($"Unsupported library schema version: {schemaVersion}.")
    {
        SchemaVersion = schemaVersion;
    }

    public int SchemaVersion { get; }
}