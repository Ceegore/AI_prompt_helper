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
    private readonly DataFolderTransitionCoordinator _coordinator;
    private string _selectedDataFolder;

    public SettingsDialog(
        string currentDataFolder,
        AppSettingsRepository settingsRepo,
        DataFolderMigrationService migrationService,
        IUserConfirmationService? confirmationService = null,
        DataFolderTransitionCoordinator? coordinator = null)
    {
        InitializeComponent();
        _currentDataFolder = currentDataFolder ?? throw new ArgumentNullException(nameof(currentDataFolder));
        _settingsRepo = settingsRepo ?? throw new ArgumentNullException(nameof(settingsRepo));
        _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));
        _confirmationService = confirmationService ?? new WpfUserConfirmationService(this);
        _coordinator = coordinator ?? new DataFolderTransitionCoordinator(
            _currentDataFolder,
            _settingsRepo,
            _migrationService,
            _confirmationService);

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

            DataFolderTransitionResult result = _coordinator.RequestTransition(targetInput ?? string.Empty);

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

            RestartRequired = result.RestartRequired;
            try
            {
                DialogResult = true;
            }
            catch (InvalidOperationException)
            {
            }
            Close();
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
