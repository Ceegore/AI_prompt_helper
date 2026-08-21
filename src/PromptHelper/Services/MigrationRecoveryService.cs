using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace PromptHelper.Services;

public sealed record RecoveryResult(
    bool Success,
    string? ErrorMessage = null,
    Exception? Error = null);

public sealed class MigrationRecoveryService
{
    private readonly MigrationManifestRepository _manifestRepo;
    private readonly IMigrationFileOps _fileOps;
    private readonly IAuthorityFileOps _authorityOps;
    private readonly IVerifiedArtifactDeleter _verifiedDeleter;
    private readonly ManagedTreeTopologyValidator _treeValidator;

    internal MigrationRecoveryService(
        MigrationManifestRepository? manifestRepo = null,
        IMigrationFileOps? fileOps = null,
        IAuthorityFileOps? authorityOps = null,
        IVerifiedArtifactDeleter? verifiedDeleter = null,
        ManagedTreeTopologyValidator? treeValidator = null)
    {
        _manifestRepo = manifestRepo ?? new MigrationManifestRepository();
        _fileOps = fileOps ?? new DefaultMigrationFileOps();
        _authorityOps = authorityOps ?? new DefaultAuthorityFileOps();
        _verifiedDeleter = verifiedDeleter ?? new WindowsVerifiedArtifactDeleter();
        _treeValidator = treeValidator ?? new ManagedTreeTopologyValidator();
    }

