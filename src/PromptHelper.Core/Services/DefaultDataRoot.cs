namespace PromptHelper.Services;

public static class DefaultDataRoot
{
    public static string Path
    {
        get
        {
            string baseDirectory = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                throw new InvalidOperationException(
                    "The operating system did not provide a local application-data directory.");
            }

            return System.IO.Path.Combine(baseDirectory, "PromptHelper");
        }
    }
}
