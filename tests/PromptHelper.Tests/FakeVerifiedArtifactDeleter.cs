using System;
using System.IO;
using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class FakeVerifiedArtifactDeleter : IVerifiedArtifactDeleter
{
    private readonly IVerifiedArtifactDeleter _inner = new WindowsVerifiedArtifactDeleter();

    public Action<string, string, long, string>? OnVerifyAndDelete { get; set; }
    public Action<string, string>? OnVerifyIdentityAndDelete { get; set; }

    public void VerifyAndDelete(string physicalRoot, string path, long expectedLength, string expectedSha256Hex)
    {
        if (OnVerifyAndDelete != null)
        {
            OnVerifyAndDelete(physicalRoot, path, expectedLength, expectedSha256Hex);
            return;
        }

        _inner.VerifyAndDelete(physicalRoot, path, expectedLength, expectedSha256Hex);
    }

    public void VerifyIdentityAndDelete(string physicalRoot, string path)
    {
        if (OnVerifyIdentityAndDelete != null)
        {
            OnVerifyIdentityAndDelete(physicalRoot, path);
            return;
        }

        _inner.VerifyIdentityAndDelete(physicalRoot, path);
    }
}
