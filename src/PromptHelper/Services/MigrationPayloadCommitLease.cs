using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

internal sealed class MigrationPayloadCommitLease : IDisposable
{
    private readonly List<SafeFileHandle> _handles = [];
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

                lease.OpenAndVerify(sourcePath, sourcePhysicalRoot, artifact.Length, artifact.Sha256Hex);
                lease.OpenAndVerify(targetPath, targetPhysicalRoot, artifact.Length, artifact.Sha256Hex);
            }

            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private void OpenAndVerify(string path, string physicalRoot, long expectedLength, string expectedSha256Hex)
    {
        // CRUU14-008: reject reparse-point files and assert the final physical path is a
        // strict descendant of the expected root, instead of trusting an ordinary FileStream
        // open (which follows reparse points) to have resolved to the intended managed file.
        SafeFileHandle handle = WindowsStrictFileOpener.OpenNonReparseUnderRoot(path, physicalRoot);

        try
        {
            long length = RandomAccess.GetLength(handle);
            if (length != expectedLength)
            {
                throw new IOException(
                    $"Payload file length mismatch during commit lease acquisition on '{path}'. Expected={expectedLength}, Actual={length}");
            }

            byte[] bytes = new byte[length];
            RandomAccess.Read(handle, bytes, 0);
            string actualSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));

            if (!string.Equals(actualSha256, expectedSha256Hex, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    $"Payload file content hash mismatch during commit lease acquisition on '{path}'. Expected={expectedSha256Hex}, Actual={actualSha256}");
            }

            _handles.Add(handle);
        }
        catch
        {
            handle.Dispose();
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
        foreach (SafeFileHandle handle in _handles)
        {
            handle.Dispose();
        }

        _handles.Clear();
    }
}
