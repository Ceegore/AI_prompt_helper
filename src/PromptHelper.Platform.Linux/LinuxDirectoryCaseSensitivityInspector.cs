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

        DirectoryCaseSensitivityState state;
        try
        {
            using (new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete))
            {
            }

            state = File.Exists(alternatePath)
                ? DirectoryCaseSensitivityState.CaseInsensitive
                : DirectoryCaseSensitivityState.CaseSensitive;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteAfterFailure(probePath);
            throw new DirectoryCaseSensitivityInspectionException(
                directory,
                ex.Message,
                ex);
        }

        try
        {
            File.Delete(probePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DirectoryCaseSensitivityInspectionException(
                directory,
                $"The case-sensitivity probe succeeded but its temporary file could not be removed: {ex.Message}",
                ex);
        }

        return state;
    }

    private static void TryDeleteAfterFailure(string probePath)
    {
        try
        {
            File.Delete(probePath);
        }
        catch
        {
            // Preserve the original inspection exception. The probe uses an unguessable,
            // app-owned name, so cleanup must never delete anything except that exact path.
        }
    }
}
