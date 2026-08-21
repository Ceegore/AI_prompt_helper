# CRUU8 — Post-CRUU7 Crash-Recovery, Monotonic Commit & Evidence Audit

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited commit:** `8c86f14c2db031b55f15ea051720358f4a4a45dd`  
**Previous audit chain:** `cruu1.md` → `cruu2.md` → `cruu3.md` → `cruu4.md` → `cruu5.md` → `cruu6.md` → `cruu7.md`  
**Purpose:** independently re-audit the CRUU7 implementation, with special emphasis on crash/interruption recovery, manifest authority, physical target binding, rollback ownership, post-commit monotonicity, default-root transitions, capability policy, WPF process-boundary behavior, test truthfulness, CI evidence, and release verification.

---

# 1. Executive result

CRUU7 again made substantial progress. The audited commit contains real implementations for:

```text
bound physical target I/O
durable payload FileStream.Flush(true)
write-through target promotion
full source payload inventory
migration attempt manifest
two-pass target content snapshots
typed unreadable/unstable/corrupt states
nonthrowing reservation Release result
settings commit point-of-no-return handling
explicit capability probe files
unavailable-folder Settings dialog catch
backup writeability warning semantics
warning aggregation
shorter settings lease contention policy
IDataFolderTransitionService
case-sensitivity detection
new CRUU7 tests
```

However, the new crash-recovery architecture has several **second-order correctness holes** that matter precisely in the abnormal states it was introduced to handle.

The most important result is:

```text
CRUU7 ADDED A MIGRATION MANIFEST,
BUT THE MANIFEST'S OWN LIFETIME IS NOT YET TRANSACTION-SAFE.
```

The audited code can:

```text
delete the ownership manifest before rollback succeeds;
lose manifest durability at its rename/promotion boundary;
fail to recognize temp files created by the attempt;
fail recovery when moving back to the default bootstrap root;
accept semantically incomplete ReadyToCommit manifests;
continue normal editing when a completed marker cannot be deleted;
discard cleanup failures in several precommit paths.
```

Therefore the current repository is **not yet zero-defect accepted**.

Correct audit status:

```text
CRUU7 IMPLEMENTATION                    = SUBSTANTIALLY LANDED
AUDITED MAIN COMMIT                     = 8c86f14c2db031b55f15ea051720358f4a4a45dd
CRUU8 SOURCE AUDIT                      = COMPLETE
NEW CRUU8 FINDINGS                      = OPEN
COMMIT-MESSAGE TEST CLAIM               = 373 TESTS PASSING
INDEPENDENT TEST REPRODUCTION HERE      = NOT AVAILABLE
GITHUB COMBINED STATUS ENTRIES          = NONE RETURNED
STRICT RELEASE                          = BLOCKED BY REAL LOGO
```

The commit message reports **373 total tests passing**. This audit treats that as supplied implementation evidence only. The current audit environment has neither a usable .NET SDK nor PowerShell/Windows WPF runtime, so the test count was not independently reproduced.

---

# 2. External platform facts used by this audit

Microsoft's documented Windows/.NET semantics remain relevant:

```text
FileStream.Flush(true)
```

is the .NET mechanism intended to flush intermediate file buffers to disk.

Microsoft's:

```text
MOVEFILE_WRITE_THROUGH
```

causes `MoveFileEx` not to return until the move operation is completed on disk.

The payload copier now uses those semantics.

The migration manifest writer currently does **not** use equivalent write-through promotion semantics.

Windows also supports per-directory case sensitivity through:

```text
FILE_CASE_SENSITIVE_INFORMATION
FILE_CS_FLAG_CASE_SENSITIVE_DIR
GetFileInformationByHandleEx
```

A failure to inspect that property cannot safely be interpreted as "case insensitive."

---

# 3. CRUU8 finding register

| ID | Severity | Finding |
|---|---|---|
| CRUU8-001 | HIGH | Precommit failure deletes the migration manifest before rollback succeeds, destroying the ownership record exactly when rollback may fail |
| CRUU8-002 | HIGH | Attempt-specific migration temp files are not represented or recovered; several real crash points are therefore unrecoverable or leave orphan temps |
| CRUU8-003 | HIGH | "Empty target" has no stable baseline model; interrupted migration back to the default bootstrap root is blocked by legitimate settings files, and unrelated pre-existing files create the same inconsistency |
| CRUU8-004 | HIGH | `MigrationManifestRepository.WriteDurable` flushes temp contents but promotes the manifest with ordinary `File.Move`/`File.Replace`, and has no failed-promotion temp cleanup |
| CRUU8-005 | HIGH | Manifest semantic invariants are incomplete: empty artifact sets, invalid source hash, missing primary metadata, undefined roles/phases, and multiple raw paths resolving to one file are not fully rejected |
| CRUU8-006 | MEDIUM-HIGH | Startup treats inability to delete a verified `ReadyToCommit` marker as success and allows normal edits; later edits can make the surviving marker block the next startup |
| CRUU8-007 | MEDIUM-HIGH | Interrupted recovery scans only top-level files; nested foreign files and nested attempt temp files can survive while the marker is deleted |
| CRUU8-008 | MEDIUM-HIGH | Precommit reservation-cleanup failures are still discarded in multiple coordinator branches |
| CRUU8-009 | MEDIUM | Reservation of a deeply nonexistent target can create multiple parent directories but cleanup tracks/removes only the final root |
| CRUU8-010 | MEDIUM-HIGH | Windows case-sensitivity inspection fails open when handle creation or `GetFileInformationByHandleEx` fails |
| CRUU8-011 | MEDIUM | A backup-only existing target with a read-only safety backup can still be rejected because the effective backup path is treated as mandatory writable "primary metadata" |
| CRUU8-012 | MEDIUM | Future-backup detection inside capability validation uses ad-hoc, case-sensitive JSON inspection rather than the repository's strict schema authority |
| CRUU8-013 | MEDIUM-HIGH | `SettingsDialog` establishes `RestartRequired` only after postcommit notification UI; a notification failure can leave the old source active after settings already changed |
| CRUU8-014 | MEDIUM verification gap | Several CRUU7 tests do not execute the behavior claimed by their names, especially postcommit cleanup, SettingsDialog exception handling, crash temp recovery, and exact release identity |
| CRUU8-015 | MEDIUM verification gap | Windows CI does not assert that required Windows-only integration tests executed, and does not implement CRUU7's exact icon-identity release gate |
| CRUU8-016 | MEDIUM release gap | CRUU7-017 exact SVG → ICO → published EXE identity verification was not implemented; the strict verifier still proves only that an icon group exists |
| CRUU8-017 | LOW-MEDIUM | Production and test switches still use `default` as an Empty-target path; the internal migration test helper can treat Interrupted/Unreadable/Unstable states as empty |
| CRUU8-018 | LOW-MEDIUM | The retained public `DataRootCapabilityValidator(IAtomicTextWriter?)` constructor silently ignores the supplied writer |
| CRUU8-019 | MEDIUM-HIGH paranoid safety | Recovery hashes a manifest-owned file, closes it, and later deletes by pathname; external replacement between verification and deletion can delete content that was never verified |
| CRUU8-020 | RELEASE BLOCKER | The approved `PromptHelperLogo.svg` and resulting real ICO are still absent |

---

# 4. Required implementation order

Implement in this exact order:

```text
PHASE A  CRUU8-001  preserve ownership marker until rollback is proven complete
PHASE B  CRUU8-002  manifest-defined temp ownership and temp recovery
PHASE C  CRUU8-003  strict Empty-target baseline + bootstrap exceptions
PHASE D  CRUU8-004  durable manifest promotion and failed-write cleanup
PHASE E  CRUU8-005  strict semantic manifest authority
PHASE F  CRUU8-006/007  unified startup/retry recovery service
PHASE G  CRUU8-008/009  cleanup truth and complete created-directory ownership
PHASE H  CRUU8-010  fail-closed case-sensitivity inspection
PHASE I  CRUU8-011/012  capability metadata-role authority
PHASE J  CRUU8-013  WPF postcommit process-boundary monotonicity
PHASE K  CRUU8-019  bind recovery deletion to verified file identity
PHASE L  CRUU8-014/015/017/018  verification and API cleanup
PHASE M  CRUU8-016/020  strict release identity only when real logo exists
```

Do not start by editing documentation or renaming tests.

