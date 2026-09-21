using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Core.Tests;

[TestClass]
public sealed class DefaultDataRootTests
{
    [TestMethod]
    public void DefaultDataRoot_uses_platform_local_application_data()
    {
        string baseDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        Assert.IsFalse(string.IsNullOrWhiteSpace(baseDirectory));
        Assert.AreEqual(
            Path.Combine(baseDirectory, "PromptHelper"),
            DefaultDataRoot.Path);
        Assert.IsTrue(Path.IsPathFullyQualified(DefaultDataRoot.Path));
    }

    [TestMethod]
    public void AppPaths_root_override_is_preserved_exactly()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-AppPaths-" + Guid.NewGuid().ToString("N"));

        var paths = new AppPaths(root);

        Assert.AreEqual(root, paths.RootDirectory);
        Assert.AreEqual(Path.Combine(root, "library.json"), paths.LibraryPath);
        Assert.AreEqual(Path.Combine(root, "prompts"), paths.PromptsDirectory);
        Assert.AreEqual(Path.Combine(root, "recovery"), paths.RecoveryDirectory);
    }
}
