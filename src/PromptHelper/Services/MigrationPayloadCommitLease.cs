using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace PromptHelper.Services;

internal sealed class MigrationPayloadCommitLease : IDisposable
{
    private readonly List<FileStream> _streams = [];
    private bool _disposed;

    private MigrationPayloadCommitLease() { }

    public static MigrationPayloadCommitLease Acquire(
        string sourcePhysicalRoot,
        string targetPhysicalRoot,
        MigrationAttemptManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePhysicalRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPhysicalRoot);
        ArgumentNullException.ThrowIfNull(manifest);

        var lease = new MigrationPayloadCommitLease();

        try
        {
            foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
            {
                string sourcePath = Path.Combine(sourcePhysicalRoot, artifact.RelativePath);
                string targetPath = Path.Combine(targetPhysicalRoot, artifact.RelativePath);

                lease.OpenAndVerify(sourcePath, artifact.Length, artifact.Sha256Hex);
                lease.OpenAndVerify(targetPath, artifact.Length, artifact.Sha256Hex);
            }

            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private void OpenAndVerify(string path, long expectedLength, string expectedSha256Hex)
    {
        FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        try
        {
            if (stream.Length != expectedLength)
            {
                throw new IOException(
                    $"Payload file length mismatch during commit lease acquisition on '{path}'. Expected={expectedLength}, Actual={stream.Length}");
            }

            byte[] hash = SHA256.HashData(stream);
            string actualSha256 = Convert.ToHexStringLower(hash);

            if (!string.Equals(actualSha256, expectedSha256Hex, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    $"Payload file content hash mismatch during commit lease acquisition on '{path}'. Expected={expectedSha256Hex}, Actual={actualSha256}");
            }

            _streams.Add(stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (FileStream stream in _streams)
        {
            stream.Dispose();
        }

        _streams.Clear();
    }
}
