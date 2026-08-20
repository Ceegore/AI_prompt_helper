using System.Text.Json.Serialization;

namespace PromptHelper.Models;

public sealed class PromptRecord
{
    [JsonRequired]
    public Guid Id { get; set; }

    [JsonRequired]
    public Guid? CategoryId { get; set; }

    [JsonRequired]
    public long SortOrder { get; set; }

    public string? Title { get; set; }
}