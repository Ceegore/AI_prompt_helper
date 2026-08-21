
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

internal enum DirectoryOpenState
{
    Missing,
    Opened
}

internal sealed record DirectoryOpenResult(
    DirectoryOpenState State,
    SafeFileHandle? Handle);

internal interface IStrictDirectoryOpener
{
    DirectoryOpenResult OpenDirectoryStrict(string path);
}
