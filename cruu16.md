# CRUU16 — Independent Post-CRUU15 Adversarial Re-Audit and Fix Plan

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited HEAD:** `3875cdc072abae3e22fb1da1ef56e0881c877c2b`  
**Previous audit:** `cruu15.md`  
**Audit date:** 2026-08-23  
**Mode:** independent source, recovery-state, ownership-authority, destructive-cleanup, test-evidence, CI/release-path audit.

> Report only. No production source was modified by this audit.

# 1. Executive verdict

The CRUU15 repair round is a **major improvement**.

The core compare-and-swap path is no longer the CRUU14-style “verify, close, then write” sequence. The current production code holds an expected-target authority through the swap, stages replacement bytes behind a retained handle, enforces expected-missing with no-overwrite promotion, and protects the previous committed object with a durable pre-image record.

The process-runner hang fix is also materially correct now: both pipes are drained concurrently, the entire child tree is killed on timeout, and post-kill drains are themselves bounded.

The finding-coverage map, pinned icon renderer, strict release workflow, mandatory symlink tests, and all-group PE icon verification are all real improvements.

However, **zero-defect acceptance still cannot be granted at this HEAD**.

The main reason is no longer the old CAS race. It is the crash-recovery protocol introduced to make the new two-rename CAS recoverable.

The most serious defect is:

> A CAS pre-image is auto-deleted on restart merely because *something* exists at the destination name. Recovery does not prove that the destination object is the candidate replacement that belonged to the interrupted CAS.

That permits the previous committed content to be destroyed after a crash if a foreign object appears at the target pathname before restart.

The second cluster is the new `.prompthelper-owned.log` authority itself:

- malformed complete records are silently ignored;
- the journal file may be followed through a reparse point;
- journal rewrite/delete is not bound to the journal object that was read;
- failures restoring pre-images are often treated as cleanup warnings or discarded completely.

The third cluster is migration:

- ownership is recorded for the staging pathname;
- after promotion the record is simply retired because the staging pathname is missing;
- the final object does **not** inherit durable ownership identity;
- retry recovery and in-process rollback still delete final artifacts using hash/length alone;
- in-process rollback still removes attempt-created directories with pathname enumeration + `Directory.Delete`.

```text
AUDITED_HEAD                       = 3875cdc072abae3e22fb1da1ef56e0881c877c2b

SOURCE_AUDIT_CLEAN                 = NO
CRUU15_ALL_FINDINGS_FIXED          = NO
NEW_OR_REOPENED_FINDINGS           = 8

CRITICAL                           = 1
HIGH                               = 5
MED_HIGH                           = 2

WINDOWS_TESTS_DIRECTLY_EXECUTED    = NO
IMPLEMENTER_REPORTED_FULL_SUITE    = 633/633 × 5
IMPLEMENTER_REPORTED_SENTINELS     = 413 exact sentinels
INDEPENDENT_ATTACHED_CI_STATUS     = NOT_OBTAINED
RELEASE_WORKFLOW_SOURCE_PRESENT    = YES
RELEASE_TAG_WORKFLOW_EXECUTED      = NOT VERIFIED / USER STATES NOT YET RUN

STRICT_RELEASE_READY               = NO
ZERO_DEFECT_VERIFIED               = NO
```

The available GitHub combined-status query exposes no statuses for the exact audited SHA, and the available commit-workflow lookup exposes no runs for this commit. Therefore the reported five clean Release runs are retained as strong implementation evidence, but they are not independent attached CI evidence in this audit environment.

# 2. CRUU15 closure matrix

| CRUU15 | CRUU16 status | Assessment |
|---|---|---|
| 001 | **CORE FIXED / residual wiring issue** | Migration manifest and payload promotion now use retained owned-stage handles. Universal physical-root binding is not actually wired through every production stage creator. See CRUU16-007. |
| 002 | **CORE FIXED / residual provenance issue** | Migration payload promotion is same-handle, but durable ownership is not transferred from temp pathname to final pathname. See CRUU16-005. |
| 003 | **CORE CAS FIXED / RECOVERY REOPENED** | The live CAS race is closed. The crash-recovery protocol has an ambiguous target-present state that can delete the pre-image. See CRUU16-001. |
| 004 | **LIVE CAS FIXED / RECOVERY HANDLING PARTIAL** | Expected-present/missing writes are atomic at runtime, but unresolved CAS recovery can be ignored or downgraded. See CRUU16-004. |
| 005 | **FIXED_SOURCE_CORE** | Strict retirable control files are now same-handle, reparse-safe, and root-bound. |
| 006 | **PARTIAL / REOPENED** | Retry temp/control cleanup gained provenance, but migrated finals and in-process rollback still use weaker destructive authority. See CRUU16-005/006. |
| 007 | **PARTIAL / REOPENED** | Unproven temps are preserved, but the ownership journal itself is not yet a trustworthy strict authority. See CRUU16-002/003/004. |
| 008 | **FIXED_SOURCE_CORE** | Inventory now enumerates through a directory handle and cross-checks entry kind; destructive authority should remain separate. |
| 009 | **STRUCTURE FIXED / SEMANTIC GAPS REMAIN** | Coverage completeness now has an external authority, but some mapped tests still do not execute the production behavior their names claim. See CRUU16-008. |
| 010 | **FIXED_SOURCE** | `ProcessTestRunner` now has bounded timeout, process-tree kill, and bounded drain. |
| 011 | **FIXED_SOURCE / RELEASE RUN PENDING** | Pinned renderer and tag release gate exist. The release workflow itself remains unexercised until a `v*` tag is pushed. |
| 012 | **PARTIAL** | Write-through handle primitive exists and is tested directly, but production writers/migration adapters still call root-unbound `CreateNew`. See CRUU16-007. |

