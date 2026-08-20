using System.Text.Json.Serialization;

namespace PromptHelper.Models;

public sealed class CategoryRecord
{
    [JsonRequired]
    public Guid Id { get; set; }

    [JsonRequired]
    public Guid? ParentId { get; set; }

    [JsonRequired]
    public string Name { get; set; } = string.Empty;

    [JsonRequired]
    public long SortOrder { get; set; }
}