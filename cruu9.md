# CRUU9 — Final-Authority, Managed-Tree, Crash-Durability & Evidence Audit

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited commit:** `be1da4fa49916a102616f82a6c74f5601ab5d2d6`  
**Previous audit chain:** `cruu1.md` → `cruu2.md` → `cruu3.md` → `cruu4.md` → `cruu5.md` → `cruu6.md` → `cruu7.md` → `cruu8.md` / `cruu8_v2.md`  
**Purpose:** independently test the CRUU8 implementation and provide a deterministic, weak-model-ready repair authority for every remaining issue.

---

# 1. Executive verdict

CRUU8 materially improved the codebase. The current commit contains real implementations for:

```text
migration manifest schema v2
predeclared payload temp paths
write-through payload promotion
write-through manifest promotion
Empty vs OccupiedNonLibrary target classification
unified retry/startup migration recovery
recursive prompts/recovery inventory
handle-bound verified final-artifact deletion
fail-closed case-sensitivity query
full target parent-chain reservation tracking
role-aware existing-target capability checks
shared library compatibility inspection
SettingsDialog postcommit RestartRequired protection
TRX evidence parser
stronger release-asset structure checking
```

The remaining problems now sit **below those abstractions**. The root may be physically safe while a child junction escapes it. A manifest may be structurally valid while its temp path points at unrelated data. A file may be present but `File.Exists` can report `false` because it is unreadable. Target payload and Ready marker may be write-through durable while the authoritative settings pointer still uses an ordinary rename.

Current audit status:

```text
CRUU8 STRUCTURAL IMPLEMENTATION          = SUBSTANTIALLY LANDED
AUDITED COMMIT                           = be1da4fa49916a102616f82a6c74f5601ab5d2d6
CRUU9 SOURCE-LEVEL AUDIT                 = COMPLETE
CRUU9 FINDINGS                           = OPEN
INDEPENDENT WINDOWS/.NET EXECUTION       = NOT AVAILABLE IN THIS AUDIT ENVIRONMENT
GITHUB COMBINED STATUS ENTRIES           = NONE RETURNED BY CONNECTOR
PR-TRIGGERED WORKFLOW EVIDENCE           = NONE RETURNED BY CONNECTOR
ZERO-DEFECT ACCEPTANCE                   = NOT GRANTED
STRICT RELEASE                           = BLOCKED
```

The connector evidence above does **not** prove that push CI did not run. It only means this audit did not receive usable direct CI evidence from the available endpoints.

---

# 2. Platform facts that control the repair

Microsoft documents that `File.Exists(path)` returns `false` rather than throwing when the caller lacks sufficient permission. Therefore `File.Exists == false` is not proof of absence for a migration authority file.

Reference:

```text
https://learn.microsoft.com/dotnet/api/system.io.file.exists
```

Microsoft documents `MOVEFILE_WRITE_THROUGH` as delaying return until the move is completed on disk. This is already used for migrated payload and manifest promotion, but the authoritative settings pointer still uses `File.Move` / `File.Replace` through `AtomicTextWriter`.

Reference:

```text
https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-movefileexw
```

`REPLACEFILE_WRITE_THROUGH` is documented as unsupported, so the settings fix should not attempt to add that flag to `ReplaceFileW`. Use a `MoveFileExW` same-volume replacement with `MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH` after flushing the staging file.

---

# 3. CRUU9 finding register

| ID | Severity | Finding |
|---|---|---|
| CRUU9-001 | CRITICAL/HIGH | Managed child directories (`prompts`, `recovery`) are not physically bound; a pre-existing junction/symlink can redirect migration writes and recovery deletion outside the bound target |
| CRUU9-002 | HIGH | `File.Exists` fail-open semantics are still used for migration markers, finals, temps and verified deletion, collapsing inaccessible authority files into “missing” |
| CRUU9-003 | HIGH | `TempRelativePath` is not constrained to the final file’s directory/name/AttemptId grammar; retry recovery deletes declared temps without hash verification |
| CRUU9-004 | HIGH | Manifest final and temp path uniqueness is checked in separate sets; a temp path can collide with another artifact’s final path |
| CRUU9-005 | MEDIUM-HIGH | Retry recovery validates target identity but not source identity; an interrupted attempt from another source can be auto-cleaned |
| CRUU9-006 | HIGH | Capability-probe directories/files are not durably represented by the migration manifest; crash during probe leaves unknown residue that cannot auto-recover |
| CRUU9-007 | HIGH | Migration-manifest staging temp is not owned by durable authority; crash during first publication or Ready replacement can strand blocking unknown residue |
| CRUU9-008 | HIGH | `settings.json` final pointer update is not write-through, so target/Ready durability is stronger than authoritative settings-pointer durability |
| CRUU9-009 | MEDIUM-HIGH | Settings atomic-writer temp files are not recognized/recoverable bootstrap control files and can block default-root recovery |
| CRUU9-010 | MEDIUM-HIGH | Retry recovery’s post-clean check tests remaining finals only; it does not reject new unknown entries or remaining temps/controls before deleting marker |
| CRUU9-011 | MEDIUM | Recovery converts some failures to `RecoveryResult` but lets temp deletion, directory cleanup, inventory and marker-delete exceptions escape raw |
| CRUU9-012 | MEDIUM | Successful migration to a newly-created target can report false cleanup failures because reservation `Release()` still attempts to delete the committed directory chain |
| CRUU9-013 | MEDIUM | Reservation ownership uses check-then-recursive-create and acquisition cleanup failures are swallowed; an externally-created directory can be misclassified as attempt-owned |
| CRUU9-014 | MEDIUM-HIGH | A stale root `.app.lock` left by a crash before manifest creation makes an otherwise empty target `OccupiedNonLibrary` and blocks retry |
| CRUU9-015 | MEDIUM | Bootstrap identity is threaded inconsistently: target is physical while several comparisons/recovery contexts use lexical bootstrap path; redundant `TargetIsBootstrapRoot` can disagree |
| CRUU9-016 | MEDIUM | Recovery ignores `.prompthelper-migration.json` and `.app.lock` by filename at every recursion depth, so nested foreign control-name files are silently ignored |
| CRUU9-017 | MEDIUM | `ReadyToCommit` is written without an explicit terminal invariant that all temps/ephemeral controls are absent and finals/tree are still stable |
| CRUU9-018 | MEDIUM | `CopySnapshotToTarget` still permits `declaredTempMap = null` and silently generates unmanifested temps; the internal helper uses the escape hatch |
| CRUU9-019 | LOW-MEDIUM | `ManifestWriteCleanupException` masks the original manifest write/promotion failure when staging-temp cleanup also fails |
| CRUU9-020 | MEDIUM | MainWindow still conditions forced shutdown on `DialogResult == true` and shows a MessageBox before requesting shutdown; process-boundary authority is not unconditional |
| CRUU9-021 | MEDIUM verification gap | Several CRUU8 tests do not prove their names: marker preservation, cleanup aggregation, real Windows case sensitivity, exhaustive dispatch, evidence-script execution and temp grammar |
| CRUU9-022 | MEDIUM verification gap | Windows CI does not separately execute mandatory categories and never invokes `VerifyTestEvidence.ps1`; missing sentinels are only warnings in the script |
| CRUU9-023 | MEDIUM release gap | Exact current SVG → committed ICO → published EXE icon identity is still not verified |
| CRUU9-024 | LOW-MEDIUM authority hardening | Migration JSON rejects duplicate `schemaVersion` only; duplicate critical properties and malformed UTF-8 are not strictly rejected before deserialization |
| CRUU9-025 | RELEASE BLOCKER | Approved `PromptHelperLogo.svg` / generated real ICO are still absent |

---

# 4. Final architecture invariants

The weak implementation model must not treat CRUU9 as 25 independent patches. The final design must satisfy these invariants:

```text
A — PHYSICAL TREE CONTAINMENT
Every managed operation remains inside the bound physical data root.
Managed child reparse points are rejected.

B — STRICT AUTHORITY PRESENCE
Missing, Present and Unreadable are different states.
File.Exists is not used to decide authority/control absence.

C — COMPLETE OWNERSHIP
Every precommit created path that can survive a crash is either declared
payload, declared payload temp, declared control, or a strict reserved
control path with deterministic recovery semantics.

D — MONOTONIC DURABILITY
Copying marker durable before payload.
Payload durable before Ready.
Ready durable before settings.
Settings pointer durable before marker retirement.

E — RECOVERY IDENTITY
Retry requires both target identity and source identity.
Unknown content is never auto-deleted.

F — MONOTONIC COMMIT
After durable settings commit, no path reports “not committed”, no target
rollback occurs, and shutdown is mandatory.

G — EXECUTED EVIDENCE
Mandatory test categories and exact sentinel names must actually appear as
passed in TRX; a script merely existing is not evidence.
```

---

# 5. Mandatory implementation phase order

```text
PHASE 0   Freeze baseline + source map
PHASE 1   Strict file authority operations                  (002)
PHASE 2   Managed physical tree containment                 (001)
PHASE 3   Manifest schema v3 authority                      (003,004,005,024)
PHASE 4   Migration control ownership + manifest staging    (006,007)
PHASE 5   Crash-durable settings pointer + temp controls    (008,009)
PHASE 6   Recovery terminal-state rewrite                   (010,011,014,015,016,017)
PHASE 7   Reservation ownership/commit semantics            (012,013)
PHASE 8   Remove unmanifested escape hatches                (018,019)
PHASE 9   Unconditional postcommit process boundary         (020)
PHASE 10  Replace weak tests + enforce CI evidence          (021,022)
PHASE 11  Exact release identity verifier                   (023)
PHASE 12  Full build/test/stress/publish
PHASE 13  Final source audit
```

CRUU9-025 remains blocked until the real approved artwork is supplied.

---

# 6. PHASE 0 — baseline instructions

Run and record:

```powershell
git status --short
git branch --show-current
git rev-parse HEAD
dotnet --info
pwsh --version
```

Audited baseline:

```text
be1da4fa49916a102616f82a6c74f5601ab5d2d6
```

If HEAD is newer, **do not reset or revert**. Compare and preserve equivalent/stronger newer fixes.

Open before editing:

```text
src/PromptHelper/App.xaml.cs
src/PromptHelper/MainWindow.xaml.cs
src/PromptHelper/Services/AppSettingsRepository.cs
src/PromptHelper/Services/AtomicTextWriter.cs
src/PromptHelper/Services/DataFolderMigrationService.cs
src/PromptHelper/Services/DataFolderTransitionCoordinator.cs
src/PromptHelper/Services/DataRootCapabilityValidator.cs
src/PromptHelper/Services/DataRootTopologyValidator.cs
src/PromptHelper/Services/EmptyTargetBaselineInspector.cs
src/PromptHelper/Services/IMigrationFileOps.cs
src/PromptHelper/Services/IMigrationManifestFileOps.cs
src/PromptHelper/Services/IVerifiedArtifactDeleter.cs
src/PromptHelper/Services/MigrationAttemptManifest.cs
src/PromptHelper/Services/MigrationManifestRepository.cs
src/PromptHelper/Services/MigrationRecoveryContext.cs
src/PromptHelper/Services/MigrationRecoveryService.cs
src/PromptHelper/Services/TargetRootReservation.cs
src/PromptHelper/Services/WindowsDirectoryCaseSensitivityInspector.cs
tests/PromptHelper.Tests/Cruu8ComprehensiveVerificationTests.cs
tools/VerifyTestEvidence.ps1
tools/VerifyReleaseAssets.ps1
.github/workflows/windows-ci.yml
```

---

# 7. CRUU9-001 — Managed child reparse escape

**Severity:** CRITICAL/HIGH

The root is physically bound, but later code performs path-based operations below it:

```text
<physicalRoot>\prompts\<id>.md
<physicalRoot>\recovery\<file>
```

`EmptyTargetBaselineInspector` currently permits pre-existing empty `prompts` and `recovery` directories. It does not reject a junction/symlink. An empty `prompts` junction to `D:\Outside` can therefore be classified as Empty, after which migration writes outside the bound target. Recovery can later delete through the same child redirect.

## Required policy

Inside the bound data root, Prompt Helper managed directories must be ordinary directories, not junctions/symlinks/mount points.

Managed directories:

```text
prompts
recovery
```

## Create `ManagedTreeTopologyValidator.cs`

