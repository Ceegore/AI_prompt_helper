using System.IO;
using PromptHelper.Infrastructure;

namespace PromptHelper.ViewModels;

public sealed class PromptCardViewModel : ObservableObject
{
    private string _copyButtonText = "Copy";
    private bool _isCopying;

    public PromptCardViewModel(Guid id, string? customTitle, string content, bool isContentAvailable, string? loadError)
    {
        Id = id;
        CustomTitle = string.IsNullOrWhiteSpace(customTitle) ? null : customTitle.Trim();
        Content = content;
        IsContentAvailable = isContentAvailable;
        LoadError = loadError;
        PreviewTitle = CustomTitle is not null
            ? TextUtilities.TruncateWithEllipsis(CustomTitle, 80)
            : ComputePreviewTitle(content, isContentAvailable);
    }

    public Guid Id { get; }
    public string? CustomTitle { get; }
    public string Content { get; }
    public bool IsContentAvailable { get; }
    public string? LoadError { get; }
    public string PreviewTitle { get; }
    public string EditableHeadline => CustomTitle ?? PreviewTitle;

    public string DisplayText => IsContentAvailable
        ? Content
        : "[Prompt file could not be loaded.]";

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