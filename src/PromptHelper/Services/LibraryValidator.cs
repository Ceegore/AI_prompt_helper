using System.IO;
using PromptHelper.Infrastructure;
using PromptHelper.Models;

namespace PromptHelper.Services;

public static class LibraryValidator
{
    public const int MaximumCategoryNameLength = 80;

    public static void Validate(LibraryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion != LibraryDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Invalid schema version: {document.SchemaVersion}. Expected: {LibraryDocument.CurrentSchemaVersion}.");
        }

        var categoryIds = new HashSet<Guid>();
        var categoriesById = new Dictionary<Guid, CategoryRecord>();

        foreach (var category in document.Categories)
        {
            if (category.Id == Guid.Empty)
            {
                throw new InvalidDataException("Category ID cannot be empty GUID.");
            }

            if (!categoryIds.Add(category.Id))
            {
                throw new InvalidDataException($"Duplicate category ID: {category.Id}");
            }

            ValidateCategoryNameProperties(category.Name);

            categoriesById[category.Id] = category;
        }

        // Validate ParentId references and self-parenting
        foreach (var category in document.Categories)
        {
            if (category.ParentId.HasValue)
            {
                if (category.ParentId.Value == Guid.Empty)
                {
                    throw new InvalidDataException($"Category '{category.Name}' has empty GUID as ParentId.");
                }

                if (category.ParentId.Value == category.Id)
                {
                    throw new InvalidDataException($"Category '{category.Name}' cannot be its own parent.");
                }

                if (!categoriesById.ContainsKey(category.ParentId.Value))
                {
                    throw new InvalidDataException($"Category '{category.Name}' references nonexistent parent: {category.ParentId.Value}");
                }
            }
        }

        // Validate cycles
        foreach (var category in document.Categories)
        {
            var visitedInChain = new HashSet<Guid> { category.Id };
            Guid? currentParentId = category.ParentId;

            while (currentParentId.HasValue)
            {
                if (!visitedInChain.Add(currentParentId.Value))
                {
                    throw new InvalidDataException($"Category hierarchy contains a cycle involving category '{category.Name}' ({category.Id}).");
                }

                currentParentId = categoriesById[currentParentId.Value].ParentId;
            }
        }

        // Validate sibling uniqueness (case-insensitive)
        var siblingsByParent = document.Categories.GroupBy(c => c.ParentId);
        foreach (var group in siblingsByParent)
        {
            var siblingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sibling in group)
            {
                if (!siblingNames.Add(sibling.Name))
                {
                    throw new InvalidDataException($"Duplicate sibling category name '{sibling.Name}' found under parent '{group.Key}'.");
                }
            }
        }

        // Validate prompts
        var promptIds = new HashSet<Guid>();
        foreach (var prompt in document.Prompts)
        {
            if (prompt.Id == Guid.Empty)
            {
                throw new InvalidDataException("Prompt ID cannot be empty GUID.");
            }

            if (!promptIds.Add(prompt.Id))
            {
                throw new InvalidDataException($"Duplicate prompt ID: {prompt.Id}");
            }

            if (prompt.CategoryId.HasValue)
            {
                if (prompt.CategoryId.Value == Guid.Empty)
                {
                    throw new InvalidDataException($"Prompt '{prompt.Id}' has empty GUID as CategoryId.");
                }

                if (!categoriesById.ContainsKey(prompt.CategoryId.Value))
                {
                    throw new InvalidDataException($"Prompt '{prompt.Id}' references nonexistent category: {prompt.CategoryId.Value}");
                }
            }

            if (prompt.Title != null)
            {
                if (string.IsNullOrWhiteSpace(prompt.Title))
                {
                    throw new InvalidDataException($"Prompt '{prompt.Id}' title cannot be whitespace only; use null for automatic title.");
                }

                if (prompt.Title != prompt.Title.Trim())
                {
                    throw new InvalidDataException($"Prompt '{prompt.Id}' title must be trimmed.");
                }

                if (TextUtilities.ContainsForbiddenSingleLineCharacter(prompt.Title))
                {
                    throw new InvalidDataException($"Prompt '{prompt.Id}' title cannot contain control characters or line breaks.");
                }
            }
        }
    }

    public static string? ValidateCategoryNameInput(string? name, IEnumerable<string> existingSiblingNames)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Category name cannot be empty.";
        }

        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return "Category name cannot be whitespace only.";
        }

        if (TextUtilities.ContainsForbiddenSingleLineCharacter(trimmed))
        {
            return "Category name cannot contain control characters or line breaks.";
        }

        if (TextUtilities.GetTextElementCount(trimmed) > MaximumCategoryNameLength)
        {
            return $"Category name cannot exceed {MaximumCategoryNameLength} characters.";
        }

        if (existingSiblingNames.Any(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return $"A category named '{trimmed}' already exists in this location.";
        }

        return null;
    }

    private static void ValidateCategoryNameProperties(string name)
    {
        if (name == null)
        {
            throw new InvalidDataException("Category name cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidDataException("Category name cannot be empty or whitespace.");
        }

        if (name != name.Trim())
        {
            throw new InvalidDataException($"Category name '{name}' must be trimmed.");
        }

        if (TextUtilities.ContainsForbiddenSingleLineCharacter(name))
        {
            throw new InvalidDataException($"Category name '{name}' cannot contain control characters or line breaks.");
        }

        if (TextUtilities.GetTextElementCount(name) > MaximumCategoryNameLength)
        {
            throw new InvalidDataException($"Category name '{name}' exceeds {MaximumCategoryNameLength} text elements.");
        }
    }
}