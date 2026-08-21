using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using PromptHelper.Infrastructure;
using PromptHelper.Models;

namespace PromptHelper.Services;

public abstract record LibraryMetadataCompatibility
{
    public sealed record Current : LibraryMetadataCompatibility;
    public sealed record Future(int Version) : LibraryMetadataCompatibility;
    public sealed record Corrupt(Exception Error) : LibraryMetadataCompatibility;
}

public sealed class LibraryRepository
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        RespectNullableAnnotations = true
    };

    private abstract record MetadataFileState
    {
        public sealed record Missing : MetadataFileState;
        public sealed record Current : MetadataFileState;
        public sealed record Future(int Version) : MetadataFileState;
        public sealed record Corrupt(Exception Error) : MetadataFileState;
        public sealed record Unreadable(Exception Error) : MetadataFileState;
    }

    private readonly AppPaths _paths;
    private readonly IDurableAtomicFileWriter _durableWriter;

    internal LibraryRepository(AppPaths paths, IDurableAtomicFileWriter durableWriter)
    {
        _paths = paths;
        _durableWriter = durableWriter;
    }

    public LibraryRepository(AppPaths paths)
        : this(paths, new WindowsDurableAtomicFileWriter())
    {
    }

    internal AppPaths Paths => _paths;
    internal IDurableAtomicFileWriter DurableWriter => _durableWriter;

    public LibraryPrimarySnapshot CapturePrimarySnapshot()
    {
        byte[] raw = File.ReadAllBytes(_paths.LibraryPath);
        string json = StrictUtf8Text.Decode(raw, "primary library metadata");
        LibraryDocument parsed = InspectAndDeserialize(json);
        LibraryValidator.Validate(parsed);
        byte[] canonical = SerializeCanonicalBytes(parsed);

        return new LibraryPrimarySnapshot(
            RawBytes: raw,
            Document: parsed,
            CanonicalBytes: canonical,
            RawSha256Hex: Convert.ToHexStringLower(SHA256.HashData(raw)),
            CanonicalSha256Hex: Convert.ToHexStringLower(SHA256.HashData(canonical)));
    }

    public CanonicalLibraryPackage CreateCanonicalPackage(LibraryDocument document)
        => CanonicalLibraryPackage.Create(document);

    public byte[] SerializeCanonicalBytes(LibraryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        LibraryValidator.Validate(document);
        string json = JsonSerializer.Serialize(document, JsonOptions);
        return StrictUtf8Text.Encode(json);
    }

    public CommitResult Commit(CanonicalLibraryPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        MetadataFileState primaryState = ReadMetadataFileState(_paths.LibraryPath);

        if (primaryState is MetadataFileState.Future futurePrimary)
        {
            throw new UnsupportedLibrarySchemaException(futurePrimary.Version);
        }

        if (primaryState is MetadataFileState.Unreadable unreadablePrimary)
        {
            throw new IOException(
                $"library.json cannot be safely replaced because it cannot be read: {unreadablePrimary.Error.Message}",
                unreadablePrimary.Error);
        }

        _durableWriter.ReplaceDurable(
            _paths.LibraryPath,
            package.CanonicalBytes,
            DurableFileClass.LibraryMetadata);

        return SynchronizeBackup(package);
    }

    public LibraryDocument ReadPrimary()
    {
        string json = StrictUtf8Text.ReadAllText(_paths.LibraryPath, "primary library metadata");
        return InspectAndDeserialize(json);
    }

    public LibraryDocument ReadBackup()
    {
        string json = StrictUtf8Text.ReadAllText(_paths.LibraryBackupPath, "backup library metadata");
        return InspectAndDeserialize(json);
    }

    public CommitResult Commit(LibraryDocument document)
    {
        CanonicalLibraryPackage package = CreateCanonicalPackage(document);
        return Commit(package);
    }

    public CommitResult SynchronizeBackup(CanonicalLibraryPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        MetadataFileState state = ReadMetadataFileState(_paths.LibraryBackupPath);

        if (state is MetadataFileState.Future future)
        {
            return new CommitResult(
                false,
                $"The library was saved, but library.backup.json uses newer schema version " +
                $"{future.Version}. The newer backup was preserved and was not overwritten.");
        }

        if (state is MetadataFileState.Unreadable unreadable)
        {
            return new CommitResult(
                false,
                "The library was saved, but its safety backup could not be synchronized: " +
                unreadable.Error.Message);
        }

        try
        {
            _durableWriter.ReplaceDurable(
                _paths.LibraryBackupPath,
                package.CanonicalBytes,
                DurableFileClass.LibraryMetadata);
            return new CommitResult(true, null);
        }
        catch (Exception ex)
        {
            return new CommitResult(
                false,
                "The library was saved, but its safety backup could not be synchronized (safety backup could not be updated). " +
                $"Current data remains stored in library.json. {ex.Message}");
        }
    }

    public CommitResult SynchronizeBackup(LibraryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CanonicalLibraryPackage package = CreateCanonicalPackage(document);
        return SynchronizeBackup(package);
    }

    internal CommitResult SynchronizeBackup(HealthyLibraryPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        CanonicalLibraryPackage canonicalPackage = CreateCanonicalPackage(package.Document);
        return SynchronizeBackup(canonicalPackage);
    }

    private static MetadataFileState ReadMetadataFileState(string path)
    {
        string raw;
        try
        {
            raw = StrictUtf8Text.ReadAllText(path, $"library file '{path}'");
        }
        catch (FileNotFoundException)
        {
            return new MetadataFileState.Missing();
        }
        catch (DirectoryNotFoundException)
        {
            return new MetadataFileState.Missing();
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or SecurityException or InvalidDataException)
        {
            return new MetadataFileState.Unreadable(ex);
        }

        try
        {
            InspectAndDeserialize(raw);
            return new MetadataFileState.Current();
        }
        catch (UnsupportedLibrarySchemaException ex)
        {
            return new MetadataFileState.Future(ex.SchemaVersion);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return new MetadataFileState.Corrupt(ex);
        }
    }

    public void TryCreateCorruptRecoveryCopy(string corruptPrimaryContent)
    {
        try
        {
            Directory.CreateDirectory(_paths.RecoveryDirectory);
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
            string recoveryFile = Path.Combine(
                _paths.RecoveryDirectory,
                $"library.corrupt-{timestamp}-{Guid.NewGuid():N}.json");

            byte[] bytes = StrictUtf8Text.Encode(corruptPrimaryContent);
            _durableWriter.ReplaceDurable(recoveryFile, bytes, DurableFileClass.RecoveryArtifact);
        }
        catch
        {
            // Explicit best-effort only
        }
    }

    public void TryCreateIncompleteRecoveryCopy(LibraryDocument document)
    {
        try
        {
            Directory.CreateDirectory(_paths.RecoveryDirectory);
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
            string recoveryFile = Path.Combine(
                _paths.RecoveryDirectory,
                $"library.incomplete-{timestamp}-{Guid.NewGuid():N}.json");

            byte[] bytes = SerializeCanonicalBytes(document);
            _durableWriter.ReplaceDurable(recoveryFile, bytes, DurableFileClass.RecoveryArtifact);
        }
        catch
        {
            // Explicit best-effort only
        }
    }

    public static LibraryMetadataCompatibility InspectCompatibility(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return new LibraryMetadataCompatibility.Corrupt(new InvalidDataException("Library JSON is empty or whitespace."));
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new LibraryMetadataCompatibility.Corrupt(new InvalidDataException("Root of library JSON must be an object."));
            }

            int schemaPropCount = 0;
            int foundSchemaVersion = 0;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase))
                {
                    schemaPropCount++;
                    if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetInt32(out foundSchemaVersion))
                    {
                        return new LibraryMetadataCompatibility.Corrupt(new InvalidDataException("Property 'schemaVersion' must be an integer."));
                    }
                }
            }

            if (schemaPropCount == 0)
            {
                return new LibraryMetadataCompatibility.Corrupt(new InvalidDataException("Missing required 'schemaVersion' property."));
            }

            if (schemaPropCount > 1)
            {
                return new LibraryMetadataCompatibility.Corrupt(new InvalidDataException("Multiple conflicting 'schemaVersion' properties found."));
            }

            if (foundSchemaVersion > LibraryDocument.CurrentSchemaVersion)
            {
                return new LibraryMetadataCompatibility.Future(foundSchemaVersion);
            }

            if (foundSchemaVersion != LibraryDocument.CurrentSchemaVersion)
            {
                return new LibraryMetadataCompatibility.Corrupt(new InvalidDataException($"Unsupported schema version: {foundSchemaVersion}."));
            }

            StrictJsonObjectAuthority.ValidateExactObject(
                doc.RootElement,
                allowedMembers: ["schemaVersion", "categories", "prompts"],
                requiredMembers: ["schemaVersion", "categories", "prompts"],
                description: "library root");

            ValidateLibraryJsonStructure(doc.RootElement);

            LibraryDocument? document = JsonSerializer.Deserialize<LibraryDocument>(rawJson, JsonOptions);
            if (document == null)
            {
                return new LibraryMetadataCompatibility.Corrupt(new InvalidDataException("Failed to deserialize library document."));
            }

            LibraryValidator.Validate(document);
            return new LibraryMetadataCompatibility.Current();
        }
        catch (Exception ex)
        {
            return new LibraryMetadataCompatibility.Corrupt(ex);
        }
    }

    public static LibraryDocument InspectAndDeserialize(string json)
    {
        if (json == null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new JsonException("Library JSON is empty or whitespace.");
        }

        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Root of library JSON must be an object.");
            }

            int schemaPropCount = 0;
            int foundSchemaVersion = 0;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase))
                {
                    schemaPropCount++;
                    if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetInt32(out foundSchemaVersion))
                    {
                        throw new InvalidDataException("Property 'schemaVersion' must be an integer.");
                    }
                }
            }

            if (schemaPropCount == 0)
            {
                throw new InvalidDataException("Missing required 'schemaVersion' property.");
            }

            if (schemaPropCount > 1)
            {
                throw new InvalidDataException("Multiple conflicting 'schemaVersion' properties found.");
            }

            if (foundSchemaVersion > LibraryDocument.CurrentSchemaVersion)
            {
                throw new UnsupportedLibrarySchemaException(foundSchemaVersion);
            }

            if (foundSchemaVersion != LibraryDocument.CurrentSchemaVersion)
            {
                throw new InvalidDataException($"Unsupported schema version: {foundSchemaVersion}.");
            }

            StrictJsonObjectAuthority.ValidateExactObject(
                doc.RootElement,
                allowedMembers: ["schemaVersion", "categories", "prompts"],
                requiredMembers: ["schemaVersion", "categories", "prompts"],
                description: "library root");

            ValidateLibraryJsonStructure(doc.RootElement);
        }

        LibraryDocument? document = JsonSerializer.Deserialize<LibraryDocument>(json, JsonOptions);
        if (document == null)
        {
            throw new JsonException("Failed to deserialize library document.");
        }

        LibraryValidator.Validate(document);
        return document;
    }

    private static void ValidateLibraryJsonStructure(JsonElement root)
    {
        if (!StrictJsonObjectAuthority.TryGetPropertyIgnoreCase(root, "categories", out JsonProperty categoriesProp) ||
            categoriesProp.Value.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("library.categories must be an array.");
        }

        int categoryIndex = 0;
        foreach (JsonElement category in categoriesProp.Value.EnumerateArray())
        {
            StrictJsonObjectAuthority.ValidateExactObject(
                category,
                allowedMembers: ["id", "parentId", "name", "sortOrder"],
                requiredMembers: ["id", "parentId", "name", "sortOrder"],
                description: $"library.categories[{categoryIndex}]");
            categoryIndex++;
        }

        if (!StrictJsonObjectAuthority.TryGetPropertyIgnoreCase(root, "prompts", out JsonProperty promptsProp) ||
            promptsProp.Value.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("library.prompts must be an array.");
        }

        int promptIndex = 0;
        foreach (JsonElement prompt in promptsProp.Value.EnumerateArray())
        {
            StrictJsonObjectAuthority.ValidateExactObject(
                prompt,
                allowedMembers: ["id", "categoryId", "sortOrder", "title"],
                requiredMembers: ["id", "categoryId", "sortOrder"],
                description: $"library.prompts[{promptIndex}]");
            promptIndex++;
        }
    }
}