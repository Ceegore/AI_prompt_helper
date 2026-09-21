using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class CrossPlatformRoundtripAcceptanceTests
{
    [TestMethod]
    public void CrossPlatform_roundtrip_phase()
    {
        if (!OperatingSystem.IsWindows()) return;

        string? root = Environment.GetEnvironmentVariable("PROMPTHELPER_CROSS_PLATFORM_ROOT");
        string? phase = Environment.GetEnvironmentVariable("PROMPTHELPER_CROSS_PLATFORM_PHASE");
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(phase))
        {
            Assert.Inconclusive("Cross-platform acceptance environment is not configured.");
        }

        if (phase == "windows-seed")
        {
            CreateWindowsSeed(root!);
            return;
        }

        if (phase == "windows-final")
        {
            VerifyLinuxMutationAndFinish(root!);
            return;
        }

        Assert.Fail($"Unknown cross-platform acceptance phase '{phase}'.");
    }

    private static void CreateWindowsSeed(string root)
    {
        Directory.CreateDirectory(root);
        var paths = new AppPaths(root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libraryRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startup = new LibraryStartupService(paths, libraryRepo, promptRepo, deleter, writer);
        StartupResult loaded = startup.LoadOrInitialize();

        var service = new PromptLibraryService(
            loaded.Document,
            libraryRepo,
            promptRepo);

        CategoryRecord category = service.CreateCategory(null, "Roundtrip Ω 😀").Value;
        CategoryRecord nested = service.CreateCategory(category.Id, "Nested العربية").Value;
        PromptRecord prompt = service.CreatePrompt(
            nested.Id,
            "Windows seed\r\nUnicode: e\u0301 😀 العربية\r\n",
            "Seed α").Value;
        _ = service.CreatePrompt(category.Id, "Second body\nLF input", "Second").Value;

        File.WriteAllText(
            Path.Combine(root, ".roundtrip-seed.txt"),
            $"{category.Id:N}\n{nested.Id:N}\n{prompt.Id:N}\n{CanonicalSha(libraryRepo.ReadPrimary())}");
    }

    private static void VerifyLinuxMutationAndFinish(string root)
    {
        string marker = Path.Combine(root, ".roundtrip-linux.txt");
        Assert.IsTrue(File.Exists(marker), "Linux mutation marker is missing.");

        string[] markerLines = File.ReadAllLines(marker);
        Assert.IsTrue(markerLines.Length >= 3, "Linux mutation marker is incomplete.");

        Guid linuxPromptId = Guid.ParseExact(markerLines[0], "N");
        string expectedLinuxBody = markerLines[1];
        string expectedLinuxHash = markerLines[2];

        var paths = new AppPaths(root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libraryRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startup = new LibraryStartupService(paths, libraryRepo, promptRepo, deleter, writer);
        StartupResult loaded = startup.LoadOrInitialize();

        Assert.AreEqual(expectedLinuxHash, CanonicalSha(loaded.Document));
        Assert.AreEqual(expectedLinuxBody, promptRepo.Read(linuxPromptId));
        Assert.IsTrue(loaded.Document.Categories.Any(c => c.Name == "Linux added 日本語"));

        var service = new PromptLibraryService(
            loaded.Document,
            libraryRepo,
            promptRepo);
        service.EditPrompt(linuxPromptId, expectedLinuxBody + "\r\nWindows final", "Linux→Windows verified");

        LibraryDocument finalDocument = service.CurrentDocument;
        Assert.AreEqual(
            CanonicalSha(finalDocument),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(paths.LibraryPath))),
            "Primary library bytes must remain canonical after the Windows return leg.");
    }

    private static string CanonicalSha(LibraryDocument document) =>
        CanonicalLibraryPackage.Create(document).Sha256Hex;
}
