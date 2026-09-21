using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Core.Tests;

[TestClass]
public sealed class CoreArchitectureTests
{
    [TestMethod]
    public void Core_targets_platform_neutral_net10()
    {
        Assembly assembly = typeof(PathIdentity).Assembly;
        TargetFrameworkAttribute? target = assembly.GetCustomAttribute<TargetFrameworkAttribute>();

        Assert.IsNotNull(target);
        StringAssert.Contains(target.FrameworkName, ".NETCoreApp,Version=v10.0");
        Assert.IsFalse(
            target.FrameworkName.Contains("Windows", StringComparison.OrdinalIgnoreCase),
            "PromptHelper.Core must remain platform-neutral.");
    }

    [TestMethod]
    public void Core_does_not_reference_WPF_assemblies()
    {
        string[] forbidden =
        [
            "PresentationFramework",
            "PresentationCore",
            "WindowsBase"
        ];

        HashSet<string> references = typeof(PathIdentity).Assembly
            .GetReferencedAssemblies()
            .Select(x => x.Name ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string assemblyName in forbidden)
        {
            Assert.IsFalse(
                references.Contains(assemblyName),
                $"PromptHelper.Core must not reference WPF assembly '{assemblyName}'.");
        }
    }
}
