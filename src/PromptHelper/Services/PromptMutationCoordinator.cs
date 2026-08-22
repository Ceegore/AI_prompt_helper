using System;
using System.IO;
using System.Security.Cryptography;
using PromptHelper.Models;

namespace PromptHelper.Services;

internal sealed class PromptMutationCoordinator
{
    private readonly AppPaths _paths;
    private readonly PromptRepository _promptRepo;
    private readonly LibraryRepository _libraryRepo;
    private readonly LibraryPackageInspector _packageInspector;
    private readonly LibraryMutationJournalRepository _journalRepo;
    private readonly LibraryMutationRecoveryService _recovery;
    private readonly IDurableAtomicFileWriter _writer;
    private readonly IVerifiedArtifactDeleter _verifiedDeleter;

    public PromptMutationCoordinator(
        AppPaths paths,
        PromptRepository promptRepo,
        LibraryRepository libraryRepo,
        LibraryPackageInspector packageInspector,
        LibraryMutationJournalRepository journalRepo,
        LibraryMutationRecoveryService recovery,
        IDurableAtomicFileWriter writer,
        IVerifiedArtifactDeleter? verifiedDeleter = null)
    {
        _paths = paths;
        _promptRepo = promptRepo;
        _libraryRepo = libraryRepo;
        _packageInspector = packageInspector;
        _journalRepo = journalRepo;
        _recovery = recovery;
        _writer = writer;
        _verifiedDeleter = verifiedDeleter ?? new WindowsVerifiedArtifactDeleter();
    }

    public CommitResult CommitCreatePrompt(
        LibraryDocument current,
        LibraryDocument candidate,
        PromptRecord newPrompt,
        string body,
        LibraryMutationKind kind)
    {
        if (kind is not LibraryMutationKind.CreatePrompt and not LibraryMutationKind.DuplicatePrompt)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        LibraryPrimarySnapshot disk = _libraryRepo.CapturePrimarySnapshot();
        byte[] currentCanonical = _libraryRepo.SerializeCanonicalBytes(current);
        if (!disk.CanonicalBytes.AsSpan().SequenceEqual(currentCanonical))
        {
            throw new InvalidOperationException(
                "The library changed outside the current Prompt Helper state. Reload before editing.");
        }

        CanonicalLibraryPackage newPackage = _libraryRepo.CreateCanonicalPackage(candidate);
        byte[] newBody = StrictUtf8Text.Encode(body);
        Guid operationId = Guid.NewGuid();

        var journal = new LibraryMutationJournal
        {
            OperationId = operationId,
            Kind = kind,
            Phase = LibraryMutationPhase.Prepared,
            PromptId = newPrompt.Id,
            BodyRelativePath = Path.Combine("prompts", $"{newPrompt.Id:N}.md"),
            OldLibrarySha256Hex = disk.RawSha256Hex,
            NewLibrarySha256Hex = newPackage.Sha256Hex,
            NewBodyLength = newBody.LongLength,
            NewBodySha256Hex = Hash(newBody)
        };

        _journalRepo.CreatePreparedDurable(journal);

        return ExecuteJournaledMutation(journal, () =>
        {
            _writer.CreateNewDurable(
                _paths.GetPromptPath(newPrompt.Id),
                newBody,
                DurableFileClass.PromptBody);

            _journalRepo.AdvanceDurable(
                journal,
                LibraryMutationPhase.BodyDurable);

            CommitResult result = _libraryRepo.CommitIfPrimaryUnchanged(newPackage, disk.RawSha256Hex);

            try
            {
                _journalRepo.AdvanceDurable(
                    journal,
                    LibraryMutationPhase.MetadataDurable);

                _journalRepo.DeleteStrict(journal.OperationId, journal.Revision);
            }
            catch (Exception ex)
            {
                throw new CommittedMutationRequiresRestartException(
                    journal.OperationId,
                    "The prompt was saved to library metadata, but recovery journal bookkeeping could not be completed. Restart required.",
                    ex);
            }

            return result;
        });
    }

