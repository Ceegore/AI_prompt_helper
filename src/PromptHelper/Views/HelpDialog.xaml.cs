using System.Reflection;
using System.Windows;

namespace PromptHelper.Views;

public partial class HelpDialog : Window
{
    public HelpDialog(string dataFolderPath)
    {
        InitializeComponent();
        DataFolderTextBox.Text = dataFolderPath;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        VersionTextBlock.Text = $"v{version}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}