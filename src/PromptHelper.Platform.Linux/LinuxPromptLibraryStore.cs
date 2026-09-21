using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using PromptHelper.Models;

namespace PromptHelper.Services;

/// <summary>
/// Product-facing Linux library store. It intentionally keeps the public library/prompt file
/// format identical to the Windows application while binding every authoritative replacement
/// to the Linux CAS/recovery primitives.
/// </summary>
public sealed class LinuxPromptLibraryStore
{
    private readonly AppPaths _paths;
    private readonly LinuxDurableAtomicFileWriter _writer = new();
    private readonly LinuxOwnedArtifactJournal _journal = new();
    private readonly LinuxAtomicExpectedFileReplacer _replacer;

    private LibraryDocument _document =
        new() { SchemaVersion = LibraryDocument.CurrentSchemaVersion };
    private string? _rawSha256Hex;

    public LinuxPromptLibraryStore(string root)
    {
        LinuxNativeFileSystem.EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        _paths = new AppPaths(Path.GetFullPath(root));
        _replacer = new LinuxAtomicExpectedFileReplacer(_journal);
    }

    public string RootDirectory => _paths.RootDirectory;

    public LibraryDocument CurrentDocument =>
        LibraryDocumentCloner.Clone(_document);

    public IReadOnlyList<string> StartupWarnings { get; private set; } = [];

    public void Initialize()
    {
        _paths.EnsureDataDirectories();

        var warnings = new List<string>();
        ReconcileOrThrow(warnings);

        if (!File.Exists(_paths.LibraryPath))
        {
            if (File.Exists(_paths.LibraryBackupPath))
            {
                LoadFromBackupAndRestore(warnings);
            }
            else
            {
                CreateFreshDefaults(warnings);
            }
        }
        else
        {
            try
            {
                LoadPrimary();
            }
            catch (Exception primaryError) when (
                primaryError is InvalidDataException or
                IOException or
                UnauthorizedAccessException)
            {
                if (IsFutureSchema(_paths.LibraryPath))
                {
                    throw;
                }

                if (!File.Exists(_paths.LibraryBackupPath))
                {
                    throw new InvalidDataException(
                        "The primary library is unreadable/corrupt and no backup is available.",
                        primaryError);
                }

                LoadFromBackupAndRestore(warnings);
                warnings.Add(
                    "The primary library was recovered from library.backup.json.");
            }
        }

        InstallPremadesIfNeeded(warnings);
        DetectUnreferencedPromptBodies(warnings);

        StartupWarnings = warnings;
    }

    public IReadOnlyList<CategoryRecord> GetCategories(Guid? parentId) =>
        _document.Categories
            .Where(category => category.ParentId == parentId)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(category => category.Id)
            .Select(CloneCategory)
            .ToArray();

    public IReadOnlyList<PromptRecord> GetPrompts(Guid? categoryId) =>
        _document.Prompts
            .Where(prompt => prompt.CategoryId == categoryId)
            .OrderBy(prompt => prompt.SortOrder)
            .ThenBy(prompt => prompt.Id)
            .Select(ClonePrompt)
            .ToArray();

    public IReadOnlyList<BreadcrumbRecord> GetBreadcrumbs(Guid? categoryId)
    {
        var result = new List<BreadcrumbRecord>
        {
            new(null, "Home")
        };

        if (categoryId is null)
        {
            return result;
        }

        var byId = _document.Categories.ToDictionary(category => category.Id);
        var chain = new Stack<CategoryRecord>();
        Guid current = categoryId.Value;
        var visited = new HashSet<Guid>();

        while (byId.TryGetValue(current, out CategoryRecord? category))
        {
            if (!visited.Add(current))
            {
                throw new InvalidDataException("Category hierarchy contains a cycle.");
            }

            chain.Push(category);
            if (category.ParentId is not Guid parent)
            {
                break;
            }

            current = parent;
        }

        foreach (CategoryRecord category in chain)
        {
            result.Add(new BreadcrumbRecord(category.Id, category.Name));
        }

        return result;
    }

