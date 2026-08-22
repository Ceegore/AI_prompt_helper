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
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
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

        journal.Revision = 0;
        byte[] bytes = SerializeValidate(journal);

        _writer.CreateNewDurable(
            _paths.LibraryMutationJournalPath,
            bytes,
            DurableFileClass.MutationControl);
    }

    public void AdvanceDurable(LibraryMutationJournal journal, LibraryMutationPhase next)
    {
        ArgumentNullException.ThrowIfNull(journal);

        LibraryMutationJournal persisted = TryReadStrict()
            ?? throw new InvalidDataException("Mutation journal disappeared.");

        if (persisted.OperationId != journal.OperationId)
        {
            throw new InvalidDataException("Mutation journal operation changed.");
        }

        if (persisted.Revision != journal.Revision)
        {
            throw new InvalidDataException(
                $"Mutation journal revision changed. Expected {journal.Revision}, found {persisted.Revision}.");
        }

        if (persisted.Phase != journal.Phase)
        {
            throw new InvalidDataException(
                $"Mutation journal phase changed. Expected {journal.Phase}, found {persisted.Phase}.");
        }

        if (!IsAllowedTransition(journal.Kind, journal.Phase, next))
        {
            throw new InvalidOperationException(
                $"Invalid mutation phase transition for {journal.Kind}: {journal.Phase} -> {next}.");
        }

        LibraryMutationJournal candidate = Clone(journal);
        candidate.Phase = next;
        candidate.Revision = journal.Revision + 1;

        byte[] bytes = SerializeValidate(candidate);

        _writer.ReplaceDurable(
            _paths.LibraryMutationJournalPath,
            bytes,
            DurableFileClass.MutationControl);

        // Only after durable success
        journal.Phase = next;
        journal.Revision = candidate.Revision;
    }

    /// <summary>
    /// Strict handle-bound retirement: the journal is opened once — without following reparse
    /// points and proven to resolve physically inside the data root — read and validated from
    /// that handle, and, only if it still matches, deleted through that exact same handle.
    /// CRUU14-005 closed the "validate by path, then delete by path" window; CRUU15-005 closes
    /// the remaining one, where a symlink at the journal path was followed and the
    /// exact-object deletion destroyed its target instead.
    /// </summary>
    public void DeleteStrict(Guid expectedOperationId, long expectedRevision)
    {
        using WindowsStrictRetirableFile? handle = WindowsStrictRetirableFile.OpenExistingOrNull(
            _paths.LibraryMutationJournalPath,
            _paths.RootDirectory);
        if (handle is null)
        {
            return;
        }

        byte[] bytes = handle.ReadAllBytes();
        string json = StrictUtf8Text.Decode(bytes, "library mutation journal");
        LibraryMutationJournal current = ParseValidate(json);

        if (current.OperationId != expectedOperationId || current.Revision != expectedRevision)
        {
            throw new InvalidDataException("Mutation journal changed before retire.");
        }

        handle.DeleteExact();
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
            int schemaVersion = ReadSchemaVersion(root);

            // Schema v2 requires an explicit "revision" so CAS state can never silently
            // default to zero on a truncated or malformed journal. Schema v1 journals
            // (written before revision existed) are still accepted for compatibility with
            // a journal left on disk by an older build; they are transparently upgraded to
            // the current schema the next time AdvanceDurable writes them.
            string[] requiredMembers = schemaVersion >= 2
                ? [
                    "schemaVersion",
                    "revision",
                    "operationId",
                    "kind",
                    "phase",
                    "promptId",
                    "bodyRelativePath",
                    "oldLibrarySha256Hex",
                    "newLibrarySha256Hex"
                ]
                : [
                    "schemaVersion",
                    "operationId",
                    "kind",
                    "phase",
                    "promptId",
                    "bodyRelativePath",
                    "oldLibrarySha256Hex",
                    "newLibrarySha256Hex"
                ];

            StrictJsonObjectAuthority.ValidateExactObject(
                root,
                allowedMembers: [
                    "schemaVersion",
                    "revision",
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
                requiredMembers: requiredMembers,
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

    private static int ReadSchemaVersion(JsonElement root)
    {
        if (!StrictJsonObjectAuthority.TryGetPropertyIgnoreCase(root, "schemaVersion", out JsonProperty prop) ||
            prop.Value.ValueKind != JsonValueKind.Number ||
            !prop.Value.TryGetInt32(out int schemaVersion))
        {
            throw new InvalidDataException("Property 'schemaVersion' must be an integer.");
        }

        return schemaVersion;
    }

    private static LibraryMutationJournal Clone(LibraryMutationJournal j) => new()
    {
        // Every durable write upgrades the journal to the current schema so a legacy
        // schema-v1 journal (no required revision) never round-trips unchanged.
        SchemaVersion = LibraryMutationJournal.CurrentSchemaVersion,
        Revision = j.Revision,
        OperationId = j.OperationId,
        Kind = j.Kind,
        Phase = j.Phase,
        PromptId = j.PromptId,
        BodyRelativePath = j.BodyRelativePath,
        OldLibrarySha256Hex = j.OldLibrarySha256Hex,
        NewLibrarySha256Hex = j.NewLibrarySha256Hex,
        OldBodyLength = j.OldBodyLength,
        OldBodySha256Hex = j.OldBodySha256Hex,
        NewBodyLength = j.NewBodyLength,
        NewBodySha256Hex = j.NewBodySha256Hex,
        RecoveryBodyRelativePath = j.RecoveryBodyRelativePath
    };

    private static void RequireSha256(string? value, string fieldName)
    {
        if (value is null || value.Length != 64)
        {
            throw new InvalidDataException($"{fieldName} must contain exactly 64 hexadecimal characters.");
        }

        foreach (char c in value)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            {
                throw new InvalidDataException($"{fieldName} must contain only hexadecimal characters.");
            }
        }
    }

    private static void ValidateJournalInvariants(LibraryMutationJournal journal)
    {
        if (journal.SchemaVersion < LibraryMutationJournal.MinSupportedSchemaVersion ||
            journal.SchemaVersion > LibraryMutationJournal.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported mutation journal schema version: {journal.SchemaVersion}.");
        }

        if (journal.Revision < 0)
        {
            throw new InvalidDataException("Mutation journal Revision cannot be negative.");
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

        RequireSha256(journal.OldLibrarySha256Hex, nameof(journal.OldLibrarySha256Hex));
        RequireSha256(journal.NewLibrarySha256Hex, nameof(journal.NewLibrarySha256Hex));

        switch (journal.Kind)
        {
            case LibraryMutationKind.CreatePrompt:
            case LibraryMutationKind.DuplicatePrompt:
                if (journal.NewBodyLength is null or < 0)
                {
                    throw new InvalidDataException("Create/Duplicate journal requires non-negative NewBodyLength.");
                }
                RequireSha256(journal.NewBodySha256Hex, nameof(journal.NewBodySha256Hex));
                if (journal.OldBodyLength is not null || journal.OldBodySha256Hex is not null)
                {
                    throw new InvalidDataException("Create/Duplicate journal must not have OldBody fields.");
                }
                if (journal.RecoveryBodyRelativePath is not null)
                {
                    throw new InvalidDataException("Create/Duplicate journal must not have RecoveryBodyRelativePath.");
                }
                break;

            case LibraryMutationKind.EditPrompt:
                if (journal.OldBodyLength is null or < 0)
                {
                    throw new InvalidDataException("Edit journal requires non-negative OldBodyLength.");
                }
                RequireSha256(journal.OldBodySha256Hex, nameof(journal.OldBodySha256Hex));
                if (journal.NewBodyLength is null or < 0)
                {
                    throw new InvalidDataException("Edit journal requires non-negative NewBodyLength.");
                }
                RequireSha256(journal.NewBodySha256Hex, nameof(journal.NewBodySha256Hex));
                if (string.IsNullOrWhiteSpace(journal.RecoveryBodyRelativePath))
                {
                    throw new InvalidDataException("Edit journal requires RecoveryBodyRelativePath.");
                }
                string expectedRecoveryRel = Path.Combine("recovery", $"mutation-{journal.OperationId:N}-old-{journal.PromptId:N}.md");
                string normRecoveryRel = journal.RecoveryBodyRelativePath.Replace('/', '\\').TrimStart('\\');
                if (!string.Equals(normRecoveryRel, expectedRecoveryRel, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Invalid recovery body relative path: '{journal.RecoveryBodyRelativePath}'. Expected '{expectedRecoveryRel}'.");
                }
                break;

            case LibraryMutationKind.DeletePrompt:
                if (journal.OldBodyLength is null or < 0)
                {
                    throw new InvalidDataException("Delete journal requires non-negative OldBodyLength.");
                }
                RequireSha256(journal.OldBodySha256Hex, nameof(journal.OldBodySha256Hex));
                if (journal.NewBodyLength is not null || journal.NewBodySha256Hex is not null)
                {
                    throw new InvalidDataException("Delete journal must not have NewBody fields.");
                }
                if (journal.RecoveryBodyRelativePath is not null)
                {
                    throw new InvalidDataException("Delete journal must not have RecoveryBodyRelativePath.");
                }
                break;
        }
    }
}
