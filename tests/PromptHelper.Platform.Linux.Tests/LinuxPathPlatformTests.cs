using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Platform.Linux.Tests;

[TestClass]
public sealed class LinuxPathPlatformTests
{
    [TestMethod]
    public void PhysicalPathResolver_resolves_symlinked_ancestor_and_preserves_missing_suffix()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-LinuxPath-" + Guid.NewGuid().ToString("N"));
        string actual = Path.Combine(root, "actual");
        string link = Path.Combine(root, "link");

        Directory.CreateDirectory(actual);
        Directory.CreateSymbolicLink(link, actual);

        try
        {
            string candidate = Path.Combine(link, "missing", "leaf");
            string expected = Path.Combine(actual, "missing", "leaf");

            string resolved = new LinuxPhysicalPathResolver()
                .ResolveWithNearestExistingAncestor(candidate);

            Assert.AreEqual(Path.GetFullPath(expected), resolved);
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void DirectoryCaseSensitivityInspector_reports_host_filesystem_semantics()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-LinuxCase-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            DirectoryCaseSensitivityState state =
                new LinuxDirectoryCaseSensitivityInspector().Inspect(root);

            Assert.AreEqual(
                DirectoryCaseSensitivityState.CaseSensitive,
                state,
                "The supported Linux CI filesystem is expected to be case-sensitive.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void PhysicalPathResolver_returns_existing_path_and_validates_input()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-LinuxPathExisting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var resolver = new LinuxPhysicalPathResolver();

            Assert.AreEqual(
                Path.GetFullPath(root),
                resolver.ResolveWithNearestExistingAncestor(root));

            Assert.Throws<ArgumentException>(() =>
                resolver.ResolveWithNearestExistingAncestor("   "));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void PhysicalPathResolver_preserves_multiple_missing_suffix_segments()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-LinuxPathSuffix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            string candidate = Path.Combine(root, "one", "two", "three");
            string resolved = new LinuxPhysicalPathResolver()
                .ResolveWithNearestExistingAncestor(candidate);

            Assert.AreEqual(Path.GetFullPath(candidate), resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void DirectoryCaseSensitivityInspector_validates_blank_and_missing_directory()
    {
        if (!OperatingSystem.IsLinux()) return;

        var inspector = new LinuxDirectoryCaseSensitivityInspector();

        Assert.Throws<ArgumentException>(() => inspector.Inspect("   "));

        string missing = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-LinuxCaseMissing-" + Guid.NewGuid().ToString("N"));

        Assert.Throws<DirectoryNotFoundException>(() =>
            inspector.Inspect(missing));
    }

    [TestMethod]
    public void Linux_platform_services_fail_closed_on_other_operating_systems()
    {
        if (OperatingSystem.IsLinux())
        {
            return;
        }

        Assert.Throws<PlatformNotSupportedException>(() =>
            new LinuxPhysicalPathResolver().ResolveWithNearestExistingAncestor(Path.GetTempPath()));

        Assert.Throws<PlatformNotSupportedException>(() =>
            new LinuxDirectoryCaseSensitivityInspector().Inspect(Path.GetTempPath()));
    }
}
