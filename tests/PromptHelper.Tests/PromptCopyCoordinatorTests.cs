using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Infrastructure;
using PromptHelper.Models;
using PromptHelper.Services;
using PromptHelper.ViewModels;

namespace PromptHelper.Tests;

[TestClass]
public sealed class PromptCopyCoordinatorTests
{
    private static (PromptLibraryService service, PromptRepository promptRepo, MainViewModel vm, AppPaths paths) CreateContext(string root)
    {
        var paths = new AppPaths(root);
        paths.EnsureRootDirectory();
        paths.EnsureDataDirectories();

        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startup = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);
        var initResult = startup.LoadOrInitialize();
        var service = new PromptLibraryService(initResult.Document, libRepo, promptRepo);
        var vm = new MainViewModel(service, promptRepo, paths.RootDirectory);

        return (service, promptRepo, vm, paths);
    }

    [TestMethod]
    public void CopyCoordinator_success_copies_full_text_then_records_recent()
    {
        using var testDir = new TestDirectory();
        var (service, promptRepo, vm, _) = CreateContext(testDir.Root);

        string multilineContent = "First Line of Prompt\r\nSecond Line\nThird Line with details.";
        var prompt = service.CreatePrompt(null, multilineContent, "Custom Headline").Value;
        vm.Refresh();

        var fake = new FakeClipboardService();
        var coordinator = new PromptCopyCoordinator(vm, fake);

        string result = coordinator.Copy(prompt.Id, "Custom Headline");

        Assert.AreEqual(multilineContent, result);
        Assert.AreEqual(multilineContent, fake.LastCopiedText);
        Assert.AreEqual(1, vm.RecentPrompts.Count);
        Assert.AreEqual(prompt.Id, vm.RecentPrompts[0].Id);
        Assert.AreEqual("Custom Headline", vm.RecentPrompts[0].Headline);
    }

    [TestMethod]
    public void CopyCoordinator_clipboard_failure_does_not_change_recent_history()
    {
        using var testDir = new TestDirectory();
        var (service, _, vm, _) = CreateContext(testDir.Root);

        var prompt = service.CreatePrompt(null, "Prompt Body", "Headline").Value;
        vm.Refresh();

        var fake = new FakeClipboardService
        {
            Failure = new InvalidOperationException("Clipboard busy")
        };
        var coordinator = new PromptCopyCoordinator(vm, fake);

        Assert.Throws<InvalidOperationException>(() => coordinator.Copy(prompt.Id, "Headline"));

        Assert.AreEqual(0, vm.RecentPrompts.Count);
    }

    [TestMethod]
    public void CopyCoordinator_reads_current_updated_body_not_stale_body()
    {
        using var testDir = new TestDirectory();
        var (service, _, vm, _) = CreateContext(testDir.Root);

        var prompt = service.CreatePrompt(null, "Original Body", "Headline").Value;
        vm.Refresh();

        var fake = new FakeClipboardService();
        var coordinator = new PromptCopyCoordinator(vm, fake);

        coordinator.Copy(prompt.Id, "Headline");
        Assert.AreEqual("Original Body", fake.LastCopiedText);

        // Edit body
        service.EditPrompt(prompt.Id, "Updated Fresh Body", "Headline");
        vm.Refresh();

        // Copy again
        coordinator.Copy(prompt.Id, "Headline");
        Assert.AreEqual("Updated Fresh Body", fake.LastCopiedText);
    }
}
