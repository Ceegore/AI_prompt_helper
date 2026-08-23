using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>
/// Atomically creates a directory and its retained handle with delete-on-close already armed.
/// NtCreateFile supplies the one operation Win32 CreateDirectoryW cannot: there is no
/// create-then-open identity gap. Some filesystems may retain an empty directory after process
/// termination despite the on-close request; migration recovery therefore preserves/adopts
/// that unclaimed empty structure instead of inferring destructive ownership.
/// </summary>
internal sealed class WindowsCrashAtomicDirectoryBootstrap : IDisposable
{
    private const uint FILE_LIST_DIRECTORY = 0x00000001;
    private const uint DELETE = 0x00010000;
    private const uint SYNCHRONIZE = 0x00100000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_CREATE = 2;
    private const uint FILE_DIRECTORY_FILE = 0x00000001;
    private const uint FILE_WRITE_THROUGH = 0x00000002;
    private const uint FILE_SYNCHRONOUS_IO_NONALERT = 0x00000020;
    private const uint FILE_DELETE_ON_CLOSE = 0x00001000;
    private const uint FILE_OPEN_REPARSE_POINT = 0x00200000;
    private const uint OBJ_CASE_INSENSITIVE = 0x00000040;
    private const int STATUS_OBJECT_NAME_COLLISION = unchecked((int)0xC0000035);
    private const int FileDispositionInfoClass = 4;
    private const int FileDispositionInfoExClass = 21;

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OBJECT_ATTRIBUTES
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_STATUS_BLOCK
    {
        public IntPtr Status;
        public IntPtr Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_DISPOSITION_INFO
    {
        [MarshalAs(UnmanagedType.U1)]
        public bool DeleteFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_DISPOSITION_INFO_EX
    {
        public uint Flags;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(
        out IntPtr fileHandle,
        uint desiredAccess,
        ref OBJECT_ATTRIBUTES objectAttributes,
        out IO_STATUS_BLOCK ioStatusBlock,
        IntPtr allocationSize,
        uint fileAttributes,
        uint shareAccess,
        uint createDisposition,
        uint createOptions,
        IntPtr eaBuffer,
        uint eaLength);

    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetFileInformationByHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandleDisposition(
        SafeFileHandle hFile,
        int fileInformationClass,
        ref FILE_DISPOSITION_INFO fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetFileInformationByHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandleDispositionEx(
        SafeFileHandle hFile,
        int fileInformationClass,
        ref FILE_DISPOSITION_INFO_EX fileInformation,
        uint bufferSize);

    private readonly SafeFileHandle _handle;
    private bool _deleteOnCloseArmed = true;
    private bool _disposed;

    private WindowsCrashAtomicDirectoryBootstrap(
        string path,
        string finalPhysicalPath,
        SafeFileHandle handle)
    {
        Path = path;
        FinalPhysicalPath = finalPhysicalPath;
        _handle = handle;
    }

    public string Path { get; }
    public string FinalPhysicalPath { get; }
    public WindowsFileIdentity Identity => WindowsFileIdentity.FromHandle(_handle);

    public static WindowsCrashAtomicDirectoryBootstrap? CreateNewOrNull(
        string path,
        string expectedParentPhysicalPath)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        string parent = System.IO.Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException($"Directory path has no parent: '{path}'.", nameof(path));
        string leaf = System.IO.Path.GetFileName(fullPath);
        if (leaf.Length == 0)
        {
            throw new ArgumentException($"Directory path has no leaf name: '{path}'.", nameof(path));
        }

        using WindowsRetirableDirectory? parentDirectory =
            WindowsRetirableDirectory.OpenExistingOrNull(
                parent,
                expectedParentPhysicalPath,
                requireDeleteAccess: false,
                allowRootItself: true);
        if (parentDirectory is null)
        {
            throw new DirectoryNotFoundException($"Directory parent does not exist: '{parent}'.");
        }

        IntPtr nameBuffer = Marshal.StringToHGlobalUni(leaf);
        IntPtr unicodePointer = IntPtr.Zero;
        try
        {
            var unicode = new UNICODE_STRING
            {
                Length = checked((ushort)(leaf.Length * sizeof(char))),
                MaximumLength = checked((ushort)((leaf.Length + 1) * sizeof(char))),
                Buffer = nameBuffer
            };
            unicodePointer = Marshal.AllocHGlobal(Marshal.SizeOf<UNICODE_STRING>());
            Marshal.StructureToPtr(unicode, unicodePointer, fDeleteOld: false);

            var attributes = new OBJECT_ATTRIBUTES
            {
                Length = Marshal.SizeOf<OBJECT_ATTRIBUTES>(),
                RootDirectory = parentDirectory.DangerousHandle,
                ObjectName = unicodePointer,
                Attributes = OBJ_CASE_INSENSITIVE
            };

            int status = NtCreateFile(
                out IntPtr rawHandle,
                FILE_LIST_DIRECTORY | DELETE | SYNCHRONIZE,
                ref attributes,
                out _,
                IntPtr.Zero,
                FILE_ATTRIBUTE_NORMAL,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                FILE_CREATE,
                FILE_DIRECTORY_FILE |
                FILE_WRITE_THROUGH |
                FILE_SYNCHRONOUS_IO_NONALERT |
                FILE_DELETE_ON_CLOSE |
                FILE_OPEN_REPARSE_POINT,
                IntPtr.Zero,
                0);

            if (status == STATUS_OBJECT_NAME_COLLISION)
            {
                return null;
            }

            if (status < 0)
            {
                throw new IOException(
                    $"Failed to atomically create directory '{fullPath}'.",
                    new Win32Exception((int)RtlNtStatusToDosError(status)));
            }

            var handle = new SafeFileHandle(rawHandle, ownsHandle: true);
            try
            {
                string finalPath = WindowsFinalPathHelper.GetNormalizedDosPath(handle);
                string expected = PathIdentity.NormalizeForComparison(fullPath);
                if (!PathIdentity.Equals(finalPath, expected) ||
                    !PathIdentity.IsStrictDescendant(
                        finalPath,
                        PathIdentity.NormalizeForComparison(expectedParentPhysicalPath)))
                {
                    throw new InvalidDataException(
                        $"New directory resolved outside its exact claimed path. Expected='{expected}', Actual='{finalPath}'.");
                }

                return new WindowsCrashAtomicDirectoryBootstrap(fullPath, finalPath, handle);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
        finally
        {
            if (unicodePointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(unicodePointer);
            }
            Marshal.FreeHGlobal(nameBuffer);
        }
    }

    public void PersistAfterDurableClaim()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var disposition = new FILE_DISPOSITION_INFO_EX { Flags = 0x00000008 };
        if (!SetFileInformationByHandleDispositionEx(
                _handle,
                FileDispositionInfoExClass,
                ref disposition,
                (uint)Marshal.SizeOf<FILE_DISPOSITION_INFO_EX>()))
        {
            throw new IOException(
                $"Failed to persist durably-claimed directory '{Path}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
        _deleteOnCloseArmed = false;
    }

    public void DeleteExact()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_deleteOnCloseArmed)
        {
            SetDeleteDisposition(delete: true);
            _deleteOnCloseArmed = true;
        }
    }

    private void SetDeleteDisposition(bool delete)
    {
        var disposition = new FILE_DISPOSITION_INFO { DeleteFile = delete };
        if (!SetFileInformationByHandleDisposition(
                _handle,
                FileDispositionInfoClass,
                ref disposition,
                (uint)Marshal.SizeOf<FILE_DISPOSITION_INFO>()))
        {
            throw new IOException(
                $"Failed to change crash-atomic directory disposition for '{Path}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _handle.Dispose();
    }
}
