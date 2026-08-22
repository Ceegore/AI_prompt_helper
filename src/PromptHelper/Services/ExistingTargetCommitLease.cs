using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using PromptHelper.Models;

namespace PromptHelper.Services;

/// <summary>
/// Opens and hashes an existing target library's metadata file and every active prompt body,
/// holding all of them open with <see cref="FileShare.Read"/> (denying concurrent writers)
/// through the commit window from immediately before settings commit to
/// <see cref="AppSettingsRepository.SaveIfUnchanged"/> returning. Unlike
/// <see cref="MigrationPayloadCommitLease"/> (which binds a migration's own copied payload),
/// this binds the pre-existing target content that the transition is about to point settings
/// at, closing the gap where an external process could still edit it between the last
/// inspection and the settings commit.
/// </summary>
internal sealed class ExistingTargetCommitLease : IDisposable
{
    private readonly List<FileStream> _streams = [];
    private bool _disposed;

    private ExistingTargetCommitLease() { }

    public static ExistingTargetCommitLease Acquire(
        string targetPhysicalRoot,
        string metadataPath,
        LibraryDocument document,
        byte[] expectedCombinedFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPhysicalRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataPath);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(expectedCombinedFingerprint);

        var lease = new ExistingTargetCommitLease();

        try
        {
            byte[] metadataBytes = lease.OpenAndRead(metadataPath);

            string promptsDir = Path.Combine(targetPhysicalRoot, "prompts");
            var promptHashes = new Dictionary<Guid, byte[]>();
            foreach (PromptRecord prompt in document.Prompts)
            {
                string promptPath = Path.Combine(promptsDir, $"{prompt.Id:N}.md");
                byte[] bodyBytes = lease.OpenAndRead(promptPath);
                promptHashes[prompt.Id] = SHA256.HashData(bodyBytes);
            }

            byte[] actualFingerprint = DataFolderMigrationService.ComputeCombinedFingerprint(metadataBytes, promptHashes);
            if (!actualFingerprint.AsSpan().SequenceEqual(expectedCombinedFingerprint))
            {
                throw new IOException(
                    "Target library content changed while acquiring the commit lease. Transition cancelled.");
            }

            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private byte[] OpenAndRead(string path)
    {
        FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        try
        {
            byte[] bytes = new byte[stream.Length];
            int total = 0;
            while (total < bytes.Length)
            {
                int read = stream.Read(bytes, total, bytes.Length - total);
                if (read == 0)
                {
                    break;
                }
                total += read;
            }

            stream.Position = 0;
            _streams.Add(stream);
            return bytes;
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
