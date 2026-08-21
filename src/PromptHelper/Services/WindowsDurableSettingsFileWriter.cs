using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PromptHelper.Services;

internal sealed class WindowsDurableSettingsFileWriter : IDurableSettingsFileWriter
{
    private const uint MOVEFILE_REPLACE_EXISTING = 0x00000001;
    private const uint MOVEFILE_WRITE_THROUGH = 0x00000008;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool MoveFileExW(
        string lpExistingFileName,
        string lpNewFileName,
        uint dwFlags);

    public void WriteDurable(string targetPath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(content);

        string directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException($"Settings target path has no directory: '{targetPath}'.");

        Directory.CreateDirectory(directory);

        string tempFileName = SettingsTempName.Generate(targetPath, Guid.NewGuid());
        string tempPath = Path.Combine(directory, tempFileName);

        bool promoted = false;
        Exception? primaryFailure = null;

        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (!MoveFileExW(
                    tempPath,
                    targetPath,
                    MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
            {
                int error = Marshal.GetLastWin32Error();
                throw new IOException(
                    $"Durable settings promotion failed for '{targetPath}'.",
                    new Win32Exception(error));
            }

            promoted = true;
        }
        catch (Exception ex)
        {
            primaryFailure = ex;
            throw;
        }
        finally
        {
            if (!promoted)
            {
                try
                {
                    if (new StrictPathAuthority().Probe(tempPath).Kind != StrictPathKind.Missing)
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception cleanupEx)
                {
                    throw new IOException(
                        $"Settings write failed for '{targetPath}' and staging cleanup failed for '{tempPath}': {primaryFailure?.Message} | Cleanup: {cleanupEx.Message}",
                        primaryFailure ?? cleanupEx);
                }
            }
        }
    }
}
