using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;
using PromptHelper.ViewModels;

namespace PromptHelper.Tests;

[TestClass]
public sealed class PromptLoadingPerformanceTests
{
    private static (TestDirectory Directory, MainViewModel ViewModel, PromptLibraryService Service, AppPaths Paths)
        CreateContext()
    {
        var directory = new TestDirectory();
        var paths = new AppPaths(directory.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libraryRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startup = new LibraryStartupService(paths, libraryRepo, promptRepo, deleter, writer);
        var startupResult = startup.LoadOrInitialize();
        var service = new PromptLibraryService(startupResult.Document, libraryRepo, promptRepo);
        var viewModel = new MainViewModel(service, promptRepo, paths.RootDirectory);
        return (directory, viewModel, service, paths);
    }

    [TestMethod]
    public void Refresh_creates_metadata_only_cards_and_loads_full_body_on_demand()
    {
        var context = CreateContext();
        using (context.Directory)
        {
            context.Service.CreatePrompt(null, "Full prompt body", "Lazy prompt");
            context.ViewModel.Refresh();

            PromptCardViewModel card = context.ViewModel.Prompts.Single();
            Assert.IsFalse(card.IsContentLoaded);

            Assert.AreEqual("Full prompt body", card.Content);
            Assert.IsTrue(card.IsContentLoaded);
        }
    }

    [TestMethod]
    public async Task Preview_loading_is_bounded_and_does_not_retain_the_full_body()
    {
        var context = CreateContext();
        using (context.Directory)
        {
            string body = new('x', 100_000);
            context.Service.CreatePrompt(null, body, null);
            context.ViewModel.Refresh();

            await context.ViewModel.LoadPromptPreviewsAsync();

            PromptCardViewModel card = context.ViewModel.Prompts.Single();
            Assert.IsFalse(card.IsContentLoaded);
            Assert.IsTrue(card.DisplayText.Length <= 4002);
            Assert.IsTrue(card.DisplayText.EndsWith("\n…", StringComparison.Ordinal));
            Assert.AreEqual(100_000, card.Content.Length);
        }
    }

    [TestMethod]
    public async Task Invalid_utf8_preview_marks_only_that_card_unavailable()
    {
        var context = CreateContext();
        using (context.Directory)
        {
            var prompt = context.Service.CreatePrompt(null, "Initially valid", "Broken body").Value;
            File.WriteAllBytes(context.Paths.GetPromptPath(prompt.Id), [0xC3, 0x28]);
            context.ViewModel.Refresh();

            await context.ViewModel.LoadPromptPreviewsAsync();

            PromptCardViewModel card = context.ViewModel.Prompts.Single();
            Assert.IsFalse(card.IsContentAvailable);
            Assert.IsFalse(card.IsContentLoaded);
            Assert.IsNotNull(card.LoadError);
        }
    }

    [TestMethod]
    public void Prompt_list_uses_recycling_virtualization()
    {
        string xaml = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "src", "PromptHelper", "MainWindow.xaml"));

        StringAssert.Contains(xaml, "VirtualizingPanel.IsVirtualizing=\"True\"");
        StringAssert.Contains(xaml, "VirtualizingPanel.VirtualizationMode=\"Recycling\"");
        StringAssert.Contains(xaml, "<VirtualizingStackPanel/>");
    }
}