    public IReadOnlyList<DestinationRecord> GetDestinations()
    {
        var result = new List<DestinationRecord>
        {
            new(null, "Home")
        };

        foreach (CategoryRecord category in
                 _document.Categories
                     .OrderBy(category => GetCategoryPath(category.Id),
                         StringComparer.OrdinalIgnoreCase))
        {
            result.Add(new DestinationRecord(
                category.Id,
                GetCategoryPath(category.Id)));
        }

        return result;
    }

    public string ReadPrompt(Guid promptId) =>
        StrictUtf8Text.ReadAllText(
            _paths.GetPromptPath(promptId),
            $"prompt body '{promptId}'");

    public string ReadPromptPreview(Guid promptId, int maxCharacters = 700)
    {
        string content = ReadPrompt(promptId);
        if (content.Length <= maxCharacters)
        {
            return content;
        }

        return content[..maxCharacters] + "…";
    }

    public OperationResult<CategoryRecord> CreateCategory(
        Guid? parentId,
        string name)
    {
        string trimmed = (name ?? string.Empty).Trim();

        if (parentId.HasValue &&
            !_document.Categories.Any(category => category.Id == parentId.Value))
        {
            throw new InvalidOperationException("Parent category no longer exists.");
        }

        string? validation = LibraryValidator.ValidateCategoryNameInput(
            trimmed,
            _document.Categories
                .Where(category => category.ParentId == parentId)
                .Select(category => category.Name));
        if (validation is not null)
        {
            throw new InvalidOperationException(validation);
        }

        LibraryDocument candidate = LibraryDocumentCloner.Clone(_document);
        var category = new CategoryRecord
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            Name = trimmed,
            SortOrder = NextCategorySortOrder(candidate, parentId)
        };
        candidate.Categories.Add(category);

