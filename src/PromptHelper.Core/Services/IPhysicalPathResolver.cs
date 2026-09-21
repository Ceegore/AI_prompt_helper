namespace PromptHelper.Services;

public interface IPhysicalPathResolver
{
    string ResolveWithNearestExistingAncestor(string path);
}
