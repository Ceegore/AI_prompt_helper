using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Platform.Linux.Tests;

[TestClass]
public sealed class LinuxDesktopSettingsStoreTests
{
    [TestMethod]
    public void NormalizeDataRoot_requires_absolute_path()
    {
        if (!OperatingSystem.IsLinux()) return;

        Assert.Throws<InvalidDataException>(() =>
            LinuxDesktopSettingsStore.NormalizeDataRoot("relative/path"));

        string absolute = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-settings-root");
        Assert.AreEqual(
            Path.GetFullPath(absolute),
            LinuxDesktopSettingsStore.NormalizeDataRoot(absolute));
    }

    [TestMethod]
    public void Effective_root_defaults_to_platform_data_root()
    {
        if (!OperatingSystem.IsLinux()) return;

        var store = new LinuxDesktopSettingsStore();
        var settings = new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            DataRootPath = null,
            UseDarkMode = true
        };

        Assert.AreEqual(
            DefaultDataRoot.Path,
            store.ResolveEffectiveDataRoot(settings));
    }
}
