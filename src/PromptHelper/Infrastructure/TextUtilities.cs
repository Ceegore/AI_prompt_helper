using System.Globalization;

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
}