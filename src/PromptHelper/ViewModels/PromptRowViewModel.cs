namespace PromptHelper.ViewModels;

public sealed class PromptRowViewModel
{
    public PromptRowViewModel(IReadOnlyList<PromptCardViewModel> prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        if (prompts.Count is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(prompts), "A prompt row must contain one to three cards.");
        }

        Prompts = prompts;
    }

    public IReadOnlyList<PromptCardViewModel> Prompts { get; }
}
