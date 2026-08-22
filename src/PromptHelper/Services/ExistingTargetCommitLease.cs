using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using PromptHelper.Models;

namespace PromptHelper.Services;

/// <summary>
/// Opens and hashes an existing target library's metadata file and every active prompt body,
/// holding all of them open (denying concurrent writers) through the commit window from
/// immediately before settings commit to <see cref="AppSettingsRepository.SaveIfUnchanged"/>
/// returning. Unlike <see cref="MigrationPayloadCommitLease"/> (which binds a migration's own
/// copied payload), this binds the pre-existing target content that the transition is about to
/// point settings at, closing the gap where an external process could still edit it between
/// the last inspection and the settings commit.
/// </summary>
internal sealed class ExistingTargetCommitLease : IDisposable
{
    private readonly List<SafeFileHandle> _handles = [];
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
            byte[] metadataBytes = lease.OpenAndRead(metadataPath, targetPhysicalRoot);

            string promptsDir = Path.Combine(targetPhysicalRoot, "prompts");
            var promptHashes = new Dictionary<Guid, byte[]>();
            foreach (PromptRecord prompt in document.Prompts)
            {
                string promptPath = Path.Combine(promptsDir, $"{prompt.Id:N}.md");
                byte[] bodyBytes = lease.OpenAndRead(promptPath, targetPhysicalRoot);

                // CRUU14-007: revalidate strict UTF-8 from the exact bytes held by this
                // lease, not just the hash computed from an earlier inspection pass.
                try
                {
                    StrictUtf8Text.Decode(bodyBytes, $"prompt file '{prompt.Id:N}.md'");
                }
                catch (InvalidDataException ex)
                {
                    throw new IOException(
                        $"Target prompt file '{prompt.Id:N}.md' is not valid UTF-8 text. Transition cancelled.",
                        ex);
                }

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

    private byte[] OpenAndRead(string path, string physicalRoot)
    {
        // CRUU14-008: reject reparse-point files and assert the final physical path is a
        // strict descendant of the expected root, instead of trusting an ordinary FileStream
        // open (which follows reparse points) to have resolved to the intended managed file.
        SafeFileHandle handle = WindowsStrictFileOpener.OpenNonReparseUnderRoot(path, physicalRoot);

        try
        {
            long length = RandomAccess.GetLength(handle);
            byte[] bytes = new byte[length];
            RandomAccess.Read(handle, bytes, 0);
            _handles.Add(handle);
            return bytes;
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
