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
            else
            {
                if (!string.IsNullOrWhiteSpace(context.ExpectedSourceLibrarySha256) &&
                    !string.IsNullOrWhiteSpace(manifest.SourceLibrarySha256Hex) &&
                    !string.Equals(context.ExpectedSourceLibrarySha256, manifest.SourceLibrarySha256Hex, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Source library hash changed since the migration attempt was created. " +
                        "Prompt Helper will not delete old attempt artifacts automatically.");
                }

                // A pre-v4 manifest has no recorded full-payload fingerprint, but the same
                // fingerprint algorithm can be derived from its own declared artifacts and
                // compared against the caller's current full-payload fingerprint. Checking
                // only the primary library hash (above) would miss a source prompt body
                // that changed without the library metadata changing; fail closed before any
                // deletion if the full payload no longer matches what this attempt captured.
                if (!string.IsNullOrWhiteSpace(context.ExpectedSourcePayloadFingerprint))
                {
                    string derivedManifestFingerprint =
                        MigrationPayloadFingerprint.ComputeFromManifestArtifacts(manifest.Artifacts);

                    if (!string.Equals(context.ExpectedSourcePayloadFingerprint, derivedManifestFingerprint, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "Source payload fingerprint changed since the migration attempt was created. " +
                            "Prompt Helper will not delete old attempt artifacts automatically.");
                    }
                }
            }

            _treeValidator.ValidateManagedTree(context.TargetPhysicalRoot, ManagedTreeValidationMode.PreCreation);

            MigrationTargetInventory before = MigrationTargetInventoryInspector.Inspect(context.TargetPhysicalRoot, manifest, context.IsExactBootstrapRoot);
            if (before.HasUnknownEntries)
            {
                throw new InvalidDataException(
                    $"Unrecognized or foreign files in migration target '{context.TargetPhysicalRoot}': {string.Join(", ", before.UnknownEntries)}. Recovery aborted to protect data.");
            }

            // 1. Delete declared control artifacts (probe files and directories, staging files).
            // Auto-deletion is manifest-owned authority, not path authority.
            // - CapabilityProbeFile controls are written with fixed, known content (the literal
            //   bytes "create"/"replace"), so their exact expected hash/length is verified
            //   before deletion: a foreign file dropped at the same declared path is refused.
            // - ManifestPhaseStaging holds a serialized copy of the manifest itself, so its
            //   own hash cannot be embedded inside the manifest without circularity; identity
            //   (reparse-point rejection + strict-descendant-path binding), not content, is
            //   what is verified instead.
            // - A directory-kind control has no content to hash at all, so it is required to
            //   be a genuine, empty, non-reparse directory (StrictPathAuthority already
            //   refuses to treat a reparse point as StrictPathKind.Directory).
            foreach (MigrationControlArtifact control in manifest.ControlArtifacts)
            {
                string controlPath = MigrationManifestRepository.ResolveManifestArtifactPath(context.TargetPhysicalRoot, control.RelativePath);

                if (control.Kind == MigrationControlArtifactKind.CapabilityProbeFile)
                {
                    if (_fileOps.FileExists(controlPath))
                    {
                        if (control.ExpectedLength is null || control.ExpectedSha256Hex is null)
                        {
                            throw new InvalidDataException(
                                $"Declared control artifact '{control.RelativePath}' has no expected hash/length recorded. Recovery aborted to protect data.");
                        }

                        _verifiedDeleter.VerifyAndDelete(
                            context.TargetPhysicalRoot,
                            controlPath,
                            control.ExpectedLength.Value,
                            control.ExpectedSha256Hex);
                    }
                }
                else if (control.Kind == MigrationControlArtifactKind.ManifestPhaseStaging)
                {
                    // CRUU15-006: a manifest stage holds a copy of the manifest itself, so its
                    // hash cannot be embedded in the manifest without circularity. Identity
                    // checks alone proved only that the object is a regular file in the right
                    // place - not that this attempt created it. Ownership recorded at creation
                    // time is what authorizes the deletion; anything else is preserved and the
                    // recovery fails closed.
                    ArtifactCleanupOutcome outcome =
                        _fileOps.DeleteOwnedFileIfProven(context.TargetPhysicalRoot, controlPath);

                    if (outcome == ArtifactCleanupOutcome.PreservedUnproven)
                    {
                        throw new InvalidDataException(
                            $"A file occupies the declared manifest staging path '{control.RelativePath}', but nothing proves " +
                            "this application created it. It was preserved and recovery was aborted to protect data.");
                    }
                }
                else if (control.Kind == MigrationControlArtifactKind.CapabilityProbeDirectory)
                {
                    // CRUU15-006: removed through a single retained directory handle, so the
                    // object proven empty and the object removed are the same one. The kernel
                    // re-checks emptiness atomically when the disposition is applied, which is
                    // strictly stronger than the previous enumerate-then-delete-by-path pair.
                    _fileOps.DeleteDirectoryExact(context.TargetPhysicalRoot, controlPath);
                }
            }

            // 2. Delete declared payload temps. A temp being cleaned up during retry may
            // legitimately be partially written (the attempt was interrupted mid-copy), so its
            // content cannot be required to match the final artifact's hash/length - but that
            // does not make "whatever regular file currently sits at the declared temp path"
            // ours to destroy (CRUU15-006). Only the ownership recorded when the stage was
            // created authorizes deletion; anything else is preserved and recovery aborts.
            foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
            {
                string tempFullPath = MigrationManifestRepository.ResolveManifestArtifactPath(context.TargetPhysicalRoot, artifact.TempRelativePath);
                ArtifactCleanupOutcome outcome =
                    _fileOps.DeleteOwnedFileIfProven(context.TargetPhysicalRoot, tempFullPath);

                if (outcome == ArtifactCleanupOutcome.PreservedUnproven)
                {
                    throw new InvalidDataException(
                        $"A file occupies the declared payload staging path '{artifact.TempRelativePath}', but nothing proves " +
                        "this application created it. It was preserved and recovery was aborted to protect data.");
                }
            }

            // 3. Remove the attempt's published finals. CRUU16-005: content and location are
            // not ownership - a foreign object carrying identical bytes satisfies a hash check
            // just as well - so deletion requires the identity recorded when the object was
            // promoted. An unproven object is preserved and recovery fails closed.
            foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
            {
                string finalFullPath = MigrationManifestRepository.ResolveManifestArtifactPath(context.TargetPhysicalRoot, artifact.RelativePath);

                ArtifactCleanupOutcome finalOutcome =
                    _fileOps.DeleteOwnedFinalIfProven(context.TargetPhysicalRoot, finalFullPath);

                if (finalOutcome == ArtifactCleanupOutcome.PreservedUnproven)
                {
                    throw new InvalidDataException(
                        $"A file occupies the migrated payload path '{artifact.RelativePath}', but nothing proves " +
                        "this attempt created it. It was preserved and recovery was aborted to protect data.");
                }
            }

            // 4. Remove attempt-created directories before retiring marker (throwing if deletion fails)
            // CRUU15-006: removed through a retained directory handle rather than a fresh
            // pathname lookup, so a directory substituted at these names after the baseline
            // decision cannot be removed in place of the one this attempt created.
            string promptsDir = Path.Combine(context.TargetPhysicalRoot, "prompts");
            if (manifest.TargetBaseline == null || !manifest.TargetBaseline.PromptsDirectoryExistedBefore)
            {
                _fileOps.DeleteDirectoryExact(context.TargetPhysicalRoot, promptsDir);
            }

            string recoveryDir = Path.Combine(context.TargetPhysicalRoot, "recovery");
            if (manifest.TargetBaseline == null || !manifest.TargetBaseline.RecoveryDirectoryExistedBefore)
            {
                _fileOps.DeleteDirectoryExact(context.TargetPhysicalRoot, recoveryDir);
            }

            _fileOps.RetireOwnedArtifacts(context.TargetPhysicalRoot);

            // 5. Re-inspect inventory and assert all attempt-created directories and temps are gone
            MigrationTargetInventory after = MigrationTargetInventoryInspector.Inspect(context.TargetPhysicalRoot, manifest, context.IsExactBootstrapRoot);
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
            _manifestRepo.DeleteStrict(markerPath, manifest.AttemptId, manifest.Phase);

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

            // Reconcile the declared stage file before checking terminal inventory.
            // CRUU15-006: this used to be a bare path probe followed by a raw DeleteFile,
            // bypassing every ownership guarantee the rest of recovery establishes. A stage
            // this attempt cannot prove it created is now preserved and startup fails closed
            // rather than destroying it.
            string stagePath = Path.Combine(context.TargetPhysicalRoot, $".prompthelper-migration.stage-{manifest.AttemptId:N}.tmp");
            ArtifactCleanupOutcome stageOutcome =
                _fileOps.DeleteOwnedFileIfProven(context.TargetPhysicalRoot, stagePath);

            if (stageOutcome == ArtifactCleanupOutcome.PreservedUnproven)
            {
                throw new InvalidDataException(
                    $"A file occupies the migration staging path '{Path.GetFileName(stagePath)}' at '{context.TargetPhysicalRoot}', " +
                    "but nothing proves this application created it. It was preserved and startup was aborted to protect data.");
            }

            _fileOps.RetireOwnedArtifacts(context.TargetPhysicalRoot);

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
            MigrationTargetInventory inventory = MigrationTargetInventoryInspector.Inspect(context.TargetPhysicalRoot, manifest, context.IsExactBootstrapRoot);
            if (inventory.HasUnknownEntries)
            {
                throw new InvalidDataException(
                    $"Unrecognized or foreign files in configured target root '{context.TargetPhysicalRoot}': {string.Join(", ", inventory.UnknownEntries)}.");
            }

            // Settings already names this root, so the final payload is committed user data.
            // Release only its rollback authority before retiring the marker; the files are
            // preserved, while the append-only journal can be removed once no other claim is
            // live. A failure keeps the marker for a later fail-closed retry.
            _fileOps.RetireCommittedMigrationArtifacts(context.TargetPhysicalRoot);

            // Retire marker
            _manifestRepo.DeleteStrict(markerPath, manifest.AttemptId, manifest.Phase);
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
