using System.IO;
using System.Text.Json;
using PromptHelper.Models;

namespace PromptHelper.Services;

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

    private readonly AppPaths _paths;
    private readonly IAtomicTextWriter _writer;

    public LibraryRepository(AppPaths paths, IAtomicTextWriter writer)
    {
        _paths = paths;
        _writer = writer;
    }

    public LibraryDocument ReadPrimary()
    {
        string json = File.ReadAllText(_paths.LibraryPath);
        return InspectAndDeserialize(json);
    }

    public LibraryDocument ReadBackup()
    {
        string json = File.ReadAllText(_paths.LibraryBackupPath);
        return InspectAndDeserialize(json);
    }

    public CommitResult Commit(LibraryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        LibraryValidator.Validate(document);

        string json = JsonSerializer.Serialize(document, JsonOptions);

        _writer.Write(_paths.LibraryPath, json);

        try
        {
            _writer.Write(_paths.LibraryBackupPath, json);
            return new CommitResult(true, null);
        }
        catch (Exception)
        {
            return new CommitResult(
                false,
                "The library was saved, but its safety backup could not be updated. Current data remains stored in library.json.");
        }
    }

    public void SynchronizeBackup(LibraryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        LibraryValidator.Validate(document);
        string json = JsonSerializer.Serialize(document, JsonOptions);
        _writer.Write(_paths.LibraryBackupPath, json);
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

            _writer.Write(recoveryFile, corruptPrimaryContent);
        }
        catch
        {
            // Explicit best-effort only
        }
    }

    public static LibraryDocument InspectAndDeserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

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
        }

        LibraryDocument? libraryDoc = JsonSerializer.Deserialize<LibraryDocument>(json, JsonOptions);
        if (libraryDoc == null)
        {
            throw new InvalidDataException("Library document deserialized to null.");
        }

        LibraryValidator.Validate(libraryDoc);

        return libraryDoc;
    }
}