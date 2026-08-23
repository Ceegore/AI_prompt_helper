using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PromptHelper.Services;

internal sealed record MigrationTargetInventory(
    IReadOnlyList<string> FinalArtifacts,
    IReadOnlyList<string> PayloadTemps,
    IReadOnlyList<string> DeclaredControls,
    IReadOnlyList<string> PersistentBootstrapControls,
    IReadOnlyList<string> AttemptCreatedDirectories,
    IReadOnlyList<string> PreExistingDirectories,
    IReadOnlyList<string> UnknownEntries)
{
    public bool HasUnknownEntries => UnknownEntries.Count > 0;
    public bool HasEphemeralControls => PayloadTemps.Count > 0 || DeclaredControls.Count > 0;
}

/// <summary>
/// CRUU14-006: every path this inspector looks at is classified through
/// <see cref="StrictPathAuthority.Probe"/> before being trusted, instead of
/// <c>Directory.Exists</c>/<c>Directory.GetFiles</c>/<c>Directory.GetDirectories</c> alone.
/// That matters for two reasons this inventory is depended on for safety decisions:
/// <list type="bullet">
/// <item><c>Directory.Exists</c> silently swallows access-denied and returns false — which
/// would make an inaccessible (not actually absent) target look "clean" to every caller that
/// treats an empty inventory as "nothing dangerous is present." <see cref="StrictPathAuthority"/>
/// instead lets <see cref="UnauthorizedAccessException"/>/<see cref="IOException"/> propagate,
/// so an unreadable target fails the calling operation instead of looking empty.</item>
/// <item>Neither <c>Directory.Exists</c> nor a bare directory listing distinguishes a reparse
/// point (symlink/junction) from a genuine file or directory. Every entry this inspector
/// classifies — the root, "prompts"/"recovery", every declared control directory, and every
/// individual file — is explicitly checked and rejected if it is a reparse point, so a
/// substituted redirect can never be silently classified as ordinary managed content.</item>
/// </list>
/// CRUU15-008 closes the two gaps that remained:
/// <list type="bullet">
/// <item>Enumeration is bound to the directory <i>object</i>. Entries come from
/// <see cref="WindowsDirectoryEnumeration.ListStrict"/>, which lists through a handle already
/// proven to be a genuine non-reparse directory, instead of re-resolving the pathname on every
/// <c>Directory.GetFiles</c>/<c>GetDirectories</c> call.</item>
/// <item>An entry whose type changes between that listing and its own probe is rejected rather
/// than reclassified: the attributes the directory object reported are cross-checked against
/// the probe, so a file swapped for a directory, or either swapped for a reparse point, fails
/// closed.</item>
/// </list>
/// The inventory is deliberately <b>advisory classification</b> and not destructive authority.
/// Every operation that destroys something now carries its own creation-bound ownership proof
/// (CRUU15-006/CRUU15-007), so a same-type content swap that this pass could not detect cannot
/// authorize a deletion downstream either.
/// </summary>
internal static class MigrationTargetInventoryInspector
{
    public static MigrationTargetInventory Inspect(
        string targetPhysicalRoot,
        MigrationAttemptManifest manifest,
        bool isBootstrapRoot = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPhysicalRoot);
        ArgumentNullException.ThrowIfNull(manifest);

        string root = PathIdentity.NormalizeForComparison(targetPhysicalRoot);
        var strictPaths = new StrictPathAuthority();

        var declaredFinals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var declaredTemps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var declaredControls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var declaredDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
        {
            string finalPath = PathIdentity.NormalizeForComparison(
                Path.Combine(root, artifact.RelativePath));
            declaredFinals.Add(finalPath);

            string tempPath = PathIdentity.NormalizeForComparison(
                Path.Combine(root, artifact.TempRelativePath));
            declaredTemps.Add(tempPath);
        }

