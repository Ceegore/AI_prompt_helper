using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class DataRootTopologyValidatorTests
{
    [TestMethod]
    public void Topology_same_root_allowed()
    {
        using var temp = new TestDirectory();
        DataRootTopologyValidator.ValidateDisjointOrSame(temp.Root, temp.Root);
    }

    [TestMethod]
    public void Topology_sibling_roots_allowed()
    {
        using var parent = new TestDirectory();
        string current = Path.Combine(parent.Root, "CurrentRoot");
        string target = Path.Combine(parent.Root, "TargetRoot");
        string bootstrap = Path.Combine(parent.Root, "Bootstrap");

        DataRootTopologyValidator.ValidateDisjointOrSame(current, target, bootstrap);
    }

    [TestMethod]
    public void Topology_target_descendant_rejected()
    {
        using var temp = new TestDirectory();
        string current = temp.Root;
        string target = Path.Combine(temp.Root, "Nested", "Target");

        Assert.Throws<InvalidOperationException>(() =>
            DataRootTopologyValidator.ValidateDisjointOrSame(current, target));
    }

    [TestMethod]
    public void Topology_target_ancestor_rejected()
    {
        using var temp = new TestDirectory();
        string current = Path.Combine(temp.Root, "Nested", "Current");
        string target = temp.Root;

        Assert.Throws<InvalidOperationException>(() =>
            DataRootTopologyValidator.ValidateDisjointOrSame(current, target));
    }

    [TestMethod]
    public void Topology_custom_target_inside_bootstrap_rejected()
    {
        using var temp = new TestDirectory();
        string current = Path.Combine(temp.Root, "Current");
        string bootstrap = Path.Combine(temp.Root, "BootstrapPromptHelper");
        string target = Path.Combine(bootstrap, "InsideBootstrap");

        Assert.Throws<InvalidOperationException>(() =>
            DataRootTopologyValidator.ValidateDisjointOrSame(current, target, bootstrap));
    }

    [TestMethod]
    public void Topology_custom_target_ancestor_of_bootstrap_rejected()
    {
        using var temp = new TestDirectory();
        string current = Path.Combine(temp.Root, "Current");
        string bootstrap = Path.Combine(temp.Root, "LocalAppData", "PromptHelper");
        string target = Path.Combine(temp.Root, "LocalAppData");

        Assert.Throws<InvalidOperationException>(() =>
            DataRootTopologyValidator.ValidateDisjointOrSame(current, target, bootstrap));
    }

    [TestMethod]
    public void Topology_default_bootstrap_root_exactly_allowed()
    {
        using var temp = new TestDirectory();
        string current = Path.Combine(temp.Root, "Current");
        string bootstrap = Path.Combine(temp.Root, "LocalAppData", "PromptHelper");

        DataRootTopologyValidator.ValidateDisjointOrSame(current, bootstrap, bootstrap);
    }

    [TestMethod]
    public void Topology_volume_root_rejected()
    {
        using var temp = new TestDirectory();
        string rootVolume = Path.GetPathRoot(temp.Root) ?? "C:\\";

        Assert.Throws<InvalidOperationException>(() =>
            DataRootTopologyValidator.ValidateDisjointOrSame(temp.Root, rootVolume));
    }
}
