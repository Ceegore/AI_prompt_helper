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
    private string _selectedDataFolder;

    public SettingsDialog(
        string currentDataFolder,
        AppSettingsRepository settingsRepo,
        DataFolderMigrationService migrationService)
    {
        InitializeComponent();
        _currentDataFolder = currentDataFolder ?? throw new ArgumentNullException(nameof(currentDataFolder));
        _settingsRepo = settingsRepo ?? throw new ArgumentNullException(nameof(settingsRepo));
        _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));

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
        string normalizedCurrent = Path.GetFullPath(_currentDataFolder.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedSelected = Path.GetFullPath((_selectedDataFolder ?? string.Empty).Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedCurrent, normalizedSelected, StringComparison.OrdinalIgnoreCase))
        {
            DialogResult = false;
            Close();
            return;
        }

        try
        {
            var result = _migrationService.PrepareTarget(_currentDataFolder, _selectedDataFolder ?? string.Empty);
            _settingsRepo.Save(new AppSettings
            {
                SchemaVersion = 1,
                DataRootPath = result.NormalizedTargetRoot
            });

            MessageBox.Show(
                this,
                "The data folder has been saved.\r\n\r\nPrompt Helper will use it the next time the application starts.\r\n\r\nThe previous data folder was left unchanged as a safety copy.",
                "Data Folder Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            RestartRequired = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidDataException or ArgumentException or NotSupportedException)
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
        DialogResult = false;
        Close();
    }
}
