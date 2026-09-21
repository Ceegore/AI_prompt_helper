using System.Text.Json;

namespace PromptHelper.Services;

public static class LibraryJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        RespectNullableAnnotations = true
    };

    // Preserve the historical Windows library byte format on every platform.
    // System.Text.Json indented output follows the host newline convention, which
    // would otherwise make hashes and canonical bytes differ between Windows and Linux.
    public static string SerializeCanonical<T>(T value)
    {
        string json = JsonSerializer.Serialize(value, Options);
        return json
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal);
    }
}
