using System;
using System.IO;
using System.Text;

namespace PromptHelper.Services;

internal static class StrictUtf8Text
{
    private static readonly UTF8Encoding Encoding =
        new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    public static string Decode(
        ReadOnlySpan<byte> bytes,
        string description)
    {
        try
        {
            ReadOnlySpan<byte> payload = bytes;

            if (payload.Length >= 3 &&
                payload[0] == 0xEF &&
                payload[1] == 0xBB &&
                payload[2] == 0xBF)
            {
                payload = payload[3..];
            }

            return Encoding.GetString(payload);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException(
                $"Invalid UTF-8 in {description}.",
                ex);
        }
    }

    public static string ReadAllText(
        string path,
        string description)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return Decode(bytes, description);
    }

    public static byte[] Encode(string text) =>
        Encoding.GetBytes(text);
}
