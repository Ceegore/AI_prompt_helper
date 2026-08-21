using System;
using System.IO;
using System.Security.Cryptography;
using PromptHelper.Models;

namespace PromptHelper.Services;

public sealed record MutationRecoveryResult(
    bool Success,
    string? Warning = null,
    string? ErrorMessage = null);

internal sealed class LibraryMutationRecoveryService
{
    private readonly AppPaths _paths;
    private readonly LibraryMutationJournalRepository _journalRepo;
    private readonly IDurableAtomicFileWriter _writer;
    private readonly IVerifiedArtifactDeleter _verifiedDeleter;
    private readonly StrictPathAuthority _strictPaths;

    public LibraryMutationRecoveryService(
        AppPaths paths,
        LibraryMutationJournalRepository journalRepo,
        IDurableAtomicFileWriter writer,
        IVerifiedArtifactDeleter? verifiedDeleter = null,
        StrictPathAuthority? strictPaths = null)
    {
        _paths = paths;
        _journalRepo = journalRepo;
        _writer = writer;
        _verifiedDeleter = verifiedDeleter ?? new WindowsVerifiedArtifactDeleter();
        _strictPaths = strictPaths ?? new StrictPathAuthority();
    }

    public MutationRecoveryResult RecoverIfPresent()
    {
        LibraryMutationJournal? journal = _journalRepo.TryReadStrict();
        if (journal is null)
        {
            return new MutationRecoveryResult(true);
        }

        try
        {
            byte[]? libraryBytes = TryReadBytes(_paths.LibraryPath);
            if (libraryBytes is null)
            {
                throw new InvalidDataException("library.json is missing during mutation recovery.");
            }

            string librarySha = Convert.ToHexStringLower(SHA256.HashData(libraryBytes));
            MutationContentState libraryState;

            if (string.Equals(librarySha, journal.OldLibrarySha256Hex, StringComparison.OrdinalIgnoreCase))
            {
                libraryState = MutationContentState.Old;
            }
            else if (string.Equals(librarySha, journal.NewLibrarySha256Hex, StringComparison.OrdinalIgnoreCase))
            {
                libraryState = MutationContentState.New;
            }
            else
            {
                libraryState = MutationContentState.Other;
            }

            if (libraryState == MutationContentState.Other)
            {
                throw new InvalidDataException("library.json does not match old or new journal hash. Recovery stopped.");
            }

            string promptPath = _paths.GetPromptPath(journal.PromptId);
            byte[]? bodyBytes = TryReadBytes(promptPath);
            MutationContentState bodyState = MutationContentClassifier.ClassifyBytes(
                bodyBytes,
                journal.OldBodyLength,
                journal.OldBodySha256Hex,
                journal.NewBodyLength,
                journal.NewBodySha256Hex);

            if (bodyState == MutationContentState.Other)
            {
                throw new InvalidDataException("Active prompt body does not match old or new journal hash. Recovery stopped.");
            }

            string? recoveryPath = !string.IsNullOrWhiteSpace(journal.RecoveryBodyRelativePath)
                ? Path.Combine(_paths.RootDirectory, journal.RecoveryBodyRelativePath)
                : null;
            byte[]? recoveryBytes = recoveryPath != null ? TryReadBytes(recoveryPath) : null;
            MutationContentState recoveryState = MutationContentClassifier.ClassifyBytes(
                recoveryBytes,
                journal.OldBodyLength,
                journal.OldBodySha256Hex,
                null,
                null);

            if (recoveryBytes != null && recoveryState == MutationContentState.Other)
            {
                throw new InvalidDataException("Recovery prompt copy does not match expected old hash. Recovery stopped.");
            }

            switch (journal.Kind)
            {
                case LibraryMutationKind.CreatePrompt:
                case LibraryMutationKind.DuplicatePrompt:
                    RecoverCreateOrDuplicate(journal, libraryState, bodyState, promptPath);
                    break;

                case LibraryMutationKind.EditPrompt:
                    RecoverEdit(journal, libraryState, bodyState, recoveryState, promptPath, recoveryPath, recoveryBytes);
                    break;

                case LibraryMutationKind.DeletePrompt:
                    return RecoverDelete(journal, libraryState, bodyState, promptPath);

                default:
                    throw new InvalidOperationException($"Unsupported mutation kind: {journal.Kind}");
            }

            _journalRepo.DeleteStrict();
            return new MutationRecoveryResult(true);
        }
        catch (Exception ex)
        {
            return new MutationRecoveryResult(false, null, ex.Message);
        }
    }

