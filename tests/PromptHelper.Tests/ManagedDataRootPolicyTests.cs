using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class ManagedDataRootPolicyTests
{
    [TestMethod]
    public void CRUU4_008_Persisted_volume_root_is_rejected_before_bootstrap()
    {
        var fakeResolver = new FakePhysicalPathResolver();
        var policy = new ManagedDataRootPolicy(fakeResolver);

        string bootstrap = @"C:\Users\test\AppData\Local\PromptHelper";

        Assert.Throws<InvalidDataException>(() =>
            policy.ValidateConfiguredRootForStartup(@"C:\", bootstrap));

        Assert.Throws<InvalidDataException>(() =>
            policy.ValidateConfiguredRootForStartup(@"D:\", bootstrap));
    }

    [TestMethod]
    public void CRUU4_008_Persisted_bootstrap_parent_is_rejected()
    {
        var fakeResolver = new FakePhysicalPathResolver();
        var policy = new ManagedDataRootPolicy(fakeResolver);

        string bootstrap = @"C:\Users\test\AppData\Local\PromptHelper";

        Assert.Throws<InvalidDataException>(() =>
            policy.ValidateConfiguredRootForStartup(@"C:\Users\test\AppData\Local", bootstrap));
    }

    [TestMethod]
    public void CRUU4_008_Exact_bootstrap_root_is_allowed()
    {
        var fakeResolver = new FakePhysicalPathResolver();
        var policy = new ManagedDataRootPolicy(fakeResolver);

        string bootstrap = @"C:\Users\test\AppData\Local\PromptHelper";

        string resolved = policy.ValidateConfiguredRootForStartup(bootstrap, bootstrap);
        Assert.AreEqual(PathIdentity.NormalizeForComparison(bootstrap), resolved);
    }

    [TestMethod]
    public void CRUU4_009_Physical_alias_of_current_is_treated_as_same_root()
    {
        var fakeResolver = new FakePhysicalPathResolver();
        string realRoot = @"C:\Data\RealLibrary";
        string aliasRoot = @"C:\Aliases\CurrentLibrary";

        fakeResolver.AddMapping(aliasRoot, realRoot);
        fakeResolver.AddMapping(realRoot, realRoot);

        var policy = new ManagedDataRootPolicy(fakeResolver);

        // Disjointness check should treat alias and real as same root (no exception)
        policy.ValidateDisjointOrSame(realRoot, aliasRoot);
    }

    [TestMethod]
    public void CRUU4_009_Physical_alias_into_bootstrap_is_rejected()
    {
        var fakeResolver = new FakePhysicalPathResolver();
        string aliasIntoBootstrap = @"C:\Aliases\BootstrapChild";
        string bootstrap = @"C:\Users\test\AppData\Local\PromptHelper";
        string realInBootstrap = @"C:\Users\test\AppData\Local\PromptHelper\Inside";

        fakeResolver.AddMapping(aliasIntoBootstrap, realInBootstrap);
        fakeResolver.AddMapping(bootstrap, bootstrap);

        var policy = new ManagedDataRootPolicy(fakeResolver);

        Assert.Throws<InvalidDataException>(() =>
            policy.ValidateConfiguredRootForStartup(aliasIntoBootstrap, bootstrap));
    }
}
