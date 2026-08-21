# CRUU7 — Post-CRUU6 Paranoid Transaction, Crash-Recovery & Verification Audit

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited commit:** `8f8aeca5a389fdba689a30e54df542399b4fdd99`  
**Previous audit chain:** `cruu1.md` → `cruu2.md` → `cruu3.md` → `cruu4.md` → `cruu5.md` → `cruu6.md`  
**Purpose:** independently re-audit the complete CRUU6 implementation, find defects in the exact hardened flows, and provide a deterministic, weak-model-ready repair plan with code architecture, failure semantics, fault injection, Windows integration tests, release gates, and exact definition of done.

---

# 1. Executive result

CRUU6 **substantially landed**. The repository now contains the intended dual-file settings precondition, settings mutation lease, post-recovery transition snapshot, physical-root consistency checks, target revalidation checkpoints, target reservation, migration ownership journal, temporary-file promotion protocol, target fingerprints, existing-file capability checks, explicit future-schema dialog catches, unavailable-root startup classification, and real NTFS junction integration tests.

A fresh audit of the *composed* flows nevertheless found another layer of defects. These are mostly transaction-boundary, interruption, and evidence problems rather than missing CRUU6 checkboxes.

The correct status is:

```text
CRUU6 STRUCTURAL IMPLEMENTATION        = SUBSTANTIALLY LANDED
CURRENT AUDITED COMMIT                 = 8f8aeca5a389fdba689a30e54df542399b4fdd99
SOURCE-LEVEL POST-CRUU6 AUDIT          = COMPLETED
NEW CRUU7 FINDINGS                     = OPEN
INDEPENDENT WINDOWS/.NET EXECUTION     = NOT AVAILABLE IN THIS AUDIT ENVIRONMENT
GITHUB COMBINED STATUS EVIDENCE        = NO STATUS ENTRIES RETURNED
FINAL ZERO-DEFECT ACCEPTANCE           = NOT GRANTED
STRICT RELEASE ACCEPTANCE              = BLOCKED BY AUTHORITATIVE LOGO ASSET
```

The key conclusion is:

```text
CRUU6 added safety CHECKPOINTS.
CRUU7 must convert those checkpoints into one actual transaction protocol.
```

A physical path that was checked is not automatically the path later written. A stream flush is not necessarily a durable disk flush. A `try/catch` rollback does not survive process termination. A cleanup result does not protect anything if callers discard it. A test name is not evidence when the injected fault never exercises the claimed branch.

---

# 2. Evidence boundary and external semantics

This report is based on direct inspection of the pushed source at the commit above. It does **not** claim to have independently run WPF/.NET/Windows tests in this audit environment.

The GitHub combined-status endpoint returned no status entries for this commit. This is not proof that CI did not run; it only means this audit did not obtain usable CI evidence through that endpoint.

For durability and Windows path semantics, the repair plan was cross-checked against Microsoft documentation:

```text
FileStream.Flush(Boolean)
https://learn.microsoft.com/dotnet/api/system.io.filestream.flush

MoveFileEx / MOVEFILE_WRITE_THROUGH
https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-movefileexw

Windows per-directory case sensitivity
https://learn.microsoft.com/windows/wsl/case-sensitivity

FILE_CASE_SENSITIVE_INFORMATION
https://learn.microsoft.com/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_case_sensitive_information

FILE_INFO_BY_HANDLE_CLASS
https://learn.microsoft.com/windows/win32/api/minwinbase/ne-minwinbase-file_info_by_handle_class
```

Microsoft documents that `FileStream.Flush(true)` flushes intermediate file buffers for disk persistence, while `MOVEFILE_WRITE_THROUGH` does not return until the move is completed on disk. Windows also supports case-sensitive directories, including `FILE_CS_FLAG_CASE_SENSITIVE_DIR = 0x00000001`.

---

# 3. CRUU7 finding register

| ID | Severity | Finding |
|---|---|---|
| CRUU7-001 | HIGH | Physical target identity is revalidated, but all target I/O still uses the mutable lexical path; a junction/reparse target can redirect actual writes between checkpoints |
| CRUU7-002 | HIGH | Migrated payload files use ordinary `Flush()` rather than durable flush before the settings pointer can be committed |
| CRUU7-003 | MEDIUM-HIGH | Migration snapshot coverage is narrower than copy coverage: backup, orphan prompts, and recovery artifacts are copied without coherent snapshot/final verification |
| CRUU7-004 | HIGH | There is no durable interrupted-migration manifest/state machine; crash/kill can strand a partial target or turn a stale completed copy into a misleading “existing library” on retry |
| CRUU7-005 | MEDIUM-HIGH | `LoadOrRecoverCore()`, `SaveCore()`, and precondition-core helpers are public, exposing a direct bypass around the new settings mutation lease |
| CRUU7-006 | MEDIUM-HIGH | Target snapshot stability rechecks metadata only; prompt bodies are hashed once, so a mixed body snapshot can still be accepted |
| CRUU7-007 | MEDIUM | Unreadable or unstable primary target states can be misclassified as “corrupt primary with valid backup” |
| CRUU7-008 | MEDIUM-HIGH | Reservation cleanup results are discarded on success and several early-exception paths; `Release()` also loses prior failures on repeated calls |
| CRUU7-009 | HIGH | A post-settings-commit reservation release exception can leave settings switched while UI reports failure and continues running the old source |
| CRUU7-010 | MEDIUM-HIGH | Capability probe uses opaque `AtomicTextWriter` temporary files that are not fully journaled; current “cleanup failure” tests do not inject cleanup failure |
| CRUU7-011 | MEDIUM | `ConfiguredDataFolderUnavailableException` can escape `SettingsDialog` during an open-session transition and reach the fatal WPF handler |
| CRUU7-012 | MEDIUM | Existing-target capability validation hard-fails backup writeability although repository semantics deliberately treat backup-sync failure/future preservation as warning-level |
| CRUU7-013 | LOW-MEDIUM | Warning composition drops settings snapshot warnings and can discard one of multiple simultaneous safety warnings |
| CRUU7-014 | LOW-MEDIUM | Settings lease retries every `IOException` and may block UI for ~5 seconds; the related test claims a 100 ms timeout but actually uses the default timeout |
| CRUU7-015 | MEDIUM verification gap | Several CRUU6 tests do not prove the behavior in their names: actual dialog handling, cleanup reporting, body stability, between-check alias redirection, and postcommit invariants remain unverified |
| CRUU7-016 | LOW-MEDIUM | `PathIdentity` uses unconditional `OrdinalIgnoreCase`, which is unsound inside Windows directories explicitly configured as case-sensitive |
| CRUU7-017 | MEDIUM release verification gap | Strict icon verification proves only that an EXE has an icon group, not that embedded icon payloads match the current committed ICO or that ICO matches current SVG |
| CRUU7-018 | RELEASE BLOCKER | The real approved `PromptHelperLogo.svg` / generated ICO remains absent |

---

# 4. Repair priority

Implement in this order:

```text
PHASE A  CRUU7-001       bind target I/O to one physical root
PHASE B  CRUU7-002       durable payload write/promotion
PHASE C  CRUU7-003       complete payload snapshot
PHASE D  CRUU7-004       interrupted-migration manifest/recovery
PHASE E  CRUU7-005       close settings lease bypass API
PHASE F  CRUU7-006/007   stable target snapshot + typed target states
PHASE G  CRUU7-008/009   cleanup truth + postcommit point of no return
PHASE H  CRUU7-010/012   explicit capability probe + backup warning policy
PHASE I  CRUU7-011/013   controlled dialog error + warning aggregation
PHASE J  CRUU7-014/015   lease behavior + truthful tests
PHASE K  CRUU7-016       case-sensitive directory fail-closed policy
PHASE L  CRUU7-017       exact icon identity gate
PHASE M  CRUU7-018       real asset only when supplied
```

Do not change tests first merely to accommodate current behavior. Repair storage semantics first, then make tests prove those semantics.

---

# 5. Locked product and architecture decisions

Preserve all of these:

1. WPF/.NET 10 remains the product stack.
2. Windows remains the supported product platform.
3. Prompt bodies remain local Markdown files under `prompts/`.
4. `library.json` remains primary metadata.
5. `library.backup.json` remains safety backup.
6. Bootstrap settings remain under `%LOCALAPPDATA%\PromptHelper`.
7. `settings.json` remains authoritative settings primary.
8. `settings.backup.json` remains settings safety backup.
9. Custom data roots remain supported.
10. Empty target means copy the current active library.
11. Existing valid target means explicit user-confirmed switch; no merge.
12. Source data is never automatically deleted.
13. Successful root change requires process shutdown/restart.
14. Future-schema primary files are never downgraded.
15. Future-schema backup files are preserved.
16. Unavailable configured custom root never falls back to default root.
17. Junction/symlink physical-path safety remains mandatory.
18. One active editor holds the active root `.app.lock`.
19. Current settings/library schema versions remain `1`.
20. Existing prompt headline/copy/recent-copy behavior remains unchanged.
21. No cloud sync, telemetry, accounts, database, updater, installer framework, trimming, or single-file publishing is introduced by this repair.
22. Never fabricate the missing logo.

---

# 6. Canonical empty-target transition after CRUU7

The final operation order must be:

```text
1. Acquire coherent settings transition snapshot.
2. Validate settings still identify the active running physical root.
3. Validate candidate topology.
4. Bind candidate lexical path to one physical target root.
5. Capture COMPLETE source payload snapshot.
6. Reserve the BOUND PHYSICAL target root.
7. Re-resolve lexical candidate and prove it still maps to bound target.
8. Detect and safely resolve any interrupted migration state.
9. Create a durable migration-attempt manifest.
10. Copy only files listed in the payload snapshot.
11. Durably flush every owned temp file.
12. Write-through promote every temp file to its final path.
13. Verify complete source file set still equals snapshot.
14. Verify every source file hash/length.
15. Verify every target file hash/length.
16. Durably update manifest phase to ReadyToCommit.
17. Revalidate lexical candidate -> same bound physical target.
18. Compare settings dual-file precondition and atomically commit settings.
19. POINT OF NO RETURN.
20. Cleanup marker/reservation without turning commit into failure.
21. Aggregate warnings.
22. Return Changed=true, RestartRequired=true.
23. MainWindow forces shutdown.
```

The existing-target flow uses the same physical binding and postcommit boundary, but no payload copy manifest unless interrupted-migration residue is already present.

---

# 7. CRUU7-001 — Revalidation is not physical binding

**Severity:** HIGH  
**Files:** `DataFolderTransitionCoordinator.cs`, `DataRootRelationship.cs`, `TargetRootReservation.cs`, `DataFolderMigrationService.cs`, `DataRootCapabilityValidator.cs`

## 7.1 Current defect

CRUU6 added physical revalidation checkpoints. The coordinator still passes `cleanTarget`—the lexical user path—to:

```text
TargetRootReservation.TryAcquire
InspectTarget
ValidateWritable
CopySnapshotToTarget
```

A junction can therefore point to physical target A during a checkpoint, to B during actual file operations, then back to A before the next checkpoint.

The checkpoint succeeds while B may already have received:

```text
.app.lock
probe files
library.json
prompt bodies
recovery artifacts
```

## 7.2 Required invariant

After initial physical resolution:

```text
lexical target = user-facing locator + eventual persisted settings value
physical target = transaction I/O authority
```

No target mutation or lock operation may use the lexical locator after binding.

## 7.3 Required type

```csharp
internal sealed record BoundTargetRoot(
    string LexicalRoot,
    string PhysicalRoot,
    DataRootRelationship InitialRelationship);
```

