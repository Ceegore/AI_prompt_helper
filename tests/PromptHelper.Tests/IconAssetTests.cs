using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PromptHelper.Tests;

[TestClass]
public sealed class IconAssetTests
{
    [TestMethod]
    public void GenerateAppIcon_script_exists_and_contains_square_padding_and_validation()
    {
        string scriptPath = RepositoryTestPaths.RequireFile("tools", "GenerateAppIcon.ps1");
        string scriptContent = File.ReadAllText(scriptPath);

        StringAssert.Contains(scriptContent, "PromptHelperLogo.svg");
        StringAssert.Contains(scriptContent, "PromptHelper.ico");
        StringAssert.Contains(scriptContent, "extent \"256x256\"");
        StringAssert.Contains(scriptContent, "gravity center");
        StringAssert.Contains(scriptContent, "auto-resize=256,128,64,48,32,24,16");
    }

    [TestMethod]
    public void PromptHelperIco_when_present_is_valid_and_contains_required_square_frames()
    {
        string root = RepositoryTestPaths.Root;
        string ico = Path.Combine(root, "src", "PromptHelper", "Assets", "PromptHelper.ico");

        if (!File.Exists(ico))
        {
            // Allowed to be absent until source SVG is supplied
            return;
        }

        byte[] bytes = File.ReadAllBytes(ico);
        Assert.IsTrue(bytes.Length > 6, "ICO is empty or truncated.");

        ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2));
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2));
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2));

        Assert.AreEqual((ushort)0, reserved);
        Assert.AreEqual((ushort)1, type);
        Assert.IsTrue(count >= 7);
        Assert.IsTrue(bytes.Length >= 6 + (count * 16));

        var sizes = new HashSet<int>();

        for (int i = 0; i < count; i++)
        {
            int offset = 6 + (i * 16);
            int width = bytes[offset] == 0 ? 256 : bytes[offset];
            int height = bytes[offset + 1] == 0 ? 256 : bytes[offset + 1];

            Assert.AreEqual(width, height, $"ICO frame {i} is not square.");
            sizes.Add(width);

            uint imageSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 8, 4));
            uint imageOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 12, 4));

            Assert.IsTrue(imageSize > 0, $"ICO frame {i} has zero image size.");
            Assert.IsTrue(imageOffset < (uint)bytes.Length, $"ICO frame {i} offset is outside file.");
            Assert.IsTrue((ulong)imageOffset + imageSize <= (ulong)bytes.Length,
                $"ICO frame {i} extends past end of file.");
        }

        foreach (int required in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            Assert.IsTrue(sizes.Contains(required), $"ICO is missing {required}x{required} frame.");
        }
    }
}
