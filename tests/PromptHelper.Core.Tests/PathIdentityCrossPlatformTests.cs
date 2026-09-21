using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Core.Tests;

[TestClass]
public sealed class PathIdentityCrossPlatformTests
{
    [TestMethod]
    public void PathIdentity_Uses_platform_case_semantics()
    {
        string upper = Path.Combine(Path.GetTempPath(), "PromptHelperCaseProbe");
        string lower = Path.Combine(Path.GetTempPath(), "prompthelpercaseprobe");

        Assert.AreEqual(
            OperatingSystem.IsWindows(),
            PathIdentity.Equals(upper, lower),
            "Windows paths should compare case-insensitively; Linux paths should compare case-sensitively.");
    }

    [TestMethod]
    public void PathIdentity_Normalization_trims_directory_separator_but_preserves_root()
    {
        string root = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))
            ?? throw new InvalidOperationException("Temporary path has no filesystem root.");

        Assert.AreEqual(root, PathIdentity.NormalizeForComparison(root));

        string nested = Path.Combine(root, "prompt-helper", "nested") + Path.DirectorySeparatorChar;
        Assert.AreEqual(
            Path.Combine(root, "prompt-helper", "nested"),
            PathIdentity.NormalizeForComparison(nested));
    }

    [TestMethod]
    public void PathIdentity_Strict_descendant_respects_platform_case_semantics()
    {
        string parent = Path.Combine(Path.GetTempPath(), "PromptHelperParent");
        string sameLettersDifferentCase = Path.Combine(Path.GetTempPath(), "prompthelperparent", "child");

        Assert.AreEqual(
            OperatingSystem.IsWindows(),
            PathIdentity.IsStrictDescendant(sameLettersDifferentCase, parent),
            "On Linux the differently-cased parent is a different path; on Windows it is the same parent.");
    }
}
