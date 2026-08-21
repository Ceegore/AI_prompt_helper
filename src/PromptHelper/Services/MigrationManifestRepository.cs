using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PromptHelper.Services;

public sealed class ManifestWriteCleanupException : IOException
{
    public string MarkerPath { get; }
    public string TempPath { get; }
    public Exception OriginalFailure { get; }
    public Exception CleanupFailure { get; }

    public ManifestWriteCleanupException(
        string markerPath,
        string tempPath,
        Exception originalFailure,
        Exception cleanupFailure)
        : base(
            $"Migration manifest write failed for '{markerPath}', and staging cleanup also failed for '{tempPath}': {originalFailure.Message} | Cleanup: {cleanupFailure.Message}",
            originalFailure)
    {
        MarkerPath = markerPath;
        TempPath = tempPath;
        OriginalFailure = originalFailure;
        CleanupFailure = cleanupFailure;
    }
}

public sealed class MigrationManifestRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IMigrationManifestFileOps _fileOps;

    public MigrationManifestRepository(IMigrationManifestFileOps? fileOps = null)
    {
        _fileOps = fileOps ?? new DefaultMigrationManifestFileOps();
    }

    public static string ResolveManifestArtifactPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidDataException("Migration artifact path cannot be empty.");
        }

        if (Path.IsPathFullyQualified(relativePath) || relativePath.StartsWith('/') || relativePath.StartsWith('\\'))
        {
            throw new InvalidDataException("Migration artifact path must be relative.");
        }

        string normalizedRoot = PathIdentity.NormalizeForComparison(root);
        string full = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));

        if (!PathIdentity.IsStrictDescendant(full, normalizedRoot))
        {
            throw new InvalidDataException("Migration artifact path escapes the target root.");
        }

        string canonicalRelative = Path.GetRelativePath(normalizedRoot, full);
        if (!string.Equals(relativePath.Replace('/', '\\'), canonicalRelative, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Migration artifact path '{relativePath}' is not canonical.");
        }

        string fileName = Path.GetFileName(full);
        if (fileName.StartsWith(".app.lock", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith(".settings.lock", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "settings.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "settings.backup.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Migration artifact targets reserved namespace: '{relativePath}'.");
        }

        return full;
    }

    public MigrationAttemptManifest? TryRead(string markerPath) => TryReadStrict(markerPath);

    public MigrationAttemptManifest? TryReadStrict(string markerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPath);

        byte[] rawBytes;
        try
        {
            rawBytes = _fileOps.ReadAllBytes(markerPath);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Failed to read migration manifest from '{markerPath}': {ex.Message}", ex);
        }

        string json;
        try
        {
            var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            json = utf8Strict.GetString(rawBytes);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Migration manifest is not valid UTF-8: '{markerPath}'.", ex);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException($"Migration manifest file is empty: '{markerPath}'.");
        }

        ValidateJsonStructure(json, markerPath);

        MigrationAttemptManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<MigrationAttemptManifest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Failed to deserialize migration manifest from '{markerPath}': {ex.Message}", ex);
        }

        if (manifest is null)
        {
            throw new InvalidDataException($"Migration manifest deserialized to null from '{markerPath}'.");
        }

        ValidateManifestInvariants(manifest, markerPath);
        return manifest;
    }

    public void CreateInitialCopyingManifestDurable(string markerPath, MigrationAttemptManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPath);
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.Phase != MigrationManifestPhase.Copying)
        {
            throw new InvalidDataException("Initial migration manifest must be in Copying phase.");
        }

        ValidateManifestInvariants(manifest, markerPath);

        string? dir = Path.GetDirectoryName(markerPath);
        if (!string.IsNullOrEmpty(dir) && new StrictPathAuthority().Probe(dir).Kind != StrictPathKind.Directory)
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(manifest, JsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        using (Stream stream = _fileOps.CreateNew(markerPath))
        {
            stream.Write(bytes, 0, bytes.Length);
            _fileOps.FlushToDisk(stream);
        }
    }

    public void WriteReadyManifestDurable(string markerPath, MigrationAttemptManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPath);
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.Phase != MigrationManifestPhase.ReadyToCommit)
        {
            throw new InvalidDataException("WriteReadyManifestDurable requires ReadyToCommit phase.");
        }

        ValidateManifestInvariants(manifest, markerPath);

        string? dir = Path.GetDirectoryName(markerPath) ?? string.Empty;
        string stagePath = Path.Combine(dir, $".prompthelper-migration.stage-{manifest.AttemptId:N}.tmp");

        string json = JsonSerializer.Serialize(manifest, JsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        bool promoted = false;
        Exception? primaryFailure = null;

        try
        {
            using (Stream stream = _fileOps.CreateNew(stagePath))
            {
                stream.Write(bytes, 0, bytes.Length);
                _fileOps.FlushToDisk(stream);
            }

            if (_fileOps.FileExists(markerPath))
            {
                _fileOps.ReplaceWriteThrough(stagePath, markerPath);
            }
            else
            {
                _fileOps.MoveNoOverwriteWriteThrough(stagePath, markerPath);
            }

            promoted = true;
        }
        catch (Exception ex)
        {
            primaryFailure = ex;
            throw;
        }
        finally
        {
            if (!promoted && _fileOps.FileExists(stagePath))
            {
                try
                {
                    _fileOps.DeleteFile(stagePath);
                }
                catch (Exception cleanupEx)
                {
                    throw new ManifestWriteCleanupException(
                        markerPath,
                        stagePath,
                        primaryFailure ?? cleanupEx,
                        cleanupEx);
                }
            }
        }
    }

    public void WriteDurable(string markerPath, MigrationAttemptManifest manifest)
    {
        if (manifest.Phase == MigrationManifestPhase.Copying)
        {
            if (_fileOps.FileExists(markerPath))
            {
                WriteReadyManifestDurable(markerPath, manifest);
            }
            else
            {
                CreateInitialCopyingManifestDurable(markerPath, manifest);
            }
        }
        else
        {
            WriteReadyManifestDurable(markerPath, manifest);
        }
    }

    public void DeleteStrict(string markerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPath);
        if (_fileOps.FileExists(markerPath))
        {
            _fileOps.DeleteFile(markerPath);
        }
    }

    public void DeleteDurable(string markerPath) => DeleteStrict(markerPath);

    private static void ValidateJsonStructure(string json, string path)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            StrictJsonObjectAuthority.ValidateExactObject(
                root,
                allowedMembers: [
                    "schemaVersion",
                    "attemptId",
                    "sourcePhysicalRoot",
                    "targetPhysicalRoot",
                    "sourceLibrarySha256Hex",
                    "phase",
                    "artifacts",
                    "controlArtifacts",
                    "targetBaseline"
                ],
                requiredMembers: [
                    "schemaVersion",
                    "attemptId",
                    "sourcePhysicalRoot",
                    "targetPhysicalRoot",
                    "sourceLibrarySha256Hex",
                    "phase",
                    "artifacts",
                    "controlArtifacts"
                ],
                description: $"migration manifest root '{path}'");

            if (!StrictJsonObjectAuthority.TryGetPropertyIgnoreCase(root, "schemaVersion", out JsonProperty schemaProp) ||
                schemaProp.Value.ValueKind != JsonValueKind.Number ||
                !schemaProp.Value.TryGetInt32(out int schemaVersion))
            {
                throw new InvalidDataException($"Property 'schemaVersion' must be an integer in '{path}'.");
            }

            if (schemaVersion != MigrationAttemptManifest.CurrentSchemaVersion)
            {
                throw new InvalidDataException($"Unsupported migration manifest schema version: {schemaVersion}. Expected {MigrationAttemptManifest.CurrentSchemaVersion}.");
            }

            if (StrictJsonObjectAuthority.TryGetPropertyIgnoreCase(root, "artifacts", out JsonProperty artifactsProp))
            {
                if (artifactsProp.Value.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException($"Property 'artifacts' must be an array in '{path}'.");
                }

                int index = 0;
                foreach (JsonElement artifact in artifactsProp.Value.EnumerateArray())
                {
                    StrictJsonObjectAuthority.ValidateExactObject(
                        artifact,
                        allowedMembers: ["relativePath", "tempRelativePath", "sha256Hex", "length", "role"],
                        requiredMembers: ["relativePath", "tempRelativePath", "sha256Hex", "length", "role"],
                        description: $"manifest.artifacts[{index}] in '{path}'");
                    index++;
                }
            }

            if (StrictJsonObjectAuthority.TryGetPropertyIgnoreCase(root, "controlArtifacts", out JsonProperty controlsProp))
            {
                if (controlsProp.Value.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException($"Property 'controlArtifacts' must be an array in '{path}'.");
                }

                int index = 0;
                foreach (JsonElement control in controlsProp.Value.EnumerateArray())
                {
                    StrictJsonObjectAuthority.ValidateExactObject(
                        control,
                        allowedMembers: ["relativePath", "kind"],
                        requiredMembers: ["relativePath", "kind"],
                        description: $"manifest.controlArtifacts[{index}] in '{path}'");
                    index++;
                }
            }

            if (StrictJsonObjectAuthority.TryGetPropertyIgnoreCase(root, "targetBaseline", out JsonProperty baselineProp) &&
                baselineProp.Value.ValueKind == JsonValueKind.Object)
            {
                StrictJsonObjectAuthority.ValidateExactObject(
                    baselineProp.Value,
                    allowedMembers: ["targetRootExistedBefore", "promptsDirectoryExistedBefore", "recoveryDirectoryExistedBefore"],
                    requiredMembers: ["targetRootExistedBefore", "promptsDirectoryExistedBefore", "recoveryDirectoryExistedBefore"],
                    description: $"manifest.targetBaseline in '{path}'");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Failed to validate JSON structure in '{path}': {ex.Message}", ex);
        }
    }

    private static void ValidateManifestInvariants(MigrationAttemptManifest manifest, string path)
    {
        if (manifest.SchemaVersion != MigrationAttemptManifest.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported migration manifest schema version: {manifest.SchemaVersion}. Expected {MigrationAttemptManifest.CurrentSchemaVersion}.");
        }

        if (manifest.AttemptId == Guid.Empty)
        {
            throw new InvalidDataException($"Migration manifest AttemptId cannot be empty: '{path}'.");
        }

        if (string.IsNullOrWhiteSpace(manifest.SourcePhysicalRoot) ||
            !Path.IsPathFullyQualified(manifest.SourcePhysicalRoot))
        {
            throw new InvalidDataException($"Migration manifest SourcePhysicalRoot must be a fully qualified path: '{manifest.SourcePhysicalRoot}'.");
        }

        if (string.IsNullOrWhiteSpace(manifest.TargetPhysicalRoot) ||
            !Path.IsPathFullyQualified(manifest.TargetPhysicalRoot))
        {
            throw new InvalidDataException($"Migration manifest TargetPhysicalRoot must be a fully qualified path: '{manifest.TargetPhysicalRoot}'.");
        }

        if (PathIdentity.Equals(manifest.SourcePhysicalRoot, manifest.TargetPhysicalRoot))
        {
            throw new InvalidDataException("Migration manifest source and target roots must not be identical.");
        }

        if (string.IsNullOrWhiteSpace(manifest.SourceLibrarySha256Hex) ||
            manifest.SourceLibrarySha256Hex.Length != 64 ||
            !IsHex(manifest.SourceLibrarySha256Hex))
        {
            throw new InvalidDataException($"SourceLibrarySha256Hex must be a 64-character hexadecimal string: '{manifest.SourceLibrarySha256Hex}'.");
        }

        if (!Enum.IsDefined(manifest.Phase))
        {
            throw new InvalidDataException($"Undefined migration manifest phase: {manifest.Phase}.");
        }

        if (manifest.Artifacts is null || manifest.Artifacts.Count == 0)
        {
            throw new InvalidDataException("Migration manifest artifacts list cannot be null or empty.");
        }

        if (manifest.ControlArtifacts is null)
        {
            throw new InvalidDataException("Migration manifest controlArtifacts list cannot be null.");
        }

        var allOwnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int primaryCount = 0;
        int safetyBackupCount = 0;

        foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
        {
            if (!Enum.IsDefined(artifact.Role))
            {
                throw new InvalidDataException($"Undefined artifact role: {artifact.Role} for '{artifact.RelativePath}'.");
            }

            string finalFull = ResolveManifestArtifactPath(manifest.TargetPhysicalRoot, artifact.RelativePath);
            if (!allOwnedPaths.Add(finalFull))
            {
                throw new InvalidDataException($"Duplicate owned migration path '{artifact.RelativePath}'.");
            }

            if (string.IsNullOrWhiteSpace(artifact.TempRelativePath))
            {
                throw new InvalidDataException($"Artifact TempRelativePath cannot be empty: '{artifact.RelativePath}'.");
            }

            string tempFull = ResolveManifestArtifactPath(manifest.TargetPhysicalRoot, artifact.TempRelativePath);
            if (!allOwnedPaths.Add(tempFull))
            {
                throw new InvalidDataException($"Migration temp/final path collision '{artifact.TempRelativePath}'.");
            }

            ValidateTempPath(manifest.AttemptId, artifact.RelativePath, artifact.TempRelativePath);

            if (artifact.Length < 0)
            {
                throw new InvalidDataException($"Artifact length cannot be negative: '{artifact.RelativePath}'.");
            }

            if (string.IsNullOrWhiteSpace(artifact.Sha256Hex) ||
                artifact.Sha256Hex.Length != 64 ||
                !IsHex(artifact.Sha256Hex))
            {
                throw new InvalidDataException($"Artifact SHA-256 hash must be 64 hexadecimal characters: '{artifact.RelativePath}'.");
            }

            switch (artifact.Role)
            {
                case MigrationPayloadRole.PrimaryMetadata:
                    primaryCount++;
                    if (!string.Equals(artifact.RelativePath, "library.json", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"PrimaryMetadata relative path must be 'library.json', found '{artifact.RelativePath}'.");
                    }
                    if (!string.Equals(artifact.Sha256Hex, manifest.SourceLibrarySha256Hex, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("PrimaryMetadata SHA-256 hash must match manifest SourceLibrarySha256Hex.");
                    }
                    break;

                case MigrationPayloadRole.SafetyBackup:
                    safetyBackupCount++;
                    if (!string.Equals(artifact.RelativePath, "library.backup.json", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"SafetyBackup relative path must be 'library.backup.json', found '{artifact.RelativePath}'.");
                    }
                    break;

                case MigrationPayloadRole.PromptBody:
                case MigrationPayloadRole.OrphanPromptBody:
                    if (!artifact.RelativePath.StartsWith("prompts" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                        !artifact.RelativePath.StartsWith("prompts/", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"Prompt body must reside under 'prompts/', found '{artifact.RelativePath}'.");
                    }
                    break;

                case MigrationPayloadRole.RecoveryArtifact:
                    if (!artifact.RelativePath.StartsWith("recovery" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                        !artifact.RelativePath.StartsWith("recovery/", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"Recovery artifact must reside under 'recovery/', found '{artifact.RelativePath}'.");
                    }
                    break;
            }
        }

        if (primaryCount != 1)
        {
            throw new InvalidDataException($"Migration manifest must contain exactly one PrimaryMetadata artifact, found {primaryCount}.");
        }

        if (safetyBackupCount > 1)
        {
            throw new InvalidDataException($"Migration manifest may contain at most one SafetyBackup artifact, found {safetyBackupCount}.");
        }

        foreach (MigrationControlArtifact control in manifest.ControlArtifacts)
        {
            if (!Enum.IsDefined(control.Kind))
            {
                throw new InvalidDataException($"Undefined control kind: {control.Kind} for '{control.RelativePath}'.");
            }

            string controlFull = ResolveManifestArtifactPath(manifest.TargetPhysicalRoot, control.RelativePath);
            if (!allOwnedPaths.Add(controlFull))
            {
                throw new InvalidDataException($"Migration control path collision '{control.RelativePath}'.");
            }

            ValidateControlGrammar(manifest.AttemptId, control);
        }
    }

    private static void ValidateTempPath(Guid attemptId, string finalRelative, string tempRelative)
    {
        string finalDir = Path.GetDirectoryName(finalRelative) ?? string.Empty;
        string tempDir = Path.GetDirectoryName(tempRelative) ?? string.Empty;

        if (!string.Equals(finalDir.Replace('/', '\\'), tempDir.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Migration temp must be in the same directory as its final artifact: '{tempRelative}'.");
        }

        string finalName = Path.GetFileName(finalRelative);
        string tempName = Path.GetFileName(tempRelative);
        string prefix = $".{finalName}.migration-{attemptId:N}-";
        const string suffix = ".tmp";

        if (!tempName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !tempName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Invalid migration temp name '{tempRelative}'.");
        }

        string nonce = tempName.Substring(prefix.Length, tempName.Length - prefix.Length - suffix.Length);
        if (nonce.Length != 32 || !IsHex(nonce))
        {
            throw new InvalidDataException($"Migration temp nonce must contain exactly 32 hexadecimal characters: '{tempRelative}'.");
        }
    }

    private static void ValidateControlGrammar(Guid attemptId, MigrationControlArtifact control)
    {
        string rel = control.RelativePath.Replace('/', '\\').TrimStart('\\');

        switch (control.Kind)
        {
            case MigrationControlArtifactKind.ManifestPhaseStaging:
                if (!string.Equals(rel, $".prompthelper-migration.stage-{attemptId:N}.tmp", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Invalid manifest phase stage name: '{control.RelativePath}'.");
                }
                break;

            case MigrationControlArtifactKind.CapabilityProbeDirectory:
                string rootProbeDir = $".prompthelper-write-probe-{attemptId:N}-root";
                string promptsProbeDir = Path.Combine("prompts", $".prompthelper-write-probe-{attemptId:N}-prompts");
                if (!string.Equals(rel, rootProbeDir, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(rel, promptsProbeDir, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Invalid capability probe directory name: '{control.RelativePath}'.");
                }
                break;

            case MigrationControlArtifactKind.CapabilityProbeFile:
                string fileName = Path.GetFileName(rel);
                if (!string.Equals(fileName, "probe-current.txt", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(fileName, "probe-replacement.tmp", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Invalid capability probe file name: '{control.RelativePath}'.");
                }
                break;
        }
    }

    private static bool IsHex(string value)
    {
        foreach (char c in value)
        {
            if (!((c >= '0' && c <= '9') ||
                  (c >= 'a' && c <= 'f') ||
                  (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }
        return true;
    }
}
