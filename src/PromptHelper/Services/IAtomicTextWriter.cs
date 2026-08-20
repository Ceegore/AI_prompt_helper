namespace PromptHelper.Services;

public interface IAtomicTextWriter
{
    void Write(string targetPath, string content);
}