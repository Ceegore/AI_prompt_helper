using System.IO;
using PromptHelper.Infrastructure;

namespace PromptHelper.ViewModels;

public sealed class PromptCardViewModel : ObservableObject
{
    private string _copyButtonText = "Copy";
    private bool _isCopying;

    public PromptCardViewModel(Guid id, string content, bool isContentAvailable, string? loadError)
    {
        Id = id;
        Content = content;
        IsContentAvailable = isContentAvailable;
        LoadError = loadError;
        PreviewTitle = ComputePreviewTitle(content, isContentAvailable);
    }

    public Guid Id { get; }
    public string Content { get; }
    public bool IsContentAvailable { get; }
    public string? LoadError { get; }
    public string PreviewTitle { get; }

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