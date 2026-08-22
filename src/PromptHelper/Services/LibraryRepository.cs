using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
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
    private readonly IAtomicExpectedFileReplacer _atomicReplacer;

    internal LibraryRepository(
        AppPaths paths,
        IDurableAtomicFileWriter durableWriter,
        IAtomicExpectedFileReplacer? atomicReplacer = null)
    {
        _paths = paths;
        _durableWriter = durableWriter;

        // A durable writer that also implements the atomic replacer stays the single
        // fault-injection seam for tests that fail "the write". Production's
        // WindowsDurableAtomicFileWriter deliberately does not implement it, so the real
        // compare-and-swap primitive is always used at runtime.
        _atomicReplacer = atomicReplacer
            ?? durableWriter as IAtomicExpectedFileReplacer
            ?? new WindowsAtomicExpectedFileReplacer();
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

    /// <summary>
    /// Read-only commit precondition: throws if the primary library file no longer
    /// matches <paramref name="expectedRawSha256Hex"/>. Does not write anything, so it is
    /// safe to call before a durable journal phase transition that must not be preceded
    /// by an undetected external change.
    /// </summary>
    public void VerifyPrimaryUnchanged(string expectedRawSha256Hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRawSha256Hex);

        byte[] currentRaw;
        try
        {
            currentRaw = File.ReadAllBytes(_paths.LibraryPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidOperationException(
                "The library changed outside the current Prompt Helper state. Reload before editing.", ex);
        }

        string currentHash = Convert.ToHexStringLower(SHA256.HashData(currentRaw));
        if (!string.Equals(currentHash, expectedRawSha256Hex, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The library changed outside the current Prompt Helper state. Reload before editing.");
        }
    }

    /// <summary>
    /// Commits <paramref name="package"/> only if the primary still holds exactly
    /// <paramref name="expectedRawSha256Hex"/>, as one atomic compare-and-swap: the expected
    /// object is held under OS-enforced exclusion from the moment its hash is proven until the
    /// replacement consumes that exclusion (see <see cref="IAtomicExpectedFileReplacer"/>).
    /// CRUU14 verified and then wrote through a separate call, which left a window in which a
    /// concurrent update was silently overwritten; there is no such window here (CRUU15-003).
    /// </summary>
    public CommitResult CommitIfPrimaryUnchanged(CanonicalLibraryPackage package, string expectedRawSha256Hex)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRawSha256Hex);

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

        try
        {
            _atomicReplacer.ReplaceIfExpected(
                _paths.RootDirectory,
                _paths.LibraryPath,
                ExpectedFileState.Present(expectedRawSha256Hex),
                package.CanonicalBytes,
                DurableFileClass.LibraryMetadata);
        }
        catch (StaleExpectedFileException ex)
        {
            throw new InvalidOperationException(
                "The library changed outside the current Prompt Helper state. Reload before editing.", ex);
        }

        return SynchronizeBackup(package);
    }

    /// <summary>
    /// The primary commit for a body-only prompt edit, where the serialized library is
    /// byte-identical before and after. The write is content-neutral, so a concurrent external
    /// change must never be overwritten by it — but it also must not fail the mutation, whose
    /// real payload (the prompt body) is already durable. A stale primary therefore yields a
    /// warning rather than an exception, and the external content stays exactly as it is
    /// (CRUU15-003).
    /// </summary>
    public CommitResult CommitContentNeutralIfPrimaryUnchanged(
        CanonicalLibraryPackage package,
        string expectedRawSha256Hex)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRawSha256Hex);

        try
        {
            return CommitIfPrimaryUnchanged(package, expectedRawSha256Hex);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is StaleExpectedFileException)
        {
            return new CommitResult(
                false,
                "The prompt text was saved, but library.json had been changed by something else " +
                "and was left untouched. Reload to see the current library.");
        }
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

    /// <summary>
    /// Internal (CRUU14-009): a public overload here would let any caller construct an
    /// arbitrary valid <see cref="CanonicalLibraryPackage"/> and publish it to the backup
    /// independently of the actual current primary. The only legitimate callers are
    /// <see cref="Commit(CanonicalLibraryPackage)"/> (which writes the same package to
    /// primary immediately before this call) and the internal
    /// <see cref="SynchronizeBackup(HealthyLibraryPackage)"/> overload used by startup
    /// recovery, which is itself derived from an inspected-healthy primary/backup read.
    /// </summary>
    internal CommitResult SynchronizeBackup(CanonicalLibraryPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        // CRUU15-004: this method promises to preserve a newer-schema backup. Inspecting the
        // backup and then replacing it in a separate step cannot keep that promise — a
        // future-schema backup written between the two would be destroyed by a decision made
        // before it existed. The observed state is therefore captured as an expectation and
        // enforced by the replacement itself, so anything that appears or changes in between
        // makes the write fail closed with the backup preserved.
        MetadataFileState state = ReadBackupStateWithHash(out string? observedSha256Hex);

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

        ExpectedFileState expected = state is MetadataFileState.Missing
            ? ExpectedFileState.Missing
            : ExpectedFileState.Present(observedSha256Hex!);

        try
        {
            _atomicReplacer.ReplaceIfExpected(
                _paths.RootDirectory,
                _paths.LibraryBackupPath,
                expected,
                package.CanonicalBytes,
                DurableFileClass.LibraryMetadata);
            return new CommitResult(true, null);
        }
        catch (StaleExpectedFileException ex)
        {
            return new CommitResult(
                false,
                "The library was saved, but library.backup.json was changed by something else " +
                $"while it was being synchronized. The existing backup was preserved and was not overwritten. {ex.Message}");
        }
        catch (Exception ex)
        {
            return new CommitResult(
                false,
                "The library was saved, but its safety backup could not be synchronized (safety backup could not be updated). " +
                $"Current data remains stored in library.json. {ex.Message}");
        }
    }

    /// <summary>
    /// Reads the backup's compatibility state and, when it exists, the exact hash of the bytes
    /// that state was derived from — so the two can be bound together as one expectation.
    /// </summary>
    private MetadataFileState ReadBackupStateWithHash(out string? sha256Hex)
    {
        sha256Hex = null;

        byte[] raw;
        try
        {
            raw = File.ReadAllBytes(_paths.LibraryBackupPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new MetadataFileState.Missing();
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return new MetadataFileState.Unreadable(ex);
        }

        sha256Hex = Convert.ToHexStringLower(SHA256.HashData(raw));

        string json;
        try
        {
            json = StrictUtf8Text.Decode(raw, $"library file '{_paths.LibraryBackupPath}'");
        }
        catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException)
        {
            return new MetadataFileState.Unreadable(ex);
        }

        try
        {
            InspectAndDeserialize(json);
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