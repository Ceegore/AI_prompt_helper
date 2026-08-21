using System;
using System.IO;
using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class FakeVerifiedArtifactDeleter : IVerifiedArtifactDeleter
{
    private readonly IVerifiedArtifactDeleter _inner = new WindowsVerifiedArtifactDeleter();

    public Action<string, long, string>? OnVerifyAndDelete { get; set; }

    public void VerifyAndDelete(string path, long expectedLength, string expectedSha256Hex)
    {
        if (OnVerifyAndDelete != null)
        {
            OnVerifyAndDelete(path, expectedLength, expectedSha256Hex);
            return;
        }

        _inner.VerifyAndDelete(path, expectedLength, expectedSha256Hex);
    }
}
