using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Core.Tests;

[TestClass]
public sealed class FileObjectIdentityTests
{
    [TestMethod]
    public void Identity_equality_requires_same_scheme_and_value()
    {
        var left = new FileObjectIdentity("linux-statx-v1", "8:1:1234");
        var same = new FileObjectIdentity("linux-statx-v1", "8:1:1234");
        var differentScheme = new FileObjectIdentity("windows-file-id-v1", "8:1:1234");
        var differentValue = new FileObjectIdentity("linux-statx-v1", "8:1:5678");

        Assert.AreEqual(left, same);
        Assert.AreNotEqual(left, differentScheme);
        Assert.AreNotEqual(left, differentValue);
    }

    [TestMethod]
    public void Expected_present_state_can_bind_platform_identity()
    {
        var identity = new FileObjectIdentity(
            "linux-statx-v1",
            "00000008:00000001:0000000000001234");

        ExpectedFileState expected = ExpectedFileState.Present(
            new string('a', 64),
            identity);

        Assert.AreEqual(ExpectedFileStateKind.Present, expected.Kind);
        Assert.AreEqual(new string('a', 64), expected.ExpectedSha256Hex);
        Assert.AreEqual(identity, expected.ExpectedIdentity);
    }

    [TestMethod]
    public void Identity_rejects_empty_scheme_or_value()
    {
        Assert.Throws<ArgumentException>(() =>
            new FileObjectIdentity("", "value"));
        Assert.Throws<ArgumentException>(() =>
            new FileObjectIdentity("linux-statx-v1", ""));
    }
}
