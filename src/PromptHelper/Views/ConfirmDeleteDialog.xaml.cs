using System.Windows;

namespace PromptHelper.Views;

public partial class ConfirmDeleteDialog : Window
{
    public ConfirmDeleteDialog(string title, string message, string actionText = "Delete")
    {
        InitializeComponent();
        Title = title;
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;
        ActionButton.Content = actionText;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}