---

# 5. Locked product behavior

Preserve:

```text
Windows WPF + .NET 10
offline/local operation
Markdown prompt bodies
library.json primary
library.backup.json safety backup
settings.json bootstrap primary
settings.backup.json bootstrap safety backup
current schema versions = 1
future schema preservation
no silent default-root fallback
empty-folder copy flow
existing-library switch flow
no merge
source retained as safety copy
forced process boundary after any committed root change
one active app lock per library
physical junction/symlink safety
no cloud/accounts/telemetry/database/updater/installer redesign
no fabricated release logo
```

Also preserve the documented public rule:

```text
Selecting an EMPTY folder copies the current library.
```

CRUU8 intentionally makes "EMPTY" mean something deterministic.

---

# 6. Canonical CRUU8 transaction rule

There are now three separate authorities:

```text
SETTINGS AUTHORITY
    where the next process should open

MIGRATION MANIFEST AUTHORITY
    what this migration attempt owns / which state it reached

TARGET PAYLOAD AUTHORITY
    the durable library bytes copied by that attempt
```

They must move monotonically.

The manifest must exist from:

```text
before first attempt-owned target temp/final is created
```

until one of these two terminal conditions:

```text
A. precommit rollback has completely removed every attempt-owned object

OR

B. settings commit succeeded AND startup/postcommit finalization can safely retire the marker
```

The ownership manifest must never disappear while attempt-owned target residue can still exist.

---

# 7. CRUU8-001 — Manifest is deleted before rollback succeeds

**Severity:** HIGH  
**Files:**

```text
src/PromptHelper/Services/DataFolderTransitionCoordinator.cs
src/PromptHelper/Services/MigrationTargetRecoveryService.cs
tests/PromptHelper.Tests/Cruu7ComprehensiveVerificationTests.cs
```

## 7.1 Current sequence

Current precommit catch effectively does:

```text
try delete migration marker
rollback transaction
release reservation
if rollback cleanup failed -> throw MigrationRollbackException
```

That ordering is unsafe.

Example:

```text
marker exists
library.json copied
prompt A copied
settings NOT committed

copy/capability/settings step throws

marker is deleted successfully

rollback cannot delete prompt A because file is locked

=> prompt A remains
=> marker is gone
=> next attempt has no ownership proof
```

An even narrower crash window exists:

```text
marker deleted
process dies before Rollback()
```

The disk is now exactly the state the manifest was intended to prevent.

## 7.2 Required ordering

Required:

```text
catch precommit exception
    ↓
attempt rollback WHILE marker still exists
    ↓
attempt reservation cleanup
    ↓
if ANY owned residue could remain:
    KEEP marker
    throw cleanup exception containing exact failures
    ↓
only when rollback is complete and target baseline restored:
    durably delete marker
    ↓
rethrow original transition failure
```

## 7.3 Exact helper shape

Create:

```csharp
private PrecommitCleanupResult
    CleanupFailedEmptyTransition(
        Exception original,
        string targetRoot,
        string markerPath,
        MigrationTargetTransaction tx,
        TargetRootReservation reservation)
```

Model:

```csharp
internal sealed record PrecommitCleanupResult(
    MigrationRollbackResult PayloadRollback,
    TargetReservationCleanupResult ReservationCleanup,
    bool MarkerDeleted,
    Exception? MarkerDeleteError)
{
    public bool TargetOwnershipFullyCleared =>
        PayloadRollback.Success &&
        ReservationCleanup.Success &&
        MarkerDeleted;
}
```

**Important:** reservation `.app.lock` cleanup failure does not necessarily mean payload ownership remains, but it is still reportable. The manifest may be deleted only if every manifest-owned payload/temp has been removed. Keep logic explicit.

## 7.4 Marker deletion condition

Before deleting marker, verify:

```text
no final manifest artifact exists
no manifest-defined temp exists
no attempt-owned created directory contains owned data
```

Then delete marker.

If marker deletion fails after full rollback:

```text
leave marker
throw/report cleanup failure
```

A marker with no payload is safe and recoverable on retry.

## 7.5 Required tests

```text
CRUU8_001_Rollback_failure_preserves_manifest
CRUU8_001_Process_crash_fixture_after_marker_delete_is_no_longer_possible_by_order
CRUU8_001_Successful_precommit_rollback_deletes_manifest_last
CRUU8_001_Marker_delete_failure_after_complete_rollback_leaves_marker
CRUU8_001_Retry_after_cleanup_failure_uses_manifest_and_does_not_treat_residue_as_existing_library
```

Use an operation trace:

```text
RollbackFile
RollbackDirectory
ReservationRelease
VerifyNoOwnedResidue
ManifestDelete
```

`ManifestDelete` must be last.

---

# 8. CRUU8-002 — Attempt temp files are not recoverable

**Severity:** HIGH

## 8.1 Current temp naming

Target copy creates temps resembling:

```text
.<finalName>.migration-<AttemptId>-<randomGuid>.tmp
```

The manifest records:

```text
final relative path
final SHA-256
final length
role
```

It does **not** record each temp path.

`MigrationTargetRecoveryService` only verifies/deletes final artifacts.

## 8.2 Real crash states

Crash after temp creation for root metadata:

```text
.prompthelper-migration.json
.library.json.migration-ATTEMPT-RANDOM.tmp
```

Recovery top-level foreign scan sees the temp as unrecognized and aborts.

Crash during a prompt temp:

```text
.prompthelper-migration.json
prompts\.GUID.md.migration-ATTEMPT-RANDOM.tmp
```

Current foreign scan does not recurse into `prompts`.

Recovery may:

```text
delete any finals
delete marker
leave prompt temp
```

The attempt becomes unowned.

## 8.3 Required design

Manifest must define temp paths **before copying starts**.

Extend artifact:

```csharp
internal sealed class MigrationManifestArtifact
{
    public string RelativePath { get; set; } = "";
    public string TempRelativePath { get; set; } = "";
    public string Sha256Hex { get; set; } = "";
    public long Length { get; set; }
    public MigrationPayloadRole Role { get; set; }
}
```

Build all temp names before the Copying manifest is written:

```csharp
string tempRelative =
    Path.Combine(
        directoryPart,
        $".{finalName}.migration-{attemptId:N}-{RandomNonce()}.tmp");
```

Nonce must have at least 128 bits.

Then `CopySnapshotToTarget` uses the manifest's predeclared temp path.

Do not generate a second random name inside the copier.

## 8.4 Temp ownership

A temp path listed in a durable manifest is attempt-owned.

Recovery may delete that exact temp path.

Do not delete wildcard-matching temps from another attempt.

## 8.5 Required recovery order

For every manifest artifact:

```text
if declared temp exists:
    delete exact temp
if declared final exists:
    verify final hash+length
    delete exact final for precommit cleanup
```

For startup committed finalization:

```text
declared temps must NOT exist
all finals must exist + match
```

Any temp in `ReadyToCommit` means the payload was not cleanly finalized.

Fail closed.

## 8.6 Tests

```text
CRUU8_002_Crash_after_library_temp_creation_is_recoverable
CRUU8_002_Crash_mid_library_temp_write_is_recoverable
CRUU8_002_Crash_after_prompt_temp_creation_is_recoverable
CRUU8_002_Crash_mid_prompt_temp_write_is_recoverable
CRUU8_002_Recovery_deletes_only_declared_attempt_temp
CRUU8_002_Temp_from_other_attempt_is_foreign_and_preserved
CRUU8_002_ReadyToCommit_with_any_declared_temp_fails_closed
```

---

# 9. CRUU8-003 — Empty target baseline is undefined

**Severity:** HIGH  
**Flows affected:**

```text
custom -> empty custom
custom -> default bootstrap root
retry after interrupted migration
folders containing unrelated files but no library metadata
```

## 9.1 Current semantic mismatch

README says:

```text
Selecting an EMPTY folder
```

Current target classification says Empty when:

```text
library.json absent
AND
library.backup.json absent
```

Unrelated files are ignored.

## 9.2 Default bootstrap flow

The exact default target legitimately contains:

```text
settings.json
settings.backup.json
.settings.lock
```

because bootstrap settings always live there.

Normal custom → default migration can work.

Interrupted custom → default migration currently cannot be cleanly recovered because `MigrationTargetRecoveryService` considers those top-level settings files foreign.

## 9.3 Required simple policy

