using System;
using System.IO;
using System.Reflection;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;
using PromptHelper.ViewModels;

namespace PromptHelper.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MainWindowCoverageTests
{
    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void Mutation_execution_covers_success_ordinary_failure_and_fatal_restart()
    {
        WpfTestHost.Invoke(() =>
        {
            using var context = WindowContext.Create();
            MainWindow window = context.Window;

            Assert.AreEqual(
                LibraryMutationExecutionResult.Succeeded,
                window.ExecuteLibraryMutationForTests(() => { }));

            foreach (Exception ordinary in new Exception[]
            {
                new IOException("io"),
                new UnauthorizedAccessException("denied"),
                new InvalidOperationException("invalid"),
                new SecurityException("security")
            })
            {
                Assert.AreEqual(
                    LibraryMutationExecutionResult.OrdinaryFailure,
                    window.ExecuteLibraryMutationForTests(() => throw ordinary));
                Assert.IsFalse(window.FatalMutationShutdownRequested);
            }

            bool operationReached = false;
            var fatal = new CommittedAtomicReplacementRequiresRestartException(
                Guid.NewGuid(),
                Path.Combine(context.Root, "library.json"),
                "bookkeeping incomplete",
                new IOException("cleanup"));

            Assert.AreEqual(
                LibraryMutationExecutionResult.FatalRestartRequired,
                window.ExecuteLibraryMutationForTests(() => throw fatal));

            Assert.IsTrue(window.FatalMutationShutdownRequested);
            Assert.IsTrue(context.Lifetime.ShutdownRequested);
            Assert.AreEqual(1, context.Lifetime.ShutdownRequestCount);
            Assert.AreEqual(1, context.RestartMessages.Count);
            StringAssert.Contains(context.RestartMessages[0].Message, "bookkeeping incomplete");
            Assert.AreEqual("Restart Required", context.RestartMessages[0].Title);

            Assert.AreEqual(
                LibraryMutationExecutionResult.FatalRestartRequired,
                window.ExecuteLibraryMutationForTests(() => operationReached = true));
            Assert.IsFalse(operationReached);
            Assert.AreEqual(1, context.Lifetime.ShutdownRequestCount);
        });
    }

    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void Complete_settings_dialog_only_requests_restart_when_required()
    {
        WpfTestHost.Invoke(() =>
        {
            using var context = WindowContext.Create();

            context.Window.CompleteSettingsDialog(
                dialogResult: false,
                restartRequired: false);

            Assert.IsFalse(context.Lifetime.ShutdownRequested);
            Assert.AreEqual(0, context.RestartMessages.Count);

            context.Window.CompleteSettingsDialog(
                dialogResult: true,
                restartRequired: true);

            Assert.IsTrue(context.Lifetime.ShutdownRequested);
            Assert.AreEqual(1, context.Lifetime.ShutdownRequestCount);
            Assert.AreEqual(1, context.RestartMessages.Count);
            StringAssert.Contains(
                context.RestartMessages[0].Message,
                "must close now");
        });
    }

    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void Complete_settings_dialog_requests_shutdown_even_if_restart_message_fails()
    {
        WpfTestHost.Invoke(() =>
        {
            using var context = WindowContext.Create(
                showRestartMessage: (_, _) => throw new InvalidOperationException("message failed"));

            Assert.Throws<InvalidOperationException>(() =>
                context.Window.CompleteSettingsDialog(
                    dialogResult: true,
                    restartRequired: true));

            Assert.IsTrue(context.Lifetime.ShutdownRequested);
            Assert.AreEqual(1, context.Lifetime.ShutdownRequestCount);
        });
    }

    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void Navigation_handlers_follow_valid_view_models_and_ignore_unrelated_data_context()
    {
        WpfTestHost.Invoke(() =>
        {
            using var context = WindowContext.Create();

            CategoryRecord parent =
                context.Service.CreateCategory(null, "Parent").Value;
            CategoryRecord child =
                context.Service.CreateCategory(parent.Id, "Child").Value;
            context.ViewModel.Refresh();

            CategoryItemViewModel parentVm =
                context.ViewModel.ChildCategories.Single(x => x.Id == parent.Id);

            InvokePrivate(
                context.Window,
                "OpenCategoryButton_Click",
                new Button { DataContext = parentVm },
                new RoutedEventArgs());

            Assert.AreEqual(parent.Id, context.ViewModel.CurrentCategoryId);
            Assert.IsTrue(context.ViewModel.ChildCategories.Any(x => x.Id == child.Id));

            BreadcrumbItemViewModel home =
                context.ViewModel.Breadcrumbs.First(x => x.CategoryId is null);

            InvokePrivate(
                context.Window,
                "BreadcrumbButton_Click",
                new Button { DataContext = home },
                new RoutedEventArgs());

            Assert.IsNull(context.ViewModel.CurrentCategoryId);

            InvokePrivate(
                context.Window,
                "OpenCategoryButton_Click",
                new Button { DataContext = new object() },
                new RoutedEventArgs());
            Assert.IsNull(context.ViewModel.CurrentCategoryId);

            InvokePrivate(
                context.Window,
                "BreadcrumbButton_Click",
                new Button { DataContext = new object() },
                new RoutedEventArgs());
            Assert.IsNull(context.ViewModel.CurrentCategoryId);
        });
    }

    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void Category_actions_open_context_menu_and_menu_item_resolution_is_fail_closed()
    {
        WpfTestHost.Invoke(() =>
        {
            using var context = WindowContext.Create();

            var menu = new ContextMenu();
            var item = new MenuItem { Header = "Rename" };
            menu.Items.Add(item);

            var category = new CategoryItemViewModel(
                Guid.NewGuid(),
                null,
                "Category");
            var button = new Button
            {
                DataContext = category,
                ContextMenu = menu
            };

            InvokePrivate(
                context.Window,
                "CategoryActionsButton_Click",
                button,
                new RoutedEventArgs());

            Assert.AreSame(button, menu.PlacementTarget);
            Assert.IsTrue(menu.IsOpen);

            MethodInfo resolver = typeof(MainWindow).GetMethod(
                "TryGetCategoryFromMenuItem",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            object?[] args = [item, null];
            bool success = (bool)resolver.Invoke(null, args)!;
            Assert.IsTrue(success);
            Assert.AreSame(category, args[1]);

            object?[] invalid = [new object(), null];
            Assert.IsFalse((bool)resolver.Invoke(null, invalid)!);
            Assert.IsNull(invalid[1]);

            menu.IsOpen = false;
        });
    }

    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void Private_copy_helper_uses_clipboard_and_records_recent_prompt()
    {
        WpfTestHost.Invoke(() =>
        {
            using var context = WindowContext.Create();
            PromptRecord prompt =
                context.Service.CreatePrompt(
                    null,
                    "body for clipboard",
                    "Clipboard title").Value;
            context.ViewModel.Refresh();

            string copied = (string)InvokePrivate(
                context.Window,
                "CopyPromptToClipboard",
                prompt.Id,
                "Clipboard title")!;

            Assert.AreEqual("body for clipboard", copied);
            Assert.AreEqual("body for clipboard", context.Clipboard.LastCopiedText);
            Assert.AreEqual(1, context.ViewModel.RecentPrompts.Count);
            Assert.AreEqual(prompt.Id, context.ViewModel.RecentPrompts[0].Id);
        });
    }

    [TestMethod]
    [TestCategory("WpfIntegration")]
    public void Main_window_constructor_validates_required_dependencies()
    {
        WpfTestHost.Invoke(() =>
        {
            using var context = WindowContext.Create();

            Assert.Throws<ArgumentNullException>(() =>
                new MainWindow(
                    null!,
                    context.Clipboard,
                    applicationLifetime: context.Lifetime,
                    showRestartMessage: (_, _) => { }));

            Assert.Throws<ArgumentNullException>(() =>
                new MainWindow(
                    context.ViewModel,
                    null!,
                    applicationLifetime: context.Lifetime,
                    showRestartMessage: (_, _) => { }));
        });
    }

    private static object? InvokePrivate(
        object target,
        string name,
        params object?[] args)
    {
        MethodInfo method = target.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(x =>
                x.Name == name &&
                x.GetParameters().Length == args.Length);

        try
        {
            return method.Invoke(target, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private sealed class WindowContext : IDisposable
    {
        private readonly TestDirectory _directory;

        private WindowContext(
            TestDirectory directory,
            MainViewModel viewModel,
            PromptLibraryService service,
            FakeClipboardService clipboard,
            FakeApplicationLifetime lifetime,
            List<(string Message, string Title)> restartMessages,
            MainWindow window)
        {
            _directory = directory;
            Root = directory.Root;
            ViewModel = viewModel;
            Service = service;
            Clipboard = clipboard;
            Lifetime = lifetime;
            RestartMessages = restartMessages;
            Window = window;
        }

        public string Root { get; }
        public MainViewModel ViewModel { get; }
        public PromptLibraryService Service { get; }
        public FakeClipboardService Clipboard { get; }
        public FakeApplicationLifetime Lifetime { get; }
        public List<(string Message, string Title)> RestartMessages { get; }
        public MainWindow Window { get; }

        public static WindowContext Create(
            Action<string, string>? showRestartMessage = null)
        {
            var directory = new TestDirectory();
            try
            {
                var paths = new AppPaths(directory.Root);
                var writer = new AtomicTextWriter();
                var deleter = new FileDeleter();
                var libraryRepo = new LibraryRepository(paths, writer);
                var promptRepo = new PromptRepository(paths, writer, deleter);
                var startup = new LibraryStartupService(
                    paths,
                    libraryRepo,
                    promptRepo,
                    deleter,
                    writer);
                StartupResult startupResult = startup.LoadOrInitialize();
                var service = new PromptLibraryService(
                    startupResult.Document,
                    libraryRepo,
                    promptRepo);
                var viewModel = new MainViewModel(
                    service,
                    promptRepo,
                    directory.Root);
                var clipboard = new FakeClipboardService();
                var lifetime = new FakeApplicationLifetime();
                var messages = new List<(string Message, string Title)>();

                Action<string, string> reporter = showRestartMessage ??
                    ((message, title) => messages.Add((message, title)));

                var window = new MainWindow(
                    viewModel,
                    clipboard,
                    applicationLifetime: lifetime,
                    showRestartMessage: reporter);

                return new WindowContext(
                    directory,
                    viewModel,
                    service,
                    clipboard,
                    lifetime,
                    messages,
                    window);
            }
            catch
            {
                directory.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Window.Close();
            _directory.Dispose();
        }
    }
}
