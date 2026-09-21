using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class AppPathsTests
{
    [TestMethod]
    public void DefaultDataRoot_uses_local_application_data()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PromptHelper");

        Assert.AreEqual(expected, DefaultDataRoot.Path);
    }

    [TestMethod]
    public void AppPaths_root_override_is_preserved_exactly()
    {
        string root = Path.Combine(Path.GetTempPath(), "PromptHelper-AppPaths-Test");
        var paths = new AppPaths(root);

        Assert.AreEqual(root, paths.RootDirectory);
        Assert.AreEqual(Path.Combine(root, "library.json"), paths.LibraryPath);
        Assert.AreEqual(Path.Combine(root, "prompts"), paths.PromptsDirectory);
        Assert.AreEqual(Path.Combine(root, "recovery"), paths.RecoveryDirectory);
    }
}