Do not add a general arbitrary baseline manifest unless product requirements change.

Use stricter documented semantics:

### Custom target

Before migration it may contain only:

```text
nothing
or empty prompts/ directory
or empty recovery/ directory
plus reservation .app.lock while reserved
```

Any unrelated file means:

```text
OccupiedNonLibrary
```

and must be rejected.

### Exact bootstrap target

May additionally contain only:

```text
settings.json
settings.backup.json
.settings.lock
```

plus the same allowed empty data subdirectories and reservation lock.

Never copy/overwrite/delete bootstrap settings files as migration payload.

## 9.4 Add target state

```csharp
TargetLibraryKind.OccupiedNonLibrary
```

Include detected entries in error/warning context.

## 9.5 Bootstrap-aware recovery context

Pass explicit context:

```csharp
internal sealed record MigrationRecoveryContext(
    string TargetPhysicalRoot,
    bool IsExactBootstrapRoot,
    IReadOnlySet<string> AllowedPersistentRelativePaths);
```

For exact bootstrap:

```text
settings.json
settings.backup.json
.settings.lock
.app.lock
.prompthelper-migration.json
```

`settings.*` are baseline/persistent, not attempt-owned.

## 9.6 Tests

```text
CRUU8_003_Custom_target_with_notes_txt_is_not_empty
CRUU8_003_Custom_target_with_nonempty_prompts_dir_is_not_empty
CRUU8_003_Custom_target_with_empty_prompts_and_recovery_dirs_is_selectable
CRUU8_003_Default_bootstrap_with_settings_files_is_selectable
CRUU8_003_Interrupted_migration_to_default_bootstrap_recovers_with_settings_files_present
CRUU8_003_Recovery_never_deletes_bootstrap_settings_primary
CRUU8_003_Recovery_never_deletes_bootstrap_settings_backup
CRUU8_003_Recovery_never_deletes_settings_lock_file_as_payload
```

---

# 10. CRUU8-004 — Manifest promotion is not actually durable

**Severity:** HIGH

## 10.1 Current behavior

`WriteDurable`:

```text
write temp
Flush(true) temp
close temp
File.Move or File.Replace to final marker
return
```

The temp contents are durably flushed.

The name/promotion step does not use the same write-through mechanism already added for target payload promotion.

## 10.2 Why this matters

The CRUU7 ordering assumes:

```text
WriteDurable(Copying)
returns
=> marker is durable before payload creation
```

If a crash/power-loss can lose the marker rename while later payload writes reached disk, crash recovery authority is gone.

## 10.3 Required manifest file ops

Create:

```csharp
internal interface IMigrationManifestFileOps
{
    Stream CreateNew(string path);
    void FlushToDisk(Stream stream);

    void MoveNoOverwriteWriteThrough(
        string source,
        string destination);

    void ReplaceWriteThrough(
        string source,
        string destination);

    bool FileExists(string path);
    void DeleteFile(string path);
}
```

Windows:

```text
first publish:
MoveFileExW(temp, marker, MOVEFILE_WRITE_THROUGH)

phase replacement:
MoveFileExW(
    temp,
    marker,
    MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)
```

## 10.4 Mandatory finally cleanup

Current writer can leave its own temp when promotion fails.

Use:

```csharp
string tempPath = ...;
bool promoted = false;

try
{
    ...
    ops.Move...;
    promoted = true;
}
finally
{
    if (!promoted && ops.FileExists(tempPath))
    {
        try delete
        catch -> throw/report ManifestWriteCleanupException
    }
}
```

Never silently leave an unknown manifest temp.

## 10.5 Tests

```text
CRUU8_004_Copying_manifest_uses_write_through_promotion
CRUU8_004_Ready_manifest_uses_write_through_replacement
CRUU8_004_Failed_first_manifest_promotion_cleans_temp
CRUU8_004_Failed_ready_manifest_replacement_cleans_temp
CRUU8_004_Manifest_temp_cleanup_failure_reports_exact_path
```

---

# 11. CRUU8-005 — Manifest semantic authority is incomplete

**Severity:** HIGH

## 11.1 Current examples

Current CRUU7 tests successfully construct manifests with:

```text
SourceLibrarySha256Hex = "abc"
Artifacts = []
```

That is direct evidence these fields are not currently authoritative.

## 11.2 Required manifest invariants

Require all of the following:

```text
SchemaVersion exactly current
AttemptId nonempty
SourcePhysicalRoot fully qualified
TargetPhysicalRoot fully qualified
source != target
SourceLibrarySha256Hex exactly 64 hex chars
Phase Enum.IsDefined
Artifacts non-null and non-empty
every artifact Role Enum.IsDefined
every artifact length >= 0
every artifact hash exactly 64 hex
every artifact final path canonical
every artifact temp path canonical
every final path unique by resolved full identity
every temp path unique by resolved full identity
no final path equals any temp path
exactly one PrimaryMetadata artifact
PrimaryMetadata.RelativePath == "library.json"
SourceLibrarySha256Hex == PrimaryMetadata.Sha256Hex
zero or one SafetyBackup artifact
SafetyBackup path, when present, == "library.backup.json"
PromptBody / OrphanPromptBody paths are below prompts/
RecoveryArtifact paths are below recovery/
no artifact path points into reserved control namespaces
```

## 11.3 Canonical relative paths

Reject aliases such as:

```text
prompts\..\library.json
.\library.json
recovery\..\library.json
```

even when they stay inside root.

Canonicalization:

```csharp
string resolved =
    ResolveManifestArtifactPath(root, relative);

string canonicalRelative =
    Path.GetRelativePath(root, resolved);

if (!RelativePathEqualsCanonical(
        relative,
        canonicalRelative))
{
    throw new InvalidDataException(
        "Migration artifact path is not canonical.");
}
```

Then detect duplicates by **resolved full path**, not raw relative string.

## 11.4 Startup authority

`ReadyToCommit` must never be accepted with:

```text
zero artifacts
no library.json
invalid primary hash
unknown role
unknown phase
target root mismatch
```

## 11.5 Tests

```text
CRUU8_005_Empty_artifact_manifest_rejected
CRUU8_005_Invalid_SourceLibrarySha256_rejected
CRUU8_005_Missing_primary_metadata_rejected
CRUU8_005_Two_primary_metadata_artifacts_rejected
CRUU8_005_Source_hash_must_equal_primary_artifact_hash
CRUU8_005_Undefined_phase_rejected
CRUU8_005_Undefined_role_rejected
CRUU8_005_Noncanonical_dotdot_alias_rejected
CRUU8_005_Two_raw_paths_resolving_same_file_rejected
CRUU8_005_Ready_manifest_without_primary_cannot_unlock_startup
```

---

# 12. CRUU8-006 — Startup proceeds when completed marker cannot be retired

**Severity:** MEDIUM-HIGH

## 12.1 Current behavior

Startup:

```text
ReadyToCommit marker found
all listed artifacts match
try delete marker
if delete throws:
    residueResolved = true
continue startup
```

This is unsafe as a long-lived state.

## 12.2 Failure sequence

```text
settings already points target
Ready marker remains because deletion fails
startup verifies old payload hash
startup ignores marker deletion failure
application opens normally
user edits a prompt
marker still contains pre-edit prompt hash
user exits
next startup sees same marker
hash no longer matches
startup now fails closed
```

A transient cleanup problem has been converted into a delayed startup failure after legitimate user edits.

## 12.3 Required monotonic policy

Before exposing a writable main window:

```text
Ready marker payload valid
AND
Ready marker successfully retired
```

If marker delete fails:

```text
do not open writable UI
show:
"Migration completed and data is intact, but Prompt Helper could not retire its migration completion marker. No data was modified. Fix folder permissions and retry."
shutdown
```

Alternative accepted design:

```text
atomically rename Ready marker to a separate Completed tombstone
```

but only if that rename itself succeeds and startup ignores a completed tombstone safely.

Weak-model recommendation: require marker delete success.

## 12.4 Do not duplicate validation in App.xaml.cs

Create one service:

```csharp
MigrationStartupFinalizationService
```

that uses the same manifest validator and file ops as retry recovery.

## 12.5 Tests

```text
CRUU8_006_Ready_marker_delete_failure_blocks_writable_startup
CRUU8_006_Ready_marker_delete_failure_does_not_modify_payload
CRUU8_006_Ready_marker_success_is_deleted_before_library_service_is_exposed
CRUU8_006_User_edit_cannot_occur_while_ready_marker_remains
```

