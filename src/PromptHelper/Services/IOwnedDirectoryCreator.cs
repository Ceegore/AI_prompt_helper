using System;

namespace PromptHelper.Services;

internal interface IOwnedDirectoryCreator
{
    DirectoryCreateOutcome TryCreateOwned(string path);
}

internal sealed class WindowsOwnedDirectoryCreator : IOwnedDirectoryCreator
{
    private readonly IReservationFileOps _ops;

    public WindowsOwnedDirectoryCreator(IReservationFileOps? ops = null)
    {
        _ops = ops ?? new DefaultReservationFileOps();
    }

    public DirectoryCreateOutcome TryCreateOwned(string path)
    {
        return _ops.TryCreateDirectoryOwned(path);
    }
}