# 3. Findings

## CRUU16-001 — CRITICAL
## CAS crash recovery treats “target pathname is occupied” as proof that the replacement committed

### Affected code

- `src/PromptHelper/Services/WindowsAtomicExpectedFileReplacer.cs`
- `src/PromptHelper/Services/IOwnedArtifactJournal.cs`
- `src/PromptHelper/Services/OwnedArtifactReconciler.cs`

### Current CAS design

For `ExpectedPresent`, the new CAS performs:

```text
1. Open expected target and hold authority.
2. Verify exact expected content from that retained handle.
3. Create and flush an owned candidate stage.
4. Record a CasPreimage ownership record.
5. Rename the old committed target aside to a unique pre-image pathname.
6. Promote candidate stage into target pathname.
7. Delete pre-image.
```

This is a materially stronger live-operation design.

### The crash recovery record is incomplete

`OwnedArtifactRecord` for a CAS pre-image stores:

```text
Kind = CasPreimage
RelativePath = pre-image pathname
Identity = old committed object's NTFS identity
RestoreRelativePath = original target pathname
```

It does **not** store:

- candidate replacement SHA-256;
- candidate replacement file identity;
- transaction/operation ID binding candidate + pre-image;
- commit phase proving the candidate landed.

### Current recovery logic

`OwnedArtifactReconciler.TryRestorePreimage` does:

```text
if target exists:
    return false
```

The caller then falls through to deleting the recorded pre-image through its proven handle.

The code comment states:

```text
"The replacement landed before the crash."
```

But the implementation only proved:

```text
"something exists at the target pathname now."
```

Those statements are not equivalent.

### Concrete data-loss sequence

```text
T1  library.json contains committed OLD
T2  CAS records pre-image ownership
T3  CAS renames OLD -> .prompthelper-preimage-library.json-....tmp
T4  process/power crash BEFORE candidate promotion
T5  another process creates a file at library.json
T6  Prompt Helper restarts
T7  recovery sees library.json exists
T8  recovery assumes candidate committed
T9  recovery deletes the recorded OLD pre-image
```

At T9, Prompt Helper has destroyed the only durable copy it recorded of the previous committed state, without proving the target contains its candidate.

This is a **data-loss-class recovery bug**.

### Required fix

Replace the standalone pre-image claim with an explicit durable CAS transaction record.

Minimum durable authority:

```text
CAS operation ID
target relative path
pre-image relative path + pre-image file identity
candidate SHA-256 + candidate length
candidate identity if available after creation
phase / state
```

Recommended phases:

```text
Prepared
PreimageRecorded
PreimageSidelineDurable
CandidatePublished
Completed
```

Recovery matrix:

```text
pre-image exists + target missing
    => restore exact pre-image

pre-image exists + target exactly matches recorded candidate
    => candidate committed; retire exact pre-image

pre-image exists + target exists but candidate identity/content DOES NOT match
    => preserve both
    => fail closed
    => NEVER delete pre-image

pre-image identity mismatch
    => preserve current object
    => fail closed / diagnostics

candidate exact + pre-image missing
    => completed / stale record cleanup

neither expected object recoverable
    => fatal recovery error
```

Do not use target pathname occupancy as commit authority.

### Required regression tests

```text
CRUU16_001_Crash_between_CAS_renames_foreign_target_preserves_preimage_and_fails_closed
CRUU16_001_Crash_between_CAS_renames_invalid_target_preserves_last_committed_preimage
CRUU16_001_Crash_after_candidate_publish_exact_candidate_allows_preimage_retirement
CRUU16_001_Target_presence_alone_is_never_CAS_commit_authority
CRUU16_001_CAS_recovery_matrix_covers_every_durable_phase
```

