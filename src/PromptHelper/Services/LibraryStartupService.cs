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
    private readonly IDurableAtomicFileWriter _writer;
    private readonly LibraryInitializationJournalRepository _initJournalRepo;

    internal LibraryStartupService(
        AppPaths paths,
        LibraryRepository libraryRepo,
        PromptRepository promptRepo,
        IFileDeleter deleter,
        IDurableAtomicFileWriter writer)
    {
        _paths = paths;
        _libraryRepo = libraryRepo;
        _promptRepo = promptRepo;
        _deleter = deleter;
        _writer = writer;
        _initJournalRepo = new LibraryInitializationJournalRepository(paths, writer);
    }

    public LibraryStartupService(
        AppPaths paths,
        LibraryRepository libraryRepo,
        PromptRepository promptRepo)
        : this(paths, libraryRepo, promptRepo, new FileDeleter(), new WindowsDurableAtomicFileWriter())
    {
    }

    public StartupResult LoadOrInitialize()
    {
        _paths.EnsureDataDirectories();

        var inspector = new LibraryPackageInspector(_paths);

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

        LibraryPackageState? primaryPackage = null;
        if (primaryResult is MetadataReadResult.Valid primaryValid)
        {
            primaryPackage = inspector.Inspect(primaryValid.Document);
        }

        // Valid primary always wins immediately IF HEALTHY
        if (primaryPackage is LibraryPackageState.Healthy primaryHealthy)
        {
            var syncResult = _libraryRepo.SynchronizeBackup(primaryHealthy.Package);
            string? backupWarning = syncResult.BackupSynchronized ? null : syncResult.Warning;

            TryRemoveStaleMarker();
            return new StartupResult(primaryHealthy.Document, false, backupWarning);
        }

        // 2. Inspect Backup
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

        LibraryPackageState? backupPackage = null;
        if (backupResult is MetadataReadResult.Valid backupValid)
        {
            backupPackage = inspector.Inspect(backupValid.Document);
        }

        // Primary is metadata current, package incomplete
        if (primaryPackage != null && primaryPackage is not LibraryPackageState.Healthy)
        {
            if (backupPackage is LibraryPackageState.Healthy backupHealthy1)
            {
                // recover metadata from complete backup; warn
                if (primaryResult is MetadataReadResult.Valid pv)
                {
                    _libraryRepo.TryCreateIncompleteRecoveryCopy(pv.Document);
                }
                var commitResult = _libraryRepo.Commit(backupHealthy1.Document);
                TryRemoveStaleMarker();

                string warning = RecoveryWarning;
                if (!commitResult.BackupSynchronized && commitResult.Warning != null)
                {
                    warning += "\r\n\r\n" + commitResult.Warning;
                }
                return new StartupResult(backupHealthy1.Document, true, warning);
            }
            else
            {
                // backup incomplete/missing/corrupt
                throw new InvalidDataException("Primary library is incomplete and no complete backup is available.");
            }
        }

        // Primary is Corrupt
        if (primaryResult is MetadataReadResult.Corrupt primaryCorrupt)
        {
            if (backupPackage is LibraryPackageState.Healthy backupHealthy2)
            {
                _libraryRepo.TryCreateCorruptRecoveryCopy(primaryCorrupt.RawContent);
                var commitResult = _libraryRepo.Commit(backupHealthy2.Document);
                TryRemoveStaleMarker();

                string warning = RecoveryWarning;
                if (!commitResult.BackupSynchronized && commitResult.Warning != null)
                {
                    warning += "\r\n\r\n" + commitResult.Warning;
                }

                return new StartupResult(backupHealthy2.Document, true, warning);
            }

            throw new InvalidDataException("Primary library metadata is corrupt and no complete backup is available.");
        }

        // Primary is Missing
        if (primaryResult is MetadataReadResult.Missing)
        {
            if (backupPackage is LibraryPackageState.Healthy backupHealthy3)
            {
                var commitResult = _libraryRepo.Commit(backupHealthy3.Document);
                TryRemoveStaleMarker();

                string warning = RecoveryWarning;
                if (!commitResult.BackupSynchronized && commitResult.Warning != null)
                {
                    warning += "\r\n\r\n" + commitResult.Warning;
                }

                return new StartupResult(backupHealthy3.Document, true, warning);
            }

            if (backupResult is MetadataReadResult.Corrupt)
            {
                throw new InvalidDataException("Primary metadata is missing and backup metadata is corrupt.");
            }

            if (backupPackage != null && backupPackage is not LibraryPackageState.Healthy)
            {
                throw new InvalidDataException("Primary metadata is missing and backup library is incomplete.");
            }

            // Both primary and backup are missing -> First run or Interrupted initialization
            return HandleFirstRunOrInterruptedInit();
        }

        throw new InvalidOperationException("Unexpected startup state.");
    }

    private StartupResult HandleFirstRunOrInterruptedInit()
    {
        LibraryInitializationJournal? journal = _initJournalRepo.TryReadStrict();
        IReadOnlyList<string> existingPromptFiles = _promptRepo.EnumeratePromptFilesStrict();
        DefaultLibraryPackage defaultPkg = DefaultLibraryFactory.CreateDefaults();

        if (journal is null)
        {
            if (existingPromptFiles.Count > 0)
            {
                throw new InvalidOperationException(
                    "Unknown prompt files found in data folder without library metadata or initialization marker. Initialization aborted to prevent data loss.");
            }

            // Clean first run
            journal = new LibraryInitializationJournal
            {
                InitializationId = Guid.NewGuid(),
                Phase = LibraryInitializationPhase.CreatingDefaults
            };
            _initJournalRepo.CreatePreparedDurable(journal);

            foreach (var kvp in defaultPkg.PromptContents)
            {
                _promptRepo.Create(kvp.Key, kvp.Value);
            }

            var commitResult = _libraryRepo.Commit(defaultPkg.Document);
            FinalizeInitJournal(journal);

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

                string existingContent = StrictUtf8Text.ReadAllText(filePath, "default prompt file");
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
            FinalizeInitJournal(journal);

            return new StartupResult(defaultPkg.Document, false, commitResult.Warning);
        }
    }

    private void FinalizeInitJournal(LibraryInitializationJournal journal)
    {
        try
        {
            if (journal.Phase != LibraryInitializationPhase.MetadataDurable)
            {
                _initJournalRepo.AdvanceDurable(journal, LibraryInitializationPhase.MetadataDurable);
            }

            _initJournalRepo.DeleteStrict(journal.InitializationId, journal.Revision);
        }
        catch
        {
            // Best effort: the journal, once at MetadataDurable, is restart-finalizable — a
            // future launch (including a retry of this one) will see that phase and retire
            // it without redoing default-library creation.
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
            raw = StrictUtf8Text.ReadAllText(path, $"metadata file '{path}'");
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
            SecurityException or
            InvalidDataException)
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