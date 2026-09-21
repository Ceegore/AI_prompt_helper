using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Desktop;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Platform.Linux.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AvaloniaDesktopHeadlessCoverageTests
{
    private sealed class RecordingLease : IAppInstanceLease
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    [TestMethod]
    public async Task Main_window_and_dialog_logic_runs_on_real_headless_Avalonia_runtime()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            using HeadlessUnitTestSession session =
                HeadlessUnitTestSession.StartNew(typeof(App));

            await session.Dispatch(() =>
            {
                LinuxNativeFileSystemGuard.EnsureSupported();

                App app = Assert.IsInstanceOfType<App>(Application.Current);
                app.ApplyTheme(useDarkMode: true);
                Assert.AreEqual(ThemeVariant.Dark, app.RequestedThemeVariant);
                app.ApplyTheme(useDarkMode: false);
                Assert.AreEqual(ThemeVariant.Light, app.RequestedThemeVariant);

                Assert.IsNotNull(Program.BuildAvaloniaApp());

                var settingsStore = new LinuxDesktopSettingsStore(root);
                var settings = new AppSettings
                {
                    SchemaVersion = AppSettings.CurrentSchemaVersion,
                    DataRootPath = root,
                    UseDarkMode = false
                };

                var library = new LinuxPromptLibraryStore(root);
                library.Initialize();

                CategoryRecord category =
                    library.CreateCategory(null, "Headless category").Value;
                CategoryRecord child =
                    library.CreateCategory(category.Id, "Child").Value;

                PromptRecord custom =
                    library.CreatePrompt(null, "custom body", "Custom title").Value;
                PromptRecord firstLine =
                    library.CreatePrompt(
                        null,
                        "\r\n" + new string('F', 90) + "\nsecond",
                        null).Value;
                PromptRecord blank =
                    library.CreatePrompt(null, " \r\n  ", null).Value;
                PromptRecord longPreview =
                    library.CreatePrompt(null, new string('P', 300), null).Value;
                PromptRecord unavailable =
                    library.CreatePrompt(null, "will disappear", null).Value;

                File.Delete(
                    Path.Combine(
                        root,
                        "prompts",
                        unavailable.Id.ToString("N") + ".md"));

                var lease = new RecordingLease();
                var window = new MainWindow(
                    settingsStore,
                    settings,
                    library,
                    lease);

                window.Show();

                TextBlock dataRoot = Require<TextBlock>(window, "DataRootText");
                TextBlock status = Require<TextBlock>(window, "StatusText");
                Border warningBorder = Require<Border>(window, "WarningBorder");
                TextBlock warningText = Require<TextBlock>(window, "WarningText");
                ItemsControl promptItems = Require<ItemsControl>(window, "PromptItems");
                ItemsControl categoryItems = Require<ItemsControl>(window, "CategoryItems");
                ItemsControl breadcrumbItems = Require<ItemsControl>(window, "BreadcrumbItems");
                ItemsControl recentItems = Require<ItemsControl>(window, "RecentItems");

                Assert.AreEqual(Path.GetFullPath(root), dataRoot.Text);
                Assert.AreEqual("Ready", status.Text);

                PromptDisplay[] displays =
                    promptItems.ItemsSource!.Cast<PromptDisplay>().ToArray();

                Assert.AreEqual(
                    "Custom title",
                    displays.Single(item => item.Id == custom.Id).Title);

                string longTitle =
                    displays.Single(item => item.Id == firstLine.Id).Title;
                Assert.AreEqual(81, longTitle.Length);
                Assert.IsTrue(longTitle.EndsWith("…", StringComparison.Ordinal));

                Assert.AreEqual(
                    "Untitled prompt",
                    displays.Single(item => item.Id == blank.Id).Title);

                PromptDisplay preview =
                    displays.Single(item => item.Id == longPreview.Id);
                Assert.AreEqual(261, preview.Preview.Length);
                Assert.IsTrue(preview.Preview.EndsWith("…", StringComparison.Ordinal));

                PromptDisplay missing =
                    displays.Single(item => item.Id == unavailable.Id);
                Assert.AreEqual("Unavailable prompt", missing.Title);
                StringAssert.Contains(missing.Preview, "Could not load prompt body");

                CategoryDisplay topCategory =
                    categoryItems.ItemsSource!
                        .Cast<CategoryDisplay>()
                        .Single(item => item.Id == category.Id);

                Invoke(
                    window,
                    "OpenCategory_Click",
                    new Button { DataContext = topCategory },
                    new RoutedEventArgs());

                CategoryDisplay nested =
                    Require<ItemsControl>(window, "CategoryItems")
                        .ItemsSource!
                        .Cast<CategoryDisplay>()
                        .Single(item => item.Id == child.Id);
                Assert.AreEqual("Child", nested.Name);

                BreadcrumbDisplay home =
                    breadcrumbItems.ItemsSource!
                        .Cast<BreadcrumbDisplay>()
                        .First(item => item.CategoryId is null);

                Invoke(
                    window,
                    "Breadcrumb_Click",
                    new Button { DataContext = home },
                    new RoutedEventArgs());

                Assert.IsTrue(
                    categoryItems.ItemsSource!
                        .Cast<CategoryDisplay>()
                        .Any(item => item.Id == category.Id));

                // Guard branches on event handlers must be safe with unrelated controls.
                string[] guardedHandlers =
                [
                    "Breadcrumb_Click",
                    "OpenCategory_Click",
                    "RenameCategory_Click",
                    "DeleteCategory_Click",
                    "CopyPrompt_Click",
                    "RecentCopy_Click",
                    "EditPrompt_Click",
                    "MovePrompt_Click",
                    "DuplicatePrompt_Click",
                    "DeletePrompt_Click"
                ];
                foreach (string handler in guardedHandlers)
                {
                    Invoke(
                        window,
                        handler,
                        new Button { DataContext = new object() },
                        new RoutedEventArgs());
                }

                Invoke(window, "ShowWarning", "headless warning");
                Assert.IsTrue(warningBorder.IsVisible);
                Assert.AreEqual("headless warning", warningText.Text);

                Invoke(window, "HideWarning");
                Assert.IsFalse(warningBorder.IsVisible);
                Assert.AreEqual(string.Empty, warningText.Text);

                for (int i = 0; i < 4; i++)
                {
                    Invoke(
                        window,
                        "RecordRecent",
                        Guid.NewGuid(),
                        $"Recent {i}",
                        $"Preview {i}");
                }

                RecentPromptDisplay[] recent =
                    recentItems.ItemsSource!
                        .Cast<RecentPromptDisplay>()
                        .ToArray();
                Assert.AreEqual(3, recent.Length);
                Assert.AreEqual("Recent 3", recent[0].Title);

                Guid removed = recent[1].Id;
                Invoke(window, "RemoveRecent", removed);
                Assert.IsFalse(
                    recentItems.ItemsSource!
                        .Cast<RecentPromptDisplay>()
                        .Any(item => item.Id == removed));

                Task mutation = (Task)Invoke(
                    window,
                    "RunMutationAsync",
                    (Func<OperationResult>)(() =>
                        new OperationResult("mutation warning")),
                    "Mutation succeeded.")!;
                mutation.GetAwaiter().GetResult();

                Assert.AreEqual("Mutation succeeded.", status.Text);
                Assert.AreEqual("mutation warning", warningText.Text);
                Assert.IsTrue(warningBorder.IsVisible);

                MethodInfo genericMutation =
                    typeof(MainWindow)
                        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                        .Single(method =>
                            method.Name == "RunMutationAsync" &&
                            method.IsGenericMethodDefinition);

                Task genericTask = (Task)genericMutation
                    .MakeGenericMethod(typeof(CategoryRecord))
                    .Invoke(
                        window,
                        [
                            (Func<OperationResult<CategoryRecord>>)(() =>
                                new OperationResult<CategoryRecord>(
                                    category,
                                    "generic warning")),
                            "Generic mutation succeeded."
                        ])!;
                genericTask.GetAwaiter().GetResult();

                Assert.AreEqual("Generic mutation succeeded.", status.Text);
                Assert.AreEqual("generic warning", warningText.Text);

                // Constructor coverage for all concrete dialogs and their control trees.
                var startupError = new StartupErrorWindow("startup failed");
                Assert.AreEqual("Prompt Helper", startupError.Title);
                Assert.IsInstanceOfType<StackPanel>(startupError.Content);

                var nameDialog = new NameDialog(
                    "Rename",
                    "Name",
                    "Initial");
                Assert.AreEqual("Rename", nameDialog.Title);
                Assert.IsInstanceOfType<StackPanel>(nameDialog.Content);

                var editor = new PromptEditorDialog(
                    "Edit",
                    "body",
                    "headline");
                Assert.AreEqual("Edit", editor.Title);
                Assert.IsInstanceOfType<StackPanel>(editor.Content);

                DestinationDisplay[] destinations =
                library.GetDestinations()
                    .Select(DestinationDisplay.From)
                    .ToArray();
                var move = new MovePromptDialog(destinations, category.Id);
                Assert.AreEqual("Move prompt", move.Title);
                Assert.IsInstanceOfType<StackPanel>(move.Content);

                var settingsDialog =
                    new SettingsDialog(root, useDarkMode: true);
                Assert.AreEqual("Settings", settingsDialog.Title);
                Assert.IsInstanceOfType<StackPanel>(settingsDialog.Content);

                DestinationDisplay destination =
                    DestinationDisplay.From(
                        new DestinationRecord(category.Id, "Headless category"));
                Assert.AreEqual("Headless category", destination.ToString());

                _ = new CategoryDisplay(category.Id, category.Name);
                _ = new BreadcrumbDisplay(category.Id, category.Name);
                _ = new PromptDisplay(custom.Id, "T", "P", "C");
                _ = new RecentPromptDisplay(custom.Id, "T", "P");
                _ = new PromptDialogResult("content", "title");
                _ = new MoveDialogResult(category.Id);
                _ = new SettingsDialogResult(root, true);

                window.Close();
                Assert.AreEqual(1, lease.DisposeCount);
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static T Require<T>(Control root, string name)
        where T : Control =>
        root.FindControl<T>(name)
        ?? throw new AssertFailedException(
            $"Required headless control '{name}' was not found.");

    private static object? Invoke(
        object target,
        string methodName,
        params object?[] arguments)
    {
        MethodInfo method =
            target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(info =>
                    info.Name == methodName &&
                    !info.IsGenericMethodDefinition &&
                    info.GetParameters().Length == arguments.Length);

        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-AvaloniaHeadless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
