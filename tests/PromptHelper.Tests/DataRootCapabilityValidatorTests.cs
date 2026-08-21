using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class DataRootCapabilityValidatorTests
{
    [TestMethod]
    public void Capability_probe_exercises_create_replace_and_cleans_on_success()
    {
        using var temp = new TestDirectory();
        var validator = new DataRootCapabilityValidator();

        validator.ValidateWritable(temp.Root);

        // Verify probe directory and files were deleted
        string[] entries = Directory.GetFileSystemEntries(temp.Root);
        Assert.AreEqual(0, entries.Length);
    }

    [TestMethod]
    public void Capability_probe_replace_failure_throws_and_cleans_temporary_files()
    {
        using var temp = new TestDirectory();
        var ops = new FakeCapabilityFileOps
        {
            OnReplace = (src, dst, bak) => throw new IOException("Simulated replace failure")
        };

        var validator = new DataRootCapabilityValidator(ops);

        Assert.Throws<IOException>(() => validator.ValidateWritable(temp.Root));

        // Verify probe temporary files were best-effort cleaned
        string[] entries = Directory.GetFileSystemEntries(temp.Root);
        Assert.AreEqual(0, entries.Length);
    }

    [TestMethod]
    public void CRUU6_006_Probe_cleanup_failure_is_reported()
    {
        using var temp = new TestDirectory();
        var ops = new FakeCapabilityFileOps
        {
            OnReplace = (src, dst, bak) => throw new IOException("Simulated replace failure"),
            OnDeleteFile = path => throw new IOException("Simulated delete failure")
        };

        var validator = new DataRootCapabilityValidator(ops);

        var ex = Assert.Throws<DataRootCapabilityProbeException>(() => validator.ValidateWritable(temp.Root));
        Assert.IsTrue(ex.CleanupFailures.Count > 0);
        Assert.AreEqual("DeleteProbeCurrentFile", ex.CleanupFailures[0].Operation);
    }
}
