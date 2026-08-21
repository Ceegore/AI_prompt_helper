using System;
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

            string bootstrapRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PromptHelper");

            var physicalResolver = new WindowsPhysicalPathResolver();
            var rootPolicy = new ManagedDataRootPolicy(physicalResolver);

            effectiveDataRoot = rootPolicy.ValidateConfiguredRootForStartup(
                effectiveDataRoot,
                bootstrapRoot);

            if (!string.IsNullOrWhiteSpace(settings.DataRootPath))
            {
                DataRootBootstrapValidator.ValidateConfiguredRoot(effectiveDataRoot);
            }

            var runtime = DataRootRuntimeContext.Create(effectiveDataRoot, bootstrapRoot, physicalResolver);

            var paths = new AppPaths(runtime.ActivePhysicalRoot);
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

            var tree = new ManagedTreeTopologyValidator(physicalResolver);
            tree.ValidateManagedTree(runtime.ActivePhysicalRoot);

            var recoveryService = new MigrationRecoveryService(treeValidator: tree);
            var recoveryContext = new MigrationRecoveryContext(
                runtime.ActivePhysicalRoot,
                runtime.BootstrapPhysicalRoot,
                ExpectedSourcePhysicalRoot: null);
            var recoveryResult = recoveryService.FinalizeCommittedStartup(recoveryContext);

            if (!recoveryResult.Success)
            {
                MessageBox.Show(
                    "Prompt Helper detected an incomplete or modified migration attempt in the configured data folder:\n\n" +
                    paths.RootDirectory +
                    $"\n\nDetails: {recoveryResult.ErrorMessage}\n\nTo protect your data, Prompt Helper will close without modifying this folder.\nReview the folder contents or remove unfinished migration artifacts before restarting.",
                    "Migration Residue Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }

            paths.EnsureDataDirectories();
            tree.ValidateManagedTree(runtime.ActivePhysicalRoot);

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
        catch (ConfiguredDataFolderUnavailableException ex)
        {
            MessageBox.Show(
                $"The configured Prompt Helper data folder is unavailable:\n{ex.DataFolderPath}\n\nPrompt Helper did not create a new library there, so your existing data was not overwritten.\nReconnect/restore the folder or repair the configured data-folder setting before continuing.",
                "Configured Data Folder Unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
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