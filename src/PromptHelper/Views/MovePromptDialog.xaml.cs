using System.Windows;
using PromptHelper.ViewModels;

namespace PromptHelper.Views;

public partial class MovePromptDialog : Window
{
    public MovePromptDialog(
        string promptPreview,
        IReadOnlyList<DestinationOptionViewModel> destinations,
        Guid? currentCategoryId,
        bool allowDuplicate)
    {
        InitializeComponent();

        PromptPreviewTextBlock.Text = promptPreview;
        DestinationComboBox.ItemsSource = destinations;

        var selected = destinations.FirstOrDefault(d => d.CategoryId == currentCategoryId)
                       ?? destinations.FirstOrDefault();
        DestinationComboBox.SelectedItem = selected;

        CopyInsteadOfMoveCheckBox.IsEnabled = allowDuplicate;
        if (!allowDuplicate)
        {
            UnavailablePromptNoticeTextBlock.Visibility = Visibility.Visible;
        }
    }

    public Guid? DestinationCategoryId =>
        (DestinationComboBox.SelectedItem as DestinationOptionViewModel)?.CategoryId;

    public bool CopyInsteadOfMove =>
        CopyInsteadOfMoveCheckBox.IsChecked == true;

    private void CopyInsteadOfMoveCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        ActionButton.Content = "Copy";
    }

    private void CopyInsteadOfMoveCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        ActionButton.Content = "Move";
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}