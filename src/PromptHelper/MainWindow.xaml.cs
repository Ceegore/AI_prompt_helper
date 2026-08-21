using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using PromptHelper.Services;
using PromptHelper.ViewModels;
using PromptHelper.Views;

namespace PromptHelper;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IClipboardService _clipboardService;
    private readonly PromptCopyCoordinator _copyCoordinator;
    private readonly AppSettingsRepository _settingsRepo;
    private readonly DataFolderMigrationService _migrationService;
    private readonly IApplicationLifetime _applicationLifetime;
    private readonly Action<string, string>? _showRestartMessage;

    public MainWindow(
        MainViewModel viewModel,
        IClipboardService clipboardService,
        AppSettingsRepository? settingsRepo = null,
        DataFolderMigrationService? migrationService = null,
        IApplicationLifetime? applicationLifetime = null,
        Action<string, string>? showRestartMessage = null)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _copyCoordinator = new PromptCopyCoordinator(_viewModel, _clipboardService);
        _settingsRepo = settingsRepo ?? new AppSettingsRepository();
        _migrationService = migrationService ?? new DataFolderMigrationService();
        _applicationLifetime = applicationLifetime ?? new WpfApplicationLifetime();
        _showRestartMessage = showRestartMessage;
        DataContext = _viewModel;

        TryApplyApplicationIcon();
    }

    private void TryApplyApplicationIcon()
    {
        try
        {
            var iconUri = new Uri("pack://application:,,,/PromptHelper;component/Assets/PromptHelper.ico", UriKind.Absolute);
            System.Windows.Resources.StreamResourceInfo? resource = Application.GetResourceStream(iconUri);
            if (resource is null)
            {
                return;
            }
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
        }
        catch (IOException)
        {
            // Optional icon resource if not yet packaged
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_viewModel.DataFolderPath, _settingsRepo, _migrationService)
        {
            Owner = this
        };
        bool? result = dialog.ShowDialog();
        CompleteSettingsDialog(result, dialog.RestartRequired);
    }

    internal void CompleteSettingsDialog(
        bool? dialogResult,
        bool restartRequired)
    {
        if (!restartRequired)
        {
            return;
        }

        try
        {
            if (_showRestartMessage != null)
            {
                _showRestartMessage(
                    "Data folder changed\n\nPrompt Helper must close now so the previous data folder cannot be modified after the migration snapshot.\n\nOpen Prompt Helper again to use the selected data folder.",
                    "Restart Required");
            }
            else
            {
                MessageBox.Show(
                    this,
                    "Data folder changed\n\nPrompt Helper must close now so the previous data folder cannot be modified after the migration snapshot.\n\nOpen Prompt Helper again to use the selected data folder.",
                    "Restart Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        finally
        {
            _applicationLifetime.RequestShutdown();
        }
    }

    private void BreadcrumbButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is BreadcrumbItemViewModel item)
        {
            _viewModel.NavigateTo(item.CategoryId);
        }
    }

    private void OpenCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CategoryItemViewModel cat)
        {
            _viewModel.NavigateTo(cat.Id);
        }
    }

    private void AddCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        var existingNames = _viewModel.ChildCategories.Select(c => c.Name);
        var dialog = new NameDialog(
            "Create Category",
            "Create",
            string.Empty,
            name => LibraryValidator.ValidateCategoryNameInput(name, existingNames))
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var result = _viewModel.CreateCategory(dialog.ResultName);
                ShowWarningIfPresent(result.Warning);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SecurityException)
            {
                MessageBox.Show(
                    this,
                    $"Failed to create category:\n\n{ex.Message}",
                    "Category Creation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void CategoryActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }

    private static bool TryGetCategoryFromMenuItem(object sender, out CategoryItemViewModel? category)
    {
        category = null;
        if (sender is MenuItem menuItem &&
            menuItem.Parent is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is FrameworkElement fe &&
            fe.DataContext is CategoryItemViewModel cat)
        {
            category = cat;
            return true;
        }
        return false;
    }

    private void RenameCategoryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetCategoryFromMenuItem(sender, out var cat) && cat != null)
        {
            RenameCategory(cat);
        }
    }

    private void DeleteCategoryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetCategoryFromMenuItem(sender, out var cat) && cat != null)
        {
            DeleteCategory(cat);
        }
    }

    private void RenameCategory(CategoryItemViewModel cat)
    {
        var existingNames = _viewModel.ChildCategories
            .Where(c => c.Id != cat.Id)
            .Select(c => c.Name);

        var dialog = new NameDialog(
            "Rename Category",
            "Save",
            cat.Name,
            name => LibraryValidator.ValidateCategoryNameInput(name, existingNames))
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var result = _viewModel.RenameCategory(cat.Id, dialog.ResultName);
                ShowWarningIfPresent(result.Warning);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SecurityException)
            {
                MessageBox.Show(
                    this,
                    $"Failed to rename category:\n\n{ex.Message}",
                    "Category Rename Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void DeleteCategory(CategoryItemViewModel cat)
    {
        if (!_viewModel.CanDeleteCategory(cat.Id, out string? blockReason))
        {
            MessageBox.Show(
                this,
                blockReason,
                "Cannot Delete Category",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirmDialog = new ConfirmDeleteDialog(
            "Delete Category",
            $"Delete category \"{cat.Name}\"?",
            "Delete")
        {
            Owner = this
        };

        if (confirmDialog.ShowDialog() == true)
        {
            try
            {
                var result = _viewModel.DeleteCategory(cat.Id);
                ShowWarningIfPresent(result.Warning);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SecurityException)
            {
                MessageBox.Show(
                    this,
                    $"Failed to delete category:\n\n{ex.Message}",
                    "Delete Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void AddPromptButton_Click(object sender, RoutedEventArgs e)
    {
        string promptText = string.Empty;
        string headlineText = string.Empty;
        bool headlineAutomatic = true;
        while (true)
        {
            var dialog = new PromptEditorDialog("Create Prompt", promptText, headlineText, headlineAutomatic)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                break;
            }

            promptText = dialog.ResultText;
            headlineText = dialog.ResultHeadlineEditorText;
            headlineAutomatic = dialog.ResultUsesAutomaticHeadline;
            try
            {
                var result = _viewModel.CreatePrompt(promptText, dialog.ResultHeadline);
                ShowWarningIfPresent(result.Warning);
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SecurityException)
            {
                MessageBox.Show(
                    this,
                    $"Failed to save new prompt:\n\n{ex.Message}",
                    "Save Prompt Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                // Loop continues with promptText preserved so user work is not lost (PLH-001)
            }
        }
    }

    private void EditPromptButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PromptCardViewModel card)
        {
            string promptText;
            try
            {
                promptText = _viewModel.GetPromptContent(card.Id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SecurityException)
            {
                MessageBox.Show(
                    this,
                    $"Could not open prompt for editing:\n\n{ex.Message}",
                    "Edit Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            string headlineText = card.EditableHeadline;
            bool headlineAutomatic = card.CustomTitle is null;

            while (true)
            {
                var dialog = new PromptEditorDialog("Edit Prompt", promptText, headlineText, headlineAutomatic)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() != true)
                {
                    break;
                }

                promptText = dialog.ResultText;
                headlineText = dialog.ResultHeadlineEditorText;
                headlineAutomatic = dialog.ResultUsesAutomaticHeadline;
                try
                {
                    var result = _viewModel.EditPrompt(card.Id, promptText, dialog.ResultHeadline);
                    ShowWarningIfPresent(result.Warning);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SecurityException)
                {
                    MessageBox.Show(
                        this,
                        $"Failed to save edited prompt:\n\n{ex.Message}",
                        "Save Prompt Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    // Loop continues with promptText preserved so user work is not lost (PLH-001)
                }
            }
        }
    }

    private void DeletePromptButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PromptCardViewModel card)
        {
            var confirmDialog = new ConfirmDeleteDialog(
                "Delete Prompt",
                "Delete this prompt from the library?",
                "Delete")
            {
                Owner = this
            };

            if (confirmDialog.ShowDialog() == true)
            {
                try
                {
                    var result = _viewModel.DeletePrompt(card.Id);
                    ShowWarningIfPresent(result.Warning);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SecurityException)
                {
                    MessageBox.Show(
                        this,
                        $"Failed to delete prompt:\n\n{ex.Message}",
                        "Delete Prompt Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }
    }

    private void MovePromptButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PromptCardViewModel card)
        {
            var destinations = _viewModel.GetDestinations();
            var dialog = new MovePromptDialog(
                card.PreviewTitle,
                destinations,
                _viewModel.CurrentCategoryId,
                card.IsContentAvailable)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (dialog.CopyInsteadOfMove)
                    {
                        var result = _viewModel.DuplicatePrompt(card.Id, dialog.DestinationCategoryId);
                        ShowWarningIfPresent(result.Warning);
                    }
                    else
                    {
                        var result = _viewModel.MovePrompt(card.Id, dialog.DestinationCategoryId);
                        ShowWarningIfPresent(result.Warning);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SecurityException)
                {
                    MessageBox.Show(
                        this,
                        $"Failed to {(dialog.CopyInsteadOfMove ? "duplicate" : "move")} prompt:\n\n{ex.Message}",
                        "Move/Duplicate Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }
    }

    private string CopyPromptToClipboard(Guid promptId, string effectiveHeadline)
    {
        return _copyCoordinator.Copy(promptId, effectiveHeadline);
    }

    private async void CopyPromptButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PromptCardViewModel card)
        {
            if (card.IsCopying)
            {
                return;
            }

            try
            {
                card.IsCopying = true;
                CopyPromptToClipboard(card.Id, card.PreviewTitle);
                card.CopyButtonText = "Copied ✓";

                await Task.Delay(900);

                card.CopyButtonText = "Copy";
                card.IsCopying = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SecurityException)
            {
                card.CopyButtonText = "Copy";
                card.IsCopying = false;

                MessageBox.Show(
                    this,
                    ex.Message,
                    "Clipboard Copy Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private async void RecentPromptCopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is RecentPromptViewModel recent)
        {
            if (recent.IsCopying)
            {
                return;
            }

            try
            {
                recent.IsCopying = true;
                CopyPromptToClipboard(recent.Id, recent.Headline);
                recent.CopyButtonText = "Copied ✓";

                await Task.Delay(900);

                recent.CopyButtonText = "Copy";
                recent.IsCopying = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SecurityException)
            {
                recent.CopyButtonText = "Copy";
                recent.IsCopying = false;

                MessageBox.Show(
                    this,
                    ex.Message,
                    "Clipboard Copy Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private void ShowWarningIfPresent(string? warning)
    {
        if (!string.IsNullOrEmpty(warning))
        {
            MessageBox.Show(
                this,
                warning,
                "Prompt Helper Notice",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}