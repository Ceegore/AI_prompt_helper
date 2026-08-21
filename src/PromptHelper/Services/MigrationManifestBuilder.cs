using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PromptHelper.Services;

internal static class MigrationManifestBuilder
{
    public static MigrationAttemptManifest BuildCopying(
        string sourcePhysicalRoot,
        string targetPhysicalRoot,
        MigrationPayloadSnapshot snapshot,
        Guid attemptId,
        MigrationCapabilityProbePlan? probePlan = null,
        MigrationTargetBaseline? baseline = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePhysicalRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPhysicalRoot);
        ArgumentNullException.ThrowIfNull(snapshot);

        var artifacts = new List<MigrationManifestArtifact>();

        foreach (MigrationPayloadFile file in snapshot.Files)
        {
            string directory = Path.GetDirectoryName(file.RelativePath) ?? string.Empty;
            string finalName = Path.GetFileName(file.RelativePath);
            string nonce = RandomNumberGenerator.GetHexString(32).ToLowerInvariant();

            string tempName = $".{finalName}.migration-{attemptId:N}-{nonce}.tmp";
            string tempRelative = string.IsNullOrEmpty(directory)
                ? tempName
                : Path.Combine(directory, tempName);

            artifacts.Add(new MigrationManifestArtifact
            {
                RelativePath = file.RelativePath,
                TempRelativePath = tempRelative,
                Sha256Hex = Convert.ToHexStringLower(file.Sha256),
                Length = file.Length,
                Role = file.Role
            });
        }

        var controlArtifacts = new List<MigrationControlArtifact>
        {
            // Manifest phase transition stage
            new MigrationControlArtifact
            {
                RelativePath = $".prompthelper-migration.stage-{attemptId:N}.tmp",
                Kind = MigrationControlArtifactKind.ManifestPhaseStaging
            }
        };

        if (probePlan != null)
        {
            byte[] createBytes = StrictUtf8Text.Encode("create");
            byte[] replaceBytes = StrictUtf8Text.Encode("replace");

            controlArtifacts.Add(new MigrationControlArtifact
            {
                RelativePath = probePlan.RootProbe.CurrentRelativePath,
                Kind = MigrationControlArtifactKind.CapabilityProbeFile,
                ExpectedLength = createBytes.LongLength,
                ExpectedSha256Hex = Convert.ToHexStringLower(SHA256.HashData(createBytes))
            });
            controlArtifacts.Add(new MigrationControlArtifact
            {
                RelativePath = probePlan.RootProbe.ReplacementRelativePath,
                Kind = MigrationControlArtifactKind.CapabilityProbeFile,
                ExpectedLength = replaceBytes.LongLength,
                ExpectedSha256Hex = Convert.ToHexStringLower(SHA256.HashData(replaceBytes))
            });

            if (probePlan.PromptsProbe != null)
            {
                controlArtifacts.Add(new MigrationControlArtifact
                {
                    RelativePath = probePlan.PromptsProbe.CurrentRelativePath,
                    Kind = MigrationControlArtifactKind.CapabilityProbeFile,
                    ExpectedLength = createBytes.LongLength,
                    ExpectedSha256Hex = Convert.ToHexStringLower(SHA256.HashData(createBytes))
                });
                controlArtifacts.Add(new MigrationControlArtifact
                {
                    RelativePath = probePlan.PromptsProbe.ReplacementRelativePath,
                    Kind = MigrationControlArtifactKind.CapabilityProbeFile,
                    ExpectedLength = replaceBytes.LongLength,
                    ExpectedSha256Hex = Convert.ToHexStringLower(SHA256.HashData(replaceBytes))
                });
            }
        }

        MigrationManifestArtifact primaryArtifact = artifacts.Single(x => x.Role == MigrationPayloadRole.PrimaryMetadata);

        MigrationTargetBaseline effectiveBaseline = baseline ?? new MigrationTargetBaseline
        {
            TargetRootExistedBefore = new StrictPathAuthority().Probe(targetPhysicalRoot).Kind == StrictPathKind.Directory,
            PromptsDirectoryExistedBefore = new StrictPathAuthority().Probe(Path.Combine(targetPhysicalRoot, "prompts")).Kind == StrictPathKind.Directory,
            RecoveryDirectoryExistedBefore = new StrictPathAuthority().Probe(Path.Combine(targetPhysicalRoot, "recovery")).Kind == StrictPathKind.Directory
        };

        return new MigrationAttemptManifest
        {
            SchemaVersion = MigrationAttemptManifest.CurrentSchemaVersion,
            AttemptId = attemptId,
            SourcePhysicalRoot = sourcePhysicalRoot,
            TargetPhysicalRoot = targetPhysicalRoot,
            SourceLibrarySha256Hex = primaryArtifact.Sha256Hex,
            SourcePayloadFingerprintSha256Hex = MigrationPayloadFingerprint.Compute(snapshot.Files),
            Phase = MigrationManifestPhase.Copying,
            Artifacts = artifacts,
            ControlArtifacts = controlArtifacts,
            TargetBaseline = effectiveBaseline
        };
    }
}