Bind once:

```csharp
private BoundTargetRoot BindTarget(
    string activeRoot,
    string lexicalTarget,
    string bootstrapRoot)
{
    DataRootRelationship relationship =
        _rootPolicy.ValidateTransition(
            activeRoot,
            lexicalTarget,
            bootstrapRoot);

    if (relationship.SamePhysicalRoot)
    {
        throw new InvalidOperationException(
            "The selected folder resolves to the active library.");
    }

    return new BoundTargetRoot(
        relationship.LexicalTarget,
        relationship.PhysicalTarget,
        relationship);
}
```

## 7.4 Required I/O rule

Use:

```csharp
reservation = TargetRootReservation.TryAcquire(bound.PhysicalRoot);
inspection = _migrationService.InspectTarget(bound.PhysicalRoot);
_capabilityValidator.ValidateWritable(bound.PhysicalRoot, ...);
_migrationService.CopySnapshotToTarget(
    activePhysicalRoot,
    bound.PhysicalRoot,
    snapshot,
    tx);
```

Do not use `bound.LexicalRoot` for those calls.

## 7.5 Revalidate only the locator

Before commit:

```csharp
private void AssertLocatorStillMapsToBoundTarget(
    string activeRoot,
    BoundTargetRoot bound,
    string bootstrapRoot)
{
    DataRootRelationship actual =
        _rootPolicy.ValidateTransition(
            activeRoot,
            bound.LexicalRoot,
            bootstrapRoot);

    if (actual.SamePhysicalRoot ||
        !PathIdentity.Equals(
            actual.PhysicalTarget,
            bound.PhysicalRoot))
    {
        throw new InvalidOperationException(
            "The selected data-folder path changed physical identity while " +
            "the transition was in progress. Nothing was committed.");
    }
}
```

Persist the lexical value only after the operation succeeds.

## 7.6 Required tests

```text
CRUU7_001_Bound_physical_target_is_used_for_all_mutating_io
CRUU7_001_Lexical_alias_flip_to_evil_and_back_writes_nothing_to_evil
CRUU7_001_Reservation_lock_is_created_only_at_bound_physical_target
CRUU7_001_Capability_probe_uses_bound_physical_target
CRUU7_001_Existing_target_inspection_uses_bound_physical_target
```

The key test must not merely make the resolver fail at checkpoint #2. It must simulate an alias that is safe during both checks but points elsewhere during an injected lexical-I/O attempt, then assert the evil directory is byte-for-byte untouched.

---

# 8. CRUU7-002 — Target payload is not durably committed before settings

**Severity:** HIGH  
**Files:** `DataFolderMigrationService.cs`, `IMigrationFileOps.cs`, `DataFolderTransitionCoordinator.cs`

## 8.1 Current defect

The migration copy currently does:

```csharp
srcStream.CopyTo(destStream);
destStream.Flush();
```

The settings writer already uses:

```csharp
stream.Flush(flushToDisk: true);
```

Therefore settings can receive a stronger persistence guarantee than the target data it points to.

## 8.2 Required I/O API

Extend:

```csharp
internal interface IMigrationFileOps
{
    byte[] ReadAllBytes(string path);
    Stream CreateNewFile(string path);
    Stream OpenRead(string path);

    void FlushToDisk(Stream stream);

    void MoveNoOverwriteWriteThrough(
        string source,
        string destination);

    IEnumerable<string> EnumeratePromptFiles(string directory);
}
```

Production durable flush:

```csharp
public void FlushToDisk(Stream stream)
{
    if (stream is not FileStream fs)
    {
        throw new InvalidOperationException(
            "Durable migration flush requires a FileStream.");
    }

    fs.Flush(flushToDisk: true);
}
```

## 8.3 Write-through promotion

Use `MoveFileExW` with:

```csharp
private const uint MOVEFILE_WRITE_THROUGH = 0x00000008;
```

Do **not** enable `MOVEFILE_REPLACE_EXISTING`.

```csharp
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
private static extern bool MoveFileExW(
    string lpExistingFileName,
    string lpNewFileName,
    uint dwFlags);
```

On failure, throw `Win32Exception` with `Marshal.GetLastWin32Error()`.

## 8.4 Required order per file

```text
CreateNew temp
Track temp ownership
Copy bytes
FlushToDisk(temp)
Close handles
MoveNoOverwriteWriteThrough(temp, final)
Promote ownership temp -> final
Hash-verify final
```

Settings commit occurs only after **all** payload files pass this sequence and global verification.

## 8.5 Required tests

```text
CRUU7_002_Every_migrated_file_is_flushed_to_disk_before_promotion
CRUU7_002_Settings_commit_occurs_after_all_durable_promotions
CRUU7_002_Flush_failure_rolls_back_without_settings_change
CRUU7_002_WriteThrough_move_failure_rolls_back_without_settings_change
```

Use a recording file-ops implementation and assert operation index ordering.

---

# 9. CRUU7-003 — Snapshot coverage does not match copy coverage

**Severity:** MEDIUM-HIGH  
**File:** `DataFolderMigrationService.cs`

## 9.1 Current mismatch

The formal source snapshot covers:

```text
library.json
prompt bodies referenced by the active LibraryDocument
```

The migration additionally copies:

```text
library.backup.json if present
all prompts\*.md, including orphan prompt files
all top-level recovery\* files
```

Those extra files can change while the migration is running without the existing snapshot/final verification necessarily detecting it.

## 9.2 Required payload model

```csharp
internal enum MigrationPayloadRole
{
    PrimaryMetadata,
    SafetyBackup,
    PromptBody,
    OrphanPromptBody,
    RecoveryArtifact
}

internal sealed record MigrationPayloadFile(
    string RelativePath,
    MigrationPayloadRole Role,
    long Length,
    byte[] Sha256);

internal sealed record MigrationPayloadSnapshot(
    LibraryDocument ActiveDocument,
    IReadOnlyList<MigrationPayloadFile> Files,
    IReadOnlySet<string> RelativePathSet);
```

## 9.3 Snapshot exact copy set before target mutation

Include:

```text
library.json
library.backup.json if present
every *.md under prompts, top-level only
every top-level file under recovery
```

Exclude:

```text
.app.lock
.settings.lock
initializing.marker
.prompthelper-migration.json
probe artifacts
migration temp artifacts
AtomicTextWriter temp artifacts
```

For every included file capture:

```text
relative path
role
length
SHA-256
```

Parse `ActiveDocument` from the exact snapshotted `library.json` bytes.

## 9.4 Copy only snapshot entries

Never re-enumerate “whatever exists now” as the copy source set.

```csharp
foreach (MigrationPayloadFile item in snapshot.Files)
{
    string source = ResolveUnderRoot(sourceRoot, item.RelativePath);
    string target = ResolveUnderRoot(targetRoot, item.RelativePath);
    CopyPayloadFileDurably(source, target, ...);
}
```

## 9.5 Final source-set verification

After copy:

```text
re-enumerate eligible source payload paths
require exact relative-path-set equality with snapshot
verify hash + length of every source file
verify hash + length of every target file
```

New or removed eligible files abort the transition.

## 9.6 Required tests

```text
CRUU7_003_Backup_change_during_copy_aborts
CRUU7_003_Backup_appearing_during_copy_aborts
CRUU7_003_Backup_disappearing_during_copy_aborts
CRUU7_003_Orphan_prompt_change_during_copy_aborts
CRUU7_003_Orphan_prompt_added_during_copy_aborts
CRUU7_003_Orphan_prompt_removed_during_copy_aborts
CRUU7_003_Recovery_artifact_change_during_copy_aborts
CRUU7_003_Source_file_set_change_aborts
CRUU7_003_Target_hashes_match_every_payload_file
```

---

# 10. CRUU7-004 — No interrupted-migration state machine

**Severity:** HIGH  
**Files:** `AppPaths.cs`, `DataFolderMigrationService.cs`, `DataFolderTransitionCoordinator.cs`, `App.xaml.cs`, new manifest/recovery classes

## 10.1 Why exception rollback is insufficient

Current rollback works for managed exceptions while the process is alive. It cannot guarantee cleanup after:

```text
Task Manager End task
process kill
fail-fast
runtime termination
OS crash
machine power loss
```

A target can therefore remain with:

```text
markerless partial library.json
subset of prompts
all copied files but settings still source
migration temp files
```

## 10.2 Dangerous stale-existing-target sequence

```text
1. Source snapshot A copies fully to target.
2. Process dies before settings commit.
3. User restarts on source.
4. Source becomes B.
5. User selects same target again expecting a move.
6. Target looks like a valid existing library containing stale A.
7. Existing-library flow correctly refuses to copy/merge.
8. User may unknowingly switch to stale A instead of moving B.
```

A durable marker is necessary to distinguish “pre-existing user library” from “unfinished migration attempt.”

## 10.3 Required marker path

```csharp
public string MigrationMarkerPath =>
    Path.Combine(
        RootDirectory,
        ".prompthelper-migration.json");
```

## 10.4 Required manifest

```csharp
internal enum MigrationManifestPhase
{
    Copying,
    ReadyToCommit
}

internal sealed class MigrationAttemptManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = 1;
    public Guid AttemptId { get; set; }

    public string SourcePhysicalRoot { get; set; } = "";
    public string TargetPhysicalRoot { get; set; } = "";
    public string SourceLibrarySha256Hex { get; set; } = "";

    public MigrationManifestPhase Phase { get; set; }

    public List<MigrationManifestArtifact> Artifacts { get; set; } = [];
}

internal sealed class MigrationManifestArtifact
{
    public string RelativePath { get; set; } = "";
    public string Sha256Hex { get; set; } = "";
    public long Length { get; set; }
    public MigrationPayloadRole Role { get; set; }
}
```

## 10.5 Manifest order

```text
Bind physical target
Reserve bound target
Resolve any old interrupted state
Capture source payload
Write Copying manifest atomically
Flush manifest durably
Begin payload finalization
```

Every migration temp name must contain the `AttemptId`.

## 10.6 Target inspection

Check `.prompthelper-migration.json` **before** ordinary `library.json` / backup classification.

Add:

```text
InterruptedMigration
```

to target kind.

A target with:

```text
valid library.json + active migration marker
```

is **not** a normal existing library.

## 10.7 Safe retry cleanup

Under the bound target reservation:

1. Parse manifest strictly.
2. Verify schema supported.
3. Verify manifest target matches bound physical target.
4. Verify source identity when settings still point source.
5. For each listed final artifact:
   - missing => okay;
   - exact hash + length match => eligible for owned cleanup;
   - mismatch => never delete automatically.
6. Delete only temp names carrying the exact AttemptId.
7. Delete manifest-owned empty directories if tracked/known.
8. Delete marker last.
9. Reinspect target.
10. Continue only if truly empty.

If any final file differs, stop with an exact manual-review message and preserve all files.

## 10.8 ReadyToCommit

After every payload file is durable and verified:

```text
manifest.Phase = ReadyToCommit
atomically write marker
Flush(true)
```

Only then commit settings.

## 10.9 Crash after settings commit

On startup, after acquiring the active target app lock:

```text
if settings points this target
and marker is ReadyToCommit
and every payload artifact exactly matches manifest
then:
    clear marker best-effort
    continue startup
else if marker exists and verification fails:
    fail closed
    do not initialize defaults
    show committed-migration-residue error
```

## 10.10 Required fixture tests

Do not kill the test runner. Construct exact disk states for each crash point:

