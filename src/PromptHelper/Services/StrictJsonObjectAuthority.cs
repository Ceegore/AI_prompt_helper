using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PromptHelper.Services;

internal static class StrictJsonObjectAuthority
{
    public static void ValidateExactObject(
        JsonElement element,
        IEnumerable<string> allowedMembers,
        IEnumerable<string> requiredMembers,
        string description)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"{description} must be a JSON object.");
        }

        var allowed = new HashSet<string>(allowedMembers, StringComparer.OrdinalIgnoreCase);
        var required = new HashSet<string>(requiredMembers, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new JsonException($"{description} contains duplicate property '{property.Name}'.");
            }

            if (!allowed.Contains(property.Name))
            {
                throw new JsonException($"{description} contains unknown property '{property.Name}'.");
            }
        }

        foreach (string requiredName in required)
        {
            if (!seen.Contains(requiredName))
            {
                throw new JsonException($"{description} is missing required property '{requiredName}'.");
            }
        }
    }

    public static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonProperty matchingProperty)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    matchingProperty = prop;
                    return true;
                }
            }
        }

        matchingProperty = default;
        return false;
    }
}