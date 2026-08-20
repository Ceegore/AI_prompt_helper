using System.Windows;
using System.Windows.Input;

namespace PromptHelper.Views;

public partial class NameDialog : Window
{
    private readonly Func<string, string?> _validator;

    public NameDialog(
        string title,
        string actionText,
        string initialValue,
        Func<string, string?> validator)
    {
        InitializeComponent();
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));

        Title = title;
        TitleTextBlock.Text = title;
        ActionButton.Content = actionText;
        NameInputTextBox.Text = initialValue;

        Loaded += (_, _) =>
        {
            NameInputTextBox.Focus();
            NameInputTextBox.SelectAll();
        };
    }

    public string ResultName { get; private set; } = string.Empty;

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        Submit();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void NameInputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Submit();
        }
    }

    private void Submit()
    {
        string text = NameInputTextBox.Text;
        string? error = _validator(text);

        if (error != null)
        {
            ErrorTextBlock.Text = error;
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        ErrorTextBlock.Visibility = Visibility.Collapsed;
        ResultName = text.Trim();
        DialogResult = true;
        Close();
    }
}