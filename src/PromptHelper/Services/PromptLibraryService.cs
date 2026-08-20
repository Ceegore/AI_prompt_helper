using System.IO;
using PromptHelper.Infrastructure;
using PromptHelper.Models;

namespace PromptHelper.Services;

public sealed class PromptLibraryService
{
    private readonly LibraryRepository _libraryRepo;
    private readonly PromptRepository _promptRepo;
    private LibraryDocument _document;

    public PromptLibraryService(
        LibraryDocument initialDocument,
        LibraryRepository libraryRepo,
        PromptRepository promptRepo)
    {
        ArgumentNullException.ThrowIfNull(initialDocument);
        _libraryRepo = libraryRepo ?? throw new ArgumentNullException(nameof(libraryRepo));
        _promptRepo = promptRepo ?? throw new ArgumentNullException(nameof(promptRepo));

        LibraryValidator.Validate(initialDocument);
        _document = LibraryDocumentCloner.Clone(initialDocument);
    }

    public LibraryDocument CurrentDocument => LibraryDocumentCloner.Clone(_document);

    #region Categories

    public IReadOnlyList<CategoryRecord> GetCategories(Guid? parentId)
    {
        return _document.Categories
            .Where(c => c.ParentId == parentId)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Id)
            .Select(c => new CategoryRecord
            {
                Id = c.Id,
                ParentId = c.ParentId,
                Name = c.Name,
                SortOrder = c.SortOrder
            })
            .ToList();
    }

    public OperationResult<CategoryRecord> CreateCategory(Guid? parentId, string name)
    {
        string trimmedName = (name ?? string.Empty).Trim();

        var candidate = LibraryDocumentCloner.Clone(_document);

        if (parentId.HasValue && !candidate.Categories.Any(c => c.Id == parentId.Value))
        {
            throw new InvalidOperationException($"Parent category does not exist: {parentId.Value}");
        }

        var existingNames = candidate.Categories
            .Where(c => c.ParentId == parentId)
            .Select(c => c.Name);

        string? validationError = LibraryValidator.ValidateCategoryNameInput(trimmedName, existingNames);
        if (validationError != null)
        {
            throw new InvalidOperationException(validationError);
        }

        long nextSortOrder = CalculateNextCategorySortOrder(candidate, parentId);

        var newCategory = new CategoryRecord
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            Name = trimmedName,
            SortOrder = nextSortOrder
        };

        candidate.Categories.Add(newCategory);
        LibraryValidator.Validate(candidate);

        CommitResult commitResult = _libraryRepo.Commit(candidate);
        _document = candidate;

        var returnedCategory = new CategoryRecord
        {
            Id = newCategory.Id,
            ParentId = newCategory.ParentId,
            Name = newCategory.Name,
            SortOrder = newCategory.SortOrder
        };

        return new OperationResult<CategoryRecord>(returnedCategory, commitResult.Warning);
    }

    public OperationResult RenameCategory(Guid categoryId, string newName)
    {
        string trimmedName = (newName ?? string.Empty).Trim();

        var candidate = LibraryDocumentCloner.Clone(_document);
        var target = candidate.Categories.FirstOrDefault(c => c.Id == categoryId)
                     ?? throw new InvalidOperationException($"Category does not exist: {categoryId}");

        var existingNames = candidate.Categories
            .Where(c => c.Id != categoryId && c.ParentId == target.ParentId)
            .Select(c => c.Name);

        string? validationError = LibraryValidator.ValidateCategoryNameInput(trimmedName, existingNames);
        if (validationError != null)
        {
            throw new InvalidOperationException(validationError);
        }

        target.Name = trimmedName;
        LibraryValidator.Validate(candidate);

        CommitResult commitResult = _libraryRepo.Commit(candidate);
        _document = candidate;

        return new OperationResult(commitResult.Warning);
    }

    public bool CanDeleteCategory(Guid categoryId, out string? reason)
    {
        bool hasSubcategories = _document.Categories.Any(c => c.ParentId == categoryId);
        bool hasPrompts = _document.Prompts.Any(p => p.CategoryId == categoryId);

        if (hasSubcategories && hasPrompts)
        {
            reason = "This category is not empty.\n\nMove or delete its prompts and subcategories first.";
            return false;
        }
        if (hasSubcategories)
        {
            reason = "This category has subcategories.\n\nMove or delete its subcategories first.";
            return false;
        }
        if (hasPrompts)
        {
            reason = "This category contains prompts.\n\nMove or delete its prompts first.";
            return false;
        }

        reason = null;
        return true;
    }

    public OperationResult DeleteCategory(Guid categoryId)
    {
        var candidate = LibraryDocumentCloner.Clone(_document);
        var target = candidate.Categories.FirstOrDefault(c => c.Id == categoryId)
                     ?? throw new InvalidOperationException($"Category does not exist: {categoryId}");

        if (!CanDeleteCategory(categoryId, out string? blockReason))
        {
            throw new InvalidOperationException(blockReason);
        }

        candidate.Categories.Remove(target);
        LibraryValidator.Validate(candidate);

        CommitResult commitResult = _libraryRepo.Commit(candidate);
        _document = candidate;

        return new OperationResult(commitResult.Warning);
    }

    #endregion

    #region Prompts

    public IReadOnlyList<PromptDisplayRecord> GetPrompts(Guid? categoryId)
    {
        var prompts = _document.Prompts
            .Where(p => p.CategoryId == categoryId)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .ToList();

        var results = new List<PromptDisplayRecord>(prompts.Count);
        foreach (var p in prompts)
        {
            string content = string.Empty;
            bool isAvailable = false;
            string? loadError = null;

            try
            {
                content = _promptRepo.Read(p.Id);
                isAvailable = true;
            }
            catch (Exception ex)
            {
                loadError = ex.Message;
            }

            results.Add(new PromptDisplayRecord(p.Id, content, isAvailable, loadError));
        }

        return results;
    }

    public OperationResult<PromptRecord> CreatePrompt(Guid? categoryId, string content)
    {
        var candidate = LibraryDocumentCloner.Clone(_document);

        if (categoryId.HasValue && !candidate.Categories.Any(c => c.Id == categoryId.Value))
        {
            throw new InvalidOperationException($"Category does not exist: {categoryId.Value}");
        }

        var newPromptId = GenerateUniquePromptGuid(candidate);
        long nextSortOrder = CalculateNextPromptSortOrder(candidate, categoryId, null);

        var newPrompt = new PromptRecord
        {
            Id = newPromptId,
            CategoryId = categoryId,
            SortOrder = nextSortOrder
        };

        _promptRepo.Create(newPromptId, content);

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
                // Best effort rollback
            }
            throw;
        }

        _document = candidate;

        var returnedPrompt = new PromptRecord
        {
            Id = newPrompt.Id,
            CategoryId = newPrompt.CategoryId,
            SortOrder = newPrompt.SortOrder
        };

        return new OperationResult<PromptRecord>(returnedPrompt, commitResult.Warning);
    }

    public OperationResult EditPrompt(Guid promptId, string content)
    {
        var target = _document.Prompts.FirstOrDefault(p => p.Id == promptId)
                     ?? throw new InvalidOperationException($"Prompt does not exist in library: {promptId}");

        _promptRepo.Update(promptId, content);
        return new OperationResult(null);
    }

    public OperationResult DeletePrompt(Guid promptId)
    {
        var candidate = LibraryDocumentCloner.Clone(_document);
        var target = candidate.Prompts.FirstOrDefault(p => p.Id == promptId)
                     ?? throw new InvalidOperationException($"Prompt does not exist: {promptId}");

        candidate.Prompts.Remove(target);
        LibraryValidator.Validate(candidate);

        CommitResult commitResult = _libraryRepo.Commit(candidate);
        _document = candidate;

        string? combinedWarning = commitResult.Warning;

        if (commitResult.BackupSynchronized)
        {
            try
            {
                _promptRepo.DeleteIfExists(promptId);
            }
            catch (Exception ex)
            {
                string deleteWarning = $"Prompt was removed from the library, but its file could not be deleted from disk: {ex.Message}";
                combinedWarning = combinedWarning != null
                    ? $"{combinedWarning}\r\n\r\n{deleteWarning}"
                    : deleteWarning;
            }
        }
        else
        {
            string backupWarning = "Prompt was removed from the primary library, but its safety backup could not be updated. Its .md file has been preserved to prevent potential corrupt recovery.";
            combinedWarning = combinedWarning != null
                ? $"{combinedWarning}\r\n\r\n{backupWarning}"
                : backupWarning;
        }

        return new OperationResult(combinedWarning);
    }

    public OperationResult MovePrompt(Guid promptId, Guid? destinationCategoryId)
    {
        var candidate = LibraryDocumentCloner.Clone(_document);
        var target = candidate.Prompts.FirstOrDefault(p => p.Id == promptId)
                     ?? throw new InvalidOperationException($"Prompt does not exist: {promptId}");

        if (destinationCategoryId.HasValue && !candidate.Categories.Any(c => c.Id == destinationCategoryId.Value))
        {
            throw new InvalidOperationException($"Destination category does not exist: {destinationCategoryId.Value}");
        }

        if (target.CategoryId == destinationCategoryId)
        {
            return new OperationResult(null);
        }

        long nextSortOrder = CalculateNextPromptSortOrder(candidate, destinationCategoryId, promptId);
        target.CategoryId = destinationCategoryId;
        target.SortOrder = nextSortOrder;

        LibraryValidator.Validate(candidate);

        CommitResult commitResult = _libraryRepo.Commit(candidate);
        _document = candidate;

        return new OperationResult(commitResult.Warning);
    }

    public OperationResult<PromptRecord> DuplicatePrompt(Guid promptId, Guid? destinationCategoryId)
    {
        var target = _document.Prompts.FirstOrDefault(p => p.Id == promptId)
                     ?? throw new InvalidOperationException($"Prompt does not exist: {promptId}");

        string content;
        try
        {
            content = _promptRepo.Read(promptId);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Cannot duplicate prompt because its content file could not be read: {ex.Message}", ex);
        }

        return CreatePrompt(destinationCategoryId, content);
    }

    #endregion

    #region Navigation & Destinations

    public IReadOnlyList<BreadcrumbRecord> GetBreadcrumbs(Guid? currentCategoryId)
    {
        var items = new List<BreadcrumbRecord>
        {
            new(null, "Home")
        };

        if (!currentCategoryId.HasValue)
        {
            return items;
        }

        var path = new List<BreadcrumbRecord>();
        Guid? nextId = currentCategoryId;

        while (nextId.HasValue)
        {
            var cat = _document.Categories.FirstOrDefault(c => c.Id == nextId.Value);
            if (cat == null)
            {
                break;
            }

            path.Add(new BreadcrumbRecord(cat.Id, cat.Name));
            nextId = cat.ParentId;
        }

        path.Reverse();
        items.AddRange(path);
        return items;
    }

    public IReadOnlyList<DestinationRecord> GetDestinations()
    {
        var categoryPaths = new List<(Guid CategoryId, string RawPath)>();

        foreach (var cat in _document.Categories)
        {
            var segments = new List<string>();
            var curr = cat;
            while (curr != null)
            {
                segments.Add(curr.Name);
                curr = curr.ParentId.HasValue
                    ? _document.Categories.FirstOrDefault(c => c.Id == curr.ParentId.Value)
                    : null;
            }

            segments.Reverse();
            string pathStr = string.Join(" > ", segments);
            categoryPaths.Add((cat.Id, pathStr));
        }

        // Global uniqueness enforcement (PLH-007)
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Home" };
        var rawGroupCounts = categoryPaths
            .GroupBy(r => r.RawPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var disambiguatedCategories = new List<DestinationRecord>(categoryPaths.Count);

        foreach (var (catId, rawPath) in categoryPaths)
        {
            string candidatePath = rawPath;
            bool isColliding = string.Equals(rawPath, "Home", StringComparison.OrdinalIgnoreCase) ||
                               rawGroupCounts[rawPath] > 1;

            if (isColliding || usedPaths.Contains(candidatePath))
            {
                string guidHex = catId.ToString("N");
                int suffixLen = 8;
                candidatePath = $"{rawPath} [{guidHex[..suffixLen]}]";

                while (usedPaths.Contains(candidatePath) && suffixLen < 32)
                {
                    suffixLen = Math.Min(32, suffixLen + 4);
                    candidatePath = $"{rawPath} [{guidHex[..suffixLen]}]";
                }
            }

            usedPaths.Add(candidatePath);
            disambiguatedCategories.Add(new DestinationRecord(catId, candidatePath));
        }

        // PLH2-006: Sort final disambiguated categories by complete DisplayPath (OrdinalIgnoreCase) then by CategoryId
        var sortedCategories = disambiguatedCategories
            .OrderBy(c => c.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.CategoryId)
            .ToList();

        var results = new List<DestinationRecord>(sortedCategories.Count + 1)
        {
            new(null, "Home")
        };
        results.AddRange(sortedCategories);

        return results;
    }

    #endregion

    #region Helpers

    private Guid GenerateUniquePromptGuid(LibraryDocument candidate)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var candidateGuid = Guid.NewGuid();
            if (!candidate.Prompts.Any(p => p.Id == candidateGuid) && !_promptRepo.Exists(candidateGuid))
            {
                return candidateGuid;
            }
        }

        throw new InvalidOperationException("Failed to generate a unique prompt GUID after 10 attempts.");
    }

    private long CalculateNextCategorySortOrder(LibraryDocument document, Guid? parentId)
    {
        var siblings = document.Categories.Where(c => c.ParentId == parentId).ToList();
        if (siblings.Count == 0)
        {
            return 10;
        }

        long maxSort = siblings.Max(c => c.SortOrder);
        if (maxSort > long.MaxValue - 10)
        {
            var ordered = siblings
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Id)
                .ToList();
            long newSort = 10;
            foreach (var s in ordered)
            {
                s.SortOrder = newSort;
                newSort += 10;
            }
            return newSort;
        }

        return maxSort + 10;
    }

    private long CalculateNextPromptSortOrder(LibraryDocument document, Guid? categoryId, Guid? excludePromptId)
    {
        var siblings = document.Prompts
            .Where(p => p.CategoryId == categoryId && (!excludePromptId.HasValue || p.Id != excludePromptId.Value))
            .ToList();

        if (siblings.Count == 0)
        {
            return 10;
        }

        long maxSort = siblings.Max(p => p.SortOrder);
        if (maxSort > long.MaxValue - 10)
        {
            var ordered = siblings
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.Id)
                .ToList();
            long newSort = 10;
            foreach (var s in ordered)
            {
                s.SortOrder = newSort;
                newSort += 10;
            }
            return newSort;
        }

        return maxSort + 10;
    }

    #endregion
}