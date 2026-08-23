using System;
using System.IO;
using System.Text;

namespace PromptHelper.Services;

internal sealed class WindowsDurableSettingsFileWriter : IDurableSettingsFileWriter
{
    public void WriteDurable(string targetPath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(content);

        string directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException($"Settings target path has no directory: '{targetPath}'.");

        Directory.CreateDirectory(directory);

        string tempFileName = SettingsTempName.Generate(targetPath, Guid.NewGuid());
        string tempPath = Path.Combine(directory, tempFileName);
        byte[] bytes = new UTF8Encoding(false).GetBytes(content);

        // CRUU14-001: retain the staging handle from creation through promotion; never
        // close it and re-open the temp path by name for the rename or for cleanup.
        using var stage = WindowsOwnedDurableStage.CreateNewInResolvedParent(tempPath);
        try
        {
            stage.Write(bytes);
            stage.FlushDurable();
            stage.PromoteReplaceExact(targetPath);
        }
        catch (Exception primaryFailure)
        {
            try
            {
                stage.DeleteExact();
            }
            catch (Exception cleanupEx)
            {
                throw new IOException(
                    $"Settings write failed for '{targetPath}' and staging cleanup failed for '{tempPath}': {primaryFailure.Message} | Cleanup: {cleanupEx.Message}",
                    primaryFailure);
            }

            throw;
        }
    }
}
