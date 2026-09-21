namespace PromptHelper.Services;

public sealed class WpfApplicationLifetime : IApplicationLifetime
{
    public void RequestShutdown()
    {
        System.Windows.Application.Current?.Shutdown();
    }
}