    public CommitResult CommitEditPrompt(
        LibraryDocument current,
        LibraryDocument candidate,
        Guid promptId,
        string newBody)
    {
        LibraryPrimarySnapshot disk = _libraryRepo.CapturePrimarySnapshot();
        byte[] currentCanonical = _libraryRepo.SerializeCanonicalBytes(current);
        if (!disk.CanonicalBytes.AsSpan().SequenceEqual(currentCanonical))
        {
            throw new InvalidOperationException(
                "The library changed outside the current Prompt Helper state. Reload before editing.");
        }

        CanonicalLibraryPackage newPackage = _libraryRepo.CreateCanonicalPackage(candidate);
        string bodyPath = _paths.GetPromptPath(promptId);
        byte[] oldBody = _promptRepo.ReadBytesStrict(promptId);
        byte[] newBodyBytes = StrictUtf8Text.Encode(newBody);
        Guid operationId = Guid.NewGuid();

        string recoveryRelative = Path.Combine(
            "recovery",
            $"mutation-{operationId:N}-old-{promptId:N}.md");

        string recoveryFull = Path.Combine(
            _paths.RootDirectory,
            recoveryRelative);

        var journal = new LibraryMutationJournal
        {
            OperationId = operationId,
            Kind = LibraryMutationKind.EditPrompt,
            Phase = LibraryMutationPhase.Prepared,
            PromptId = promptId,
            BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
            OldLibrarySha256Hex = disk.RawSha256Hex,
            NewLibrarySha256Hex = newPackage.Sha256Hex,
            OldBodyLength = oldBody.LongLength,
            OldBodySha256Hex = Hash(oldBody),
            NewBodyLength = newBodyBytes.LongLength,
            NewBodySha256Hex = Hash(newBodyBytes),
            RecoveryBodyRelativePath = recoveryRelative
        };

        _journalRepo.CreatePreparedDurable(journal);

        return ExecuteJournaledMutation(journal, () =>
        {
            _writer.CreateNewDurable(
                recoveryFull,
                oldBody,
                DurableFileClass.RecoveryArtifact);

            _journalRepo.AdvanceDurable(
                journal,
                LibraryMutationPhase.RecoveryBodyDurable);

            // Narrow the TOCTOU window on the prompt body: re-verify the active body still
            // matches what was read at the start of this operation immediately before the
            // destructive replace, instead of trusting a read that may be stale by the time
            // an interactive edit dialog was closed.
            byte[] currentBodyBeforeReplace;
            try
            {
                currentBodyBeforeReplace = _promptRepo.ReadBytesStrict(promptId);
            }
            catch (FileNotFoundException ex)
            {
                throw new InvalidOperationException(
                    "The prompt file was removed outside the current Prompt Helper state. Reload before editing.", ex);
            }

            if (!currentBodyBeforeReplace.AsSpan().SequenceEqual(oldBody))
            {
                throw new InvalidOperationException(
                    "The prompt file changed outside the current Prompt Helper state. Reload before editing.");
            }

            _writer.ReplaceDurable(
                bodyPath,
                newBodyBytes,
                DurableFileClass.PromptBody);

            _journalRepo.AdvanceDurable(
                journal,
                LibraryMutationPhase.BodyDurable);

            // For a body-only edit, OldLibrarySha256Hex == NewLibrarySha256Hex, so the
            // primary commit below is content-neutral: library.json is byte-identical
            // whether or not it actually runs. That means library.json content can never
            // be used, on restart, to distinguish "commit happened" from "commit did not
            // happen". Advance the journal to MetadataDurable *before* invoking the
            // content-neutral commit so the durable journal phase alone is the unambiguous
            // commit authority: once this write lands, recovery must treat the mutation as
            // committed (keep the new body) even if the commit call below throws or the
            // process dies before it runs, because there is nothing left for it to change.
            bool bodyOnlyEdit = string.Equals(
                journal.OldLibrarySha256Hex,
                journal.NewLibrarySha256Hex,
                StringComparison.OrdinalIgnoreCase);

            CommitResult result;
            if (bodyOnlyEdit)
            {
                // Re-verify freshness before durably committing to MetadataDurable: once
                // that phase is written, recovery treats the mutation as committed even if
                // the commit call below never runs, so an external change must be detected
                // here, before the phase advance, not after.
                _libraryRepo.VerifyPrimaryUnchanged(disk.RawSha256Hex);

                _journalRepo.AdvanceDurable(
                    journal,
                    LibraryMutationPhase.MetadataDurable);

                result = _libraryRepo.Commit(newPackage);
            }
            else
            {
                result = _libraryRepo.CommitIfPrimaryUnchanged(newPackage, disk.RawSha256Hex);
            }

            try
            {
                if (!bodyOnlyEdit)
                {
                    _journalRepo.AdvanceDurable(
                        journal,
                        LibraryMutationPhase.MetadataDurable);
                }

                _verifiedDeleter.VerifyAndDelete(
                    _paths.RootDirectory,
                    recoveryFull,
                    oldBody.LongLength,
                    Hash(oldBody));

                _journalRepo.DeleteStrict(journal.OperationId, journal.Revision);
            }
            catch (Exception ex)
            {
                throw new CommittedMutationRequiresRestartException(
                    journal.OperationId,
                    "The prompt was updated in library metadata, but recovery journal bookkeeping could not be completed. Restart required.",
                    ex);
            }

            return result;
        });
    }

