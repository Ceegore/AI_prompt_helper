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
    ulong Inode)
{
    private const int AtEmptyPath = 0x1000;
    private const uint StatxInode = 0x00000100;

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct StatxBuffer
    {
        [FieldOffset(28)]
        public ushort Mode;

        [FieldOffset(32)]
        public ulong Inode;

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
            $"{DeviceMajor:x8}:{DeviceMinor:x8}:{Inode:x16}");

    public static implicit operator FileObjectIdentity(LinuxFileIdentity identity) =>
        identity.ToObjectIdentity();

    public static LinuxFileIdentity FromHandle(SafeFileHandle handle)
    {
        LinuxNativeFileSystem.EnsureLinux();
        ArgumentNullException.ThrowIfNull(handle);

        int fd = handle.DangerousGetHandle().ToInt32();
        if (statx(fd, string.Empty, AtEmptyPath, StatxInode, out StatxBuffer info) != 0)
        {
            int error = Marshal.GetLastPInvokeError();
            throw new IOException(
                $"Unable to read Linux file identity from an open handle (errno {error}).",
                new Win32Exception(error));
        }

        return new LinuxFileIdentity(
            info.DeviceMajor,
            info.DeviceMinor,
            info.Inode);
    }
}