---

# 13. CRUU8-007 — Recovery is only top-level aware

**Severity:** MEDIUM-HIGH

## 13.1 Current scan

Current foreign detection enumerates:

```text
files directly under target root
```

It does not recursively inspect:

```text
prompts/
recovery/
```

## 13.2 Consequence

Nested unrecognized files can survive recovery.

Example:

```text
prompts\foreign.txt
prompts\.GUID.md.migration-ATTEMPT.tmp
```

Current recovery can:

```text
delete finals
leave nested file
leave directory because nonempty
delete migration marker
```

The marker is gone although target is not restored to baseline.

## 13.3 Required recursive inventory

Build:

```csharp
TargetRecoveryInventory
```

containing relative paths for **all** files/directories below target.

Categorize:

```text
AllowedPersistentBaseline
ManifestFinal
ManifestTemp
ReservationControl
Unknown
```

If any `Unknown` exists:

```text
do not delete manifest
do not delete unknown object
abort
```

After deleting manifest-owned content:

```text
re-enumerate
require only baseline/control entries remain
then delete marker
```

## 13.4 Tests

```text
CRUU8_007_Nested_foreign_prompt_file_blocks_recovery
CRUU8_007_Nested_foreign_recovery_file_blocks_recovery
CRUU8_007_Nested_attempt_temp_is_owned_and_removed
CRUU8_007_Unknown_nested_directory_blocks_marker_delete
CRUU8_007_Final_inventory_is_rechecked_before_marker_delete
```

---

# 14. CRUU8-008 — Precommit reservation cleanup failures are lost

**Severity:** MEDIUM-HIGH

## 14.1 Current branches

Several branches call `reservation.Release()` but discard or fail to surface the result.

Examples include:

```text
capability validation failure
final fingerprint mismatch
physical revalidation exception
settings CAS failure
interrupted recovery exception
target no longer empty
manifest creation failure
```

Some paths rely only on `using.Dispose()`.

`Dispose()` cannot return the structured cleanup result.

## 14.2 Required scope

Introduce:

```csharp
internal sealed class TargetReservationScope
```

or coordinator helper that guarantees:

```text
before commit:
    primary exception + cleanup failures are combined

after commit:
    cleanup failures become warnings
```

Model:

```csharp
internal sealed class TransitionPrecommitException
    : IOException
{
    public Exception PrimaryFailure { get; }
    public IReadOnlyList<MigrationRollbackFailure>
        CleanupFailures { get; }
}
```

## 14.3 Single cleanup owner

Avoid manual:

```csharp
reservation.Release();
throw;
```

throughout the coordinator.

Use one precommit/finally boundary.

## 14.4 Tests

Inject release failures at:

```text
physical revalidation
locked inspection mismatch
capability failure
final fingerprint mismatch
settings CAS failure
interrupted recovery failure
manifest-write failure
```

Every test must assert exact cleanup failure path and operation.

---

# 15. CRUU8-009 — Intermediate parent directories leak

**Severity:** MEDIUM

## 15.1 Example

Candidate:

```text
C:\Existing\NewA\NewB\Target
```

Only `C:\Existing` exists.

`Directory.CreateDirectory(Target)` creates:

```text
NewA
NewB
Target
```

Reservation records only:

```text
rootExistedBefore = false
```

Release deletes only:

```text
Target
```

After failed transition:

```text
NewA\NewB
```

can remain.

## 15.2 Required ownership list

Before creation:

```csharp
IReadOnlyList<string>
    GetMissingDirectoryChain(
        targetRoot);
```

Example:

```text
[
  C:\Existing\NewA,
  C:\Existing\NewA\NewB,
  C:\Existing\NewA\NewB\Target
]
```

After successful creation, reservation owns that exact list.

On precommit cleanup:

```text
delete deepest -> shallowest
only when empty
never delete pre-existing ancestor
```

After successful committed transition:

```text
do NOT delete target chain
```

## 15.3 Extend reservation file ops

Do not use static `Directory.CreateDirectory` outside the injected abstraction.

Add:

```text
CreateDirectory
```

to `IReservationFileOps`.

## 15.4 Tests

```text
CRUU8_009_Failed_nested_new_target_removes_all_created_parent_directories
CRUU8_009_Preexisting_parent_is_never_deleted
CRUU8_009_Nonempty_created_parent_is_reported_not_deleted
```

---

# 16. CRUU8-010 — Case sensitivity detection fails open

**Severity:** MEDIUM-HIGH

## 16.1 Current behavior

`WindowsDirectoryCaseSensitivityInspector.IsCaseSensitive` returns:

```csharp
false
```

when:

```text
CreateFileW returns invalid handle
GetFileInformationByHandleEx fails
```

But `false` means:

```text
successfully inspected and case insensitive
```

Those are not equivalent.

## 16.2 Required API

Use tri-state or throw.

Recommended:

```csharp
internal enum DirectoryCaseSensitivityState
{
    CaseInsensitive,
    CaseSensitive
}
```

API:

```csharp
DirectoryCaseSensitivityState Inspect(
    string existingDirectory);
```

Failure:

```csharp
throw new DirectoryCaseSensitivityInspectionException(
    path,
    win32Error);
```

## 16.3 Managed policy

On inspection failure:

```text
startup -> fail closed with controlled filesystem-policy error
transition -> fail closed before mutation
```

Do not guess case-insensitive.

## 16.4 Required native seam

Create:

```csharp
internal interface ICaseSensitivityNativeApi
```

so tests can inject:

```text
CreateFile failure
GetFileInformationByHandleEx failure
case flag set
case flag absent
```

## 16.5 Real Windows test

Add integration fixture using:

```powershell
fsutil file setCaseSensitiveInfo <dir> enable
```

Production must not shell out.

Verify `WindowsDirectoryCaseSensitivityInspector` detects it.

---

# 17. CRUU8-011 — Backup-only target still gets hard writeability requirement

**Severity:** MEDIUM

## 17.1 Root cause

For `RecoverableBackupOnly`:

```text
EffectiveMetadataPath = library.backup.json
```

Coordinator builds:

```text
ExistingLibraryCapabilityContext(
    EffectiveMetadataPath,
    EffectiveDocument)
```

Capability validator then treats `MetadataPath` as:

```text
"Primary library metadata MUST be writable"
```

Therefore a read-only backup-only target can fail before the later backup-warning policy.

## 17.2 Required capability context

Replace ambiguous context with:

```csharp
internal sealed record ExistingLibraryCapabilityContext(
    TargetLibraryKind Kind,
    string? PrimaryMetadataPath,
    string? SafetyBackupPath,
    LibraryDocument Document);
```

Rules:

### ValidPrimary

Hard:

```text
primary writable
active prompt bodies writable
root replace/create/delete capability
```

Soft:

```text
backup writable -> ideal
backup read-only/unwritable -> warning
future backup -> preserve, no write test
```

### RecoverableBackupOnly

Hard:

```text
backup readable
all referenced prompt bodies readable/writable as required for active editing after startup
root can create future primary
```

Do not require existing backup file itself to be writable.

Recovery can create `library.json`.

Backup synchronization failure remains a warning.

## 17.3 Tests

```text
CRUU8_011_Readonly_backup_only_target_is_selectable_with_warning
CRUU8_011_Backup_only_target_root_must_allow_primary_creation
CRUU8_011_Backup_only_unreadable_backup_still_rejected
```

---

# 18. CRUU8-012 — Capability future-schema detection diverges from repository authority

**Severity:** MEDIUM

## 18.1 Current ad-hoc logic

Capability validator uses roughly:

```csharp
JsonDocument.Parse(...)
TryGetProperty("schemaVersion", ...)
```

`TryGetProperty` is exact/case-sensitive.

Repository authority explicitly handles schemaVersion case-insensitively and rejects duplicates.

Example:

```json
{
  "SchemaVersion": 99,
  ...
}
```

is future according to repository schema authority.

Capability code may fail to recognize it as future.

A read-only future backup can then be reported as an ordinary unwritable backup.

## 18.2 Required shared inspector

Do not reimplement schema authority.

Expose an internal non-mutating helper from `LibraryRepository`:

```csharp
internal static LibraryMetadataCompatibility
    InspectCompatibility(string rawJson);
```

