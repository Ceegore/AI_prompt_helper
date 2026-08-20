using System.IO;
using PromptHelper.Infrastructure;
using PromptHelper.Models;

namespace PromptHelper.Services;

public sealed class PromptLibraryService
{
    private readonly LibraryRepository _libraryRepo;
    private readonly PromptRepository _promptRepo;

    public PromptLibraryService(
        LibraryDocument initialDocument,
        LibraryRepository libraryRepo,
        PromptRepository promptRepo)
    {
        ArgumentNullException.ThrowIfNull(initialDocument);
        _libraryRepo = libraryRepo ?? throw new ArgumentNullException(nameof(libraryRepo));
        _promptRepo = promptRepo ?? throw new ArgumentNullException(nameof(promptRepo));

        LibraryValidator.Validate(initialDocument);
        CurrentDocument = initialDocument;
    }

    public LibraryDocument CurrentDocument { get; private set; }

    #region Category Operations

    public OperationResult<CategoryRecord> CreateCategory(Guid? parentId, string rawName)
    {
        ArgumentNullException.ThrowIfNull(rawName);
        string trimmedName = rawName.Trim();

        ValidateParentExists(parentId);
        ValidateSiblingCategoryNameUniqueness(parentId, trimmedName, null);

        var candidate = LibraryDocumentCloner.Clone(CurrentDocument);
        Guid categoryId = Guid.NewGuid();
        long sortOrder = CalculateNextCategorySortOrder(candidate, parentId);

        var newCategory = new CategoryRecord
        {
            Id = categoryId,
            ParentId = parentId,
            Name = trimmedName,
            SortOrder = sortOrder
        };

        candidate.Categories.Add(newCategory);
        LibraryValidator.Validate(candidate);

        CommitResult commitResult = _libraryRepo.Commit(candidate);
        CurrentDocument = candidate;

        return new OperationResult<CategoryRecord>(newCategory, commitResult.Warning);
    }

    public OperationResult RenameCategory(Guid categoryId, string newRawName)
    {
        ArgumentNullException.ThrowIfNull(newRawName);
        string trimmedName = newRawName.Trim();

        CategoryRecord existing = GetCategoryOrThrow(categoryId);
        ValidateSiblingCategoryNameUniqueness(existing.ParentId, trimmedName, categoryId);

        var candidate = LibraryDocumentCloner.Clone(CurrentDocument);
        var target = candidate.Categories.First(c => c.Id == categoryId);
        target.Name = trimmedName;

        LibraryValidator.Validate(candidate);
        CommitResult commitResult = _libraryRepo.Commit(candidate);
        CurrentDocument = candidate;

        return new OperationResult(commitResult.Warning);
    }

    public OperationResult DeleteCategory(Guid categoryId)
    {
        CategoryRecord existing = GetCategoryOrThrow(categoryId);

        // Reject if has child categories or prompts
        if (CurrentDocument.Categories.Any(c => c.ParentId == categoryId))
        {
            throw new InvalidOperationException("This category is not empty. Move or delete its subcategories first.");
        }

        if (CurrentDocument.Prompts.Any(p => p.CategoryId == categoryId))
        {
            throw new InvalidOperationException("This category is not empty. Move or delete its prompts first.");
        }

        var candidate = LibraryDocumentCloner.Clone(CurrentDocument);
        candidate.Categories.RemoveAll(c => c.Id == categoryId);

        LibraryValidator.Validate(candidate);
        CommitResult commitResult = _libraryRepo.Commit(candidate);
        CurrentDocument = candidate;

        return new OperationResult(commitResult.Warning);
    }

    #endregion

    #region Prompt Operations

    public OperationResult<PromptRecord> CreatePrompt(Guid? categoryId, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ValidateParentExists(categoryId);

        Guid promptId = GenerateUniquePromptGuid();
        _promptRepo.Create(promptId, content);

        var candidate = LibraryDocumentCloner.Clone(CurrentDocument);
        long sortOrder = CalculateNextPromptSortOrder(candidate, categoryId);

        var newPrompt = new PromptRecord
        {
            Id = promptId,
            CategoryId = categoryId,
            SortOrder = sortOrder
        };

        candidate.Prompts.Add(newPrompt);
        LibraryValidator.Validate(candidate);

        CommitResult commitResult;
        try
        {
            commitResult = _libraryRepo.Commit(candidate);
        }
        catch
        {
            try
            {
                _promptRepo.DeleteIfExists(promptId);
            }
            catch
            {
                // Best effort orphan cleanup
            }

            throw;
        }

        CurrentDocument = candidate;
        return new OperationResult<PromptRecord>(newPrompt, commitResult.Warning);
    }

    public OperationResult EditPrompt(Guid promptId, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        GetPromptOrThrow(promptId);

        if (!_promptRepo.Exists(promptId))
        {
            throw new FileNotFoundException("Prompt file does not exist.", _promptRepo.ToString());
        }

        _promptRepo.Update(promptId, content);
        return new OperationResult(null);
    }

