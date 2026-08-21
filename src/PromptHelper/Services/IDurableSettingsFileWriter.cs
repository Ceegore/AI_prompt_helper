using System;

namespace PromptHelper.Services;

public interface IDurableSettingsFileWriter
{
    void WriteDurable(string targetPath, string content);
}