```text
CRUU7_004_Crash_after_manifest_only_is_recoverable
CRUU7_004_Crash_after_primary_only_is_interrupted_not_existing
CRUU7_004_Crash_after_subset_of_prompts_is_interrupted_not_invalid
CRUU7_004_Crash_after_full_copy_before_settings_is_not_existing_library
CRUU7_004_Retry_cleans_only_manifest_owned_exact_files
CRUU7_004_Mismatched_manifest_artifact_is_never_auto_deleted
CRUU7_004_Ready_marker_after_settings_commit_is_cleared_on_startup
CRUU7_004_Ready_marker_with_payload_mismatch_blocks_startup
CRUU7_004_Future_manifest_schema_blocks_without_mutation
CRUU7_004_Traversal_path_in_manifest_is_rejected_without_delete
```

---

# 11. CRUU7-005 — Public settings Core methods bypass the mutation lease

**Severity:** MEDIUM-HIGH  
**File:** `AppSettingsRepository.cs`

Current public methods include core operations intended to execute only inside the lease. This exposes a maintenance-level bypass of the very serialization CRUU6 introduced.

## 11.1 Required public surface

Keep safe public operations only:

```text
LoadOrRecover
Save
LoadForTransitionAndCapturePrecondition
SaveIfUnchanged
GetEffectiveDataRoot(settings)
SettingsPath
BackupPath
```

Make these private:

```text
LoadOrRecoverCore
SaveCore
CaptureWritePreconditionCore
CaptureFileToken
```

If a test truly needs an internal helper, use `internal` plus the existing `InternalsVisibleTo`, never public.

## 11.2 Required reflection test

```csharp
[TestMethod]
public void CRUU7_005_No_public_settings_core_mutation_methods()
{
    MethodInfo[] methods =
        typeof(AppSettingsRepository)
            .GetMethods(
                BindingFlags.Public |
                BindingFlags.Instance);

    Assert.IsFalse(
        methods.Any(m =>
            m.Name.EndsWith(
                "Core",
                StringComparison.Ordinal)));

    Assert.IsFalse(
        methods.Any(m =>
            m.Name is "CaptureFileToken" or
                      "CaptureWritePreconditionCore"));
}
```

Also add a code-path test proving every public mutator acquires the settings lease.

---

# 12. CRUU7-006 — Prompt bodies are not stable across target fingerprint capture

**Severity:** MEDIUM-HIGH  
**File:** `DataFolderMigrationService.cs`

CRUU6 fixed metadata/document hybrid reads by rereading metadata. Prompt bodies are still each hashed only once.

With multiple prompts, the fingerprint can contain body versions that never coexisted.

## 12.1 Required two-pass snapshot

```csharp
internal sealed record TargetContentPass(
    byte[] MetadataBytes,
    LibraryDocument Document,
    IReadOnlyDictionary<Guid, byte[]> PromptHashes);
```

Capture a full pass twice:

```csharp
TargetContentPass first = CaptureTargetContentPass(...);
TargetContentPass second = CaptureTargetContentPass(...);

if (!ContentPassesEqual(first, second))
{
    throw new TargetInspectionUnstableException(
        "Target library changed while being inspected. Retry.");
}
```

Compare:

```text
metadata exact bytes
prompt ID set
hash of every active prompt body
```

Return the second stable pass as authority.

## 12.2 Required equality helper

```csharp
private static bool ContentPassesEqual(
    TargetContentPass left,
    TargetContentPass right)
{
    if (!left.MetadataBytes.AsSpan()
        .SequenceEqual(right.MetadataBytes))
    {
        return false;
    }

    if (left.PromptHashes.Count != right.PromptHashes.Count)
    {
        return false;
    }

    foreach (var pair in left.PromptHashes)
    {
        if (!right.PromptHashes.TryGetValue(pair.Key, out byte[]? other) ||
            !pair.Value.AsSpan().SequenceEqual(other))
        {
            return false;
        }
    }

    return true;
}
```

## 12.3 Required tests

```text
CRUU7_006_Prompt_body_change_between_snapshot_passes_aborts
CRUU7_006_Two_prompt_hybrid_snapshot_is_rejected
CRUU7_006_Stable_target_two_pass_snapshot_succeeds
```


---

# 13. CRUU7-007 — Unreadable and unstable primary target states are misclassified as corrupt

**Severity:** MEDIUM  
**File:** `DataFolderMigrationService.cs`

## 13.1 Current classification collapse

Current target inspection effectively performs:

```csharp
try
{
    primarySnapshot = CaptureTargetContentSnapshot(...);
}
catch (UnsupportedLibrarySchemaException)
{
    ...
}
catch (Exception ex)
{
    primaryEx = ex;
    primarySnapshot = null;
}
```

Later, if a valid backup exists, any failure to obtain a primary snapshot can become:

```text
CorruptPrimaryWithValidBackup
```

That incorrectly conflates:

```text
invalid JSON / invalid schema-1 structure
locked file
permission denied
transient I/O failure
metadata changed during inspection
prompt body changed during inspection
```

The user action differs materially between those states.

## 13.2 Required typed metadata state

```csharp
internal abstract record TargetMetadataState
{
    public sealed record Missing : TargetMetadataState;

    public sealed record StableCurrent(
        TargetContentSnapshot Snapshot) : TargetMetadataState;

    public sealed record Future(
        int Version) : TargetMetadataState;

    public sealed record Corrupt(
        Exception Error) : TargetMetadataState;

    public sealed record Unreadable(
        Exception Error) : TargetMetadataState;

    public sealed record Unstable(
        Exception Error) : TargetMetadataState;
}
```

Classification:

```text
JsonException / InvalidDataException from actual parsing/validation
    -> Corrupt

IOException / UnauthorizedAccessException / SecurityException
    -> Unreadable

TargetInspectionUnstableException
    -> Unstable

UnsupportedLibrarySchemaException
    -> Future
```

## 13.3 Required combination matrix

```text
primary StableCurrent                         => ValidPrimary
primary Future                                => FutureSchema
primary Unreadable                            => Unreadable / hard controlled error
primary Unstable                              => Unstable / retryable controlled error
primary Corrupt + backup StableCurrent        => CorruptPrimaryWithValidBackup
primary Missing + backup StableCurrent        => RecoverableBackupOnly
primary Missing + backup Missing              => Empty
primary Corrupt + backup Missing/Corrupt      => Invalid
```

Do not use backup as substitute for unreadable/unstable primary.

## 13.4 Required tests

```text
CRUU7_007_Unstable_primary_valid_backup_is_retryable_not_corrupt
CRUU7_007_Unreadable_primary_valid_backup_is_not_corrupt
CRUU7_007_Actual_corrupt_primary_valid_backup_is_corrupt_recovery_case
CRUU7_007_Unstable_error_does_not_tell_user_to_start_target_for_recovery
```

---

# 14. CRUU7-008 — Reservation cleanup result is not authoritative

**Severity:** MEDIUM-HIGH  
**Files:** `TargetRootReservation.cs`, `DataFolderTransitionCoordinator.cs`

## 14.1 Current defects

Current `Release()` has this shape:

```csharp
if (_disposed)
{
    return new TargetReservationCleanupResult([]);
}

_disposed = true;
_lock.Dispose();
// then cleanup failures are collected
```

Problems:

1. `_lock.Dispose()` is outside cleanup failure capture.
2. `_disposed` is set before cleanup completes.
3. A second `Release()` returns an empty result, losing the first failure result.
4. Existing-target success ignores the result.
5. Empty-target success ignores the result.
6. Early exceptions before the empty-flow transaction `try` rely on `using.Dispose()` and discard cleanup result.
7. Existing-target branch has no unified cleanup aggregation for early failures.

## 14.2 Required idempotent cached result

```csharp
private TargetReservationCleanupResult? _releaseResult;

public TargetReservationCleanupResult Release()
{
    if (_releaseResult is not null)
    {
        return _releaseResult;
    }

    var failures = new List<MigrationRollbackFailure>();

    try
    {
        _lock.Dispose();
    }
    catch (Exception ex)
    {
        failures.Add(new MigrationRollbackFailure(
            _lockPath,
            "ReleaseLockHandle",
            ex.Message));
    }

    if (_deleteLockFileOnDispose)
    {
        try
        {
            _fileOps.DeleteFile(_lockPath);
        }
        catch (Exception ex)
        {
            failures.Add(new MigrationRollbackFailure(
                _lockPath,
                "DeleteReservationLockFile",
                ex.Message));
        }
    }

    // Delete newly created root only if empty; report all failures.

    _releaseResult =
        new TargetReservationCleanupResult(failures);

    return _releaseResult;
}
```

`Release()` must never throw.

## 14.3 Required file-op seam

```csharp
internal interface IReservationFileOps
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    IReadOnlyList<string> EnumerateEntries(string path);
    void DeleteFile(string path);
    void DeleteDirectory(string path);
}
```

This is required for deterministic cleanup-failure tests.

## 14.4 Required tests

```text
CRUU7_008_Release_result_is_idempotent
CRUU7_008_Release_handle_failure_is_returned_not_thrown
CRUU7_008_Precommit_existing_target_cleanup_failure_is_reported
CRUU7_008_Precommit_empty_target_cleanup_failure_is_reported
CRUU7_008_Second_release_returns_same_failure_result
```

---

# 15. CRUU7-009 — Postcommit cleanup can violate the forced process boundary

**Severity:** HIGH  
**Files:** `DataFolderTransitionCoordinator.cs`, `TargetRootReservation.cs`, `SettingsDialog.xaml.cs`, `MainWindow.xaml.cs`

## 15.1 Current dangerous sequence

In the empty-target branch the current order is approximately:

```text
SaveIfUnchanged(settings)
tx.Commit()
reservation.Release()
return Changed=true
```

If `reservation.Release()` throws after settings have been saved:

```text
settings already points new target
transaction already committed
catch executes
rollback is a no-op because tx committed
second Release may return empty
exception escapes
SettingsDialog shows failure
RestartRequired is never set true
MainWindow does not request shutdown
old source remains the active in-memory repository
```

That breaks the explicit rule that settings transition must create a process boundary before any more old-root edits.

## 15.2 Required point-of-no-return rule

After settings commit succeeds:

```text
NO ORDINARY CLEANUP FAILURE MAY TURN THE TRANSITION INTO FAILURE.
```

Use explicit stage state:

```csharp
bool settingsCommitted = false;

SettingsSaveResult saveResult =
    _settingsRepo.SaveIfUnchanged(...);

settingsCommitted = true;
```

All subsequent ordinary cleanup operations must be nonthrowing and convert failures to warnings.

## 15.3 Required result semantics

After commit:

```csharp
TargetReservationCleanupResult cleanup = reservation.Release();

string? warning = WarningCombiner.Combine(
    settingsSnapshot.Warning,
    targetWarning,
    capabilityWarning,
    saveResult.Warning,
    cleanup.ToWarning());

return new DataFolderTransitionResult(
    Changed: true,
    RestartRequired: true,
    ExistingLibrarySelected: ...,
    NormalizedTargetRoot: bound.LexicalRoot,
    Warning: warning);
```

A stale unlocked `.app.lock` cleanup problem is a warning, not a reason to keep editing the old source.

## 15.4 Required tests

```text
CRUU7_009_Postcommit_reservation_cleanup_failure_still_returns_Changed_true
CRUU7_009_Postcommit_cleanup_failure_still_requires_restart
CRUU7_009_Settings_committed_transition_never_returns_failure_semantics
CRUU7_009_MainWindow_requests_shutdown_after_postcommit_warning
```

The `MainWindow` test must use the injected `IApplicationLifetime` and assert `RequestShutdown()` is called.

---

