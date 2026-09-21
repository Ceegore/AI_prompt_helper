using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Platform.Linux.Tests;

[TestClass]
public sealed class LinuxAppInstanceLockTests
{
    [TestMethod]
    public void Second_instance_is_rejected_until_first_lease_is_released()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        string lockPath = Path.Combine(root, ".app.lock");
        var provider = new LinuxAppInstanceLockProvider();

        try
        {
            using IAppInstanceLease? first = provider.TryAcquire(lockPath);
            Assert.IsNotNull(first);

            using IAppInstanceLease? second = provider.TryAcquire(lockPath);
            Assert.IsNull(second);

            first.Dispose();

            using IAppInstanceLease? third = provider.TryAcquire(lockPath);
            Assert.IsNotNull(third);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Existing_lock_probe_reports_held_state_without_stealing_lock()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateRoot();
        string lockPath = Path.Combine(root, ".app.lock");
        var provider = new LinuxAppInstanceLockProvider();

        try
        {
            Assert.IsFalse(provider.IsExistingLockHeld(root));

            using IAppInstanceLease? first = provider.TryAcquire(lockPath);
            Assert.IsNotNull(first);
            Assert.IsTrue(provider.IsExistingLockHeld(root));

            first.Dispose();
            Assert.IsFalse(provider.IsExistingLockHeld(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Provider_rejects_non_Linux_use()
    {
        if (OperatingSystem.IsLinux())
        {
            return;
        }

        var provider = new LinuxAppInstanceLockProvider();
        Assert.Throws<PlatformNotSupportedException>(() =>
            provider.TryAcquire(Path.Combine(Path.GetTempPath(), ".app.lock")));
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-LinuxLock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
