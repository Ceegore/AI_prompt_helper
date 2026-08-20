using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class PromptLibraryServiceTests
{
    private static (PromptLibraryService service, AppPaths paths, LibraryRepository libRepo, PromptRepository promptRepo, FaultInjectingAtomicTextWriter faultWriter, FaultInjectingFileDeleter faultDeleter)
        CreateTestContext(string root)
    {
        var paths = new AppPaths(root);
        var baseWriter = new AtomicTextWriter();
        var faultWriter = new FaultInjectingAtomicTextWriter(baseWriter);
        var faultDeleter = new FaultInjectingFileDeleter();

        var libRepo = new LibraryRepository(paths, faultWriter);
        var promptRepo = new PromptRepository(paths, faultWriter, faultDeleter);
        var doc = new LibraryDocument();
        libRepo.Commit(doc);

        var service = new PromptLibraryService(doc, libRepo, promptRepo);
        return (service, paths, libRepo, promptRepo, faultWriter, faultDeleter);
    }

    #region Category Service Tests

    [TestMethod]
    public void Create_category_at_Home()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var result = service.CreateCategory(null, "Tools");

        Assert.IsNotNull(result.Value);
        Assert.AreEqual("Tools", result.Value.Name);
        Assert.IsNull(result.Value.ParentId);
        Assert.AreEqual(1, service.CurrentDocument.Categories.Count);
    }

    [TestMethod]
    public void Create_nested_category()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var parent = service.CreateCategory(null, "Tools").Value;
        var child = service.CreateCategory(parent.Id, "Windows").Value;

        Assert.AreEqual(parent.Id, child.ParentId);
        Assert.AreEqual(2, service.CurrentDocument.Categories.Count);
    }

    [TestMethod]
    public void Duplicate_sibling_rejected()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        service.CreateCategory(null, "Tools");
        Assert.Throws<InvalidOperationException>(() => service.CreateCategory(null, "Tools"));
    }

    [TestMethod]
    public void Case_variant_sibling_rejected()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        service.CreateCategory(null, "Tools");
        Assert.Throws<InvalidOperationException>(() => service.CreateCategory(null, "tools"));
    }

    [TestMethod]
    public void Same_name_other_parent_allowed()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var parent1 = service.CreateCategory(null, "Cat1").Value;
        var parent2 = service.CreateCategory(null, "Cat2").Value;

        var child1 = service.CreateCategory(parent1.Id, "Testing").Value;
        var child2 = service.CreateCategory(parent2.Id, "Testing").Value;

        Assert.IsNotNull(child1);
        Assert.IsNotNull(child2);
    }

    [TestMethod]
    public void Control_character_name_rejected()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        Assert.Throws<InvalidOperationException>(() => service.CreateCategory(null, "Bad\tName"));
    }

    [TestMethod]
    public void Rename()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var cat = service.CreateCategory(null, "OldName").Value;
        service.RenameCategory(cat.Id, "NewName");

        Assert.AreEqual("NewName", service.CurrentDocument.Categories.First(c => c.Id == cat.Id).Name);
    }

    [TestMethod]
    public void Rename_duplicate_rejected()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var cat1 = service.CreateCategory(null, "Cat1").Value;
        var cat2 = service.CreateCategory(null, "Cat2").Value;

        Assert.Throws<InvalidOperationException>(() => service.RenameCategory(cat2.Id, "cat1"));
    }

    [TestMethod]
    public void Delete_empty()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var cat = service.CreateCategory(null, "Temp").Value;
        service.DeleteCategory(cat.Id);

        Assert.AreEqual(0, service.CurrentDocument.Categories.Count);
    }

    [TestMethod]
    public void Delete_with_prompt_rejected()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var cat = service.CreateCategory(null, "WithPrompt").Value;
        service.CreatePrompt(cat.Id, "Some content");

        Assert.Throws<InvalidOperationException>(() => service.DeleteCategory(cat.Id));
    }

    [TestMethod]
    public void Delete_with_child_rejected()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var parent = service.CreateCategory(null, "Parent").Value;
        service.CreateCategory(parent.Id, "Child");

        Assert.Throws<InvalidOperationException>(() => service.DeleteCategory(parent.Id));
    }

    [TestMethod]
    public void Category_primary_save_failure_keeps_in_memory_state()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, faultWriter, _) = CreateTestContext(testDir.Root);

        faultWriter.ShouldFail = (path, callNum) => path.EndsWith("library.json", StringComparison.OrdinalIgnoreCase);

        Assert.Throws<IOException>(() => service.CreateCategory(null, "FailedCat"));
        Assert.AreEqual(0, service.CurrentDocument.Categories.Count);
    }

    [TestMethod]
    public void Category_backup_failure_commits_with_warning()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, faultWriter, _) = CreateTestContext(testDir.Root);

        faultWriter.ShouldFail = (path, callNum) => path.EndsWith("library.backup.json", StringComparison.OrdinalIgnoreCase);

        var result = service.CreateCategory(null, "BackupFailCat");
        Assert.IsNotNull(result.Warning);
        Assert.AreEqual(1, service.CurrentDocument.Categories.Count);
    }

    #endregion

    #region Prompt Service Tests

    [TestMethod]
    public void Create_prompt_on_Home()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, promptRepo, _, _) = CreateTestContext(testDir.Root);

        var result = service.CreatePrompt(null, "Home Prompt Content");

        Assert.IsNotNull(result.Value);
        Assert.IsNull(result.Value.CategoryId);
        Assert.AreEqual(1, service.CurrentDocument.Prompts.Count);
        Assert.IsTrue(promptRepo.Exists(result.Value.Id));
        Assert.AreEqual("Home Prompt Content", promptRepo.Read(result.Value.Id));
    }

    [TestMethod]
    public void Create_prompt_in_category()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, promptRepo, _, _) = CreateTestContext(testDir.Root);

        var cat = service.CreateCategory(null, "General").Value;
        var result = service.CreatePrompt(cat.Id, "Category Prompt Content");

        Assert.AreEqual(cat.Id, result.Value.CategoryId);
        Assert.AreEqual("Category Prompt Content", promptRepo.Read(result.Value.Id));
    }

    [TestMethod]
    public void Create_primary_failure_no_metadata_commit()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, faultWriter, _) = CreateTestContext(testDir.Root);

        faultWriter.ShouldFail = (path, callNum) => path.EndsWith("library.json", StringComparison.OrdinalIgnoreCase);

        Assert.Throws<IOException>(() => service.CreatePrompt(null, "Fail prompt"));
        Assert.AreEqual(0, service.CurrentDocument.Prompts.Count);
    }

    [TestMethod]
    public void Create_primary_failure_file_cleanup()
    {
        using var testDir = new TestDirectory();
        var (service, paths, _, _, faultWriter, _) = CreateTestContext(testDir.Root);

        faultWriter.ShouldFail = (path, callNum) => path.EndsWith("library.json", StringComparison.OrdinalIgnoreCase);

        Assert.Throws<IOException>(() => service.CreatePrompt(null, "Fail prompt"));

        var files = Directory.GetFiles(paths.PromptsDirectory, "*.md");
        Assert.AreEqual(0, files.Length);
    }

    [TestMethod]
    public void Create_cleanup_failure_leaves_orphan_only()
    {
        using var testDir = new TestDirectory();
        var (service, paths, _, _, faultWriter, faultDeleter) = CreateTestContext(testDir.Root);

        faultWriter.ShouldFail = (path, callNum) => path.EndsWith("library.json", StringComparison.OrdinalIgnoreCase);
        faultDeleter.Fail = true;

        Assert.Throws<IOException>(() => service.CreatePrompt(null, "Fail prompt"));

        Assert.AreEqual(0, service.CurrentDocument.Prompts.Count);
        var files = Directory.GetFiles(paths.PromptsDirectory, "*.md");
        Assert.AreEqual(1, files.Length);
    }

    [TestMethod]
    public void Create_backup_failure_commits()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, promptRepo, faultWriter, _) = CreateTestContext(testDir.Root);

        faultWriter.ShouldFail = (path, callNum) => path.EndsWith("library.backup.json", StringComparison.OrdinalIgnoreCase);

        var result = service.CreatePrompt(null, "Committed with warning");
        Assert.IsNotNull(result.Warning);
        Assert.AreEqual(1, service.CurrentDocument.Prompts.Count);
        Assert.IsTrue(promptRepo.Exists(result.Value.Id));
    }

    [TestMethod]
    public void Edit_prompt()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, promptRepo, _, _) = CreateTestContext(testDir.Root);

        var prompt = service.CreatePrompt(null, "Original").Value;
        service.EditPrompt(prompt.Id, "Edited text");

        Assert.AreEqual("Edited text", promptRepo.Read(prompt.Id));
    }

    [TestMethod]
    public void Edit_missing_file_fails()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, promptRepo, _, _) = CreateTestContext(testDir.Root);

        var prompt = service.CreatePrompt(null, "Original").Value;
        promptRepo.DeleteIfExists(prompt.Id);

        Assert.Throws<FileNotFoundException>(() => service.EditPrompt(prompt.Id, "Updated"));
    }

    [TestMethod]
    public void Delete_prompt_success()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, promptRepo, _, _) = CreateTestContext(testDir.Root);

        var prompt = service.CreatePrompt(null, "To delete").Value;
        service.DeletePrompt(prompt.Id);

        Assert.AreEqual(0, service.CurrentDocument.Prompts.Count);
        Assert.IsFalse(promptRepo.Exists(prompt.Id));
    }

    [TestMethod]
    public void Delete_primary_failure_preserves_prompt()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, promptRepo, faultWriter, _) = CreateTestContext(testDir.Root);

        var prompt = service.CreatePrompt(null, "Keep me").Value;
        faultWriter.ShouldFail = (path, callNum) => path.EndsWith("library.json", StringComparison.OrdinalIgnoreCase);

        Assert.Throws<IOException>(() => service.DeletePrompt(prompt.Id));
        Assert.AreEqual(1, service.CurrentDocument.Prompts.Count);
        Assert.IsTrue(promptRepo.Exists(prompt.Id));
    }

    [TestMethod]
    public void Delete_backup_failure_keeps_file()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, promptRepo, faultWriter, _) = CreateTestContext(testDir.Root);

        var prompt = service.CreatePrompt(null, "Keep file").Value;
        faultWriter.ShouldFail = (path, callNum) => path.EndsWith("library.backup.json", StringComparison.OrdinalIgnoreCase);

        var result = service.DeletePrompt(prompt.Id);

        Assert.IsNotNull(result.Warning);
        Assert.AreEqual(0, service.CurrentDocument.Prompts.Count);
        Assert.IsTrue(promptRepo.Exists(prompt.Id)); // file retained for backup safety
    }

    [TestMethod]
    public void Delete_file_failure_leaves_orphan()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, promptRepo, _, faultDeleter) = CreateTestContext(testDir.Root);

        var prompt = service.CreatePrompt(null, "Orphan file").Value;
        faultDeleter.Fail = true;

        var result = service.DeletePrompt(prompt.Id);

        Assert.IsNotNull(result.Warning);
        Assert.AreEqual(0, service.CurrentDocument.Prompts.Count);
        Assert.IsTrue(promptRepo.Exists(prompt.Id));
    }

    [TestMethod]
    public void Move_prompt()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var cat1 = service.CreateCategory(null, "Cat1").Value;
        var cat2 = service.CreateCategory(null, "Cat2").Value;

        var prompt = service.CreatePrompt(cat1.Id, "Move me").Value;
        service.MovePrompt(prompt.Id, cat2.Id);

        Assert.AreEqual(cat2.Id, service.CurrentDocument.Prompts.First(p => p.Id == prompt.Id).CategoryId);
    }

    [TestMethod]
    public void Move_to_Home()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var cat = service.CreateCategory(null, "Cat1").Value;
        var prompt = service.CreatePrompt(cat.Id, "Move to home").Value;

        service.MovePrompt(prompt.Id, null);

        Assert.IsNull(service.CurrentDocument.Prompts.First(p => p.Id == prompt.Id).CategoryId);
    }

    [TestMethod]
    public void Move_same_category_noop()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var cat = service.CreateCategory(null, "Cat1").Value;
        var prompt = service.CreatePrompt(cat.Id, "No-op move").Value;

        var result = service.MovePrompt(prompt.Id, cat.Id);
        Assert.IsNull(result.Warning);
    }

    [TestMethod]
    public void Move_backup_failure_commits()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, faultWriter, _) = CreateTestContext(testDir.Root);

        var cat1 = service.CreateCategory(null, "Cat1").Value;
        var cat2 = service.CreateCategory(null, "Cat2").Value;
        var prompt = service.CreatePrompt(cat1.Id, "Move me").Value;

        faultWriter.ShouldFail = (path, callNum) => path.EndsWith("library.backup.json", StringComparison.OrdinalIgnoreCase);

        var result = service.MovePrompt(prompt.Id, cat2.Id);
        Assert.IsNotNull(result.Warning);
        Assert.AreEqual(cat2.Id, service.CurrentDocument.Prompts.First(p => p.Id == prompt.Id).CategoryId);
    }

    [TestMethod]
    public void Duplicate_prompt()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, promptRepo, _, _) = CreateTestContext(testDir.Root);

        var cat1 = service.CreateCategory(null, "Cat1").Value;
        var cat2 = service.CreateCategory(null, "Cat2").Value;
        var prompt = service.CreatePrompt(cat1.Id, "Duplicate content").Value;

        var dup = service.DuplicatePrompt(prompt.Id, cat2.Id).Value;

        Assert.AreNotEqual(prompt.Id, dup.Id);
        Assert.AreEqual(cat2.Id, dup.CategoryId);
        Assert.AreEqual(2, service.CurrentDocument.Prompts.Count);
        Assert.AreEqual("Duplicate content", promptRepo.Read(dup.Id));
    }

    [TestMethod]
    public void Duplicate_same_category()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, promptRepo, _, _) = CreateTestContext(testDir.Root);

        var cat = service.CreateCategory(null, "Cat").Value;
        var prompt = service.CreatePrompt(cat.Id, "Duplicate content").Value;

        var dup = service.DuplicatePrompt(prompt.Id, cat.Id).Value;

        Assert.AreNotEqual(prompt.Id, dup.Id);
        Assert.AreEqual(cat.Id, dup.CategoryId);
        Assert.AreEqual(2, service.CurrentDocument.Prompts.Count);
    }

    [TestMethod]
    public void Duplicate_has_new_id()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var prompt = service.CreatePrompt(null, "Content").Value;
        var dup = service.DuplicatePrompt(prompt.Id, null).Value;

        Assert.AreNotEqual(prompt.Id, dup.Id);
    }

    [TestMethod]
    public void Duplicate_primary_failure_no_metadata_commit()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, faultWriter, _) = CreateTestContext(testDir.Root);

        var prompt = service.CreatePrompt(null, "Content").Value;

        faultWriter.ShouldFail = (path, callNum) => path.EndsWith("library.json", StringComparison.OrdinalIgnoreCase);

        Assert.Throws<IOException>(() => service.DuplicatePrompt(prompt.Id, null));
        Assert.AreEqual(1, service.CurrentDocument.Prompts.Count);
    }

    [TestMethod]
    public void Duplicate_primary_failure_cleanup()
    {
        using var testDir = new TestDirectory();
        var (service, paths, _, _, faultWriter, _) = CreateTestContext(testDir.Root);

        var prompt = service.CreatePrompt(null, "Content").Value;

        faultWriter.ShouldFail = (path, callNum) => path.EndsWith("library.json", StringComparison.OrdinalIgnoreCase);

        Assert.Throws<IOException>(() => service.DuplicatePrompt(prompt.Id, null));
        var files = Directory.GetFiles(paths.PromptsDirectory, "*.md");
        Assert.AreEqual(1, files.Length);
    }

    [TestMethod]
    public void Duplicate_backup_failure_commits()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, promptRepo, faultWriter, _) = CreateTestContext(testDir.Root);

        var prompt = service.CreatePrompt(null, "Content").Value;

        faultWriter.ShouldFail = (path, callNum) => path.EndsWith("library.backup.json", StringComparison.OrdinalIgnoreCase);

        var result = service.DuplicatePrompt(prompt.Id, null);
        Assert.IsNotNull(result.Warning);
        Assert.AreEqual(2, service.CurrentDocument.Prompts.Count);
    }

    [TestMethod]
    public void Duplicate_unavailable_source_fails()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, promptRepo, _, _) = CreateTestContext(testDir.Root);

        var prompt = service.CreatePrompt(null, "Content").Value;
        promptRepo.DeleteIfExists(prompt.Id);

        Assert.Throws<InvalidOperationException>(() => service.DuplicatePrompt(prompt.Id, null));
    }

    #endregion

    #region Navigation and Destination Tests

    [TestMethod]
    public void Home_breadcrumb()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var crumbs = service.GetBreadcrumbs(null);
        Assert.AreEqual(1, crumbs.Count);
        Assert.AreEqual("Home", crumbs[0].Name);
        Assert.IsNull(crumbs[0].CategoryId);
    }

    [TestMethod]
    public void One_level_breadcrumb()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var cat = service.CreateCategory(null, "Games").Value;
        var crumbs = service.GetBreadcrumbs(cat.Id);

        Assert.AreEqual(2, crumbs.Count);
        Assert.AreEqual("Home", crumbs[0].Name);
        Assert.AreEqual("Games", crumbs[1].Name);
    }

    [TestMethod]
    public void Deep_breadcrumb()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var cat1 = service.CreateCategory(null, "Games").Value;
        var cat2 = service.CreateCategory(cat1.Id, "Impl").Value;
        var cat3 = service.CreateCategory(cat2.Id, "Android").Value;

        var crumbs = service.GetBreadcrumbs(cat3.Id);
        Assert.AreEqual(4, crumbs.Count);
        Assert.AreEqual("Home", crumbs[0].Name);
        Assert.AreEqual("Games", crumbs[1].Name);
        Assert.AreEqual("Impl", crumbs[2].Name);
        Assert.AreEqual("Android", crumbs[3].Name);
    }

    [TestMethod]
    public void Destination_Home_first()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        service.CreateCategory(null, "B_Cat");
        service.CreateCategory(null, "A_Cat");

        var destinations = service.GetDestinations();
        Assert.AreEqual("Home", destinations[0].DisplayPath);
        Assert.IsNull(destinations[0].CategoryId);
    }

    [TestMethod]
    public void Destination_alphabetic_paths()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        service.CreateCategory(null, "Z_Cat");
        service.CreateCategory(null, "A_Cat");

        var destinations = service.GetDestinations();
        Assert.AreEqual("Home", destinations[0].DisplayPath);
        Assert.AreEqual("A_Cat", destinations[1].DisplayPath);
        Assert.AreEqual("Z_Cat", destinations[2].DisplayPath);
    }

    [TestMethod]
    public void Destination_root_name_Home_is_disambiguated()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var cat = service.CreateCategory(null, "Home").Value;

        var destinations = service.GetDestinations();
        Assert.AreEqual(2, destinations.Count);

        var root = destinations.First(d => !d.CategoryId.HasValue);
        var catDest = destinations.First(d => d.CategoryId == cat.Id);

        Assert.AreEqual("Home", root.DisplayPath);
        Assert.IsTrue(catDest.DisplayPath.StartsWith("Home ["));
    }

    [TestMethod]
    public void Destination_separator_collision_is_disambiguated()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var a = service.CreateCategory(null, "A").Value;
        var bUnderA = service.CreateCategory(a.Id, "B").Value;

        var aGtB = service.CreateCategory(null, "A > B").Value;

        var destinations = service.GetDestinations();
        var dest1 = destinations.First(d => d.CategoryId == bUnderA.Id);
        var dest2 = destinations.First(d => d.CategoryId == aGtB.Id);

        Assert.IsTrue(dest1.DisplayPath.Contains("["));
        Assert.IsTrue(dest2.DisplayPath.Contains("["));
    }

    #endregion

    #region Failure and Scale Tests

    [TestMethod]
    public void Large_prompt_test()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, promptRepo, _, _) = CreateTestContext(testDir.Root);

        string largeContent = new('x', 60000);
        var prompt = service.CreatePrompt(null, largeContent).Value;

        Assert.AreEqual(largeContent, promptRepo.Read(prompt.Id));

        string edited = largeContent + "extra";
        service.EditPrompt(prompt.Id, edited);
        Assert.AreEqual(edited, promptRepo.Read(prompt.Id));

        var dup = service.DuplicatePrompt(prompt.Id, null).Value;
        Assert.AreEqual(edited, promptRepo.Read(dup.Id));

        service.DeletePrompt(prompt.Id);
        service.DeletePrompt(dup.Id);
        Assert.AreEqual(0, service.CurrentDocument.Prompts.Count);
    }

    [TestMethod]
    public void Deep_hierarchy_test()
    {
        using var testDir = new TestDirectory();
        var (service, _, _, _, _, _) = CreateTestContext(testDir.Root);

        var a = service.CreateCategory(null, "A").Value;
        var b = service.CreateCategory(a.Id, "B").Value;
        var c = service.CreateCategory(b.Id, "C").Value;
        var d = service.CreateCategory(c.Id, "D").Value;
        var e = service.CreateCategory(d.Id, "E").Value;
        var f = service.CreateCategory(e.Id, "F").Value;

        var crumbs = service.GetBreadcrumbs(f.Id);
        Assert.AreEqual(7, crumbs.Count); // Home, A, B, C, D, E, F

        var destinations = service.GetDestinations();
        Assert.IsTrue(destinations.Any(dest => dest.DisplayPath == "A > B > C > D > E > F"));
    }

    #endregion
}