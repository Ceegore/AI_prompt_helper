using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PromptHelper.Views;

public partial class PromptEditorDialog : Window
{
    private readonly bool _initialHeadlineWasAutomatic;
    private bool _isInitializingHeadline;
    private bool _headlineWasUserEdited;

    public PromptEditorDialog(
        string title,
        string initialText,
        string initialHeadline = "",
        bool initialHeadlineWasAutomatic = false)
    {
        InitializeComponent();
        Title = title;
        TitleTextBlock.Text = title;
        _initialHeadlineWasAutomatic = initialHeadlineWasAutomatic;

        _isInitializingHeadline = true;
        HeadlineTextBox.Text = initialHeadline;
        _isInitializingHeadline = false;

        EditorTextBox.Text = initialText;

        Loaded += (_, _) =>
        {
            EditorTextBox.Focus();
            EditorTextBox.CaretIndex = EditorTextBox.Text.Length;
        };
    }

    public string ResultText { get; private set; } = string.Empty;
    public string ResultHeadlineEditorText { get; private set; } = string.Empty;
    public bool ResultUsesAutomaticHeadline { get; private set; }
    public string? ResultHeadline { get; private set; }

    private void HeadlineTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitializingHeadline)
        {
            _headlineWasUserEdited = true;
        }
    }

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
        ResultHeadlineEditorText = HeadlineTextBox.Text;

        string trimmed = HeadlineTextBox.Text.Trim();

        if (trimmed.Length == 0)
        {
            ResultUsesAutomaticHeadline = true;
            ResultHeadline = null;
        }
        else if (_initialHeadlineWasAutomatic && !_headlineWasUserEdited)
        {
            ResultUsesAutomaticHeadline = true;
            ResultHeadline = null;
        }
        else
        {
            ResultUsesAutomaticHeadline = false;
            ResultHeadline = trimmed;
        }

        try
        {
            DialogResult = true;
        }
        catch (InvalidOperationException)
        {
            // Allowed when window was shown without ShowDialog (e.g. in test runner)
        }
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DialogResult = false;
        }
        catch (InvalidOperationException)
        {
            // Allowed when window was shown without ShowDialog (e.g. in test runner)
        }
        Close();
    }
}