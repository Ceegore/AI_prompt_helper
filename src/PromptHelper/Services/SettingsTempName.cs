using System;
using System.IO;

namespace PromptHelper.Services;

internal static class SettingsTempName
{
    private const string Prefix = ".prompthelper-settings-";
    private const string Suffix = ".tmp";

    public static string Generate(string targetPath, Guid nonce)
    {
        string fileName = Path.GetFileName(targetPath);
        return $"{Prefix}{fileName}-{nonce:N}{Suffix}";
    }

    public static bool TryParse(string fileName, out string targetFileName)
    {
        targetFileName = string.Empty;

        if (string.IsNullOrWhiteSpace(fileName) ||
            !fileName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string middle = fileName.Substring(Prefix.Length, fileName.Length - Prefix.Length - Suffix.Length);
        int lastHyphen = middle.LastIndexOf('-');
        if (lastHyphen <= 0)
        {
            return false;
        }

        string targetName = middle.Substring(0, lastHyphen);
        string guidPart = middle.Substring(lastHyphen + 1);

        if (guidPart.Length != 32 || !Guid.TryParseExact(guidPart, "N", out _))
        {
            return false;
        }

        targetFileName = targetName;
        return true;
    }
}
