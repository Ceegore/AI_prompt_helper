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

        if (!Directory.Exists(root))
        {
            return new MigrationTargetInventory(
                foundFinals,
                foundTemps,
                foundControls,
                foundPersistentBootstrapControls,
                attemptCreatedDirs.Where(Directory.Exists).ToList(),
                preExistingDirs.Where(Directory.Exists).ToList(),
                unknownEntries);
        }

        void ScanDirectory(string dir, bool isRoot)
        {
            if (!Directory.Exists(dir))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(dir))
            {
                string normFile = PathIdentity.NormalizeForComparison(file);
                string fileName = Path.GetFileName(file);

                if (isRoot && ManagedControlPathPolicy.IsReservedEphemeralRootControl(fileName))
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

            foreach (string subDir in Directory.GetDirectories(dir))
            {
                string normSubDir = PathIdentity.NormalizeForComparison(subDir);

                if (isRoot && (PathIdentity.Equals(normSubDir, promptsDir) || PathIdentity.Equals(normSubDir, recoveryDir)))
                {
                    ScanDirectory(normSubDir, isRoot: false);
                }
                else if (declaredDirs.Contains(normSubDir))
                {
                    foundControls.Add(normSubDir);
                    ScanDirectory(normSubDir, isRoot: false);
                }
                else
                {
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
            attemptCreatedDirs.Where(Directory.Exists).ToList(),
            preExistingDirs.Where(Directory.Exists).ToList(),
            unknownEntries);
    }
}
