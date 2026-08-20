using System;
using System.IO;
using System.Security;
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

        if (primaryResult is MetadataReadResult.Unreadable primaryUnreadable)
        {
            throw new IOException($"The library metadata could not be read: {primaryUnreadable.Error.Message}", primaryUnreadable.Error);
        }

        // Valid primary always wins immediately
        if (primaryResult is MetadataReadResult.Valid primaryValid)
        {
            string? backupWarning = null;
            MetadataReadResult backupState = ReadMetadataState(_paths.LibraryBackupPath);

            if (backupState is MetadataReadResult.FutureSchema futureBackup)
            {
                backupWarning =
                    $"The current library.json was loaded, but library.backup.json uses " +
                    $"newer schema version {futureBackup.Version}. " +
                    "The newer backup was preserved and was not overwritten.";
            }
            else if (backupState is MetadataReadResult.Unreadable unreadableBackup)
            {
                backupWarning =
                    "The current library.json was loaded, but its safety backup could not " +
                    $"be synchronized: {unreadableBackup.Error.Message}";
            }
            else
            {
                try
                {
                    _libraryRepo.SynchronizeBackup(primaryValid.Document);
                }
                catch (Exception)
                {
                    backupWarning =
                        "The library was loaded from library.json, but its safety backup " +
                        "could not be synchronized.";
                }
            }

            TryRemoveStaleMarker();
            return new StartupResult(primaryValid.Document, false, backupWarning);
        }

        // 2. Inspect Backup only when primary is corrupt or missing
        MetadataReadResult backupResult = ReadMetadataState(_paths.LibraryBackupPath);

        if (backupResult is MetadataReadResult.FutureSchema backupFuture)
        {
            throw new UnsupportedLibrarySchemaException(backupFuture.Version);
        }

        if (backupResult is MetadataReadResult.Unreadable unreadableBackupRecovery)
        {
            throw new IOException(
                $"The library backup could not be read: {unreadableBackupRecovery.Error.Message}",
                unreadableBackupRecovery.Error);
        }

        // Primary is Corrupt
        if (primaryResult is MetadataReadResult.Corrupt primaryCorrupt)
        {
            if (backupResult is MetadataReadResult.Valid backupValid)
            {
                _libraryRepo.TryCreateCorruptRecoveryCopy(primaryCorrupt.RawContent);
                var commitResult = _libraryRepo.Commit(backupValid.Document);
                TryRemoveStaleMarker();

                string warning = RecoveryWarning;
                if (!commitResult.BackupSynchronized && commitResult.Warning != null)
                {
                    warning += "\r\n\r\n" + commitResult.Warning;
                }

                return new StartupResult(backupValid.Document, true, warning);
            }

            throw new InvalidDataException("Primary library metadata is corrupt and no valid backup is available.");
        }

        // Primary is Missing
        if (backupResult is MetadataReadResult.Valid backupValidFromMissing)
        {
            var commitResult = _libraryRepo.Commit(backupValidFromMissing.Document);
            TryRemoveStaleMarker();

            string warning = RecoveryWarning;
            if (!commitResult.BackupSynchronized && commitResult.Warning != null)
            {
                warning += "\r\n\r\n" + commitResult.Warning;
            }

            return new StartupResult(backupValidFromMissing.Document, true, warning);
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
        bool markerExists = IsMarkerPresent();
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

            var commitResult = _libraryRepo.Commit(defaultPkg.Document);
            TryRemoveStaleMarker();

            return new StartupResult(defaultPkg.Document, false, commitResult.Warning);
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

            var commitResult = _libraryRepo.Commit(defaultPkg.Document);
            TryRemoveStaleMarker();

            return new StartupResult(defaultPkg.Document, false, commitResult.Warning);
        }
    }

    private bool IsMarkerPresent()
    {
        try
        {
            using var fs = new FileStream(_paths.InitializationMarkerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private void TryRemoveStaleMarker()
    {
        try
        {
            _deleter.DeleteIfExists(_paths.InitializationMarkerPath);
        }
        catch
        {
            // Best effort
        }
    }

    private static MetadataReadResult ReadMetadataState(string path)
    {
        string raw;
        try
        {
            raw = File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return new MetadataReadResult.Missing();
        }
        catch (DirectoryNotFoundException)
        {
            return new MetadataReadResult.Missing();
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            SecurityException)
        {
            return new MetadataReadResult.Unreadable(ex);
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
        public sealed record Unreadable(Exception Error) : MetadataReadResult;
    }
}