    public CommitResult CommitDeletePrompt(
        LibraryDocument current,
        LibraryDocument candidate,
        Guid promptId)
    {
        if (!_promptRepo.Exists(promptId))
        {
            CanonicalLibraryPackage pkg = _libraryRepo.CreateCanonicalPackage(candidate);
            return _libraryRepo.Commit(pkg);
        }

        LibraryPrimarySnapshot disk = _libraryRepo.CapturePrimarySnapshot();
        byte[] currentCanonical = _libraryRepo.SerializeCanonicalBytes(current);
        if (!disk.CanonicalBytes.AsSpan().SequenceEqual(currentCanonical))
        {
            throw new InvalidOperationException(
                "The library changed outside the current Prompt Helper state. Reload before editing.");
        }

        CanonicalLibraryPackage newPackage = _libraryRepo.CreateCanonicalPackage(candidate);
        byte[] body = _promptRepo.ReadBytesStrict(promptId);
        Guid operationId = Guid.NewGuid();

        var journal = new LibraryMutationJournal
        {
            OperationId = operationId,
            Kind = LibraryMutationKind.DeletePrompt,
            Phase = LibraryMutationPhase.Prepared,
            PromptId = promptId,
            BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
            OldLibrarySha256Hex = disk.RawSha256Hex,
            NewLibrarySha256Hex = newPackage.Sha256Hex,
            OldBodyLength = body.LongLength,
            OldBodySha256Hex = Hash(body)
        };

        _journalRepo.CreatePreparedDurable(journal);

        return ExecuteJournaledMutation(journal, () =>
        {
            CommitResult result = _libraryRepo.CommitIfPrimaryUnchanged(newPackage, disk.RawSha256Hex);

            try
            {
                _journalRepo.AdvanceDurable(
                    journal,
                    LibraryMutationPhase.MetadataDurable);

                if (result.BackupSynchronized)
                {
                    try
                    {
                        _verifiedDeleter.VerifyAndDelete(
                            _paths.RootDirectory,
                            _paths.GetPromptPath(promptId),
                            body.LongLength,
                            Hash(body));

                        _journalRepo.AdvanceDurable(
                            journal,
                            LibraryMutationPhase.BodyDeleted);
                    }
                    catch (Exception ex)
                    {
                        string warning = string.IsNullOrEmpty(result.Warning)
                            ? $"Failed to delete prompt body file: {ex.Message}"
                            : $"{result.Warning} Also failed to delete prompt body file: {ex.Message}";
                        result = new CommitResult(result.BackupSynchronized, warning);
                    }
                }

                _journalRepo.DeleteStrict(journal.OperationId, journal.Revision);
            }
            catch (Exception ex) when (ex is not CommittedMutationRequiresRestartException)
            {
                throw new CommittedMutationRequiresRestartException(
                    journal.OperationId,
                    "The prompt was deleted from library metadata, but recovery journal bookkeeping could not be completed. Restart required.",
                    ex);
            }

            return result;
        });
    }

    private CommitResult ExecuteJournaledMutation(
        LibraryMutationJournal journal,
        Func<CommitResult> operation)
    {
        Exception? original = null;
        try
        {
            return operation();
        }
        catch (Exception ex)
        {
            original = ex;
        }

        if (original is CommittedMutationRequiresRestartException)
        {
            throw original;
        }

        MutationRecoveryResult recovery = _recovery.RecoverIfPresent();
        if (!recovery.Success)
        {
            LibraryMutationJournal? persisted = _journalRepo.TryReadStrict();
            bool primaryCommitted = persisted is not null &&
                                    persisted.Phase >= LibraryMutationPhase.MetadataDurable;

            if (primaryCommitted)
            {
                throw new CommittedMutationRequiresRestartException(
                    journal.OperationId,
                    "The prompt change reached the library metadata, but Prompt Helper could not " +
                    "finish durable recovery bookkeeping. Restart required.",
                    original!);
            }

            throw new IOException(
                "The prompt change failed and automatic rollback could not be completed. " +
                "Recovery evidence was preserved.",
                original);
        }

        if (recovery.Committed)
        {
            return recovery.CommitResult ?? new CommitResult(true, recovery.Warning);
        }

        throw original!;
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
}