```csharp
using System;
using System.IO;

namespace PromptHelper.Services;

internal sealed class ManagedTreeTopologyValidator
{
    private readonly IPhysicalPathResolver _resolver;

    public ManagedTreeTopologyValidator(
        IPhysicalPathResolver? resolver = null)
    {
        _resolver = resolver ?? new WindowsPhysicalPathResolver();
    }

    public void ValidateManagedTree(string physicalRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);

        string root = PathIdentity.NormalizeForComparison(physicalRoot);
        ValidateManagedDirectory(root, "prompts");
        ValidateManagedDirectory(root, "recovery");
    }

    public void ValidateManagedDirectory(
        string physicalRoot,
        string childName)
    {
        string child = Path.Combine(physicalRoot, childName);
        if (!Directory.Exists(child))
        {
            return;
        }

        FileAttributes attrs = File.GetAttributes(child);
        if ((attrs & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Prompt Helper managed directory '{child}' is a reparse point. " +
                "Managed data directories may not be junctions or symbolic links.");
        }

        string actual = DataRootTopologyValidator.ResolvePhysicalOrThrow(
            _resolver,
            child,
            $"managed '{childName}' directory");

        string expected = PathIdentity.NormalizeForComparison(
            Path.Combine(physicalRoot, childName));

        if (!PathIdentity.Equals(actual, expected))
        {
            throw new InvalidDataException(
                $"Managed directory '{child}' resolves outside its expected " +
                $"physical location. Expected '{expected}', resolved '{actual}'.");
        }
    }
}
```

Keep **both** the reparse attribute test and physical equality test.

## Mandatory call sites

```text
App startup after physical root + app lock, before migration recovery/library load
source snapshot before reading prompts/recovery
empty target after reservation before payload copy
existing target before accepting fingerprint
retry recovery before deletion
committed startup finalization before reads/hashes
```

## Stronger recommended lease

Add `ManagedTreeLease` that opens root/prompts/recovery directory handles with no `FILE_SHARE_DELETE` during migration/recovery. This makes later directory-node swapping harder and turns the previous repeated-check model into a bounded tree lease.

## Verified deletion containment

Change API to include physical root:

```csharp
void VerifyAndDelete(
    string physicalRoot,
    string path,
    long expectedLength,
    string expectedSha256Hex);
```

After opening the file handle, resolve the handle’s final path and require it to remain inside `physicalRoot` before marking the same handle for deletion.

## Tests

```text
CRUU9_001_Empty_prompts_junction_outside_target_is_rejected
CRUU9_001_Empty_recovery_junction_outside_target_is_rejected
CRUU9_001_Migration_never_writes_through_prompts_junction
CRUU9_001_Retry_never_deletes_through_prompts_junction
CRUU9_001_Committed_startup_rejects_managed_child_reparse
CRUU9_001_Normal_managed_directories_are_accepted
```

The junction tests must be real Windows tests and assert the outside directory remains byte-for-byte unchanged.

---

# 8. CRUU9-002 — `File.Exists` is fail-open for authority

**Severity:** HIGH

Do not use `File.Exists` to decide whether a marker/final/temp/control is absent. On Windows/.NET, insufficient permission can produce `false`.

## Create `StrictFileAuthority.cs`

```csharp
using System.IO;

namespace PromptHelper.Services;

internal enum StrictFilePresence
{
    Missing,
    Present
}

internal static class StrictFileAuthority
{
    public static StrictFilePresence GetPresence(string path)
    {
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            return StrictFilePresence.Present;
        }
        catch (FileNotFoundException)
        {
            return StrictFilePresence.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return StrictFilePresence.Missing;
        }
    }

    public static byte[]? ReadOptionalBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public static void DeleteIfPresentStrict(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
```

Do not catch `UnauthorizedAccessException` or generic `IOException` as Missing.

## Required refactors

- `MigrationManifestRepository.TryRead`: read first; only FileNotFound/DirectoryNotFound means null.
- `MigrationRecoveryService`: no `FileExists(markerPath)` early success gate.
- `App.xaml.cs`: call committed-startup finalization unconditionally; service itself decides whether marker is truly absent.
- verified artifact deleter: remove `File.Exists` pre-check; inspect native error from `CreateFileW`.
- target primary/backup state: attempt read; FileNotFound => Missing, access/sharing => Unreadable.

## Tests

```text
CRUU9_002_Unreadable_marker_is_not_treated_as_missing
CRUU9_002_Unreadable_final_is_not_treated_as_missing
CRUU9_002_Unreadable_temp_is_not_treated_as_missing
CRUU9_002_Verified_deleter_access_denied_is_not_noop
CRUU9_002_Unreadable_library_primary_is_Unreadable_not_Empty
CRUU9_002_App_startup_does_not_skip_unreadable_marker
```

---

# 9. CRUU9-003 — TempRelativePath can name unrelated target data

**Severity:** HIGH

Current schema-v2 validation proves only that a temp path is canonical, inside the lexical root and different from its own final. Retry recovery deletes it without hash verification.

Required temp grammar:

```text
final: library.json
temp:  .library.json.migration-<attemptIdN>-<32 hex chars>.tmp

final: prompts\abc.md
temp:  prompts\.abc.md.migration-<attemptIdN>-<32 hex chars>.tmp
```

`32 hex chars` = 128-bit nonce. Change production generation from `GetHexString(16)` to `GetHexString(32)`.

## Copy-ready validator

```csharp
private static void ValidateTempPath(
    Guid attemptId,
    string finalRelative,
    string tempRelative)
{
    string finalDir = Path.GetDirectoryName(finalRelative) ?? string.Empty;
    string tempDir = Path.GetDirectoryName(tempRelative) ?? string.Empty;

    if (!string.Equals(finalDir, tempDir, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException(
            "Migration temp must be in the same directory as its final artifact.");
    }

    string finalName = Path.GetFileName(finalRelative);
    string tempName = Path.GetFileName(tempRelative);
    string prefix = $".{finalName}.migration-{attemptId:N}-";
    const string suffix = ".tmp";

    if (!tempName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
        !tempName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException($"Invalid migration temp name '{tempRelative}'.");
    }

    string nonce = tempName.Substring(
        prefix.Length,
        tempName.Length - prefix.Length - suffix.Length);

    if (nonce.Length != 32 || nonce.Any(c => !Uri.IsHexDigit(c)))
    {
        throw new InvalidDataException(
            "Migration temp nonce must contain exactly 32 hexadecimal characters.");
    }
}
```

## Tests

```text
CRUU9_003_Temp_in_different_directory_rejected
CRUU9_003_Temp_without_attempt_id_rejected
CRUU9_003_Temp_with_other_attempt_id_rejected
CRUU9_003_Temp_with_short_nonce_rejected
CRUU9_003_Temp_with_nonhex_nonce_rejected
CRUU9_003_Production_temp_grammar_accepted
CRUU9_003_Arbitrary_prompt_cannot_be_declared_as_temp
```

---

# 10. CRUU9-004 — Cross-artifact final/temp collision

**Severity:** HIGH

Current code stores final paths and temp paths in separate HashSets. A temp for artifact A can therefore equal final for artifact B.

Recovery deletes temps first, so that collision can delete B’s final before verified-final handling.

Use one set:

```csharp
var allOwnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
{
    string finalFull = ResolveManifestArtifactPath(...);
    string tempFull = ResolveManifestArtifactPath(...);

    if (!allOwnedPaths.Add(finalFull))
    {
        throw new InvalidDataException(
            $"Duplicate owned migration path '{artifact.RelativePath}'.");
    }

    if (!allOwnedPaths.Add(tempFull))
    {
        throw new InvalidDataException(
            $"Migration temp/final path collision '{artifact.TempRelativePath}'.");
    }
}
```

Tests:

```text
CRUU9_004_Temp_A_equal_Final_B_rejected
CRUU9_004_Final_A_equal_Temp_B_rejected
CRUU9_004_Two_temps_equal_rejected
CRUU9_004_Two_finals_equal_rejected
```

---

# 11. CRUU9-005 — Retry recovery ignores source identity

**Severity:** MEDIUM-HIGH

Retry currently requires target equality but does not require the interrupted manifest’s `SourcePhysicalRoot` to equal the active source root.

Extend context:

```csharp
internal sealed record MigrationRecoveryContext(
    string TargetPhysicalRoot,
    string PhysicalBootstrapRoot,
    string? ExpectedSourcePhysicalRoot);
```

For retry:

```csharp
if (string.IsNullOrWhiteSpace(context.ExpectedSourcePhysicalRoot) ||
    !PathIdentity.Equals(
        context.ExpectedSourcePhysicalRoot,
        manifest.SourcePhysicalRoot))
{
    throw new MigrationRecoveryException(
        context.TargetPhysicalRoot,
        "ValidateSourceIdentity",
        new InvalidDataException(
            "The interrupted migration belongs to a different source library. " +
            "Prompt Helper will not delete it automatically."));
}
```

Committed startup does not need the original source to exist.

Tests:

```text
CRUU9_005_Same_source_interrupted_retry_can_clean
CRUU9_005_Different_source_interrupted_retry_fails_closed
CRUU9_005_Different_source_files_preserved_byte_exact
CRUU9_005_Committed_startup_does_not_require_source_to_exist
```

---

# 12. CRUU9-006 — Capability probe has no crash ownership

**Severity:** HIGH

The empty-migration capability probe creates random directories/files after the Copying manifest exists, but those control paths are not represented in the durable manifest. If the process dies during the probe, retry sees unknown content and cannot recover automatically.

## Required manifest schema v3

Bump only the migration-control manifest:

```csharp
public const int CurrentSchemaVersion = 3;
```

Do not change library/settings schemas.

Add:

```csharp
internal enum MigrationControlArtifactKind
{
    CapabilityProbeDirectory,
    CapabilityProbeFile,
    ManifestPhaseStaging
}

internal sealed class MigrationControlArtifact
{
    public string RelativePath { get; set; } = string.Empty;
    public MigrationControlArtifactKind Kind { get; set; }
}
```

Manifest:

```csharp
public List<MigrationControlArtifact> ControlArtifacts { get; set; } = [];
```

## Predeclare a probe plan before Copying marker publication

Create `MigrationCapabilityProbePlan.cs`:

```csharp
internal sealed record CapabilityProbeLocationPlan(
    string DirectoryRelativePath,
    string CurrentFileRelativePath,
    string ReplacementFileRelativePath);

internal sealed record MigrationCapabilityProbePlan(
    CapabilityProbeLocationPlan RootProbe,
    CapabilityProbeLocationPlan? PromptsProbe);
```

Use deterministic AttemptId-derived directories:

```text
.prompthelper-write-probe-<attemptIdN>-root
prompts\.prompthelper-write-probe-<attemptIdN>-prompts
```

Files:

```text
probe-current.txt
probe-replacement.tmp
```

Add every path to `ControlArtifacts` before the initial Copying manifest is made durable.

## Capability API

For empty migration, require the plan:

```csharp
internal CapabilityValidationResult ValidateWritable(
    string root,
    ICreatedPathJournal journal,
    ExistingLibraryCapabilityContext? existing,
    MigrationCapabilityProbePlan probePlan)
```

Do not generate another hidden Guid path internally.

Recovery owns and may remove only the exact manifest-declared probe controls.

Ready gate requires every ephemeral probe control absent.

Tests:

```text
CRUU9_006_Crash_after_probe_dir_creation_recovers
CRUU9_006_Crash_after_probe_current_creation_recovers
CRUU9_006_Crash_after_probe_replacement_creation_recovers
CRUU9_006_Other_attempt_probe_is_foreign
CRUU9_006_Ready_manifest_with_probe_residue_rejected
```

---

# 13. CRUU9-007 — Manifest staging temp is itself unowned

**Severity:** HIGH

Current `MigrationManifestRepository.WriteDurable` writes an unclaimed random staging temp before publishing/replacing `.prompthelper-migration.json`.

## First Copying publication

Do **not** use a staging temp.

Write the final marker path directly with `FileMode.CreateNew`, write complete JSON, `Flush(true)`, then return.

```csharp
public void CreateInitialCopyingManifestDurable(
    string markerPath,
    MigrationAttemptManifest manifest)
{
    if (manifest.Phase != MigrationManifestPhase.Copying)
    {
        throw new InvalidDataException("Initial manifest must be Copying.");
    }

    byte[] bytes = SerializeValidated(manifest);

    using Stream stream = _fileOps.CreateNew(markerPath);
    stream.Write(bytes, 0, bytes.Length);
    _fileOps.FlushToDisk(stream);
}
```