## CRUU16-002 — HIGH
## Ownership journal parser silently drops malformed complete records, not only a torn final append

### Affected code

- `src/PromptHelper/Services/IOwnedArtifactJournal.cs`
- `src/PromptHelper/Services/OwnedArtifactReconciler.cs`

### Intended contract

The journal comment says its line-oriented format means a torn tail should discard only the incomplete final record instead of invalidating the whole journal.

### Actual parser

Current `Read` conceptually does:

```text
decode entire file
split on newline
for every line:
    if TryDeserialize(line):
        keep it
    else:
        silently ignore it
```

This silently ignores malformed lines **anywhere in the file**.

It does not distinguish:

```text
valid complete record
corrupt complete record
malformed middle record
incomplete final non-newline-terminated tail
invalid UTF-8
```

`Encoding.UTF8.GetString` is also not configured as a strict rejecting decoder.

### Consequence

If a live `CasPreimage` record is corrupted while surrounding records remain parseable, that record simply disappears from recovery authority.

Recovery can then fail to restore an interrupted CAS and later compact the journal without the lost record.

### Required fix

1. Decode as strict UTF-8.
2. Every newline-terminated record must parse exactly.
3. Any malformed **complete** record => journal corrupt => fail closed.
4. Only one final non-newline-terminated record may be treated as a torn append.
5. Preserve the original journal on parse failure.
6. Do not perform destructive cleanup when journal authority is corrupt.
7. Strongly consider per-record checksum/CRC or hash.

### Required tests

```text
CRUU16_002_Malformed_middle_ownership_record_fails_closed
CRUU16_002_Malformed_complete_final_record_fails_closed
CRUU16_002_Only_incomplete_nonterminated_final_tail_may_be_ignored
CRUU16_002_Invalid_UTF8_ownership_journal_fails_closed
CRUU16_002_Corrupt_journal_never_gets_compacted_over_original_evidence
CRUU16_002_Corrupt_CasPreimage_record_never_silently_disappears
```

## CRUU16-003 — HIGH
## The ownership journal is not itself protected by the strict authority model it authorizes

### Affected code

- `src/PromptHelper/Services/IOwnedArtifactJournal.cs`
- `src/PromptHelper/Services/WindowsVerifiedArtifactDeleter.cs`
- `src/PromptHelper/Services/WindowsOwnedDurableStage.cs`

The new ledger authorizes automatic deletion and CAS restoration. It therefore needs stronger authority than ordinary data.

### Problem A — append follows reparse points

`WindowsOwnedArtifactJournal.Record` opens `.prompthelper-owned.log` with:

```text
OPEN_ALWAYS
GENERIC_WRITE
FILE_SHARE_READ
FILE_FLAG_WRITE_THROUGH
```

but not `FILE_FLAG_OPEN_REPARSE_POINT`.

It does not inspect the reparse tag and does not resolve/assert the final physical path under the expected root.

### Problem B — read follows reparse points

`Read` uses ordinary `File.ReadAllBytes(journalPath)`.

### Problem C — empty rewrite can delete a replacement journal

If no surviving claims remain, `Rewrite` deletes the current object at the journal pathname without carrying the expected journal identity captured from the prior read.

A foreign regular file inserted between read and rewrite can therefore be deleted.

### Problem D — non-empty rewrite can overwrite a replacement journal

For survivors, `Rewrite` stages new content then blindly `PromoteReplaceExact(journalPath)`.

There is no expected-current CAS binding to the journal object that was read.

### Required fix

Create one `StrictOwnedArtifactJournalAuthority` that:

1. opens with `OPEN_REPARSE_POINT`;
2. rejects reparse objects;
3. proves final physical path under the root;
4. captures file identity + revision/hash;
5. parses from that exact handle;
6. appends under same-object authority;
7. rewrites only by expected-current CAS;
8. retires only the exact journal object read/validated.

The journal rewrite stage itself must be root-bound.

### Required tests

```text
CRUU16_003_Ownership_journal_symlink_is_never_followed_for_append
CRUU16_003_Ownership_journal_symlink_is_never_followed_for_read
CRUU16_003_Journal_replaced_after_read_is_not_deleted_by_empty_rewrite
CRUU16_003_Journal_replaced_after_read_is_not_overwritten_by_nonempty_rewrite
CRUU16_003_Journal_rewrite_stage_is_physically_root_bound
CRUU16_003_Journal_final_handle_path_must_equal_expected_managed_location
```

## CRUU16-004 — HIGH
## Failed CAS/pre-image recovery is not consistently fatal before application state is consumed

