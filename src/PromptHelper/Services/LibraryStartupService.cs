using System.IO;
using System.Text.Json;
using PromptHelper.Models;

namespace PromptHelper.Services;

public sealed class LibraryStartupService
{
    public const string RecoveryWarning =
        "Library data was recovered from the safety backup.\r\n\r\n" +
        "If Prompt Helper had previously warned that the safety backup could not " +
        "be updated, the restored library structure may represent an older saved " +
        "state. Existing prompt files were not automatically deleted.";

    private readonly AppPaths _paths;
    private readonly LibraryRepository _libraryRepo;
    private readonly PromptRepository _promptRepo;
    private readonly IFileDeleter _deleter;
    private readonly IAtomicTextWriter _writer;

    public LibraryStartupService(
        AppPaths paths,
        LibraryRepository libraryRepo,
        PromptRepository promptRepo,
        IFileDeleter deleter,
        IAtomicTextWriter writer)
    {
        _paths = paths;
        _libraryRepo = libraryRepo;
        _promptRepo = promptRepo;
        _deleter = deleter;
        _writer = writer;
    }

    public StartupResult LoadOrInitialize()
    {
        _paths.EnsureDataDirectories();

        // 1. Inspect Primary
        MetadataReadResult primaryResult = ReadMetadataState(_paths.LibraryPath);
        if (primaryResult is MetadataReadResult.FutureSchema primaryFuture)
        {
            throw new UnsupportedLibrarySchemaException(primaryFuture.Version);
        }

        // 2. Inspect Backup
        MetadataReadResult backupResult = ReadMetadataState(_paths.LibraryBackupPath);

        // Matrix resolution
        if (primaryResult is MetadataReadResult.Valid primaryValid)
        {
            // Valid primary always wins
            try
            {
                _libraryRepo.SynchronizeBackup(primaryValid.Document);
            }
            catch
            {
                // Best effort backup sync
            }

            TryRemoveStaleMarker();
            return new StartupResult(primaryValid.Document, false, null);
        }

        if (primaryResult is MetadataReadResult.Corrupt primaryCorrupt)
        {
            if (backupResult is MetadataReadResult.Valid backupValid)
            {
                _libraryRepo.TryCreateCorruptRecoveryCopy(primaryCorrupt.RawContent);
                _libraryRepo.Commit(backupValid.Document);
                TryRemoveStaleMarker();
                return new StartupResult(backupValid.Document, true, RecoveryWarning);
            }

            throw new InvalidDataException("Primary library metadata is corrupt and no valid backup is available.");
        }

        // Primary is Missing
        if (backupResult is MetadataReadResult.FutureSchema backupFuture)
        {
            throw new UnsupportedLibrarySchemaException(backupFuture.Version);
        }

        if (backupResult is MetadataReadResult.Valid backupValidFromMissing)
        {
            _libraryRepo.Commit(backupValidFromMissing.Document);
            TryRemoveStaleMarker();
            return new StartupResult(backupValidFromMissing.Document, true, RecoveryWarning);
        }

        if (backupResult is MetadataReadResult.Corrupt)
        {
            throw new InvalidDataException("Primary metadata is missing and backup metadata is corrupt.");
        }

        // Both primary and backup are missing -> First run or Interrupted initialization
        return HandleFirstRunOrInterruptedInit();
    }

    private StartupResult HandleFirstRunOrInterruptedInit()
    {
        bool markerExists = File.Exists(_paths.InitializationMarkerPath);
        IReadOnlyList<string> existingPromptFiles = _promptRepo.EnumeratePromptFiles();
        DefaultLibraryPackage defaultPkg = DefaultLibraryFactory.CreateDefaults();

        if (!markerExists)
        {
            if (existingPromptFiles.Count > 0)
            {
                throw new InvalidOperationException(
                    "Unknown prompt files found in data folder without library metadata or initialization marker. Initialization aborted to prevent data loss.");
            }

            // Clean first run
            _writer.Write(_paths.InitializationMarkerPath, "initializing");

            foreach (var kvp in defaultPkg.PromptContents)
            {
                _promptRepo.Create(kvp.Key, kvp.Value);
            }

            _libraryRepo.Commit(defaultPkg.Document);
            TryRemoveStaleMarker();

            return new StartupResult(defaultPkg.Document, false, null);
        }
        else
        {
            // Interrupted initialization recovery
            var allowedDefaultGuids = defaultPkg.PromptContents.Keys.ToHashSet();

            foreach (string filePath in existingPromptFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                if (!Guid.TryParseExact(fileName, "N", out Guid fileGuid) || !allowedDefaultGuids.Contains(fileGuid))
                {
                    throw new InvalidOperationException($"Found unknown prompt file during interrupted initialization: {filePath}");
                }

                string existingContent = File.ReadAllText(filePath);
                string expectedContent = defaultPkg.PromptContents[fileGuid];
                if (!string.Equals(existingContent, expectedContent, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Found modified prompt file during interrupted initialization: {filePath}");
                }
            }

            // Create any missing default prompt files
            foreach (var kvp in defaultPkg.PromptContents)
            {
                if (!_promptRepo.Exists(kvp.Key))
                {
                    _promptRepo.Create(kvp.Key, kvp.Value);
                }
            }

            _libraryRepo.Commit(defaultPkg.Document);
            TryRemoveStaleMarker();

            return new StartupResult(defaultPkg.Document, false, null);
        }
    }

    private void TryRemoveStaleMarker()
    {
        try
        {
            if (File.Exists(_paths.InitializationMarkerPath))
            {
                _deleter.DeleteIfExists(_paths.InitializationMarkerPath);
            }
        }
        catch
        {
            // Best effort
        }
    }

    private static MetadataReadResult ReadMetadataState(string path)
    {
        if (!File.Exists(path))
        {
            return new MetadataReadResult.Missing();
        }

        string raw;
        try
        {
            raw = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is not (JsonException or InvalidDataException))
        {
            throw;
        }

        try
        {
            LibraryDocument doc = LibraryRepository.InspectAndDeserialize(raw);
            return new MetadataReadResult.Valid(doc);
        }
        catch (UnsupportedLibrarySchemaException ex)
        {
            return new MetadataReadResult.FutureSchema(ex.SchemaVersion);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return new MetadataReadResult.Corrupt(raw);
        }
    }

    private abstract record MetadataReadResult
    {
        public sealed record Valid(LibraryDocument Document) : MetadataReadResult;
        public sealed record Corrupt(string RawContent) : MetadataReadResult;
        public sealed record Missing : MetadataReadResult;
        public sealed record FutureSchema(int Version) : MetadataReadResult;
    }
}