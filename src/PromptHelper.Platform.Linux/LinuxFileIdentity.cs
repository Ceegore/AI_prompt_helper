using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

/// <summary>
/// Stable Linux inode identity reported by statx: filesystem device major/minor plus inode.
/// </summary>
internal readonly record struct LinuxFileIdentity(
    uint DeviceMajor,
    uint DeviceMinor,
    ulong Inode,
    long BirthTimeSeconds,
    uint BirthTimeNanoseconds)
{
    private const int AtEmptyPath = 0x1000;
    private const uint StatxBasicStats = 0x000007ff;
    private const uint StatxBirthTime = 0x00000800;
    private const ushort FileTypeMask = 0xf000;
    private const ushort RegularFile = 0x8000;

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct StatxBuffer
    {
        [FieldOffset(0)]
        public uint Mask;

        [FieldOffset(28)]
        public ushort Mode;

        [FieldOffset(32)]
        public ulong Inode;

        // struct statx_timestamp stx_btime starts at byte 80.
        [FieldOffset(80)]
        public long BirthTimeSeconds;

        [FieldOffset(88)]
        public uint BirthTimeNanoseconds;

        [FieldOffset(136)]
        public uint DeviceMajor;

        [FieldOffset(140)]
        public uint DeviceMinor;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int statx(
        int directoryFd,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mask,
        out StatxBuffer buffer);

    public FileObjectIdentity ToObjectIdentity() =>
        new(
            "linux-statx-v1",
            $"{DeviceMajor:x8}:{DeviceMinor:x8}:{Inode:x16}:{BirthTimeSeconds:x16}:{BirthTimeNanoseconds:x8}");

    public static implicit operator FileObjectIdentity(LinuxFileIdentity identity) =>
        identity.ToObjectIdentity();

    public static LinuxFileIdentity FromHandle(SafeFileHandle handle)
    {
        LinuxNativeFileSystem.EnsureLinux();
        ArgumentNullException.ThrowIfNull(handle);

        StatxBuffer info = ReadHandleStatx(handle);

        AssertBirthTimeAvailable(info);

        return new LinuxFileIdentity(
            info.DeviceMajor,
            info.DeviceMinor,
            info.Inode,
            info.BirthTimeSeconds,
            info.BirthTimeNanoseconds);
    }

    public static void AssertRegularFile(SafeFileHandle handle, string path)
    {
        StatxBuffer info = ReadHandleStatx(handle);
        if ((info.Mode & FileTypeMask) != RegularFile)
        {
            throw new InvalidDataException(
                $"Expected a regular file but found another filesystem object at '{path}'.");
        }
    }

    private static StatxBuffer ReadHandleStatx(SafeFileHandle handle)
    {
        LinuxNativeFileSystem.EnsureLinux();
        ArgumentNullException.ThrowIfNull(handle);

        int fd = handle.DangerousGetHandle().ToInt32();
        if (statx(
                fd,
                string.Empty,
                AtEmptyPath,
                StatxBasicStats | StatxBirthTime,
                out StatxBuffer info) != 0)
        {
            int error = Marshal.GetLastPInvokeError();
            throw new IOException(
                $"Unable to inspect an open Linux file handle (errno {error}).",
                new Win32Exception(error));
        }

        return info;
    }

    private static void AssertBirthTimeAvailable(StatxBuffer info)
    {
        if ((info.Mask & StatxBirthTime) == 0)
        {
            throw new PlatformNotSupportedException(
                "The current Linux filesystem does not expose statx creation time (STATX_BTIME). " +
                "Prompt Helper cannot use inode number alone as durable exact-object provenance because Linux may reuse inodes.");
        }
    }
}
