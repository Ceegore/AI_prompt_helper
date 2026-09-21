using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace PromptHelper.Desktop;

internal sealed class StartupErrorWindow : Window
{
    public StartupErrorWindow(string message)
    {
        Title = "Prompt Helper";
        Width = 560;
        Height = 260;
        MinWidth = 460;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var close = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 90
        };
        close.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = "Prompt Helper could not start",
                    FontSize = 22,
                    FontWeight = FontWeight.Bold
                },
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                close
            }
        };
    }
}

internal sealed class NameDialog : Window
{
    private readonly TextBox _nameBox;

    public NameDialog(
        string title,
        string label,
        string? initialValue = null)
    {
        Title = title;
        Width = 460;
        Height = 210;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _nameBox = new TextBox
        {
            Text = initialValue ?? string.Empty,
            PlaceholderText = label
        };

        var save = new Button
        {
            Content = "Save",
            MinWidth = 90
        };
        save.Click += (_, _) =>
        {
            string value = (_nameBox.Text ?? string.Empty).Trim();
            if (value.Length != 0)
            {
                Close(value);
            }
        };

        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 90
        };
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = label },
                _nameBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { cancel, save }
                }
            }
        };

        Opened += (_, _) =>
        {
            _nameBox.Focus();
            _nameBox.SelectAll();
        };
    }
}

internal sealed class PromptEditorDialog : Window
{
    private readonly TextBox _titleBox;
    private readonly TextBox _contentBox;

    public PromptEditorDialog(
        string windowTitle,
        string content,
        string? title)
    {
        Title = windowTitle;
        Width = 820;
        Height = 640;
        MinWidth = 620;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _titleBox = new TextBox
        {
            Text = title ?? string.Empty,
            PlaceholderText = "Optional headline"
        };

        _contentBox = new TextBox
        {
            Text = content,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 390,
            FontFamily = new FontFamily("monospace")
        };

        var save = new Button
        {
            Content = "Save",
            MinWidth = 100
        };
        save.Click += (_, _) =>
        {
            Close(new PromptDialogResult(
                _contentBox.Text ?? string.Empty,
                string.IsNullOrWhiteSpace(_titleBox.Text)
                    ? null
                    : _titleBox.Text!.Trim()));
        };

        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 100
        };
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Headline" },
                _titleBox,
                new TextBlock { Text = "Prompt" },
                _contentBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { cancel, save }
                }
            }
        };

        Opened += (_, _) => _contentBox.Focus();
    }
}

internal sealed class MovePromptDialog : Window
{
    private readonly ComboBox _destinationBox;

    public MovePromptDialog(
        IReadOnlyList<DestinationDisplay> destinations,
        Guid? currentCategoryId)
    {
        Title = "Move prompt";
        Width = 520;
        Height = 230;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _destinationBox = new ComboBox
        {
            ItemsSource = destinations,
            MinWidth = 420,
            SelectedItem = destinations.FirstOrDefault(
                item => item.CategoryId == currentCategoryId)
                ?? destinations.FirstOrDefault()
        };

        var move = new Button
        {
            Content = "Move",
            MinWidth = 90
        };
        move.Click += (_, _) =>
        {
            if (_destinationBox.SelectedItem is DestinationDisplay selected)
            {
                Close(new MoveDialogResult(selected.CategoryId));
            }
        };

        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 90
        };
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = "Destination category" },
                _destinationBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { cancel, move }
                }
            }
        };
    }
}

internal sealed class SettingsDialog : Window
{
    private readonly TextBox _dataRootBox;
    private readonly CheckBox _darkMode;

    public SettingsDialog(
        string currentDataRoot,
        bool useDarkMode)
    {
        Title = "Settings";
        Width = 700;
        Height = 350;
        MinWidth = 560;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _dataRootBox = new TextBox
        {
            Text = currentDataRoot,
            PlaceholderText = "Absolute library folder"
        };

        _darkMode = new CheckBox
        {
            Content = "Dark mode",
            IsChecked = useDarkMode
        };

        var browse = new Button
        {
            Content = "Browse…",
            MinWidth = 100
        };
        browse.Click += async (_, _) =>
        {
            IReadOnlyList<IStorageFolder> folders =
                await StorageProvider.OpenFolderPickerAsync(
                    new FolderPickerOpenOptions
                    {
                        Title = "Choose Prompt Helper library folder",
                        AllowMultiple = false
                    });

            IStorageFolder? folder = folders.FirstOrDefault();
            if (folder is not null && folder.Path.IsFile)
            {
                _dataRootBox.Text = folder.Path.LocalPath;
            }
        };

        var useDefault = new Button
        {
            Content = "Use default",
            MinWidth = 100
        };
        useDefault.Click += (_, _) => _dataRootBox.Text = string.Empty;

        var save = new Button
        {
            Content = "Save",
            MinWidth = 100
        };
        save.Click += (_, _) =>
        {
            string? root = string.IsNullOrWhiteSpace(_dataRootBox.Text)
                ? null
                : _dataRootBox.Text!.Trim();

            Close(new SettingsDialogResult(
                root,
                _darkMode.IsChecked == true));
        };

        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 100
        };
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = "Library folder",
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = "Switches the library location. The old folder is never deleted or copied automatically.",
                    TextWrapping = TextWrapping.Wrap
                },
                _dataRootBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children = { browse, useDefault }
                },
                _darkMode,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { cancel, save }
                }
            }
        };
    }
}

internal static class Dialogs
{
    public static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string message,
        string confirmText = "Delete")
    {
        var window = new Window
        {
            Title = title,
            Width = 520,
            Height = 250,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var confirm = new Button
        {
            Content = confirmText,
            MinWidth = 100
        };
        confirm.Click += (_, _) => window.Close(true);

        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 100
        };
        cancel.Click += (_, _) => window.Close(false);

        window.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 18,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { cancel, confirm }
                }
            }
        };

        return await window.ShowDialog<bool>(owner);
    }

    public static async Task ShowMessageAsync(
        Window owner,
        string title,
        string message)
    {
        var window = new Window
        {
            Title = title,
            Width = 560,
            Height = 270,
            MinWidth = 460,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var close = new Button
        {
            Content = "OK",
            MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        close.Click += (_, _) => window.Close();

        window.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 18,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                close
            }
        };

        await window.ShowDialog(owner);
    }
}
