using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class FakeApplicationLifetime : IApplicationLifetime
{
    public bool ShutdownRequested { get; private set; }
    public int ShutdownRequestCount { get; private set; }

    public void RequestShutdown()
    {
        ShutdownRequested = true;
        ShutdownRequestCount++;
    }
}