### Affected code

- `src/PromptHelper/Services/SettingsTempReconciler.cs`
- `src/PromptHelper/Services/AppSettingsRepository.cs`
- `src/PromptHelper/Services/DataRootTempReconciler.cs`
- `src/PromptHelper/App.xaml.cs`
- `src/PromptHelper/Services/OwnedArtifactReconciler.cs`

### Settings path

Every settings load/save path calls:

```text
SettingsTempReconciler.Reconcile(...)
```

but discards the returned `TempReconciliationResult`.

If the reconciler records a failure because a CAS pre-image could not be restored, the ownership journal could not be read, the journal rewrite failed, or an owned artifact could not be reconciled, the settings repository still proceeds to `LoadOrRecoverInternal`.

A crash-window state such as a temporarily missing `settings.json` can therefore be interpreted as an ordinary missing primary.

### Data-root path

`App.OnStartup` inspects `DataRootTempReconciler.Reconcile`, but every failure is added to diagnostics as a **warning** and startup continues into mutation recovery and library startup.

A failure to restore a recorded pre-image of committed state is not an ordinary stale-temp cleanup warning.

### Required fix

Make outcomes typed, e.g.:

```text
BenignCleanupFailure
UnprovenArtifactPreserved
OwnershipJournalCorrupt
CasRecoveryAmbiguous
CasPreimageRestoreFailed
AuthorityViolation
```

Policy:

```text
Unproven temp preserved              => warning
ordinary stale stage cleanup failed  => warning/fail by role
ownership journal corrupt            => FATAL
CAS recovery ambiguous               => FATAL
CAS pre-image restore failed         => FATAL
authority/root/reparse violation      => FATAL
```

`AppSettingsRepository` must consume the result. `App.OnStartup` must stop before state load for fatal recovery classes.

### Required tests

```text
CRUU16_004_Settings_load_aborts_when_CAS_preimage_restore_fails
CRUU16_004_Settings_load_aborts_when_ownership_journal_is_corrupt
CRUU16_004_Data_root_startup_aborts_on_unresolved_CAS_recovery
CRUU16_004_Data_root_startup_aborts_on_ownership_authority_violation
CRUU16_004_Unproven_temp_preservation_remains_nonfatal_warning
CRUU16_004_Benign_cleanup_failure_does_not_get_conflated_with_committed_state_recovery
```

## CRUU16-005 — HIGH
## Migration ownership is lost when a stage is promoted; final deletion still uses content as ownership

### Affected code

- `src/PromptHelper/Services/IMigrationFileOps.cs`
- `src/PromptHelper/Services/DataFolderMigrationService.cs`
- `src/PromptHelper/Services/MigrationRecoveryService.cs`
- `src/PromptHelper/Services/IVerifiedArtifactDeleter.cs`

### Current ownership lifecycle

When a migration stage is created, the journal records:

```text
kind = Stage
relativePath = temp pathname
identity = NTFS file ID
```

The object is promoted through the same handle to the final pathname, preserving the object identity.

But the durable ownership record is **not transferred to the final pathname**.

`CopySnapshotToTarget` then calls `RetireOwnedArtifacts(targetRoot)`. The reconciler sees the temp pathname is missing and drops the claim.

The final object has no creation-bound ownership record.

### Retry recovery

`MigrationRecoveryService.RecoverForRetry` still deletes final artifacts using expected length + SHA-256.

That proves content and location, not that it is the exact object created by this attempt.

### In-process rollback

`MigrationTargetTransaction.Rollback` likewise deletes `FinalOwned` files by expected hash/length.

### Concrete foreign-data deletion

```text
T1 migration publishes final bytes X
T2 stage ownership record is retired
T3 external process replaces final with a different object containing byte-identical X
T4 migration rollback/retry begins
T5 hash + length match
T6 Prompt Helper deletes the foreign replacement
```

This contradicts the CRUU15 rule that automatic destruction requires identity proof.

### Required fix

Promotion must perform a durable ownership transition:

```text
Stage(tempPath, fileId)
    -> MigrationFinal(finalPath, same fileId, attemptId, role)
```

Then retry and in-process rollback must require the recorded file identity. Same bytes + different identity must be preserved and fail closed.

### Required tests

```text
CRUU16_005_Migration_promotion_transfers_identity_from_temp_to_final
CRUU16_005_Migration_final_replaced_by_foreign_same_bytes_is_preserved
CRUU16_005_Retry_final_delete_requires_recorded_file_identity
CRUU16_005_Inprocess_rollback_final_delete_requires_recorded_file_identity
CRUU16_005_Final_hash_match_alone_never_authorizes_automatic_deletion
```

