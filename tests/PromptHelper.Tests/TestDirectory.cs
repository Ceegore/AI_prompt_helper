using System.IO;

namespace PromptHelper.Tests;

public sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "PromptHelperTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch
        {
            // Test cleanup only.
        }
    }
}