    public OperationResult DeletePrompt(Guid promptId)
    {
        GetPromptOrThrow(promptId);

        var candidate = LibraryDocumentCloner.Clone(CurrentDocument);
        candidate.Prompts.RemoveAll(p => p.Id == promptId);
        LibraryValidator.Validate(candidate);

        CommitResult commitResult = _libraryRepo.Commit(candidate);
        CurrentDocument = candidate;

        if (!commitResult.BackupSynchronized)
        {
            // Primary committed, backup failed -> retain .md file to prevent backup restore corruption
            return new OperationResult(commitResult.Warning);
        }

        try
        {
            _promptRepo.DeleteIfExists(promptId);
            return new OperationResult(null);
        }
        catch (Exception)
        {
            return new OperationResult(
                "The prompt was removed from the library, but its old .md file could not be deleted. " +
                "The file was left in the data folder.");
        }
    }

    public OperationResult MovePrompt(Guid promptId, Guid? destinationCategoryId)
    {
        PromptRecord existing = GetPromptOrThrow(promptId);
        ValidateParentExists(destinationCategoryId);

        if (existing.CategoryId == destinationCategoryId)
        {
            return new OperationResult(null);
        }

        var candidate = LibraryDocumentCloner.Clone(CurrentDocument);
        var target = candidate.Prompts.First(p => p.Id == promptId);
        target.CategoryId = destinationCategoryId;
        target.SortOrder = CalculateNextPromptSortOrder(candidate, destinationCategoryId);

        LibraryValidator.Validate(candidate);
        CommitResult commitResult = _libraryRepo.Commit(candidate);
        CurrentDocument = candidate;

        return new OperationResult(commitResult.Warning);
    }

    public OperationResult<PromptRecord> DuplicatePrompt(Guid sourcePromptId, Guid? destinationCategoryId)
    {
        GetPromptOrThrow(sourcePromptId);
        ValidateParentExists(destinationCategoryId);

        if (!_promptRepo.Exists(sourcePromptId))
        {
            throw new InvalidOperationException("Cannot duplicate unavailable prompt.");
        }

        string sourceContent = _promptRepo.Read(sourcePromptId);
        Guid newPromptId = GenerateUniquePromptGuid();

        _promptRepo.Create(newPromptId, sourceContent);

        var candidate = LibraryDocumentCloner.Clone(CurrentDocument);
        long sortOrder = CalculateNextPromptSortOrder(candidate, destinationCategoryId);

        var newPrompt = new PromptRecord
        {
            Id = newPromptId,
            CategoryId = destinationCategoryId,
            SortOrder = sortOrder
        };

        candidate.Prompts.Add(newPrompt);
        LibraryValidator.Validate(candidate);

        CommitResult commitResult;
        try
        {
            commitResult = _libraryRepo.Commit(candidate);
        }
        catch
        {
            try
            {
                _promptRepo.DeleteIfExists(newPromptId);
            }
            catch
            {
                // Best effort cleanup
            }

            throw;
        }

        CurrentDocument = candidate;
        return new OperationResult<PromptRecord>(newPrompt, commitResult.Warning);
    }

    #endregion

    #region Query and Navigation

