using System;
using System.IO;
using System.Security.Cryptography;

namespace PromptHelper.Services;

internal sealed class MigrationReadyGate
{
    private readonly IAuthorityFileOps _authority;
    private readonly ManagedTreeTopologyValidator _tree;

    public MigrationReadyGate(
        IAuthorityFileOps? authority = null,
        ManagedTreeTopologyValidator? tree = null)
    {
        _authority = authority ?? new DefaultAuthorityFileOps();
        _tree = tree ?? new ManagedTreeTopologyValidator();
    }

    public void AssertReady(
        string physicalTargetRoot,
        MigrationAttemptManifest manifest,
        MigrationPayloadSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalTargetRoot);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(snapshot);

        _tree.ValidateManagedTree(physicalTargetRoot);

        foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
        {
            AssertFinalMatches(physicalTargetRoot, artifact);
            AssertTempMissing(physicalTargetRoot, artifact);
        }

        foreach (MigrationControlArtifact control in manifest.ControlArtifacts)
        {
            if (IsEphemeral(control))
            {
                AssertControlMissing(physicalTargetRoot, control);
            }
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

    private void AssertTempMissing(string targetRoot, MigrationManifestArtifact artifact)
    {
        string tempPath = MigrationManifestRepository.ResolveManifestArtifactPath(targetRoot, artifact.TempRelativePath);
        if (_authority.GetPresenceStrict(tempPath) != StrictFilePresence.Missing)
        {
            throw new InvalidDataException(
                $"Terminal invariant violated: temporary payload '{artifact.TempRelativePath}' still exists before Ready phase.");
        }
    }

    private void AssertControlMissing(string targetRoot, MigrationControlArtifact control)
    {
        string controlPath = MigrationManifestRepository.ResolveManifestArtifactPath(targetRoot, control.RelativePath);
        if (new StrictPathAuthority().Probe(controlPath).Kind != StrictPathKind.Missing)
        {
            throw new InvalidDataException(
                $"Terminal invariant violated: ephemeral control artifact '{control.RelativePath}' still exists before Ready phase.");
        }
    }

    private static bool IsEphemeral(MigrationControlArtifact control) =>
        control.Kind != MigrationControlArtifactKind.ManifestPhaseStaging;
}
