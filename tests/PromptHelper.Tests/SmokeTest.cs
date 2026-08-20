using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PromptHelper.Tests;

[TestClass]
public sealed class SmokeTest
{
    [TestMethod]
    public void Smoke_test_throws()
    {
        Assert.Throws<InvalidOperationException>(() => throw new InvalidOperationException());
    }
}