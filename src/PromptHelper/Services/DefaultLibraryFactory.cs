using PromptHelper.Models;

namespace PromptHelper.Services;

public static class DefaultLibraryFactory
{
    public static readonly Guid GamesCategoryId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    public static readonly Guid ToolsCategoryId =
        Guid.Parse("10000000-0000-0000-0000-000000000002");

    public static readonly Guid GamesPlanningCategoryId =
        Guid.Parse("10000000-0000-0000-0000-000000000011");

    public static readonly Guid GamesImplementationCategoryId =
        Guid.Parse("10000000-0000-0000-0000-000000000012");

    public static readonly Guid GamesTestingCategoryId =
        Guid.Parse("10000000-0000-0000-0000-000000000013");

    public static readonly Guid ToolsPlanningCategoryId =
        Guid.Parse("10000000-0000-0000-0000-000000000021");

    public static readonly Guid ToolsImplementationCategoryId =
        Guid.Parse("10000000-0000-0000-0000-000000000022");

    public static readonly Guid ToolsTestingCategoryId =
        Guid.Parse("10000000-0000-0000-0000-000000000023");

    public static readonly Guid DefaultPrompt1Id =
        Guid.Parse("20000000-0000-0000-0000-000000000001");

    public static readonly Guid DefaultPrompt2Id =
        Guid.Parse("20000000-0000-0000-0000-000000000002");

    public const string DefaultPrompt1Content = """
# Task

Create a detailed implementation plan for the supplied game project.

## Requirements

- identify unclear requirements
- define implementation phases
- minimize decisions left to the implementation agent
- include validation and testing steps
- preserve the supplied product scope
""";

    public const string DefaultPrompt2Content = """
# Task

Perform a thorough quality review of the supplied implementation.

Check for:

- functional defects
- missing requirements
- inconsistent behaviour
- data-loss risks
- error-handling problems
- regression risks

Repair confirmed defects where permitted and run the relevant tests again.
""";

    public static DefaultLibraryPackage CreateDefaults()
    {
        var doc = new LibraryDocument
        {
            SchemaVersion = LibraryDocument.CurrentSchemaVersion,
            Categories =
            [
                new CategoryRecord
                {
                    Id = GamesCategoryId,
                    ParentId = null,
                    Name = "Games",
                    SortOrder = 10
                },
                new CategoryRecord
                {
                    Id = ToolsCategoryId,
                    ParentId = null,
                    Name = "Tools",
                    SortOrder = 20
                },
                new CategoryRecord
                {
                    Id = GamesPlanningCategoryId,
                    ParentId = GamesCategoryId,
                    Name = "Planning",
                    SortOrder = 10
                },
                new CategoryRecord
                {
                    Id = GamesImplementationCategoryId,
                    ParentId = GamesCategoryId,
                    Name = "Implementation",
                    SortOrder = 20
                },
                new CategoryRecord
                {
                    Id = GamesTestingCategoryId,
                    ParentId = GamesCategoryId,
                    Name = "Testing",
                    SortOrder = 30
                },
                new CategoryRecord
                {
                    Id = ToolsPlanningCategoryId,
                    ParentId = ToolsCategoryId,
                    Name = "Planning",
                    SortOrder = 10
                },
                new CategoryRecord
                {
                    Id = ToolsImplementationCategoryId,
                    ParentId = ToolsCategoryId,
                    Name = "Implementation",
                    SortOrder = 20
                },
                new CategoryRecord
                {
                    Id = ToolsTestingCategoryId,
                    ParentId = ToolsCategoryId,
                    Name = "Testing",
                    SortOrder = 30
                }
            ],
            Prompts =
            [
                new PromptRecord
                {
                    Id = DefaultPrompt1Id,
                    CategoryId = GamesPlanningCategoryId,
                    SortOrder = 10
                },
                new PromptRecord
                {
                    Id = DefaultPrompt2Id,
                    CategoryId = ToolsTestingCategoryId,
                    SortOrder = 10
                }
            ]
        };

        var contents = new Dictionary<Guid, string>
        {
            [DefaultPrompt1Id] = DefaultPrompt1Content,
            [DefaultPrompt2Id] = DefaultPrompt2Content
        };

        return new DefaultLibraryPackage(doc, contents);
    }
}