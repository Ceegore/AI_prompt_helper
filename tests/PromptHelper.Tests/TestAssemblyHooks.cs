using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PromptHelper.Tests;

[TestClass]
public sealed class TestAssemblyHooks
{
    [AssemblyInitialize]
    public static void Initialize(TestContext _)
    {
        WpfTestHost.Start();
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        WpfTestHost.Stop();
    }
}
