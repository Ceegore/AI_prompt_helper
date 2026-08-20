using System;
using System.IO;
using System.Linq;
using PromptHelper.Infrastructure;

namespace PromptHelper.Services;

public sealed class DataRootCapabilityValidator
{
    private readonly IAtomicTextWriter _writer;

    public DataRootCapabilityValidator(IAtomicTextWriter? writer = null)
    {
        _writer = writer ?? new AtomicTextWriter();
    }

    public void ValidateWritable(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
        }

        ProbeLocation(root);

        string promptsDir = Path.Combine(root, "prompts");
        if (Directory.Exists(promptsDir))
        {
            ProbeLocation(promptsDir);
        }
    }

    private void ProbeLocation(string directory)
    {
        string probeDir = Path.Combine(
            directory,
            $".prompthelper-write-probe-{Guid.NewGuid():N}");

        string probeFile = Path.Combine(probeDir, "probe.txt");

        try
        {
            Directory.CreateDirectory(probeDir);

            _writer.Write(probeFile, "create");
            _writer.Write(probeFile, "replace"); // exercises File.Replace

            File.Delete(probeFile);
            Directory.Delete(probeDir);
        }
        catch
        {
            try
            {
                if (File.Exists(probeFile))
                {
                    File.Delete(probeFile);
                }

                if (Directory.Exists(probeDir) &&
                    !Directory.EnumerateFileSystemEntries(probeDir).Any())
                {
                    Directory.Delete(probeDir);
                }
            }
            catch
            {
                // Best effort cleanup; preserve original error
            }

            throw;
        }
    }
}