    public RecoveryResult RecoverForRetry(MigrationRecoveryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string markerPath = Path.Combine(context.TargetPhysicalRoot, ".prompthelper-migration.json");

        MigrationAttemptManifest? manifest;
        try
        {
            manifest = _manifestRepo.TryReadStrict(markerPath);
        }
        catch (Exception ex)
        {
            var recEx = new MigrationRecoveryException(context.TargetPhysicalRoot, "ReadManifest", ex);
            return new RecoveryResult(false, recEx.Message, recEx);
        }

        if (manifest is null)
        {
            return new RecoveryResult(true);
        }

        try
        {
            if (!PathIdentity.Equals(context.TargetPhysicalRoot, manifest.TargetPhysicalRoot))
            {
                throw new InvalidDataException(
                    $"Migration manifest target root '{manifest.TargetPhysicalRoot}' does not match physical directory '{context.TargetPhysicalRoot}'.");
            }

            if (string.IsNullOrWhiteSpace(context.ExpectedSourcePhysicalRoot) ||
                !PathIdentity.Equals(context.ExpectedSourcePhysicalRoot, manifest.SourcePhysicalRoot))
            {
                throw new InvalidDataException(
                    "The interrupted migration belongs to a different source library. " +
                    "Prompt Helper will not delete it automatically.");
            }

            if (manifest.SchemaVersion >= 4)
            {
                if (!string.IsNullOrWhiteSpace(context.ExpectedSourcePayloadFingerprint) &&
                    !string.Equals(context.ExpectedSourcePayloadFingerprint, manifest.SourcePayloadFingerprintSha256Hex, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Source payload fingerprint changed since the migration attempt was created. " +
                        "Prompt Helper will not delete old attempt artifacts automatically.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(context.ExpectedSourceLibrarySha256) &&
                     !string.IsNullOrWhiteSpace(manifest.SourceLibrarySha256Hex))
            {
                if (!string.Equals(context.ExpectedSourceLibrarySha256, manifest.SourceLibrarySha256Hex, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Source library hash changed since the migration attempt was created. " +
                        "Prompt Helper will not delete old attempt artifacts automatically.");
                }
            }

            _treeValidator.ValidateManagedTree(context.TargetPhysicalRoot, ManagedTreeValidationMode.PreCreation);

            MigrationTargetInventory before = MigrationTargetInventoryInspector.Inspect(context.TargetPhysicalRoot, manifest);
            if (before.HasUnknownEntries)
            {
                throw new InvalidDataException(
                    $"Unrecognized or foreign files in migration target '{context.TargetPhysicalRoot}': {string.Join(", ", before.UnknownEntries)}. Recovery aborted to protect data.");
            }

            // 1. Delete declared control artifacts (probe files and directories, staging files)
            foreach (MigrationControlArtifact control in manifest.ControlArtifacts)
            {
                string controlPath = MigrationManifestRepository.ResolveManifestArtifactPath(context.TargetPhysicalRoot, control.RelativePath);
                if (_fileOps.FileExists(controlPath))
                {
                    _fileOps.DeleteFile(controlPath);
                }
                else if (_fileOps.DirectoryExists(controlPath))
                {
                    _fileOps.DeleteDirectory(controlPath);
                }
            }

            // 2. Delete declared payload temps
            foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
            {
                string tempFullPath = MigrationManifestRepository.ResolveManifestArtifactPath(context.TargetPhysicalRoot, artifact.TempRelativePath);
                if (_fileOps.FileExists(tempFullPath))
                {
                    _fileOps.DeleteFile(tempFullPath);
                }
            }

            // 3. Verify and delete finals using verified deleter
            foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
            {
                string finalFullPath = MigrationManifestRepository.ResolveManifestArtifactPath(context.TargetPhysicalRoot, artifact.RelativePath);
                _verifiedDeleter.VerifyAndDelete(context.TargetPhysicalRoot, finalFullPath, artifact.Length, artifact.Sha256Hex);
            }

            // 4. Remove attempt-created directories before retiring marker (throwing if deletion fails)
            string promptsDir = Path.Combine(context.TargetPhysicalRoot, "prompts");
            if ((manifest.TargetBaseline == null || !manifest.TargetBaseline.PromptsDirectoryExistedBefore) &&
                _fileOps.DirectoryExists(promptsDir))
            {
                _fileOps.DeleteDirectory(promptsDir);
            }

            string recoveryDir = Path.Combine(context.TargetPhysicalRoot, "recovery");
            if ((manifest.TargetBaseline == null || !manifest.TargetBaseline.RecoveryDirectoryExistedBefore) &&
                _fileOps.DirectoryExists(recoveryDir))
            {
                _fileOps.DeleteDirectory(recoveryDir);
            }

            // 5. Re-inspect inventory and assert all attempt-created directories and temps are gone
            MigrationTargetInventory after = MigrationTargetInventoryInspector.Inspect(context.TargetPhysicalRoot, manifest);
            if (after.HasUnknownEntries)
            {
                throw new InvalidDataException(
                    $"Unknown entries remain after cleanup in target '{context.TargetPhysicalRoot}': {string.Join(", ", after.UnknownEntries)}.");
            }

            if (after.PayloadTemps.Count > 0 || after.FinalArtifacts.Count > 0)
            {
                throw new InvalidDataException("Attempt payload artifacts remain after cleanup.");
            }

            if (after.AttemptCreatedDirectories.Count > 0)
            {
                throw new InvalidDataException(
                    $"Attempt-created directories still exist: {string.Join(", ", after.AttemptCreatedDirectories)}.");
            }

            // 6. Delete marker LAST
            _manifestRepo.DeleteStrict(markerPath);

            if (_authorityOps.GetPresenceStrict(markerPath) != StrictFilePresence.Missing)
            {
                throw new IOException($"Migration marker still exists after deletion: '{markerPath}'.");
            }

            return new RecoveryResult(true);
        }
        catch (Exception ex)
        {
            var recEx = ex as MigrationRecoveryException ?? new MigrationRecoveryException(context.TargetPhysicalRoot, "RecoverForRetry", ex);
            return new RecoveryResult(false, recEx.Message, recEx);
        }
    }

    public RecoveryResult FinalizeCommittedStartup(MigrationRecoveryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            _treeValidator.ValidateManagedTree(context.TargetPhysicalRoot, ManagedTreeValidationMode.PreCreation);

            string markerPath = Path.Combine(context.TargetPhysicalRoot, ".prompthelper-migration.json");

            MigrationAttemptManifest? manifest;
            try
            {
                manifest = _manifestRepo.TryReadStrict(markerPath);
            }
            catch (Exception ex)
            {
                var recEx = new MigrationRecoveryException(context.TargetPhysicalRoot, "ReadManifest", ex);
                return new RecoveryResult(false, recEx.Message, recEx);
            }

            if (manifest is null)
            {
                return new RecoveryResult(true);
            }

            if (manifest.Phase != MigrationManifestPhase.ReadyToCommit)
            {
                throw new InvalidDataException(
                    $"Incomplete migration attempt ({manifest.Phase}) found at configured data root '{context.TargetPhysicalRoot}'. Startup aborted.");
            }

            if (!PathIdentity.Equals(context.TargetPhysicalRoot, manifest.TargetPhysicalRoot))
            {
                throw new InvalidDataException(
                    $"Migration manifest target root '{manifest.TargetPhysicalRoot}' does not match configured physical root '{context.TargetPhysicalRoot}'.");
            }

            // Reconcile declared stage file if present before checking terminal inventory
            string stagePath = Path.Combine(context.TargetPhysicalRoot, $".prompthelper-migration.stage-{manifest.AttemptId:N}.tmp");
            if (_fileOps.FileExists(stagePath))
            {
                _fileOps.DeleteFile(stagePath);
            }

            // Verify no declared temps exist
            foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
            {
                string tempFullPath = MigrationManifestRepository.ResolveManifestArtifactPath(context.TargetPhysicalRoot, artifact.TempRelativePath);
                if (_authorityOps.GetPresenceStrict(tempFullPath) != StrictFilePresence.Missing)
                {
                    throw new InvalidDataException(
                        $"Incomplete migration state: declared temporary file '{artifact.TempRelativePath}' still exists at '{context.TargetPhysicalRoot}'.");
                }
            }

            // Verify no ephemeral controls exist
            foreach (MigrationControlArtifact control in manifest.ControlArtifacts)
            {
                string controlPath = MigrationManifestRepository.ResolveManifestArtifactPath(context.TargetPhysicalRoot, control.RelativePath);
                if (new StrictPathAuthority().Probe(controlPath).Kind != StrictPathKind.Missing)
                {
                    throw new InvalidDataException(
                        $"Incomplete migration state: ephemeral control '{control.RelativePath}' still exists at '{context.TargetPhysicalRoot}'.");
                }
            }

            // Verify all finals exist and match
            foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
            {
                string finalFullPath = MigrationManifestRepository.ResolveManifestArtifactPath(context.TargetPhysicalRoot, artifact.RelativePath);
                if (_authorityOps.GetPresenceStrict(finalFullPath) != StrictFilePresence.Present)
                {
                    throw new InvalidDataException(
                        $"Missing migration artifact '{artifact.RelativePath}' at configured root '{context.TargetPhysicalRoot}'.");
                }

                byte[]? bytes = _authorityOps.ReadOptionalBytesStrict(finalFullPath);
                if (bytes is null)
                {
                    throw new InvalidDataException($"Unreadable migration artifact '{artifact.RelativePath}'.");
                }

                if (bytes.Length != artifact.Length)
                {
                    throw new InvalidDataException(
                        $"Migration artifact '{artifact.RelativePath}' length mismatch at '{context.TargetPhysicalRoot}'. Expected {artifact.Length}, found {bytes.Length}.");
                }

                byte[] hash = SHA256.HashData(bytes);
                string hex = Convert.ToHexStringLower(hash);
                if (!string.Equals(hex, artifact.Sha256Hex, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Migration artifact '{artifact.RelativePath}' hash mismatch at '{context.TargetPhysicalRoot}'. Expected {artifact.Sha256Hex}, found {hex}.");
                }
            }

            // Verify no foreign files
            MigrationTargetInventory inventory = MigrationTargetInventoryInspector.Inspect(context.TargetPhysicalRoot, manifest);
            if (inventory.HasUnknownEntries)
            {
                throw new InvalidDataException(
                    $"Unrecognized or foreign files in configured target root '{context.TargetPhysicalRoot}': {string.Join(", ", inventory.UnknownEntries)}.");
            }

            // Retire marker
            _manifestRepo.DeleteStrict(markerPath);
            if (_authorityOps.GetPresenceStrict(markerPath) != StrictFilePresence.Missing)
            {
                throw new IOException(
                    "Migration completed and data is intact, but Prompt Helper could not retire its migration completion marker. " +
                    "No data was modified. Fix folder permissions and retry.");
            }

            return new RecoveryResult(true);
        }
        catch (Exception ex)
        {
            var recEx = ex as MigrationRecoveryException ?? new MigrationRecoveryException(context.TargetPhysicalRoot, "FinalizeCommittedStartup", ex);
            return new RecoveryResult(false, recEx.Message, recEx);
        }
    }
}
