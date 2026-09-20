using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;
using System.IO;

namespace PromptHelper.Tests;

[TestClass]
public sealed class PremadePackTests
{
    private static (
        AppPaths Paths,
        LibraryRepository Library,
        PromptRepository Prompts,
        PremadePackInstaller Installer) CreateContext(string root)
    {
        var paths = new AppPaths(root);
        paths.EnsureDataDirectories();
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var library = new LibraryRepository(paths, writer);
        var prompts = new PromptRepository(paths, writer, deleter);
        var installer = new PremadePackInstaller(
            library,
            prompts,
            new LibraryPackageInspector(paths));
        return (paths, library, prompts, installer);
    }

    [TestMethod]
    public void Catalog_mirrors_Games_and_Tools_and_is_test_focused()
    {
        DefaultLibraryPackage package = PremadePromptCatalog.CreatePackage();

        Assert.AreEqual(PremadePromptCatalog.CurrentPackVersion, package.Document.PremadePackVersion);
        Assert.AreEqual(16, package.Document.Categories.Count);
        Assert.AreEqual(42, package.Document.Prompts.Count);
        Assert.AreEqual(42, package.PromptContents.Count);

        CategoryRecord root = package.Document.Categories.Single(x => x.Name == "Premades");
        CategoryRecord games = Child(package.Document, root.Id, "Games");
        CategoryRecord tools = Child(package.Document, root.Id, "Tools");

        foreach (CategoryRecord mirroredRoot in new[] { games, tools })
        {
            _ = Child(package.Document, mirroredRoot.Id, "Planning");
            _ = Child(package.Document, mirroredRoot.Id, "Implementation");
            _ = Child(package.Document, mirroredRoot.Id, "Testing");
        }

        Assert.IsTrue(package.Document.Prompts.Count(x =>
            IsDescendantOf(package.Document, x.CategoryId, Child(package.Document, games.Id, "Testing").Id)) >= 10);
        Assert.IsTrue(package.Document.Prompts.Count(x =>
            IsDescendantOf(package.Document, x.CategoryId, Child(package.Document, tools.Id, "Testing").Id)) >= 20);

        foreach (PremadePromptDefinition prompt in PremadePromptCatalog.Prompts)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(prompt.Title));
            Assert.IsTrue(prompt.Content.StartsWith("# ", StringComparison.Ordinal));
            Assert.IsTrue(prompt.Content.Length >= 300, $"Premade '{prompt.Title}' is too shallow.");
            StringAssert.Contains(prompt.Content, "# Task");
        }
    }

    [TestMethod]
    public void Fresh_defaults_include_original_content_and_complete_premade_pack()
    {
        DefaultLibraryPackage defaults = DefaultLibraryFactory.CreateDefaults();

        Assert.AreEqual(8 + 16, defaults.Document.Categories.Count);
        Assert.AreEqual(2 + 42, defaults.Document.Prompts.Count);
        Assert.AreEqual(2 + 42, defaults.PromptContents.Count);
        Assert.AreEqual(PremadePromptCatalog.CurrentPackVersion, defaults.Document.PremadePackVersion);
        Assert.IsTrue(defaults.Document.Categories.Any(x => x.Id == DefaultLibraryFactory.GamesCategoryId));
        Assert.IsTrue(defaults.Document.Categories.Any(x => x.Id == PremadePromptCatalog.PremadesCategoryId));
    }

    [TestMethod]
    public void Existing_library_is_upgraded_without_changing_user_content()
    {
        using var temp = new TestDirectory();
        var (_, library, prompts, installer) = CreateContext(temp.Root);
        Guid userCategoryId = Guid.NewGuid();
        Guid userPromptId = Guid.NewGuid();
        const string userBody = "My private prompt stays byte-for-byte unchanged.";

        prompts.Create(userPromptId, userBody);
        var original = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = userCategoryId, Name = "My Work", SortOrder = 7 }
            ],
            Prompts =
            [
                new PromptRecord
                {
                    Id = userPromptId,
                    CategoryId = userCategoryId,
                    Title = "User Prompt",
                    SortOrder = 3
                }
            ]
        };
        library.Commit(original);

        PremadePackInstallResult result = installer.InstallIfNeeded(original);

        Assert.IsTrue(result.Installed);
        Assert.AreEqual(PremadePromptCatalog.CurrentPackVersion, result.Document.PremadePackVersion);
        Assert.AreEqual(userBody, prompts.Read(userPromptId));
        CollectionAssert.AreEqual(
            new[] { "My Work", "Premades" },
            result.Document.Categories.Where(x => x.ParentId == null).Select(x => x.Name).Order().ToArray());
        Assert.AreEqual(1 + PremadePromptCatalog.Prompts.Count, result.Document.Prompts.Count);
        Assert.AreEqual(result.Document.Prompts.Count, new LibraryPackageInspector(library.Paths)
            .Inspect(result.Document) is LibraryPackageState.Healthy healthy ? healthy.Bodies.Count : -1);
    }

    [TestMethod]
    public void Existing_matching_Premades_hierarchy_is_reused_not_duplicated()
    {
        using var temp = new TestDirectory();
        var (_, library, _, installer) = CreateContext(temp.Root);
        Guid customRoot = Guid.NewGuid();
        Guid customGames = Guid.NewGuid();
        var original = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = customRoot, Name = "Premades", SortOrder = 1 },
                new CategoryRecord { Id = customGames, ParentId = customRoot, Name = "Games", SortOrder = 1 }
            ]
        };
        library.Commit(original);

        PremadePackInstallResult result = installer.InstallIfNeeded(original);

        Assert.IsTrue(result.Installed);
        Assert.AreEqual(1, result.Document.Categories.Count(x => x.ParentId == null && x.Name == "Premades"));
        Assert.AreEqual(1, result.Document.Categories.Count(x => x.ParentId == customRoot && x.Name == "Games"));
        Assert.IsFalse(result.Document.Categories.Any(x => x.Id == PremadePromptCatalog.PremadesCategoryId));
        Assert.IsFalse(result.Document.Categories.Any(x => x.Id == PremadePromptCatalog.GamesCategoryId));
        Assert.IsTrue(result.Document.Prompts.Any(x =>
            x.CategoryId == result.Document.Categories.Single(c =>
                c.ParentId == customGames && c.Name == "Planning").Id));
    }

    [TestMethod]
    public void Interrupted_install_resumes_from_exact_orphan_body()
    {
        using var temp = new TestDirectory();
        var (_, library, prompts, installer) = CreateContext(temp.Root);
        var original = new LibraryDocument();
        library.Commit(original);

        PremadePromptDefinition first = PremadePromptCatalog.Prompts[0];
        prompts.Create(first.Id, first.Content);

        PremadePackInstallResult result = installer.InstallIfNeeded(original);

        Assert.IsTrue(result.Installed);
        Assert.AreEqual(first.Content, prompts.Read(first.Id));
        Assert.IsTrue(result.Document.Prompts.Any(x => x.Id == first.Id));
    }

    [TestMethod]
    public void Conflicting_orphan_body_fails_closed_without_overwrite()
    {
        using var temp = new TestDirectory();
        var (_, library, prompts, installer) = CreateContext(temp.Root);
        var original = new LibraryDocument();
        library.Commit(original);

        PremadePromptDefinition first = PremadePromptCatalog.Prompts[0];
        prompts.Create(first.Id, "foreign body");

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => installer.InstallIfNeeded(original));

        StringAssert.Contains(error.Message, "different content");
        Assert.AreEqual("foreign body", prompts.Read(first.Id));
        Assert.AreEqual(0, library.ReadPrimary().PremadePackVersion);
    }

    [TestMethod]
    public void Existing_stable_prompt_is_treated_as_user_owned_and_never_overwritten()
    {
        using var temp = new TestDirectory();
        var (_, library, prompts, installer) = CreateContext(temp.Root);
        PremadePromptDefinition first = PremadePromptCatalog.Prompts[0];
        Guid customCategory = Guid.NewGuid();
        const string customized = "My customized version of the bundled prompt.";
        prompts.Create(first.Id, customized);
        var original = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = customCategory, Name = "Personal", SortOrder = 1 }
            ],
            Prompts =
            [
                new PromptRecord
                {
                    Id = first.Id,
                    CategoryId = customCategory,
                    Title = "Renamed by user",
                    SortOrder = 999
                }
            ]
        };
        library.Commit(original);

        PremadePackInstallResult result = installer.InstallIfNeeded(original);

        Assert.IsTrue(result.Installed);
        Assert.AreEqual(customized, prompts.Read(first.Id));
        PromptRecord preserved = result.Document.Prompts.Single(x => x.Id == first.Id);
        Assert.AreEqual(customCategory, preserved.CategoryId);
        Assert.AreEqual("Renamed by user", preserved.Title);
        Assert.AreEqual(999, preserved.SortOrder);
    }

    [TestMethod]
    public void Incomplete_existing_library_defers_pack_without_creating_files()
    {
        using var temp = new TestDirectory();
        var (paths, library, _, installer) = CreateContext(temp.Root);
        Guid unavailable = Guid.NewGuid();
        var original = new LibraryDocument
        {
            Prompts =
            [
                new PromptRecord { Id = unavailable, Title = "Unavailable", SortOrder = 1 }
            ]
        };
        library.Commit(original);

        PremadePackInstallResult result = installer.InstallIfNeeded(original);

        Assert.IsFalse(result.Installed);
        Assert.IsNotNull(result.Warning);
        Assert.AreEqual(0, result.Document.PremadePackVersion);
        Assert.IsFalse(File.Exists(paths.GetPromptPath(PremadePromptCatalog.Prompts[0].Id)));
    }

    [TestMethod]
    public void Installed_marker_makes_upgrade_idempotent_and_respects_user_deletion()
    {
        using var temp = new TestDirectory();
        var (paths, library, prompts, installer) = CreateContext(temp.Root);
        var original = new LibraryDocument();
        library.Commit(original);
        PremadePackInstallResult first = installer.InstallIfNeeded(original);

        Guid removedId = PremadePromptCatalog.Prompts[0].Id;
        LibraryDocument userEdited = LibraryDocumentCloner.Clone(first.Document);
        userEdited.Prompts.RemoveAll(x => x.Id == removedId);
        library.Commit(userEdited);
        File.Delete(paths.GetPromptPath(removedId));

        PremadePackInstallResult second = installer.InstallIfNeeded(userEdited);

        Assert.IsFalse(second.Installed);
        Assert.IsFalse(second.Document.Prompts.Any(x => x.Id == removedId));
        Assert.IsFalse(prompts.Exists(removedId));
        Assert.AreEqual(userEdited.Categories.Count, second.Document.Categories.Count);
        Assert.AreEqual(userEdited.Prompts.Count, second.Document.Prompts.Count);
    }

    [TestMethod]
    public void Legacy_schema_one_JSON_without_pack_marker_remains_readable()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "categories": [],
          "prompts": []
        }
        """;

        LibraryDocument document = LibraryRepository.InspectAndDeserialize(json);

        Assert.AreEqual(0, document.PremadePackVersion);
        StringAssert.Contains(
            System.Text.Encoding.UTF8.GetString(new LibraryRepository(new AppPaths(Path.GetTempPath()))
                .SerializeCanonicalBytes(document)),
            "\"premadePackVersion\": 0");
    }

    private static CategoryRecord Child(LibraryDocument document, Guid parentId, string name) =>
        document.Categories.Single(x => x.ParentId == parentId && x.Name == name);

    private static bool IsDescendantOf(LibraryDocument document, Guid? categoryId, Guid ancestorId)
    {
        while (categoryId.HasValue)
        {
            if (categoryId.Value == ancestorId)
            {
                return true;
            }

            categoryId = document.Categories.Single(x => x.Id == categoryId.Value).ParentId;
        }

        return false;
    }
}
