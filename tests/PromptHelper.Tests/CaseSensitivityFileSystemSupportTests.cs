using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using PromptHelper.Services;

namespace PromptHelper.Tests;

/// <summary>
/// Regression coverage for filesystems that do not implement Windows' per-directory
/// case-sensitivity feature.
///
/// Measured on an exFAT volume: GetFileInformationByHandleEx(FileCaseSensitiveInfo) fails
/// with Win32 error 87 (ERROR_INVALID_PARAMETER) for every directory on the volume, root or
/// not. The inspector used to surface that as an inspection failure, so
/// DataRootTopologyValidator.ValidateTransition rejected every exFAT and FAT32 volume with
/// "Failed to verify case-insensitivity for target directory 'E:\'", and
/// ManagedDataRootPolicy.ValidateConfiguredRootForStartup reported the same message instead
/// of the dedicated ConfiguredDataFolderUnavailableException.
///
/// Such a volume cannot hold a case-sensitive directory at all, so it already satisfies the
/// requirement the check exists to enforce.
/// </summary>
[TestClass]
public sealed class CaseSensitivityFileSystemSupportTests
{
    private const int ErrorInvalidFunction = 1;
    private const int ErrorAccessDenied = 5;
    private const int ErrorNotSupported = 50;
    private const int ErrorInvalidParameter = 87;

    private sealed class FakeCaseSensitivityNativeApi : ICaseSensitivityNativeApi
    {
        public int? QueryError { get; init; }
        public uint QueryFlags { get; init; }
        public string? FileSystemName { get; init; }
        public bool FileSystemNameAvailable { get; init; } = true;

        public SafeFileHandle OpenDirectoryHandle(string path)
        {
            // Any non-zero, non-(-1) value reads as valid. The fake never dereferences it.
            return new SafeFileHandle((IntPtr)1, ownsHandle: false);
        }

        public bool QueryCaseSensitiveInfo(SafeFileHandle handle, out uint flags, out int win32Error)
        {
            if (QueryError.HasValue)
            {
                flags = 0;
                win32Error = QueryError.Value;
                return false;
            }

            flags = QueryFlags;
            win32Error = 0;
            return true;
        }

        public bool TryGetFileSystemName(SafeFileHandle handle, out string? fileSystemName)
        {
            if (!FileSystemNameAvailable)
            {
                fileSystemName = null;
                return false;
            }

            fileSystemName = FileSystemName;
            return !string.IsNullOrEmpty(fileSystemName);
        }
    }

    private static WindowsDirectoryCaseSensitivityInspector Inspector(FakeCaseSensitivityNativeApi api)
    {
        return new WindowsDirectoryCaseSensitivityInspector(api);
    }

    [TestMethod]
    [DataRow("exFAT", ErrorInvalidParameter)]
    [DataRow("FAT32", ErrorInvalidParameter)]
    [DataRow("FAT", ErrorInvalidParameter)]
    [DataRow("exFAT", ErrorNotSupported)]
    [DataRow("exFAT", ErrorInvalidFunction)]
    public void Unsupporting_filesystem_reports_case_insensitive(string fileSystem, int queryError)
    {
        var inspector = Inspector(new FakeCaseSensitivityNativeApi
        {
            QueryError = queryError,
            FileSystemName = fileSystem
        });

        Assert.AreEqual(
            DirectoryCaseSensitivityState.CaseInsensitive,
            inspector.Inspect(@"E:\SomeFolder"));
    }

    [TestMethod]
    [DataRow("NTFS")]
    [DataRow("ReFS")]
    [DataRow("ntfs")]
    public void Capable_filesystem_still_fails_closed_on_query_error(string fileSystem)
    {
        var inspector = Inspector(new FakeCaseSensitivityNativeApi
        {
            QueryError = ErrorInvalidParameter,
            FileSystemName = fileSystem
        });

        var ex = Assert.Throws<DirectoryCaseSensitivityInspectionException>(
            () => inspector.Inspect(@"C:\SomeFolder"));
        Assert.AreEqual(ErrorInvalidParameter, ex.Win32ErrorCode);
    }

