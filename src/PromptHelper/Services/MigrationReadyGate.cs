using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PromptHelper.Services;

internal sealed class MigrationReadyGate
{
    private readonly IAuthorityFileOps _authority;
    private readonly ManagedTreeTopologyValidator _tree;
    private readonly DataFolderMigrationService _migrationService;

    public MigrationReadyGate(
        IAuthorityFileOps? authority = null,
        ManagedTreeTopologyValidator? tree = null,
        DataFolderMigrationService? migrationService = null)
    {
        _authority = authority ?? new DefaultAuthorityFileOps();
        _tree = tree ?? new ManagedTreeTopologyValidator();
        _migrationService = migrationService ?? new DataFolderMigrationService();
    }

    public void AssertReady(
        string sourcePhysicalRoot,
        string physicalTargetRoot,
        MigrationAttemptManifest manifest,
        MigrationPayloadSnapshot originalSnapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePhysicalRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalTargetRoot);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(originalSnapshot);

        _tree.ValidateManagedTree(physicalTargetRoot, ManagedTreeValidationMode.PreCreation);

        // Recheck source payload stability before Ready commit
        MigrationPayloadSnapshot freshSnapshot = _migrationService.CaptureSourcePayloadSnapshot(sourcePhysicalRoot);
        string freshFingerprint = MigrationPayloadFingerprint.Compute(freshSnapshot.Files);

        if (!string.Equals(freshFingerprint, manifest.SourcePayloadFingerprintSha256Hex, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Source library changed before ReadyToCommit.");
        }

        // Terminal target inventory inspection
        MigrationTargetInventory inventory = MigrationTargetInventoryInspector.Inspect(physicalTargetRoot, manifest);

        if (inventory.HasUnknownEntries)
        {
            throw new InvalidDataException(
                $"Terminal invariant violated: unknown entry '{inventory.UnknownEntries.First()}' found in target before Ready phase.");
        }

        if (inventory.PayloadTemps.Count > 0)
        {
            throw new InvalidDataException(
                $"Terminal invariant violated: temporary payload '{inventory.PayloadTemps.First()}' still exists before Ready phase.");
        }

        string markerPath = PathIdentity.NormalizeForComparison(
            Path.Combine(physicalTargetRoot, ".prompthelper-migration.json"));
        string appLockPath = PathIdentity.NormalizeForComparison(
            Path.Combine(physicalTargetRoot, ".app.lock"));

        foreach (string control in inventory.DeclaredControls)
        {
            if (!PathIdentity.Equals(control, markerPath) && !PathIdentity.Equals(control, appLockPath))
            {
                throw new InvalidDataException(
                    $"Terminal invariant violated: ephemeral control artifact '{control}' still exists before Ready phase.");
            }
        }

        foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
        {
            AssertFinalMatches(physicalTargetRoot, artifact);
        }
    }

    private void AssertFinalMatches(string targetRoot, MigrationManifestArtifact artifact)
    {
        string finalPath = MigrationManifestRepository.ResolveManifestArtifactPath(targetRoot, artifact.RelativePath);
        if (_authority.GetPresenceStrict(finalPath) != StrictFilePresence.Present)
        {
            throw new InvalidDataException(
                $"Terminal invariant violated: final artifact '{artifact.RelativePath}' is missing before Ready phase.");
        }

        byte[]? bytes = _authority.ReadOptionalBytesStrict(finalPath);
        if (bytes is null)
        {
            throw new InvalidDataException(
                $"Terminal invariant violated: final artifact '{artifact.RelativePath}' is unreadable before Ready phase.");
        }

        if (bytes.Length != artifact.Length)
        {
            throw new InvalidDataException(
                $"Terminal invariant violated: final artifact '{artifact.RelativePath}' length mismatch before Ready phase. Expected {artifact.Length}, found {bytes.Length}.");
        }

        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(hash, artifact.Sha256Hex, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Terminal invariant violated: final artifact '{artifact.RelativePath}' hash mismatch before Ready phase.");
        }
    }
}
