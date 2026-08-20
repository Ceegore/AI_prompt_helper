namespace PromptHelper.Services;

public interface IApplicationLifetime
{
    void RequestShutdown();
}

public sealed class WpfApplicationLifetime : IApplicationLifetime
{
    public void RequestShutdown()
    {
        System.Windows.Application.Current?.Shutdown();
    }
}
