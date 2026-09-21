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
