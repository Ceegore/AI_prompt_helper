using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PromptHelper.Views;

public partial class PromptEditorDialog : Window
{
    public PromptEditorDialog(string title, string initialText, string initialHeadline = "")
    {
        InitializeComponent();
        Title = title;
        TitleTextBlock.Text = title;
        HeadlineTextBox.Text = initialHeadline;
        EditorTextBox.Text = initialText;

        Loaded += (_, _) =>
        {
            EditorTextBox.Focus();
            EditorTextBox.CaretIndex = EditorTextBox.Text.Length;
        };
    }

    public string ResultText { get; private set; } = string.Empty;
    public string? ResultHeadline { get; private set; }

    private void WrapLinesCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool wrap = WrapLinesCheckBox.IsChecked == true;

        EditorTextBox.TextWrapping = wrap
            ? TextWrapping.Wrap
            : TextWrapping.NoWrap;

        EditorTextBox.HorizontalScrollBarVisibility = wrap
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ResultText = EditorTextBox.Text;

        string trimmedHeadline = HeadlineTextBox.Text.Trim();
        ResultHeadline = trimmedHeadline.Length == 0
            ? null
            : trimmedHeadline;

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}