# 16. CRUU7-010 — Capability probe can leave untracked AtomicTextWriter residue

**Severity:** MEDIUM-HIGH  
**Files:** `DataRootCapabilityValidator.cs`, `AtomicTextWriter.cs`, tests

## 16.1 Current hidden-temp problem

Capability probing calls:

```csharp
_writer.Write(probeFile, "create");
_writer.Write(probeFile, "replace");
```

`AtomicTextWriter` internally creates a hidden temp file:

```text
.<probeFile>.<guid>.tmp
```

and deletes that temp only best-effort.

The capability journal does not know its exact path.

If the writer operation fails and its hidden temp cleanup also fails:

```text
probeFile may not exist
hidden temp remains
probe directory is nonempty
ProbeLocation skips Directory.Delete because not empty
no exception is necessarily recorded for the skipped delete
migration journal knows directory but not hidden temp
rollback can report success while residue remains
```

## 16.2 Required explicit probe protocol

Do not use `AtomicTextWriter` for the capability probe.

Create:

```csharp
internal interface ICapabilityFileOps
{
    Stream CreateNew(string path);
    void FlushToDisk(Stream stream);
    void Replace(string replacement, string destination);
    bool FileExists(string path);
    bool DirectoryExists(string path);
    IReadOnlyList<string> EnumerateEntries(string path);
    void DeleteFile(string path);
    void DeleteDirectory(string path);
}
```

Exact owned probe files:

```text
probe-current.txt
probe-replacement.tmp
```

Protocol:

```text
create unique probe directory; track immediately
create current with CreateNew; track immediately
write + Flush(true)
create replacement with CreateNew; track immediately
write + Flush(true)
File.Replace(replacement, current, null)
update ownership bookkeeping
explicitly delete current
explicitly delete directory
```

If directory remains nonempty unexpectedly, record the names of unexpected entries as a cleanup failure.

## 16.3 Current CRUU6 test must be replaced

A test named `CRUU6_006_Probe_cleanup_failure_is_reported` currently injects write failure, not cleanup failure, and only asserts `IOException`.

Required tests:

```text
CRUU7_010_Probe_file_delete_failure_reports_exact_residue
CRUU7_010_Probe_directory_delete_failure_reports_exact_residue
CRUU7_010_Unexpected_nonempty_probe_directory_is_reported
CRUU7_010_Empty_target_transition_aggregates_probe_cleanup_failure
```

Every test must assert exact failure path and operation.

---

# 17. CRUU7-011 — ConfiguredDataFolderUnavailableException can escape SettingsDialog

**Severity:** MEDIUM  
**Files:** `ManagedDataRootPolicy.cs`, `ConfiguredDataFolderUnavailableException.cs`, `SettingsDialog.xaml.cs`

CRUU6 correctly added a dedicated unavailable-root startup path. The coordinator also calls startup-root validation for the currently configured settings root during a transition.

If the path disappears while the application is already open, `ConfiguredDataFolderUnavailableException` may reach `SettingsDialog`.

The dialog currently does not explicitly catch that exception, and it inherits directly from `Exception`.

## 17.1 Required catch

```csharp
catch (ConfiguredDataFolderUnavailableException ex)
{
    _confirmationService.ShowWarning(
        "The currently configured Prompt Helper data folder can no longer " +
        "be resolved:\r\n\r\n" +
        ex.DataFolderPath +
        "\r\n\r\nNo data-folder change was committed. " +
        "Restore or reconnect the folder and retry.",
        "Configured Data Folder Unavailable");

    return;
}
```

No fallback. No fatal dispatcher shutdown for this ordinary recoverable condition.

## 17.2 Required tests

```text
CRUU7_011_Unavailable_current_settings_path_is_controlled_dialog_warning
CRUU7_011_Unavailable_error_does_not_set_restart_required
CRUU7_011_Unavailable_error_does_not_change_settings
```

---

# 18. CRUU7-012 — Backup writeability policy conflicts with LibraryRepository semantics

**Severity:** MEDIUM  
**Files:** `DataRootCapabilityValidator.cs`, `LibraryRepository.cs`, coordinator

`LibraryRepository` deliberately treats these as warning-level:

```text
future-schema backup -> preserve, warning
unreadable backup -> warning
backup write failure -> warning
```

Existing-target capability validation currently includes `library.backup.json` in a hard writable-file list.

That means a valid writable primary and prompts can be blocked only because an optional safety backup is read-only—even when the current app is required not to modify a future-schema backup at all.

## 18.1 Required result model

```csharp
internal sealed record CapabilityValidationResult(
    string? Warning);
```

Hard requirements for a normal valid-primary target:

```text
root create/delete/replace probe works
primary metadata writable
active prompt bodies writable
```

Backup behavior:

```text
future backup -> do not require writable; preserve
current read-only/unwritable backup -> warning
unreadable backup -> warning or target-state error according to startup authority; do not blindly mutate
```

For backup-only recoverable target:

```text
backup must be readable
root must permit primary creation
backup itself need not be writable merely to select the target
```

## 18.2 Required tests

```text
CRUU7_012_Readonly_current_backup_warns_but_does_not_block_valid_primary_target
CRUU7_012_Future_readonly_backup_does_not_block_valid_primary_target
CRUU7_012_Readonly_primary_still_blocks_target
CRUU7_012_Readonly_active_prompt_still_blocks_target
```

---

# 19. CRUU7-013 — Warning composition loses safety information

**Severity:** LOW-MEDIUM  
**Files:** `DataFolderTransitionCoordinator.cs`, `DataFolderTransitionResult.cs`

Current warning sources include:

```text
settingsSnapshot.Warning
initial/locked target warning
saveResult.Warning
```

Current return code can use:

```csharp
saveResult.Warning ?? lockedInspection.Warning
```

which discards one warning if both exist, and settings snapshot warning may disappear entirely.

CRUU7 adds even more warning sources:

```text
capability backup warning
postcommit reservation cleanup warning
manifest cleanup warning
```

## 19.1 Required combiner

```csharp
internal static class WarningCombiner
{
    public static string? Combine(params string?[] warnings)
    {
        string[] values = warnings
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return values.Length == 0
            ? null
            : string.Join("\r\n\r\n", values);
    }
}
```

## 19.2 Tests

```text
CRUU7_013_Settings_and_target_warnings_are_both_preserved
CRUU7_013_Capability_and_backup_warning_are_both_preserved
CRUU7_013_Postcommit_cleanup_warning_is_added_not_replaced
CRUU7_013_Duplicate_warning_text_is_not_repeated
```

---

# 20. CRUU7-014 — Settings mutation lease retries unrelated I/O and blocks too long

**Severity:** LOW-MEDIUM  
**Files:** `SettingsMutationLease.cs`, `AppSettingsRepositoryTests.cs`

Current acquisition retries any `IOException` until default timeout:

```text
5000 ms
```

Only actual sharing/locking contention should retry.

The current CRUU6 test comment says a 100 ms timeout should be used, but the call path invokes `SaveIfUnchanged()` with the default lease timeout and can wait roughly five seconds. Five stress runs can therefore multiply avoidable delays.

## 20.1 Required contention classifier

```csharp
private const int ERROR_SHARING_VIOLATION = 32;
private const int ERROR_LOCK_VIOLATION = 33;

private static bool IsContention(IOException ex)
{
    int code = ex.HResult & 0xFFFF;
    return code is ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION;
}
```

Retry only when `IsContention(ex)`.

## 20.2 Required injectable policy

```csharp
internal sealed record SettingsLeasePolicy(
    TimeSpan Timeout,
    TimeSpan RetryDelay);
```

Suggested production default:

```text
Timeout = 1–2 seconds
RetryDelay = 25–50 ms
```

Suggested test policy:

```text
Timeout = 50 ms
RetryDelay = 5 ms
```

## 20.3 Tests

```text
CRUU7_014_Noncontention_io_failure_is_not_retried
CRUU7_014_Contention_uses_injected_short_test_timeout
CRUU7_014_Lease_test_does_not_wait_five_seconds
CRUU7_014_Contended_settings_operation_returns_controlled_busy_error
```

No arbitrary long sleeps.

---

# 21. CRUU7-015 — CRUU6 tests do not prove several named behaviors

**Severity:** MEDIUM verification gap

Examples from current tests:

## Capability cleanup

A test named:

```text
CRUU6_006_Probe_cleanup_failure_is_reported
```

does not cause cleanup to fail. It causes the writer to fail and accepts ordinary `IOException`.

## Reservation cleanup

A test named:

```text
CRUU6_007_Reservation_lock_cleanup_failure_is_reported
```

uses broad:

```csharp
Assert.Throws<Exception>(...)
```

and does not require the cleanup failure to be present in the returned exception details.

## Dialog behavior

A test named:

```text
CRUU6_009_Future_target_schema_is_controlled_dialog_error
```

calls the coordinator directly and expects `UnsupportedLibrarySchemaException`. It does not construct `SettingsDialog`, so it proves the coordinator contract, not dialog handling.

The future-settings test also accepts generic `Exception`.

## Target snapshot

Metadata instability is tested; prompt-body instability is not.

## Reparse path

Current resolver test proves identity change *at a checkpoint* is rejected. It does not prove a safe→evil→safe transition between checkpoints cannot receive writes.

## 21.1 Required testability interface

Create:

```csharp
internal interface IDataFolderTransitionService
{
    DataFolderTransitionResult RequestTransition(string candidateRoot);
}
```

`DataFolderTransitionCoordinator` implements it.

`SettingsDialog` accepts the interface, enabling direct WPF behavior tests without reflection.

## 21.2 Required WPF tests

Using `WpfTestHost`:

```text
CRUU7_015_SettingsDialog_handles_future_library_schema_without_escape
CRUU7_015_SettingsDialog_handles_future_settings_schema_without_escape
CRUU7_015_SettingsDialog_handles_unavailable_root_without_escape
CRUU7_015_SettingsDialog_leaves_restart_false_on_precommit_failure
CRUU7_015_SettingsDialog_sets_restart_true_on_success_with_warning
```

Do not use `Assert.Throws<Exception>` for safety-contract tests.

---

# 22. CRUU7-016 — OrdinalIgnoreCase is not universally valid on Windows

**Severity:** LOW-MEDIUM  
**Files:** `PathIdentity.cs`, `ManagedDataRootPolicy.cs`, Windows path services/tests

Windows supports directories configured as case-sensitive. Inside such a directory, names differing only by case can be distinct.

Current `PathIdentity` always compares with:

```csharp
StringComparison.OrdinalIgnoreCase
```

Because Prompt Helper does not require case-sensitive managed roots, the lowest-risk repair is to reject them explicitly rather than redesign all identity rules.

## 22.1 Required inspector

```csharp
internal interface IDirectoryCaseSensitivityInspector
{
    bool IsCaseSensitive(string existingDirectory);
}
```

Windows implementation:

1. Open directory with `CreateFileW` + `FILE_FLAG_BACKUP_SEMANTICS`.
2. Call `GetFileInformationByHandleEx` using `FileCaseSensitiveInfo`.
3. Read a structure equivalent to:

```csharp
[StructLayout(LayoutKind.Sequential)]
private struct FILE_CASE_SENSITIVE_INFORMATION
{
    public uint Flags;
}
```

4. Detect:

```csharp
private const uint FILE_CS_FLAG_CASE_SENSITIVE_DIR = 0x00000001;
```

5. Fail closed if set.

For a nonexistent target, inspect the nearest existing ancestor used to resolve naming semantics.

## 22.2 Tests

