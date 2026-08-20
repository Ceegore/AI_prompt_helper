using PromptHelper.Infrastructure;

namespace PromptHelper.ViewModels;

public sealed class RecentPromptViewModel : ObservableObject
{
    private string _headline;
    private string _excerpt;
    private string _copyButtonText = "Copy";
    private bool _isCopying;

    public RecentPromptViewModel(Guid id, string headline, string excerpt)
    {
        Id = id;
        _headline = headline;
        _excerpt = excerpt;
    }

    public Guid Id { get; }

    public string Headline
    {
        get => _headline;
        private set => SetProperty(ref _headline, value);
    }

    public string Excerpt
    {
        get => _excerpt;
        private set => SetProperty(ref _excerpt, value);
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

    public void RefreshDisplay(string headline, string excerpt)
    {
        Headline = headline;
        Excerpt = excerpt;
    }
}
