using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

public sealed class WindowsPhysicalPathResolver : IPhysicalPathResolver
{
    private readonly IStrictDirectoryOpener _directoryOpener;

    internal WindowsPhysicalPathResolver(IStrictDirectoryOpener? directoryOpener = null)
    {
        _directoryOpener = directoryOpener ?? new WindowsStrictDirectoryOpener();
    }

    public WindowsPhysicalPathResolver() : this(null)
    {
    }

    public string ResolveWithNearestExistingAncestor(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string full = Path.GetFullPath(path);
        string current = full;
        var suffix = new Stack<string>();

        while (true)
        {
            DirectoryOpenResult result = _directoryOpener.OpenDirectoryStrict(current);

            if (result.State == DirectoryOpenState.Opened)
            {
                using SafeFileHandle handle = result.Handle!;

                string resolved = WindowsFinalPathHelper.GetNormalizedDosPath(handle);

                while (suffix.Count > 0)
                {
                    resolved = Path.Combine(resolved, suffix.Pop());
                }

                return Path.GetFullPath(resolved);
            }

            string trimmed = current.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            string name = Path.GetFileName(trimmed);

            if (string.IsNullOrEmpty(name))
            {
                throw new DirectoryNotFoundException($"No accessible ancestor for '{full}'.");
            }

            suffix.Push(name);

            string? parent = Path.GetDirectoryName(trimmed);

            if (string.IsNullOrEmpty(parent) || PathIdentity.Equals(parent, current))
            {
                throw new DirectoryNotFoundException($"No accessible ancestor for '{full}'.");
            }

            current = parent;
        }
    }
}
