using PromptHelper.Infrastructure;

namespace PromptHelper.ViewModels;

public sealed class CategoryItemViewModel : ObservableObject
{
    public CategoryItemViewModel(Guid id, Guid? parentId, string name)
    {
        Id = id;
        ParentId = parentId;
        Name = name;
    }

    public Guid Id { get; }
    public Guid? ParentId { get; }
    public string Name { get; }
}