```text
CRUU7_016_Case_sensitive_managed_root_is_rejected
CRUU7_016_Case_sensitive_candidate_ancestor_is_rejected
CRUU7_016_Real_NTFS_case_sensitive_directory_is_rejected
```

A Windows integration test may use `fsutil file setCaseSensitiveInfo` only to construct the fixture; production code must not shell out.

---

# 23. CRUU7-017 — Strict icon gate does not prove exact icon identity

**Severity:** MEDIUM release verification gap  
**Files:** `tools/VerifyReleaseAssets.ps1`, `tools/GenerateAppIcon.ps1`, CI

Current published EXE verification calls `ExtractIconEx(..., -1, ...)` and only requires at least one icon group.

That proves:

```text
some icon resource exists
```

It does not prove:

```text
embedded frames match current PromptHelper.ico
committed ICO matches current PromptHelperLogo.svg
EXE is not carrying an older stale icon
```

## 23.1 Required SVG -> ICO identity

Make icon generation accept explicit output path:

```powershell
./tools/GenerateAppIcon.ps1 `
  -SourceSvg src/PromptHelper/Assets/PromptHelperLogo.svg `
  -OutputIco artifacts/icon-check/PromptHelper.ico
```

Strict verifier regenerates a temporary ICO and compares either:

```text
exact bytes
```

or, if generator metadata is nondeterministic:

```text
normalized ordered frame dimensions + raw frame payload SHA-256 hashes
```

against committed `PromptHelper.ico`.

## 23.2 Required ICO -> EXE identity

Use Windows resource APIs in the verifier or a small helper:

```text
LoadLibraryExW(..., LOAD_LIBRARY_AS_DATAFILE)
EnumResourceNamesW
FindResourceW
LoadResource
SizeofResource
LockResource
FreeLibrary
```

Resource types:

```text
RT_ICON = 3
RT_GROUP_ICON = 14
```

Parse the group icon entries, load each referenced raw `RT_ICON` payload, hash it, and compare the multiset against the current ICO frame payloads.

Required:

```text
same required dimensions
same number of expected frames/groups according to packaging
same frame payload hashes
```

## 23.3 Tests

```text
CRUU7_017_Committed_ico_matches_current_svg_generation
CRUU7_017_Published_exe_icon_payload_hashes_match_committed_ico
CRUU7_017_Stale_wrong_exe_icon_is_rejected
CRUU7_017_Stale_ico_after_svg_change_is_rejected
```

---

# 24. CRUU7-018 — Real logo remains an external release blocker

**Severity:** RELEASE BLOCKER

At the audited commit, the repository still does not contain the expected `src/PromptHelper/Assets` path, and README still says:

```text
Release asset pending: PromptHelperLogo.svg
```

Do not synthesize or substitute a fake asset.

Until the approved SVG is supplied:

```text
development build/test may proceed
normal non-strict CI may proceed
strict release acceptance remains blocked
```

After the real SVG arrives:

```text
generate ICO
verify SVG -> ICO identity
build/publish
verify ICO -> EXE identity
manual Explorer/window/taskbar/Alt+Tab icon validation
```

---

# 25. Recommended transition-session object

To prevent another round of string/path/state drift, introduce one internal per-transition session model:

```csharp
internal sealed class DataFolderTransitionSession
{
    public required Guid AttemptId { get; init; }
    public required string ActivePhysicalRoot { get; init; }
    public required BoundTargetRoot Target { get; init; }
    public required string BootstrapPhysicalRoot { get; init; }
    public required SettingsTransitionSnapshot SettingsSnapshot { get; init; }

    public MigrationPayloadSnapshot? PayloadSnapshot { get; set; }
    public TargetRootReservation? Reservation { get; set; }
    public DataFolderMigrationService.MigrationTargetTransaction? TargetTransaction { get; set; }

    public bool SettingsCommitted { get; set; }

    public List<string> Warnings { get; } = [];
}
```

This is internal architecture, not new public product API.

The single `SettingsCommitted` bit defines the point-of-no-return error semantics.

---

# 26. Empty-target state machine

For deterministic tests and fault injection, use an internal stage enum:

```csharp
internal enum EmptyTargetTransitionStage
{
    Created,
    SettingsSnapshotCaptured,
    ActiveIdentityValidated,
    TargetBound,
    SourceSnapshotted,
    TargetReserved,
    InterruptedStateResolved,
    ManifestCreated,
    PayloadCopiedDurably,
    PayloadVerified,
    ReadyManifestCommitted,
    SettingsCommitted,
    PostCommitCleanupCompleted
}
```

Do not persist every stage. Persist only the minimal `Copying` / `ReadyToCommit` manifest phase required for crash recovery.

---

# 27. Existing-target state machine

```csharp
internal enum ExistingTargetTransitionStage
{
    Created,
    SettingsSnapshotCaptured,
    ActiveIdentityValidated,
    TargetBound,
    InitialTargetSnapshotted,
    UserConfirmed,
    TargetReserved,
    PhysicalIdentityRevalidated,
    LockedTargetSnapshotted,
    CapabilityValidated,
    PreCommitTargetSnapshotted,
    SettingsCommitted,
    PostCommitCleanupCompleted
}
```

This makes each failure boundary explicitly testable.

---

# 28. Manifest strict-validation rules

Never trust a migration marker merely because it parses.

Require:

```text
schemaVersion exactly 1
AttemptId non-empty
source physical root fully qualified
target physical root fully qualified
source != target
target equals bound physical target
artifact relative paths are nonempty
artifact paths are not absolute
artifact paths contain no traversal escaping root
no duplicate relative path
SHA-256 exactly 64 hex chars
length >= 0
known role only
artifact cannot target marker/lock/probe/temp namespace
```

Any invalid marker means:

```text
DO NOT DELETE target data
DO NOT OVERWRITE target data
show explicit manual-review error
```

---

# 29. Safe manifest path joining

Use a helper that proves containment:

```csharp
internal static string ResolveManifestArtifactPath(
    string root,
    string relativePath)
{
    if (Path.IsPathFullyQualified(relativePath))
    {
        throw new InvalidDataException(
            "Migration artifact path must be relative.");
    }

    string normalizedRoot =
        PathIdentity.NormalizeForComparison(root);

    string full = Path.GetFullPath(
        Path.Combine(normalizedRoot, relativePath));

    if (!PathIdentity.IsStrictDescendant(full, normalizedRoot))
    {
        throw new InvalidDataException(
            "Migration artifact path escapes the target root.");
    }

    return full;
}
```

Apply case-sensitive-directory rejection before relying on case-insensitive containment semantics.

---

# 30. Interrupted migration recovery policy

Use the simplest safe policy; do not attempt clever arbitrary-stage resume.

```text
IF settings still point source:
    inspect marker under bound target lock
    safely clean exact owned artifacts whose current hash/length match manifest
    preserve any changed/non-owned file
    delete marker last
    re-inspect target
    start a fresh migration

IF settings point target AND marker is ReadyToCommit:
    verify complete payload against marker
    if exact -> clear marker and continue startup
    if mismatch -> fail closed

OTHERWISE:
    manual-review error, no auto-delete
```

---

# 31. Crash/retry matrix

| Crash point | On-disk target | Required next behavior |
|---|---|---|
| Before manifest | empty or reservation residue | normal retry / cleanup residue |
| Manifest only | Copying marker | exact owned cleanup, fresh retry |
| During primary temp | marker + attempt temp | delete exact attempt temp, retry |
| Primary final only | marker + primary | delete only if manifest hash/length matches |
| Subset prompts | marker + subset finals/temps | exact cleanup, retry |
| Full payload + Copying | complete copy but not ready | interrupted, not existing library |
| ReadyToCommit before settings | durable payload + Ready marker, settings source | exact cleanup then fresh retry (simplest policy) |
| Settings committed + Ready marker | settings target | startup verifies then finalizes marker cleanup |
| Settings committed + payload mismatch | settings target | fail closed; no default initialization |
| Corrupt/future marker | unknown ownership | no auto-delete; controlled error |

---

# 32. File-by-file implementation map

## `AppSettingsRepository.cs`

```text
make Core helpers private
make token capture private/internal as needed
inject SettingsLeasePolicy
safe public methods remain lease-protected
```

## `SettingsMutationLease.cs`

```text
retry only Win32 sharing/lock violations
short bounded configurable timeout
non-contention I/O fails immediately
```

## `DataFolderTransitionCoordinator.cs`

```text
introduce DataFolderTransitionSession
bind physical target once
all target I/O uses bound physical root
lexical locator only revalidated and persisted
add interrupted-migration recovery branch
make postcommit boundary explicit
aggregate all warnings
```

## `DataFolderMigrationService.cs`

```text
full payload snapshot
copy snapshot list only
durable flush/write-through promotion
two-pass target body snapshot
typed target metadata states
full source/target hash verification
```

## `IMigrationFileOps.cs`

Add:

```text
FlushToDisk
MoveNoOverwriteWriteThrough
helpers needed for deterministic full payload enumeration/testing
```

## `TargetRootReservation.cs`

```text
physical root only
idempotent cached Release result
Release never throws
inject reservation cleanup file ops
```

## `DataRootCapabilityValidator.cs`

```text
replace AtomicTextWriter probe with explicit probe file ops
return CapabilityValidationResult
backup writeability warning-level where appropriate
```

## `AppPaths.cs`

Add migration marker path.

## New recommended files

```text
BoundTargetRoot.cs
DataFolderTransitionSession.cs
MigrationPayloadSnapshot.cs
MigrationAttemptManifest.cs
MigrationManifestRepository.cs
MigrationTargetRecoveryService.cs
TargetInspectionUnstableException.cs
WarningCombiner.cs
ICapabilityFileOps.cs
IReservationFileOps.cs
SettingsLeasePolicy.cs
```

## `SettingsDialog.xaml.cs`

```text
accept IDataFolderTransitionService
catch ConfiguredDataFolderUnavailableException
preserve future-schema catches
```

## `App.xaml.cs`

```text
handle ReadyToCommit residue on selected active target
never initialize defaults if committed marker verification fails
```

## `ManagedDataRootPolicy.cs`

Add case-sensitive-root fail-closed policy while preserving physical alias safety.

## Release scripts

Add exact SVG -> ICO -> EXE identity checks after product fixes.


---

# 33. Required test doubles and seams

Do not use timing tricks to emulate deterministic races.

Required/extended test doubles:

```text
FakePhysicalPathResolver
FaultInjectingMigrationFileOps
RecordingMigrationFileOps
FakeCapabilityFileOps
FakeReservationFileOps
FakeMigrationManifestRepository
FakeDataFolderTransitionService
FakeApplicationLifetime
```

Each fault double should support exact operation callbacks instead of one generic “fail sometimes” flag.

Example trace helper:

```csharp
internal sealed class OperationTrace
{
    public List<string> Entries { get; } = [];

    public void Add(string entry) => Entries.Add(entry);

    public int IndexOf(string prefix) =>
        Entries.FindIndex(x =>
            x.StartsWith(prefix, StringComparison.Ordinal));
}
```

Use it to assert:

```text
FlushToDisk(file) < MoveWriteThrough(file)
all MoveWriteThrough < ReadyManifestWrite
ReadyManifestWrite < SettingsCommit
SettingsCommit < ReservationRelease
```

---

# 34. Complete CRUU7 named regression matrix

The weak model must add these tests or semantically equivalent stronger tests. Existing CRUU1–CRUU6 tests remain unless a test is replaced because it is demonstrably false/weak; in that case preserve the scenario with a stronger assertion.

## CRUU7-001 physical binding

