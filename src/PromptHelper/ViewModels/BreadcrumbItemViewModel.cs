using PromptHelper.Infrastructure;

namespace PromptHelper.ViewModels;

public sealed class BreadcrumbItemViewModel : ObservableObject
{
    public BreadcrumbItemViewModel(Guid? categoryId, string name, bool isCurrent)
    {
        CategoryId = categoryId;
        Name = name;
        IsCurrent = isCurrent;
    }

    public Guid? CategoryId { get; }
    public string Name { get; }
    public bool IsCurrent { get; }
    public bool IsClickable => !IsCurrent;
}