No payload creation is allowed before this method returns.

If the process dies while writing the initial final marker, no payload can exist yet. A partial/corrupt marker fails closed instead of creating unowned payload.

## Copying -> Ready replacement

Use deterministic stage:

```text
.prompthelper-migration.stage-<attemptIdN>.tmp
```

Declare it in `ControlArtifacts` as `ManifestPhaseStaging`.

Sequence:

```text
CreateNew exact stage
write JSON
Flush(true)
MoveFileExW(stage, marker, REPLACE_EXISTING | WRITE_THROUGH)
```

No random phase-stage filename.

Tests:

```text
CRUU9_007_Crash_during_initial_manifest_write_leaves_final_marker_path
CRUU9_007_Corrupt_initial_marker_blocks_without_payload_mutation
CRUU9_007_Crash_during_ready_stage_is_recoverable
CRUU9_007_Other_attempt_stage_is_foreign
```

---

# 14. CRUU9-008 — Settings pointer durability is weaker than target durability

**Severity:** HIGH

Current target protocol is stronger than the final settings pointer:

```text
payload temp Flush(true)
payload MoveFileEx WRITE_THROUGH
Ready manifest Flush(true) + write-through promotion
settings.json AtomicTextWriter -> File.Replace/File.Move
marker retirement
```

The protocol has not established that the settings pointer’s final-name update is durably ordered before marker retirement.

## Create `IDurableSettingsFileWriter.cs`

```csharp
internal interface IDurableSettingsFileWriter
{
    void WriteDurable(string targetPath, string content);
}
```

## Create `WindowsDurableSettingsFileWriter.cs`

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PromptHelper.Services;

internal sealed class WindowsDurableSettingsFileWriter
    : IDurableSettingsFileWriter
{
    private const uint MOVEFILE_REPLACE_EXISTING = 0x00000001;
    private const uint MOVEFILE_WRITE_THROUGH = 0x00000008;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileExW(
        string source,
        string destination,
        uint flags);

    public void WriteDurable(string targetPath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(content);

        string directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Settings path has no directory.");

        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(
            directory,
            $".prompthelper-settings-{Path.GetFileName(targetPath)}-{Guid.NewGuid():N}.tmp");

        bool promoted = false;
        Exception? primaryFailure = null;

        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (!MoveFileExW(
                    tempPath,
                    targetPath,
                    MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"Durable settings promotion failed for '{targetPath}'.");
            }

            promoted = true;
        }
        catch (Exception ex)
        {
            primaryFailure = ex;
            throw;
        }
        finally
        {
            if (!promoted)
            {
                // Cleanup through strict settings-temp cleanup helper.
                // If cleanup also fails, preserve both failures in a typed exception.
            }
        }
    }
}
```

Do not use `File.Exists(targetPath)` to decide replace behavior.

## AppSettingsRepository

Inject durable writer separately:

```csharp
private readonly IDurableSettingsFileWriter _durableSettingsWriter;
```

Authoritative primary:

```csharp
_durableSettingsWriter.WriteDurable(_settingsPath, json);
```

Prefer using the same durable writer for settings backup too, but backup failure remains warning-level according to current product semantics.

`SaveIfUnchanged` returns only after durable primary promotion.

Coordinator sets:

```csharp
settingsCommitted = true;
```

only after that return.

Tests must prove exact operation order:

```text
FlushSettingsTemp
MoveSettingsWriteThrough
SaveIfUnchangedReturn
MarkerRetire
```

Required tests:

```text
CRUU9_008_Settings_primary_uses_write_through_promotion
CRUU9_008_Settings_failure_before_durable_promotion_rolls_target_back
CRUU9_008_Marker_not_retired_before_durable_settings_return
```

---

# 15. CRUU9-009 — Settings temp residue blocks default-root recovery

**Severity:** MEDIUM-HIGH

Use reserved grammar from CRUU9-008:

```text
.prompthelper-settings-settings.json-<guidN>.tmp
.prompthelper-settings-settings.backup.json-<guidN>.tmp
```

Create strict parser `SettingsTempName` and cleanup stale recognized temps **only while holding `.settings.lock`**.

Call cleanup at the beginning of:

```text
LoadOrRecover
LoadForTransitionAndCapturePrecondition
Save
SaveIfUnchanged
```

Copy-ready shape:

```csharp
private void CleanupStaleSettingsTempsCore()
{
    string? directory = Path.GetDirectoryName(_settingsPath);
    if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
    {
        return;
    }

    foreach (string path in Directory.EnumerateFiles(
                 directory,
                 ".prompthelper-settings-*.tmp",
                 SearchOption.TopDirectoryOnly))
    {
        string name = Path.GetFileName(path);
        if (!SettingsTempName.TryParse(name, out _))
        {
            continue;
        }

        File.Delete(path);
    }
}
```

If a recognized stale settings temp cannot be deleted, stop. Do not migrate into exact bootstrap root while bootstrap control state is unresolved.

Tests:

```text
CRUU9_009_Stale_primary_settings_temp_cleaned_under_lease
CRUU9_009_Stale_backup_settings_temp_cleaned_under_lease
CRUU9_009_Invalid_similar_filename_not_deleted
CRUU9_009_Unremovable_settings_temp_blocks_default_root_transition
```

---

# 16. CRUU9-010 — Retry post-clean validation is incomplete

**Severity:** MEDIUM-HIGH

Current retry rebuilds inventory after cleanup but checks remaining finals only. Before deleting marker, it must require:

```text
no unknown entries
no final artifact
no payload temp
no ephemeral control artifact
only permitted persistent baseline/control remains
```

Create `RecoveryBaselineVerifier.cs`:

```csharp
internal static class RecoveryBaselineVerifier
{
    public static void AssertRestored(
        string targetRoot,
        TargetRecoveryInventory inventory,
        IAuthorityFileOps authorityOps)
    {
        if (inventory.HasUnknownEntries)
        {
            throw new InvalidDataException(
                "Recovery target contains unknown entries: " +
                string.Join(", ", inventory.UnknownEntries));
        }

        foreach (string rel in inventory.ManifestFinals)
        {
            AssertMissing(targetRoot, rel, authorityOps);
        }

        foreach (string rel in inventory.ManifestTemps)
        {
            AssertMissing(targetRoot, rel, authorityOps);
        }

        foreach (string rel in inventory.EphemeralControlArtifacts)
        {
            AssertMissing(targetRoot, rel, authorityOps);
        }
    }
}
```

Marker delete is allowed only after this gate.

Race test:

```text
initial inventory clean
cleanup
second inventory injects foreign.txt
=> marker remains, foreign preserved, recovery fails
```

---

# 17. CRUU9-011 — Recovery error contract is inconsistent

**Severity:** MEDIUM

Choose one contract. Recommended: typed exceptions.

Create:

```csharp
internal sealed class MigrationRecoveryException : IOException
{
    public string TargetRoot { get; }
    public string Operation { get; }

    public MigrationRecoveryException(
        string targetRoot,
        string operation,
        Exception inner)
        : base(
            $"Migration recovery failed during '{operation}' for '{targetRoot}': {inner.Message}",
            inner)
    {
        TargetRoot = targetRoot;
        Operation = operation;
    }
}
```

Refactor service to either:

```csharp
void RecoverForRetry(MigrationRecoveryContext context)
void FinalizeCommittedStartup(MigrationRecoveryContext context)
```

or wrap the **entire** method body into `RecoveryResult`. Do not mix raw throws with partial result handling.

Expected filesystem failures at:

```text
temp delete
verified final delete
directory cleanup
inventory
marker retirement
```

must have one stable error contract.

Coordinator catches that typed recovery failure after reservation, releases reservation, and aggregates cleanup failure if necessary.

---

# 18. CRUU9-012 — Reservation deletes committed directory ownership

**Severity:** MEDIUM

On successful new-target migration, `Release()` sees the intentionally non-empty target as an undeletable created directory and can generate false cleanup warnings.

Add:

```csharp
private bool _createdDirectoriesCommitted;

public void CommitRootOwnership()
{
    _createdDirectoriesCommitted = true;
}
```

Release:

```csharp
if (!_createdDirectoriesCommitted && _createdDirectories.Count > 0)
{
    CleanupCreatedDirectories(_createdDirectories, _fileOps, failures);
}
```

Coordinator after durable settings commit:

```csharp
settingsCommitted = true;
tx.Commit();
reservation.CommitRootOwnership();
```

Then release only lock/control state.

Tests:

```text
CRUU9_012_New_target_success_has_no_false_cleanup_warning
CRUU9_012_New_target_failure_removes_created_chain
CRUU9_012_Postcommit_release_preserves_new_target
```

---

# 19. CRUU9-013 — Reservation ownership race and swallowed acquire cleanup

**Severity:** MEDIUM

Do not infer ownership from:

```text
DirectoryExists=false
then recursive Directory.CreateDirectory(root)
```

Use native per-segment create result.

Extend file ops:

```csharp
internal enum DirectoryCreateOutcome
{
    CreatedByCaller,
    AlreadyExists
}

DirectoryCreateOutcome TryCreateDirectoryOwned(string path);
```

Windows implementation uses `CreateDirectoryW`:

```text
success -> CreatedByCaller
ERROR_ALREADY_EXISTS -> AlreadyExists
other -> throw
```

Create shallowest -> deepest and record only paths for which this process received success.

If lock acquisition then fails, cleanup only those exact paths and surface cleanup failures in a typed `TargetRootReservationAcquireException`.

Tests:

```text
CRUU9_013_Concurrent_parent_creation_not_marked_owned
CRUU9_013_Only_actually_created_directories_removed
CRUU9_013_Acquire_failure_reports_parent_cleanup_failure
```

---

# 20. CRUU9-014 — Stale root `.app.lock` blocks retry

**Severity:** MEDIUM-HIGH

`.app.lock` presence is not active lock ownership. It is normal for the lock file to survive after the process handle closes.

Change Empty target baseline:

```text
allow exact root-relative .app.lock always
```

Do not allow:

```text
prompts\.app.lock
recovery\.app.lock
```

Reservation is the authority:

```text
can acquire FileShare.None -> stale/unheld -> proceed
sharing/lock violation -> target in use -> block
```

Tests:

```text
CRUU9_014_Stale_root_app_lock_does_not_make_target_occupied
CRUU9_014_Held_root_app_lock_blocks_transition
CRUU9_014_Nested_app_lock_is_foreign
CRUU9_014_Crash_after_reservation_before_manifest_is_retryable
```

---

# 21. CRUU9-015 — Physical/lexical bootstrap mismatch

**Severity:** MEDIUM

Create `DataRootRuntimeContext`:

```csharp
internal sealed record DataRootRuntimeContext(
    string ActivePhysicalRoot,
    string BootstrapLexicalRoot,
    string BootstrapPhysicalRoot);
```

Resolve physical bootstrap once with the same physical resolver:

```csharp
string physicalBootstrap =
    DataRootTopologyValidator.ResolvePhysicalOrThrow(
        _physicalPathResolver,
        bootstrapRoot,
        "bootstrap settings folder");
```

Use `BootstrapPhysicalRoot` for:

```text
EmptyTargetBaselineInspector
MigrationRecoveryContext
exact-bootstrap comparison
settings/control baseline
```

Remove redundant persisted `TargetIsBootstrapRoot` from manifest v3. Derive truth from physical paths.

Test fake resolver:

```text
lexical bootstrap -> redirected physical bootstrap
```

and verify exact-default migration preserves settings controls.

---

# 22. CRUU9-016 — Nested control filenames are silently ignored

**Severity:** MEDIUM

Recovery currently ignores `.prompthelper-migration.json` and `.app.lock` by filename even during recursive scans. Only root-relative controls are controls.

Use:

```csharp
private static bool IsRootControlPath(string relativePath)
{
    string rel = NormalizeRel(relativePath);

    return rel.Equals(
               ".app.lock",
               StringComparison.OrdinalIgnoreCase) ||
           rel.Equals(
               ".prompthelper-migration.json",
               StringComparison.OrdinalIgnoreCase);
}
```

Do not use only `Path.GetFileName`.

Tests:

```text
CRUU9_016_Root_app_lock_is_control
CRUU9_016_Nested_app_lock_is_unknown
CRUU9_016_Root_manifest_is_control
CRUU9_016_Nested_manifest_named_file_is_unknown
```

---

# 23. CRUU9-017 — ReadyToCommit has no explicit terminal invariant gate

**Severity:** MEDIUM

Create `MigrationReadyGate.cs`.

It must verify immediately before the phase change:

```text
managed tree physically safe
all final payload files present and exact hash+length
all declared payload temps strictly absent
all ephemeral control paths absent
no unknown entries
source snapshot still valid if the design requires one last source proof
```

Suggested shape:

```csharp
internal sealed class MigrationReadyGate
{
    private readonly IAuthorityFileOps _authority;
    private readonly ManagedTreeTopologyValidator _tree;

