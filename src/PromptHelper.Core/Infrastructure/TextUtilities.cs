using System.Globalization;
using System.Text.RegularExpressions;

namespace PromptHelper.Infrastructure;

public static class TextUtilities
{
    public static int GetTextElementCount(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return StringInfo.ParseCombiningCharacters(value).Length;
    }

    public static string TruncateWithEllipsis(string value, int maximumTextElements)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (maximumTextElements < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumTextElements));
        }

        int[] starts = StringInfo.ParseCombiningCharacters(value);

        if (starts.Length <= maximumTextElements)
        {
            return value;
        }

        int kept = maximumTextElements - 1;
        int endIndex = starts[kept];

        return value[..endIndex] + "…";
    }

    public static string CreateCompactPreview(string? content, int maxTextElements = 160)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        string normalized = Regex.Replace(content, @"\s+", " ").Trim();
        return TruncateWithEllipsis(normalized, maxTextElements);
    }

    public static bool ContainsForbiddenSingleLineCharacter(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        foreach (char c in value)
        {
            if (char.IsControl(c) || c is '\u2028' or '\u2029')
            {
                return true;
            }
        }

        return false;
    }
}