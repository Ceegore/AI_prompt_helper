namespace PromptHelper.Services;

public interface IDirectoryCaseSensitivityInspector
{
    bool IsCaseSensitive(string existingDirectory);
}
