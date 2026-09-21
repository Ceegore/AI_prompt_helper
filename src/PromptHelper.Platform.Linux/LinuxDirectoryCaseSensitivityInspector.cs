namespace PromptHelper.Services;

public sealed class LinuxDirectoryCaseSensitivityInspector : IDirectoryCaseSensitivityInspector
{
    public DirectoryCaseSensitivityState Inspect(string existingDirectory)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "LinuxDirectoryCaseSensitivityInspector can only be used on Linux.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(existingDirectory);

        string directory = Path.GetFullPath(existingDirectory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"Case-sensitivity probe directory does not exist: '{directory}'.");
        }

        string token = Guid.NewGuid().ToString("N");
        string probeName = $".prompthelper-case-probe-{token}-A";
        string alternateName = $".prompthelper-case-probe-{token}-a";
        string probePath = Path.Combine(directory, probeName);
        string alternatePath = Path.Combine(directory, alternateName);

        try
        {
            using (new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete))
            {
            }

            return File.Exists(alternatePath)
                ? DirectoryCaseSensitivityState.CaseInsensitive
                : DirectoryCaseSensitivityState.CaseSensitive;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DirectoryCaseSensitivityInspectionException(
                directory,
                ex.Message,
                ex);
        }
        finally
        {
            try
            {
                File.Delete(probePath);
            }
            catch
            {
                // The original inspection failure remains authoritative. A later capability
                // probe will reject an unwritable data root if cleanup could not complete.
            }
        }
    }
}
