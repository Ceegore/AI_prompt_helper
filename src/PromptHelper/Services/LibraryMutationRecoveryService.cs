using System;
using System.IO;
using System.Security.Cryptography;
using PromptHelper.Models;

namespace PromptHelper.Services;

public sealed record MutationRecoveryResult(
    bool Success,
    bool Committed = false,
    CommitResult? CommitResult = null,
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
            bool oldMatch = string.Equals(librarySha, journal.OldLibrarySha256Hex, StringComparison.OrdinalIgnoreCase);
            bool newMatch = string.Equals(librarySha, journal.NewLibrarySha256Hex, StringComparison.OrdinalIgnoreCase);

            LibraryMutationMetadataState libraryState;
            if (oldMatch && newMatch)
            {
                libraryState = LibraryMutationMetadataState.OldAndNewSameBytes;
            }
            else if (oldMatch)
            {
                libraryState = LibraryMutationMetadataState.OldOnly;
            }
            else if (newMatch)
            {
                libraryState = LibraryMutationMetadataState.NewOnly;
            }
            else
            {
                libraryState = LibraryMutationMetadataState.Other;
            }

            if (libraryState == LibraryMutationMetadataState.Other)
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

            return journal.Kind switch
            {
                LibraryMutationKind.CreatePrompt or LibraryMutationKind.DuplicatePrompt
                    => RecoverCreateOrDuplicate(journal, libraryState, bodyState, promptPath),

                LibraryMutationKind.EditPrompt
                    => RecoverEdit(journal, libraryState, bodyState, recoveryState, promptPath, recoveryPath, recoveryBytes),

                LibraryMutationKind.DeletePrompt
                    => RecoverDelete(journal, libraryState, bodyState, promptPath),

                _ => throw new InvalidOperationException($"Unsupported mutation kind: {journal.Kind}")
            };
        }
        catch (Exception ex)
        {
            return new MutationRecoveryResult(false, ErrorMessage: ex.Message);
        }
    }

    private MutationRecoveryResult RecoverCreateOrDuplicate(
        LibraryMutationJournal journal,
        LibraryMutationMetadataState libraryState,
        MutationContentState bodyState,
        string promptPath)
    {
        if (libraryState == LibraryMutationMetadataState.OldOnly ||
            (libraryState == LibraryMutationMetadataState.OldAndNewSameBytes && journal.Phase < LibraryMutationPhase.MetadataDurable))
        {
            if (bodyState == MutationContentState.New)
            {
                if (journal.Phase >= LibraryMutationPhase.BodyDurable)
                {
                    _verifiedDeleter.VerifyAndDelete(
                        _paths.RootDirectory,
                        promptPath,
                        journal.NewBodyLength!.Value,
                        journal.NewBodySha256Hex!);
                }
                else
                {
                    throw new InvalidDataException("Ambiguous body ownership during create/duplicate recovery: body exists but BodyDurable phase was not reached.");
                }
            }
            else if (bodyState != MutationContentState.Missing)
            {
                throw new InvalidDataException("Inconsistent active body state for create/duplicate recovery.");
            }

            _journalRepo.DeleteStrict(journal.OperationId, journal.Revision);
            return new MutationRecoveryResult(true, Committed: false);
        }

        if (libraryState == LibraryMutationMetadataState.NewOnly ||
            (libraryState == LibraryMutationMetadataState.OldAndNewSameBytes && journal.Phase >= LibraryMutationPhase.MetadataDurable))
        {
            if (bodyState != MutationContentState.New)
            {
                throw new InvalidDataException("Committed create/duplicate mutation lacks valid new prompt body.");
            }

            _journalRepo.DeleteStrict(journal.OperationId, journal.Revision);
            return new MutationRecoveryResult(true, Committed: true, CommitResult: new CommitResult(true, null));
        }

        throw new InvalidDataException("Inconsistent create/duplicate mutation state.");
    }

    private MutationRecoveryResult RecoverEdit(
        LibraryMutationJournal journal,
        LibraryMutationMetadataState libraryState,
        MutationContentState bodyState,
        MutationContentState recoveryState,
        string promptPath,
        string? recoveryPath,
        byte[]? recoveryBytes)
    {
        bool committed = libraryState switch
        {
            LibraryMutationMetadataState.NewOnly => true,
            LibraryMutationMetadataState.OldOnly => false,
            LibraryMutationMetadataState.OldAndNewSameBytes => journal.Phase >= LibraryMutationPhase.MetadataDurable,
            _ => throw new InvalidDataException("Library metadata does not match the mutation journal.")
        };

        if (!committed)
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
                    _verifiedDeleter.VerifyAndDelete(
                        _paths.RootDirectory,
                        recoveryPath,
                        journal.OldBodyLength!.Value,
                        journal.OldBodySha256Hex!);
                }
            }
            else
            {
                throw new InvalidDataException("Inconsistent edit recovery state: old library metadata with missing old body.");
            }

            _journalRepo.DeleteStrict(journal.OperationId, journal.Revision);
            return new MutationRecoveryResult(true, Committed: false);
        }
        else
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
                    throw new InvalidDataException("Recovery prompt copy contains unexpected bytes.");
                }
            }

            _journalRepo.DeleteStrict(journal.OperationId, journal.Revision);
            return new MutationRecoveryResult(true, Committed: true, CommitResult: new CommitResult(true, null));
        }
    }

    private MutationRecoveryResult RecoverDelete(
        LibraryMutationJournal journal,
        LibraryMutationMetadataState libraryState,
        MutationContentState bodyState,
        string promptPath)
    {
        if (libraryState == LibraryMutationMetadataState.OldOnly)
        {
            _journalRepo.DeleteStrict(journal.OperationId, journal.Revision);
            return new MutationRecoveryResult(true, Committed: false);
        }

        if (libraryState == LibraryMutationMetadataState.NewOnly)
        {
            if (bodyState == MutationContentState.Old)
            {
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
                    _journalRepo.DeleteStrict(journal.OperationId, journal.Revision);
                    return new MutationRecoveryResult(true, Committed: true, CommitResult: new CommitResult(true, null));
                }

                _journalRepo.DeleteStrict(journal.OperationId, journal.Revision);
                string warning = "Prompt was deleted from metadata, but prompt file was preserved because safety backup was not synchronized.";
                return new MutationRecoveryResult(
                    true,
                    Committed: true,
                    CommitResult: new CommitResult(false, warning),
                    Warning: warning);
            }

            if (bodyState == MutationContentState.Missing)
            {
                _journalRepo.DeleteStrict(journal.OperationId, journal.Revision);
                return new MutationRecoveryResult(true, Committed: true, CommitResult: new CommitResult(true, null));
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