        foreach (MigrationControlArtifact control in manifest.ControlArtifacts)
        {
            string controlPath = PathIdentity.NormalizeForComparison(
                Path.Combine(root, control.RelativePath));
            if (control.Kind == MigrationControlArtifactKind.CapabilityProbeDirectory)
            {
                declaredDirs.Add(controlPath);
            }
            else
            {
                declaredControls.Add(controlPath);
            }
        }

        string markerPath = PathIdentity.NormalizeForComparison(
            Path.Combine(root, ".prompthelper-migration.json"));
        declaredControls.Add(markerPath);

        string appLockPath = PathIdentity.NormalizeForComparison(
            Path.Combine(root, ".app.lock"));
        declaredControls.Add(appLockPath);

        var attemptCreatedDirs = new List<string>();
        var preExistingDirs = new List<string>();

        string promptsDir = PathIdentity.NormalizeForComparison(Path.Combine(root, "prompts"));
        string recoveryDir = PathIdentity.NormalizeForComparison(Path.Combine(root, "recovery"));

        if (manifest.TargetBaseline != null)
        {
            if (manifest.TargetBaseline.PromptsDirectoryExistedBefore)
            {
                preExistingDirs.Add(promptsDir);
            }
            else
            {
                attemptCreatedDirs.Add(promptsDir);
            }

            if (manifest.TargetBaseline.RecoveryDirectoryExistedBefore)
            {
                preExistingDirs.Add(recoveryDir);
            }
            else
            {
                attemptCreatedDirs.Add(recoveryDir);
            }
        }
        else
        {
            attemptCreatedDirs.Add(promptsDir);
            attemptCreatedDirs.Add(recoveryDir);
        }

        var foundFinals = new List<string>();
        var foundTemps = new List<string>();
        var foundControls = new List<string>();
        var foundPersistentBootstrapControls = new List<string>();
        var unknownEntries = new List<string>();

        bool DirectoryPresentStrict(string dir)
        {
            StrictPathProbe probe = strictPaths.Probe(dir);
            if (probe.Kind == StrictPathKind.Missing)
            {
                return false;
            }

            if (probe.Kind != StrictPathKind.Directory)
            {
                throw new InvalidDataException($"Expected a directory but found a file: '{dir}'.");
            }

            AssertNotReparse(probe, dir);
            return true;
        }

        StrictPathProbe rootProbe = strictPaths.Probe(root);
        if (rootProbe.Kind == StrictPathKind.Missing)
        {
            return new MigrationTargetInventory(
                foundFinals,
                foundTemps,
                foundControls,
                foundPersistentBootstrapControls,
                attemptCreatedDirs.Where(DirectoryPresentStrict).ToList(),
                preExistingDirs.Where(DirectoryPresentStrict).ToList(),
                unknownEntries);
        }

        if (rootProbe.Kind != StrictPathKind.Directory)
        {
            throw new InvalidDataException($"Migration target root is not a directory: '{root}'.");
        }

        AssertNotReparse(rootProbe, root);

