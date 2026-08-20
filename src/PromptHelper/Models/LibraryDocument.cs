using System.Text.Json.Serialization;

namespace PromptHelper.Models;

public sealed class LibraryDocument
{
    public const int CurrentSchemaVersion = 1;

    [JsonRequired]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonRequired]
    public List<CategoryRecord> Categories { get; set; } = [];

    [JsonRequired]
    public List<PromptRecord> Prompts { get; set; } = [];
}