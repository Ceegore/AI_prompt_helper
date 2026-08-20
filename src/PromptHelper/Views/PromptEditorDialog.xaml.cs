using System.Windows;
using System.Windows.Input;

namespace PromptHelper.Views;

public partial class PromptEditorDialog : Window
{
    public PromptEditorDialog(string title, string initialText)
    {
        InitializeComponent();
        Title = title;
        TitleTextBlock.Text = title;
        EditorTextBox.Text = initialText;

        Loaded += (_, _) =>
        {
            EditorTextBox.Focus();
            EditorTextBox.CaretIndex = EditorTextBox.Text.Length;
        };
    }

    public string ResultText { get; private set; } = string.Empty;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ResultText = EditorTextBox.Text;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}