using System;
using System.ComponentModel;
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

        var relationship = policy.ValidateTransition(realRoot, aliasRoot);
        Assert.IsTrue(relationship.SamePhysicalRoot);
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

    [TestMethod]
    public void CRUU5_006_Resolver_IO_failure_aborts_transition()
    {
        var resolver = new FakePhysicalPathResolver
        {
            Failure = new IOException("Injected path resolution IO failure")
        };

        var policy = new ManagedDataRootPolicy(resolver);

        Assert.Throws<InvalidOperationException>(() =>
            policy.ValidateTransition(@"C:\Data\Current", @"C:\Data\Target"));
    }

    [TestMethod]
    public void CRUU5_006_Resolver_Unauthorized_failure_aborts_transition()
    {
        var resolver = new FakePhysicalPathResolver
        {
            Failure = new UnauthorizedAccessException("Injected access denied")
        };

        var policy = new ManagedDataRootPolicy(resolver);

        Assert.Throws<InvalidOperationException>(() =>
            policy.ValidateTransition(@"C:\Data\Current", @"C:\Data\Target"));
    }

    [TestMethod]
    public void CRUU5_006_Resolver_Win32_failure_aborts_transition()
    {
        var resolver = new FakePhysicalPathResolver
        {
            Failure = new Win32Exception(5, "Access is denied")
        };

        var policy = new ManagedDataRootPolicy(resolver);

        Assert.Throws<InvalidOperationException>(() =>
            policy.ValidateTransition(@"C:\Data\Current", @"C:\Data\Target"));
    }

    [TestMethod]
    public void CRUU5_007_Alias_resolving_to_volume_root_is_rejected()
    {
        var resolver = new FakePhysicalPathResolver();
        string alias = @"C:\Aliases\LooksSafe";
        string current = @"C:\Data\Active";
        string bootstrap = @"C:\Users\Test\AppData\Local\PromptHelper";

        resolver.AddMapping(alias, @"D:\");
        resolver.AddMapping(current, current);
        resolver.AddMapping(bootstrap, bootstrap);

        var policy = new ManagedDataRootPolicy(resolver);

        Assert.Throws<InvalidOperationException>(() =>
            policy.ValidateTransition(
                current,
                alias,
                bootstrap));
    }

    [TestMethod]
    public void CRUU5_007_Physical_UNC_share_root_alias_rejected()
    {
        var resolver = new FakePhysicalPathResolver();
        string alias = @"C:\Aliases\UncShare";
        string current = @"C:\Data\Active";
        string bootstrap = @"C:\Users\Test\AppData\Local\PromptHelper";

        resolver.AddMapping(alias, @"\\server\share");
        resolver.AddMapping(current, current);
        resolver.AddMapping(bootstrap, bootstrap);

        var policy = new ManagedDataRootPolicy(resolver);

        Assert.Throws<InvalidOperationException>(() =>
            policy.ValidateTransition(
                current,
                alias,
                bootstrap));
    }

    [TestMethod]
    public void CRUU5_007_Startup_rejects_physical_volume_alias()
    {
        var resolver = new FakePhysicalPathResolver();
        string alias = @"C:\Aliases\PointsToVolume";
        string bootstrap = @"C:\Users\Test\AppData\Local\PromptHelper";

        resolver.AddMapping(alias, @"E:\");
        resolver.AddMapping(bootstrap, bootstrap);

        var policy = new ManagedDataRootPolicy(resolver);

        Assert.Throws<InvalidDataException>(() =>
            policy.ValidateConfiguredRootForStartup(alias, bootstrap));
    }

    [TestMethod]
    public void CRUU6_011_Unavailable_configured_root_uses_dedicated_safety_error()
    {
        string bootstrap = @"C:\Users\Test\AppData\Local\PromptHelper";
        string configured = @"E:\Removable\CustomData";

        // DirectoryNotFound
        var resolver1 = new FakePhysicalPathResolver
        {
            Failure = new DirectoryNotFoundException("Could not find part of path")
        };
        var policy1 = new ManagedDataRootPolicy(resolver1);
        var ex1 = Assert.Throws<ConfiguredDataFolderUnavailableException>(() =>
            policy1.ValidateConfiguredRootForStartup(configured, bootstrap));
        Assert.AreEqual(Path.GetFullPath(configured), ex1.DataFolderPath);

        // DriveNotFound
        var resolver2 = new FakePhysicalPathResolver
        {
            Failure = new DriveNotFoundException("Drive not found")
        };
        var policy2 = new ManagedDataRootPolicy(resolver2);
        var ex2 = Assert.Throws<ConfiguredDataFolderUnavailableException>(() =>
            policy2.ValidateConfiguredRootForStartup(configured, bootstrap));
        Assert.AreEqual(Path.GetFullPath(configured), ex2.DataFolderPath);

        // Win32 path not found
        var resolver3 = new FakePhysicalPathResolver
        {
            Failure = new Win32Exception(3, "The system cannot find the path specified")
        };
        var policy3 = new ManagedDataRootPolicy(resolver3);
        var ex3 = Assert.Throws<ConfiguredDataFolderUnavailableException>(() =>
            policy3.ValidateConfiguredRootForStartup(configured, bootstrap));
        Assert.AreEqual(Path.GetFullPath(configured), ex3.DataFolderPath);
    }
}