    public void AssertReady(
        string physicalTargetRoot,
        MigrationAttemptManifest manifest,
        MigrationPayloadSnapshot snapshot)
    {
        _tree.ValidateManagedTree(physicalTargetRoot);

        foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
        {
            AssertFinalMatches(physicalTargetRoot, artifact);
            AssertTempMissing(physicalTargetRoot, artifact);
        }

        foreach (MigrationControlArtifact control in manifest.ControlArtifacts)
        {
            if (IsEphemeral(control))
            {
                AssertControlMissing(physicalTargetRoot, control);
            }
        }
    }
}
```

Only after this returns:

```csharp
manifest.Phase = MigrationManifestPhase.ReadyToCommit;
```

Tests inject a declared temp, probe residue, unknown file, changed final and child junction between copy and Ready gate.

---

# 24. CRUU9-018 — Unmanifested temp fallback remains

**Severity:** MEDIUM

Remove the current nullable fallback completely.

Required production signature:

```csharp
internal void CopySnapshotToTarget(
    string currentRoot,
    string targetRoot,
    MigrationPayloadSnapshot snapshot,
    MigrationAttemptManifest manifest,
    MigrationTargetTransaction tx)
```

For every snapshot file:

```text
find exactly one manifest artifact by RelativePath
require role/length/hash equal snapshot
resolve exact TempRelativePath
copy using that temp
```

No artifact -> throw before mutation.

Extra artifact -> throw before mutation.

No overload may generate a hidden random temp.

The internal `PrepareTargetForMigrationUnitTest` must either be removed or construct a real manifest through the same `MigrationManifestBuilder` used by production.

Tests:

```text
CRUU9_018_Copy_requires_manifest_artifact_for_every_snapshot_file
CRUU9_018_Extra_manifest_artifact_rejected
CRUU9_018_No_CopySnapshot_overload_has_nullable_temp_map
```

---

# 25. CRUU9-019 — Manifest cleanup exception masks primary failure

**Severity:** LOW-MEDIUM

Create exception that preserves both failures:

```csharp
internal sealed class ManifestWriteCleanupException : IOException
{
    public string MarkerPath { get; }
    public string TempPath { get; }
    public Exception OriginalFailure { get; }
    public Exception CleanupFailure { get; }

