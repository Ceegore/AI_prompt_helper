using System;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

internal interface IManagedDirectoryHandleApi
{
    SafeFileHandle OpenManagedDirectoryWithoutDeleteShare(string path);
    Exception CreateLastError(string path);
}
