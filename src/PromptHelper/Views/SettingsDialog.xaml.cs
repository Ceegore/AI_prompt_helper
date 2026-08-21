using System;
using System.IO;
using System.Reflection;
using System.Security;
using System.Windows;
using Microsoft.Win32;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Views;

public partial class SettingsDialog : Window
{
    private readonly string _currentDataFolder;
    private readonly AppSettingsRepository _settingsRepo;
    private readonly DataFolderMigrationService _migrationService;
    private readonly IUserConfirmationService _confirmationService;
    private readonly IDataFolderTransitionService _transitionService;
    private string _selectedDataFolder;

    public SettingsDialog(
        string currentDataFolder,
        AppSettingsRepository settingsRepo,
        DataFolderMigrationService migrationService,
        IUserConfirmationService? confirmationService = null,
        IDataFolderTransitionService? transitionService = null)
    {
        InitializeComponent();
        _currentDataFolder = currentDataFolder ?? throw new ArgumentNullException(nameof(currentDataFolder));
        _settingsRepo = settingsRepo ?? throw new ArgumentNullException(nameof(settingsRepo));
        _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));
        _confirmationService = confirmationService ?? new WpfUserConfirmationService(this);
        _transitionService = transitionService ?? new DataFolderTransitionCoordinator(
            _currentDataFolder,
            _settingsRepo,
            _migrationService,
            _confirmationService);

        _selectedDataFolder = _currentDataFolder;
        DataFolderTextBox.Text = _selectedDataFolder;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        VersionTextBlock.Text = $"v{version}";
    }

    public SettingsDialog(
        string currentDataFolder,
        AppSettingsRepository settingsRepo,
        DataFolderMigrationService migrationService,
        IUserConfirmationService? confirmationService,
        DataFolderTransitionCoordinator? coordinator)
        : this(currentDataFolder, settingsRepo, migrationService, confirmationService, (IDataFolderTransitionService?)coordinator)
    {
    }

    public bool RestartRequired { get; private set; }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog
        {
            Title = "Select Prompt Helper data folder",
            Multiselect = false,
            InitialDirectory = Directory.Exists(_selectedDataFolder)
                ? _selectedDataFolder
                : _currentDataFolder
        };

        if (picker.ShowDialog(this) == true)
        {
            _selectedDataFolder = picker.FolderName;
            DataFolderTextBox.Text = _selectedDataFolder;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ExecuteSaveInternal();
    }

    internal void ExecuteSaveForTest()
    {
        ExecuteSaveInternal();
    }

    private void ExecuteSaveInternal()
    {
        try
        {
            string targetInput = !string.IsNullOrWhiteSpace(DataFolderTextBox?.Text)
                ? DataFolderTextBox.Text
                : _selectedDataFolder;

            DataFolderTransitionResult result = _transitionService.RequestTransition(targetInput ?? string.Empty);

            if (!result.Changed)
            {
                if (result.ExistingLibrarySelected)
                {
                    // User cancelled confirmation, remain on dialog
                    return;
                }

                // Same directory selected, no-op
                RestartRequired = false;
                try
                {
                    DialogResult = true;
                }
                catch (InvalidOperationException)
                {
                }
                Close();
                return;
            }

            // CRUU8-013: Monotonic postcommit boundary.
            // Establish RestartRequired BEFORE showing notification UI!
            RestartRequired = true;

            try
            {
                if (!result.ExistingLibrarySelected)
                {
                    string successMessage = "The data folder has been saved.\r\n\r\nPrompt Helper will use it the next time the application starts.\r\n\r\nThe previous data folder was left unchanged as a safety copy.";
                    if (!string.IsNullOrEmpty(result.Warning))
                    {
                        successMessage += $"\r\n\r\nWarning: {result.Warning}";
                    }

                    _confirmationService.ShowInformation(
                        successMessage,
                        "Data Folder Saved");
                }
                else if (!string.IsNullOrEmpty(result.Warning))
                {
                    _confirmationService.ShowWarning(
                        $"The data folder setting was updated, but a warning occurred:\r\n\r\n{result.Warning}",
                        "Settings Warning");
                }
            }
            catch
            {
                // Notification failure must NOT revert or clear RestartRequired.
            }

            try
            {
                DialogResult = true;
            }
            catch (InvalidOperationException)
            {
            }
            Close();
        }
        catch (ConfiguredDataFolderUnavailableException ex)
        {
            _confirmationService.ShowWarning(
                "The currently configured Prompt Helper data folder can no longer be resolved:\r\n\r\n" +
                ex.DataFolderPath +
                "\r\n\r\nNo data-folder change was committed. Restore or reconnect the folder and retry.",
                "Configured Data Folder Unavailable");
        }
        catch (UnsupportedLibrarySchemaException ex)
        {
            _confirmationService.ShowWarning(
                "The selected folder contains a Prompt Helper library created by a newer schema " +
                $"({ex.SchemaVersion}).\r\n\r\n" +
                "The folder was not selected and the current data-folder setting was not changed.",
                "Newer Library Version");
        }
        catch (UnsupportedSettingsSchemaException ex)
        {
            _confirmationService.ShowWarning(
                "Prompt Helper settings changed to a newer schema " +
                $"({ex.SchemaVersion}) while this dialog was open.\r\n\r\n" +
                "No data-folder change was committed. Close Prompt Helper and use the newer version.",
                "Newer Settings Version");
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            SecurityException or
            InvalidDataException or
            ArgumentException or
            NotSupportedException or
            InvalidOperationException)
        {
            _confirmationService.ShowWarning(
                $"Failed to configure the selected data folder:\r\n\r\n{ex.Message}\r\n\r\nThe previous data folder was left unchanged as a safety copy.",
                "Data Folder Configuration Error");
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DialogResult = false;
        }
        catch (InvalidOperationException)
        {
        }
        Close();
    }
}
