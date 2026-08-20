using System.IO;
using System.Windows;
using System.Windows.Threading;
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

        DispatcherUnhandledException += App_DispatcherUnhandledException;

        try
        {
            var writer = new AtomicTextWriter();
            var settingsRepo = new AppSettingsRepository(writer);
            var settingsResult = settingsRepo.LoadOrRecover();
            var settings = settingsResult.Settings;

            string effectiveDataRoot = settingsRepo.GetEffectiveDataRoot(settings);

            if (!string.IsNullOrWhiteSpace(settings.DataRootPath))
            {
                try
                {
                    DataRootBootstrapValidator.ValidateConfiguredRoot(effectiveDataRoot);
                }
                catch (ConfiguredDataFolderUnavailableException ex)
                {
                    MessageBox.Show(
                        $"The configured Prompt Helper data folder is unavailable:\n{ex.DataFolderPath}\n\nPrompt Helper did not create a new library there, so your existing data was not overwritten.\nReconnect/restore the folder or repair the configured data-folder setting before continuing.",
                        "Configured Data Folder Unavailable",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Shutdown();
                    return;
                }
            }

            var paths = new AppPaths(effectiveDataRoot);
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

            var libraryService = new PromptLibraryService(startupResult.Document, libraryRepo, promptRepo);
            var clipboardService = new ClipboardService();
            var migrationService = new DataFolderMigrationService();
            var mainViewModel = new MainViewModel(libraryService, promptRepo, paths.RootDirectory);

            var mainWindow = new MainWindow(mainViewModel, clipboardService, settingsRepo, migrationService);
            MainWindow = mainWindow;
            mainWindow.Show();

            if (!string.IsNullOrEmpty(settingsResult.Warning))
            {
                MessageBox.Show(
                    settingsResult.Warning,
                    settingsResult.RecoveredFromBackup
                        ? "Settings Recovery Notice"
                        : "Settings Backup Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            if (!string.IsNullOrEmpty(startupResult.Warning))
            {
                MessageBox.Show(
                    startupResult.Warning,
                    "Prompt Helper Recovery Notice",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (UnsupportedSettingsSchemaException ex)
        {
            MessageBox.Show(
                $"Prompt Helper settings were created by a newer version (schema {ex.SchemaVersion}) and cannot be safely opened by this build.\n\nInstall the newer Prompt Helper version or restore compatible settings.",
                "Unsupported Settings Schema",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to load or initialize Prompt Helper library:\n\n{ex.Message}",
                "Prompt Helper Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            MessageBox.Show(
                $"An unexpected error occurred and Prompt Helper must close:\n\n{e.Exception.Message}",
                "Prompt Helper Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            e.Handled = true;
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _appLock?.Dispose();
        base.OnExit(e);
    }
}