## CRUU16-006 — HIGH
## In-process migration rollback still contains pathname-based directory destruction and bypass surfaces

### Affected code

- `src/PromptHelper/Services/DataFolderMigrationService.cs`
- `src/PromptHelper/Services/IMigrationFileOps.cs`

### Current transaction rollback

`MigrationTargetTransaction.Rollback` still follows the older pattern:

```text
for tracked created directory:
    Probe(path)
    Directory.EnumerateFileSystemEntries(path)
    Directory.Delete(path)
```

This is not the `WindowsRetirableDirectory` same-handle path used by retry recovery.

The directory proven empty and the directory passed to `Directory.Delete(path)` are independent path lookups.

### Raw destructive APIs remain exposed

The current `IMigrationFileOps` still exposes:

```text
DeleteFile(string path)
DeleteDirectory(string path)
```

and `DefaultMigrationFileOps` implements them through path probe + `File.Delete` / `Directory.Delete`.

The source says they are retained for interfaces not yet fully migrated.

### Required fix

1. Convert in-process rollback to exact file/directory authority.
2. Remove raw path delete methods from `IMigrationFileOps`.
3. Let compiler errors reveal every remaining caller.
4. Permit raw delete only inside narrowly audited low-level primitives.

### Required tests

```text
CRUU16_006_Inprocess_rollback_swapped_attempt_directory_is_never_deleted
CRUU16_006_Inprocess_rollback_directory_is_removed_through_exact_handle
CRUU16_006_IMigrationFileOps_exposes_no_raw_DeleteFile
CRUU16_006_IMigrationFileOps_exposes_no_raw_DeleteDirectory
CRUU16_006_Production_migration_services_contain_no_path_based_destructive_fallback
```

## CRUU16-007 — MED-HIGH
## Root-bound staging exists as a primitive but is not wired through every production persistence path

### Affected code

- `src/PromptHelper/Services/WindowsOwnedDurableStage.cs`
- `src/PromptHelper/Services/WindowsDurableAtomicFileWriter.cs`
- `src/PromptHelper/Services/WindowsDurableSettingsFileWriter.cs`
- `src/PromptHelper/Services/AtomicTextWriter.cs`
- `src/PromptHelper/Services/IMigrationFileOps.cs`
- `src/PromptHelper/Services/IMigrationManifestFileOps.cs`
- `tests/PromptHelper.Tests/Cruu15ProcessRunnerAndDurabilityTests.cs`

### Good primitive

`WindowsOwnedDurableStage.CreateNewUnderRoot(stagePath, physicalRoot)` exists and proves from the retained handle that the created object is non-reparse and physically under the expected root.

### Actual production wiring

Multiple production paths still call bare:

```text
WindowsOwnedDurableStage.CreateNew(...)
```

including:

- `WindowsDurableAtomicFileWriter`
- `WindowsDurableSettingsFileWriter`
- `AtomicTextWriter`
- `DefaultMigrationFileOps.CreateOwnedStage`
- `DefaultMigrationManifestFileOps.CreateOwnedStage`
- ownership-journal rewrite stage

Only the new CAS replacer systematically uses `CreateNewUnderRoot`.

### Existing CRUU15 sentinel is insufficient

`CRUU15_012_Durable_stage_asserts_physical_root_before_promotion` exercises the helper directly.

It does not prove every real writer uses it.

`CRUU15_012_ReplaceDurable...` performs an ordinary write with no redirected-parent/root adversary.

### Required fix

Make root binding structural, e.g.:

```text
IOwnedStageFactory.Create(stagePath, physicalRoot)
```

or require a bound root/directory authority in every persistence constructor.

Restrict or remove bare `CreateNew` from managed persistence.

### Required tests

```text
CRUU16_007_WindowsDurableAtomicFileWriter_uses_root_bound_stage
CRUU16_007_WindowsDurableSettingsFileWriter_uses_root_bound_stage
CRUU16_007_Migration_payload_stage_is_root_bound_in_real_copy_path
CRUU16_007_Migration_manifest_stage_is_root_bound_in_real_write_path
CRUU16_007_Ownership_journal_rewrite_stage_is_root_bound
CRUU16_007_Helper_only_CreateNewUnderRoot_test_cannot_satisfy_production_wiring_gate
```

## CRUU16-008 — MED-HIGH / VERIFICATION DEFECT
## The coverage authority is structurally complete but still maps some findings to tests that do not prove the named production behavior

### Affected code

- `tools/FindingCoverageMap.json`
- `tests/PromptHelper.Tests/FindingCoverageMap.cs`
- `tests/PromptHelper.Tests/Cruu15HistoricalCoverageTests.cs`
- `tests/PromptHelper.Tests/Cruu15ProcessRunnerAndDurabilityTests.cs`

