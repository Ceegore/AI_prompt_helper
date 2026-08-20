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
            var result = _viewModel.CreateCategory(dialog.ResultName);
            ShowWarningIfPresent(result.Warning);
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
                var result = _viewModel.RenameCategory(cat.Id, dialog.ResultName);
                ShowWarningIfPresent(result.Warning);
            }
        }
    }

    private void DeleteCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CategoryItemViewModel cat)
        {
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
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(
                        this,
                        ex.Message,
                        "Cannot Delete Category",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
        }
    }

    private void AddPromptButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PromptEditorDialog("Create Prompt", string.Empty)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            var result = _viewModel.CreatePrompt(dialog.ResultText);
            ShowWarningIfPresent(result.Warning);
        }
    }

    private void EditPromptButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PromptCardViewModel card)
        {
            string rawContent;
            try
            {
                rawContent = _viewModel.GetPromptContent(card.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Could not open prompt for editing:\n\n{ex.Message}",
                    "Edit Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            var dialog = new PromptEditorDialog("Edit Prompt", rawContent)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                var result = _viewModel.EditPrompt(card.Id, dialog.ResultText);
                ShowWarningIfPresent(result.Warning);
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
                var result = _viewModel.DeletePrompt(card.Id);
                ShowWarningIfPresent(result.Warning);
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
            catch (Exception ex)
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