        string? warning = CommitMetadata(candidate);
        return new OperationResult<CategoryRecord>(
            CloneCategory(category),
            warning);
    }

    public OperationResult RenameCategory(Guid categoryId, string newName)
    {
        LibraryDocument candidate = LibraryDocumentCloner.Clone(_document);
        CategoryRecord category = candidate.Categories
            .SingleOrDefault(item => item.Id == categoryId)
            ?? throw new InvalidOperationException("Category no longer exists.");

        string trimmed = (newName ?? string.Empty).Trim();
        string? validation = LibraryValidator.ValidateCategoryNameInput(
            trimmed,
            candidate.Categories
                .Where(item =>
                    item.Id != categoryId &&
                    item.ParentId == category.ParentId)
                .Select(item => item.Name));
        if (validation is not null)
        {
            throw new InvalidOperationException(validation);
        }

        category.Name = trimmed;
        return new OperationResult(CommitMetadata(candidate));
    }

    public OperationResult DeleteCategory(Guid categoryId)
    {
        if (_document.Categories.Any(category => category.ParentId == categoryId) ||
            _document.Prompts.Any(prompt => prompt.CategoryId == categoryId))
        {
            throw new InvalidOperationException(
                "This category is not empty. Move or delete its contents first.");
        }

        LibraryDocument candidate = LibraryDocumentCloner.Clone(_document);
        CategoryRecord category = candidate.Categories
            .SingleOrDefault(item => item.Id == categoryId)
            ?? throw new InvalidOperationException("Category no longer exists.");
        candidate.Categories.Remove(category);

        return new OperationResult(CommitMetadata(candidate));
    }

    public OperationResult<PromptRecord> CreatePrompt(
        Guid? categoryId,
        string content,
        string? title)
    {
        ArgumentNullException.ThrowIfNull(content);
        ValidatePromptDestination(categoryId);

        string? normalizedTitle = NormalizePromptTitle(title);
        Guid id = Guid.NewGuid();
        string path = _paths.GetPromptPath(id);
        byte[] bytes = StrictUtf8Text.Encode(content);

        CreateBodyNoOverwrite(path, bytes);

        FileObjectIdentity createdIdentity =
            CaptureIdentity(path);
        string createdHash =
            Convert.ToHexStringLower(SHA256.HashData(bytes));

        LibraryDocument candidate = LibraryDocumentCloner.Clone(_document);
        var prompt = new PromptRecord
        {
            Id = id,
            CategoryId = categoryId,
            Title = normalizedTitle,
            SortOrder = NextPromptSortOrder(candidate, categoryId)
        };
        candidate.Prompts.Add(prompt);

        try
        {
            string? warning = CommitMetadata(candidate);
            return new OperationResult<PromptRecord>(
                ClonePrompt(prompt),
                warning);
        }
        catch
        {
            _ = LinuxExactFileRetirement.Retire(
                _paths.RootDirectory,
                path,
                createdIdentity,
                createdHash,
                bytes.LongLength);
            throw;
        }
    }

    public OperationResult EditPrompt(
        Guid promptId,
        string content,
        string? title)
    {
        ArgumentNullException.ThrowIfNull(content);

        PromptRecord existing = _document.Prompts
            .SingleOrDefault(prompt => prompt.Id == promptId)
            ?? throw new InvalidOperationException("Prompt no longer exists.");

        string path = _paths.GetPromptPath(promptId);
        byte[] oldBytes = File.ReadAllBytes(path);
        string oldHash =
            Convert.ToHexStringLower(SHA256.HashData(oldBytes));
        byte[] newBytes = StrictUtf8Text.Encode(content);
        string? normalizedTitle = NormalizePromptTitle(title);

        bool bodyChanged = !oldBytes.AsSpan().SequenceEqual(newBytes);
        bool titleChanged = !string.Equals(
            existing.Title,
            normalizedTitle,
            StringComparison.Ordinal);

        if (!bodyChanged && !titleChanged)
        {
            return new OperationResult();
        }

        if (bodyChanged)
        {
            _replacer.ReplaceIfExpected(
                _paths.RootDirectory,
                path,
                ExpectedFileState.Present(oldHash),
                newBytes,
                DurableFileClass.PromptBody);
            ReconcileOrThrow();
        }

        if (!titleChanged)
        {
            return new OperationResult();
        }

        LibraryDocument candidate = LibraryDocumentCloner.Clone(_document);
        PromptRecord prompt = candidate.Prompts.Single(item => item.Id == promptId);
        prompt.Title = normalizedTitle;

        try
        {
            return new OperationResult(CommitMetadata(candidate));
        }
        catch (Exception metadataError) when (bodyChanged)
        {
            try
            {
                string newHash =
                    Convert.ToHexStringLower(SHA256.HashData(newBytes));
                _replacer.ReplaceIfExpected(
                    _paths.RootDirectory,
                    path,
                    ExpectedFileState.Present(newHash),
                    oldBytes,
                    DurableFileClass.PromptBody);
                ReconcileOrThrow();
            }
            catch (Exception rollbackError)
            {
                throw new IOException(
                    "Prompt body was updated, metadata failed, and the previous body could not be restored safely. Restart Prompt Helper before making another change.",
                    new AggregateException(metadataError, rollbackError));
            }

            throw;
        }
    }

    public OperationResult DeletePrompt(Guid promptId)
    {
        PromptRecord existing = _document.Prompts
            .SingleOrDefault(prompt => prompt.Id == promptId)
            ?? throw new InvalidOperationException("Prompt no longer exists.");

        string path = _paths.GetPromptPath(promptId);
        FileObjectIdentity identity = CaptureIdentity(path);
        byte[] bytes = File.ReadAllBytes(path);
        string hash =
            Convert.ToHexStringLower(SHA256.HashData(bytes));

        LibraryDocument candidate = LibraryDocumentCloner.Clone(_document);
        candidate.Prompts.RemoveAll(prompt => prompt.Id == promptId);

        string? warning = CommitMetadata(candidate);

        LinuxExactRetirementOutcome outcome =
            LinuxExactFileRetirement.Retire(
                _paths.RootDirectory,
                path,
                identity,
                hash,
                bytes.LongLength);

        if (outcome is
            LinuxExactRetirementOutcome.ForeignPreserved or
            LinuxExactRetirementOutcome.ConflictPreserved)
        {
            warning = CombineWarnings(
                warning,
                "The prompt was removed from the library, but its body file changed concurrently and was preserved.");
        }

        return new OperationResult(warning);
    }

    public OperationResult MovePrompt(
        Guid promptId,
        Guid? destinationCategoryId)
    {
        ValidatePromptDestination(destinationCategoryId);

        LibraryDocument candidate = LibraryDocumentCloner.Clone(_document);
        PromptRecord prompt = candidate.Prompts
            .SingleOrDefault(item => item.Id == promptId)
            ?? throw new InvalidOperationException("Prompt no longer exists.");

        prompt.CategoryId = destinationCategoryId;
        prompt.SortOrder =
            NextPromptSortOrder(candidate, destinationCategoryId, promptId);

        return new OperationResult(CommitMetadata(candidate));
    }

    public OperationResult<PromptRecord> DuplicatePrompt(
        Guid promptId,
        Guid? destinationCategoryId)
    {
        ValidatePromptDestination(destinationCategoryId);

        PromptRecord source = _document.Prompts
            .SingleOrDefault(prompt => prompt.Id == promptId)
            ?? throw new InvalidOperationException("Prompt no longer exists.");

        byte[] body = File.ReadAllBytes(_paths.GetPromptPath(promptId));
        Guid newId = Guid.NewGuid();
        string newPath = _paths.GetPromptPath(newId);

        CreateBodyNoOverwrite(newPath, body);
        FileObjectIdentity identity = CaptureIdentity(newPath);
        string hash =
            Convert.ToHexStringLower(SHA256.HashData(body));

        LibraryDocument candidate = LibraryDocumentCloner.Clone(_document);
        var duplicate = new PromptRecord
        {
            Id = newId,
            CategoryId = destinationCategoryId,
            Title = source.Title,
            SortOrder = NextPromptSortOrder(candidate, destinationCategoryId)
        };
        candidate.Prompts.Add(duplicate);

        try
        {
            string? warning = CommitMetadata(candidate);
            return new OperationResult<PromptRecord>(
                ClonePrompt(duplicate),
                warning);
        }
        catch
        {
            _ = LinuxExactFileRetirement.Retire(
                _paths.RootDirectory,
                newPath,
                identity,
                hash,
                body.LongLength);
            throw;
        }
    }

    private void CreateFreshDefaults(List<string> warnings)
    {
        DefaultLibraryPackage package = DefaultLibraryFactory.CreateDefaults();

        foreach ((Guid id, string content) in package.PromptContents)
        {
            string path = _paths.GetPromptPath(id);
            byte[] expected = StrictUtf8Text.Encode(content);

            if (File.Exists(path))
            {
                byte[] existing = File.ReadAllBytes(path);
                if (!existing.AsSpan().SequenceEqual(expected))
                {
                    throw new InvalidDataException(
                        $"Cannot initialize Prompt Helper because '{Path.GetFileName(path)}' already exists with unexpected content.");
                }
                continue;
            }

            CreateBodyNoOverwrite(path, expected);
        }

        byte[] libraryBytes = Serialize(package.Document);
        _replacer.ReplaceIfExpected(
            _paths.RootDirectory,
            _paths.LibraryPath,
            ExpectedFileState.Missing,
            libraryBytes,
            DurableFileClass.LibraryMetadata);
        ReconcileOrThrow(warnings);

        _document = LibraryDocumentCloner.Clone(package.Document);
        _rawSha256Hex =
            Convert.ToHexStringLower(SHA256.HashData(libraryBytes));
        SynchronizeBackup(libraryBytes, warnings);
    }

    private void InstallPremadesIfNeeded(List<string> warnings)
    {
        if (_document.PremadePackVersion >= PremadePromptCatalog.CurrentPackVersion)
        {
            return;
        }

        LibraryDocument candidate = LibraryDocumentCloner.Clone(_document);
        DefaultLibraryPackage pack = PremadePromptCatalog.CreatePackage();
        var categoryMap = new Dictionary<Guid, Guid>();

        foreach (PremadeCategoryDefinition definition in PremadePromptCatalog.Categories)
        {
            Guid? actualParent = definition.ParentId.HasValue
                ? categoryMap[definition.ParentId.Value]
                : null;

            CategoryRecord? sameId =
                candidate.Categories.SingleOrDefault(item => item.Id == definition.Id);
            if (sameId is not null)
            {
                categoryMap[definition.Id] = sameId.Id;
                continue;
            }

            CategoryRecord? sameName =
                candidate.Categories.SingleOrDefault(item =>
                    item.ParentId == actualParent &&
                    string.Equals(
                        item.Name,
                        definition.Name,
                        StringComparison.OrdinalIgnoreCase));

            if (sameName is not null)
            {
                categoryMap[definition.Id] = sameName.Id;
                continue;
            }

            var category = new CategoryRecord
            {
                Id = definition.Id,
                ParentId = actualParent,
                Name = definition.Name,
                SortOrder = definition.SortOrder
            };
            candidate.Categories.Add(category);
            categoryMap[definition.Id] = category.Id;
        }

        foreach (PremadePromptDefinition definition in PremadePromptCatalog.Prompts)
        {
            if (candidate.Prompts.Any(prompt => prompt.Id == definition.Id))
            {
                continue;
            }

            string content = pack.PromptContents[definition.Id];
            string path = _paths.GetPromptPath(definition.Id);
            byte[] expected = StrictUtf8Text.Encode(content);

            if (File.Exists(path))
            {
                byte[] existing = File.ReadAllBytes(path);
                if (!existing.AsSpan().SequenceEqual(expected))
                {
                    throw new InvalidDataException(
                        $"Cannot install Premades because '{Path.GetFileName(path)}' already exists with different content.");
                }
            }
            else
            {
                CreateBodyNoOverwrite(path, expected);
            }

            candidate.Prompts.Add(new PromptRecord
            {
                Id = definition.Id,
                CategoryId = categoryMap[definition.CategoryId],
                Title = definition.Title,
                SortOrder = definition.SortOrder
            });
        }

        candidate.PremadePackVersion = PremadePromptCatalog.CurrentPackVersion;
        string? warning = CommitMetadata(candidate);
        if (warning is not null)
        {
            warnings.Add(warning);
        }
    }

    private void LoadPrimary()
    {
        byte[] raw = File.ReadAllBytes(_paths.LibraryPath);
        _document = Deserialize(raw, _paths.LibraryPath);
        _rawSha256Hex =
            Convert.ToHexStringLower(SHA256.HashData(raw));
    }

    private void LoadFromBackupAndRestore(List<string> warnings)
    {
        byte[] backup = File.ReadAllBytes(_paths.LibraryBackupPath);
        LibraryDocument document =
            Deserialize(backup, _paths.LibraryBackupPath);
        byte[] canonical = Serialize(document);

        if (File.Exists(_paths.LibraryPath))
        {
            _writer.ReplaceDurable(
                _paths.LibraryPath,
                canonical,
                DurableFileClass.LibraryMetadata);
        }
        else
        {
            _writer.CreateNewDurable(
                _paths.LibraryPath,
                canonical,
                DurableFileClass.LibraryMetadata);
        }

        _document = document;
        _rawSha256Hex =
            Convert.ToHexStringLower(SHA256.HashData(canonical));
        warnings.Add("Library metadata was restored from the valid backup.");
    }

    private string? CommitMetadata(LibraryDocument candidate)
    {
        LibraryValidator.Validate(candidate);

        byte[] bytes = Serialize(candidate);
        ExpectedFileState expected = _rawSha256Hex is null
            ? ExpectedFileState.Missing
            : ExpectedFileState.Present(_rawSha256Hex);

        try
        {
            _replacer.ReplaceIfExpected(
                _paths.RootDirectory,
                _paths.LibraryPath,
                expected,
                bytes,
                DurableFileClass.LibraryMetadata);
        }
        catch (StaleExpectedFileException ex)
        {
            throw new InvalidOperationException(
                "The library changed outside Prompt Helper. Reload before editing.",
                ex);
        }

        ReconcileOrThrow();

        _document = LibraryDocumentCloner.Clone(candidate);
        _rawSha256Hex =
            Convert.ToHexStringLower(SHA256.HashData(bytes));

        var warnings = new List<string>();
        SynchronizeBackup(bytes, warnings);
        return warnings.Count == 0
            ? null
            : string.Join(Environment.NewLine, warnings);
    }

    private void SynchronizeBackup(
        byte[] bytes,
        List<string> warnings)
    {
        try
        {
            if (File.Exists(_paths.LibraryBackupPath) &&
                IsFutureSchema(_paths.LibraryBackupPath))
            {
                warnings.Add(
                    "library.backup.json uses a newer schema and was preserved.");
                return;
            }

            if (File.Exists(_paths.LibraryBackupPath))
            {
                _writer.ReplaceDurable(
                    _paths.LibraryBackupPath,
                    bytes,
                    DurableFileClass.LibraryMetadata);
            }
            else
            {
                _writer.CreateNewDurable(
                    _paths.LibraryBackupPath,
                    bytes,
                    DurableFileClass.LibraryMetadata);
            }
        }
        catch (Exception ex)
        {
            warnings.Add(
                $"The library was saved, but its backup could not be synchronized: {ex.Message}");
        }
    }

    private void CreateBodyNoOverwrite(
        string path,
        ReadOnlySpan<byte> bytes)
    {
        _replacer.ReplaceIfExpected(
            _paths.RootDirectory,
            path,
            ExpectedFileState.Missing,
            bytes,
            DurableFileClass.PromptBody);
        ReconcileOrThrow();
    }

    private void ReconcileOrThrow(
        List<string>? warnings = null)
    {
        LinuxOwnedArtifactReconciler.Result result =
            LinuxOwnedArtifactReconciler.Reconcile(
                _paths.RootDirectory,
                _journal);

        if (result.HasFatal)
        {
            string details = string.Join(
                Environment.NewLine,
                result.Outcomes
                    .Where(outcome =>
                        outcome.Severity == ReconciliationSeverity.Fatal)
                    .Select(outcome =>
                        $"[{outcome.Code}] {outcome.Message}"));

            throw new InvalidDataException(
                "Linux recovery found unresolved durable state and stopped before loading the library." +
                Environment.NewLine + details);
        }

        if (warnings is not null)
        {
            warnings.AddRange(
                result.Outcomes
                    .Where(outcome =>
                        outcome.Severity == ReconciliationSeverity.Warning)
                    .Select(outcome => outcome.Message));
        }
    }

    private void DetectUnreferencedPromptBodies(List<string> warnings)
    {
        HashSet<string> referenced = _document.Prompts
            .Select(prompt => $"{prompt.Id:N}.md")
            .ToHashSet(StringComparer.Ordinal);

        string[] orphanNames = Directory
            .EnumerateFiles(_paths.PromptsDirectory, "*.md")
            .Select(Path.GetFileName)
            .Where(name =>
                name is not null &&
                !referenced.Contains(name))
            .Cast<string>()
            .ToArray();

        if (orphanNames.Length != 0)
        {
            warnings.Add(
                $"{orphanNames.Length} unreferenced prompt body file(s) were preserved in the prompts folder. Prompt Helper never deletes an unproven orphan automatically.");
        }
    }

    private static LibraryDocument Deserialize(
        byte[] raw,
        string path)
    {
        string json =
            StrictUtf8Text.Decode(raw, $"library metadata '{path}'");

        JsonDocument inspection;
        try
        {
            inspection = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Library '{path}' is not valid JSON.",
                ex);
        }

        using (inspection)
        {
        if (!inspection.RootElement.TryGetProperty(
                "schemaVersion",
                out JsonElement schemaElement) ||
            schemaElement.ValueKind != JsonValueKind.Number ||
            !schemaElement.TryGetInt32(out int schemaVersion))
        {
            throw new InvalidDataException(
                $"Library '{path}' has no valid schemaVersion.");
        }

        if (schemaVersion > LibraryDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Library '{path}' uses newer schema version {schemaVersion}. This build supports {LibraryDocument.CurrentSchemaVersion}.");
        }

        LibraryDocument document =
            JsonSerializer.Deserialize<LibraryDocument>(
                json,
                LibraryJson.Options)
            ?? throw new InvalidDataException(
                $"Library '{path}' could not be deserialized.");

        LibraryValidator.Validate(document);
        return document;
        }
    }

    private static bool IsFutureSchema(string path)
    {
        try
        {
            string json =
                StrictUtf8Text.ReadAllText(
                    path,
                    $"library metadata '{path}'");
            using JsonDocument document = JsonDocument.Parse(json);

            return document.RootElement.TryGetProperty(
                       "schemaVersion",
                       out JsonElement schema) &&
                   schema.ValueKind == JsonValueKind.Number &&
                   schema.TryGetInt32(out int version) &&
                   version > LibraryDocument.CurrentSchemaVersion;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Serialize(LibraryDocument document)
    {
        LibraryValidator.Validate(document);
        return StrictUtf8Text.Encode(
            LibraryJson.SerializeCanonical(document));
    }

    private void ValidatePromptDestination(Guid? categoryId)
    {
        if (categoryId.HasValue &&
            !_document.Categories.Any(category => category.Id == categoryId.Value))
        {
            throw new InvalidOperationException(
                "Destination category no longer exists.");
        }
    }

    private string GetCategoryPath(Guid categoryId)
    {
        var byId = _document.Categories.ToDictionary(category => category.Id);
        var names = new Stack<string>();
        Guid current = categoryId;
        var visited = new HashSet<Guid>();

        while (byId.TryGetValue(current, out CategoryRecord? category))
        {
            if (!visited.Add(current))
            {
                throw new InvalidDataException(
                    "Category hierarchy contains a cycle.");
            }

            names.Push(category.Name);
            if (category.ParentId is not Guid parent)
            {
                break;
            }

            current = parent;
        }

        return string.Join(" / ", names);
    }

    private static string? NormalizePromptTitle(string? title)
    {
        string trimmed = (title ?? string.Empty).Trim();
        string? validation = LibraryValidator.ValidatePromptTitleInput(trimmed);
        if (validation is not null)
        {
            throw new InvalidOperationException(validation);
        }

        return trimmed.Length == 0
            ? null
            : trimmed;
    }

    private static long NextCategorySortOrder(
        LibraryDocument document,
        Guid? parentId)
    {
        long max = document.Categories
            .Where(category => category.ParentId == parentId)
            .Select(category => category.SortOrder)
            .DefaultIfEmpty(0)
            .Max();

        return checked(max + 10);
    }

    private static long NextPromptSortOrder(
        LibraryDocument document,
        Guid? categoryId,
        Guid? excluding = null)
    {
        long max = document.Prompts
            .Where(prompt =>
                prompt.CategoryId == categoryId &&
                (!excluding.HasValue || prompt.Id != excluding.Value))
            .Select(prompt => prompt.SortOrder)
            .DefaultIfEmpty(0)
            .Max();

        return checked(max + 10);
    }

    private static FileObjectIdentity CaptureIdentity(string path)
    {
        using SafeFileHandle handle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        return LinuxFileIdentity
            .FromHandle(handle)
            .ToObjectIdentity();
    }

    private static CategoryRecord CloneCategory(CategoryRecord source) =>
        new()
        {
            Id = source.Id,
            ParentId = source.ParentId,
            Name = source.Name,
            SortOrder = source.SortOrder
        };

    private static PromptRecord ClonePrompt(PromptRecord source) =>
        new()
        {
            Id = source.Id,
            CategoryId = source.CategoryId,
            Title = source.Title,
            SortOrder = source.SortOrder
        };

    private static string CombineWarnings(
        string? first,
        string second) =>
        string.IsNullOrWhiteSpace(first)
            ? second
            : first + Environment.NewLine + second;
}
