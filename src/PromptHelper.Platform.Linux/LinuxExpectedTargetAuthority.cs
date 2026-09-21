using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

internal sealed class LinuxExpectedTargetAuthority : IDisposable
{
    private readonly SafeFileHandle _handle;

    private LinuxExpectedTargetAuthority(
        string openedPath,
        string finalPhysicalPath,
        SafeFileHandle handle,
        LinuxFileIdentity identity)
    {
        OpenedPath = openedPath;
        FinalPhysicalPath = finalPhysicalPath;
        _handle = handle;
        Identity = identity;
    }

    public string OpenedPath { get; }

    public string FinalPhysicalPath { get; }

    public LinuxFileIdentity Identity { get; }

    public static LinuxExpectedTargetAuthority? Open(
        string path,
        string physicalRoot)
    {
        LinuxNativeFileSystem.EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);

        string fullPath = Path.GetFullPath(path);
        SafeFileHandle? handle =
            LinuxNativeFileSystem.OpenReadOnlyNoFollowOrNull(fullPath);
        if (handle is null)
        {
            return null;
        }

        try
        {
            LinuxFileIdentity.AssertRegularFile(handle, fullPath);
            LinuxFileIdentity identity = LinuxFileIdentity.FromHandle(handle);

            string resolvedRoot =
                new LinuxPhysicalPathResolver()
                    .ResolveWithNearestExistingAncestor(physicalRoot);

            string procHandlePath =
                $"/proc/self/fd/{handle.DangerousGetHandle().ToInt32()}";
            FileSystemInfo? resolved =
                File.ResolveLinkTarget(procHandlePath, returnFinalTarget: true);

            if (resolved is null)
            {
                throw new IOException(
                    $"Unable to resolve the physical path for open Linux file '{fullPath}'.");
            }

            string finalPath = Path.GetFullPath(resolved.FullName);
            if (!PathIdentity.IsStrictDescendant(finalPath, resolvedRoot))
            {
                throw new InvalidDataException(
                    $"Refusing file outside the managed data root. Root: '{resolvedRoot}', file: '{finalPath}'.");
            }

            return new LinuxExpectedTargetAuthority(
                fullPath,
                finalPath,
                handle,
                identity);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public byte[] ReadAllBytes()
    {
        long length = RandomAccess.GetLength(_handle);
        if (length > int.MaxValue)
        {
            throw new IOException(
                $"File '{OpenedPath}' is too large to verify atomically.");
        }

        byte[] bytes = new byte[(int)length];
        int read = 0;
        while (read < bytes.Length)
        {
            int count = RandomAccess.Read(
                _handle,
                bytes.AsSpan(read),
                read);

            if (count <= 0)
            {
                throw new IOException(
                    $"Unexpected end of data reading '{OpenedPath}'.");
            }

            read += count;
        }

        return bytes;
    }

    public void AssertContentMatches(string expectedSha256Hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256Hex);

        string actual =
            Convert.ToHexStringLower(SHA256.HashData(ReadAllBytes()));

        if (!string.Equals(
                actual,
                expectedSha256Hex,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new StaleExpectedFileException(
                $"'{OpenedPath}' changed outside the current state. Reload before editing.");
        }
    }

    public void AssertIdentityMatches(FileObjectIdentity expectedIdentity)
    {
        if (Identity.ToObjectIdentity() != expectedIdentity)
        {
            throw new StaleExpectedFileException(
                $"'{OpenedPath}' was replaced by a different filesystem object. Reload before editing.");
        }
    }

    public void Dispose() => _handle.Dispose();
}
