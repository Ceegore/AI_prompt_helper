using System;
using System.IO;

namespace PromptHelper.Services;

internal static class StrictFileAuthority
{
    public static StrictFilePresence GetPresence(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            return StrictFilePresence.Present;
        }
        catch (FileNotFoundException)
        {
            return StrictFilePresence.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return StrictFilePresence.Missing;
        }
    }

    public static byte[]? ReadOptionalBytes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            return File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public static void DeleteIfPresentStrict(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            File.Delete(path);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