    public ManifestWriteCleanupException(
        string markerPath,
        string tempPath,
        Exception originalFailure,
        Exception cleanupFailure)
        : base(
            $"Migration manifest write failed for '{markerPath}', and " +
            $"staging cleanup also failed for '{tempPath}'.",
            originalFailure)
    {
        MarkerPath = markerPath;
        TempPath = tempPath;
        OriginalFailure = originalFailure;
        CleanupFailure = cleanupFailure;
    }
}
```

Do not throw a cleanup-only exception from `finally` that replaces the primary promotion failure.

Test exact `OriginalFailure` and `CleanupFailure` references.

---

# 26. CRUU9-020 — Forced shutdown still depends on DialogResult

**Severity:** MEDIUM

`RestartRequired` is the process-boundary authority. `DialogResult` is not.

Refactor MainWindow:

```csharp
internal void CompleteSettingsDialog(
    bool? dialogResult,
    bool restartRequired)
{
    if (!restartRequired)
    {
        return;
    }

    try
    {
        MessageBox.Show(
            this,
            "Data folder changed\n\nPrompt Helper must close now so " +
            "the previous data folder cannot be modified after the migration snapshot.\n\n" +
            "Open Prompt Helper again to use the selected data folder.",
            "Restart Required",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
    finally
    {
        _applicationLifetime.RequestShutdown();
    }
}
```

Production:

```csharp
bool? result = dialog.ShowDialog();
CompleteSettingsDialog(result, dialog.RestartRequired);
```

Do not require `result == true`.

Tests:

```text
CRUU9_020_RestartRequired_true_Result_true_shuts_down
CRUU9_020_RestartRequired_true_Result_null_shuts_down
CRUU9_020_RestartRequired_true_Result_false_shuts_down
CRUU9_020_RestartRequired_false_does_not_shutdown
CRUU9_020_Restart_message_failure_still_requests_shutdown
```

---

# 27. CRUU9-021 — CRUU8 tests overclaim behavior

**Severity:** MEDIUM verification gap

Replace weak evidence, do not merely add comments.

Current examples:

```text
CRUU8_001 only injects flush failure and Assert.Throws<Exception>; it does not assert marker preservation.
CRUU8_002 uses temp names that violate the intended AttemptId grammar.
CRUU8_008 manually constructs MigrationRollbackException instead of inducing coordinator cleanup failure.
CRUU8_010 is tagged WindowsFilesystemIntegration but uses a fake inspector.
CRUU8_017 checks enum count, not dispatch behavior.
CRUU8_019 checks script existence, not TRX parsing.
```

Rules:

```text
No Assert.Throws<Exception> in CRUU9 safety tests.
A test named WindowsFilesystemIntegration performs real Windows filesystem behavior.
A test named cleanup failure injects an actual cleanup failure.
A test named evidence parsing invokes the script against a fixture TRX.
```

Required replacements:

```text
CRUU9_021_Rollback_delete_failure_preserves_actual_marker
CRUU9_021_Coordinator_cleanup_failure_contains_exact_operation
CRUU9_021_Real_case_sensitive_NTFS_test_executes_native_inspector
CRUU9_021_All_target_kinds_execute_real_dispatch_path
CRUU9_021_VerifyTestEvidence_accepts_valid_fixture_TRX
CRUU9_021_VerifyTestEvidence_rejects_missing_sentinel_TRX
```

---

# 28. CRUU9-022 — CI/evidence enforcement is not wired

**Severity:** MEDIUM verification gap

`VerifyTestEvidence.ps1` currently emits `Write-Warning` for missing sentinels and still succeeds. CI does not call the script and does not run the mandatory categories separately.

## Fix evidence script

Required parameters:

```powershell
param(
    [Parameter(Mandatory=$true)]
    [string]$TrxPath,

    [string[]]$RequiredTests = @()
)
```

Missing required test:

```powershell
if ($missingRequired.Count -gt 0) {
    throw (
        "Required tests were not executed: " +
        ($missingRequired -join ", ")
    )
}
```

Require exact names and successful outcomes.

## CI required steps

```yaml
- name: Test Crash Recovery
  shell: pwsh
  run: |
    dotnet test PromptHelper.slnx `
      -c Release `
      --no-build `
      --filter "TestCategory=CrashRecovery" `
      --logger "trx;LogFileName=cruu9-crash.trx"

- name: Test WPF Integration
  shell: pwsh
  run: |
    dotnet test PromptHelper.slnx `
      -c Release `
      --no-build `
      --filter "TestCategory=WpfIntegration" `
      --logger "trx;LogFileName=cruu9-wpf.trx"

- name: Test Windows Filesystem Integration
  shell: pwsh
  run: |
    dotnet test PromptHelper.slnx `
      -c Release `
      --no-build `
      --filter "TestCategory=WindowsFilesystemIntegration" `
      --logger "trx;LogFileName=cruu9-winfs.trx"
```

Then invoke verifier against actual produced TRX paths. Confirm the real path in CI rather than guessing it.

Mandatory exact sentinels should include at least:

```text
CRUU9_001_Empty_prompts_junction_outside_target_is_rejected
CRUU9_002_Unreadable_marker_is_not_treated_as_missing
CRUU9_008_Settings_primary_uses_write_through_promotion
CRUU9_020_Restart_message_failure_still_requests_shutdown
```

---

# 29. CRUU9-023 — Exact release icon identity still absent

**Severity:** MEDIUM release gap

Current strict verifier proves ICO structure and that the EXE exposes at least one icon group. It does not prove identity.

Final strict flow:

```text
current approved SVG
  -> regenerate temporary expected ICO
  -> compare normalized RGBA pixel hashes per required size to committed ICO
  -> extract RT_GROUP_ICON / RT_ICON resources from published EXE
  -> compare normalized RGBA hashes to committed ICO
```

Required sizes:

```text
16
24
32
48
64
128
256
```

Do not rely only on raw ICO byte equality.

Create fixture A/B tests now, even before product logo exists:

```text
A vs A pass
A vs B fail
EXE(A) vs A pass
EXE(A) vs B fail
missing required size fail
```

CRUU9-025 remains separate.

---

# 30. CRUU9-024 — Strict manifest JSON authority

**Severity:** LOW-MEDIUM

Raw JSON validation currently rejects duplicate `schemaVersion` only. Reject duplicate case-insensitive known properties for every object.

Root properties:

```text
schemaVersion
attemptId
sourcePhysicalRoot
targetPhysicalRoot
sourceLibrarySha256Hex
phase
artifacts
controlArtifacts
```

Payload artifact:

```text
relativePath
tempRelativePath
sha256Hex
length
role
```

Control artifact:

```text
relativePath
kind
```

Recommended: reject unknown properties in migration control manifests.

Strict UTF-8:

```csharp
string json = new UTF8Encoding(
    encoderShouldEmitUTF8Identifier: false,
    throwOnInvalidBytes: true)
    .GetString(rawBytes);
```

Tests:

```text
CRUU9_024_Duplicate_targetPhysicalRoot_rejected
CRUU9_024_Duplicate_artifacts_case_variant_rejected
CRUU9_024_Unknown_root_property_rejected
CRUU9_024_Invalid_UTF8_manifest_rejected
```

---

# 31. CRUU9-025 — Real logo remains absent

**Severity:** RELEASE BLOCKER

At audited commit, `src/PromptHelper/Assets` is not present through the repository contents endpoint.

Do not fabricate a logo, copy a fixture into product assets, or weaken the release gate.

Correct status until approved artwork is supplied:

```text
PRODUCT/CODE MAY BECOME CLEAN
STRICT RELEASE = BLOCKED
```

---

# 32. Exact proposed manifest schema v3

```csharp
internal enum MigrationManifestPhase
{
    Copying,
    ReadyToCommit
}

internal enum MigrationControlArtifactKind
{
    CapabilityProbeDirectory,
    CapabilityProbeFile,
    ManifestPhaseStaging
}

internal sealed class MigrationAttemptManifest
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid AttemptId { get; set; }
    public string SourcePhysicalRoot { get; set; } = string.Empty;
    public string TargetPhysicalRoot { get; set; } = string.Empty;
    public string SourceLibrarySha256Hex { get; set; } = string.Empty;
    public MigrationManifestPhase Phase { get; set; }
    public List<MigrationManifestArtifact> Artifacts { get; set; } = [];
    public List<MigrationControlArtifact> ControlArtifacts { get; set; } = [];
}

internal sealed class MigrationManifestArtifact
{
    public string RelativePath { get; set; } = string.Empty;
    public string TempRelativePath { get; set; } = string.Empty;
    public string Sha256Hex { get; set; } = string.Empty;
    public long Length { get; set; }
    public MigrationPayloadRole Role { get; set; }
}

internal sealed class MigrationControlArtifact
{
    public string RelativePath { get; set; } = string.Empty;
    public MigrationControlArtifactKind Kind { get; set; }
}
```

Remove redundant `TargetIsBootstrapRoot` and derive bootstrap identity from physical runtime context.

---

# 33. Schema-v2 compatibility policy

Do not blindly delete v2 residues and do not simply reject every v2 marker if safe compatibility can be proven.

Recommended reader:

```text
read raw schemaVersion first
schema 3 -> strict v3 parser
schema 2 -> schema2 DTO + NEW strict temp grammar + cross-collision checks + source identity
other -> fail closed
```

A v2 marker from the current CRUU8 implementation may be recoverable if:

```text
every temp matches production grammar
every path is safe
source matches retry source
no unknown probe/control residue exists
```

If those cannot be proven, fail closed without deletion.

---

# 34. `MigrationManifestBuilder` — one authority for generated names

Create one builder. Coordinator must not hand-build path strings.

```csharp
internal static class MigrationManifestBuilder
{
    public static MigrationAttemptManifest BuildCopying(
        string sourcePhysicalRoot,
        string targetPhysicalRoot,
        MigrationPayloadSnapshot snapshot,
        Guid attemptId)
    {
        var artifacts = new List<MigrationManifestArtifact>();

        foreach (MigrationPayloadFile file in snapshot.Files)
        {
            string directory = Path.GetDirectoryName(file.RelativePath) ?? string.Empty;
            string finalName = Path.GetFileName(file.RelativePath);
            string nonce = RandomNumberGenerator.GetHexString(32).ToLowerInvariant();

            string tempName =
                $".{finalName}.migration-{attemptId:N}-{nonce}.tmp";

            string tempRelative = string.IsNullOrEmpty(directory)
                ? tempName
                : Path.Combine(directory, tempName);

            artifacts.Add(new MigrationManifestArtifact
            {
                RelativePath = file.RelativePath,
                TempRelativePath = tempRelative,
                Sha256Hex = Convert.ToHexStringLower(file.Sha256),
                Length = file.Length,
                Role = file.Role
            });
        }

        List<MigrationControlArtifact> controls = BuildControlArtifacts(attemptId, snapshot);

        return new MigrationAttemptManifest
        {
            AttemptId = attemptId,
            SourcePhysicalRoot = sourcePhysicalRoot,
            TargetPhysicalRoot = targetPhysicalRoot,
            SourceLibrarySha256Hex = artifacts.Single(
                x => x.Role == MigrationPayloadRole.PrimaryMetadata).Sha256Hex,
            Phase = MigrationManifestPhase.Copying,
            Artifacts = artifacts,
            ControlArtifacts = controls
        };
    }
}
```

---

# 35. Stronger recommended `ManagedTreeLease`

For the strongest practical Windows design, after validating the managed tree, hold directory handles during transition/recovery.

Open:

```text
physical root
prompts if present
recovery if present
```

with:

```text
FILE_FLAG_BACKUP_SEMANTICS
FILE_SHARE_READ | FILE_SHARE_WRITE
NO FILE_SHARE_DELETE
```

This prevents ordinary rename/delete/reparse replacement of those directory nodes while the operation is active.

Use during:

```text
empty-target migration
existing-target final inspection
retry recovery
committed startup finalization
```

If implementing this, add Windows race tests attempting junction replacement while the lease is held.

---

# 36. Final required empty-target sequence

```text
01 acquire settings transition snapshot + dual CAS precondition
02 resolve active physical root
03 resolve physical bootstrap root
04 bind target physical root
05 validate managed tree
06 strict target classification
07 capture complete source snapshot
08 allocate AttemptId
09 build manifest v3 + payload temps + control paths
10 reserve target root
11 acquire managed-tree lease
12 revalidate lexical locator -> same physical target
13 if interrupted: recover only if source identity matches
14 strict Empty baseline recheck
15 validate managed tree again
16 create initial Copying marker directly at final marker path + Flush(true)
17 copy every payload artifact using manifest-declared temp
18 Flush(true) each temp
19 write-through promote each final
20 verify source set + source hashes
21 verify target hashes
22 capability probe using manifest-declared controls
23 remove probe controls
24 MigrationReadyGate.AssertReady
25 write deterministic Ready staging control
26 Flush(true) stage + write-through replace marker to Ready
27 revalidate locator
28 validate managed tree
29 compare settings CAS
30 durable write-through settings primary
31 POINT OF NO RETURN
32 commit target transaction
33 reservation.CommitRootOwnership
34 retire Ready marker
35 release reservation
36 combine postcommit warnings
37 return Changed=true RestartRequired=true
38 SettingsDialog preserves RestartRequired
39 MainWindow shuts down regardless DialogResult
```

---

# 37. Final required retry sequence

```text
01 strict-read marker; unreadable != missing
02 parse strict v3 or strictly-safe v2
03 require target physical identity
04 require manifest source == active source physical identity
05 validate managed tree
06 acquire tree/recovery lease
07 recursive inventory
08 reject unknown content
09 delete exact declared controls
10 delete exact declared payload temps
11 verify+delete exact finals using same-handle deleter constrained to physical root
12 remove empty attempt directories
13 rebuild full inventory
14 RecoveryBaselineVerifier.AssertRestored
15 delete marker LAST
16 strictly prove marker missing
17 return success
```

Any failure:

```text
marker remains
foreign data remains untouched
source settings remain authoritative
```

---

# 38. Final required startup sequence

```text
01 acquire settings lease
02 clean strict stale settings temps
03 load/recover settings
04 resolve configured physical root
05 resolve physical bootstrap root
06 validate topology + case sensitivity
07 acquire .app.lock
08 validate managed tree
09 call committed migration finalization unconditionally
10 if marker truly missing -> continue
11 if Ready -> verify all finals, no temps/controls, no unknowns
12 require marker retirement success
13 ensure normal data directories
14 validate managed tree again
15 load library
16 show MainWindow
```

No writable UI before migration authority is resolved.

---

# 39. Fault-injection matrix

| Fault | Required outcome |
|---|---|
| marker access denied | fail closed; no payload mutation |
| prompts child is junction | reject before write |
| recovery child is junction | reject before deletion |
| arbitrary manifest temp points to user file | manifest rejected |
| temp A == final B | manifest rejected |
| interrupted source != active source | no delete |
| capability probe crash | exact declared controls recover |
| initial marker partial write | corrupt marker at reserved path; no payload exists |
| Ready stage crash | Copying marker + declared stage recoverable |
| settings temp flush failure | no settings commit |
| settings MoveFileEx failure | no point of no return |
| stale settings temp | cleanup under settings lease |
| new foreign entry after retry cleanup | marker remains |
| remaining declared temp | marker remains |
| stale root `.app.lock` | retry permitted |
| held `.app.lock` | transition blocked |
| nested `.app.lock` | foreign |
| concurrent parent creation | not recorded as ours |
| successful new target release | no false directory warning |
| Restart MessageBox throws | shutdown still requested |
| required sentinel missing | CI fails |
| wrong published EXE icon | strict release gate fails |

---

# 40. Required preservation assertions

For failure tests, snapshot bytes before operation and compare afterward:

```text
settings.json
settings.backup.json
source library.json
source library.backup.json
source active prompt
source orphan prompt
source recovery artifact
foreign target file
other-attempt temp/control
bootstrap settings files
outside-junction target files
```

Use byte equality, not only `File.Exists`.

---

# 41. Weak-model traps — forbidden shortcuts

```text
Do not use File.Exists as authority presence.
Do not assume physical root makes child directories safe.
Do not accept arbitrary canonical temp paths.
Do not use separate uniqueness sets for finals and temps only.
Do not auto-clean a migration from another source.
Do not rely on in-memory transaction journal for crash ownership.
Do not create a random manifest staging temp before durable marker ownership.
Do not claim Flush(true) on staging file makes the final settings pathname durable.
Do not delete marker after checking only finals.
Do not mix raw recovery exceptions and RecoveryResult arbitrarily.
Do not call committed target nonempty state a cleanup failure.
Do not infer directory ownership from a stale Exists check.
Do not treat .app.lock presence as active lock ownership.
Do not compare physical target to lexical bootstrap.
Do not treat control filenames as controls at any depth.
Do not assign ReadyToCommit without a terminal invariant gate.
Do not keep nullable/unmanifested temp fallback for tests.
Do not replace primary failure with cleanup failure.
Do not use DialogResult as commit authority.
Do not tag a fake test as Windows integration evidence.
Do not let missing sentinels only warn.
Do not fabricate the product logo.
```

---

# 42. Recommended new files

```text
src/PromptHelper/Services/
    StrictFileAuthority.cs
    IAuthorityFileOps.cs
    ManagedTreeTopologyValidator.cs
    ManagedTreeLease.cs
    DataRootRuntimeContext.cs
    MigrationManifestBuilder.cs
    MigrationControlArtifact.cs
    MigrationCapabilityProbePlan.cs
    MigrationReadyGate.cs
    RecoveryBaselineVerifier.cs
    IDurableSettingsFileWriter.cs
    WindowsDurableSettingsFileWriter.cs
    SettingsTempName.cs
    MigrationRecoveryException.cs
    TargetRootReservationAcquireException.cs

tests/PromptHelper.Tests/
    Cruu9AuthorityFileTests.cs
    Cruu9ManagedTreeTests.cs
    Cruu9ManifestV3Tests.cs
    Cruu9MigrationControlTests.cs
    Cruu9DurableSettingsTests.cs
    Cruu9RecoveryTests.cs
    Cruu9ReservationTests.cs
    Cruu9WpfCommitBoundaryTests.cs
    Cruu9EvidenceTests.cs
    WindowsManagedTreeIntegrationTests.cs

tools/
    VerifyTestEvidence.ps1
    CompareIconIdentity.ps1
```

Do not create duplicate abstractions if newer HEAD already contains equivalent stronger ones.

---

# 43. Mandatory named tests

## Authority

```text
CRUU9_002_Unreadable_marker_is_not_treated_as_missing
CRUU9_002_Unreadable_final_is_not_treated_as_missing
CRUU9_002_Unreadable_temp_is_not_treated_as_missing
```

## Managed tree

```text
CRUU9_001_Empty_prompts_junction_outside_target_is_rejected
CRUU9_001_Empty_recovery_junction_outside_target_is_rejected
CRUU9_001_Retry_never_deletes_outside_bound_root
```

## Manifest

```text
CRUU9_003_Temp_without_attempt_id_rejected
CRUU9_003_Arbitrary_prompt_cannot_be_declared_as_temp
CRUU9_004_Temp_A_equal_Final_B_rejected
CRUU9_005_Different_source_interrupted_retry_fails_closed
CRUU9_024_Duplicate_targetPhysicalRoot_rejected
CRUU9_024_Invalid_UTF8_manifest_rejected
```

## Controls

```text
CRUU9_006_Crash_after_probe_dir_creation_recovers
CRUU9_007_Crash_during_ready_stage_is_recoverable
```

## Durable settings

```text
CRUU9_008_Settings_primary_uses_write_through_promotion
CRUU9_008_Settings_failure_before_durable_promotion_rolls_target_back
CRUU9_008_Marker_not_retired_before_durable_settings_return
CRUU9_009_Stale_primary_settings_temp_cleaned_under_lease
```

## Recovery

```text
CRUU9_010_New_foreign_entry_after_cleanup_preserves_marker
CRUU9_010_Remaining_temp_preserves_marker
CRUU9_011_Marker_delete_failure_is_typed_recovery_failure
```

## Reservation

```text
CRUU9_012_New_target_success_has_no_false_cleanup_warning
CRUU9_013_Concurrent_parent_creation_not_marked_owned
CRUU9_014_Stale_root_app_lock_does_not_make_target_occupied
```

## Bootstrap/control

```text
CRUU9_015_Physical_bootstrap_alias_is_recognized
CRUU9_016_Nested_app_lock_is_unknown
CRUU9_017_Ready_gate_rejects_declared_temp
CRUU9_018_Copy_requires_manifest_artifact_for_every_snapshot_file
```

## UI/evidence

```text
CRUU9_020_RestartRequired_true_Result_null_shuts_down
CRUU9_020_Restart_message_failure_still_requests_shutdown
CRUU9_021_VerifyTestEvidence_rejects_missing_sentinel_TRX
```

---

# 44. Windows integration requirements

Mandatory real Windows operations:

```text
real target-root junction
real prompts-child junction
real recovery-child junction
real persisted root alias
real case-sensitive directory
real MoveFileEx settings writer smoke test
real handle-bound verified deletion
real stale .app.lock acquisition
```

A fake inspector cannot satisfy the real case-sensitive test.

A skipped mandatory integration test is missing evidence, not PASS.

---

# 45. Phase implementation loop for weak AI

For every phase:

```text
1. Read current production code.
2. Read current related tests.
3. Implement production change.
4. Add direct negative + positive focused tests.
5. Build Release.
6. Run focused tests.
7. Inspect git diff for weakening/escape hatches.
8. Only then move to next phase.
```

Never implement all phases before compiling.

---

# 46. Focused test command examples

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --filter "FullyQualifiedName~Cruu9AuthorityFileTests"
```

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --filter "FullyQualifiedName~Cruu9ManagedTreeTests"
```

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --filter "FullyQualifiedName~Cruu9ManifestV3Tests"
```

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --filter "FullyQualifiedName~Cruu9RecoveryTests"
```

---

# 47. Full acceptance commands

## Restore

```powershell
dotnet restore PromptHelper.slnx
```

## Release build

```powershell
dotnet build PromptHelper.slnx `
  -c Release `
  --no-restore
```

Required:

```text
0 errors
0 warnings
```

## Mandatory categories

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --filter "TestCategory=CrashRecovery" `
  --logger "trx;LogFileName=cruu9-crash.trx"
```

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --filter "TestCategory=WpfIntegration" `
  --logger "trx;LogFileName=cruu9-wpf.trx"
```

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --filter "TestCategory=WindowsFilesystemIntegration" `
  --logger "trx;LogFileName=cruu9-winfs.trx"
```

## Full suite

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --logger "trx;LogFileName=cruu9-full.trx"
```

## Five runs

```powershell
1..5 | ForEach-Object {
    Write-Host "=== CRUU9 FULL RUN $_ / 5 ==="

    dotnet test PromptHelper.slnx `
      -c Release `
      --no-build `
      --logger "trx;LogFileName=cruu9-run-$_.trx"

    if ($LASTEXITCODE -ne 0) {
        throw "CRUU9 full-suite run $_ failed."
    }
}
```

## Publish

```powershell
Remove-Item -Recurse -Force artifacts\publish-check -ErrorAction SilentlyContinue

