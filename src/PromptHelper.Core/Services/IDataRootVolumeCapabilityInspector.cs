using System.ComponentModel;

namespace PromptHelper.Services;

/// <summary>
/// What a candidate data folder's volume can do, as far as Prompt Helper's durability model
/// is concerned.
/// </summary>
/// <param name="FileSystemName">
/// The filesystem backing the inspected directory ("NTFS", "ReFS", "exFAT", "ext4", ...), or
/// null when the platform would not name it.
/// </param>
/// <param name="SupportsFileIdentity">
/// Whether a durable identity can be read from an open handle on this volume - the capability
/// <see cref="FileObjectIdentity"/> is built on, and therefore the one the whole crash-safe
/// write path depends on.
/// </param>
public sealed record DataRootVolumeCapabilities(
    string? FileSystemName,
    bool SupportsFileIdentity);

/// <summary>
/// Raised when a candidate data folder sits on a volume whose filesystem cannot provide a
/// capability Prompt Helper's durability guarantee is built on.
///
/// This exists so the refusal is an explanation rather than a leaked native diagnostic.
/// Before it, choosing a folder on an exFAT drive - a USB stick, an SD card, most large
/// external drives - failed deep inside a staging write with
/// "Unable to inspect staging file attributes ... (Win32 error: 87)", which tells the user
/// neither what is wrong nor what to do (CRUU22-002).
///
/// The platform-specific remedy is supplied by the caller, because which filesystems are
/// affected and what the user should choose instead are platform facts, not core ones.
/// </summary>
public sealed class UnsupportedDataRootFilesystemException : Exception
{
    public UnsupportedDataRootFilesystemException(
        string dataFolderPath,
        string? fileSystemName,
        string missingCapability,
        string? remedy = null)
        : base(BuildMessage(dataFolderPath, fileSystemName, missingCapability, remedy))
    {
        DataFolderPath = dataFolderPath;
        FileSystemName = fileSystemName;
        MissingCapability = missingCapability;
    }

    public string DataFolderPath { get; }
    public string? FileSystemName { get; }
    public string MissingCapability { get; }

    private static string BuildMessage(
        string dataFolderPath,
        string? fileSystemName,
        string missingCapability,
        string? remedy)
    {
        string filesystem = string.IsNullOrEmpty(fileSystemName)
            ? "The filesystem of that drive"
            : fileSystemName;

        string message =
            $"'{dataFolderPath}' cannot be used as the Prompt Helper data folder. " +
            $"{filesystem} does not support {missingCapability}, which Prompt Helper requires " +
            "to guarantee that a save is never left half-written if the application or the " +
            "machine stops unexpectedly.";

        return string.IsNullOrWhiteSpace(remedy) ? message : $"{message} {remedy}";
    }
}

/// <summary>
/// Raised when the volume's capabilities could not be determined at all. Distinct from
/// <see cref="UnsupportedDataRootFilesystemException"/>: "unsupported" is a verdict, this is
/// the absence of one, and callers fail closed on it.
/// </summary>
public sealed class DataRootVolumeCapabilityInspectionException : IOException
{
    public DataRootVolumeCapabilityInspectionException(string path, int nativeErrorCode)
        : base(
            $"Unable to determine the filesystem capabilities of '{path}' (native error: {nativeErrorCode}).",
            new Win32Exception(nativeErrorCode))
    {
        Path = path;
        NativeErrorCode = nativeErrorCode;
    }

    public DataRootVolumeCapabilityInspectionException(string path, string message, Exception innerException)
        : base($"Unable to determine the filesystem capabilities of '{path}': {message}", innerException)
    {
        Path = path;
        NativeErrorCode = 0;
    }

    public string Path { get; }
    public int NativeErrorCode { get; }

    // Compatibility alias, matching DirectoryCaseSensitivityInspectionException.
    public int Win32ErrorCode => NativeErrorCode;
}

public interface IDataRootVolumeCapabilityInspector
{
    /// <summary>
    /// Inspects the volume backing an existing directory.
    /// </summary>
    /// <exception cref="DataRootVolumeCapabilityInspectionException">
    /// The capabilities could not be determined.
    /// </exception>
    DataRootVolumeCapabilities Inspect(string existingDirectory);
}
