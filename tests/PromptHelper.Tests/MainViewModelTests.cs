using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;
using PromptHelper.ViewModels;

namespace PromptHelper.Tests;

[TestClass]
public sealed class MainViewModelTests
{
    private static (MainViewModel viewModel, PromptLibraryService service, PromptRepository promptRepo)
        CreateTestContext(string root)
    {
        var paths = new AppPaths(root);
        var baseWriter = new AtomicTextWriter();
        var baseDeleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, baseWriter);
        var promptRepo = new PromptRepository(paths, baseWriter, baseDeleter);
        var startupService = new LibraryStartupService(paths, libRepo, promptRepo, baseDeleter, baseWriter);
        var startupResult = startupService.LoadOrInitialize();

        var service = new PromptLibraryService(startupResult.Document, libRepo, promptRepo);
        var vm = new MainViewModel(service, promptRepo, paths.RootDirectory);

        return (vm, service, promptRepo);
    }

    #region Preview Tests

    [TestMethod]
    public void Empty_prompt_preview()
    {
        Assert.AreEqual("(Empty prompt)", PromptCardViewModel.ComputePreviewTitle("", true));
        Assert.AreEqual("(Empty prompt)", PromptCardViewModel.ComputePreviewTitle("   \n\r\n\t", true));
    }

    [TestMethod]
    public void Normal_first_line()
    {
        string content = "First line text\nSecond line";
        Assert.AreEqual("First line text", PromptCardViewModel.ComputePreviewTitle(content, true));
    }

    [TestMethod]
    public void Leading_blank_lines_ignored()
    {
        string content = "\r\n  \n\nActual Title\nMore text";
        Assert.AreEqual("Actual Title", PromptCardViewModel.ComputePreviewTitle(content, true));
    }

    [TestMethod]
    public void More_than_80_text_elements_ellipsized()
    {
        string longLine = new('a', 100);
        string preview = PromptCardViewModel.ComputePreviewTitle(longLine, true);

        Assert.IsTrue(preview.EndsWith("…"));
        Assert.AreEqual(80, PromptHelper.Infrastructure.TextUtilities.GetTextElementCount(preview));
    }

    [TestMethod]
    public void Emoji_not_split_mid_text_element()
    {
        string emojis = string.Concat(Enumerable.Repeat("🚀", 85));
        string preview = PromptCardViewModel.ComputePreviewTitle(emojis, true);

        Assert.IsTrue(preview.EndsWith("…"));
        Assert.AreEqual(80, PromptHelper.Infrastructure.TextUtilities.GetTextElementCount(preview));
    }

    [TestMethod]
    public void Unavailable_prompt_preview()
    {
        Assert.AreEqual("(Unavailable prompt)", PromptCardViewModel.ComputePreviewTitle("Some content", false));
    }

    #endregion

    #region ViewModel State and Refresh Tests

    [TestMethod]
    public void Initial_state_loads_defaults()
    {
        using var testDir = new TestDirectory();
        var (vm, _, _) = CreateTestContext(testDir.Root);

        Assert.IsNull(vm.CurrentCategoryId);
        Assert.AreEqual(1, vm.Breadcrumbs.Count);
        Assert.AreEqual("Home", vm.Breadcrumbs[0].Name);
        Assert.IsTrue(vm.Breadcrumbs[0].IsCurrent);

        Assert.AreEqual(2, vm.ChildCategories.Count); // Games, Tools
        Assert.AreEqual(0, vm.Prompts.Count); // Home has no prompts in defaults
        Assert.IsFalse(vm.HasPrompts);
        Assert.IsTrue(vm.HasNoPrompts);
        Assert.IsTrue(vm.HasChildCategories);
    }

    [TestMethod]
    public void Navigation_updates_state()
    {
        using var testDir = new TestDirectory();
        var (vm, _, _) = CreateTestContext(testDir.Root);

        var gamesCat = vm.ChildCategories.First(c => c.Name == "Games");
        vm.NavigateTo(gamesCat.Id);

        Assert.AreEqual(gamesCat.Id, vm.CurrentCategoryId);
        Assert.AreEqual(2, vm.Breadcrumbs.Count);
        Assert.AreEqual("Home", vm.Breadcrumbs[0].Name);
        Assert.IsFalse(vm.Breadcrumbs[0].IsCurrent);
        Assert.AreEqual("Games", vm.Breadcrumbs[1].Name);
        Assert.IsTrue(vm.Breadcrumbs[1].IsCurrent);

        Assert.AreEqual(3, vm.ChildCategories.Count); // Planning, Implementation, Testing
    }

    [TestMethod]
    public void CreateCategory_refreshes_collection()
    {
        using var testDir = new TestDirectory();
        var (vm, _, _) = CreateTestContext(testDir.Root);

        vm.CreateCategory("NewTopCategory");

        Assert.AreEqual(3, vm.ChildCategories.Count);
        Assert.IsTrue(vm.ChildCategories.Any(c => c.Name == "NewTopCategory"));
    }

    [TestMethod]
    public void RenameCategory_refreshes_collection()
    {
        using var testDir = new TestDirectory();
        var (vm, _, _) = CreateTestContext(testDir.Root);

        var toolsCat = vm.ChildCategories.First(c => c.Name == "Tools");
        vm.RenameCategory(toolsCat.Id, "Utilities");

        Assert.IsTrue(vm.ChildCategories.Any(c => c.Name == "Utilities"));
        Assert.IsFalse(vm.ChildCategories.Any(c => c.Name == "Tools"));
    }

    [TestMethod]
    public void DeleteCategory_refreshes_collection()
    {
        using var testDir = new TestDirectory();
        var (vm, _, _) = CreateTestContext(testDir.Root);

        var cat = vm.CreateCategory("TempCategory").Value;
        Assert.AreEqual(3, vm.ChildCategories.Count);

        vm.DeleteCategory(cat.Id);
        Assert.AreEqual(2, vm.ChildCategories.Count);
    }

    [TestMethod]
    public void CreatePrompt_refreshes_prompts_list()
    {
        using var testDir = new TestDirectory();
        var (vm, _, _) = CreateTestContext(testDir.Root);

        vm.CreatePrompt("Prompt on Home");

        Assert.AreEqual(1, vm.Prompts.Count);
        Assert.AreEqual("Prompt on Home", vm.Prompts[0].Content);
        Assert.IsTrue(vm.HasPrompts);
        Assert.IsFalse(vm.HasNoPrompts);
    }

    [TestMethod]
    public void EditPrompt_refreshes_prompts_list()
    {
        using var testDir = new TestDirectory();
        var (vm, _, _) = CreateTestContext(testDir.Root);

        var prompt = vm.CreatePrompt("Initial content").Value;
        vm.EditPrompt(prompt.Id, "Updated content");

        Assert.AreEqual(1, vm.Prompts.Count);
        Assert.AreEqual("Updated content", vm.Prompts[0].Content);
    }

    [TestMethod]
    public void DeletePrompt_refreshes_prompts_list()
    {
        using var testDir = new TestDirectory();
        var (vm, _, _) = CreateTestContext(testDir.Root);

        var prompt = vm.CreatePrompt("To delete").Value;
        Assert.AreEqual(1, vm.Prompts.Count);

        vm.DeletePrompt(prompt.Id);
        Assert.AreEqual(0, vm.Prompts.Count);
        Assert.IsTrue(vm.HasNoPrompts);
    }

    [TestMethod]
    public void MovePrompt_refreshes_prompts_list()
    {
        using var testDir = new TestDirectory();
        var (vm, _, _) = CreateTestContext(testDir.Root);

        var prompt = vm.CreatePrompt("Move from Home").Value;
        var gamesCat = vm.ChildCategories.First(c => c.Name == "Games");

        vm.MovePrompt(prompt.Id, gamesCat.Id);

        // On Home, prompt is now gone
        Assert.AreEqual(0, vm.Prompts.Count);

        // Navigate to Games, prompt is now there
        vm.NavigateTo(gamesCat.Id);
        Assert.IsTrue(vm.Prompts.Any(p => p.Id == prompt.Id));
    }

    [TestMethod]
    public void DuplicatePrompt_refreshes_prompts_list()
    {
        using var testDir = new TestDirectory();
        var (vm, _, _) = CreateTestContext(testDir.Root);

        var prompt = vm.CreatePrompt("Original prompt").Value;
        var dup = vm.DuplicatePrompt(prompt.Id, null).Value;

        Assert.AreEqual(2, vm.Prompts.Count);
        Assert.IsTrue(vm.Prompts.Any(p => p.Id == prompt.Id));
        Assert.IsTrue(vm.Prompts.Any(p => p.Id == dup.Id));
    }

    #endregion

    #region Recent Prompts Tests

    [TestMethod]
    public void RecentPrompts_starts_empty()
    {
        using var testDir = new TestDirectory();
        var (vm, _, _) = CreateTestContext(testDir.Root);

        Assert.AreEqual(0, vm.RecentPrompts.Count);
    }

    [TestMethod]
    public void RecordSuccessfulPromptCopy_adds_newest_first()
    {
        using var testDir = new TestDirectory();
        var (vm, _, _) = CreateTestContext(testDir.Root);

        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var idC = Guid.NewGuid();

        vm.RecordSuccessfulPromptCopy(idA, "Headline A", "Body A");
        vm.RecordSuccessfulPromptCopy(idB, "Headline B", "Body B");
        vm.RecordSuccessfulPromptCopy(idC, "Headline C", "Body C");

        Assert.AreEqual(3, vm.RecentPrompts.Count);
        Assert.AreEqual(idC, vm.RecentPrompts[0].Id);
        Assert.AreEqual(idB, vm.RecentPrompts[1].Id);
        Assert.AreEqual(idA, vm.RecentPrompts[2].Id);
    }

    [TestMethod]
    public void RecordSuccessfulPromptCopy_fourth_unique_evicts_oldest()
    {
        using var testDir = new TestDirectory();
        var (vm, _, _) = CreateTestContext(testDir.Root);

        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var idC = Guid.NewGuid();
        var idD = Guid.NewGuid();

        vm.RecordSuccessfulPromptCopy(idA, "Headline A", "Body A");
        vm.RecordSuccessfulPromptCopy(idB, "Headline B", "Body B");
        vm.RecordSuccessfulPromptCopy(idC, "Headline C", "Body C");
        vm.RecordSuccessfulPromptCopy(idD, "Headline D", "Body D");

        Assert.AreEqual(3, vm.RecentPrompts.Count);
        Assert.AreEqual(idD, vm.RecentPrompts[0].Id);
        Assert.AreEqual(idC, vm.RecentPrompts[1].Id);
        Assert.AreEqual(idB, vm.RecentPrompts[2].Id);
        Assert.IsFalse(vm.RecentPrompts.Any(x => x.Id == idA));
    }

    [TestMethod]
    public void RecordSuccessfulPromptCopy_recopy_existing_moves_to_first_without_duplicate()
    {
        using var testDir = new TestDirectory();
        var (vm, _, _) = CreateTestContext(testDir.Root);

        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var idC = Guid.NewGuid();
        var idD = Guid.NewGuid();

        vm.RecordSuccessfulPromptCopy(idA, "Headline A", "Body A");
        vm.RecordSuccessfulPromptCopy(idB, "Headline B", "Body B");
        vm.RecordSuccessfulPromptCopy(idC, "Headline C", "Body C");
        vm.RecordSuccessfulPromptCopy(idD, "Headline D", "Body D"); // [D, C, B]

        vm.RecordSuccessfulPromptCopy(idC, "Headline C Updated", "Body C Updated"); // [C, D, B]

        Assert.AreEqual(3, vm.RecentPrompts.Count);
        Assert.AreEqual(idC, vm.RecentPrompts[0].Id);
        Assert.AreEqual("Headline C Updated", vm.RecentPrompts[0].Headline);
        Assert.AreEqual("Body C Updated", vm.RecentPrompts[0].Excerpt);
        Assert.AreEqual(idD, vm.RecentPrompts[1].Id);
        Assert.AreEqual(idB, vm.RecentPrompts[2].Id);
    }

    [TestMethod]
    public void Refresh_and_navigation_does_not_clear_RecentPrompts()
    {
        using var testDir = new TestDirectory();
        var (vm, _, _) = CreateTestContext(testDir.Root);

        var idA = Guid.NewGuid();
        vm.RecordSuccessfulPromptCopy(idA, "Headline A", "Body A");

        vm.Refresh();
        Assert.AreEqual(1, vm.RecentPrompts.Count);

        var gamesCat = vm.ChildCategories.First(c => c.Name == "Games");
        vm.NavigateTo(gamesCat.Id);
        Assert.AreEqual(1, vm.RecentPrompts.Count);
    }

    [TestMethod]
    public void EditPrompt_updates_headline_and_excerpt_in_RecentPrompts_without_changing_order()
    {
        using var testDir = new TestDirectory();
        var (vm, _, _) = CreateTestContext(testDir.Root);

        var promptA = vm.CreatePrompt("Body A", "Title A").Value;
        var promptB = vm.CreatePrompt("Body B", "Title B").Value;

        vm.RecordSuccessfulPromptCopy(promptA.Id, "Title A", "Body A");
        vm.RecordSuccessfulPromptCopy(promptB.Id, "Title B", "Body B");

        // List is [B, A]
        Assert.AreEqual(promptB.Id, vm.RecentPrompts[0].Id);
        Assert.AreEqual(promptA.Id, vm.RecentPrompts[1].Id);

        vm.EditPrompt(promptA.Id, "New Body A", "New Title A");

        // Order is still [B, A], but A's headline and excerpt are updated
        Assert.AreEqual(promptB.Id, vm.RecentPrompts[0].Id);
        Assert.AreEqual(promptA.Id, vm.RecentPrompts[1].Id);
        Assert.AreEqual("New Title A", vm.RecentPrompts[1].Headline);
        Assert.AreEqual("New Body A", vm.RecentPrompts[1].Excerpt);
    }

    [TestMethod]
    public void DeletePrompt_removes_from_RecentPrompts()
    {
        using var testDir = new TestDirectory();
        var (vm, _, _) = CreateTestContext(testDir.Root);

        var prompt = vm.CreatePrompt("Body", "Title").Value;
        vm.RecordSuccessfulPromptCopy(prompt.Id, "Title", "Body");
        Assert.AreEqual(1, vm.RecentPrompts.Count);

        vm.DeletePrompt(prompt.Id);
        Assert.AreEqual(0, vm.RecentPrompts.Count);
    }

    #endregion
}