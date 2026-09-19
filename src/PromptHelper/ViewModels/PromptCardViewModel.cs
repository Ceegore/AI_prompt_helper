using System.IO;
using PromptHelper.Infrastructure;

namespace PromptHelper.ViewModels;

public sealed class PromptCardViewModel : ObservableObject
{
    private string _copyButtonText = "Copy";
    private bool _isCopying;
    private readonly Func<string>? _contentLoader;
    private string _content;
    private bool _isContentLoaded;
    private bool _isContentAvailable;
    private string? _loadError;
    private string _previewTitle;
    private string _displayText;

    public PromptCardViewModel(Guid id, string? customTitle, string content, bool isContentAvailable, string? loadError)
    {
        Id = id;
        CustomTitle = string.IsNullOrWhiteSpace(customTitle) ? null : customTitle.Trim();
        _content = content;
        _isContentLoaded = true;
        _isContentAvailable = isContentAvailable;
        _loadError = loadError;
        _previewTitle = CustomTitle is not null
            ? TextUtilities.TruncateWithEllipsis(CustomTitle, 80)
            : ComputePreviewTitle(content, isContentAvailable);
        _displayText = isContentAvailable
            ? content
            : "[Prompt file could not be loaded.]";
    }

    public PromptCardViewModel(Guid id, string? customTitle, Func<string> contentLoader)
    {
        Id = id;
        CustomTitle = string.IsNullOrWhiteSpace(customTitle) ? null : customTitle.Trim();
        _contentLoader = contentLoader ?? throw new ArgumentNullException(nameof(contentLoader));
        _content = string.Empty;
        _isContentLoaded = false;
        _isContentAvailable = true;
        _previewTitle = CustomTitle is not null
            ? TextUtilities.TruncateWithEllipsis(CustomTitle, 80)
            : "(Loading preview…)";
        _displayText = "Loading preview…";
    }

    public Guid Id { get; }
    public string? CustomTitle { get; }
    public string Content
    {
        get
        {
            if (!_isContentLoaded && _contentLoader != null)
            {
                _content = _contentLoader();
                _isContentLoaded = true;
                _isContentAvailable = true;
                _loadError = null;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsContentLoaded));
                OnPropertyChanged(nameof(IsContentAvailable));
                OnPropertyChanged(nameof(LoadError));
            }

            return _content;
        }
    }

    public bool IsContentLoaded => _isContentLoaded;
    public bool IsContentAvailable => _isContentAvailable;
    public string? LoadError => _loadError;
    public string PreviewTitle => _previewTitle;
    public string EditableHeadline => CustomTitle ?? PreviewTitle;

    public string DisplayText => _displayText;

    public void ApplyPreview(PromptPreviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _isContentAvailable = result.IsContentAvailable;
        _loadError = result.LoadError;
        _displayText = result.IsContentAvailable
            ? result.PreviewText + (result.IsTruncated ? "\n…" : string.Empty)
            : "[Prompt file could not be loaded.]";

        _previewTitle = CustomTitle is not null
            ? TextUtilities.TruncateWithEllipsis(CustomTitle, 80)
            : ComputePreviewTitle(result.PreviewText, result.IsContentAvailable);

        OnPropertyChanged(nameof(IsContentAvailable));
        OnPropertyChanged(nameof(LoadError));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(PreviewTitle));
        OnPropertyChanged(nameof(EditableHeadline));
    }

    public string CopyButtonText
    {
        get => _copyButtonText;
        set => SetProperty(ref _copyButtonText, value);
    }

    public bool IsCopying
    {
        get => _isCopying;
        set => SetProperty(ref _isCopying, value);
    }

    public static string ComputePreviewTitle(string content, bool isContentAvailable)
    {
        if (!isContentAvailable)
        {
            return "(Unavailable prompt)";
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return "(Empty prompt)";
        }

        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                return TextUtilities.TruncateWithEllipsis(trimmed, 80);
            }
        }

        return "(Empty prompt)";
    }
}
