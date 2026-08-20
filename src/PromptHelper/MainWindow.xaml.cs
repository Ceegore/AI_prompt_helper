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
    private readonly ClipboardService _clipboardService;

    public MainWindow(MainViewModel viewModel, ClipboardService clipboardService)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));

        DataContext = _viewModel;
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        var helpDialog = new HelpDialog(_viewModel.DataFolderPath)
        {
            Owner = this
        };
        helpDialog.ShowDialog();
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

    private void RenameCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CategoryItemViewModel cat)
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
    }

    private void DeleteCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CategoryItemViewModel cat)
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
    }

    private void AddPromptButton_Click(object sender, RoutedEventArgs e)
    {
        string promptText = string.Empty;
        while (true)
        {
            var dialog = new PromptEditorDialog("Create Prompt", promptText)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                break;
            }

            promptText = dialog.ResultText;
            try
            {
                var result = _viewModel.CreatePrompt(promptText);
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

            while (true)
            {
                var dialog = new PromptEditorDialog("Edit Prompt", promptText)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() != true)
                {
                    break;
                }

                promptText = dialog.ResultText;
                try
                {
                    var result = _viewModel.EditPrompt(card.Id, promptText);
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
                string textToCopy = _viewModel.GetPromptContent(card.Id);
                _clipboardService.CopyText(textToCopy);

                card.IsCopying = true;
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