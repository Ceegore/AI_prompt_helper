using System;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Infrastructure;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
[DoNotParallelize]
public sealed class WindowsPhysicalPathResolverIntegrationTests
{
    private static void CreateJunction(string junction, string target)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{junction}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start cmd.exe.");

        bool exited = process.WaitForExit(5000);
        Assert.IsTrue(exited, "mklink /J timed out.");
        Assert.AreEqual(0, process.ExitCode, "mklink /J failed.");
    }

    private static void DeleteJunction(string junction)
    {
        if (Directory.Exists(junction))
        {
            try
            {
                Directory.Delete(junction);
            }
            catch
            {
            }
        }
    }

    private static void SeedValidLibrary(string rootDir)
    {
        var paths = new AppPaths(rootDir);
        paths.EnsureRootDirectory();
        paths.EnsureDataDirectories();

        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startupService = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);
        startupService.LoadOrInitialize();
    }

    [TestMethod]
    public void CRUU5_013_Real_junction_resolves_to_target()
    {
        using var temp = new TestDirectory();
        string targetDir = Path.Combine(temp.Root, "RealTarget");
        Directory.CreateDirectory(targetDir);

        string junctionDir = Path.Combine(temp.Root, "JunctionToTarget");
        CreateJunction(junctionDir, targetDir);

        try
        {
            var resolver = new WindowsPhysicalPathResolver();
            string resolved = resolver.ResolveWithNearestExistingAncestor(junctionDir);

            Assert.AreEqual(
                PathIdentity.NormalizeForComparison(targetDir),
                PathIdentity.NormalizeForComparison(resolved));
        }
        finally
        {
            DeleteJunction(junctionDir);
        }
    }

    [TestMethod]
    public void CRUU5_013_Real_junction_alias_of_active_root_is_noop()
    {
        using var temp = new TestDirectory();
        using var settingsDir = new TestDirectory();

        string activeRoot = Path.Combine(temp.Root, "ActiveLibrary");
        Directory.CreateDirectory(activeRoot);
        SeedValidLibrary(activeRoot);

        string junctionAlias = Path.Combine(temp.Root, "AliasOfActive");
        CreateJunction(junctionAlias, activeRoot);

        try
        {
            string settingsPath = Path.Combine(settingsDir.Root, "settings.json");
            File.WriteAllText(settingsPath, $"{{\"schemaVersion\": 1, \"dataRootPath\": \"{activeRoot.Replace("\\", "\\\\")}\"}}");

            var settingsRepo = new AppSettingsRepository(settingsPathOverride: settingsPath);
            var migrationService = new DataFolderMigrationService();
            var confirmation = new FakeUserConfirmationService();

            var coordinator = new DataFolderTransitionCoordinator(
                activeRoot,
                settingsRepo,
                migrationService,
                confirmation,
                pathResolver: new WindowsPhysicalPathResolver());

            var result = coordinator.RequestTransition(junctionAlias);

            Assert.IsFalse(result.Changed);
            Assert.IsFalse(result.RestartRequired);
            Assert.IsFalse(result.ExistingLibrarySelected);
            Assert.AreEqual(0, confirmation.PromptCount);
        }
        finally
        {
            DeleteJunction(junctionAlias);
        }
    }

    [TestMethod]
    public void CRUU5_013_Real_junction_into_bootstrap_is_rejected()
    {
        using var temp = new TestDirectory();
        using var bootstrapDir = new TestDirectory();

        string current = Path.Combine(temp.Root, "Current");
        Directory.CreateDirectory(current);

        string junctionToBootstrapSub = Path.Combine(temp.Root, "JunctionToBootstrapSub");
        string bootstrapSub = Path.Combine(bootstrapDir.Root, "NestedInsideBootstrap");
        Directory.CreateDirectory(bootstrapSub);

        CreateJunction(junctionToBootstrapSub, bootstrapSub);

        try
        {
            var policy = new ManagedDataRootPolicy(new WindowsPhysicalPathResolver());

            Assert.Throws<InvalidOperationException>(() =>
                policy.ValidateTransition(
                    current,
                    junctionToBootstrapSub,
                    bootstrapDir.Root));
        }
        finally
        {
            DeleteJunction(junctionToBootstrapSub);
        }
    }

    [TestMethod]
    public void CRUU5_013_Real_junction_to_volume_root_is_rejected()
    {
        using var temp = new TestDirectory();
        string current = Path.Combine(temp.Root, "Current");
        Directory.CreateDirectory(current);

        string volumeRoot = Path.GetPathRoot(temp.Root)
            ?? throw new InvalidOperationException("No volume root.");

        string junctionToDrive = Path.Combine(temp.Root, "JunctionToDriveRoot");
        CreateJunction(junctionToDrive, volumeRoot);

        try
        {
            var policy = new ManagedDataRootPolicy(new WindowsPhysicalPathResolver());

            Assert.Throws<InvalidOperationException>(() =>
                policy.ValidateTransition(
                    current,
                    junctionToDrive,
                    Path.Combine(temp.Root, "Bootstrap")));
        }
        finally
        {
            DeleteJunction(junctionToDrive);
        }
    }
}
