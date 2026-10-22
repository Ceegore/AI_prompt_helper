using System;
using System.ComponentModel;
using System.IO;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[ProductionEvidenceTestClass]
[DoNotParallelize]
public sealed class Cruu22RegressionTests
{
    private const int ErrorInvalidFunction = 1;
    private const int ErrorAccessDenied = 5;
    private const int ErrorNotSupported = 50;
    private const int ErrorInvalidParameter = 87;

    private sealed class FakeVolumeCapabilityNativeApi : IVolumeCapabilityNativeApi
    {
        public int? FileIdError { get; init; }
        public string? FileSystemName { get; init; } = "NTFS";
        public bool FileSystemNameAvailable { get; init; } = true;
        public bool HandleInvalid { get; init; }

        public SafeFileHandle OpenDirectoryHandle(string path)
        {
            return HandleInvalid
                ? new SafeFileHandle(IntPtr.Zero, ownsHandle: false)
                : new SafeFileHandle((IntPtr)1, ownsHandle: false);
        }

        public bool TryReadFileId(SafeFileHandle handle, out int win32Error)
        {
            if (FileIdError.HasValue)
            {
                win32Error = FileIdError.Value;
                return false;
            }

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

    private static WindowsDataRootVolumeCapabilityInspector Inspector(FakeVolumeCapabilityNativeApi api)
    {
        return new WindowsDataRootVolumeCapabilityInspector(api);
    }

    // ------------------------------ CRUU22-001 -----------------------------------------
    // A filesystem that does not implement Windows' per-directory case-sensitivity feature
    // was reported as an inspection failure, so every exFAT/FAT32 volume - and any other
    // filesystem lacking the query - was refused as a data folder with a message claiming a
    // case-insensitivity problem that cannot exist there.
    //
    // Behavioural coverage lives in CaseSensitivityFileSystemSupportTests; these two bind the
    // finding to the production path a user actually traverses.

    [TestMethod]
    [ProductionSymbolEvidence("WindowsDirectoryCaseSensitivityInspector.Inspect")]
    public void CRUU22_001_Filesystem_without_case_sensitivity_feature_is_not_an_inspection_failure()
    {
        var api = new CaseSensitivityApi { QueryError = ErrorInvalidParameter, FileSystemName = "exFAT" };
        Assert.AreEqual(
            DirectoryCaseSensitivityState.CaseInsensitive,
            new WindowsDirectoryCaseSensitivityInspector(api).Inspect(@"E:\Folder"));
    }

    [TestMethod]
    [ProductionSymbolEvidence("WindowsDirectoryCaseSensitivityInspector.Inspect")]
    public void CRUU22_001_Capable_filesystem_query_failure_still_fails_closed()
    {
        var api = new CaseSensitivityApi { QueryError = ErrorInvalidParameter, FileSystemName = "NTFS" };
        var ex = Assert.Throws<DirectoryCaseSensitivityInspectionException>(
            () => new WindowsDirectoryCaseSensitivityInspector(api).Inspect(@"C:\Folder"));
        Assert.AreEqual(ErrorInvalidParameter, ex.Win32ErrorCode);
    }

    private sealed class CaseSensitivityApi : ICaseSensitivityNativeApi
    {
        public int? QueryError { get; init; }
        public uint QueryFlags { get; init; }
        public string? FileSystemName { get; init; }

        public SafeFileHandle OpenDirectoryHandle(string path) => new((IntPtr)1, ownsHandle: false);

        public bool QueryCaseSensitiveInfo(SafeFileHandle handle, out uint flags, out int win32Error)
        {
            if (QueryError.HasValue) { flags = 0; win32Error = QueryError.Value; return false; }
            flags = QueryFlags; win32Error = 0; return true;
        }

        public bool TryGetFileSystemName(SafeFileHandle handle, out string? fileSystemName)
        {
            fileSystemName = FileSystemName;
            return !string.IsNullOrEmpty(fileSystemName);
        }
    }

    // ------------------------------ CRUU22-002 -----------------------------------------
    // A volume whose filesystem has no 128-bit file IDs cannot support the provenance model
    // the crash-safe write path is built on, but nothing checked for it up front. Selecting
    // such a folder failed deep inside a staging write with
    // "Unable to inspect staging file attributes ... (Win32 error: 87)" - an internal
    // diagnostic that names neither the cause nor the remedy.

    [TestMethod]
    [ProductionSymbolEvidence("WindowsDataRootVolumeCapabilityInspector.Inspect")]
    public void CRUU22_002_Volume_without_file_identity_is_reported_as_unsupported()
    {
        // Not [DataRow]: a data-driven test is recorded in the TRX as "Name (87)", so the
        // exact-name evidence gate reads the parent as Not Executed and the sentinel proves
        // nothing. Every mapped sentinel in this repository is a plain [TestMethod].
        foreach (int fileIdError in new[] { ErrorInvalidParameter, ErrorNotSupported, ErrorInvalidFunction })
        {
            DataRootVolumeCapabilities caps = Inspector(new FakeVolumeCapabilityNativeApi
            {
                FileIdError = fileIdError,
                FileSystemName = "exFAT"
            }).Inspect(@"E:\Folder");

            Assert.IsFalse(caps.SupportsFileIdentity, $"Win32 {fileIdError} must mean no file identity.");
            Assert.AreEqual("exFAT", caps.FileSystemName);
        }
    }

    [TestMethod]
    [ProductionSymbolEvidence("WindowsDataRootVolumeCapabilityInspector.Inspect")]
    public void CRUU22_002_Volume_with_file_identity_is_supported()
    {
        DataRootVolumeCapabilities caps = Inspector(new FakeVolumeCapabilityNativeApi
        {
            FileSystemName = "NTFS"
        }).Inspect(@"C:\Folder");

        Assert.IsTrue(caps.SupportsFileIdentity);
        Assert.AreEqual("NTFS", caps.FileSystemName);
    }

    [TestMethod]
    [ProductionSymbolEvidence("WindowsDataRootVolumeCapabilityInspector.Inspect")]
    public void CRUU22_002_Non_capability_error_fails_closed_rather_than_blaming_the_drive()
    {
        // ERROR_ACCESS_DENIED says nothing about the filesystem. Reporting it as "unsupported"
        // would tell the user to go buy a different drive over a permissions problem.
        var ex = Assert.Throws<DataRootVolumeCapabilityInspectionException>(
            () => Inspector(new FakeVolumeCapabilityNativeApi
            {
                FileIdError = ErrorAccessDenied,
                FileSystemName = "NTFS"
            }).Inspect(@"C:\Folder"));

        Assert.AreEqual(ErrorAccessDenied, ex.Win32ErrorCode);
    }

    [TestMethod]
    [ProductionSymbolEvidence("WindowsDataRootVolumeCapabilityInspector.Inspect")]
    public void CRUU22_002_Unopenable_directory_is_an_inspection_failure()
    {
        Assert.Throws<DataRootVolumeCapabilityInspectionException>(
            () => Inspector(new FakeVolumeCapabilityNativeApi { HandleInvalid = true }).Inspect(@"C:\Folder"));
    }

    [TestMethod]
    [ProductionSymbolEvidence("WindowsDataRootVolumeCapabilityInspector.Inspect")]
    public void CRUU22_002_Unsupported_volume_is_reported_even_without_a_filesystem_name()
    {
        DataRootVolumeCapabilities caps = Inspector(new FakeVolumeCapabilityNativeApi
        {
            FileIdError = ErrorInvalidParameter,
            FileSystemNameAvailable = false
        }).Inspect(@"E:\Folder");

        Assert.IsFalse(caps.SupportsFileIdentity);
        Assert.IsNull(caps.FileSystemName);

        // The message must still read sensibly when the platform would not name the
        // filesystem: the core contract falls back to a generic subject, and the
        // platform-supplied remedy is what makes it actionable. (The remedy lives with the
        // caller because which filesystems are affected is a platform fact, not a core one;
        // DataRootTopologyValidator supplies the Windows wording.)
        var withoutName = new UnsupportedDataRootFilesystemException(
            @"E:\Folder",
            null,
            "file identifiers (128-bit file IDs)",
            "Choose a folder on an NTFS or ReFS drive instead.");

        StringAssert.Contains(withoutName.Message, "The filesystem of that drive");
        StringAssert.Contains(withoutName.Message, @"E:\Folder");
        StringAssert.Contains(withoutName.Message, "NTFS or ReFS");

        // Without a remedy the message is still a complete sentence, not a dangling fragment.
        var bare = new UnsupportedDataRootFilesystemException(
            @"E:\Folder",
            "exFAT",
            "file identifiers (128-bit file IDs)");

        StringAssert.Contains(bare.Message, "exFAT does not support");
        StringAssert.EndsWith(bare.Message, "stops unexpectedly.");
    }

    [TestMethod]
    [ProductionSymbolEvidence("DataRootTopologyValidator.AssertVolumeSupportsDataRoot")]
    public void CRUU22_002_Folder_selection_refuses_unsupported_volume_with_an_actionable_message()
    {
        using var current = new TestDirectory();
        using var bootstrap = new TestDirectory();

        string target = Path.Combine(current.Root, "..", "Cruu22UnsupportedTarget");
        target = Path.GetFullPath(target);

        var volumeInspector = new FakeDataRootVolumeCapabilityInspector();
        volumeInspector.MarkWithoutFileIdentity(Path.GetDirectoryName(target)!, "exFAT");

        var ex = Assert.Throws<UnsupportedDataRootFilesystemException>(() =>
            DataRootTopologyValidator.ValidateTransition(
                current.Root,
                target,
                bootstrap.Root,
                caseInspector: new FakeDirectoryCaseSensitivityInspector(),
                volumeInspector: volumeInspector));

        Assert.AreEqual("exFAT", ex.FileSystemName);
        StringAssert.Contains(ex.Message, "exFAT");
        StringAssert.Contains(ex.Message, "NTFS or ReFS");

        // The old failure mode: a native diagnostic reaching the user.
        Assert.IsFalse(
            ex.Message.Contains("Win32", StringComparison.OrdinalIgnoreCase),
            $"The refusal must explain itself, not leak a native error: {ex.Message}");
        Assert.IsFalse(
            ex.Message.Contains("staging", StringComparison.OrdinalIgnoreCase),
            $"The refusal must not mention internal staging artifacts: {ex.Message}");
    }

    [TestMethod]
    [ProductionSymbolEvidence("DataRootTopologyValidator.AssertVolumeSupportsDataRoot")]
    public void CRUU22_002_Startup_refuses_configured_folder_on_unsupported_volume()
    {
        using var configured = new TestDirectory();
        using var bootstrap = new TestDirectory();

        var volumeInspector = new FakeDataRootVolumeCapabilityInspector();
        volumeInspector.MarkWithoutFileIdentity(configured.Root, "FAT32");

        var policy = new ManagedDataRootPolicy(
            caseInspector: new FakeDirectoryCaseSensitivityInspector(),
            volumeInspector: volumeInspector);

        var ex = Assert.Throws<UnsupportedDataRootFilesystemException>(() =>
            policy.ValidateConfiguredRootForStartup(configured.Root, bootstrap.Root));

        Assert.AreEqual("FAT32", ex.FileSystemName);
        StringAssert.Contains(ex.Message, "NTFS or ReFS");
    }

    [TestMethod]
    [ProductionSymbolEvidence("DataRootTopologyValidator.AssertVolumeSupportsDataRoot")]
    public void CRUU22_002_Supported_volume_is_accepted_unchanged()
    {
        using var current = new TestDirectory();
        using var bootstrap = new TestDirectory();

        string target = Path.GetFullPath(Path.Combine(current.Root, "..", "Cruu22SupportedTarget"));

        DataRootRelationship relationship = DataRootTopologyValidator.ValidateTransition(
            current.Root,
            target,
            bootstrap.Root,
            caseInspector: new FakeDirectoryCaseSensitivityInspector(),
            volumeInspector: new FakeDataRootVolumeCapabilityInspector());

        Assert.IsFalse(relationship.SamePhysicalRoot);
    }

    [TestMethod]
    [ProductionSymbolEvidence("DataRootTopologyValidator.AssertVolumeSupportsDataRoot")]
    public void CRUU22_002_Inspection_failure_fails_closed_without_claiming_unsupported()
    {
        using var current = new TestDirectory();
        using var bootstrap = new TestDirectory();

        string target = Path.GetFullPath(Path.Combine(current.Root, "..", "Cruu22FailingTarget"));

        var volumeInspector = new FakeDataRootVolumeCapabilityInspector();
        volumeInspector.MarkInspectionFailure(Path.GetDirectoryName(target)!);

        Assert.Throws<DataRootVolumeCapabilityInspectionException>(() =>
            DataRootTopologyValidator.ValidateTransition(
                current.Root,
                target,
                bootstrap.Root,
                caseInspector: new FakeDirectoryCaseSensitivityInspector(),
                volumeInspector: volumeInspector));
    }

    [TestMethod]
    [ProductionSymbolEvidence("WindowsDataRootVolumeCapabilityInspector.Inspect")]
    public void CRUU22_002_Every_mounted_volume_is_classified_without_an_inspection_failure()
    {
        // Real native path, no fakes. On a machine with an exFAT/FAT volume mounted this is
        // what proves the classification is right rather than merely consistent; on an
        // all-NTFS machine, including CI, it asserts every volume is identity-capable.
        var inspector = new WindowsDataRootVolumeCapabilityInspector();
        var failures = new List<string>();

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            string root;
            string format;
            try
            {
                if (!drive.IsReady || drive.DriveType == DriveType.CDRom) { continue; }
                root = drive.RootDirectory.FullName;
                if (!Directory.Exists(root)) { continue; }
                format = drive.DriveFormat;
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            try
            {
                DataRootVolumeCapabilities caps = inspector.Inspect(root);

                bool expectedIdentity =
                    format.Equals("NTFS", StringComparison.OrdinalIgnoreCase) ||
                    format.Equals("ReFS", StringComparison.OrdinalIgnoreCase);

                if (caps.SupportsFileIdentity != expectedIdentity)
                {
                    failures.Add(
                        $"{root} ({format}): SupportsFileIdentity={caps.SupportsFileIdentity}, expected {expectedIdentity}");
                }
            }
            catch (DataRootVolumeCapabilityInspectionException ex)
            {
                failures.Add($"{root} ({format}): inspection failed, Win32 {ex.Win32ErrorCode}");
            }
        }

        Assert.AreEqual(
            0,
            failures.Count,
            $"Volume capability classification is wrong for: {string.Join("; ", failures)}");
    }
}
