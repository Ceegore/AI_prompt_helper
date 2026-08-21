using System.IO;
using PromptHelper.Services;

namespace PromptHelper.Tests;

public sealed class FaultInjectingFileDeleter : IFileDeleter, IVerifiedArtifactDeleter
{
    private readonly WindowsVerifiedArtifactDeleter _inner = new();
    public bool Fail { get; set; }

    public void DeleteIfExists(string path)
    {
        if (Fail)
        {
            throw new IOException("Injected delete failure.");
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void VerifyAndDelete(string physicalRoot, string path, long expectedLength, string expectedSha256Hex)
    {
        if (Fail)
        {
            throw new IOException("Injected delete failure.");
        }

        _inner.VerifyAndDelete(physicalRoot, path, expectedLength, expectedSha256Hex);
    }
}