States:

```text
Current
Future(version)
Corrupt
```

Capability consumes that state.

Future:

```text
do not test backup writeability
preserve
optional warning explaining newer backup exists
```

## 18.3 Tests

```text
CRUU8_012_Case_variant_future_schema_backup_is_preserved
CRUU8_012_Duplicate_schema_properties_use_repository_authority
CRUU8_012_Malformed_backup_does_not_get_misidentified_as_future
```

---

# 19. CRUU8-013 — SettingsDialog can lose the forced process boundary after commit

**Severity:** MEDIUM-HIGH

## 19.1 Current order

After coordinator returns committed success:

```text
show success/warning UI
then
RestartRequired = result.RestartRequired
then
DialogResult = true
close
```

## 19.2 Failure

If `ShowInformation` or `ShowWarning` throws an operational exception:

```text
settings already changed
target already committed
RestartRequired still false
catch displays configuration error
dialog can remain open
MainWindow never requests shutdown
old source remains active in memory
```

That violates the central migration safety invariant.

## 19.3 Required order

Immediately after service returns:

```csharp
DataFolderTransitionResult result =
    _transitionService.RequestTransition(...);

if (result.Changed)
{
    // Product invariant:
    RestartRequired = true;
}
```

Do this **before any notification UI**.

Do not trust a malformed injected result with:

```text
Changed=true
RestartRequired=false
```

Product policy says any committed change needs restart.

Then display informational UI.

If notification UI fails:

```text
do not reinterpret transition as failed
close dialog
return success boundary
MainWindow must still request shutdown
```

## 19.4 Recommended helper

```csharp
private void CompleteCommittedTransition(
    DataFolderTransitionResult result)
{
    RestartRequired = true;

    try
    {
        ShowTransitionNotice(result);
    }
    catch
    {
        // Optional log only; do not lose restart requirement.
    }

    TrySetDialogResultTrue();
    Close();
}
```

## 19.5 Tests

Use fake confirmation service that throws from:

```text
ShowInformation
ShowWarning
```

Assertions:

```text
RestartRequired == true
dialog closes
settings stays target
no generic "previous folder unchanged / no change committed" semantics
MainWindow shutdown requested
```

---

# 20. CRUU8-014 — CRUU7 tests overstate several behaviors

**Severity:** MEDIUM verification gap

The 373-test claim may be true as a raw count.

Several important CRUU7 tests still do not prove the named behavior.

## 20.1 Postcommit cleanup

Test named around postcommit cleanup currently performs a normal successful transition.

It does not inject reservation cleanup failure.

Required:

```text
fake reservation ops delete failure
real coordinator commit
assert Changed=true
assert RestartRequired=true
assert warning includes exact cleanup failure
```

## 20.2 SettingsDialog

Current tests construct a dialog and sometimes call the fake transition service directly.

That does not invoke:

```text
SaveButton_Click
dialog catch blocks
postcommit completion path
```

Required:

```text
invoke Save button routed command/click on WpfTestHost
or expose an internal ExecuteSaveForTest helper containing the exact production logic
```

Do not duplicate production logic in the test.

## 20.3 Crash recovery

Current tests cover manifest finals.

They do not cover:

```text
manifest temp
partial temp
marker-delete-before-rollback
default bootstrap baseline
nested foreign files
Ready marker delete failure
```

## 20.4 Case sensitivity

Only fake inspector tests exist.

No real Windows case-sensitive-directory test exists in the Windows integration file.

## 20.5 Release identity

Current CRUU7 release tests assert script text contains strings such as:

```text
$SourceSvg
$OutputIco
$requiredSizes
256
```

This is not runtime release verification.

## 20.6 Rule

A CRUU8 test may not claim:

```text
cleanup failure
dialog handling
durability
icon identity
Windows filesystem behavior
```

without causing/observing that behavior.

---

# 21. CRUU8-015 — CI does not enforce required integration evidence

**Severity:** MEDIUM verification gap

## 21.1 Current workflow

CI:

```text
restore
build
dotnet test
publish
basic release script
```

It does not assert:

```text
required Windows junction tests executed
real case-sensitive test executed
no required test skipped
CRUU8 recovery fixture group executed
strict exact icon identity executed
```

## 21.2 Add named integration categories

MSTest traits:

```csharp
[TestCategory("WindowsFilesystemIntegration")]
[TestCategory("WpfIntegration")]
[TestCategory("CrashRecovery")]
```

CI separate commands:

```powershell
dotnet test ... --filter "TestCategory=WindowsFilesystemIntegration"
dotnet test ... --filter "TestCategory=WpfIntegration"
dotnet test ... --filter "TestCategory=CrashRecovery"
```

This makes "no tests discovered" fail instead of silently hiding missing evidence.

## 21.3 TRX evidence

Add script:

```text
tools/VerifyTestEvidence.ps1
```

Require named sentinel tests to appear and pass.

Examples:

```text
real junction
real case-sensitive directory
SettingsDialog future schema
SettingsDialog postcommit notification failure
manifest temp crash recovery
default-root interrupted recovery
```

---

# 22. CRUU8-016 — Exact icon identity remains unimplemented

**Severity:** MEDIUM release gap

## 22.1 Current CRUU7 implementation

`GenerateAppIcon.ps1` now accepts custom source/output paths.

Useful, but not the CRUU7-017 acceptance requirement.

`VerifyReleaseAssets.ps1` still proves:

```text
ICO structurally valid
required sizes exist
published EXE has >= 1 icon group
```

It does not prove:

```text
committed ICO corresponds to current SVG
published EXE icon frames correspond to committed ICO
```

## 22.2 Strict SVG → ICO verification

In strict gate:

```text
generate a temporary ICO from current SVG
compare expected frame pixel hashes to committed ICO
```

Prefer decoded pixel hashes rather than full ICO byte equality because compression/container output can differ across ImageMagick versions.

For each required size:

```text
16
24
32
48
64
128
256
```

decode to 32-bit RGBA and hash normalized pixels.

## 22.3 ICO → EXE verification

Use Windows resource APIs to enumerate:

```text
RT_GROUP_ICON
RT_ICON
```

Load embedded frame payloads.

Decode/normalize each required frame and compare pixel hashes to committed ICO.

`ExtractIconEx(..., -1)` count is only a presence check and may remain as an additional check.

## 22.4 Tests

Use fixtures:

```text
known ICO A
known different ICO B
test EXE containing A
```

Require:

```text
A vs A pass
A vs B fail
EXE(A) vs A pass
EXE(A) vs B fail
```

---

# 23. CRUU8-017 — `default` still means Empty

**Severity:** LOW-MEDIUM

Production switch contains:

```text
case Empty:
default:
    HandleEmptyTargetTransition
```

CRUU7 explicitly wanted unknown states to fail closed.

Replace with:

```csharp
case TargetLibraryKind.Empty:
    ...

default:
    throw new InvalidOperationException(
        $"Unsupported target-library state: {initialInspection.Kind}.");
```

The internal `PrepareTargetForMigrationUnitTest` is worse because new states such as:

```text
Unreadable
Unstable
InterruptedMigration
```

can fall into its Empty-copy path.

Either:

```text
remove this helper and test coordinator directly
```

or make its switch exhaustive.

Tests:

```text
CRUU8_017_Internal_helper_rejects_interrupted_target
CRUU8_017_Internal_helper_rejects_unreadable_target
CRUU8_017_Internal_helper_rejects_unstable_target
```

---

# 24. CRUU8-018 — Ignored compatibility constructor

**Severity:** LOW-MEDIUM

Current public constructor:

```csharp
public DataRootCapabilityValidator(
    IAtomicTextWriter? writer)
    : this((ICapabilityFileOps?)null)
{
}
```

The argument is ignored.

A caller reasonably expects the supplied writer to affect validation.

That can produce false tests or broken dependency injection.

Required:

```text
remove the constructor if no longer needed
```

or:

```text
mark obsolete and implement a real adapter
```

Do not retain a public parameter that has no effect.

Test API surface if retained.

---

# 25. CRUU8-019 — Recovery verifies one pathname version and can delete another

**Severity:** MEDIUM-HIGH paranoid safety

## 25.1 Current sequence

Recovery:

```text
ReadAllBytes(path)
hash verified
close file
...
DeleteFile(path)
```

External actor can replace the pathname after verification and before deletion.