```text
CRUU7_001_Bound_physical_target_is_used_for_all_mutating_io
CRUU7_001_Lexical_alias_flip_to_evil_and_back_writes_nothing_to_evil
CRUU7_001_Reservation_lock_is_created_only_at_bound_physical_target
CRUU7_001_Capability_probe_uses_bound_physical_target
CRUU7_001_Existing_target_inspection_uses_bound_physical_target
```

## CRUU7-002 durability

```text
CRUU7_002_Every_migrated_file_is_flushed_to_disk_before_promotion
CRUU7_002_Settings_commit_occurs_after_all_durable_promotions
CRUU7_002_Flush_failure_rolls_back_without_settings_change
CRUU7_002_WriteThrough_move_failure_rolls_back_without_settings_change
```

## CRUU7-003 payload coverage

```text
CRUU7_003_Backup_change_during_copy_aborts
CRUU7_003_Backup_appearing_during_copy_aborts
CRUU7_003_Backup_disappearing_during_copy_aborts
CRUU7_003_Orphan_prompt_change_during_copy_aborts
CRUU7_003_Orphan_prompt_added_during_copy_aborts
CRUU7_003_Orphan_prompt_removed_during_copy_aborts
CRUU7_003_Recovery_artifact_change_during_copy_aborts
CRUU7_003_Recovery_artifact_added_during_copy_aborts
CRUU7_003_Recovery_artifact_removed_during_copy_aborts
CRUU7_003_Source_file_set_change_aborts
CRUU7_003_Target_hashes_match_every_payload_file
```

## CRUU7-004 interruption

```text
CRUU7_004_Crash_after_manifest_only_is_recoverable
CRUU7_004_Crash_after_primary_only_is_interrupted_not_existing
CRUU7_004_Crash_after_subset_of_prompts_is_interrupted_not_invalid
CRUU7_004_Crash_after_full_copy_before_settings_is_not_existing_library
CRUU7_004_Retry_cleans_only_manifest_owned_exact_files
CRUU7_004_Mismatched_manifest_artifact_is_never_auto_deleted
CRUU7_004_Ready_marker_after_settings_commit_is_cleared_on_startup
CRUU7_004_Ready_marker_with_payload_mismatch_blocks_startup
CRUU7_004_Future_manifest_schema_blocks_without_mutation
CRUU7_004_Traversal_path_in_manifest_is_rejected_without_delete
```

## CRUU7-005 settings API

```text
CRUU7_005_No_public_settings_core_mutation_methods
CRUU7_005_Public_settings_mutators_all_acquire_lease
```

## CRUU7-006 stable target snapshot

```text
CRUU7_006_Prompt_body_change_between_snapshot_passes_aborts
CRUU7_006_Two_prompt_hybrid_snapshot_is_rejected
CRUU7_006_Stable_target_two_pass_snapshot_succeeds
```

## CRUU7-007 state classification

```text
CRUU7_007_Unstable_primary_valid_backup_is_retryable_not_corrupt
CRUU7_007_Unreadable_primary_valid_backup_is_not_corrupt
CRUU7_007_Actual_corrupt_primary_valid_backup_is_corrupt_recovery_case
CRUU7_007_Unstable_error_does_not_offer_corrupt_recovery_guidance
```

## CRUU7-008 reservation cleanup

```text
CRUU7_008_Release_result_is_idempotent
CRUU7_008_Release_handle_failure_is_returned_not_thrown
CRUU7_008_Precommit_existing_target_cleanup_failure_is_reported
CRUU7_008_Precommit_empty_target_cleanup_failure_is_reported
CRUU7_008_Second_release_returns_same_failure_result
```

## CRUU7-009 postcommit boundary

```text
CRUU7_009_Postcommit_reservation_cleanup_failure_still_returns_Changed_true
CRUU7_009_Postcommit_cleanup_failure_still_requires_restart
CRUU7_009_Settings_committed_transition_never_returns_failure_semantics
CRUU7_009_MainWindow_requests_shutdown_after_postcommit_warning
```

## CRUU7-010 capability cleanup

```text
CRUU7_010_Probe_file_delete_failure_reports_exact_residue
CRUU7_010_Probe_directory_delete_failure_reports_exact_residue
CRUU7_010_Unexpected_nonempty_probe_directory_is_reported
CRUU7_010_Empty_target_transition_aggregates_probe_cleanup_failure
```

## CRUU7-011 controlled path loss

```text
CRUU7_011_Unavailable_current_settings_path_is_controlled_dialog_warning
CRUU7_011_Unavailable_error_does_not_set_restart_required
CRUU7_011_Unavailable_error_does_not_change_settings
```

## CRUU7-012 capability policy

```text
CRUU7_012_Readonly_current_backup_warns_but_does_not_block_valid_primary_target
CRUU7_012_Future_readonly_backup_does_not_block_valid_primary_target
CRUU7_012_Readonly_primary_still_blocks_target
CRUU7_012_Readonly_active_prompt_still_blocks_target
```

## CRUU7-013 warning composition

```text
CRUU7_013_Settings_and_target_warnings_are_both_preserved
CRUU7_013_Capability_and_backup_warning_are_both_preserved
CRUU7_013_Postcommit_cleanup_warning_is_added_not_replaced
CRUU7_013_Duplicate_warning_text_is_not_repeated
```

## CRUU7-014 settings lease

```text
CRUU7_014_Noncontention_io_failure_is_not_retried
CRUU7_014_Contention_uses_injected_short_test_timeout
CRUU7_014_Lease_test_does_not_wait_five_seconds
CRUU7_014_Contended_settings_operation_returns_controlled_busy_error
```

## CRUU7-015 real UI/evidence

```text
CRUU7_015_SettingsDialog_handles_future_library_schema_without_escape
CRUU7_015_SettingsDialog_handles_future_settings_schema_without_escape
CRUU7_015_SettingsDialog_handles_unavailable_root_without_escape
CRUU7_015_SettingsDialog_leaves_restart_false_on_precommit_failure
CRUU7_015_SettingsDialog_sets_restart_true_on_success_with_warning
CRUU7_015_Reservation_cleanup_test_requires_exact_failure_detail
CRUU7_015_Capability_cleanup_test_requires_actual_cleanup_failure
```

## CRUU7-016 case-sensitive directories

```text
CRUU7_016_Case_sensitive_managed_root_is_rejected
CRUU7_016_Case_sensitive_candidate_ancestor_is_rejected
CRUU7_016_Real_NTFS_case_sensitive_directory_is_rejected
```

## CRUU7-017 release icon identity

```text
CRUU7_017_Committed_ico_matches_current_svg_generation
CRUU7_017_Published_exe_icon_payload_hashes_match_committed_ico
CRUU7_017_Stale_wrong_exe_icon_is_rejected
CRUU7_017_Stale_ico_after_svg_change_is_rejected
```

---

# 35. Fault-injection matrix

| Area | Fault | Required result |
|---|---|---|
| settings | primary changes after snapshot | precommit abort; no pointer write |
| settings | backup changes after snapshot | precommit abort; no overwrite |
| settings | future backup appears | precommit abort or preserve; never downgrade |
| physical path | candidate initially unsafe | no mutation |
| physical path | lexical alias safe→evil→safe | all I/O remains bound safe target; evil untouched |
| reservation | lock creation fails | source/settings unchanged |
| reservation | lock cleanup fails precommit | exact cleanup failure report |
| reservation | cleanup fails postcommit | warning + Changed=true + restart |
| manifest | temp write fails | no final payload |
| manifest | durable flush fails | no final payload/settings commit |
| manifest | corrupt/future schema | no auto-delete |
| manifest | traversal artifact | no auto-delete/outside access |
| copy | temp create fails | rollback |
| copy | mid-copy fails | owned temp rollback |
| copy | durable flush fails | no settings commit |
| copy | write-through move fails | rollback |
| source | backup changes | abort |
| source | orphan prompt appears | abort |
| source | recovery file disappears | abort |
| target snapshot | metadata changes | unstable retry |
| target snapshot | prompt body changes | unstable retry |
| target snapshot | primary locked | unreadable, not corrupt |
| capability | current probe delete fails | residue reported |
| capability | probe directory delete fails | residue reported |
| capability | unexpected hidden file | directory-not-empty report |
| existing target | backup read-only | warning-level |
| existing target | primary read-only | hard reject |
| existing target | active prompt read-only | hard reject |
| UI | future library schema | warning; no fatal handler |
| UI | future settings schema | warning; no fatal handler |
| UI | configured current root disappears | warning; no fatal handler |
| lease | non-sharing IOException | immediate failure, no retry loop |
| lease | sharing violation | bounded retry |
| startup | Ready marker + exact payload | finalize and open |
| startup | Ready marker + mismatch | fail closed |
| path semantics | case-sensitive root | fail closed |
| release | stale wrong EXE icon | strict gate fail |

---

# 36. Invariants after every precommit failure

Where applicable, every safety test must assert:

```text
settings.json unchanged
settings.backup.json unchanged except any legitimate recovery completed before transition snapshot
source library.json unchanged
source library.backup.json unchanged
source prompt files unchanged
source recovery artifacts unchanged
foreign target files unchanged
no settings pointer to target
no unknown migration temp residue
no unknown capability probe residue
no migration marker unless failure report identifies it
no held target lock after operation returns
```

Do not stop at `Assert.Throws`.

---

# 37. Invariants after every postcommit warning

After settings commit:

```text
settings points intended lexical target
target physical payload is valid and durable
DataFolderTransitionResult.Changed == true
RestartRequired == true
warning contains cleanup issue
MainWindow requests shutdown
old source is not edited again
```

A postcommit warning is not a rollback candidate.

---

# 38. Manual Windows transition matrix

Run against the self-contained published EXE after automated pass.

## Empty target cases

```text
nonexistent local folder
existing empty local folder
empty nested folder
folder whose parent is writable but candidate must be created
folder with unrelated non-library file
read-only parent
```

## Existing target cases

```text
normal valid target
valid target + read-only current backup
valid target + future-schema backup
valid target + read-only primary
valid target + read-only active prompt
backup-only recoverable target
future-schema primary
corrupt primary + valid backup
locked primary + valid backup
unstable/actively-changing primary
```

## Alias cases

```text
candidate junction alias of current -> no-op
persisted settings junction alias -> third-root transition works
candidate junction into bootstrap -> reject
candidate junction to volume root -> reject
candidate junction to existing library -> switch flow
candidate lexical path changed during confirmation -> bound physical I/O remains safe
```

## Recovery cases

Use deterministic fixture creator rather than manual ad hoc editing:

```text
Copying marker only
Copying + library.json
Copying + partial prompts
Copying + full payload
ReadyToCommit + settings still source
ReadyToCommit + settings target
ReadyToCommit + one mismatched file
invalid manifest path traversal
future manifest schema
```

---

# 39. Real Windows integration requirements

The following must execute on Windows:

```text
real NTFS junction resolution test
persisted junction alias transition test
physical alias to volume root rejection
physical alias into bootstrap rejection
real read-only existing primary test
real read-only prompt test
real case-sensitive-directory rejection test
WPF SettingsDialog exception-handling tests
```

If a required Windows test is skipped on `windows-latest`, report it as missing evidence. Do not silently count it as acceptance.

---

# 40. Case-sensitive Windows integration fixture

Test-only fixture creation may use:

```powershell
fsutil.exe file setCaseSensitiveInfo <directory> enable
```

Then run production policy against the directory and require explicit rejection.

Cleanup:

```powershell
fsutil.exe file setCaseSensitiveInfo <directory> disable
```

only after child case-colliding entries are removed.

