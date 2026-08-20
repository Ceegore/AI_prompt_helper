using System;
using System.IO;
using System.Security;
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

    private abstract record MetadataFileState
    {
        public sealed record Missing : MetadataFileState;
        public sealed record Current : MetadataFileState;
        public sealed record Future(int Version) : MetadataFileState;
        public sealed record Corrupt(Exception Error) : MetadataFileState;
        public sealed record Unreadable(Exception Error) : MetadataFileState;
    }

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

        string json = JsonSerializer.Serialize(document, JsonOptions);
        _writer.Write(_paths.LibraryPath, json);

        return SynchronizeBackupPreservingFuture(json);
    }

    public CommitResult SynchronizeBackup(LibraryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        LibraryValidator.Validate(document);

        string json = JsonSerializer.Serialize(document, JsonOptions);
        return SynchronizeBackupPreservingFuture(json);
    }

    private CommitResult SynchronizeBackupPreservingFuture(string json)
    {
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
            _writer.Write(_paths.LibraryBackupPath, json);
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

    private static MetadataFileState ReadMetadataFileState(string path)
    {
        string raw;
        try
        {
            raw = File.ReadAllText(path);
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
            ex is IOException or UnauthorizedAccessException or SecurityException)
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

            _writer.Write(recoveryFile, corruptPrimaryContent);
        }
        catch
        {
            // Explicit best-effort only
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
        }

        LibraryDocument? document = JsonSerializer.Deserialize<LibraryDocument>(json, JsonOptions);
        if (document == null)
        {
            throw new JsonException("Failed to deserialize library document.");
        }

        LibraryValidator.Validate(document);
        return document;
    }
}