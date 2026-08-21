namespace PromptHelper.Services;

internal sealed record BoundTargetRoot(
    string LexicalRoot,
    string PhysicalRoot,
    DataRootRelationship InitialRelationship);