        void ScanDirectory(string dir, bool isRoot)
        {
            if (!isRoot && !DirectoryPresentStrict(dir))
            {
                return;
            }

            IReadOnlyList<DirectoryEntry> entries =
                WindowsDirectoryEnumeration.ListStrict(dir) ?? [];

            foreach (DirectoryEntry entry in entries)
            {
                if (entry.IsDirectory)
                {
                    continue;
                }

                string file = Path.Combine(dir, entry.Name);

                StrictPathProbe fileProbe = strictPaths.Probe(file);
                if (fileProbe.Kind == StrictPathKind.Missing)
                {
                    // Deleted between enumeration and probing; nothing to classify.
                    continue;
                }

                if (fileProbe.Kind != StrictPathKind.File)
                {
                    throw new InvalidDataException($"Expected a file but found a directory: '{file}'.");
                }

                AssertNotReparse(fileProbe, file);
                AssertProbeAgreesWithEnumeration(entry, fileProbe, file);

                string normFile = PathIdentity.NormalizeForComparison(file);
                string fileName = Path.GetFileName(file);

                if (isRoot && ManagedControlPathPolicy.IsPersistentManagedControl(fileName))
                {
                    foundPersistentBootstrapControls.Add(normFile);
                }
                else if (isRoot && ManagedControlPathPolicy.IsReservedEphemeralRootControl(fileName))
                {
                    foundControls.Add(normFile);
                }
                else if (isRoot && isBootstrapRoot && ManagedControlPathPolicy.IsPersistentBootstrapControl(fileName))
                {
                    foundPersistentBootstrapControls.Add(normFile);
                }
                else if (declaredFinals.Contains(normFile))
                {
                    foundFinals.Add(normFile);
                }
                else if (declaredTemps.Contains(normFile))
                {
                    foundTemps.Add(normFile);
                }
                else if (declaredControls.Contains(normFile))
                {
                    foundControls.Add(normFile);
                }
                else
                {
                    unknownEntries.Add(normFile);
                }
            }

            foreach (DirectoryEntry entry in entries)
            {
                if (!entry.IsDirectory)
                {
                    continue;
                }

                string subDir = Path.Combine(dir, entry.Name);

                StrictPathProbe subDirProbe = strictPaths.Probe(subDir);
                if (subDirProbe.Kind == StrictPathKind.Missing)
                {
                    // Deleted between enumeration and probing; nothing to classify.
                    continue;
                }

                if (subDirProbe.Kind != StrictPathKind.Directory)
                {
                    throw new InvalidDataException($"Expected a directory but found a file: '{subDir}'.");
                }

                AssertProbeAgreesWithEnumeration(entry, subDirProbe, subDir);

                string normSubDir = PathIdentity.NormalizeForComparison(subDir);

                if (isRoot && (PathIdentity.Equals(normSubDir, promptsDir) || PathIdentity.Equals(normSubDir, recoveryDir)))
                {
                    AssertNotReparse(subDirProbe, subDir);
                    ScanDirectory(normSubDir, isRoot: false);
                }
                else if (declaredDirs.Contains(normSubDir))
                {
                    AssertNotReparse(subDirProbe, subDir);
                    foundControls.Add(normSubDir);
                    ScanDirectory(normSubDir, isRoot: false);
                }
                else
                {
                    // An unrecognized reparse point is still reported as an unknown entry
                    // (fail-closed via HasUnknownEntries) rather than silently descended into.
                    unknownEntries.Add(normSubDir);
                }
            }
        }

        ScanDirectory(root, isRoot: true);

        return new MigrationTargetInventory(
            foundFinals,
            foundTemps,
            foundControls,
            foundPersistentBootstrapControls,
            attemptCreatedDirs.Where(DirectoryPresentStrict).ToList(),
            preExistingDirs.Where(DirectoryPresentStrict).ToList(),
            unknownEntries);
    }

    /// <summary>
    /// Rejects an entry whose kind changed between the directory object's own listing and the
    /// probe of that entry (CRUU15-008). Reclassifying it would let a swapped object inherit
    /// the classification the original earned.
    /// </summary>
    private static void AssertProbeAgreesWithEnumeration(
        DirectoryEntry entry,
        StrictPathProbe probe,
        string path)
    {
        bool probeIsDirectory = probe.Kind == StrictPathKind.Directory;
        bool probeIsReparse = probe.Attributes.HasValue &&
                              (probe.Attributes.Value & FileAttributes.ReparsePoint) != 0;

        if (probeIsDirectory != entry.IsDirectory || probeIsReparse != entry.IsReparsePoint)
        {
            throw new InvalidDataException(
                $"'{path}' changed between directory enumeration and inspection. Inspection aborted to protect data.");
        }
    }

    private static void AssertNotReparse(StrictPathProbe probe, string path)
    {
        if (probe.Attributes.HasValue && (probe.Attributes.Value & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"Refusing to classify reparse-point path as ordinary managed content: '{path}'.");
        }
    }
}
