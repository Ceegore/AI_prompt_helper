namespace PromptHelper.Services;

internal enum LinuxExactRetirementOutcome
{
    Missing,
    Retired,
    ForeignPreserved,
    ConflictPreserved
}

/// <summary>
/// Best-effort exact-object retirement for Linux regular files.
///
/// Linux unlink is pathname-based. To avoid deleting an object that replaced the one we
/// inspected, retirement first moves the pathname to an unguessable same-directory quarantine,
/// re-opens that new name without following links, and proves the statx identity again. A
/// foreign object is restored to the original name when possible and is never intentionally
/// deleted.
/// </summary>
internal static class LinuxExactFileRetirement
{
    internal static Action<string>? BeforeQuarantineRenameForTests;

    public static LinuxExactRetirementOutcome Retire(
        string physicalRoot,
        string path,
        FileObjectIdentity expectedIdentity,
        string? expectedSha256Hex = null,
        long expectedLength = -1)
    {
        LinuxNativeFileSystem.EnsureLinux();

        string fullRoot =
            new LinuxPhysicalPathResolver()
                .ResolveWithNearestExistingAncestor(physicalRoot);
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException(
                $"Retirement path has no parent: '{fullPath}'.");

        using LinuxExpectedTargetAuthority? authority =
            LinuxExpectedTargetAuthority.Open(fullPath, fullRoot);
        if (authority is null)
        {
            return LinuxExactRetirementOutcome.Missing;
        }

        if (authority.Identity.ToObjectIdentity() != expectedIdentity)
        {
            return LinuxExactRetirementOutcome.ForeignPreserved;
        }

        AssertOptionalContent(
            authority,
            expectedSha256Hex,
            expectedLength,
            fullPath);

        BeforeQuarantineRenameForTests?.Invoke(fullPath);

        string quarantine = Path.Combine(
            directory,
            $".prompthelper-retired-{Guid.NewGuid():N}.tmp");

        if (!LinuxNativeFileSystem.TryRenameNoReplace(
                fullPath,
                quarantine,
                out int renameError))
        {
            if (renameError is
                LinuxNativeFileSystem.ErrorNoEntry or
                LinuxNativeFileSystem.ErrorNotDirectory)
            {
                return LinuxExactRetirementOutcome.Missing;
            }

            throw LinuxNativeFileSystem.NativeIOException(
                $"Unable to quarantine Linux artifact '{fullPath}' for exact retirement.",
                renameError);
        }

        LinuxNativeFileSystem.FlushDirectory(directory);

        LinuxExpectedTargetAuthority? moved = null;
        try
        {
            moved = LinuxExpectedTargetAuthority.Open(quarantine, fullRoot);
            if (moved is null ||
                moved.Identity.ToObjectIdentity() != expectedIdentity)
            {
                return RestoreForeignOrPreserve(
                    quarantine,
                    fullPath,
                    directory);
            }

            AssertOptionalContent(
                moved,
                expectedSha256Hex,
                expectedLength,
                quarantine);
        }
        catch
        {
            if (moved is not null &&
                moved.Identity.ToObjectIdentity() != expectedIdentity)
            {
                _ = RestoreForeignOrPreserve(
                    quarantine,
                    fullPath,
                    directory);
            }

            throw;
        }
        finally
        {
            moved?.Dispose();
        }

        // The quarantine name is freshly random and its object was proven after the rename.
        // A same-user adversary could theoretically race this final pathname unlink, but any
        // substitution would have to target an unguessable name in the tiny interval between
        // the post-rename proof and this call. This is materially stronger than deleting the
        // original public pathname after an earlier proof.
        File.Delete(quarantine);
        LinuxNativeFileSystem.FlushDirectory(directory);
        return LinuxExactRetirementOutcome.Retired;
    }

    private static LinuxExactRetirementOutcome RestoreForeignOrPreserve(
        string quarantine,
        string original,
        string directory)
    {
        if (LinuxNativeFileSystem.TryRenameNoReplace(
                quarantine,
                original,
                out _))
        {
            LinuxNativeFileSystem.FlushDirectory(directory);
            return LinuxExactRetirementOutcome.ForeignPreserved;
        }

        // Preserve both names when the original pathname is occupied.
        return LinuxExactRetirementOutcome.ConflictPreserved;
    }

    private static void AssertOptionalContent(
        LinuxExpectedTargetAuthority authority,
        string? expectedSha256Hex,
        long expectedLength,
        string path)
    {
        if (expectedSha256Hex is null && expectedLength < 0)
        {
            return;
        }

        byte[] bytes = authority.ReadAllBytes();

        if (expectedLength >= 0 && bytes.LongLength != expectedLength)
        {
            throw new StaleExpectedFileException(
                $"Artifact '{path}' length changed before retirement.");
        }

        if (expectedSha256Hex is not null)
        {
            string actual =
                Convert.ToHexStringLower(
                    System.Security.Cryptography.SHA256.HashData(bytes));
            if (!string.Equals(
                    actual,
                    expectedSha256Hex,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new StaleExpectedFileException(
                    $"Artifact '{path}' content changed before retirement.");
            }
        }
    }
}
