using PromptHelper.Models;

namespace PromptHelper.Services;

public static class LibraryDocumentCloner
{
    public static LibraryDocument Clone(LibraryDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new LibraryDocument
        {
            SchemaVersion = source.SchemaVersion,

            Categories = source.Categories
                .Select(x => new CategoryRecord
                {
                    Id = x.Id,
                    ParentId = x.ParentId,
                    Name = x.Name,
                    SortOrder = x.SortOrder
                })
                .ToList(),

            Prompts = source.Prompts
                .Select(x => new PromptRecord
                {
                    Id = x.Id,
                    CategoryId = x.CategoryId,
                    SortOrder = x.SortOrder
                })
                .ToList()
        };
    }
}