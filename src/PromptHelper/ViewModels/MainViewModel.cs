using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using PromptHelper.Infrastructure;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly PromptLibraryService _service;
    private readonly PromptRepository _promptRepo;
    private Guid? _currentCategoryId;
    private int _promptCollectionVersion;

    public MainViewModel(
        PromptLibraryService service,
        PromptRepository promptRepo,
        string dataFolderPath)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _promptRepo = promptRepo ?? throw new ArgumentNullException(nameof(promptRepo));
        DataFolderPath = dataFolderPath;

        Breadcrumbs = new ObservableCollection<BreadcrumbItemViewModel>();
        ChildCategories = new ObservableCollection<CategoryItemViewModel>();
        Prompts = new ObservableCollection<PromptCardViewModel>();
        PromptRows = new ObservableCollection<PromptRowViewModel>();

        Refresh();
    }

    public string DataFolderPath { get; }

    public Guid? CurrentCategoryId
    {
        get => _currentCategoryId;
        private set => SetProperty(ref _currentCategoryId, value);
    }

    public ObservableCollection<BreadcrumbItemViewModel> Breadcrumbs { get; }
    public ObservableCollection<CategoryItemViewModel> ChildCategories { get; }
    public ObservableCollection<PromptCardViewModel> Prompts { get; }
    public ObservableCollection<PromptRowViewModel> PromptRows { get; }
    public ObservableCollection<RecentPromptViewModel> RecentPrompts { get; } = new();
    public event EventHandler? PromptsChanged;

    public bool HasPrompts => Prompts.Count > 0;
    public bool HasNoPrompts => Prompts.Count == 0;
    public bool HasChildCategories => ChildCategories.Count > 0;

    public void NavigateTo(Guid? categoryId)
    {
        CurrentCategoryId = categoryId;
        Refresh();
    }

    public void Refresh()
    {
        // Invalidate in-flight preview work before replacing any cards. The version also
        // gives preview completion an O(1) stale-result check instead of scanning the entire
        // ObservableCollection once for every prompt.
        Interlocked.Increment(ref _promptCollectionVersion);

        // Breadcrumbs
        var breadcrumbsData = _service.GetBreadcrumbs(CurrentCategoryId);
        Breadcrumbs.Clear();
        for (int i = 0; i < breadcrumbsData.Count; i++)
        {
            bool isCurrent = i == breadcrumbsData.Count - 1;
            Breadcrumbs.Add(new BreadcrumbItemViewModel(
                breadcrumbsData[i].CategoryId,
                breadcrumbsData[i].Name,
                isCurrent));
        }

        // Child Categories
        var categoriesData = _service.GetCategories(CurrentCategoryId);
        ChildCategories.Clear();
        foreach (var cat in categoriesData)
        {
            ChildCategories.Add(new CategoryItemViewModel(cat.Id, cat.ParentId, cat.Name));
        }

        // Prompts
        var promptsData = _service.GetPromptSummaries(CurrentCategoryId);
        Prompts.Clear();
        foreach (var prompt in promptsData)
        {
            Prompts.Add(new PromptCardViewModel(
                prompt.Id,
                prompt.Title,
                () => _promptRepo.Read(prompt.Id)));
        }

        PromptRows.Clear();
        for (int i = 0; i < Prompts.Count; i += 3)
        {
            PromptRows.Add(new PromptRowViewModel(Prompts.Skip(i).Take(3).ToArray()));
        }

        OnPropertyChanged(nameof(HasPrompts));
        OnPropertyChanged(nameof(HasNoPrompts));
        OnPropertyChanged(nameof(HasChildCategories));
        PromptsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task LoadPromptPreviewsAsync(CancellationToken cancellationToken = default)
    {
        int collectionVersion = Volatile.Read(ref _promptCollectionVersion);
        PromptCardViewModel[] cards = Prompts.ToArray();

        // Process at most four cards at a time. Creating one suspended Task per prompt can
        // consume substantial memory for very large libraries even though only four reads
        // are active. Batching keeps both I/O and task allocation strictly bounded.
        foreach (PromptCardViewModel[] batch in cards.Chunk(4))
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task<(PromptCardViewModel Card, PromptPreviewResult Preview)>[] loads = batch
                .Select(async card =>
                {
                    PromptPreviewResult preview = await _service.LoadPromptPreviewAsync(
                        card.Id,
                        maxCharacters: 4000,
                        cancellationToken);
                    return (card, preview);
                })
                .ToArray();

            (PromptCardViewModel Card, PromptPreviewResult Preview)[] results =
                await Task.WhenAll(loads);

            if (collectionVersion != Volatile.Read(ref _promptCollectionVersion))
            {
                return;
            }

            foreach ((PromptCardViewModel card, PromptPreviewResult preview) in results)
            {
                card.ApplyPreview(preview);
            }
        }
    }

    public void RecordSuccessfulPromptCopy(
        Guid promptId,
        string headline,
        string currentContent)
    {
        var existing = RecentPrompts.FirstOrDefault(x => x.Id == promptId);
        if (existing != null)
        {
            RecentPrompts.Remove(existing);
        }

        string excerpt = TextUtilities.CreateCompactPreview(currentContent);
        var item = existing ?? new RecentPromptViewModel(
            promptId,
            headline,
            excerpt);

        item.RefreshDisplay(headline, excerpt);

        RecentPrompts.Insert(0, item);

        while (RecentPrompts.Count > 3)
        {
            RecentPrompts.RemoveAt(RecentPrompts.Count - 1);
        }
    }

    public void RemoveRecentPrompt(Guid promptId)
    {
        var existing = RecentPrompts.FirstOrDefault(x => x.Id == promptId);
        if (existing != null)
        {
            RecentPrompts.Remove(existing);
        }
    }

    public void RefreshRecentPromptDisplay(Guid promptId)
    {
        var existing = RecentPrompts.FirstOrDefault(x => x.Id == promptId);
        if (existing == null)
        {
            return;
        }

        var matchingCard = Prompts.FirstOrDefault(p => p.Id == promptId);
        if (matchingCard != null)
        {
            existing.RefreshDisplay(
                matchingCard.PreviewTitle,
                TextUtilities.CreateCompactPreview(matchingCard.Content));
        }
    }

    public bool CanDeleteCategory(Guid categoryId, out string? reason)
    {
        return _service.CanDeleteCategory(categoryId, out reason);
    }

    public OperationResult<CategoryRecord> CreateCategory(string name)
    {
        var result = _service.CreateCategory(CurrentCategoryId, name);
        Refresh();
        return result;
    }

    public OperationResult RenameCategory(Guid categoryId, string newName)
    {
        var result = _service.RenameCategory(categoryId, newName);
        Refresh();
        return result;
    }

    public OperationResult DeleteCategory(Guid categoryId)
    {
        var result = _service.DeleteCategory(categoryId);
        Refresh();
        return result;
    }

    public OperationResult<PromptRecord> CreatePrompt(string content, string? title)
    {
        var result = _service.CreatePrompt(CurrentCategoryId, content, title);
        Refresh();
        return result;
    }

    public OperationResult<PromptRecord> CreatePrompt(string content)
        => CreatePrompt(content, null);

    public OperationResult EditPrompt(Guid promptId, string content, string? title)
    {
        var result = _service.EditPrompt(promptId, content, title);
        Refresh();
        RefreshRecentPromptDisplay(promptId);
        return result;
    }

    public OperationResult EditPrompt(Guid promptId, string content)
    {
        var currentCard = Prompts.FirstOrDefault(p => p.Id == promptId);
        if (currentCard != null)
        {
            return EditPrompt(promptId, content, currentCard.CustomTitle);
        }

        var result = _service.EditPrompt(promptId, content);
        Refresh();
        RefreshRecentPromptDisplay(promptId);
        return result;
    }

    public OperationResult DeletePrompt(Guid promptId)
    {
        var result = _service.DeletePrompt(promptId);
        Refresh();
        RemoveRecentPrompt(promptId);
        return result;
    }

    public OperationResult MovePrompt(Guid promptId, Guid? destinationCategoryId)
    {
        var result = _service.MovePrompt(promptId, destinationCategoryId);
        Refresh();
        return result;
    }

    public OperationResult<PromptRecord> DuplicatePrompt(Guid promptId, Guid? destinationCategoryId)
    {
        var result = _service.DuplicatePrompt(promptId, destinationCategoryId);
        Refresh();
        return result;
    }

    public IReadOnlyList<DestinationOptionViewModel> GetDestinations()
    {
        return _service.GetDestinations()
            .Select(d => new DestinationOptionViewModel(d.CategoryId, d.DisplayPath))
            .ToList();
    }

    public string GetPromptContent(Guid promptId)
    {
        return _promptRepo.Read(promptId);
    }

    public Task<string> GetPromptContentAsync(Guid promptId, CancellationToken cancellationToken = default)
    {
        return _promptRepo.ReadAsync(promptId, cancellationToken);
    }
}
