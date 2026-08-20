using PromptHelper.Infrastructure;

namespace PromptHelper.ViewModels;

public sealed class DestinationOptionViewModel : ObservableObject
{
    public DestinationOptionViewModel(Guid? categoryId, string displayPath)
    {
        CategoryId = categoryId;
        DisplayPath = displayPath;
    }

    public Guid? CategoryId { get; }
    public string DisplayPath { get; }
}