    [TestMethod]
    public void Unidentifiable_filesystem_fails_closed_on_query_error()
    {
        var inspector = Inspector(new FakeCaseSensitivityNativeApi
        {
            QueryError = ErrorInvalidParameter,
            FileSystemNameAvailable = false
        });

        var ex = Assert.Throws<DirectoryCaseSensitivityInspectionException>(
            () => inspector.Inspect(@"X:\SomeFolder"));
        Assert.AreEqual(ErrorInvalidParameter, ex.Win32ErrorCode);
    }

    [TestMethod]
    public void Non_feature_error_still_fails_closed_on_unsupporting_filesystem()
    {
        // ERROR_ACCESS_DENIED is a real failure to inspect, not "the feature is absent",
        // so it must keep failing closed even on a filesystem without the feature.
        var inspector = Inspector(new FakeCaseSensitivityNativeApi
        {
            QueryError = ErrorAccessDenied,
            FileSystemName = "exFAT"
        });

        var ex = Assert.Throws<DirectoryCaseSensitivityInspectionException>(
            () => inspector.Inspect(@"E:\SomeFolder"));
        Assert.AreEqual(ErrorAccessDenied, ex.Win32ErrorCode);
    }

    [TestMethod]
    public void Successful_query_is_unchanged()
    {
        var caseSensitive = Inspector(new FakeCaseSensitivityNativeApi
        {
            QueryFlags = 0x00000001,
            FileSystemName = "NTFS"
        });
        Assert.AreEqual(
            DirectoryCaseSensitivityState.CaseSensitive,
            caseSensitive.Inspect(@"C:\SomeFolder"));

        var caseInsensitive = Inspector(new FakeCaseSensitivityNativeApi
        {
            QueryFlags = 0,
            FileSystemName = "NTFS"
        });
        Assert.AreEqual(
            DirectoryCaseSensitivityState.CaseInsensitive,
            caseInsensitive.Inspect(@"C:\SomeFolder"));
    }

    [TestMethod]
    public void Unsupporting_filesystem_is_accepted_as_a_data_folder_target()
    {
        // The end-to-end consequence of the bug: a target on a filesystem without the
        // feature must be a usable data folder, not a validation error.
        var api = new FakeCaseSensitivityNativeApi
        {
            QueryError = ErrorInvalidParameter,
            FileSystemName = "exFAT"
        };

        using var current = new TestDirectory();
        string target = Path.Combine(current.Root, "..", "PromptHelperExFatTarget");
        string bootstrap = Path.Combine(Path.GetTempPath(), "PromptHelperBootstrapProbe");

        DataRootRelationship relationship = DataRootTopologyValidator.ValidateTransition(
            current.Root,
            Path.GetFullPath(target),
            bootstrap,
            caseInspector: Inspector(api));

        Assert.IsFalse(relationship.SamePhysicalRoot);
    }

    [TestMethod]
    public void Every_present_volume_can_be_inspected_on_this_machine()
    {
        // Guards the real native path, not a fake: on a machine that has a non-NTFS/ReFS
        // volume mounted (exFAT USB stick, SD card, FAT32 partition) this test is what
        // actually catches a regression. On an all-NTFS machine - including CI - it simply
        // verifies that every mounted volume root inspects cleanly.
        var inspector = new WindowsDirectoryCaseSensitivityInspector();
        var failures = new List<string>();

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType == DriveType.CDRom)
            {
                continue;
            }

            string root;
            try
            {
                root = drive.RootDirectory.FullName;
                if (!Directory.Exists(root))
                {
                    continue;
                }
            }
            catch (IOException)
            {
                continue;
            }

            try
            {
                DirectoryCaseSensitivityState state = inspector.Inspect(root);
                if (state == DirectoryCaseSensitivityState.CaseSensitive)
                {
                    // Legitimate on an NTFS volume whose root was flagged; not a defect.
                    continue;
                }
            }
            catch (DirectoryCaseSensitivityInspectionException ex)
            {
                failures.Add($"{root} ({SafeFormat(drive)}): Win32 {ex.Win32ErrorCode}");
            }
        }

        Assert.AreEqual(
            0,
            failures.Count,
            $"Case-sensitivity inspection failed on mounted volume(s): {string.Join("; ", failures)}");
    }

    private static string SafeFormat(DriveInfo drive)
    {
        try { return drive.DriveFormat; } catch { return "unknown"; }
    }
}