dotnet publish `
  src/PromptHelper/PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/publish-check
```

Require:

```text
PromptHelper.exe
LICENSE
THIRD_PARTY_NOTICES.md
```

---

# 48. Final source grep gate

After tests pass:

```powershell
git grep -n "File.Exists" -- `
  src/PromptHelper/Services/Migration* `
  src/PromptHelper/App.xaml.cs
```

Every result must be manually reviewed. Authority decisions should not rely on it.

Also:

```powershell
git grep -n "declaredTempMap"
git grep -n "Assert.Throws<Exception>"
git grep -n "Write-Warning" -- tools/VerifyTestEvidence.ps1
git grep -n "TargetIsBootstrapRoot"
```

Expected after CRUU9:

```text
nullable declaredTempMap escape: none
CRUU9 safety Assert.Throws<Exception>: none
missing-evidence warning-only path: none
redundant TargetIsBootstrapRoot: none
```

---

# 49. Per-finding acceptance criteria

## CRUU9-001

```text
PASS only if real child-junction tests prove no migration/recovery write/delete can escape physical root.
```

## CRUU9-002

```text
PASS only if unreadable marker/final/temp is never treated Missing and File.Exists is removed from authority gates.
```

## CRUU9-003

```text
PASS only if temp grammar includes exact final name + AttemptId + 32 hex nonce chars and arbitrary target files cannot be temp-owned.
```

## CRUU9-004

```text
PASS only if one unified path set rejects every final/temp/control collision.
```

## CRUU9-005

```text
PASS only if retry source identity must match and different-source bytes are preserved.
```

## CRUU9-006

```text
PASS only if crash at every capability-probe creation point auto-recovers from durable declared ownership.
```

## CRUU9-007

```text
PASS only if no manifest stage can exist without a reserved final marker or declared control authority.
```

## CRUU9-008

```text
PASS only if settings primary final promotion is write-through and marker retirement occurs afterward.
```

## CRUU9-009

```text
PASS only if stale settings temps have strict grammar, cleanup occurs under settings lease and similar foreign filenames survive.
```

## CRUU9-010

```text
PASS only if second full inventory proves no unknown/temp/control residue before marker deletion.
```

## CRUU9-011

```text
PASS only if all expected recovery filesystem failures use one typed contract.
```

## CRUU9-012

```text
PASS only if successful new root has no false cleanup warning and created directories are preserved after commit.
```

## CRUU9-013

```text
PASS only if directory ownership records native creation success, not a stale pre-check.
```

## CRUU9-014

```text
PASS only if stale root lock is retryable and held lock blocks.
```

## CRUU9-015

```text
PASS only if physical bootstrap is resolved once and used consistently.
```

## CRUU9-016

```text
PASS only if control paths are recognized by exact root-relative path, not filename.
```

## CRUU9-017

```text
PASS only if Ready phase has explicit terminal invariant gate.
```

## CRUU9-018

```text
PASS only if no production/internal copy API can generate unmanifested temps.
```

## CRUU9-019

```text
PASS only if both original and cleanup failures remain inspectable.
```

## CRUU9-020

```text
PASS only if RestartRequired always causes shutdown regardless modal result or restart-message failure.
```

## CRUU9-021 / 022

```text
PASS only if tests actually execute named behavior and CI fails when mandatory evidence is missing.
```

## CRUU9-023

```text
PASS code-preparation only when exact normalized icon identity verifier is implemented with fixtures.
```

## CRUU9-024

```text
PASS only if duplicate critical JSON fields and malformed UTF-8 fail closed.
```

## CRUU9-025

```text
BLOCKED until the real approved product artwork is supplied.
```

---

# 50. Required implementation evidence report

```text
CRUU9 IMPLEMENTATION EVIDENCE

BASELINE
- starting commit:
- final commit:
- branch:

FINDINGS
- CRUU9-001:
- CRUU9-002:
- CRUU9-003:
- CRUU9-004:
- CRUU9-005:
- CRUU9-006:
- CRUU9-007:
- CRUU9-008:
- CRUU9-009:
- CRUU9-010:
- CRUU9-011:
- CRUU9-012:
- CRUU9-013:
- CRUU9-014:
- CRUU9-015:
- CRUU9-016:
- CRUU9-017:
- CRUU9-018:
- CRUU9-019:
- CRUU9-020:
- CRUU9-021:
- CRUU9-022:
- CRUU9-023:
- CRUU9-024:
- CRUU9-025:

BUILD
- exact command:
- exit:
- warnings:
- errors:

FOCUSED TESTS
- authority:
- managed tree:
- manifest:
- control ownership:
- durable settings:
- recovery:
- reservation:
- WPF:
- Windows filesystem:
- evidence verifier:

FULL TEST
- total:
- passed:
- failed:
- skipped:
- duration:

FIVE RUNS
- 1:
- 2:
- 3:
- 4:
- 5:

MANDATORY WINDOWS EVIDENCE
- prompts junction:
- recovery junction:
- real case-sensitive NTFS:
- verified deletion:
- stale app lock:
- write-through settings wrapper:

CRASH FIXTURES
- corrupt initial marker:
- partial payload temp:
- capability probe:
- Ready stage:
- settings temp:
- marker unreadable:
- post-clean foreign race:

PUBLISH
- exit:
- PromptHelper.exe:
- LICENSE:
- THIRD_PARTY_NOTICES.md:

RELEASE ICON
- approved SVG present:
- committed ICO:
- SVG->ICO identity:
- ICO->EXE identity:
- strict release gate:

FINAL
- product/code:
- strict release:
```

Never claim PASS without direct command/result evidence.

---

# 51. Definition of done — product/code

```text
[ ] File.Exists removed from migration authority decisions.
[ ] Unreadable marker cannot become Missing.
[ ] prompts/recovery reparse points rejected.
[ ] Managed tree validated at startup/copy/recovery.
[ ] Recovery cannot delete outside physical root.
[ ] Manifest schema v3 implemented.
[ ] Temp grammar exact final+AttemptId+128-bit nonce.
[ ] One path set rejects final/temp/control collisions.
[ ] Retry requires matching source identity.
[ ] Capability probe controls durably owned.
[ ] Initial marker has no unowned staging-temp window.
[ ] Ready staging deterministic and owned.
[ ] settings.json final promotion write-through durable.
[ ] Marker retirement after durable settings only.
[ ] Stale settings temps cleaned under settings lease.
[ ] Retry second inventory rejects unknown/temp/control residue.
[ ] Recovery has one typed error contract.
[ ] New target success has no false directory warning.
[ ] Reservation records only actually-created directories.
[ ] Reservation acquisition cleanup failures surfaced.
[ ] Stale root .app.lock retryable.
[ ] Held root .app.lock blocks.
[ ] Nested control-name files are foreign.
[ ] Physical bootstrap resolved once and threaded consistently.
[ ] ReadyToCommit has explicit terminal gate.
[ ] Unmanifested temp fallback removed.
[ ] Manifest cleanup exception preserves both failures.
[ ] RestartRequired forces shutdown regardless DialogResult.
[ ] Restart notification failure still shuts down.
[ ] Safety tests use exact exceptions.
[ ] Real Windows category uses real Windows operations.
[ ] Missing test evidence fails CI.
[ ] CI runs categories separately.
[ ] Full Release suite passes.
[ ] Five consecutive suites pass.
[ ] Self-contained win-x64 publish passes.
```

---

# 52. Definition of done — release

```text
[ ] Approved PromptHelperLogo.svg supplied.
[ ] PromptHelper.ico generated from exact approved SVG.
[ ] Normalized SVG->ICO frame identity passes at every required size.
[ ] Published EXE embedded icon identity equals committed ICO.
[ ] Explorer icon manually verified.
[ ] Taskbar icon manually verified.
[ ] Alt+Tab icon manually verified.
[ ] Window icon manually verified.
```

Until approved SVG exists:

```text
STRICT RELEASE = BLOCKED
```

---

# 53. Copy-ready maximal weak-model implementation prompt

```text
ROLE
You are a weak implementation AI for Prompt Helper.
You are not designing architecture. CRUU9 defines the architecture.

AUDITED BASELINE
be1da4fa49916a102616f82a6c74f5601ab5d2d6

PRIMARY INPUT
cruu9.md

GOAL
Implement CRUU9-001 through CRUU9-024 exactly.
CRUU9-025 remains BLOCKED unless the real approved product logo is supplied.

IF HEAD IS NEWER
Do not reset. Compare the newer code to the audited baseline, preserve equivalent/stronger fixes, and implement only missing CRUU9 behavior.

MANDATORY PHASE ORDER
0 baseline
1 strict authority file operations
2 managed physical tree containment
3 manifest schema v3 authority
4 migration control ownership + manifest staging
5 crash-durable settings pointer
6 recovery terminal-state rewrite
7 reservation ownership/commit semantics
8 remove unmanifested escape hatches
9 unconditional postcommit shutdown
10 tests + CI evidence
11 exact release identity tooling
12 full build/test/stress/publish
13 final source audit

NON-NEGOTIABLE SAFETY RULES
- Do not use File.Exists to decide whether a migration authority/control file is absent.
- Missing and unreadable are different states.
- prompts/recovery may not be reparse points.
- No migration/recovery operation may escape the bound physical root.
- Every payload temp must encode exact final name, AttemptId and 32 hex nonce chars.
- One unified owned-path set must reject final/temp/control collisions.
- Retry cleanup requires manifest source physical root == current active source physical root.
- Every precommit control file that can survive a crash must have durable ownership.
- Do not create hidden random capability probe paths during manifested migration.
- Initial Copying marker must not depend on an unowned staging temp.
- Ready replacement staging must be deterministic/owned.
- settings.json final pointer promotion must be write-through durable before marker retirement.
- Stale settings temps use strict reserved grammar and are cleaned only under settings lease.
- Marker is deleted only after a second full inventory proves baseline restored.
- New target directory ownership becomes committed after settings commit and is never treated as cleanup residue.
- Record only directories this process actually created.
- Root .app.lock presence is not active lock ownership.
- Resolve physical bootstrap once; never compare physical target to lexical bootstrap.
- Root control names are controls only at exact root-relative paths.
- ReadyToCommit requires an explicit final invariant gate.
- Remove nullable/unmanifested temp fallback from CopySnapshotToTarget.
- Preserve original failure and cleanup failure separately.
- RestartRequired=true always forces process shutdown regardless DialogResult.
- Real Windows tests execute real Windows operations.
- Missing mandatory TRX sentinel fails CI.
- Do not fabricate PromptHelperLogo.svg.

TEST RULES
- Direct negative and positive tests for every CRUU9 finding.
- Exact exception types; no Assert.Throws<Exception> for safety tests.
- Byte-preservation assertions for source/bootstrap/foreign data.
- Fault injection callbacks, not sleeps.
- Mandatory categories run separately.
- Full Release suite five consecutive times.
- Self-contained win-x64 publish.

FINAL OUTPUT
Return the exact CRUU9 IMPLEMENTATION EVIDENCE template from cruu9.md.
Never claim any build/test/publish/manual check passed unless it actually ran.
```

