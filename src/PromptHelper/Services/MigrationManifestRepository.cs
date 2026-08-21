using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PromptHelper.Services;

internal sealed class ManifestWriteCleanupException : IOException
{
    public string TempPath { get; }
    public ManifestWriteCleanupException(string tempPath, Exception inner)
        : base($"Failed to clean manifest temp file at '{tempPath}': {inner.Message}", inner)
    {
        TempPath = tempPath;
    }
}

internal sealed class MigrationManifestRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
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

        // Canonical relative check: reject aliases like 'prompts\..\library.json' or '.\library.json'
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
        if (fileName.StartsWith(".prompthelper", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith(".app.lock", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith(".settings.lock", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "settings.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "settings.backup.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Migration artifact targets reserved namespace: '{relativePath}'.");
        }

        return full;
    }

    public MigrationAttemptManifest? TryRead(string markerPath)
    {
        if (string.IsNullOrWhiteSpace(markerPath) || !_fileOps.FileExists(markerPath))
        {
            return null;
        }

        string json;
        try
        {
            byte[] rawBytes = _fileOps.ReadAllBytes(markerPath);
            json = Encoding.UTF8.GetString(rawBytes);
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException($"Failed to read migration manifest from '{markerPath}': {ex.Message}", ex);
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

    public void WriteDurable(string markerPath, MigrationAttemptManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPath);
        ArgumentNullException.ThrowIfNull(manifest);

        ValidateManifestInvariants(manifest, markerPath);

        string? dir = Path.GetDirectoryName(markerPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(manifest, JsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        string tempPath = Path.Combine(
            dir ?? string.Empty,
            $".{Path.GetFileName(markerPath)}.{manifest.AttemptId:N}-{RandomNumberGenerator.GetHexString(8).ToLowerInvariant()}.tmp");

        bool promoted = false;
        try
        {
            using (Stream stream = _fileOps.CreateNew(tempPath))
            {
                stream.Write(bytes);
                _fileOps.FlushToDisk(stream);
            }

            if (_fileOps.FileExists(markerPath))
            {
                _fileOps.ReplaceWriteThrough(tempPath, markerPath);
            }
            else
            {
                _fileOps.MoveNoOverwriteWriteThrough(tempPath, markerPath);
            }

            promoted = true;
        }
        finally
        {
            if (!promoted && _fileOps.FileExists(tempPath))
            {
                try
                {
                    _fileOps.DeleteFile(tempPath);
                }
                catch (Exception cleanupEx)
                {
                    throw new ManifestWriteCleanupException(tempPath, cleanupEx);
                }
            }
        }
    }

    public void DeleteDurable(string markerPath)
    {
        Delete(markerPath);
    }

    public void Delete(string markerPath)
    {
        if (_fileOps.FileExists(markerPath))
        {
            _fileOps.DeleteFile(markerPath);
        }
    }

    private static void ValidateJsonStructure(string json, string path)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Root of migration manifest must be an object: '{path}'.");
        }

        int schemaVersionCount = 0;
        int schemaVersion = 0;

        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase))
            {
                schemaVersionCount++;
                if (property.Value.ValueKind != JsonValueKind.Number ||
                    !property.Value.TryGetInt32(out schemaVersion))
                {
                    throw new InvalidDataException($"Property 'schemaVersion' must be an integer in '{path}'.");
                }
            }
        }

        if (schemaVersionCount == 0)
        {
            throw new InvalidDataException($"Missing required 'schemaVersion' property in '{path}'.");
        }

        if (schemaVersionCount > 1)
        {
            throw new InvalidDataException($"Multiple 'schemaVersion' properties found in '{path}'.");
        }

        if (schemaVersion != MigrationAttemptManifest.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported migration manifest schema version: {schemaVersion}. Expected {MigrationAttemptManifest.CurrentSchemaVersion}.");
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

        var finalFullPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tempFullPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int primaryCount = 0;
        int safetyBackupCount = 0;

        foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
        {
            if (!Enum.IsDefined(artifact.Role))
            {
                throw new InvalidDataException($"Undefined artifact role: {artifact.Role} for '{artifact.RelativePath}'.");
            }

            string finalFull = ResolveManifestArtifactPath(manifest.TargetPhysicalRoot, artifact.RelativePath);
            if (!finalFullPaths.Add(finalFull))
            {
                throw new InvalidDataException($"Duplicate resolved artifact final path in migration manifest: '{artifact.RelativePath}'.");
            }

            if (string.IsNullOrWhiteSpace(artifact.TempRelativePath))
            {
                throw new InvalidDataException($"Artifact TempRelativePath cannot be empty: '{artifact.RelativePath}'.");
            }

            string tempFull = ResolveManifestArtifactPath(manifest.TargetPhysicalRoot, artifact.TempRelativePath);
            if (!tempFullPaths.Add(tempFull))
            {
                throw new InvalidDataException($"Duplicate resolved artifact temp path in migration manifest: '{artifact.TempRelativePath}'.");
            }

            if (PathIdentity.Equals(finalFull, tempFull))
            {
                throw new InvalidDataException($"Artifact final path and temp path must not be identical: '{artifact.RelativePath}'.");
            }

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
