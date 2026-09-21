using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Platform.Linux.Tests;

[TestClass]
public sealed class LinuxPromptLibraryStoreTests
{
    [TestMethod]
    public void Initialize_creates_windows_compatible_default_library_and_premades()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            var store = new LinuxPromptLibraryStore(root);
            store.Initialize();

            LibraryDocument document = store.CurrentDocument;
            Assert.AreEqual(
                PremadePromptCatalog.CurrentPackVersion,
                document.PremadePackVersion);
            Assert.IsTrue(document.Categories.Any(c => c.Name == "Games"));
            Assert.IsTrue(document.Categories.Any(c => c.Name == "Tools"));
            Assert.IsTrue(document.Categories.Any(c => c.Name == "Premades"));
            Assert.IsTrue(document.Prompts.Count >= 40);

            string raw = File.ReadAllText(Path.Combine(root, "library.json"));
            Assert.IsTrue(raw.Contains("\r\n", StringComparison.Ordinal));
            Assert.IsTrue(File.Exists(Path.Combine(root, "library.backup.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Category_and_prompt_crud_round_trips_after_restart()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            var store = new LinuxPromptLibraryStore(root);
            store.Initialize();

            CategoryRecord category =
                store.CreateCategory(null, "My prompts").Value;

            PromptRecord prompt =
                store.CreatePrompt(
                    category.Id,
                    "first body",
                    "First title").Value;

            store.EditPrompt(
                prompt.Id,
                "second body",
                "Second title");

            PromptRecord duplicate =
                store.DuplicatePrompt(prompt.Id, null).Value;

            store.MovePrompt(prompt.Id, null);
            store.DeletePrompt(duplicate.Id);

            var reopened = new LinuxPromptLibraryStore(root);
            reopened.Initialize();

            Assert.AreEqual(
                "second body",
                reopened.ReadPrompt(prompt.Id));

            PromptRecord reopenedPrompt =
                reopened.CurrentDocument.Prompts
                    .Single(p => p.Id == prompt.Id);
            Assert.IsNull(reopenedPrompt.CategoryId);
            Assert.AreEqual("Second title", reopenedPrompt.Title);
            Assert.IsFalse(
                reopened.CurrentDocument.Prompts
                    .Any(p => p.Id == duplicate.Id));

            reopened.DeletePrompt(prompt.Id);
            reopened.DeleteCategory(category.Id);
            Assert.IsFalse(
                reopened.CurrentDocument.Categories
                    .Any(c => c.Id == category.Id));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void External_library_change_is_never_silently_overwritten()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            var store = new LinuxPromptLibraryStore(root);
            store.Initialize();

            string library = Path.Combine(root, "library.json");
            File.AppendAllText(library, " ");

            Assert.Throws<InvalidOperationException>(() =>
                store.CreateCategory(null, "Should not commit"));

            Assert.IsTrue(
                File.ReadAllText(library).EndsWith(" ", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Corrupt_primary_is_restored_from_valid_backup()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            var store = new LinuxPromptLibraryStore(root);
            store.Initialize();

            File.WriteAllText(
                Path.Combine(root, "library.json"),
                "{ definitely not json");

            var reopened = new LinuxPromptLibraryStore(root);
            reopened.Initialize();

            Assert.IsTrue(
                reopened.StartupWarnings.Any(w =>
                    w.Contains("backup", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(
                reopened.CurrentDocument.Categories.Count > 0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Empty_category_must_be_empty_before_delete()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            var store = new LinuxPromptLibraryStore(root);
            store.Initialize();

            CategoryRecord category =
                store.CreateCategory(null, "Protected").Value;
            PromptRecord prompt =
                store.CreatePrompt(category.Id, "body", null).Value;

            Assert.Throws<InvalidOperationException>(() =>
                store.DeleteCategory(category.Id));

            store.DeletePrompt(prompt.Id);
            store.DeleteCategory(category.Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Breadcrumbs_and_destinations_follow_nested_categories()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            var store = new LinuxPromptLibraryStore(root);
            store.Initialize();

            CategoryRecord a =
                store.CreateCategory(null, "A").Value;
            CategoryRecord b =
                store.CreateCategory(a.Id, "B").Value;

            string[] breadcrumbs =
                store.GetBreadcrumbs(b.Id)
                    .Select(item => item.Name)
                    .ToArray();

            CollectionAssert.AreEqual(
                new[] { "Home", "A", "B" },
                breadcrumbs);

            Assert.IsTrue(
                store.GetDestinations()
                    .Any(item =>
                        item.CategoryId == b.Id &&
                        item.DisplayPath == "A / B"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-LinuxStore-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