### Improvement

The finding map is checked against finding IDs extracted from external audit reports, so removing an ID from the map does not make the requirement disappear.

That is a real fix.

### Remaining semantic false positives

#### CRUU15-012

The mapped root-bound test exercises `CreateNewUnderRoot` directly while real production writers still use bare `CreateNew`.

#### CRUU12-017

`CRUU12_017_Target_baseline_records_absence_before_reservation_creates_the_root` manually constructs a baseline with `false` values; it does not execute the transition/reservation path that is supposed to capture that baseline before mutation.

#### CRUU13-016

`CRUU13_016_All_executable_icon_groups_are_compared_not_just_the_first` in the historical coverage file reads source text and searches for strings such as `EnumResourceNamesW`, `groups`, and `RequireIcon`. That test by itself is not behavioral multi-group verification.

There are stronger icon tests elsewhere, but this proves that a mapped exact sentinel can still be non-behavioral.

### Required fix

Add evidence type/quality to the coverage map. For CRITICAL/HIGH persistence findings:

- source-string tests cannot be sole evidence;
- helper-only tests cannot be sole evidence;
- the production call path must execute;
- exact race/fault cuts must be deterministic where applicable.

### Required tests/gates

```text
CRUU16_008_High_risk_finding_requires_at_least_one_production_behavior_test
CRUU16_008_Source_text_only_test_cannot_be_sole_evidence_for_high_risk_finding
CRUU16_008_Helper_only_test_cannot_be_sole_evidence_for_production_wiring_finding
CRUU16_008_CRUU12_017_executes_real_transition_baseline_capture
CRUU16_008_CRUU15_012_executes_every_real_stage_creator
```

# 4. Positive verification

The following CRUU15 work was re-inspected and should remain closed unless new evidence appears.

## Live expected-file CAS

The current CAS holds expected-object authority through the swap. The CRUU14 verify/close/write race is no longer the primary defect. CRUU16-001 is specifically a restart-state ambiguity.

## ExpectedMissing

Expected-missing is enforced by no-overwrite promotion itself.

## Strict control-file retirement

`WindowsStrictRetirableFile` combines same-handle read/delete, `FILE_FLAG_OPEN_REPARSE_POINT`, reparse rejection, final physical path, and root containment.

## Handle-bound directory enumeration

`WindowsDirectoryEnumeration.ListStrict` lists through a directory handle, and inventory cross-checks entry kind.

## ProcessTestRunner

The runner starts concurrent pipe drains, uses bounded wait, kills the process tree on timeout, and bounds post-kill pipe drains.

## Release/icon source chain

The repository contains pinned icon generation, reproducibility verification, a `v*` tag release workflow, strict published-EXE icon verification, and all-group PE comparison.

## Coverage completeness authority

The finding map now derives required IDs from checked-in audit reports rather than proving its own completeness.

# 5. Root diagnosis

The remaining defects share one architectural theme:

> **Object identity is now handled correctly while an object is live, but identity is not always carried across durable state transitions.**

Missing transitions include:

```text
CAS candidate stage
    -> published candidate / committed recovery phase

migration stage identity
    -> migration final identity

ownership journal snapshot
    -> rewritten journal identity

reconciliation failure
    -> typed startup-fatal authority

root-bound stage helper
    -> every production persistence call
```

The next pass should avoid adding more pathname checks. Make these durable authority transitions first-class state.

# 6. Ordered CRUU16 fix plan

## PHASE 01 — Replace ambiguous CAS pre-image claims with a real CAS transaction journal
Fixes CRUU16-001.

Exit: no code decides “replacement committed” from target pathname presence alone.

## PHASE 02 — Make ownership-journal parsing strict and fail-closed
Fixes CRUU16-002.

Exit: malformed complete records cannot silently disappear.

## PHASE 03 — Make the ownership journal itself a strict expected-current object
Fixes CRUU16-003.

Exit: a reparse/replaced `.prompthelper-owned.log` cannot be read, appended to, overwritten, or deleted as though it were the validated ledger.

## PHASE 04 — Introduce typed reconciliation severity and stop startup on unresolved committed-state recovery
Fixes CRUU16-004.

Exit: failed/ambiguous CAS restore cannot be downgraded to a notice or ignored.

## PHASE 05 — Transfer migration ownership to final objects
Fixes CRUU16-005.

Exit: no migration final is automatically deleted using content hash alone.

## PHASE 06 — Remove all in-process rollback pathname destruction
Fixes CRUU16-006.

Exit: `IMigrationFileOps` contains no raw path delete escape hatch.

