using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PromptHelper.Services;

internal sealed class LibraryMutationJournalRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppPaths _paths;
    private readonly IDurableAtomicFileWriter _writer;
    private readonly StrictPathAuthority _strictPaths;

    public LibraryMutationJournalRepository(
        AppPaths paths,
        IDurableAtomicFileWriter writer,
        StrictPathAuthority? strictPaths = null)
    {
        _paths = paths;
        _writer = writer;
        _strictPaths = strictPaths ?? new StrictPathAuthority();
    }

    public LibraryMutationJournal? TryReadStrict()
    {
        StrictPathProbe state = _strictPaths.Probe(_paths.LibraryMutationJournalPath);

        if (state.Kind == StrictPathKind.Missing)
        {
            return null;
        }

        if (state.Kind != StrictPathKind.File)
        {
            throw new InvalidDataException("Library mutation journal path is not a file.");
        }

        string json = StrictUtf8Text.ReadAllText(_paths.LibraryMutationJournalPath, "library mutation journal");
        return ParseValidate(json);
    }

    public void CreatePreparedDurable(LibraryMutationJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);

        if (journal.Phase != LibraryMutationPhase.Prepared)
        {
            throw new InvalidOperationException("New mutation journal must begin in Prepared phase.");
        }

        byte[] bytes = SerializeValidate(journal);

        _writer.CreateNewDurable(
            _paths.LibraryMutationJournalPath,
            bytes,
            DurableFileClass.MutationControl);
    }

    public void AdvanceDurable(LibraryMutationJournal journal, LibraryMutationPhase next)
    {
        ArgumentNullException.ThrowIfNull(journal);

        if (!IsAllowedTransition(journal.Kind, journal.Phase, next))
        {
            throw new InvalidOperationException(
                $"Invalid mutation phase transition for {journal.Kind}: {journal.Phase} -> {next}.");
        }

        journal.Phase = next;
        byte[] bytes = SerializeValidate(journal);

        _writer.ReplaceDurable(
            _paths.LibraryMutationJournalPath,
            bytes,
            DurableFileClass.MutationControl);
    }

    public void DeleteStrict()
    {
        StrictPathProbe state = _strictPaths.Probe(_paths.LibraryMutationJournalPath);

        if (state.Kind == StrictPathKind.Missing)
        {
            return;
        }

        if (state.Kind != StrictPathKind.File)
        {
            throw new InvalidDataException("Mutation journal path is not a file.");
        }

        File.Delete(_paths.LibraryMutationJournalPath);
    }

    public static bool IsAllowedTransition(LibraryMutationKind kind, LibraryMutationPhase current, LibraryMutationPhase next)
    {
        return (kind, current, next) switch
        {
            (LibraryMutationKind.CreatePrompt or LibraryMutationKind.DuplicatePrompt, LibraryMutationPhase.Prepared, LibraryMutationPhase.BodyDurable) => true,
            (LibraryMutationKind.CreatePrompt or LibraryMutationKind.DuplicatePrompt, LibraryMutationPhase.BodyDurable, LibraryMutationPhase.MetadataDurable) => true,

            (LibraryMutationKind.EditPrompt, LibraryMutationPhase.Prepared, LibraryMutationPhase.RecoveryBodyDurable) => true,
            (LibraryMutationKind.EditPrompt, LibraryMutationPhase.RecoveryBodyDurable, LibraryMutationPhase.BodyDurable) => true,
            (LibraryMutationKind.EditPrompt, LibraryMutationPhase.BodyDurable, LibraryMutationPhase.MetadataDurable) => true,

            (LibraryMutationKind.DeletePrompt, LibraryMutationPhase.Prepared, LibraryMutationPhase.MetadataDurable) => true,
            (LibraryMutationKind.DeletePrompt, LibraryMutationPhase.MetadataDurable, LibraryMutationPhase.BodyDeleted) => true,

            _ => false
        };
    }

    public static byte[] SerializeValidate(LibraryMutationJournal journal)
    {
        ValidateJournalInvariants(journal);
        string json = JsonSerializer.Serialize(journal, JsonOptions);
        return StrictUtf8Text.Encode(json);
    }

    public static LibraryMutationJournal ParseValidate(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("Library mutation journal JSON is empty or whitespace.");
        }

        using (JsonDocument document = JsonDocument.Parse(json))
        {
            JsonElement root = document.RootElement;
            StrictJsonObjectAuthority.ValidateExactObject(
                root,
                allowedMembers: [
                    "schemaVersion",
                    "operationId",
                    "kind",
                    "phase",
                    "promptId",
                    "bodyRelativePath",
                    "oldLibrarySha256Hex",
                    "newLibrarySha256Hex",
                    "oldBodyLength",
                    "oldBodySha256Hex",
                    "newBodyLength",
                    "newBodySha256Hex",
                    "recoveryBodyRelativePath"
                ],
                requiredMembers: [
                    "schemaVersion",
                    "operationId",
                    "kind",
                    "phase",
                    "promptId",
                    "bodyRelativePath",
                    "oldLibrarySha256Hex",
                    "newLibrarySha256Hex"
                ],
                description: "library mutation journal root");
        }

        LibraryMutationJournal? journal = JsonSerializer.Deserialize<LibraryMutationJournal>(json, JsonOptions);
        if (journal is null)
        {
            throw new InvalidDataException("Library mutation journal deserialized to null.");
        }

        ValidateJournalInvariants(journal);
        return journal;
    }

    private static void ValidateJournalInvariants(LibraryMutationJournal journal)
    {
        if (journal.SchemaVersion != LibraryMutationJournal.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported mutation journal schema version: {journal.SchemaVersion}.");
        }

        if (journal.OperationId == Guid.Empty)
        {
            throw new InvalidDataException("Mutation journal OperationId cannot be empty.");
        }

        if (!Enum.IsDefined(journal.Kind))
        {
            throw new InvalidDataException($"Undefined mutation kind: {journal.Kind}.");
        }

        if (!Enum.IsDefined(journal.Phase))
        {
            throw new InvalidDataException($"Undefined mutation phase: {journal.Phase}.");
        }

        if (journal.PromptId == Guid.Empty)
        {
            throw new InvalidDataException("Mutation journal PromptId cannot be empty.");
        }

        string expectedBodyRel = Path.Combine("prompts", $"{journal.PromptId:N}.md");
        string normBodyRel = journal.BodyRelativePath.Replace('/', '\\').TrimStart('\\');
        if (!string.Equals(normBodyRel, expectedBodyRel, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Invalid body relative path: '{journal.BodyRelativePath}'. Expected '{expectedBodyRel}'.");
        }

        if (string.IsNullOrWhiteSpace(journal.OldLibrarySha256Hex) || journal.OldLibrarySha256Hex.Length != 64)
        {
            throw new InvalidDataException("Invalid OldLibrarySha256Hex in mutation journal.");
        }

        if (string.IsNullOrWhiteSpace(journal.NewLibrarySha256Hex) || journal.NewLibrarySha256Hex.Length != 64)
        {
            throw new InvalidDataException("Invalid NewLibrarySha256Hex in mutation journal.");
        }

        if (!string.IsNullOrWhiteSpace(journal.RecoveryBodyRelativePath))
        {
            string expectedRecoveryRel = Path.Combine("recovery", $"mutation-{journal.OperationId:N}-old-{journal.PromptId:N}.md");
            string normRecoveryRel = journal.RecoveryBodyRelativePath.Replace('/', '\\').TrimStart('\\');
            if (!string.Equals(normRecoveryRel, expectedRecoveryRel, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Invalid recovery body relative path: '{journal.RecoveryBodyRelativePath}'. Expected '{expectedRecoveryRel}'.");
            }
        }
    }
}
