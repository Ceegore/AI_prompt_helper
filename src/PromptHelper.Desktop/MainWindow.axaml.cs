using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly LinuxDesktopSettingsStore _settingsStore;
    private readonly IAppInstanceLease _instanceLease;

    private AppSettings _settings;
    private LinuxPromptLibraryStore _library;
    private Guid? _currentCategoryId;
    private readonly List<RecentPromptDisplay> _recent = [];

    private readonly TextBlock _dataRootText;
    private readonly Border _warningBorder;
    private readonly TextBlock _warningText;
    private readonly ItemsControl _recentItems;
    private readonly ItemsControl _breadcrumbItems;
    private readonly ItemsControl _categoryItems;
    private readonly ItemsControl _promptItems;
    private readonly TextBlock _statusText;

    public MainWindow(
        LinuxDesktopSettingsStore settingsStore,
        AppSettings settings,
        LinuxPromptLibraryStore library,
        IAppInstanceLease instanceLease)
    {
        _settingsStore = settingsStore;
        _settings = settings;
        _library = library;
        _instanceLease = instanceLease;

        AvaloniaXamlLoader.Load(this);

        _dataRootText = Require<TextBlock>("DataRootText");
        _warningBorder = Require<Border>("WarningBorder");
        _warningText = Require<TextBlock>("WarningText");
        _recentItems = Require<ItemsControl>("RecentItems");
        _breadcrumbItems = Require<ItemsControl>("BreadcrumbItems");
        _categoryItems = Require<ItemsControl>("CategoryItems");
        _promptItems = Require<ItemsControl>("PromptItems");
        _statusText = Require<TextBlock>("StatusText");

        Closed += (_, _) => _instanceLease.Dispose();
        KeyDown += Window_KeyDown;

        RefreshView();

        if (_library.StartupWarnings.Count != 0)
        {
            ShowWarning(string.Join(
                Environment.NewLine,
                _library.StartupWarnings));
        }
    }

    private T Require<T>(string name)
        where T : Control =>
        this.FindControl<T>(name)
        ?? throw new InvalidOperationException(
            $"Required control '{name}' was not found.");

    private void RefreshView()
    {
        _dataRootText.Text = _library.RootDirectory;

        _breadcrumbItems.ItemsSource =
            _library.GetBreadcrumbs(_currentCategoryId)
                .Select(item =>
                    new BreadcrumbDisplay(
                        item.CategoryId,
                        item.Name))
                .ToArray();

        _categoryItems.ItemsSource =
            _library.GetCategories(_currentCategoryId)
                .Select(category =>
                    new CategoryDisplay(
                        category.Id,
                        category.Name))
                .ToArray();

        _promptItems.ItemsSource =
            _library.GetPrompts(_currentCategoryId)
                .Select(CreatePromptDisplay)
                .ToArray();

        _recentItems.ItemsSource = _recent.ToArray();
        _statusText.Text = "Ready";
    }

    private PromptDisplay CreatePromptDisplay(PromptRecord prompt)
    {
        try
        {
            string body = _library.ReadPrompt(prompt.Id);
            return new PromptDisplay(
                prompt.Id,
                ResolveTitle(prompt, body),
                CreatePreview(body),
                prompt.Title);
        }
        catch (Exception ex)
        {
            return new PromptDisplay(
                prompt.Id,
                prompt.Title ?? "Unavailable prompt",
                "Could not load prompt body: " + ex.Message,
                prompt.Title);
        }
    }

    private static string ResolveTitle(
        PromptRecord prompt,
        string body)
    {
        if (!string.IsNullOrWhiteSpace(prompt.Title))
        {
            return prompt.Title;
        }

        string? first = body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length != 0);

        return string.IsNullOrWhiteSpace(first)
            ? "Untitled prompt"
            : first.Length <= 80
                ? first
                : first[..80] + "…";
    }

    private static string CreatePreview(string body)
    {
        string compact = string.Join(
            " ",
            body.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length != 0));

        return compact.Length <= 260
            ? compact
            : compact[..260] + "…";
    }

    private void Breadcrumb_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not BreadcrumbDisplay item)
        {
            return;
        }

        _currentCategoryId = item.CategoryId;
        RefreshView();
    }

    private void OpenCategory_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not CategoryDisplay item)
        {
            return;
        }

        _currentCategoryId = item.Id;
        RefreshView();
    }

    private async void AddCategory_Click(
        object? sender,
        RoutedEventArgs e)
    {
        string? name = await new NameDialog(
            "New category",
            "Category name")
            .ShowDialog<string?>(this);

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await RunMutationAsync(
            () => _library.CreateCategory(
                _currentCategoryId,
                name),
            "Category created.");
    }

    private async void RenameCategory_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not CategoryDisplay item)
        {
            return;
        }

        string? name = await new NameDialog(
            "Rename category",
            "Category name",
            item.Name)
            .ShowDialog<string?>(this);

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await RunMutationAsync(
            () => _library.RenameCategory(
                item.Id,
                name),
            "Category renamed.");
    }

    private async void DeleteCategory_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not CategoryDisplay item)
        {
            return;
        }

        bool confirmed = await Dialogs.ConfirmAsync(
            this,
            "Delete category",
            $"Delete category '{item.Name}'? Only empty categories can be deleted.");

        if (!confirmed)
        {
            return;
        }

        await RunMutationAsync(
            () => _library.DeleteCategory(item.Id),
            "Category deleted.");
    }

    private async void AddPrompt_Click(
        object? sender,
        RoutedEventArgs e)
    {
        PromptDialogResult? result =
            await new PromptEditorDialog(
                "New prompt",
                string.Empty,
                null)
            .ShowDialog<PromptDialogResult?>(this);

        if (result is null)
        {
            return;
        }

        await RunMutationAsync(
            () => _library.CreatePrompt(
                _currentCategoryId,
                result.Content,
                result.Title),
            "Prompt created.");
    }

    private async void CopyPrompt_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not PromptDisplay item)
        {
            return;
        }

        await CopyPromptByIdAsync(item.Id, item.Title);
    }

    private async void RecentCopy_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not RecentPromptDisplay item)
        {
            return;
        }

        await CopyPromptByIdAsync(item.Id, item.Title);
    }

    private async Task CopyPromptByIdAsync(
        Guid promptId,
        string title)
    {
        try
        {
            string body = _library.ReadPrompt(promptId);
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
                ?? throw new InvalidOperationException(
                    "The desktop clipboard is unavailable.");

            await clipboard.SetTextAsync(body);
            RecordRecent(promptId, title, CreatePreview(body));
            _statusText.Text = "Copied to clipboard.";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Copy failed", ex);
        }
    }

    private async void EditPrompt_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not PromptDisplay item)
        {
            return;
        }

        try
        {
            string body = _library.ReadPrompt(item.Id);
            PromptDialogResult? result =
                await new PromptEditorDialog(
                    "Edit prompt",
                    body,
                    item.CustomTitle)
                .ShowDialog<PromptDialogResult?>(this);

            if (result is null)
            {
                return;
            }

            await RunMutationAsync(
                () => _library.EditPrompt(
                    item.Id,
                    result.Content,
                    result.Title),
                "Prompt saved.");

            RemoveRecent(item.Id);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Edit failed", ex);
        }
    }

    private async void MovePrompt_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not PromptDisplay item)
        {
            return;
        }

        DestinationDisplay[] destinations =
            _library.GetDestinations()
                .Select(DestinationDisplay.From)
                .ToArray();

        MoveDialogResult? move = await new MovePromptDialog(
            destinations,
            _currentCategoryId)
            .ShowDialog<MoveDialogResult?>(this);

        if (move is null)
        {
            return;
        }

        await RunMutationAsync(
            () => _library.MovePrompt(
                item.Id,
                move.CategoryId),
            "Prompt moved.");
    }

    private async void DuplicatePrompt_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not PromptDisplay item)
        {
            return;
        }

        await RunMutationAsync(
            () => _library.DuplicatePrompt(
                item.Id,
                _currentCategoryId),
            "Prompt duplicated.");
    }

    private async void DeletePrompt_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not PromptDisplay item)
        {
            return;
        }

        bool confirmed = await Dialogs.ConfirmAsync(
            this,
            "Delete prompt",
            $"Delete '{item.Title}'?");

        if (!confirmed)
        {
            return;
        }

        await RunMutationAsync(
            () => _library.DeletePrompt(item.Id),
            "Prompt deleted.");

        RemoveRecent(item.Id);
    }

    private async void Settings_Click(
        object? sender,
        RoutedEventArgs e)
    {
        SettingsDialogResult? result =
            await new SettingsDialog(
                _library.RootDirectory,
                _settings.UseDarkMode)
            .ShowDialog<SettingsDialogResult?>(this);

        if (result is null)
        {
            return;
        }

        try
        {
            string? normalized =
                LinuxDesktopSettingsStore.NormalizeDataRoot(
                    result.DataRootPath);

            var candidateSettings = new AppSettings
            {
                SchemaVersion = AppSettings.CurrentSchemaVersion,
                DataRootPath = normalized,
                UseDarkMode = result.UseDarkMode
            };

            string candidateRoot =
                _settingsStore.ResolveEffectiveDataRoot(
                    candidateSettings);

            LinuxPromptLibraryStore nextLibrary = _library;
            if (!string.Equals(
                    candidateRoot,
                    _library.RootDirectory,
                    StringComparison.Ordinal))
            {
                nextLibrary =
                    new LinuxPromptLibraryStore(candidateRoot);
                nextLibrary.Initialize();
            }

            _settingsStore.Save(candidateSettings);
            _settings = candidateSettings;
            _library = nextLibrary;
            _currentCategoryId = null;
            _recent.Clear();

            if (Application.Current is App app)
            {
                app.ApplyTheme(_settings.UseDarkMode);
            }

            RefreshView();

            if (_library.StartupWarnings.Count != 0)
            {
                ShowWarning(string.Join(
                    Environment.NewLine,
                    _library.StartupWarnings));
            }
            else
            {
                HideWarning();
            }

            _statusText.Text = "Settings saved.";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Settings could not be applied", ex);
        }
    }

    private async Task RunMutationAsync(
        Func<OperationResult> mutation,
        string success)
    {
        try
        {
            OperationResult result = mutation();
            RefreshView();
            _statusText.Text = success;

            if (!string.IsNullOrWhiteSpace(result.Warning))
            {
                ShowWarning(result.Warning);
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Operation failed", ex);
        }
    }

    private async Task RunMutationAsync<T>(
        Func<OperationResult<T>> mutation,
        string success)
    {
        try
        {
            OperationResult<T> result = mutation();
            RefreshView();
            _statusText.Text = success;

            if (!string.IsNullOrWhiteSpace(result.Warning))
            {
                ShowWarning(result.Warning);
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Operation failed", ex);
        }
    }

    private void RecordRecent(
        Guid id,
        string title,
        string preview)
    {
        _recent.RemoveAll(item => item.Id == id);
        _recent.Insert(0, new RecentPromptDisplay(id, title, preview));

        while (_recent.Count > 3)
        {
            _recent.RemoveAt(_recent.Count - 1);
        }

        _recentItems.ItemsSource = _recent.ToArray();
    }

    private void RemoveRecent(Guid id)
    {
        _recent.RemoveAll(item => item.Id == id);
        _recentItems.ItemsSource = _recent.ToArray();
    }

    private void ShowWarning(string message)
    {
        _warningText.Text = message;
        _warningBorder.IsVisible = true;
    }

    private void HideWarning()
    {
        _warningText.Text = string.Empty;
        _warningBorder.IsVisible = false;
    }

    private async Task ShowErrorAsync(
        string title,
        Exception ex)
    {
        _statusText.Text = title;
        await Dialogs.ShowMessageAsync(
            this,
            title,
            ex.Message);
    }

    private void Window_KeyDown(
        object? sender,
        KeyEventArgs e)
    {
        bool control =
            e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift =
            e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool alt =
            e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        if (control && e.Key == Key.N)
        {
            if (shift)
            {
                AddCategory_Click(this, new RoutedEventArgs());
            }
            else
            {
                AddPrompt_Click(this, new RoutedEventArgs());
            }
            e.Handled = true;
            return;
        }

        if (alt &&
            e.Key == Key.Left &&
            _currentCategoryId is not null)
        {
            IReadOnlyList<BreadcrumbRecord> crumbs =
                _library.GetBreadcrumbs(_currentCategoryId);
            _currentCategoryId =
                crumbs.Count >= 2
                    ? crumbs[^2].CategoryId
                    : null;
            RefreshView();
            e.Handled = true;
        }
    }
}