Then Prompt Helper deletes bytes it never verified.

This violates the strongest interpretation of:

```text
delete only exact manifest-owned unchanged content
```

## 25.2 Required Windows handle-bound delete

Create recovery primitive:

```csharp
internal interface IVerifiedArtifactDeleter
{
    void VerifyAndDelete(
        string path,
        long expectedLength,
        ReadOnlySpan<byte> expectedSha256);
}
```

Windows implementation:

1. `CreateFileW` with read + delete access.
2. Use sharing flags that prevent other writers/replacers for the verification/delete interval.
3. Hash from the same open handle.
4. Validate length/hash.
5. Mark the **same handle identity** for deletion using `SetFileInformationByHandle` and file disposition information.
6. Close handle.

Do not:

```text
hash path
close
delete path later
```

If handle-bound deletion is judged too invasive, minimum accepted fallback is:

```text
verify
obtain FileIdInfo
reopen for delete
obtain FileIdInfo again
require same volume/file ID
re-verify hash
delete
```

but handle-bound deletion is stronger.

## 25.3 Tests

Use injected file-op seam to simulate:

```text
file A verified
path replaced by file B
```

Expected:

```text
B is never deleted
marker remains
recovery fails closed
```

---

# 26. CRUU8-020 — Real logo still absent

**Severity:** RELEASE BLOCKER

Repository still has no accessible:

```text
src/PromptHelper/Assets/PromptHelperLogo.svg
src/PromptHelper/Assets/PromptHelper.ico
```

README still says release asset pending.

Do not fabricate.

Until provided:

```text
normal product/code testing can continue
strict release acceptance cannot complete
```

---

# 27. New recovery architecture

Create a single service:

```csharp
internal sealed class MigrationRecoveryService
```

Do not maintain separate ad-hoc logic in:

```text
DataFolderTransitionCoordinator
App.xaml.cs
```

API:

```csharp
public RecoveryResult RecoverForRetry(
    MigrationRecoveryContext context);

public RecoveryResult FinalizeCommittedStartup(
    MigrationRecoveryContext context);
```

## Retry semantics

Settings still point source.

Allowed:

```text
Copying
ReadyToCommit
```

Action:

```text
verify marker authority
verify baseline
delete manifest-defined temps
verify/delete exact manifest finals
restore empty baseline
delete marker LAST
```

## Startup semantics

Settings point target.

Only:

```text
ReadyToCommit
```

is accepted.

Action:

```text
verify target identity
verify all finals
require no declared temps
require no unknown files outside allowed baseline/payload
retire marker
only then expose library
```

`Copying` at configured target:

```text
fail closed
```

---

# 28. Empty target inspector

Create:

```csharp
internal sealed record EmptyTargetBaselineInspection(
    bool IsAcceptable,
    IReadOnlyList<string> UnexpectedEntries);
```

Inputs:

```text
target physical root
bootstrap physical root
reservation active yes/no
```

Allowed exact bootstrap control names:

```text
settings.json
settings.backup.json
.settings.lock
```

Allowed transition control:

```text
.app.lock
.prompthelper-migration.json   only when recovery path expects it
```

Allowed dirs:

```text
prompts   only if empty
recovery  only if empty
```

Anything else rejects Empty migration.

---

# 29. Manifest version upgrade

CRUU8 changes manifest structure.

Do **not** silently reinterpret schema 1 if fields become mandatory.

Recommended:

```text
MigrationAttemptManifest.CurrentSchemaVersion = 2
```

Schema 1 residue from CRUU7 must be handled conservatively.

Two choices:

### Safer weak-model choice

```text
schema 1 marker:
    fail closed
    explain marker was produced by older crash-recovery protocol
    do not auto-delete
```

### Optional explicit migration

Only if you can reconstruct temp ownership safely.

Because schema 1 lacks declared temp paths, automatic recovery cannot prove temp ownership fully.

Therefore **fail closed for schema 1** is recommended.

This is a migration-manifest schema only; library/settings schema remain 1.

---

# 30. Manifest v2 shape

```csharp
internal sealed class MigrationAttemptManifest
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = 2;

    public Guid AttemptId { get; set; }

    public string SourcePhysicalRoot { get; set; } = "";

    public string TargetPhysicalRoot { get; set; } = "";

    public bool TargetIsBootstrapRoot { get; set; }

    public string SourceLibrarySha256Hex { get; set; } = "";

    public MigrationManifestPhase Phase { get; set; }

    public List<MigrationManifestArtifact> Artifacts { get; set; } = [];
}

internal sealed class MigrationManifestArtifact
{
    public string RelativePath { get; set; } = "";

    public string TempRelativePath { get; set; } = "";

    public string Sha256Hex { get; set; } = "";

    public long Length { get; set; }

    public MigrationPayloadRole Role { get; set; }
}
```

Do not persist arbitrary environment state.

---

# 31. Correct precommit catch template

```csharp
catch (Exception original)
{
    if (settingsCommitted)
    {
        // No rollback after point of no return.
        postCommitWarnings.Add(
            $"Unexpected postcommit issue: {original.Message}");
    }
    else
    {
        MigrationRollbackResult rollback =
            tx.Rollback();

        TargetReservationCleanupResult reservationResult =
            reservation.Release();

        bool payloadResiduePossible =
            !rollback.Success ||
            HasDeclaredAttemptObjects(
                markerPath,
                manifest);

        if (!payloadResiduePossible)
        {
            try
            {
                manifestRepo.DeleteDurable(markerPath);
            }
            catch (Exception markerEx)
            {
                throw new MigrationRollbackException(
                    original,
                    bound.PhysicalRoot,
                    CombineFailures(
                        rollback,
                        reservationResult,
                        MarkerFailure(markerEx)));
            }
        }

        var failures =
            CombineFailures(
                rollback,
                reservationResult);

        if (failures.Count > 0)
        {
            // marker intentionally remains if ownership residue may remain.
            throw new MigrationRollbackException(
                original,
                bound.PhysicalRoot,
                failures);
        }

        throw;
    }
}
```

This is pseudocode shape; adapt cleanly.

---

# 32. Correct startup finalization template

```csharp
RecoveryResult recovery =
    migrationRecovery.FinalizeCommittedStartup(
        new MigrationRecoveryContext(
            targetPhysicalRoot,
            bootstrapPhysicalRoot));

if (!recovery.Success)
{
    ShowMigrationRecoveryError(...);
    Shutdown();
    return;
}

// Only after marker was retired successfully:
paths.EnsureDataDirectories();
LoadLibrary();
ShowMainWindow();
```

Do not set success when marker deletion failed.

---

# 33. WPF committed-result invariant

Centralize:

```csharp
private void HandleTransitionResult(
    DataFolderTransitionResult result)
{
    if (!result.Changed)
    {
        HandleNoChange(result);
        return;
    }

    // Point-of-no-return is already behind us.
    RestartRequired = true;

    try
    {
        ShowCommittedTransitionMessage(result);
    }
    catch
    {
        // Optional debug logging.
        // Do NOT clear RestartRequired.
    }

    try
    {
        DialogResult = true;
    }
    catch (InvalidOperationException)
    {
    }

    Close();
}
```

The generic precommit exception catch must never execute for a successfully returned committed result.

---

# 34. Required CRUU8 test matrix

## Manifest lifetime

```text
CRUU8_001_Rollback_failure_preserves_manifest
CRUU8_001_Successful_rollback_deletes_manifest_last
CRUU8_001_Crash_window_between_manifest_delete_and_rollback_eliminated
```

## Temp ownership

```text
CRUU8_002_Root_metadata_partial_temp_recoverable
CRUU8_002_Prompt_partial_temp_recoverable
CRUU8_002_Recovery_deletes_exact_declared_temp
CRUU8_002_Other_attempt_temp_preserved
```

## Empty/default baseline

```text
CRUU8_003_Custom_target_notes_file_rejected
CRUU8_003_Default_root_settings_files_allowed
CRUU8_003_Default_root_interrupted_migration_recovers
CRUU8_003_Settings_files_never_deleted_by_recovery
```

## Durable manifest

```text
CRUU8_004_Copying_marker_write_through
CRUU8_004_Ready_marker_replace_write_through
CRUU8_004_Failed_manifest_promotion_no_temp_residue
```

## Manifest semantics