## PHASE 07 — Make physical-root binding mandatory in the stage API
Fixes CRUU16-007.

Exit: every real managed production stage is physically root-bound before write/promotion.

## PHASE 08 — Upgrade finding coverage from structural mapping to behavioral evidence quality
Fixes CRUU16-008.

Exit: every HIGH/CRITICAL persistence finding has at least one real production-path adversarial test.

## PHASE 09 — Add CRUU16 crash/fault matrix

At minimum:

```text
CAS:
- before pre-image record
- after pre-image record
- after sideline rename
- after candidate publish
- before pre-image retire

ownership journal:
- torn final append
- corrupt middle record
- replaced ledger
- reparse ledger
- rewrite collision

migration:
- after temp ownership record
- after final promotion before ownership transfer
- after ownership transfer
- before rollback
- foreign same-byte final replacement

startup:
- settings CAS restore failure
- data-root CAS restore failure
- corrupt ledger
```

## PHASE 10 — Final Windows acceptance on exact tested SHA

```text
1. fresh clone
2. no existing node_modules
3. npm ci pinned renderer
4. icon reproducibility
5. Release restore/build
6. targeted CAS recovery suite
7. targeted ownership-journal corruption suite
8. targeted migration provenance/rollback suite
9. targeted filesystem/reparse suite
10. full suite
11. full suite × 5
12. exact sentinel verification
13. finding coverage verification
14. self-contained win-x64 publish
15. strict published-EXE asset chain
16. push exact SHA
17. verify CI/check attached to exact SHA
18. exercise throwaway v* pre-release tag path or equivalent release workflow
19. independent source re-audit
```

# 7. Mandatory CRUU16 sentinel additions

```text
CRUU16_001_Crash_between_CAS_renames_foreign_target_preserves_preimage_and_fails_closed
CRUU16_001_Crash_between_CAS_renames_invalid_target_preserves_last_committed_preimage
CRUU16_001_Crash_after_candidate_publish_exact_candidate_allows_preimage_retirement
CRUU16_001_Target_presence_alone_is_never_CAS_commit_authority
CRUU16_001_CAS_recovery_matrix_covers_every_durable_phase

CRUU16_002_Malformed_middle_ownership_record_fails_closed
CRUU16_002_Malformed_complete_final_record_fails_closed
CRUU16_002_Only_incomplete_nonterminated_final_tail_may_be_ignored
CRUU16_002_Invalid_UTF8_ownership_journal_fails_closed
CRUU16_002_Corrupt_journal_never_gets_compacted_over_original_evidence
CRUU16_002_Corrupt_CasPreimage_record_never_silently_disappears

CRUU16_003_Ownership_journal_symlink_is_never_followed_for_append
CRUU16_003_Ownership_journal_symlink_is_never_followed_for_read
CRUU16_003_Journal_replaced_after_read_is_not_deleted_by_empty_rewrite
CRUU16_003_Journal_replaced_after_read_is_not_overwritten_by_nonempty_rewrite
CRUU16_003_Journal_rewrite_stage_is_physically_root_bound
CRUU16_003_Journal_final_handle_path_must_equal_expected_managed_location

CRUU16_004_Settings_load_aborts_when_CAS_preimage_restore_fails
CRUU16_004_Settings_load_aborts_when_ownership_journal_is_corrupt
CRUU16_004_Data_root_startup_aborts_on_unresolved_CAS_recovery
CRUU16_004_Data_root_startup_aborts_on_ownership_authority_violation
CRUU16_004_Unproven_temp_preservation_remains_nonfatal_warning
CRUU16_004_Benign_cleanup_failure_does_not_get_conflated_with_committed_state_recovery

CRUU16_005_Migration_promotion_transfers_identity_from_temp_to_final
CRUU16_005_Migration_final_replaced_by_foreign_same_bytes_is_preserved
CRUU16_005_Retry_final_delete_requires_recorded_file_identity
CRUU16_005_Inprocess_rollback_final_delete_requires_recorded_file_identity
CRUU16_005_Final_hash_match_alone_never_authorizes_automatic_deletion

CRUU16_006_Inprocess_rollback_swapped_attempt_directory_is_never_deleted
CRUU16_006_Inprocess_rollback_directory_is_removed_through_exact_handle
CRUU16_006_IMigrationFileOps_exposes_no_raw_DeleteFile
CRUU16_006_IMigrationFileOps_exposes_no_raw_DeleteDirectory
CRUU16_006_Production_migration_services_contain_no_path_based_destructive_fallback

CRUU16_007_WindowsDurableAtomicFileWriter_uses_root_bound_stage
CRUU16_007_WindowsDurableSettingsFileWriter_uses_root_bound_stage
CRUU16_007_Migration_payload_stage_is_root_bound_in_real_copy_path
CRUU16_007_Migration_manifest_stage_is_root_bound_in_real_write_path
CRUU16_007_Ownership_journal_rewrite_stage_is_root_bound
CRUU16_007_Helper_only_CreateNewUnderRoot_test_cannot_satisfy_production_wiring_gate

CRUU16_008_High_risk_finding_requires_at_least_one_production_behavior_test
CRUU16_008_Source_text_only_test_cannot_be_sole_evidence_for_high_risk_finding
CRUU16_008_Helper_only_test_cannot_be_sole_evidence_for_production_wiring_finding
CRUU16_008_CRUU12_017_executes_real_transition_baseline_capture
CRUU16_008_CRUU15_012_executes_every_real_stage_creator
```

