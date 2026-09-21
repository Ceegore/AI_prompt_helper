using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Platform.Linux.Tests;

[TestClass]
public sealed class CrossPlatformRoundtripAcceptanceTests
{
    [TestMethod]
    public void Windows_library_opens_mutates_and_remains_canonical_on_Linux()
    {
        if (!OperatingSystem.IsLinux()) return;

        string? root = Environment.GetEnvironmentVariable("PROMPTHELPER_CROSS_PLATFORM_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            Assert.Inconclusive("Cross-platform acceptance environment is not configured.");
        }

        string seedMarker = Path.Combine(root!, ".roundtrip-seed.txt");
        Assert.IsTrue(File.Exists(seedMarker), "Windows seed marker is missing.");
        string[] seed = File.ReadAllLines(seedMarker);
        Assert.IsTrue(seed.Length >= 4, "Windows seed marker is incomplete.");

        Guid seedPromptId = Guid.ParseExact(seed[2], "N");
        string windowsCanonicalHash = seed[3];

        var store = new LinuxPromptLibraryStore(root!);
        store.Initialize();

        Assert.AreEqual(
            windowsCanonicalHash,
            CanonicalLibraryPackage.Create(store.CurrentDocument).Sha256Hex);
        StringAssert.Contains(store.ReadPrompt(seedPromptId), "Windows seed");
        Assert.IsTrue(store.CurrentDocument.Categories.Any(c => c.Name == "Roundtrip Ω 😀"));
        Assert.IsTrue(store.CurrentDocument.Categories.Any(c => c.Name == "Nested العربية"));

        CategoryRecord linuxCategory =
            store.CreateCategory(null, "Linux added 日本語").Value;
        const string linuxBody = "Linux mutation 😀 e\u0301 العربية";
        PromptRecord linuxPrompt =
            store.CreatePrompt(linuxCategory.Id, linuxBody, "Linux title").Value;

        var reopened = new LinuxPromptLibraryStore(root!);
        reopened.Initialize();
        Assert.AreEqual(linuxBody, reopened.ReadPrompt(linuxPrompt.Id));

        string canonicalHash =
            CanonicalLibraryPackage.Create(reopened.CurrentDocument).Sha256Hex;
        string fileHash =
            Convert.ToHexStringLower(SHA256.HashData(
                File.ReadAllBytes(Path.Combine(root!, "library.json"))));
        Assert.AreEqual(canonicalHash, fileHash);

        File.WriteAllText(
            Path.Combine(root!, ".roundtrip-linux.txt"),
            $"{linuxPrompt.Id:N}\n{linuxBody}\n{canonicalHash}");
    }
}