---

# 54. Final audit conclusion

At:

```text
be1da4fa49916a102616f82a6c74f5601ab5d2d6
```

Prompt Helper is materially safer than at the CRUU7 baseline, but CRUU8 has not yet closed every terminal state.

The final CRUU9 repair is not “add more checks.” It is:

```text
PHYSICAL ROOT AUTHORITY
+
PHYSICAL MANAGED-CHILD AUTHORITY
+
STRICT PRESENT/MISSING/UNREADABLE STATES
+
COMPLETE PAYLOAD + CONTROL OWNERSHIP
+
DURABLE SETTINGS POINTER
+
TERMINAL RECOVERY VERIFICATION
+
EXECUTED TEST EVIDENCE
```

The implementation model must repeatedly ask:

```text
If the process dies on the next line, is every surviving path either:
  durably final,
  durably owned,
  strictly absent,
  or explicitly foreign and untouched?
```

For every managed path it must also ask:

```text
Is this only a lexical descendant, or have I proved that its physical
behavior remains inside the bound physical data tree?
```

CRUU9 is the implementation authority for the next repair pass.

---

# 55. FILE-BY-FILE IMPLEMENTATION MAP FOR THE WEAK MODEL

This section is mandatory. The implementer should use it as the edit checklist.

## `App.xaml.cs`

Must change:

```text
- resolve physical bootstrap root once;
- construct DataRootRuntimeContext;
- acquire app lock;
- validate managed tree;
- call migration startup finalization unconditionally rather than behind File.Exists;
- do not show MainWindow until marker finalization has definitely succeeded;
- validate managed tree again before repositories are exposed.
```

Expected final conceptual flow:

```csharp
var runtime = DataRootRuntimeContext.Create(...);
var paths = new AppPaths(runtime.ActivePhysicalRoot);

_appLock = AppInstanceLock.TryAcquire(paths.LockPath);
if (_appLock is null)
{
    ...
    return;
}

var tree = new ManagedTreeTopologyValidator(physicalResolver);
tree.ValidateManagedTree(runtime.ActivePhysicalRoot);

var recovery = new MigrationRecoveryService(...);
recovery.FinalizeCommittedStartup(
    new MigrationRecoveryContext(
        runtime.ActivePhysicalRoot,
        runtime.BootstrapPhysicalRoot,
        ExpectedSourcePhysicalRoot: null));

paths.EnsureDataDirectories();
tree.ValidateManagedTree(runtime.ActivePhysicalRoot);

// Only now load library and show UI.
```

Do not keep the old outer `File.Exists(paths.MigrationMarkerPath)` gate.

## `MainWindow.xaml.cs`

Must change:

```text
- forced shutdown depends on RestartRequired only;
- notification MessageBox is inside try/finally;
- finally always calls RequestShutdown.
```

## `AppSettingsRepository.cs`

Must change:

```text
- inject durable settings writer;
- clean strict settings temps while settings lease held;
- write authoritative primary through durable writer;
- keep dual-file CAS semantics;
- no public Core bypass;
- backup warning semantics preserved.
```

## `AtomicTextWriter.cs`

Do not necessarily replace globally if not needed.

But:

```text
- settings primary must not depend on its weaker final promotion;
- if generic writer remains for prompt/library writes, document that it is not the transition-pointer writer.
```

## `DataFolderTransitionCoordinator.cs`

Must change:

```text
- construct DataRootRuntimeContext;
- use ManifestBuilder, not hand-built temp strings;
- pass physical bootstrap root;
- retry recovery receives active source physical root;
- managed-tree validation/lease before copy and recovery;
- initial Copying marker uses direct durable final-path creation;
- capability validator receives manifest-declared probe plan;
- Ready gate before phase update;
- settings durable return is the point of no return;
- call reservation.CommitRootOwnership after commit;
- postcommit warning only after point of no return.
```

## `DataFolderMigrationService.cs`

Must change:

```text
- remove optional declaredTempMap fallback;
- require manifest object;
- validate snapshot <-> manifest one-to-one mapping before target mutation;
- validate managed child tree before write;
- use strict authority reads for source/target control where relevant;
- unit helper constructs a real manifest or is removed.
```

## `MigrationAttemptManifest.cs`

Must change:

```text
- schema 3;
- add ControlArtifacts;
- remove TargetIsBootstrapRoot;
- keep Artifact TempRelativePath;
```

## `MigrationManifestRepository.cs`

Must change:

```text
- no File.Exists authority gate;
- strict UTF-8;
- strict duplicate property detection;
- temp grammar validation;
- one unified owned path set;
- validate controls;
- initial marker direct writer;
- deterministic Ready staging path;
- preserve original+cleanup exception.
```

## `MigrationRecoveryService.cs`

Must change:

```text
- source identity required for retry;
- strict marker presence/read;
- managed-tree validation;
- exact root-relative control handling;
- manifest controls recognized;
- no wildcard temp cleanup;
- second full terminal inventory;
- marker delete last;
- one typed failure contract.
```

## `EmptyTargetBaselineInspector.cs`

Must change:

```text
- exact root .app.lock is allowed control;
- nested .app.lock is not allowed;
- exact bootstrap controls use PHYSICAL bootstrap identity;
- prompts/recovery must be empty ordinary non-reparse directories;
- strict unknown-entry reporting.
```

## `TargetRootReservation.cs`

Must change:

```text
- native per-component creation ownership;
- cleanup failures on acquisition surfaced;
- CommitRootOwnership state;
- committed target directory chain never cleaned.
```

## `IVerifiedArtifactDeleter.cs`

Must change:

```text
- remove File.Exists pre-check;
- distinguish native not-found from access denied;
- constrain opened handle final physical path to bound root;
- hash and delete same file handle identity.
```

## `DataRootCapabilityValidator.cs`

Must change:

```text
- empty manifested migration consumes predeclared probe plan;
- no hidden random probe names for manifested transition;
- current existing-library behavior remains role-aware;
- shared schema compatibility authority retained.
```

## `VerifyTestEvidence.ps1`

Must change:

```text
- missing required test => throw/fail;
- exact test-name matching;
- failed/skipped/not-executed required test => fail;
- support caller-supplied required test names.
```

## `.github/workflows/windows-ci.yml`

Must change:

```text
- separate CrashRecovery run;
- separate WpfIntegration run;
- separate WindowsFilesystemIntegration run;
- invoke evidence verifier for each;
- full suite still runs;
- stress five-run option retained;
- publish retained;
- strict icon gate remains conditional on real asset.
```

---

# 56. COPY-READY MANIFEST V3 VALIDATION CORE

The weak model should use this structure rather than inventing an alternative validation algorithm.

```csharp
private static void ValidateManifestInvariants(
    MigrationAttemptManifest manifest,
    string markerPath)
{
    if (manifest.SchemaVersion !=
        MigrationAttemptManifest.CurrentSchemaVersion)
    {
        throw new InvalidDataException(
            $"Unsupported migration manifest schema " +
            $"{manifest.SchemaVersion} at '{markerPath}'.");
    }

    if (manifest.AttemptId == Guid.Empty)
    {
        throw new InvalidDataException(
            "Migration AttemptId cannot be empty.");
    }

    RequireFullyQualified(
        manifest.SourcePhysicalRoot,
        "SourcePhysicalRoot");

    RequireFullyQualified(
        manifest.TargetPhysicalRoot,
        "TargetPhysicalRoot");

    if (PathIdentity.Equals(
            manifest.SourcePhysicalRoot,
            manifest.TargetPhysicalRoot))
    {
        throw new InvalidDataException(
            "Migration source and target roots must differ.");
    }

    ValidateSha256Hex(
        manifest.SourceLibrarySha256Hex,
        "SourceLibrarySha256Hex");

    if (!Enum.IsDefined(manifest.Phase))
    {
        throw new InvalidDataException(
            $"Undefined migration phase '{manifest.Phase}'.");
    }

    if (manifest.Artifacts is null ||
        manifest.Artifacts.Count == 0)
    {
        throw new InvalidDataException(
            "Migration manifest requires payload artifacts.");
    }

    if (manifest.ControlArtifacts is null)
    {
        throw new InvalidDataException(
            "Migration manifest ControlArtifacts cannot be null.");
    }

    var allOwned =
        new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

    int primaryCount = 0;
    int backupCount = 0;

    foreach (MigrationManifestArtifact artifact
             in manifest.Artifacts)
    {
        if (!Enum.IsDefined(artifact.Role))
        {
            throw new InvalidDataException(
                $"Undefined payload role '{artifact.Role}'.");
        }

        ValidateSha256Hex(
            artifact.Sha256Hex,
            $"SHA-256 for {artifact.RelativePath}");

        if (artifact.Length < 0)
        {
            throw new InvalidDataException(
                $"Negative artifact length: " +
                $"'{artifact.RelativePath}'.");
        }

        string finalFull =
            ResolveManifestArtifactPath(
                manifest.TargetPhysicalRoot,
                artifact.RelativePath);

        string tempFull =
            ResolveManifestArtifactPath(
                manifest.TargetPhysicalRoot,
                artifact.TempRelativePath);

        ValidateTempPath(
            manifest.AttemptId,
            artifact.RelativePath,
            artifact.TempRelativePath);

        AddUniqueOwnedPath(
            allOwned,
            finalFull,
            artifact.RelativePath);

        AddUniqueOwnedPath(
            allOwned,
            tempFull,
            artifact.TempRelativePath);

        ValidateRolePath(artifact);

        if (artifact.Role ==
            MigrationPayloadRole.PrimaryMetadata)
        {
            primaryCount++;

            if (!artifact.RelativePath.Equals(
                    "library.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "PrimaryMetadata must be library.json.");
            }

            if (!artifact.Sha256Hex.Equals(
                    manifest.SourceLibrarySha256Hex,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Primary library hash must equal " +
                    "SourceLibrarySha256Hex.");
            }
        }

        if (artifact.Role ==
            MigrationPayloadRole.SafetyBackup)
        {
            backupCount++;
        }
    }

    if (primaryCount != 1)
    {
        throw new InvalidDataException(
            $"Exactly one PrimaryMetadata artifact is required; " +
            $"found {primaryCount}.");
    }

    if (backupCount > 1)
    {
        throw new InvalidDataException(
            $"At most one SafetyBackup artifact is allowed; " +
            $"found {backupCount}.");
    }

    foreach (MigrationControlArtifact control
             in manifest.ControlArtifacts)
    {
        if (!Enum.IsDefined(control.Kind))
        {
            throw new InvalidDataException(
                $"Undefined control kind '{control.Kind}'.");
        }

        string full = ResolveControlPath(
            manifest,
            control);

        AddUniqueOwnedPath(
            allOwned,
            full,
            control.RelativePath);

        ValidateControlGrammar(
            manifest.AttemptId,
            control);
    }
}

private static void AddUniqueOwnedPath(
    HashSet<string> allOwned,
    string fullPath,
    string displayPath)
{
    if (!allOwned.Add(fullPath))
    {
        throw new InvalidDataException(
            $"Migration ownership path collision: " +
            $"'{displayPath}'.");
    }
}
```

The implementation must not split payload final/temp/control uniqueness into independent sets.

---

# 57. COPY-READY CONTROL ARTIFACT GRAMMAR

Recommended exact grammar:

```text
ManifestPhaseStaging:
.prompthelper-migration.stage-<attemptIdN>.tmp

Root CapabilityProbeDirectory:
.prompthelper-write-probe-<attemptIdN>-root

Root CapabilityProbeFile:
.prompthelper-write-probe-<attemptIdN>-root\probe-current.txt
.prompthelper-write-probe-<attemptIdN>-root\probe-replacement.tmp

Prompts CapabilityProbeDirectory:
prompts\.prompthelper-write-probe-<attemptIdN>-prompts

Prompts CapabilityProbeFile:
prompts\.prompthelper-write-probe-<attemptIdN>-prompts\probe-current.txt
prompts\.prompthelper-write-probe-<attemptIdN>-prompts\probe-replacement.tmp
```