```text
CRUU8_005_Empty_artifacts_rejected
CRUU8_005_Invalid_source_hash_rejected
CRUU8_005_Primary_required
CRUU8_005_Duplicate_resolved_path_rejected
CRUU8_005_Undefined_role_rejected
```

## Startup

```text
CRUU8_006_Marker_delete_failure_blocks_main_window
CRUU8_006_Marker_retired_before_first_edit
```

## Recursive recovery

```text
CRUU8_007_Nested_foreign_file_blocks_recovery
CRUU8_007_Nested_attempt_temp_recovered
CRUU8_007_Final_inventory_recheck
```

## Cleanup

```text
CRUU8_008_Revalidation_failure_reports_release_cleanup
CRUU8_008_Capability_failure_reports_release_cleanup
CRUU8_008_Settings_CAS_failure_reports_release_cleanup
```

## Directories

```text
CRUU8_009_Deep_nested_target_parent_chain_removed_on_failure
```

## Case sensitivity

```text
CRUU8_010_Native_case_query_failure_is_fail_closed
CRUU8_010_Real_case_sensitive_NTFS_directory_rejected
```

## Capability role

```text
CRUU8_011_Readonly_backup_only_target_allowed_with_warning
CRUU8_012_Case_variant_future_backup_preserved
```

## UI

```text
CRUU8_013_ShowInformation_failure_still_requests_restart
CRUU8_013_ShowWarning_failure_still_requests_restart
CRUU8_013_Committed_result_cannot_enter_precommit_error_semantics
```

## Recovery identity

```text
CRUU8_019_Path_replaced_after_hash_is_not_deleted
```

---

# 35. Test implementation rules

1. No broad `Assert.Throws<Exception>` for safety contracts.
2. Exact exception type required.
3. Exact residue path required.
4. Exact operation required.
5. No source-text grep in place of runtime behavior when runtime behavior is testable.
6. No arbitrary sleeps.
7. Use synchronization gates/callbacks.
8. Every crash fixture begins from an explicit manifest+disk state.
9. Every recovery test inventories the target afterward.
10. Every precommit failure asserts settings unchanged.
11. Every postcommit warning asserts restart required.
12. Windows-specific tests must be separately discoverable in CI.
13. A skipped mandatory Windows integration test is missing evidence, not PASS.

---

# 36. Full crash fixture table

| Stage | Marker | Temp | Final | Settings | Required retry/startup |
|---|---|---|---|---|---|
| Before Copying marker | none | none | none | source | normal |
| Copying marker only | Copying | none | none | source | delete marker last, retry |
| Partial manifest temp before marker publish | no final marker | manifest temp | none | source | temp writer cleans/fails explicitly |
| Copying + library temp partial | Copying | declared partial | none | source | delete declared temp, retry |
| Copying + library final | Copying | none | primary | source | verify/delete final, retry |
| Copying + subset prompts | Copying | maybe | subset | source | cleanup exact owned set |
| Full payload + Copying | Copying | none | all | source | cleanup exact owned set, retry |
| Ready + full payload | Ready | none | all | source | cleanup/retry if user reselects target |
| Ready + full payload | Ready | none | all | target | startup verifies, retires marker, opens |
| Ready + marker cannot delete | Ready | none | all | target | block writable startup |
| Ready + prompt modified | Ready | none | modified | target | fail closed |
| Ready + unknown nested file | Ready | none | all + foreign | target | fail closed / manual review |
| Schema v1 old marker | v1 | unknown | unknown | any | fail closed; no auto-delete |

---

# 37. Failure invariants before settings commit

After any precommit failure:

```text
settings primary still identifies old source
settings backup not silently changed except legitimate pre-snapshot recovery
source library bytes intact
source prompt bytes intact
source backup intact
source recovery artifacts intact
migration marker remains if any attempt-owned residue may remain
no manifest-owned temp/final exists without a marker
foreign target content untouched
bootstrap settings untouched
cleanup failure explicitly reported
```

---

# 38. Success invariants after settings commit

After settings commit:

```text
settings points intended lexical target
target physical payload matches manifest snapshot
manifest phase was ReadyToCommit before settings commit
no attempt temp remains
Changed == true
RestartRequired == true
postcommit cleanup failures are warnings only
old source is never edited again by current process
```

Before the next process exposes the library:

```text
Ready marker successfully retired
OR startup fails closed
```

---

# 39. File-by-file change map

## `DataFolderTransitionCoordinator.cs`

Must:

```text
preserve marker through rollback
centralize precommit cleanup
pass bootstrap recovery baseline context
use manifest v2 declared temp paths
remove default=>Empty switch behavior
preserve postcommit warnings
```

## `MigrationAttemptManifest.cs`

Must:

```text
schema v2
TempRelativePath
strict role/phase semantics
bootstrap-target flag if used
```

## `MigrationManifestRepository.cs`

Must:

```text
strict semantic invariants
canonical path identity
source hash validation
write-through final marker promotion
failed temp cleanup
DeleteDurable or explicit deletion result
```

## `MigrationTargetRecoveryService.cs`

Prefer replace with:

```text
MigrationRecoveryService
```

Must:

```text
recursive inventory
bootstrap baseline awareness
declared temp cleanup
final inventory recheck
marker delete last
handle-bound verified deletion
separate Retry vs Startup finalization semantics
```

## `DataFolderMigrationService.cs`

Must:

```text
consume manifest-declared temp path
no random hidden temp path generation
exhaustive target-state switch
```

## `TargetRootReservation.cs`

Must:

```text
track full created directory chain
injected CreateDirectory
structured cleanup all paths
```

## `WindowsDirectoryCaseSensitivityInspector.cs`

Must:

```text
throw on native inspection failure
never return false because inspection failed
native API seam
```

## `DataRootCapabilityValidator.cs`

Must:

```text
metadata role-aware context
shared schema compatibility inspector
remove ignored writer constructor
```

## `SettingsDialog.xaml.cs`

Must:

```text
set RestartRequired immediately on committed Changed result
postcommit notification failure cannot reenter precommit error semantics
```

## `App.xaml.cs`

Must:

```text
delegate migration marker finalization
no duplicate ad-hoc hash loop
no writable startup if marker retirement fails
```

## CI

Must:

```text
explicit integration categories
evidence verification
exact release identity gate when real logo exists
```

---

# 40. Weak-model forbidden shortcuts

Do not:

```text
delete manifest before rollback
delete manifest when cleanup result is unknown
recover temps with a broad wildcard not scoped to exact attempt
ignore nested target content
treat settings.json as foreign when target is exact bootstrap root
permit arbitrary unrelated files while calling target Empty
write manifest temp with Flush(true) then use non-write-through promotion
accept empty ReadyToCommit artifact list
leave SourceLibrarySha256Hex unvalidated
accept noncanonical manifest aliases
continue startup when Ready marker cannot be deleted
swallow precommit reservation cleanup failures
return false from case inspector on native failure
require backup-only safety backup to be writable
reimplement schema authority with case-sensitive TryGetProperty
show success UI before protecting RestartRequired
claim a dialog test when production Save handler never ran
claim cleanup failure test without injecting cleanup failure
claim icon identity from source-string assertions
map unknown target enum values to Empty
retain an ignored dependency-injection constructor
hash a path and later delete a potentially replaced path
fabricate the release logo
```

---

# 41. Verification commands

Run on Windows.

```powershell
git rev-parse HEAD
git status --short
dotnet --info
pwsh --version
```

Expected implementation baseline starts from or includes:

```text
8c86f14c2db031b55f15ea051720358f4a4a45dd
```

Restore:

```powershell
dotnet restore PromptHelper.slnx
```

Release build:

```powershell
dotnet build PromptHelper.slnx `
  -c Release `
  --no-restore
```

Full tests:

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --logger "trx;LogFileName=cruu8-full.trx"
```

Mandatory categories separately:

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --filter "TestCategory=CrashRecovery"

dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --filter "TestCategory=WindowsFilesystemIntegration"

dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --filter "TestCategory=WpfIntegration"
```

Five runs:

```powershell
1..5 | ForEach-Object {
    Write-Host "CRUU8 full-suite run $_"

    dotnet test PromptHelper.slnx `
      -c Release `
      --no-build `
      --logger "trx;LogFileName=cruu8-run-$_.trx"

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
```

Publish:

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

---

# 42. Manual regression after automated pass

```text
normal startup
create/edit/move/copy/delete prompt
category create/rename/delete
recent-copy bar
same-folder no-op
empty custom target transition
existing target cancel
existing target confirm
future target rejection
read-only primary rejection
read-only valid-primary backup warning
read-only backup-only selection
custom -> exact default root transition
persisted junction alias transition
unavailable current configured path
```

Then CRUU8 recovery fixtures:

```text
Copying marker only
root metadata temp
prompt temp
partial final payload
Ready before settings
Ready after settings
Ready marker deletion denied
default-root interrupted migration
nested foreign target file
old schema-v1 marker
```

---

# 43. Release asset commands

Before real logo exists:

```powershell
pwsh ./tools/VerifyReleaseAssets.ps1
```

Strict release remains blocked.

After approved SVG exists:

```powershell
pwsh ./tools/GenerateAppIcon.ps1

pwsh ./tools/VerifyReleaseAssets.ps1 `
  -RequireIcon

dotnet publish `
  src/PromptHelper/PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/publish-check

pwsh ./tools/VerifyReleaseAssets.ps1 `
  -RequireIcon `
  -PublishedExe `
  artifacts/publish-check/PromptHelper.exe
```

CRUU8 requires this verifier to include exact normalized frame identity, not only icon-count presence.

---

# 44. Required implementation evidence report

```text
CRUU8 IMPLEMENTATION EVIDENCE

BASELINE
- branch:
- starting commit:
- final commit:

CRUU8 FINDINGS
- 001:
- 002:
- 003:
- 004:
- 005:
- 006:
- 007:
- 008:
- 009:
- 010:
- 011:
- 012:
- 013:
- 014:
- 015:
- 016:
- 017:
- 018:
- 019:
- 020:

BUILD
- exact command:
- exit:
- warnings:
- errors:

FULL TEST
- exact total:
- passed:
- failed:
- skipped:
- duration:

MANDATORY CATEGORIES
- CrashRecovery discovered:
- CrashRecovery passed:
- WindowsFilesystemIntegration discovered:
- WindowsFilesystemIntegration passed:
- WpfIntegration discovered:
- WpfIntegration passed:

FIVE RUNS
- run 1:
- run 2:
- run 3:
- run 4:
- run 5:

CRASH FIXTURES
- manifest only:
- root temp:
- prompt temp:
- partial finals:
- ready pre-settings:
- ready post-settings:
- marker delete denied:
- default bootstrap target:
- nested foreign:
- schema-v1 marker:

PUBLISH
- exit:
- PromptHelper.exe:
- LICENSE:
- THIRD_PARTY_NOTICES.md:

ICON
- approved SVG present:
- ICO present:
- SVG->ICO normalized frame identity:
- ICO->EXE normalized frame identity:
- strict gate:
```

---

# 45. CRUU8 definition of done

Product/code clean only when:

```text
[ ] Manifest survives every precommit cleanup failure.
[ ] Marker is deleted only after owned residue is gone.
[ ] Every temp path is declared before copy.
[ ] Retry recovery handles partial root temp.
[ ] Retry recovery handles partial nested prompt temp.
[ ] Recovery never deletes temp from another attempt.
[ ] Empty custom target means actually empty.
[ ] Exact bootstrap target permits only known bootstrap control files.
[ ] Interrupted migration back to default root is recoverable.
[ ] Bootstrap settings files are never migration-owned/deleted.
[ ] Copying marker publication is write-through.
[ ] Ready marker replacement is write-through.
[ ] Manifest writer cleans failed temp files.
[ ] Manifest artifacts nonempty.
[ ] Exactly one library.json primary artifact exists.
[ ] SourceLibrarySha256Hex is valid and matches primary.
[ ] Roles/phases defined.
[ ] Relative paths canonical.
[ ] Duplicate resolved paths rejected.
[ ] Startup validates target root.
[ ] Writable startup requires Ready marker retirement.
[ ] Recursive target inventory detects nested foreign content.
[ ] Final inventory rechecked before marker delete.
[ ] All precommit reservation cleanup failures surfaced.
[ ] All newly created target parent directories tracked.
[ ] Case sensitivity inspection failures fail closed.
[ ] Real case-sensitive Windows test executes.
[ ] Backup-only read-only safety backup does not falsely block selection.
[ ] Future-backup compatibility uses shared repository authority.
[ ] Committed SettingsDialog result protects RestartRequired before UI.
[ ] Notification failure cannot keep old source active.
[ ] Crash recovery tests exercise actual temp/failure states.
[ ] WPF tests execute production Save path.
[ ] CI proves mandatory integration tests were discovered.
[ ] Unknown target enum state does not map to Empty.
[ ] Ignored capability constructor removed/fixed.
[ ] Recovery deletion is bound to verified file identity.
[ ] Release build passes.
[ ] Full suite passes.
[ ] Five full runs pass.
[ ] Self-contained win-x64 publish passes.
```

Strict release additionally:

```text
[ ] real approved PromptHelperLogo.svg
[ ] generated ICO
[ ] normalized SVG->ICO frame identity
[ ] normalized ICO->EXE frame identity
[ ] manual Explorer icon
[ ] manual taskbar icon
[ ] manual Alt+Tab icon
[ ] manual window icon
```

---

# 46. Copy-ready implementation prompt

```text
ROLE
You are the implementation model for Prompt Helper CRUU8.

AUDITED BASELINE
main commit:
8c86f14c2db031b55f15ea051720358f4a4a45dd

INPUT
- repository
- cruu8.md
- prior CRUU documents only as historical context

GOAL
Close CRUU8-001 through CRUU8-019.
CRUU8-020 is an external release-logo dependency and MUST NOT be fabricated.

MANDATORY ORDER
A preserve manifest through rollback
B declare/recover attempt temp paths
C strict Empty target + default bootstrap baseline
D make manifest publication/replacement truly durable
E harden manifest semantic invariants
F unify retry/startup recovery
G cleanup truth + full created-directory ownership
H fail-closed case-sensitivity inspection
I capability metadata role/schema authority
J WPF postcommit shutdown monotonicity
K handle-bound verified recovery deletion
L exact behavioral tests + CI evidence
M exact icon identity only if real approved SVG exists

NON-NEGOTIABLE
- Never delete the migration marker before rollback proves payload ownership is cleared.
- Never leave attempt-owned payload without a marker.
- Never delete target content that cannot be proven attempt-owned.
- Never wildcard-delete another attempt's temp.
- Never treat default bootstrap settings files as migration payload.
- Never treat an arbitrary non-library folder as Empty.
- Never publish a Copying marker with a non-write-through rename.
- Never accept an empty ReadyToCommit manifest.
- Never continue normal writable startup when the Ready marker cannot be retired.
- Never discard precommit cleanup failure details.
- Never return case-insensitive when the native case-sensitivity query failed.
- Never require a backup-only safety backup to be writable merely because it is the effective metadata source.
- Never reimplement schema authority inconsistently.
- Never allow postcommit notification UI to clear/avoid RestartRequired.
- Never claim a safety behavior from a source-string test.
- Never map unknown target states to Empty.
- Never hash one path version and later delete another unverified replacement.
- Never fabricate PromptHelperLogo.svg.

TESTING
Implement direct regression tests for every CRUU8 finding.
Use deterministic injected filesystem operations.
Create exact crash fixtures for temp/final/marker states.
Run Windows filesystem integration separately.
Exercise the actual SettingsDialog Save path.
Run full suite five consecutive times.
Publish self-contained win-x64.

EVIDENCE
Return the exact CRUU8 IMPLEMENTATION EVIDENCE report from cruu8.md.
Do not claim any test/build/publish/manual result that did not actually run.
```

---

# 47. Final audit verdict

At:

```text
8c86f14c2db031b55f15ea051720358f4a4a45dd
```

CRUU7 is an important improvement but **not final**.

The next repair is narrower in concept than CRUU7:

```text
MAKE THE MANIFEST ITSELF TRANSACTIONAL.
```

Specifically:

```text
ownership marker must outlive rollback risk
temp ownership must be declared
empty baseline must be deterministic
manifest promotion must be durable
manifest semantics must be strict
startup and retry must use one recovery authority
postcommit state must never regress
```

Once CRUU8 closes these findings, another audit should focus less on adding safety mechanisms and more on proving that the now-complete state machine has no contradictory terminal states.

Strict release remains separately blocked by the missing approved logo asset.
