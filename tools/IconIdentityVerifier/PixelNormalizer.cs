using System;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconIdentityVerifier;

public static class PixelNormalizer
{
    public static string NormalizeAndHash(BitmapSource frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        int width = frame.PixelWidth;
        int height = frame.PixelHeight;

        var formatted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        formatted.CopyPixels(pixels, stride, 0);

        // Convert BGRA to standardized RGBA
        byte[] rgba = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            rgba[i] = pixels[i + 2];     // R
            rgba[i + 1] = pixels[i + 1]; // G
            rgba[i + 2] = pixels[i];     // B
            rgba[i + 3] = pixels[i + 3]; // A
        }

        byte[] hash = SHA256.HashData(rgba);
        return Convert.ToHexStringLower(hash);
    }
}
