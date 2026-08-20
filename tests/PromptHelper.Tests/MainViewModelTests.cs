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
}