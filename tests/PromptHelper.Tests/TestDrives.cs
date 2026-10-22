using System;
using System.IO;

namespace PromptHelper.Tests;

/// <summary>
/// Drive letters chosen from what this machine actually has mounted.
///
/// Tests that need "a path on a drive that is not there" used to hardcode a letter such as
/// Z:\ or E:\. That silently couples the test to the developer's drive layout: a mapped
/// network drive at Z:, or an exFAT stick at E:, changes what the code under test does and
/// the test fails for a reason that has nothing to do with the product. One such coupling
/// masked a real defect until the volume was inspected directly.
/// </summary>
internal static class TestDrives
{
    /// <summary>
    /// A drive root that is not mounted on this machine, scanned from Z: downwards so it
    /// stays clear of the letters Windows hands out first. Throws when every letter is in
    /// use, because silently falling back to a mounted drive would reintroduce exactly the
    /// coupling this helper exists to remove.
    /// </summary>
    public static string UnusedRoot()
    {
        for (char letter = 'Z'; letter >= 'D'; letter--)
        {
            string root = $"{letter}:\\";

            if (Directory.Exists(root))
            {
                continue;
            }

            // Directory.Exists is false for an existing but unreadable drive too, so confirm
            // the letter is genuinely unassigned rather than merely inaccessible.
            try
            {
                var drive = new DriveInfo($"{letter}:");
                if (drive.DriveType != DriveType.NoRootDirectory)
                {
                    continue;
                }
            }
            catch (ArgumentException)
            {
                // Not a valid drive letter on this machine; treat it as unused.
            }

            return root;
        }

        throw new InvalidOperationException(
            "Every drive letter from D: to Z: is mounted on this machine, so no unused drive " +
            "root is available for tests that require an absent volume.");
    }

    /// <summary>
    /// A path under <see cref="UnusedRoot"/>, for example "Q:\NonExistentDrive\MissingFolder".
    /// </summary>
    public static string UnusedPath(params string[] segments)
    {
        string path = UnusedRoot();
        foreach (string segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }
}