    private void RecoverCreateOrDuplicate(
        LibraryMutationJournal journal,
        MutationContentState libraryState,
        MutationContentState bodyState,
        string promptPath)
    {
        if (libraryState == MutationContentState.Old)
        {
            if (bodyState == MutationContentState.New)
            {
                _verifiedDeleter.VerifyAndDelete(
                    _paths.RootDirectory,
                    promptPath,
                    journal.NewBodyLength!.Value,
                    journal.NewBodySha256Hex!);
            }
            else if (bodyState != MutationContentState.Missing)
            {
                throw new InvalidDataException("Inconsistent active body state for create/duplicate recovery.");
            }
        }
        else if (libraryState == MutationContentState.New)
        {
            if (bodyState != MutationContentState.New)
            {
                throw new InvalidDataException("Committed create/duplicate mutation lacks valid new prompt body.");
            }
        }
    }

    private void RecoverEdit(
        LibraryMutationJournal journal,
        MutationContentState libraryState,
        MutationContentState bodyState,
        MutationContentState recoveryState,
        string promptPath,
        string? recoveryPath,
        byte[]? recoveryBytes)
    {
        if (libraryState == MutationContentState.Old)
        {
            if (recoveryState == MutationContentState.Old && recoveryBytes != null)
            {
                if (bodyState == MutationContentState.New || bodyState == MutationContentState.Missing)
                {
                    _writer.ReplaceDurable(promptPath, recoveryBytes, DurableFileClass.PromptBody);
                }

                if (recoveryPath != null && File.Exists(recoveryPath))
                {
                    _verifiedDeleter.VerifyAndDelete(
                        _paths.RootDirectory,
                        recoveryPath,
                        journal.OldBodyLength!.Value,
                        journal.OldBodySha256Hex!);
                }
            }
            else if (bodyState == MutationContentState.Old)
            {
                if (recoveryPath != null && File.Exists(recoveryPath))
                {
                    File.Delete(recoveryPath);
                }
            }
            else
            {
                throw new InvalidDataException("Inconsistent edit recovery state: old library metadata with missing old body.");
            }
        }
        else if (libraryState == MutationContentState.New)
        {
            if (bodyState != MutationContentState.New)
            {
                throw new InvalidDataException("Committed edit mutation lacks valid new prompt body.");
            }

            if (recoveryPath != null && File.Exists(recoveryPath))
            {
                if (recoveryState == MutationContentState.Old)
                {
                    _verifiedDeleter.VerifyAndDelete(
                        _paths.RootDirectory,
                        recoveryPath,
                        journal.OldBodyLength!.Value,
                        journal.OldBodySha256Hex!);
                }
                else
                {
                    File.Delete(recoveryPath);
                }
            }
        }
    }

    private MutationRecoveryResult RecoverDelete(
        LibraryMutationJournal journal,
        MutationContentState libraryState,
        MutationContentState bodyState,
        string promptPath)
    {
        if (libraryState == MutationContentState.Old)
        {
            // Deletion did not commit
            _journalRepo.DeleteStrict();
            return new MutationRecoveryResult(true);
        }

        if (libraryState == MutationContentState.New)
        {
            if (bodyState == MutationContentState.Old)
            {
                // Check backup metadata state
                byte[]? backupBytes = TryReadBytes(_paths.LibraryBackupPath);
                bool backupSynchronized = false;

                if (backupBytes != null)
                {
                    string backupSha = Convert.ToHexStringLower(SHA256.HashData(backupBytes));
                    if (string.Equals(backupSha, journal.NewLibrarySha256Hex, StringComparison.OrdinalIgnoreCase))
                    {
                        backupSynchronized = true;
                    }
                }

                if (backupSynchronized)
                {
                    _verifiedDeleter.VerifyAndDelete(
                        _paths.RootDirectory,
                        promptPath,
                        journal.OldBodyLength!.Value,
                        journal.OldBodySha256Hex!);
                    _journalRepo.DeleteStrict();
                    return new MutationRecoveryResult(true);
                }

                // Backup is not synchronized; preserve as orphan and retire journal with warning
                _journalRepo.DeleteStrict();
                return new MutationRecoveryResult(
                    true,
                    Warning: "Prompt was deleted from metadata, but prompt file was preserved because safety backup was not synchronized.");
            }

            if (bodyState == MutationContentState.Missing)
            {
                _journalRepo.DeleteStrict();
                return new MutationRecoveryResult(true);
            }
        }

        throw new InvalidDataException("Inconsistent delete mutation state.");
    }

    private byte[]? TryReadBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Failed to read '{path}' during mutation recovery: {ex.Message}", ex);
        }
    }
}
