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

        Guid operationId = Guid.NewGuid();

        byte[] oldLibrary = _libraryRepo.SerializeCanonicalBytes(current);
        byte[] newLibrary = _libraryRepo.SerializeCanonicalBytes(candidate);
        byte[] newBody = StrictUtf8Text.Encode(body);

        var journal = new LibraryMutationJournal
        {
            OperationId = operationId,
            Kind = kind,
            Phase = LibraryMutationPhase.Prepared,
            PromptId = newPrompt.Id,
            BodyRelativePath = Path.Combine("prompts", $"{newPrompt.Id:N}.md"),
            OldLibrarySha256Hex = Hash(oldLibrary),
            NewLibrarySha256Hex = Hash(newLibrary),
            NewBodyLength = newBody.LongLength,
            NewBodySha256Hex = Hash(newBody)
        };

        _journalRepo.CreatePreparedDurable(journal);

        try
        {
            _writer.CreateNewDurable(
                _paths.GetPromptPath(newPrompt.Id),
                newBody,
                DurableFileClass.PromptBody);

            _journalRepo.AdvanceDurable(
                journal,
                LibraryMutationPhase.BodyDurable);

            CommitResult result = _libraryRepo.CommitCanonicalBytes(
                candidate,
                newLibrary);

            _journalRepo.AdvanceDurable(
                journal,
                LibraryMutationPhase.MetadataDurable);

            _journalRepo.DeleteStrict();
            return result;
        }
        catch
        {
            try
            {
                _verifiedDeleter.VerifyAndDelete(
                    _paths.RootDirectory,
                    _paths.GetPromptPath(newPrompt.Id),
                    newBody.LongLength,
                    Hash(newBody));
            }
            catch { }

            try
            {
                _journalRepo.DeleteStrict();
            }
            catch { }

            throw;
        }
    }

    public CommitResult CommitEditPrompt(
        LibraryDocument current,
        LibraryDocument candidate,
        Guid promptId,
        string newBody)
    {
        Guid operationId = Guid.NewGuid();

        string bodyPath = _paths.GetPromptPath(promptId);
        byte[] oldBody = _promptRepo.ReadBytesStrict(promptId);
        byte[] newBodyBytes = StrictUtf8Text.Encode(newBody);

        byte[] oldLibrary = _libraryRepo.SerializeCanonicalBytes(current);
        byte[] newLibrary = _libraryRepo.SerializeCanonicalBytes(candidate);

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
            OldLibrarySha256Hex = Hash(oldLibrary),
            NewLibrarySha256Hex = Hash(newLibrary),
            OldBodyLength = oldBody.LongLength,
            OldBodySha256Hex = Hash(oldBody),
            NewBodyLength = newBodyBytes.LongLength,
            NewBodySha256Hex = Hash(newBodyBytes),
            RecoveryBodyRelativePath = recoveryRelative
        };

        _journalRepo.CreatePreparedDurable(journal);

        try
        {
            _writer.CreateNewDurable(
                recoveryFull,
                oldBody,
                DurableFileClass.RecoveryArtifact);

            _journalRepo.AdvanceDurable(
                journal,
                LibraryMutationPhase.RecoveryBodyDurable);

            _writer.ReplaceDurable(
                bodyPath,
                newBodyBytes,
                DurableFileClass.PromptBody);

            _journalRepo.AdvanceDurable(
                journal,
                LibraryMutationPhase.BodyDurable);

            CommitResult result = _libraryRepo.CommitCanonicalBytes(
                candidate,
                newLibrary);

            _journalRepo.AdvanceDurable(
                journal,
                LibraryMutationPhase.MetadataDurable);

            _verifiedDeleter.VerifyAndDelete(
                _paths.RootDirectory,
                recoveryFull,
                oldBody.LongLength,
                Hash(oldBody));

            _journalRepo.DeleteStrict();
            return result;
        }
        catch
        {
            try
            {
                if (File.Exists(recoveryFull))
                {
                    _writer.ReplaceDurable(
                        bodyPath,
                        oldBody,
                        DurableFileClass.PromptBody);

                    _verifiedDeleter.VerifyAndDelete(
                        _paths.RootDirectory,
                        recoveryFull,
                        oldBody.LongLength,
                        Hash(oldBody));
                }
            }
            catch { }

            try
            {
                _journalRepo.DeleteStrict();
            }
            catch { }

            throw;
        }
    }

    public CommitResult CommitDeletePrompt(
        LibraryDocument current,
        LibraryDocument candidate,
        Guid promptId)
    {
        if (!_promptRepo.Exists(promptId))
        {
            byte[] newBytes = _libraryRepo.SerializeCanonicalBytes(candidate);
            return _libraryRepo.CommitCanonicalBytes(candidate, newBytes);
        }

        byte[] oldLibrary = _libraryRepo.SerializeCanonicalBytes(current);
        byte[] newLibrary = _libraryRepo.SerializeCanonicalBytes(candidate);
        byte[] body = _promptRepo.ReadBytesStrict(promptId);

        var journal = new LibraryMutationJournal
        {
            OperationId = Guid.NewGuid(),
            Kind = LibraryMutationKind.DeletePrompt,
            Phase = LibraryMutationPhase.Prepared,
            PromptId = promptId,
            BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
            OldLibrarySha256Hex = Hash(oldLibrary),
            NewLibrarySha256Hex = Hash(newLibrary),
            OldBodyLength = body.LongLength,
            OldBodySha256Hex = Hash(body)
        };

        _journalRepo.CreatePreparedDurable(journal);

        CommitResult result = _libraryRepo.CommitCanonicalBytes(
            candidate,
            newLibrary);

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

        _journalRepo.DeleteStrict();
        return result;
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
}