Production application code must use Win32 API inspection, not shell out to `fsutil`.

---

# 41. Recommended operation trace assertions

For one empty-target migration containing primary + backup + two prompts + one recovery file, assert a trace equivalent to:

```text
SettingsSnapshot
BindTarget
SnapshotPayload
ReservePhysicalTarget
WriteManifest.Copying
FlushManifest.Copying
CreateTemp.library
FlushToDisk.library
MoveWriteThrough.library
CreateTemp.backup
FlushToDisk.backup
MoveWriteThrough.backup
CreateTemp.prompt1
FlushToDisk.prompt1
MoveWriteThrough.prompt1
CreateTemp.prompt2
FlushToDisk.prompt2
MoveWriteThrough.prompt2
CreateTemp.recovery
FlushToDisk.recovery
MoveWriteThrough.recovery
VerifySourceSet
VerifySourceHashes
VerifyTargetHashes
WriteManifest.ReadyToCommit
FlushManifest.ReadyToCommit
RevalidateLexicalTarget
SettingsCommit
ReservationRelease
DeleteManifest
ReturnChangedRestartRequired
```

Exact internal names may differ. Ordering may not.

---

# 42. Dedicated postcommit test helper

Add a test-only injectable callback at transition stage boundaries rather than inducing failures through unrelated filesystem tricks.

Example:

```csharp
internal interface ITransitionFaultHook
{
    void OnStage(string stageName);
}
```

Production default no-op.

Test can throw at:

```text
AfterSettingsCommit
BeforeReservationRelease
AfterReservationRelease
BeforeManifestDelete
```

But the implementation must still enforce that errors after commit do not produce precommit failure semantics.

If adding a general hook feels too broad, inject specific file-op cleanup failures instead. Do not add production user-visible debugging UI.

---

# 43. Manifest ownership security rules

The marker is an ownership record. Treat it conservatively.

Never auto-delete:

```text
files absent from manifest
files whose length differs
files whose SHA-256 differs
files outside target root
files with a path that normalizes outside target
files under an unknown/future manifest schema
files whose target physical root does not match current bound target
```

Only auto-delete an exact manifest-owned artifact when the current bytes still match the manifest.

This prevents recovery code from becoming a generic recursive-delete mechanism.

---

# 44. Migration marker vs `.app.lock`

Roles are different:

```text
.app.lock
    live process ownership of library root

.prompthelper-migration.json
    durable evidence of an interrupted or ready-to-commit migration attempt
```

Do not merge the two concepts.

A stale unlocked `.app.lock` may be harmless. A migration marker is semantically meaningful until explicitly resolved.

---

# 45. Migration marker vs `initializing.marker`

Also keep distinct:

```text
initializing.marker
    first-run default library creation recovery

.prompthelper-migration.json
    data-root transition recovery
```

Do not reuse the initialization marker for migrations.

Startup must handle the correct marker for the correct root state.

---

# 46. Target kind after CRUU7

Recommended enum:

```csharp
internal enum TargetLibraryKind
{
    Empty,
    ValidPrimary,
    RecoverableBackupOnly,
    CorruptPrimaryWithValidBackup,
    FutureSchema,
    Unreadable,
    Unstable,
    InterruptedMigration,
    Invalid
}
```

The coordinator must explicitly handle every value.

Do not use a `default:` branch that maps a future enum value to Empty.

---

# 47. Capability result after CRUU7

```csharp
internal sealed record CapabilityValidationResult(
    string? Warning);
```

Hard failure examples:

```text
cannot create probe files
cannot atomically replace normal managed file
cannot delete probe
primary is read-only
active prompt body is read-only
root permission denied
```

Warning examples:

```text
backup is read-only
backup synchronization may fail
postcommit stale lock file could not be removed
```

Do not blur these severities.

---

# 48. WarningCombiner copy-ready implementation

```csharp
internal static class WarningCombiner
{
    public static string? Combine(params string?[] warnings)
    {
        string[] values = warnings
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return values.Length == 0
            ? null
            : string.Join("\r\n\r\n", values);
    }
}
```

---

# 49. Durable copy helper copy-ready shape

```csharp
private void CopyPayloadFileDurably(
    string sourcePath,
    string finalPath,
    Guid attemptId,
    MigrationTargetTransaction tx)
{
    string directory = Path.GetDirectoryName(finalPath)
        ?? throw new InvalidOperationException(
            "Target payload path has no directory.");

    EnsureDirectoryTracked(directory, tx);

    string tempPath = Path.Combine(
        directory,
        $".{Path.GetFileName(finalPath)}." +
        $"migration-{attemptId:N}-{Guid.NewGuid():N}.tmp");

    using (Stream source = _fileOps.OpenRead(sourcePath))
    using (Stream destination = _fileOps.CreateNewFile(tempPath))
    {
        tx.TrackCreatedFile(tempPath);
        source.CopyTo(destination);
        _fileOps.FlushToDisk(destination);
    }

    _fileOps.MoveNoOverwriteWriteThrough(
        tempPath,
        finalPath);

    tx.PromoteCreatedFile(
        tempPath,
        finalPath);
}
```

Hash verification remains mandatory afterward.

---

# 50. Migration manifest durable write requirements

Create a dedicated repository rather than scattering marker writes:

```csharp
internal sealed class MigrationManifestRepository
{
    public MigrationAttemptManifest? TryRead(string markerPath);

    public void WriteDurable(
        string markerPath,
        MigrationAttemptManifest manifest);

    public void Delete(string markerPath);
}
```

`WriteDurable` must:

```text
serialize deterministic JSON
CreateNew temp in same directory
write bytes
Flush(true)
atomically replace/move temp to marker path
return only after final marker is established
```

Use strict schema/property validation analogous to settings/library authority. Do not allow duplicate case-variant `schemaVersion` properties.

---

# 51. Settings public API invariant

After CRUU7, reflection should show no public method whose name contains:

```text
Core
Unsafe
WithoutLease
CaptureFileToken
CaptureWritePreconditionCore
```

The public repository must make the safe path the easiest path.

---

# 52. Settings lease error UX

Contention should produce a controlled message such as:

```text
Prompt Helper settings are currently being updated by another operation.
No data-folder change was committed. Retry in a moment.
```

Do not display raw `HResult` or a five-second frozen UI followed by a generic fatal error.

---

# 53. WPF SettingsDialog test strategy

Do not reflection-invoke private handlers if avoidable.

Introduce `IDataFolderTransitionService` and inject a fake.

On WPF STA thread:

```text
construct SettingsDialog
set fake service behavior
trigger the Save command/click
inspect FakeUserConfirmationService
inspect RestartRequired
verify dialog/window lifetime behavior
```

For future schema:

```text
fake RequestTransition throws UnsupportedLibrarySchemaException
expect warning title Newer Library Version
no exception escapes WpfTestHost.Invoke
RestartRequired false
```

For unavailable root:

```text
fake throws ConfiguredDataFolderUnavailableException
expect controlled warning
no fatal exception
```

---

# 54. Current weak tests that must not remain as sole evidence

These patterns are insufficient:

```csharp
Assert.Throws<Exception>(...);
```

for safety contracts.

Also insufficient:

```text
comment says cleanup will fail
but no cleanup operation is actually faulted
```

or:

```text
test name says SettingsDialog
but only coordinator is instantiated
```

or:

```text
test comment says timeout 100ms
but production default 5000ms is used
```

Keep historical regression coverage, but add direct proof.

---

# 55. Source-size and responsibility guidance

Do not implement CRUU7 by making `DataFolderTransitionCoordinator.cs` a 1000-line service.

Suggested responsibility split:

```text
DataFolderTransitionCoordinator.cs      orchestration / state boundary
MigrationPayloadSnapshotBuilder.cs      complete source snapshot
MigrationFileCopier.cs                   durable owned copy
MigrationManifestRepository.cs          durable marker I/O
MigrationTargetRecoveryService.cs       interrupted attempt resolution
TargetInspectionService.cs              typed two-pass target inspection
TargetRootReservation.cs                target lock ownership
DataRootCapabilityValidator.cs          capability policy
AppSettingsRepository.cs                settings authority/lease
WarningCombiner.cs                       warning aggregation
```

No unrelated UI redesign.

---

# 56. Required build and verification commands

Run on Windows from repository root.

## Environment capture

```powershell
git rev-parse HEAD
git status --short
dotnet --info
pwsh --version
```

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

Require:

```text
exit 0
0 errors
0 warnings unless a warning is explicitly justified in the implementation evidence
```