# 8. Final acceptance matrix

Do not grant zero-defect status until all are true:

```text
CAS-01  Destination pathname presence is never commit authority.
CAS-02  Candidate identity/content is durably bound into recovery state.
CAS-03  Every two-rename crash cut has an unambiguous recovery outcome.

OWN-01  Ownership journal is strict UTF-8.
OWN-02  Malformed complete records fail closed.
OWN-03  Only a precisely-defined torn final append can be ignored.
OWN-04  Journal read/append/rewrite/delete is reparse-safe and root-bound.
OWN-05  Rewrite is expected-current/CAS-bound to the journal that was read.
OWN-06  Journal corruption preserves evidence and stops destructive cleanup.

START-01 Settings consumes reconciliation outcome.
START-02 Data-root startup treats unresolved CAS authority as fatal.
START-03 Benign unproven temp preservation remains nonfatal.

MIG-01  Temp ownership becomes final ownership on promotion.
MIG-02  Retry final deletion requires object identity.
MIG-03  In-process final rollback requires object identity.
MIG-04  In-process directory rollback uses exact directory authority.
MIG-05  No raw path delete bypass remains in migration file ops.

ROOT-01 Every managed production stage is physically root-bound.
ROOT-02 Root-binding tests execute real product writers, not only helper APIs.

TEST-01 Every CRITICAL/HIGH finding has a production-behavior sentinel.
TEST-02 Source-text/helper-only tests cannot be sole high-risk evidence.
TEST-03 Every required sentinel runs and passes in exact final TRX evidence.

REL-01  Fresh checkout passes.
REL-02  Full Release suite passes five consecutive times.
REL-03  Exact final SHA has attached Windows CI evidence.
REL-04  Self-contained publish passes strict icon chain.
REL-05  Release-tag workflow is exercised before final release acceptance.
REL-06  Final independent source audit finds zero remaining defects.
```

Only then:

```text
STRICT_RELEASE_READY = YES
ZERO_DEFECT_VERIFIED = YES
```

# 9. Final assessment

The repository is significantly stronger than at CRUU15.

The implementation correctly attacked the architectural problem rather than merely adding more path checks. The live CAS, process runner, strict retirable files, directory enumeration, icon generation, and finding-map changes are meaningful improvements.

The remaining issues are narrower, but not cosmetic.

The new ownership journal is now part of the application's recovery authority. Its grammar, identity, rewrite semantics, failure propagation, and transaction completeness therefore need to meet the same standard imposed on library/settings/migration files.

The highest-priority repair is CRUU16-001.

Until recovery can distinguish:

```text
"our candidate is at the target"
```

from:

```text
"some file happens to be at the target"
```

the new CAS cannot be called crash-safe.

Likewise, migration cannot truthfully claim provenance-bound destruction while final files lose their ownership identity at promotion and are later deleted by hash alone.

# 10. Final status

```text
AUDITED_HEAD                       = 3875cdc072abae3e22fb1da1ef56e0881c877c2b

IMPLEMENTER_REPORTED_FULL_SUITE    = 633/633 × 5
IMPLEMENTER_REPORTED_TOTAL_RESULTS = 3165
IMPLEMENTER_REPORTED_SENTINELS     = 413
IMPLEMENTER_REPORTED_FINDINGS_MAP  = 77

INDEPENDENT_WINDOWS_EXECUTION      = NOT AVAILABLE
INDEPENDENT_GITHUB_STATUS          = NO STATUS EXPOSED FOR EXACT SHA

CRUU16_FINDINGS                    = 8
CRITICAL                           = 1
HIGH                               = 5
MED_HIGH                           = 2

SOURCE_AUDIT_CLEAN                 = NO
CRUU15_ALL_FINDINGS_FIXED          = NO
STRICT_RELEASE_READY               = NO
ZERO_DEFECT_VERIFIED               = NO
```
