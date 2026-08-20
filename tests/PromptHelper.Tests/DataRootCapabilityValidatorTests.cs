using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Infrastructure;
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
        var baseWriter = new AtomicTextWriter();
        var faultWriter = new FaultInjectingAtomicTextWriter(baseWriter)
        {
            ShouldFail = (_, callNum) => callNum == 2
        };

        var validator = new DataRootCapabilityValidator(faultWriter);

        Assert.Throws<IOException>(() => validator.ValidateWritable(temp.Root));

        // Verify probe temporary files were best-effort cleaned
        string[] entries = Directory.GetFileSystemEntries(temp.Root);
        Assert.AreEqual(0, entries.Length);
    }
}