No other `.prompthelper-write-probe-*` path is auto-owned.

Do not use wildcard deletion.

---

# 58. COPY-READY RETRY RECOVERY SKELETON

```csharp
public void RecoverForRetry(
    MigrationRecoveryContext context)
{
    ArgumentNullException.ThrowIfNull(context);

    string markerPath = Path.Combine(
        context.TargetPhysicalRoot,
        ".prompthelper-migration.json");

    MigrationAttemptManifest? manifest;

    try
    {
        manifest = _manifestRepo.TryReadStrict(markerPath);
    }
    catch (Exception ex)
    {
        throw Wrap(
            context,
            "ReadManifest",
            ex);
    }

    if (manifest is null)
    {
        return;
    }

    RequireTargetIdentity(
        context,
        manifest);

    RequireRetrySourceIdentity(
        context,
        manifest);

    _treeValidator.ValidateManagedTree(
        context.TargetPhysicalRoot);

    using ManagedTreeLease treeLease =
        ManagedTreeLease.Acquire(
            context.TargetPhysicalRoot);

    TargetRecoveryInventory before =
        BuildInventory(
            context,
            manifest);

    if (before.HasUnknownEntries)
    {
        throw RecoveryForeignData(
            context,
            before.UnknownEntries);
    }

    DeleteDeclaredControlsForRetry(
        context,
        manifest);

    DeleteDeclaredPayloadTemps(
        context,
        manifest);

    foreach (MigrationManifestArtifact artifact
             in manifest.Artifacts)
    {
        string finalPath =
            MigrationManifestRepository
                .ResolveManifestArtifactPath(
                    context.TargetPhysicalRoot,
                    artifact.RelativePath);

        _verifiedDeleter.VerifyAndDelete(
            context.TargetPhysicalRoot,
            finalPath,
            artifact.Length,
            artifact.Sha256Hex);
    }

    CleanupAttemptDirectories(context);

    TargetRecoveryInventory after =
        BuildInventory(
            context,
            manifest);

    RecoveryBaselineVerifier.AssertRestored(
        context.TargetPhysicalRoot,
        after,
        _authorityOps);

    _manifestRepo.DeleteStrict(markerPath);

    if (_authorityOps.GetPresenceStrict(markerPath) !=
        StrictFilePresence.Missing)
    {
        throw new MigrationRecoveryException(
            context.TargetPhysicalRoot,
            "RetireManifest",
            new IOException(
                "Migration marker still exists after deletion."));
    }
}
```

Important:

```text
marker deletion is last
source identity is checked before deletion
managed tree is validated before deletion
second inventory is mandatory
```

---

# 59. COPY-READY COMMITTED STARTUP FINALIZATION SKELETON

```csharp
public void FinalizeCommittedStartup(
    MigrationRecoveryContext context)
{
    string markerPath = Path.Combine(
        context.TargetPhysicalRoot,
        ".prompthelper-migration.json");

    MigrationAttemptManifest? manifest =
        _manifestRepo.TryReadStrict(markerPath);

    if (manifest is null)
    {
        return;
    }

    if (manifest.Phase !=
        MigrationManifestPhase.ReadyToCommit)
    {
        throw new MigrationRecoveryException(
            context.TargetPhysicalRoot,
            "ValidateCommittedPhase",
            new InvalidDataException(
                $"Configured target contains incomplete " +
                $"migration phase '{manifest.Phase}'."));
    }

    RequireTargetIdentity(context, manifest);
    _treeValidator.ValidateManagedTree(
        context.TargetPhysicalRoot);

    using ManagedTreeLease lease =
        ManagedTreeLease.Acquire(
            context.TargetPhysicalRoot);

    AssertNoPayloadTemps(
        context,
        manifest);

    AssertNoEphemeralControls(
        context,
        manifest);

    AssertEveryFinalMatches(
        context,
        manifest);

    TargetRecoveryInventory inventory =
        BuildInventory(
            context,
            manifest);

    if (inventory.HasUnknownEntries)
    {
        throw RecoveryForeignData(
            context,
            inventory.UnknownEntries);
    }

    _manifestRepo.DeleteStrict(markerPath);

    if (_authorityOps.GetPresenceStrict(markerPath) !=
        StrictFilePresence.Missing)
    {
        throw new MigrationRecoveryException(
            context.TargetPhysicalRoot,
            "RetireCommittedMarker",
            new IOException(
                "Ready migration marker could not be retired."));
    }
}
```

No MainWindow until this method returns.

---

# 60. COPY-READY RESERVATION DIRECTORY CREATION OUTLINE

```csharp
private static IReadOnlyList<string>
    CreateMissingDirectoryChainOwned(
        string root,
        IReservationFileOps ops)
{
    List<string> candidates =
        GetMissingDirectoryChain(root, ops)
            .ToList();

    var owned = new List<string>();

    try
    {
        foreach (string candidate in candidates)
        {
            DirectoryCreateOutcome result =
                ops.TryCreateDirectoryOwned(candidate);

            if (result ==
                DirectoryCreateOutcome.CreatedByCaller)
            {
                owned.Add(candidate);
            }
        }

        return owned;
    }
    catch (Exception original)
    {
        var failures =
            new List<MigrationRollbackFailure>();

        CleanupCreatedDirectories(
            owned,
            ops,
            failures);

        if (failures.Count > 0)
        {
            throw new TargetRootReservationAcquireException(
                root,
                original,
                failures);
        }

        throw;
    }
}
```

Never record an `AlreadyExists` directory as owned.

---

# 61. COPY-READY `VerifyTestEvidence.ps1` CORE

The final script must **fail**, not warn, when mandatory evidence is missing.

```powershell
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$TrxPath,

    [string[]]$RequiredTests = @()
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $TrxPath)) {
    throw "TRX file not found: $TrxPath"
}

[xml]$trx = Get-Content $TrxPath -Raw

$ns = [System.Xml.XmlNamespaceManager]::new($trx.NameTable)
$ns.AddNamespace(
    "t",
    "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")

$results = $trx.SelectNodes("//t:UnitTestResult", $ns)
if ($results.Count -eq 0) {
    $results = $trx.SelectNodes("//UnitTestResult")
}

if ($results.Count -eq 0) {
    throw "TRX contains no executed test results."
}

$byName = @{}
foreach ($result in $results) {
    $byName[[string]$result.testName] = $result
}

foreach ($required in $RequiredTests) {
    if (-not $byName.ContainsKey($required)) {
        throw "Required test was not executed: $required"
    }

    $outcome = [string]$byName[$required].outcome
    if ($outcome -ne "Passed") {
        throw (
            "Required test did not pass: " +
            "$required outcome=$outcome"
        )
    }
}

Write-Host (
    "Required test evidence verified: " +
    "$($RequiredTests.Count) sentinel(s)."
)
```

Keep the existing whole-run counters check too.

---

# 62. CRASH FIXTURE BUILDER FOR TESTS

Create a reusable test helper instead of hand-writing inconsistent manifests.

```csharp
internal sealed class MigrationCrashFixtureBuilder
{
    private readonly string _sourceRoot;
    private readonly string _targetRoot;
    private readonly Guid _attemptId;
    private readonly MigrationAttemptManifest _manifest;

    public MigrationCrashFixtureBuilder(
        string sourceRoot,
        string targetRoot,
        MigrationPayloadSnapshot snapshot)
    {
        _sourceRoot = sourceRoot;
        _targetRoot = targetRoot;
        _attemptId = Guid.NewGuid();

        _manifest = MigrationManifestBuilder.BuildCopying(
            sourceRoot,
            targetRoot,
            snapshot,
            _attemptId);
    }

    public MigrationCrashFixtureBuilder WithPhase(
        MigrationManifestPhase phase)
    {
        _manifest.Phase = phase;
        return this;
    }

    public MigrationCrashFixtureBuilder WithPartialTemp(
        string finalRelativePath,
        byte[] bytes)
    {
        MigrationManifestArtifact artifact =
            _manifest.Artifacts.Single(
                x => x.RelativePath.Equals(
                    finalRelativePath,
                    StringComparison.OrdinalIgnoreCase));

        string path =
            MigrationManifestRepository.ResolveManifestArtifactPath(
                _targetRoot,
                artifact.TempRelativePath);

        Directory.CreateDirectory(
            Path.GetDirectoryName(path)!);

        File.WriteAllBytes(path, bytes);
        return this;
    }

    public MigrationCrashFixtureBuilder WithForeignFile(
        string relativePath,
        byte[] bytes)
    {
        string path = Path.Combine(
            _targetRoot,
            relativePath);

        Directory.CreateDirectory(
            Path.GetDirectoryName(path)!);

        File.WriteAllBytes(path, bytes);
        return this;
    }

    public MigrationAttemptManifest Manifest => _manifest;
}
```

Use this helper so test manifests use the same valid production grammar except for the exact field under test.

---

# 63. REQUIRED NEGATIVE TESTS

The weak model must not stop at positive cases.

Mandatory negative states:

```text
unreadable marker
unreadable payload final
prompts junction to outside
recovery junction to outside
temp path unrelated to final
temp with wrong AttemptId
temp with too-short nonce
temp/final cross collision
different-source interrupted marker
probe residue from same attempt
probe residue from other attempt
Ready stage residue
settings write-through failure
stale settings temp cannot be deleted
second inventory gains foreign file
remaining temp after cleanup
stale root lock
held root lock
nested root-control name
Ready gate sees changed final
Ready gate sees child junction
postcommit MessageBox throws
missing required TRX sentinel
wrong EXE icon fixture
invalid UTF-8 manifest
duplicate critical manifest property
```

For each destructive/recovery negative test, assert foreign/source/bootstrap bytes remain exact.

---

# 64. REQUIRED POSITIVE TESTS

Mandatory success states:

```text
normal empty custom migration
normal existing-library switch
custom -> exact physical default bootstrap migration
retry same-source Copying marker with partial temp
retry same-source Copying marker with partial final
committed startup Ready marker
stale root app lock then successful reservation
normal non-reparse prompts/recovery tree
read-only safety backup warning semantics
successful new target with zero false cleanup warnings
normal shutdown after committed settings transition
```

---

# 65. FINAL WEAK-AI SELF-REVIEW QUESTIONS

Before claiming completion, answer each with YES and cite the code/test that proves it:

```text
1. Can an unreadable migration marker ever be interpreted as absent?
2. Can prompts/recovery physically resolve outside the bound root?
3. Can a manifest temp point at a user file that does not match production temp grammar?
4. Can any temp/control/final path collide with another owned path?
5. Can retry delete an interrupted migration belonging to another source?
6. Can a crash during capability probing leave an unowned path?
7. Can a crash during manifest phase replacement leave an unknown stage path?
8. Is settings.json final-name promotion write-through durable before marker retirement?
9. Can stale settings staging files block default-root recovery without a controlled cleanup path?
10. Does retry run a second complete inventory before deleting marker?
11. Can a recovery filesystem exception escape without the typed recovery contract?
12. Does successful new-root Release avoid trying to delete the committed root?
13. Does directory ownership rely only on actual native creation success?
14. Is stale .app.lock treated differently from a held lock?
15. Is exact bootstrap comparison physical-to-physical?
16. Can nested .app.lock or nested manifest filenames be silently ignored?
17. Is ReadyToCommit preceded by a terminal invariant gate?
18. Is there any copy API that can generate an unmanifested temp?
19. If manifest write and cleanup both fail, are both causes preserved?
20. Does RestartRequired force shutdown even if DialogResult is null/false?
21. Do CRUU9 test names correspond to operations actually executed?
22. Does CI fail when a required sentinel does not run?
23. Does strict icon verification prove identity, not presence?
24. Are duplicate critical JSON fields and malformed UTF-8 rejected?
25. Is the real product logo still correctly reported BLOCKED if absent?
```

Any NO means CRUU9 is not complete.

---

# 66. FINAL ACCEPTANCE RULE

The implementation model may state:

```text
CRUU9 PRODUCT/CODE = PASS
```

only when all CRUU9-001 through CRUU9-024 are fixed **and** direct Windows evidence is complete.

It may state:

```text
STRICT RELEASE = PASS
```

only when CRUU9-025 is also resolved with the real approved logo and exact identity verification.

If the toolchain cannot execute a required Windows check, use:

```text
NOT_RUN / BLOCKED_EVIDENCE
```

not PASS.
