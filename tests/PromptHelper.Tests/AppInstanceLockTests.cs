using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class AppInstanceLockTests
{
    [TestMethod]
    public void First_acquire_succeeds()
    {
        using var testDir = new TestDirectory();
        string lockFile = Path.Combine(testDir.Root, ".app.lock");

        using var appLock = AppInstanceLock.TryAcquire(lockFile);

        Assert.IsNotNull(appLock);
    }

    [TestMethod]
    public void Second_acquire_while_first_open_returns_null()
    {
        using var testDir = new TestDirectory();
        string lockFile = Path.Combine(testDir.Root, ".app.lock");

        using var firstLock = AppInstanceLock.TryAcquire(lockFile);
        Assert.IsNotNull(firstLock);

        using var secondLock = AppInstanceLock.TryAcquire(lockFile);
        Assert.IsNull(secondLock);
    }

    [TestMethod]
    public void Acquire_after_first_disposed_succeeds()
    {
        using var testDir = new TestDirectory();
        string lockFile = Path.Combine(testDir.Root, ".app.lock");

        var firstLock = AppInstanceLock.TryAcquire(lockFile);
        Assert.IsNotNull(firstLock);
        firstLock.Dispose();

        using var secondLock = AppInstanceLock.TryAcquire(lockFile);
        Assert.IsNotNull(secondLock);
    }
}