using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace PromptHelper.Services;

internal sealed record RecoveryResult(
    bool Success,
    string? ErrorMessage = null,
    Exception? Error = null);

internal sealed class TargetRecoveryInventory
{
    public HashSet<string> AllowedPersistentBaseline { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ManifestFinals { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ManifestTemps { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> UnknownEntries { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasUnknownEntries => UnknownEntries.Count > 0;
}

internal sealed class MigrationRecoveryService
{
    private readonly MigrationManifestRepository _manifestRepo;
    private readonly IMigrationFileOps _fileOps;
    private readonly IVerifiedArtifactDeleter _verifiedDeleter;

    public MigrationRecoveryService(
        MigrationManifestRepository? manifestRepo = null,
        IMigrationFileOps? fileOps = null,
        IVerifiedArtifactDeleter? verifiedDeleter = null)
    {
        _manifestRepo = manifestRepo ?? new MigrationManifestRepository();
        _fileOps = fileOps ?? new DefaultMigrationFileOps();
        _verifiedDeleter = verifiedDeleter ?? new WindowsVerifiedArtifactDeleter();
    }

    private static string NormalizeRel(string p) =>
        p.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

    public TargetRecoveryInventory BuildInventory(
        string targetRoot,
        MigrationAttemptManifest manifest,
        MigrationRecoveryContext context)
    {
        var inventory = new TargetRecoveryInventory();

        foreach (string persistent in context.AllowedPersistentRelativePaths)
        {
            inventory.AllowedPersistentBaseline.Add(NormalizeRel(persistent));
        }

        foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
        {
            inventory.ManifestFinals.Add(NormalizeRel(artifact.RelativePath));
            inventory.ManifestTemps.Add(NormalizeRel(artifact.TempRelativePath));
        }

        if (!_fileOps.DirectoryExists(targetRoot))
        {
            return inventory;
        }

        void ScanDirectory(string dir)
        {
            foreach (string file in _fileOps.EnumerateFiles(dir, "*"))
            {
                string rel = NormalizeRel(Path.GetRelativePath(targetRoot, file));
                string fileName = Path.GetFileName(file);

                if (string.Equals(fileName, ".prompthelper-migration.json", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, ".app.lock", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (inventory.AllowedPersistentBaseline.Contains(rel) ||
                    inventory.ManifestFinals.Contains(rel) ||
                    inventory.ManifestTemps.Contains(rel))
                {
                    continue;
                }

                inventory.UnknownEntries.Add(rel);
            }

            foreach (string entry in _fileOps.EnumerateEntries(dir))
            {
                if (_fileOps.DirectoryExists(entry))
                {
                    string dirName = Path.GetFileName(entry);
                    if (string.Equals(dirName, "prompts", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dirName, "recovery", StringComparison.OrdinalIgnoreCase))
                    {
                        ScanDirectory(entry);
                    }
                    else
                    {
                        string relDir = NormalizeRel(Path.GetRelativePath(targetRoot, entry));
                        inventory.UnknownEntries.Add(relDir);
                    }
                }
            }
        }

        ScanDirectory(targetRoot);
        return inventory;
    }

    public RecoveryResult RecoverForRetry(MigrationRecoveryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string markerPath = Path.Combine(context.TargetPhysicalRoot, ".prompthelper-migration.json");
        if (!_fileOps.FileExists(markerPath))
        {
            return new RecoveryResult(true);
        }

        MigrationAttemptManifest manifest;
        try
        {
            var maybe = _manifestRepo.TryRead(markerPath);
            if (maybe == null)
            {
                throw new InvalidDataException($"Corrupted or unreadable migration manifest at '{markerPath}'.");
            }
            manifest = maybe;
        }
        catch (Exception ex)
        {
            return new RecoveryResult(false, ex.Message, ex);
        }

        if (!PathIdentity.Equals(context.TargetPhysicalRoot, manifest.TargetPhysicalRoot))
        {
            var ex = new InvalidDataException(
                $"Migration manifest target root '{manifest.TargetPhysicalRoot}' does not match physical directory '{context.TargetPhysicalRoot}'.");
            return new RecoveryResult(false, ex.Message, ex);
        }

        TargetRecoveryInventory inventory = BuildInventory(context.TargetPhysicalRoot, manifest, context);
        if (inventory.HasUnknownEntries)
        {
            var ex = new InvalidDataException(
                $"Unrecognized or foreign files in migration target '{context.TargetPhysicalRoot}': {string.Join(", ", inventory.UnknownEntries)}. Recovery aborted to protect data.");
            return new RecoveryResult(false, ex.Message, ex);
        }

        // 1. Delete declared attempt temps
        foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
        {
            string tempFullPath = MigrationManifestRepository.ResolveManifestArtifactPath(context.TargetPhysicalRoot, artifact.TempRelativePath);
            if (_fileOps.FileExists(tempFullPath))
            {
                _fileOps.DeleteFile(tempFullPath);
            }
        }

        // 2. Verify and delete finals using verified deleter
        foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
        {
            string finalFullPath = MigrationManifestRepository.ResolveManifestArtifactPath(context.TargetPhysicalRoot, artifact.RelativePath);
            if (_fileOps.FileExists(finalFullPath))
            {
                try
                {
                    _verifiedDeleter.VerifyAndDelete(finalFullPath, artifact.Length, artifact.Sha256Hex);
                }
                catch (Exception ex)
                {
                    return new RecoveryResult(false, $"Failed verified deletion of '{artifact.RelativePath}': {ex.Message}", ex);
                }
            }
        }

        // 3. Clean empty subdirectories (prompts, recovery)
        string promptsDir = Path.Combine(context.TargetPhysicalRoot, "prompts");
        if (_fileOps.DirectoryExists(promptsDir) && _fileOps.EnumerateEntries(promptsDir).Count == 0)
        {
            _fileOps.DeleteDirectory(promptsDir);
        }

        string recoveryDir = Path.Combine(context.TargetPhysicalRoot, "recovery");
        if (_fileOps.DirectoryExists(recoveryDir) && _fileOps.EnumerateEntries(recoveryDir).Count == 0)
        {
            _fileOps.DeleteDirectory(recoveryDir);
        }

        // 4. Re-inventory before marker delete
        TargetRecoveryInventory postCleanInventory = BuildInventory(context.TargetPhysicalRoot, manifest, context);
        if (postCleanInventory.ManifestFinals.Count > 0)
        {
            foreach (string finalRel in postCleanInventory.ManifestFinals)
            {
                string path = MigrationManifestRepository.ResolveManifestArtifactPath(context.TargetPhysicalRoot, finalRel);
                if (_fileOps.FileExists(path))
                {
                    var ex = new InvalidDataException($"Manifest artifact '{finalRel}' still exists after cleanup.");
                    return new RecoveryResult(false, ex.Message, ex);
                }
            }
        }

        // 5. Delete marker last
        _manifestRepo.DeleteDurable(markerPath);
        return new RecoveryResult(true);
    }

    public RecoveryResult FinalizeCommittedStartup(MigrationRecoveryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string markerPath = Path.Combine(context.TargetPhysicalRoot, ".prompthelper-migration.json");
        if (!_fileOps.FileExists(markerPath))
        {
            return new RecoveryResult(true);
        }

        MigrationAttemptManifest manifest;
        try
        {
            var maybe = _manifestRepo.TryRead(markerPath);
            if (maybe == null)
            {
                throw new InvalidDataException($"Corrupted or unreadable migration manifest at '{markerPath}'.");
            }
            manifest = maybe;
        }
        catch (Exception ex)
        {
            return new RecoveryResult(false, $"Failed to inspect migration manifest: {ex.Message}", ex);
        }

        if (manifest.Phase != MigrationManifestPhase.ReadyToCommit)
        {
            var ex = new InvalidDataException(
                $"Incomplete migration attempt ({manifest.Phase}) found at configured data root '{context.TargetPhysicalRoot}'. Startup aborted.");
            return new RecoveryResult(false, ex.Message, ex);
        }

        if (!PathIdentity.Equals(context.TargetPhysicalRoot, manifest.TargetPhysicalRoot))
        {
            var ex = new InvalidDataException(
                $"Migration manifest target root '{manifest.TargetPhysicalRoot}' does not match configured physical root '{context.TargetPhysicalRoot}'.");
            return new RecoveryResult(false, ex.Message, ex);
        }

        // Verify no declared temps exist
        foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
        {
            string tempFullPath = MigrationManifestRepository.ResolveManifestArtifactPath(context.TargetPhysicalRoot, artifact.TempRelativePath);
            if (_fileOps.FileExists(tempFullPath))
            {
                var ex = new InvalidDataException(
                    $"Incomplete migration state: declared temporary file '{artifact.TempRelativePath}' still exists at '{context.TargetPhysicalRoot}'.");
                return new RecoveryResult(false, ex.Message, ex);
            }
        }

        // Verify all finals exist and match
        foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
        {
            string finalFullPath = MigrationManifestRepository.ResolveManifestArtifactPath(context.TargetPhysicalRoot, artifact.RelativePath);
            if (!_fileOps.FileExists(finalFullPath))
            {
                var ex = new InvalidDataException(
                    $"Missing migration artifact '{artifact.RelativePath}' at configured root '{context.TargetPhysicalRoot}'.");
                return new RecoveryResult(false, ex.Message, ex);
            }

            byte[] bytes = _fileOps.ReadAllBytes(finalFullPath);
            if (bytes.Length != artifact.Length)
            {
                var ex = new InvalidDataException(
                    $"Migration artifact '{artifact.RelativePath}' length mismatch at '{context.TargetPhysicalRoot}'. Expected {artifact.Length}, found {bytes.Length}.");
                return new RecoveryResult(false, ex.Message, ex);
            }

            byte[] hash = SHA256.HashData(bytes);
            string hex = Convert.ToHexStringLower(hash);
            if (!string.Equals(hex, artifact.Sha256Hex, StringComparison.OrdinalIgnoreCase))
            {
                var ex = new InvalidDataException(
                    $"Migration artifact '{artifact.RelativePath}' hash mismatch at '{context.TargetPhysicalRoot}'. Expected {artifact.Sha256Hex}, found {hex}.");
                return new RecoveryResult(false, ex.Message, ex);
            }
        }

        // Verify no foreign files
        TargetRecoveryInventory inventory = BuildInventory(context.TargetPhysicalRoot, manifest, context);
        if (inventory.HasUnknownEntries)
        {
            var ex = new InvalidDataException(
                $"Unrecognized or foreign files in configured target root '{context.TargetPhysicalRoot}': {string.Join(", ", inventory.UnknownEntries)}.");
            return new RecoveryResult(false, ex.Message, ex);
        }

        // Retire marker
        try
        {
            _manifestRepo.DeleteDurable(markerPath);
            if (_fileOps.FileExists(markerPath))
            {
                throw new IOException($"Migration marker file '{markerPath}' could not be deleted.");
            }
        }
        catch (Exception ex)
        {
            return new RecoveryResult(
                false,
                "Migration completed and data is intact, but Prompt Helper could not retire its migration completion marker. " +
                "No data was modified. Fix folder permissions and retry.",
                ex);
        }

        return new RecoveryResult(true);
    }
}
