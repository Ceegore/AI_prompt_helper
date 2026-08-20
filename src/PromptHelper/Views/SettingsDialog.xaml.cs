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
    private string _selectedDataFolder;

    public SettingsDialog(
        string currentDataFolder,
        AppSettingsRepository settingsRepo,
        DataFolderMigrationService migrationService,
        IUserConfirmationService? confirmationService = null)
    {
        InitializeComponent();
        _currentDataFolder = currentDataFolder ?? throw new ArgumentNullException(nameof(currentDataFolder));
        _settingsRepo = settingsRepo ?? throw new ArgumentNullException(nameof(settingsRepo));
        _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));
        _confirmationService = confirmationService ?? new WpfUserConfirmationService(this);

        _selectedDataFolder = _currentDataFolder;
        DataFolderTextBox.Text = _selectedDataFolder;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        VersionTextBlock.Text = $"v{version}";
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
        try
        {
            string targetInput = !string.IsNullOrWhiteSpace(DataFolderTextBox?.Text)
                ? DataFolderTextBox.Text
                : _selectedDataFolder;

            string normalizedCurrent = Path.GetFullPath(_currentDataFolder.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedSelected = Path.GetFullPath((targetInput ?? string.Empty).Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(normalizedCurrent, normalizedSelected, StringComparison.OrdinalIgnoreCase))
            {
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

            if (AppInstanceLock.IsExistingLockHeld(normalizedSelected))
            {
                MessageBox.Show(
                    this,
                    "The selected Prompt Helper library is currently in use by another running instance.\r\n\r\nClose that instance and try again.",
                    "Target Library In Use",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            var result = _migrationService.PrepareTarget(_currentDataFolder, targetInput ?? string.Empty);

            if (result.ExistingLibraryFound)
            {
                bool confirmed = _confirmationService.ConfirmExistingLibrarySwitch(
                    result.NormalizedTargetRoot,
                    result.Warning);

                if (!confirmed)
                {
                    return;
                }
            }

            var saveResult = _settingsRepo.Save(new AppSettings
            {
                SchemaVersion = AppSettings.CurrentSchemaVersion,
                DataRootPath = result.NormalizedTargetRoot
            });

            if (!result.ExistingLibraryFound)
            {
                string successMessage = "The data folder has been saved.\r\n\r\nPrompt Helper will use it the next time the application starts.\r\n\r\nThe previous data folder was left unchanged as a safety copy.";
                if (!string.IsNullOrEmpty(saveResult.Warning))
                {
                    successMessage += $"\r\n\r\nWarning: {saveResult.Warning}";
                }

                MessageBox.Show(
                    this,
                    successMessage,
                    "Data Folder Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (!string.IsNullOrEmpty(saveResult.Warning))
            {
                MessageBox.Show(
                    this,
                    $"The data folder setting was updated, but a warning occurred:\r\n\r\n{saveResult.Warning}",
                    "Settings Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            RestartRequired = true;
            try
            {
                DialogResult = true;
            }
            catch (InvalidOperationException)
            {
            }
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidDataException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                $"Failed to configure the selected data folder:\r\n\r\n{ex.Message}\r\n\r\nThe previous data folder was left unchanged as a safety copy.",
                "Data Folder Configuration Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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
