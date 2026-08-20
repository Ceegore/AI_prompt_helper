namespace PromptHelper.Services;

public sealed record DataRootRelationship(
    string LexicalCurrent,
    string LexicalTarget,
    string PhysicalCurrent,
    string PhysicalTarget,
    bool SamePhysicalRoot);
