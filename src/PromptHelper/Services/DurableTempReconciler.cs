using System;
using System.IO;

namespace PromptHelper.Services;

internal static class DurableTempReconciler
{
    private const string Prefix = ".prompthelper-tmp-";
    private const string Suffix = ".tmp";

    public static bool TryParseDurableTemp(string fileName, out DurableFileClass fileClass)
    {
        fileClass = default;

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

        string tag = middle.Substring(0, lastHyphen);
        string guidPart = middle.Substring(lastHyphen + 1);

        if (guidPart.Length != 32 || !Guid.TryParseExact(guidPart, "N", out _))
        {
            return false;
        }

        switch (tag.ToLowerInvariant())
        {
            case "settings":
                fileClass = DurableFileClass.Settings;
                return true;
            case "library":
                fileClass = DurableFileClass.LibraryMetadata;
                return true;
            case "prompt":
                fileClass = DurableFileClass.PromptBody;
                return true;
            case "recovery":
                fileClass = DurableFileClass.RecoveryArtifact;
                return true;
            case "init":
                fileClass = DurableFileClass.InitializationControl;
                return true;
            case "migration":
                fileClass = DurableFileClass.MigrationControl;
                return true;
            case "mutation":
                fileClass = DurableFileClass.MutationControl;
                return true;
            default:
                return false;
        }
    }

    public static bool TryParseLegacyDataRootTemp(string fileName, out string description)
    {
        description = string.Empty;

        if (string.IsNullOrWhiteSpace(fileName) ||
            !fileName.StartsWith('.') ||
            !fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fileName.StartsWith(".library.json.", StringComparison.OrdinalIgnoreCase))
        {
            string guidPart = fileName.Substring(".library.json.".Length, fileName.Length - ".library.json.".Length - ".tmp".Length);
            if (guidPart.Length == 32 && Guid.TryParseExact(guidPart, "N", out _))
            {
                description = "legacy library metadata temp";
                return true;
            }
        }

        if (fileName.StartsWith(".library.backup.json.", StringComparison.OrdinalIgnoreCase))
        {
            string guidPart = fileName.Substring(".library.backup.json.".Length, fileName.Length - ".library.backup.json.".Length - ".tmp".Length);
            if (guidPart.Length == 32 && Guid.TryParseExact(guidPart, "N", out _))
            {
                description = "legacy library backup temp";
                return true;
            }
        }

        if (fileName.StartsWith(".initializing.marker.", StringComparison.OrdinalIgnoreCase))
        {
            string guidPart = fileName.Substring(".initializing.marker.".Length, fileName.Length - ".initializing.marker.".Length - ".tmp".Length);
            if (guidPart.Length == 32 && Guid.TryParseExact(guidPart, "N", out _))
            {
                description = "legacy initializing marker temp";
                return true;
            }
        }

        return false;
    }

    public static bool TryParseLegacyPromptTemp(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            !fileName.StartsWith('.') ||
            !fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Format: .<promptGuidN>.md.<tempGuidN>.tmp
        string[] parts = fileName.Split('.');
        // parts: ["", "<promptGuidN>", "md", "<tempGuidN>", "tmp"]
        if (parts.Length == 5 &&
            parts[0] == "" &&
            parts[2].Equals("md", StringComparison.OrdinalIgnoreCase) &&
            parts[4].Equals("tmp", StringComparison.OrdinalIgnoreCase))
        {
            if (Guid.TryParse(parts[1], out _) &&
                parts[3].Length == 32 && Guid.TryParseExact(parts[3], "N", out _))
            {
                return true;
            }
        }

        return false;
    }
}
