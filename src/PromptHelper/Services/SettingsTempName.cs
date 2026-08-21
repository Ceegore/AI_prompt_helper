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

        string target = middle.Substring(0, lastHyphen);
        string guidPart = middle.Substring(lastHyphen + 1);

        if (guidPart.Length != 32 || !Guid.TryParseExact(guidPart, "N", out _))
        {
            return false;
        }

        if (!string.Equals(
                target,
                "settings.json",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                target,
                "settings.backup.json",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        targetFileName = target;
        return true;
    }

    public static bool TryParseLegacySettingsTemp(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (fileName.StartsWith(".settings.json.", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            string guidPart = fileName.Substring(".settings.json.".Length, fileName.Length - ".settings.json.".Length - ".tmp".Length);
            return guidPart.Length == 32 && Guid.TryParseExact(guidPart, "N", out _);
        }

        if (fileName.StartsWith(".settings.backup.json.", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            string guidPart = fileName.Substring(".settings.backup.json.".Length, fileName.Length - ".settings.backup.json.".Length - ".tmp".Length);
            return guidPart.Length == 32 && Guid.TryParseExact(guidPart, "N", out _);
        }

        return false;
    }
}
