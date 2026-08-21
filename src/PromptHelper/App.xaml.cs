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
    private ManagedDataRootSessionLease? _managedTreeLease;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += App_DispatcherUnhandledException;

        try
        {
            var durableWriter = new WindowsDurableAtomicFileWriter();
            var verifiedDeleter = new WindowsVerifiedArtifactDeleter();
            var settingsRepo = new AppSettingsRepository();
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
            tree.ValidateManagedTree(runtime.ActivePhysicalRoot, ManagedTreeValidationMode.PreCreation);

            var conflictDetector = new RecoveryJournalConflictDetector();
            conflictDetector.EnsureNoConflicts(paths);

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
            tree.ValidateManagedTree(runtime.ActivePhysicalRoot, ManagedTreeValidationMode.RuntimeRequired);

            _managedTreeLease = ManagedDataRootSessionLease.Acquire(paths.RootDirectory);

            DataRootTempReconciler.Reconcile(
                paths,
                isBootstrapRoot: PathIdentity.Equals(runtime.ActivePhysicalRoot, runtime.BootstrapPhysicalRoot));

            var journalRepo = new LibraryMutationJournalRepository(paths, durableWriter);
            var mutationRecovery = new LibraryMutationRecoveryService(paths, journalRepo, durableWriter, verifiedDeleter);
            var mutResult = mutationRecovery.RecoverIfPresent();
            if (!mutResult.Success)
            {
                MessageBox.Show(
                    $"Failed to recover incomplete library mutation: {mutResult.ErrorMessage}",
                    "Library Mutation Recovery Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }

            var deleter = new FileDeleter();
            var libraryRepo = new LibraryRepository(paths, durableWriter);
            var promptRepo = new PromptRepository(paths, durableWriter, deleter);
            var startupService = new LibraryStartupService(paths, libraryRepo, promptRepo, deleter, durableWriter);

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

            // Reconcile unreferenced prompt body orphans if backup authority is current
            try
            {
                var backupDoc = libraryRepo.ReadBackup();
                var orphanReconciler = new PromptOrphanReconciler(paths, promptRepo, journalRepo);
                orphanReconciler.Reconcile(new OrphanReconciliationAuthority(startupResult.Document, backupDoc));
            }
            catch
            {
                // Defer orphan cleanup if backup is missing, corrupt, or future schema
            }

            var inspector = new LibraryPackageInspector(paths);
            var coordinator = new PromptMutationCoordinator(
                paths,
                promptRepo,
                libraryRepo,
                inspector,
                journalRepo,
                mutationRecovery,
                durableWriter,
                verifiedDeleter);

            var libraryService = new PromptLibraryService(startupResult.Document, libraryRepo, promptRepo, coordinator);
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
        _managedTreeLease?.Dispose();
        _managedTreeLease = null;

        _appLock?.Dispose();
        _appLock = null;

        base.OnExit(e);
    }
}