    public IReadOnlyList<CategoryRecord> GetCategories(Guid? parentId)
    {
        return CurrentDocument.Categories
            .Where(c => c.ParentId == parentId)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Id)
            .ToList();
    }

    public IReadOnlyList<PromptDisplayRecord> GetPrompts(Guid? categoryId)
    {
        var prompts = CurrentDocument.Prompts
            .Where(p => p.CategoryId == categoryId)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .ToList();

        var displayList = new List<PromptDisplayRecord>(prompts.Count);
        foreach (var prompt in prompts)
        {
            try
            {
                if (_promptRepo.Exists(prompt.Id))
                {
                    string content = _promptRepo.Read(prompt.Id);
                    displayList.Add(new PromptDisplayRecord(prompt.Id, content, true, null));
                }
                else
                {
                    displayList.Add(new PromptDisplayRecord(prompt.Id, string.Empty, false, "Prompt file not found."));
                }
            }
            catch (Exception ex)
            {
                displayList.Add(new PromptDisplayRecord(prompt.Id, string.Empty, false, ex.Message));
            }
        }

        return displayList;
    }

    public IReadOnlyList<(Guid? CategoryId, string Name)> GetBreadcrumbs(Guid? categoryId)
    {
        var breadcrumbs = new List<(Guid? CategoryId, string Name)>
        {
            (null, "Home")
        };

        if (!categoryId.HasValue)
        {
            return breadcrumbs;
        }

        var categoriesById = CurrentDocument.Categories.ToDictionary(c => c.Id);
        var chain = new List<CategoryRecord>();
        Guid? current = categoryId;

        while (current.HasValue)
        {
            if (categoriesById.TryGetValue(current.Value, out var cat))
            {
                chain.Add(cat);
                current = cat.ParentId;
            }
            else
            {
                break;
            }
        }

        chain.Reverse();
        foreach (var cat in chain)
        {
            breadcrumbs.Add((cat.Id, cat.Name));
        }

        return breadcrumbs;
    }

    public IReadOnlyList<DestinationRecord> GetDestinations()
    {
        var rawDestinations = new List<(Guid? Id, string Path)>
        {
            (null, "Home")
        };

        var categoriesById = CurrentDocument.Categories.ToDictionary(c => c.Id);

        foreach (var category in CurrentDocument.Categories)
        {
            var parts = new List<string>();
            CategoryRecord? current = category;
            while (current != null)
            {
                parts.Add(current.Name);
                current = current.ParentId.HasValue && categoriesById.TryGetValue(current.ParentId.Value, out var parent)
                    ? parent
                    : null;
            }

            parts.Reverse();
            string path = string.Join(" > ", parts);
            rawDestinations.Add((category.Id, path));
        }

        // Detect collisions case-insensitively
        var grouped = rawDestinations.GroupBy(d => d.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var result = new List<DestinationRecord>();

        foreach (var group in grouped)
        {
            var list = group.ToList();
            if (list.Count == 1)
            {
                result.Add(new DestinationRecord(list[0].Id, list[0].Path));
            }
            else
            {
                foreach (var item in list)
                {
                    if (!item.Id.HasValue)
                    {
                        result.Add(new DestinationRecord(null, item.Path));
                    }
                    else
                    {
                        string suffix = item.Id.Value.ToString("N")[..8];
                        result.Add(new DestinationRecord(item.Id, $"{item.Path} [{suffix}]"));
                    }
                }
            }
        }

        // Sort Home first, others by display path
        var home = result.First(r => !r.CategoryId.HasValue);
        var others = result.Where(r => r.CategoryId.HasValue).OrderBy(r => r.DisplayPath, StringComparer.OrdinalIgnoreCase);

        var finalOrder = new List<DestinationRecord> { home };
        finalOrder.AddRange(others);
        return finalOrder;
    }

    #endregion

    #region Helper Methods

    private CategoryRecord GetCategoryOrThrow(Guid id)
    {
        var category = CurrentDocument.Categories.FirstOrDefault(c => c.Id == id);
        if (category == null)
        {
            throw new InvalidOperationException($"Category not found: {id}");
        }

        return category;
    }

    private PromptRecord GetPromptOrThrow(Guid id)
    {
        var prompt = CurrentDocument.Prompts.FirstOrDefault(p => p.Id == id);
        if (prompt == null)
        {
            throw new InvalidOperationException($"Prompt not found: {id}");
        }

        return prompt;
    }

    private void ValidateParentExists(Guid? parentId)
    {
        if (parentId.HasValue && !CurrentDocument.Categories.Any(c => c.Id == parentId.Value))
        {
            throw new InvalidOperationException($"Parent category does not exist: {parentId.Value}");
        }
    }

    private void ValidateSiblingCategoryNameUniqueness(Guid? parentId, string trimmedName, Guid? excludeCategoryId)
    {
        string? error = LibraryValidator.ValidateCategoryNameInput(
            trimmedName,
            CurrentDocument.Categories
                .Where(c => c.ParentId == parentId && (!excludeCategoryId.HasValue || c.Id != excludeCategoryId.Value))
                .Select(c => c.Name));

        if (error != null)
        {
            throw new InvalidOperationException(error);
        }
    }

    private Guid GenerateUniquePromptGuid()
    {
        for (int i = 0; i < 10; i++)
        {
            Guid candidate = Guid.NewGuid();
            if (!CurrentDocument.Prompts.Any(p => p.Id == candidate) && !_promptRepo.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Failed to generate unique prompt GUID after 10 attempts.");
    }

    private static long CalculateNextCategorySortOrder(LibraryDocument doc, Guid? parentId)
    {
        var siblings = doc.Categories
            .Where(c => c.ParentId == parentId)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Id)
            .ToList();

        if (siblings.Count == 0)
        {
            return 10;
        }

        long max = siblings.Max(c => c.SortOrder);
        if (max > long.MaxValue - 10)
        {
            long current = 10;
            foreach (var sibling in siblings)
            {
                sibling.SortOrder = current;
                current += 10;
            }

            return current;
        }

        return max + 10;
    }

    private static long CalculateNextPromptSortOrder(LibraryDocument doc, Guid? categoryId)
    {
        var siblings = doc.Prompts
            .Where(p => p.CategoryId == categoryId)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .ToList();

        if (siblings.Count == 0)
        {
            return 10;
        }

        long max = siblings.Max(p => p.SortOrder);
        if (max > long.MaxValue - 10)
        {
            long current = 10;
            foreach (var sibling in siblings)
            {
                sibling.SortOrder = current;
                current += 10;
            }

            return current;
        }

        return max + 10;
    }

    #endregion
}