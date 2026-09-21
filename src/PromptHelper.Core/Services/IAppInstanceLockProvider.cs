namespace PromptHelper.Services;

public interface IAppInstanceLease : IDisposable
{
}

public interface IAppInstanceLockProvider
{
    IAppInstanceLease? TryAcquire(string lockPath);

    bool IsExistingLockHeld(string root);
}
