using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace PromptHelper.Tests;

/// <summary>
/// CRUU14-012: binds the approved source SVG to a locked set of per-size normalized-pixel
/// hashes, so a later change to either the SVG or the checked-in ICO that was not deliberately
/// re-approved is caught by comparing against pixel content rather than compressed bytes
/// (two different PNG encoders/settings can produce different bytes for identical pixels;
/// comparing decoded pixels is the actual identity that matters).
/// </summary>
internal sealed record IconApprovedFrame(int Size, string NormalizedRgbaSha256Hex);

internal sealed record IconApprovalManifest(
    string SvgSha256Hex,
    IReadOnlyList<IconApprovedFrame> Frames)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string ComputeSha256Hex(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string ComputeSvgHash(string svgPath) =>
        ComputeSha256Hex(File.ReadAllBytes(svgPath));

    /// <summary>
    /// Decodes a single ICO frame's PNG payload bytes to a canonical Bgra32 pixel buffer and
    /// hashes that raw pixel data — not the compressed bytes, so re-encoding the same image
    /// with a different PNG compression level does not look like a content change.
    /// </summary>
    public static string ComputeNormalizedRgbaHash(byte[] pngFrameBytes)
    {
        using var stream = new MemoryStream(pngFrameBytes);
        BitmapFrame frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        FormatConvertedBitmap converted = new();
        converted.BeginInit();
        converted.Source = frame;
        converted.DestinationFormat = System.Windows.Media.PixelFormats.Bgra32;
        converted.EndInit();

        int width = converted.PixelWidth;
        int height = converted.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BitConverter.GetBytes(width));
        hash.AppendData(BitConverter.GetBytes(height));
        hash.AppendData(pixels);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Parses a classic ICO file's directory and returns each frame's raw payload bytes by size.</summary>
    public static Dictionary<int, byte[]> ReadIcoFramePayloads(byte[] icoBytes)
    {
        ushort count = BitConverter.ToUInt16(icoBytes, 4);
        var result = new Dictionary<int, byte[]>();

        for (int i = 0; i < count; i++)
        {
            int entryOffset = 6 + (i * 16);
            int w = icoBytes[entryOffset] == 0 ? 256 : icoBytes[entryOffset];
            int h = icoBytes[entryOffset + 1] == 0 ? 256 : icoBytes[entryOffset + 1];
            uint size = BitConverter.ToUInt32(icoBytes, entryOffset + 8);
            uint offset = BitConverter.ToUInt32(icoBytes, entryOffset + 12);

            if (w != h)
            {
                throw new InvalidDataException($"ICO frame {i} is not square: {w}x{h}.");
            }

            byte[] payload = new byte[size];
            Array.Copy(icoBytes, offset, payload, 0, size);
            result[w] = payload;
        }

        return result;
    }

    /// <summary>
    /// Regenerates the approval manifest from whatever SVG/ICO are currently on disk. Only
    /// call this deliberately, after reviewing an intentional artwork change — e.g. from a
    /// throwaway test or a one-off script that calls this then <see cref="Save"/>. It is not
    /// wired into the normal test run: the manifest is meant to be a locked, reviewed
    /// reference, not something that silently updates itself to match whatever is on disk.
    /// </summary>
    public static IconApprovalManifest ComputeFromCommittedArtwork(string svgPath, string icoPath, IEnumerable<int> requiredSizes)
    {
        byte[] icoBytes = File.ReadAllBytes(icoPath);
        Dictionary<int, byte[]> framesBySize = ReadIcoFramePayloads(icoBytes);

        var frames = new List<IconApprovedFrame>();
        foreach (int size in requiredSizes)
        {
            if (!framesBySize.TryGetValue(size, out byte[]? payload))
            {
                throw new InvalidDataException($"Committed ICO is missing required size {size}x{size}.");
            }

            frames.Add(new IconApprovedFrame(size, ComputeNormalizedRgbaHash(payload)));
        }

        return new IconApprovalManifest(ComputeSvgHash(svgPath), frames);
    }

    public static IconApprovalManifest Load(string manifestPath)
    {
        string json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<IconApprovalManifest>(json, JsonOptions)
            ?? throw new InvalidDataException($"Icon approval manifest deserialized to null: '{manifestPath}'.");
    }

    public void Save(string manifestPath)
    {
        string json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(manifestPath, json);
    }
}
