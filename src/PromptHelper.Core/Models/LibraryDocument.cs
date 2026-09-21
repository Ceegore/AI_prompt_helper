using System.Text.Json.Serialization;

namespace PromptHelper.Models;

public sealed class LibraryDocument
{
    public const int CurrentSchemaVersion = 1;

    [JsonRequired]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    // Optional in schema v1 so existing libraries can be upgraded in place. Once written,
    // this marker prevents user-deleted or user-edited premades from being reinstalled.
    public int PremadePackVersion { get; set; }

    [JsonRequired]
    public List<CategoryRecord> Categories { get; set; } = [];

    [JsonRequired]
    public List<PromptRecord> Prompts { get; set; } = [];
}
