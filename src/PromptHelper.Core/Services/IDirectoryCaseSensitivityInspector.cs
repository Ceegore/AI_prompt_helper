using System.ComponentModel;

namespace PromptHelper.Services;

public enum DirectoryCaseSensitivityState
{
    CaseInsensitive,
    CaseSensitive
}

public sealed class DirectoryCaseSensitivityInspectionException : IOException
{
    public string DirectoryPath { get; }
    public int NativeErrorCode { get; }

    // Compatibility alias for the existing Windows implementation and tests.
    public int Win32ErrorCode => NativeErrorCode;

    public DirectoryCaseSensitivityInspectionException(string path, int nativeErrorCode)
        : base(
            $"Failed to inspect case sensitivity of directory '{path}' (native error: {nativeErrorCode}).",
            new Win32Exception(nativeErrorCode))
    {
        DirectoryPath = path;
        NativeErrorCode = nativeErrorCode;
    }

    public DirectoryCaseSensitivityInspectionException(
        string path,
        string message,
        Exception innerException)
        : base($"Failed to inspect case sensitivity of directory '{path}': {message}", innerException)
    {
        DirectoryPath = path;
        NativeErrorCode = 0;
    }
}

public interface IDirectoryCaseSensitivityInspector
{
    DirectoryCaseSensitivityState Inspect(string existingDirectory);
}
