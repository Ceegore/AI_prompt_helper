using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;

namespace IconIdentityVerifier;

public static class IcoReader
{
    public static readonly int[] MandatorySizes = [16, 24, 32, 48, 64, 128, 256];

    public static Dictionary<(int Width, int Height), string> ReadFrames(string icoPath)
    {
        using var stream = File.OpenRead(icoPath);
        return ReadFrames(stream);
    }

    public static Dictionary<(int Width, int Height), string> ReadFrames(Stream stream)
    {
        var decoder = new IconBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        var result = new Dictionary<(int Width, int Height), string>();

        foreach (var frame in decoder.Frames)
        {
            int w = frame.PixelWidth;
            int h = frame.PixelHeight;
            string hash = PixelNormalizer.NormalizeAndHash(frame);
            result[(w, h)] = hash;
        }

        return result;
    }
}
