using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PromptHelper.Services;

internal sealed class MigrationManifestRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

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

        string fileName = Path.GetFileName(full);
        if (fileName.StartsWith(".prompthelper", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith(".app.lock", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith(".settings.lock", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Migration artifact targets reserved namespace: '{relativePath}'.");
        }

        return full;
    }

    public MigrationAttemptManifest? TryRead(string markerPath)
    {
        if (string.IsNullOrWhiteSpace(markerPath) || !File.Exists(markerPath))
        {
            return null;
        }

        string json = File.ReadAllText(markerPath);
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
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(manifest, JsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        string tempPath = Path.Combine(
            dir ?? string.Empty,
            $".{Path.GetFileName(markerPath)}.{manifest.AttemptId:N}-{Guid.NewGuid():N}.tmp");

        using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            fs.Write(bytes);
            fs.Flush(flushToDisk: true);
        }

        if (File.Exists(markerPath))
        {
            File.Replace(tempPath, markerPath, null);
        }
        else
        {
            File.Move(tempPath, markerPath, overwrite: false);
        }
    }

    public void Delete(string markerPath)
    {
        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
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

        if (schemaVersion > MigrationAttemptManifest.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported migration manifest schema version: {schemaVersion}.");
        }

        if (schemaVersion != MigrationAttemptManifest.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Invalid migration manifest schema version: {schemaVersion}.");
        }
    }

    private static void ValidateManifestInvariants(MigrationAttemptManifest manifest, string path)
    {
        if (manifest.SchemaVersion != MigrationAttemptManifest.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported migration manifest schema version: {manifest.SchemaVersion}.");
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

        if (manifest.Artifacts is null)
        {
            throw new InvalidDataException("Migration manifest artifacts list cannot be null.");
        }

        var relativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
        {
            ResolveManifestArtifactPath(manifest.TargetPhysicalRoot, artifact.RelativePath);

            if (!relativePaths.Add(artifact.RelativePath))
            {
                throw new InvalidDataException($"Duplicate artifact path in migration manifest: '{artifact.RelativePath}'.");
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
