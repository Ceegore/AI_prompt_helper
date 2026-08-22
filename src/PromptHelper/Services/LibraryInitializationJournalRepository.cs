using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PromptHelper.Services;

/// <summary>
/// Durable phase journal for first-run/interrupted default-library initialization, mirroring
/// <see cref="LibraryMutationJournalRepository"/>'s CreateNew-publication + revision-CAS
/// pattern instead of an unstructured marker file. The journal itself is restart-finalizable:
/// once a launch has durably advanced it to <see cref="LibraryInitializationPhase.MetadataDurable"/>,
/// any later launch (including this one, on a best-effort retry) may retire it without redoing
/// default-library creation.
/// </summary>
internal sealed class LibraryInitializationJournalRepository
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

    public LibraryInitializationJournalRepository(
        AppPaths paths,
        IDurableAtomicFileWriter writer,
        StrictPathAuthority? strictPaths = null)
    {
        _paths = paths;
        _writer = writer;
        _strictPaths = strictPaths ?? new StrictPathAuthority();
    }

    public LibraryInitializationJournal? TryReadStrict()
    {
        StrictPathProbe state = _strictPaths.Probe(_paths.InitializationMarkerPath);

        if (state.Kind == StrictPathKind.Missing)
        {
            return null;
        }

        if (state.Kind != StrictPathKind.File)
        {
            throw new InvalidDataException("Library initialization journal path is not a file.");
        }

        string json = StrictUtf8Text.ReadAllText(_paths.InitializationMarkerPath, "library initialization journal");
        return ParseValidate(json);
    }

    public void CreatePreparedDurable(LibraryInitializationJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);

        if (journal.Phase != LibraryInitializationPhase.CreatingDefaults)
        {
            throw new InvalidOperationException("New initialization journal must begin in CreatingDefaults phase.");
        }

        journal.Revision = 0;
        byte[] bytes = SerializeValidate(journal);

        _writer.CreateNewDurable(
            _paths.InitializationMarkerPath,
            bytes,
            DurableFileClass.InitializationControl);
    }

    public void AdvanceDurable(LibraryInitializationJournal journal, LibraryInitializationPhase next)
    {
        ArgumentNullException.ThrowIfNull(journal);

        LibraryInitializationJournal persisted = TryReadStrict()
            ?? throw new InvalidDataException("Initialization journal disappeared.");

        if (persisted.InitializationId != journal.InitializationId)
        {
            throw new InvalidDataException("Initialization journal identity changed.");
        }

        if (persisted.Revision != journal.Revision)
        {
            throw new InvalidDataException(
                $"Initialization journal revision changed. Expected {journal.Revision}, found {persisted.Revision}.");
        }

        if (persisted.Phase != journal.Phase)
        {
            throw new InvalidDataException(
                $"Initialization journal phase changed. Expected {journal.Phase}, found {persisted.Phase}.");
        }

        if (journal.Phase != LibraryInitializationPhase.CreatingDefaults || next != LibraryInitializationPhase.MetadataDurable)
        {
            throw new InvalidOperationException(
                $"Invalid initialization phase transition: {journal.Phase} -> {next}.");
        }

        var candidate = new LibraryInitializationJournal
        {
            SchemaVersion = LibraryInitializationJournal.CurrentSchemaVersion,
            InitializationId = journal.InitializationId,
            Phase = next,
            Revision = journal.Revision + 1
        };

        byte[] bytes = SerializeValidate(candidate);

        _writer.ReplaceDurable(
            _paths.InitializationMarkerPath,
            bytes,
            DurableFileClass.InitializationControl);

        // Only after durable success
        journal.Phase = next;
        journal.Revision = candidate.Revision;
    }

    public void DeleteStrict(Guid expectedInitializationId, long expectedRevision)
    {
        LibraryInitializationJournal? current = TryReadStrict();
        if (current is null)
        {
            return;
        }

        if (current.InitializationId != expectedInitializationId || current.Revision != expectedRevision)
        {
            throw new InvalidDataException("Initialization journal changed before retire.");
        }

        StrictPathProbe state = _strictPaths.Probe(_paths.InitializationMarkerPath);
        if (state.Kind == StrictPathKind.File)
        {
            File.Delete(_paths.InitializationMarkerPath);
        }
    }

    private static byte[] SerializeValidate(LibraryInitializationJournal journal)
    {
        ValidateJournalInvariants(journal);
        string json = JsonSerializer.Serialize(journal, JsonOptions);
        return StrictUtf8Text.Encode(json);
    }

    private static LibraryInitializationJournal ParseValidate(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("Library initialization journal JSON is empty or whitespace.");
        }

        using (JsonDocument document = JsonDocument.Parse(json))
        {
            StrictJsonObjectAuthority.ValidateExactObject(
                document.RootElement,
                allowedMembers: ["schemaVersion", "revision", "initializationId", "phase"],
                requiredMembers: ["schemaVersion", "revision", "initializationId", "phase"],
                description: "library initialization journal root");
        }

        LibraryInitializationJournal? journal = JsonSerializer.Deserialize<LibraryInitializationJournal>(json, JsonOptions);
        if (journal is null)
        {
            throw new InvalidDataException("Library initialization journal deserialized to null.");
        }

        ValidateJournalInvariants(journal);
        return journal;
    }

    private static void ValidateJournalInvariants(LibraryInitializationJournal journal)
    {
        if (journal.SchemaVersion != LibraryInitializationJournal.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported initialization journal schema version: {journal.SchemaVersion}.");
        }

        if (journal.Revision < 0)
        {
            throw new InvalidDataException("Initialization journal Revision cannot be negative.");
        }

        if (journal.InitializationId == Guid.Empty)
        {
            throw new InvalidDataException("Initialization journal InitializationId cannot be empty.");
        }

        if (!Enum.IsDefined(journal.Phase))
        {
            throw new InvalidDataException($"Undefined initialization phase: {journal.Phase}.");
        }
    }
}
