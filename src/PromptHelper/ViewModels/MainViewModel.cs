using System.Collections.ObjectModel;
using PromptHelper.Infrastructure;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly PromptLibraryService _service;
    private readonly PromptRepository _promptRepo;
    private Guid? _currentCategoryId;

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
        var promptsData = _service.GetPrompts(CurrentCategoryId);
        Prompts.Clear();
        foreach (var prompt in promptsData)
        {
            Prompts.Add(new PromptCardViewModel(
                prompt.Id,
                prompt.Content,
                prompt.IsContentAvailable,
                prompt.LoadError));
        }

        OnPropertyChanged(nameof(HasPrompts));
        OnPropertyChanged(nameof(HasNoPrompts));
        OnPropertyChanged(nameof(HasChildCategories));
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

    public OperationResult<PromptRecord> CreatePrompt(string content)
    {
        var result = _service.CreatePrompt(CurrentCategoryId, content);
        Refresh();
        return result;
    }

    public OperationResult EditPrompt(Guid promptId, string content)
    {
        var result = _service.EditPrompt(promptId, content);
        Refresh();
        return result;
    }

    public OperationResult DeletePrompt(Guid promptId)
    {
        var result = _service.DeletePrompt(promptId);
        Refresh();
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
}