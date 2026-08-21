using System;
using System.ComponentModel;
using System.IO;

namespace PromptHelper.Services;

public enum DirectoryCaseSensitivityState
{
    CaseInsensitive,
    CaseSensitive
}

public sealed class DirectoryCaseSensitivityInspectionException : IOException
{
    public string DirectoryPath { get; }
    public int Win32ErrorCode { get; }

    public DirectoryCaseSensitivityInspectionException(string path, int win32Error)
        : base($"Failed to inspect case sensitivity of directory '{path}' (Win32 error: {win32Error}).", new Win32Exception(win32Error))
    {
        DirectoryPath = path;
        Win32ErrorCode = win32Error;
    }
}

public interface IDirectoryCaseSensitivityInspector
{
    DirectoryCaseSensitivityState Inspect(string existingDirectory);
    bool IsCaseSensitive(string existingDirectory);
}
