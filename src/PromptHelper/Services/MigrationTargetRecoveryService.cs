using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace PromptHelper.Services;

internal static class MigrationTargetRecoveryService
{
    public static void ResolveInterruptedTarget(
        string targetRoot,
        MigrationManifestRepository? manifestRepo = null,
        IMigrationFileOps? fileOps = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRoot);

        var repo = manifestRepo ?? new MigrationManifestRepository();
        var ops = fileOps ?? new DefaultMigrationFileOps();

        string markerPath = Path.Combine(targetRoot, ".prompthelper-migration.json");
        if (!ops.FileExists(markerPath))
        {
            return;
        }

        MigrationAttemptManifest manifest;
        try
        {
            var maybe = repo.TryRead(markerPath);
            if (maybe == null)
            {
                throw new InvalidDataException(
                    $"Corrupted or invalid migration manifest at '{markerPath}'.");
            }
            manifest = maybe;
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException(
                $"Unable to read migration manifest at '{markerPath}': {ex.Message}",
                ex);
        }

        string normalizedTarget = PathIdentity.NormalizeForComparison(targetRoot);
        string normalizedManifestTarget = PathIdentity.NormalizeForComparison(manifest.TargetPhysicalRoot);

        if (!PathIdentity.Equals(normalizedTarget, normalizedManifestTarget))
        {
            throw new InvalidDataException(
                $"Migration manifest target root '{manifest.TargetPhysicalRoot}' does not match physical directory '{targetRoot}'.");
        }

        var ownedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
        {
            ownedRelativePaths.Add(artifact.RelativePath);
        }

        // 1. Foreign file detection
        IReadOnlyList<string> allTargetFiles = ops.EnumerateFiles(targetRoot, "*");
        foreach (string file in allTargetFiles)
        {
            string fileName = Path.GetFileName(file);
            if (string.Equals(fileName, ".prompthelper-migration.json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, ".app.lock", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string rel = Path.GetRelativePath(targetRoot, file);
            if (!ownedRelativePaths.Contains(rel))
            {
                throw new InvalidDataException(
                    $"Unrecognized or foreign file '{rel}' in migration target '{targetRoot}'. Transition aborted.");
            }
        }

        // 2. Strict artifact verification
        foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
        {
            string artifactFullPath = MigrationManifestRepository.ResolveManifestArtifactPath(targetRoot, artifact.RelativePath);

            if (!ops.FileExists(artifactFullPath))
            {
                continue;
            }

            byte[] currentBytes = ops.ReadAllBytes(artifactFullPath);
            if (currentBytes.Length != artifact.Length)
            {
                throw new InvalidDataException(
                    $"Migration artifact '{artifact.RelativePath}' length mismatch in target '{targetRoot}'. Expected {artifact.Length} bytes, found {currentBytes.Length} bytes.");
            }

            byte[] currentHash = SHA256.HashData(currentBytes);
            string currentHex = Convert.ToHexStringLower(currentHash);

            if (!string.Equals(currentHex, artifact.Sha256Hex, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Migration artifact '{artifact.RelativePath}' hash mismatch in target '{targetRoot}'. Expected {artifact.Sha256Hex}, found {currentHex}.");
            }
        }

        // 3. Deletion of verified manifest-owned files
        foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
        {
            string artifactFullPath = MigrationManifestRepository.ResolveManifestArtifactPath(targetRoot, artifact.RelativePath);
            if (ops.FileExists(artifactFullPath))
            {
                ops.DeleteFile(artifactFullPath);
            }
        }

        // 4. Clean empty subdirectories (e.g. prompts, recovery)
        string promptsDir = Path.Combine(targetRoot, "prompts");
        if (ops.DirectoryExists(promptsDir) && ops.EnumerateEntries(promptsDir).Count == 0)
        {
            ops.DeleteDirectory(promptsDir);
        }

        string recoveryDir = Path.Combine(targetRoot, "recovery");
        if (ops.DirectoryExists(recoveryDir) && ops.EnumerateEntries(recoveryDir).Count == 0)
        {
            ops.DeleteDirectory(recoveryDir);
        }

        // 5. Delete manifest marker
        repo.Delete(markerPath);
    }
}