## Full Release test

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --logger "trx;LogFileName=cruu7-full.trx"
```

Record exact:

```text
total
passed
failed
skipped
duration
```

---

# 57. Five consecutive full-suite runs

```powershell
1..5 | ForEach-Object {
    Write-Host "=== CRUU7 RUN $_ / 5 ==="

    dotnet test PromptHelper.slnx `
      -c Release `
      --no-build `
      --logger "trx;LogFileName=cruu7-run-$_.trx"

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
```

Every run must independently pass.

No “4/5 is acceptable.”

---

# 58. Publish verification

```powershell
Remove-Item `
  -Recurse `
  -Force `
  artifacts\publish-check `
  -ErrorAction SilentlyContinue

dotnet publish `
  src/PromptHelper/PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/publish-check
```

Require:

```text
artifacts/publish-check/PromptHelper.exe
artifacts/publish-check/LICENSE
artifacts/publish-check/THIRD_PARTY_NOTICES.md
```

Run non-strict release assets regardless of missing logo:

```powershell
pwsh ./tools/VerifyReleaseAssets.ps1
```

---

# 59. Strict release only after real logo exists

After actual approved SVG is present:

```powershell
pwsh ./tools/GenerateAppIcon.ps1
pwsh ./tools/VerifyReleaseAssets.ps1 -RequireIcon
```

Publish, then:

```powershell
pwsh ./tools/VerifyReleaseAssets.ps1 `
  -RequireIcon `
  -PublishedExe artifacts/publish-check/PromptHelper.exe
```

CRUU7 additionally requires exact SVG→ICO and ICO→EXE frame identity, not merely icon-count presence.

---

# 60. Manual GUI regression after automated pass

```text
1. Launch published PromptHelper.exe.
2. Verify default/existing library opens.
3. Browse categories.
4. Create category.
5. Rename category.
6. Delete empty category.
7. Create prompt.
8. Edit prompt headline/content.
9. Move prompt.
10. Duplicate prompt.
11. Copy prompt.
12. Verify recent-copy bar.
13. Delete prompt.
14. Open settings and cancel.
15. Select same physical folder through same path -> no-op.
16. Select same physical folder through junction -> no-op.
17. Select empty folder -> migration success + forced shutdown.
18. Reopen -> migrated library exact.
19. Select existing valid library -> confirmation.
20. Cancel -> no mutation.
21. Confirm -> forced shutdown.
22. Reopen -> target library active.
23. Future-schema target -> controlled warning, app survives.
24. Currently configured removable/alias path unavailable -> controlled warning.
25. Read-only primary target -> reject.
26. Read-only backup target -> warning policy, not false hard reject.
27. Interrupted marker fixture -> explicit recovery behavior.
28. Verify no unknown probe/migration temp residue after successful flows.
```

---

# 61. Manual icon verification after real asset exists

Verify:

```text
Explorer executable icon
window title icon
taskbar icon
Alt+Tab icon
```

All must visually match the approved logo.

Do not accept only the binary resource verifier as visual approval.

---

# 62. Explicit forbidden shortcuts

The weak model must not:

```text
- keep mutating target through lexical alias after physical bind;
- remove physical alias safety;
- claim repeated validation equals bound I/O;
- use ordinary Stream.Flush and call target data durable;
- commit settings before every payload final is durable;
- copy files not listed in source payload snapshot;
- omit backup/orphan/recovery files from snapshot while still copying them;
- treat an interrupted migration as ordinary existing target;
- recursively delete interrupted target contents without exact manifest ownership;
- delete a manifest-listed file whose current hash differs;
- make SaveCore/LoadOrRecoverCore public;
- classify all target exceptions as corrupt;
- use valid backup when primary is merely unreadable/unstable;
- discard reservation cleanup result;
- throw ordinary cleanup error after settings commit;
- leave RestartRequired false after settings commit;
- use AtomicTextWriter as opaque capability probe primitive;
- hard-block target solely because optional safety backup is read-only;
- combine warnings with ??;
- retry all IOException values in settings lease;
- use 5-second contention waits in tests;
- use Assert.Throws<Exception> for CRUU7 safety acceptance;
- claim dialog handling without constructing/testing dialog behavior;
- ignore Windows case-sensitive directory support;
- fabricate PromptHelperLogo.svg;
- accept EXE icon merely because ExtractIconEx reports one group;
- delete or weaken prior valid regression tests.
```

---

# 63. Implementation evidence template

The implementation model must return this exact style of report:

```text
CRUU7 IMPLEMENTATION EVIDENCE

BASELINE
- audited baseline:
- implementation branch:
- final commit:

FINDINGS
- CRUU7-001: FIXED / OPEN
- CRUU7-002: FIXED / OPEN
- CRUU7-003: FIXED / OPEN
- CRUU7-004: FIXED / OPEN
- CRUU7-005: FIXED / OPEN
- CRUU7-006: FIXED / OPEN
- CRUU7-007: FIXED / OPEN
- CRUU7-008: FIXED / OPEN
- CRUU7-009: FIXED / OPEN
- CRUU7-010: FIXED / OPEN
- CRUU7-011: FIXED / OPEN
- CRUU7-012: FIXED / OPEN
- CRUU7-013: FIXED / OPEN
- CRUU7-014: FIXED / OPEN
- CRUU7-015: FIXED / OPEN
- CRUU7-016: FIXED / OPEN
- CRUU7-017: FIXED / OPEN
- CRUU7-018: BLOCKED / COMPLETE

BUILD
- command:
- exit code:
- warnings:
- errors:

TESTS
- command:
- total:
- passed:
- failed:
- skipped:
- duration:

FIVE RUNS
- run 1:
- run 2:
- run 3:
- run 4:
- run 5:

WINDOWS INTEGRATION
- junction tests executed:
- case-sensitive test executed:
- WPF dialog tests executed:
- skipped tests:
- reasons:

MIGRATION RECOVERY FIXTURES
- manifest-only:
- partial-primary:
- partial-prompts:
- full-copy-before-settings:
- ReadyToCommit-before-settings:
- ReadyToCommit-after-settings:
- mismatched artifact:
- traversal artifact:

PUBLISH
- command:
- exit code:
- PromptHelper.exe:
- LICENSE:
- THIRD_PARTY_NOTICES.md:

STRICT ICON
- real SVG present:
- generated committed ICO present:
- SVG -> ICO identity:
- ICO -> EXE identity:
- strict release gate:
```

Never replace exact evidence with “all tests passed.”

---

# 64. Definition of done — product/code

CRUU7 product/code is complete only when every box is true:

```text
[ ] All target mutation uses one bound physical target root.
[ ] Reservation `.app.lock` is created at bound physical target.
[ ] Lexical locator is revalidated before commit.
[ ] Alias safe→evil→safe cannot redirect any operation.
[ ] Every target temp file receives Flush(true)-equivalent durability.
[ ] Every final promotion uses write-through/equivalent durability.
[ ] Settings commit occurs after all target durability and verification.
[ ] Snapshot file set exactly equals copy file set.
[ ] Backup is included in payload snapshot if copied.
[ ] Orphan prompts are included if copied.
[ ] Recovery artifacts are included if copied.
[ ] Final source file-set equality is checked.
[ ] Every source payload hash/length is verified.
[ ] Every target payload hash/length is verified.
[ ] Durable migration marker exists before final payload creation.
[ ] Partial migration is classified as InterruptedMigration.
[ ] Full pre-settings copy is still classified as interrupted, not existing library.
[ ] Recovery auto-deletes only exact manifest-owned matching files.
[ ] Changed/foreign files are never auto-deleted.
[ ] ReadyToCommit residue is handled on startup.
[ ] Invalid/future manifest fails closed without mutation.
[ ] Settings Core helpers cannot be called publicly without lease.
[ ] Target prompt bodies use stable two-pass capture.
[ ] Unreadable primary is not called corrupt.
[ ] Unstable primary is not called corrupt.
[ ] Reservation Release never throws.
[ ] Reservation Release returns same result on repeated call.
[ ] Precommit cleanup failures are surfaced.
[ ] Postcommit cleanup failures are warnings only.
[ ] Any committed settings transition has Changed=true.
[ ] Any committed settings transition has RestartRequired=true.
[ ] MainWindow shuts down after committed transition with warnings.
[ ] Capability probe owns every path explicitly.
[ ] Capability cleanup failure is actually injected and verified.
[ ] Configured-root path loss is controlled inside SettingsDialog.
[ ] Backup writeability matches LibraryRepository warning semantics.
[ ] All warning sources are combined.
[ ] Settings lease retries only actual lock contention.
[ ] Lease tests use injected short timeout.
[ ] Real WPF dialog behavior is tested.
[ ] Case-sensitive managed root fails closed.
[ ] Release build passes.
[ ] Full test suite passes.
[ ] Five consecutive full-suite runs pass.
[ ] Required Windows tests execute.
[ ] Self-contained win-x64 publish succeeds.
```

---

# 65. Definition of done — strict release

Strict release additionally requires:

```text
[ ] Real approved PromptHelperLogo.svg supplied.
[ ] PromptHelper.ico generated from that exact SVG.
[ ] Committed ICO matches regeneration from current SVG.
[ ] ICO binary frame structure passes.
[ ] Published EXE icon payload hashes match committed ICO.
[ ] Explorer icon manually verified.
[ ] Window icon manually verified.
[ ] Taskbar icon manually verified.
[ ] Alt+Tab icon manually verified.
```

If all code findings are closed but asset is absent, report exactly:

```text
CRUU7 PRODUCT/CODE CLEAN
STRICT RELEASE ASSET DEPENDENCY OPEN
```

Do not call the release complete.

---

# 66. Copy-ready weak-model implementation prompt

```text
ROLE
You are the implementation model for Prompt Helper CRUU7.

BASELINE AUTHORITY
The audit was performed against current main commit:
8f8aeca5a389fdba689a30e54df542399b4fdd99

Before editing, verify current HEAD.
If main advanced, compare it with this baseline and preserve any already-landed
CRUU7-equivalent fixes. Do not revert unrelated valid work.

INPUT
1. Current Prompt Helper repository
2. cruu7.md
3. Earlier CRUU documents only as historical context

GOAL
Close CRUU7-001 through CRUU7-017 completely.
CRUU7-018 is an external real-logo dependency and MUST NOT be faked.

MANDATORY IMPLEMENTATION ORDER
A. Bind target I/O to one physical root.
B. Add durable target file flush and write-through promotion.
C. Replace partial source snapshot with complete payload snapshot.
D. Add durable interrupted-migration manifest and safe recovery.
E. Close public settings Core lease bypass.
F. Implement stable two-pass target body snapshot and typed target states.
G. Make reservation cleanup result authoritative and nonthrowing.
H. Enforce post-settings-commit point-of-no-return semantics.
I. Replace opaque AtomicTextWriter capability probe with explicit owned probe files.
J. Correct backup capability warning policy.
K. Handle configured-root unavailable error inside SettingsDialog.
L. Preserve all warning sources.
M. Tighten settings lease contention classification and timeout.
N. Replace weak CRUU6 evidence tests with direct behavioral tests.
O. Fail closed on Windows case-sensitive managed directories.
P. Strengthen SVG -> ICO -> published EXE exact identity verification.
Q. Do not touch real logo dependency unless actual approved SVG is supplied.

NON-NEGOTIABLE DATA SAFETY
- Never delete source library data.
- Never merge existing target libraries.
- Never auto-delete a target file unless a valid migration manifest proves this
  attempt owns it AND its current length/hash still exactly match the manifest.
- Never auto-delete a mismatched or foreign target file.
- Never initialize a default library when configured custom root is unavailable.
- Never overwrite a future-schema primary.
- Never perform target mutation through a mutable lexical alias after physical bind.
- Never commit settings before all target payload files are durable and verified.
- Never turn a post-settings-commit cleanup problem into Changed=false or
  RestartRequired=false.
- Never swallow precommit cleanup failure.
- Never classify all target exceptions as corruption.
- Never use catch(Exception) as target authority classification.
- Never accept broad Assert.Throws<Exception> as CRUU7 safety evidence.
- Never fabricate PromptHelperLogo.svg.

IMPLEMENTATION ARCHITECTURE
Follow the canonical state machines, BoundTargetRoot, full
MigrationPayloadSnapshot, durable MigrationAttemptManifest, typed target state,
nonthrowing reservation Release, explicit capability file ops, warning combiner,
and testability interfaces specified in cruu7.md.

TESTING
Implement every CRUU7 named regression test or a demonstrably stronger equivalent.
Use deterministic injected race/failure seams.
Do not use arbitrary Thread.Sleep.
Do not let lock-contention tests wait five seconds.
Use WpfTestHost for actual SettingsDialog behavior.
Use real Windows junction integration tests.
Run a real case-sensitive-directory Windows integration test where the environment
supports fixture creation; if it cannot execute, report missing evidence instead of PASS.

VALIDATION
Run, in order:
1. git status / HEAD capture
2. dotnet restore
3. Release build
4. full Release test suite
5. five consecutive full Release test suites
6. self-contained win-x64 publish
7. non-strict release asset verification
8. strict icon verification only if the real approved SVG exists

OUTPUT
Return the exact CRUU7 IMPLEMENTATION EVIDENCE template from cruu7.md.
Include exact test totals, skipped tests, command exit codes, and all remaining blockers.

Never claim a test/build/publish/Windows/manual check passed unless it actually ran.
```

---

# 67. Final audit verdict

CRUU6 should **not** be reverted. It materially improved the repository.

The repeated second-order defects now point to one clear missing abstraction: the data-folder change must be treated as a physically bound, durability-ordered, crash-recoverable transaction rather than a sequence of individually checked filesystem operations.

The central CRUU7 protocol is:

```text
BIND PHYSICAL TARGET
        ↓
SNAPSHOT COMPLETE SOURCE PAYLOAD
        ↓
CREATE DURABLE ATTEMPT MANIFEST
        ↓
COPY ONLY SNAPSHOTTED FILES
        ↓
DURABLY FLUSH + WRITE-THROUGH PROMOTE
        ↓
VERIFY COMPLETE SOURCE + TARGET
        ↓
DURABLY MARK READY
        ↓
REVALIDATE LEXICAL LOCATOR
        ↓
COMPARE + COMMIT SETTINGS
        ↓
POINT OF NO RETURN
        ↓
NONTHROWING CLEANUP + ALL WARNINGS
        ↓
FORCED PROCESS BOUNDARY
```

At audited commit:

```text
8f8aeca5a389fdba689a30e54df542399b4fdd99
```

the repository is **not yet zero-defect accepted**.

The real logo remains a separate release dependency and must not be synthesized by the implementation model.
