using System;
using System.Threading;
using System.Threading.Tasks;
using PromptHelper.ViewModels;

namespace PromptHelper.Services;

public sealed class PromptCopyCoordinator
{
    private readonly MainViewModel _viewModel;
    private readonly IClipboardService _clipboard;

    public PromptCopyCoordinator(
        MainViewModel viewModel,
        IClipboardService clipboard)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
    }

    public string Copy(Guid promptId, string effectiveHeadline)
    {
        string text = _viewModel.GetPromptContent(promptId);
        _clipboard.CopyText(text);
        _viewModel.RecordSuccessfulPromptCopy(
            promptId,
            effectiveHeadline,
            text);
        return text;
    }

    public async Task<string> CopyAsync(
        Guid promptId,
        string effectiveHeadline,
        CancellationToken cancellationToken = default)
    {
        string text = await _viewModel.GetPromptContentAsync(promptId, cancellationToken);
        _clipboard.CopyText(text);
        _viewModel.RecordSuccessfulPromptCopy(promptId, effectiveHeadline, text);
        return text;
    }
}
