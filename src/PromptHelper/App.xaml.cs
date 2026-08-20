using System.Windows;
using PromptHelper.Models;
using PromptHelper.Services;
using PromptHelper.ViewModels;

namespace PromptHelper;

public partial class App : Application
{
    private AppInstanceLock? _appLock;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = new AppPaths();
        paths.EnsureRootDirectory();

        _appLock = AppInstanceLock.TryAcquire(paths.LockPath);
        if (_appLock == null)
        {
            MessageBox.Show(
                "Another instance of Prompt Helper is already running and using the library.",
                "Prompt Helper",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        paths.EnsureDataDirectories();

        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libraryRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startupService = new LibraryStartupService(paths, libraryRepo, promptRepo, deleter, writer);

        StartupResult startupResult;
        try
        {
            startupResult = startupService.LoadOrInitialize();
        }
        catch (UnsupportedLibrarySchemaException ex)
        {
            MessageBox.Show(
                $"The library file was created by a newer version of Prompt Helper (schema version {ex.SchemaVersion}) and cannot be opened.",
                "Unsupported Library Schema",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to load or initialize Prompt Helper library:\n\n{ex.Message}",
                "Prompt Helper Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var libraryService = new PromptLibraryService(startupResult.Document, libraryRepo, promptRepo);
        var clipboardService = new ClipboardService();
        var mainViewModel = new MainViewModel(libraryService, promptRepo, paths.RootDirectory);

        var mainWindow = new MainWindow(mainViewModel, clipboardService);
        MainWindow = mainWindow;
        mainWindow.Show();

        if (!string.IsNullOrEmpty(startupResult.Warning))
        {
            MessageBox.Show(
                startupResult.Warning,
                "Prompt Helper Recovery Notice",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _appLock?.Dispose();
        base.OnExit(e);
    }
}