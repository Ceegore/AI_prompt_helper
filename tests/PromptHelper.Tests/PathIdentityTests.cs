using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class PathIdentityTests
{
    [TestMethod]
    public void CRUU4_009_Normalization_preserves_drive_root_semantics()
    {
        string cDrive = PathIdentity.NormalizeForComparison(@"C:\");
        Assert.AreEqual(@"C:\", cDrive);

        string dDrive = PathIdentity.NormalizeForComparison(@"D:\");
        Assert.AreEqual(@"D:\", dDrive);

        string normalDir = PathIdentity.NormalizeForComparison(@"C:\Folder\Sub\");
        Assert.AreEqual(@"C:\Folder\Sub", normalDir);
    }

    [TestMethod]
    public void PathIdentity_Equals_handles_slashes_and_casing()
    {
        Assert.IsTrue(PathIdentity.Equals(@"C:\Data\Prompts", @"c:/data/prompts/"));
        Assert.IsFalse(PathIdentity.Equals(@"C:\Data\Prompts", @"C:\Data\Prompts2"));
    }

    [TestMethod]
    public void PathIdentity_IsStrictDescendant_handles_subdirectories_and_exact_match()
    {
        Assert.IsTrue(PathIdentity.IsStrictDescendant(@"C:\Data\Prompts\Sub", @"C:\Data\Prompts"));
        Assert.IsFalse(PathIdentity.IsStrictDescendant(@"C:\Data\Prompts", @"C:\Data\Prompts"));
        Assert.IsFalse(PathIdentity.IsStrictDescendant(@"C:\Data\PromptsSibling", @"C:\Data\Prompts"));
    }
}
