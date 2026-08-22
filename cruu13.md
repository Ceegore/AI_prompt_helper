# CRUU13 — Independent Post-CRUU12 Adversarial Re-Audit

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited product commit:** `1c00161c5edc18f1ca2856dd3f5d1e2db6ea2555`  
**CRUU12 baseline:** `5c1904f870d0b2587407b4484e02e6ed889a4acd`  
**Audit date:** 2026-08-21  
**Audit mode:** independent source/test/evidence re-audit; implementation claims, test names, and commit messages were not treated as proof.

> Report only. No product implementation was changed by this audit.

---

# 1. Executive verdict

The CRUU12 implementation is a large and meaningful improvement. Several difficult fixes are real: exact old-library snapshots exist; mutation journal copy-on-write/revision CAS exists; settings token/schema/path authority is much stronger; settings-temp and data-root-temp cleanup are separated; atomic directory creation was introduced; migration payload commit leases exist for the empty-target copy path; lifecycle-journal conflict detection exists; directory-handle type checks and final-path checks improved; and the split document/bytes primary commit API was removed.

However, **zero-defect acceptance is not valid** at the audited commit.

The independent re-audit found both unresolved CRUU12 failure mechanisms and new second-order defects introduced by the repair architecture. Several required CRUU12 regression tests are also false-positive sentinels: their names describe an adversarial/fault scenario that the test body never creates.

```text
AUDITED PRODUCT HEAD                       = 1c00161c5edc18f1ca2856dd3f5d1e2db6ea2555
DELTA FROM CRUU12 BASE                     = 1 commit
CRUU12 ZERO-DEFECT ACCEPTANCE              = NOT GRANTED
SOURCE AUDIT CLEAN                         = NO
WINDOWS/.NET/WPF DIRECT EXECUTION          = NOT_INDEPENDENTLY_VERIFIED
GITHUB COMBINED STATUS ENTRIES RETURNED    = NONE
GITHUB COMMIT WORKFLOW RUNS RETURNED       = NONE
STRICT RELEASE READY                       = NO
APPROVED PromptHelperLogo.svg              = ABSENT
CRUU13 FINDINGS                            = 19
CRITICAL                                   = 1
HIGH                                       = 5
MED-HIGH                                   = 8
MED                                        = 3
RELEASE GAP                                = 1
RELEASE BLOCKER                            = 1
```

The implementation evidence reports successful local Windows runs, including five full-suite runs. Those reports are useful implementation evidence, but they are **not independent runtime verification** in this audit environment. More importantly, source-level defects below remain even if all reported tests passed.

---

# 2. CRUU12 finding status matrix

| CRUU12 ID | Independent status | Reason |
|---|---|---|
| 001 | **PARTIAL / REOPENED** | destructive generic catch was removed, but committed-restart exceptions are swallowed by normal UI error handling; equal-hash body-only postcommit cut is still ambiguous |
| 002 | SOURCE_FIXED_BUT_NOT_ACCEPTED | exact raw primary snapshot is now captured and journaled |
| 003 | **PARTIAL / REOPENED** | equal-hash state is phase-aware, but a primary commit followed by failed `MetadataDurable` advance leaves phase at `BodyDurable` and restart rolls a committed body-only edit back |
| 004 | PARTIAL | copy-on-write CAS exists; however `revision` is not required by strict JSON grammar |
| 005 | PARTIAL | kind-specific fields and hex hashes improved; revision grammar remains non-exact and required sentinel coverage is absent |
| 006 | SOURCE_FIXED_BUT_NOT_ACCEPTED | settings-only temp reconciliation is separated and data-root reconciliation moved after app lock/tree lease |
| 007 | **PARTIAL** | recovery directory is scanned, but reconciliation failures are returned and then ignored by `App`; authority still uses permissive existence/enumeration APIs in places |
| 008 | SOURCE_FIXED_BUT_NOT_ACCEPTED | settings token capture now distinguishes missing from unreadable |
| 009 | SOURCE_FIXED_BUT_NOT_ACCEPTED | current settings schema is enforced consistently |
| 010 | SOURCE_FIXED_BUT_NOT_ACCEPTED | `dataRootPath` normalization/validation is now applied |
| 011 | **NOT_FIXED** | retry cleanup happens before target-operation lease acquisition; later-created child directories are not added to the lease |
| 012 | PARTIAL_VERIFICATION | production directory creation is atomic, but required test is not concurrent and never exercises foreign-content rollback |
| 013 | PARTIAL_VERIFICATION | transaction state is stronger, but required test injects no bookkeeping failure |
| 014 | **NOT_FIXED** | `RecoverForRetry` still raw-deletes declared controls and payload temps by path; expected hash/length fields are not used |
| 015 | PARTIAL | schema-v4 names are stricter, but legacy/v3 control grammar remains broad |
| 016 | **NOT_FIXED** | schema-v3 retry still compares only `SourceLibrarySha256Hex`; full payload fingerprint is not enforced |
| 017 | SOURCE_FIXED_BUT_NOT_ACCEPTED | target-root preexistence now derives from reservation baseline |
| 018 | **NOT_FIXED** | ReadyGate still rejects legitimate bootstrap settings as ephemeral controls |
| 019 | **NOT_FIXED** | inventory remains based on `Directory.Exists/GetFiles/GetDirectories` rather than strict fail-closed enumeration |
| 020 | PARTIAL_VERIFICATION | lifecycle conflict detector exists, but all pair/triple combinations are not covered |
| 021 | **NOT_FIXED** | migration metadata decode still enables BOM auto-detection, so UTF-16/32 can be accepted |
| 022 | SOURCE_FIXED_BUT_NOT_ACCEPTED | strict directory opener now validates directory attribute |
| 023 | PARTIAL_VERIFICATION | final-handle identity/type/reparse checks added; sentinel does not force the named swap |
| 024 | **PARTIAL / REOPENED** | empty-target copy uses a commit lease, but existing-target transition has no equivalent final content binding |
| 025 | **NOT_FIXED** | rollback `cleanRollback` ignores `DeclaredControls`; marker may be retired while stage/control residue remains |
| 026 | **NOT_FIXED** | wildcard `.prompthelper-capability-*.tmp` deletion remains in production |
| 027 | **NOT_FIXED** | capability-probe catch cleanup still deletes by path without same-object/hash authority |
| 028 | **PARTIAL** | split document+bytes primary commit API is gone, but public `SynchronizeBackup(LibraryDocument)` remains |
| 029 | SOURCE_FIXED_BUT_NOT_ACCEPTED | unsafe durability/deletion adapters were removed from inspected production architecture |
| 030 | **NOT_FIXED** | mutation-recovery warning, temp cleanup failures, and orphan warnings are not surfaced |
| 031 | **NOT_FIXED** | initialization still uses unstructured marker + `ReplaceDurable` + best-effort retirement |
| 032 | **NOT_FIXED** | multiple sentinels still do not execute their named behavior |
| 033 | **NOT_FIXED** | SVG→ICO identity unproved; 256→downsize pipeline remains; EXE reader checks only first icon group |
| 034 | **BLOCKED_EXTERNAL_ASSET** | approved real `src/PromptHelper/Assets/PromptHelperLogo.svg` is absent |

---

# 3. CRUU13-001 — CRITICAL: committed mutation restart exception is handled as an ordinary save error

**Maps to:** CRUU12-001  
**Files:** `CommittedMutationRequiresRestartException.cs`, `MainWindow.xaml.cs`

`CommittedMutationRequiresRestartException` derives from `IOException`. Prompt CRUD handlers catch broad `IOException`, show a normal save error, and for Create/Edit keep the editor loop alive.

Therefore a mutation that already reached durable metadata but could not finish journal bookkeeping does **not** force controlled shutdown.

## Consequence

The same process can continue operating with an unresolved recovery journal and potentially stale in-memory library state.

## Repair

Catch `CommittedMutationRequiresRestartException` **before** general I/O exceptions, display one restart-required warning, then call the application lifetime shutdown path. Never return to the editor loop.

## Required tests

```text
CRUU13_001_Create_postcommit_journal_failure_requests_shutdown
CRUU13_001_Edit_postcommit_journal_failure_requests_shutdown
CRUU13_001_Delete_postcommit_journal_failure_requests_shutdown
CRUU13_001_Duplicate_postcommit_journal_failure_requests_shutdown
CRUU13_001_Fatal_mutation_exception_is_not_caught_by_normal_save_error_path
CRUU13_001_No_second_mutation_can_start_after_fatal_postcommit_result
```

---

# 4. CRUU13-002 — HIGH: body-only edit can be reported committed but restart rolls it back

**Maps to:** CRUU12-001 / CRUU12-003  
**Files:** `PromptMutationCoordinator.cs`, `LibraryMutationRecoveryService.cs`

For a body-only edit:

```text
OldLibrarySha256Hex == NewLibrarySha256Hex
```

Current cut:

```text
body NEW durable
journal BodyDurable
library commit succeeds
MetadataDurable journal advance fails
CommittedMutationRequiresRestartException thrown
```

The durable journal remains `BodyDurable`. On restart, `OldAndNewSameBytes` is interpreted as committed only when phase >= `MetadataDurable`, so recovery restores the OLD body.

That contradicts the postcommit exception semantics.

## Repair

Introduce an explicit durable commit authority for equal-metadata body-only edits. The journal state, not identical metadata bytes, must unambiguously decide commit vs rollback.

## Required tests

```text
CRUU13_002_Body_only_edit_primary_step_then_MetadataDurable_failure_has_consistent_outcome
CRUU13_002_Body_only_edit_precommit_failure_restores_old_body
CRUU13_002_Body_only_edit_postcommit_failure_keeps_new_body
CRUU13_002_Body_only_edit_restart_twice_is_idempotent
CRUU13_002_No_committed_exception_is_thrown_for_a_state_recovery_will_roll_back
```

---

# 5. CRUU13-003 — HIGH regression: existing-target transition lost final content binding before settings commit

**Maps to:** CRUU12-024  
**File:** `DataFolderTransitionCoordinator.cs`

The existing-target path performs locked inspection and fingerprint comparison, then capability probing and settings commit. It no longer performs a final content fingerprint check immediately before settings commit and has no payload commit lease equivalent.

An external process can modify target metadata/body after inspection and before settings commit.

## Repair

Create an `ExistingTargetCommitLease` that opens and hashes metadata plus active prompt bodies, denies replacement/write for the commit window, and remains held through `SaveIfUnchanged`.

---

# 6. CRUU13-004 — HIGH: retry cleanup still deletes controls and temps by path authority

**Maps to:** CRUU12-014  
**Files:** `MigrationRecoveryService.cs`, `MigrationControlArtifact.cs`

`RecoverForRetry` still directly calls `DeleteFile` for declared controls and payload temps. New expected hash/length fields exist but are not enforced or used in recovery.

A foreign replacement at the exact declared path can still be deleted.

## Repair

All auto-deleted manifest-owned files must have exact expected hash/length and use verified deletion. Directory controls require stronger identity/ownership authority.

---

# 7. CRUU13-005 — HIGH: target-operation lease does not cover retry and later-created children

**Maps to:** CRUU12-011  
**Files:** `DataFolderTransitionCoordinator.cs`, `ManagedTargetOperationLease.cs`

Interrupted retry runs **before** the operation lease is acquired.

Also, missing `prompts/` or `recovery/` directories are skipped at lease acquisition and never added after migration creates them.

## Repair

Acquire root lease before retry; immediately bind newly-created managed child directories into the operation lease with handle identity verification.

---

# 8. CRUU13-006 — MED-HIGH: schema-v3 retry still ignores full payload fingerprint

**Maps to:** CRUU12-016  
**File:** `MigrationRecoveryService.cs`

Schema 3 still validates only the primary library hash. The existing CRUU12 test merely computes two fingerprints and never calls retry.

## Repair

Compare the current full payload fingerprint against the derived v3 manifest payload fingerprint and fail closed before any deletion.

---

# 9. CRUU13-007 — HIGH functional: exact bootstrap migration still fails ReadyGate

**Maps to:** CRUU12-018  
**Files:** `MigrationTargetInventoryInspector.cs`, `MigrationReadyGate.cs`

Inventory classifies bootstrap settings as controls, but ReadyGate allows only marker + `.app.lock`, so legitimate bootstrap settings are rejected.

The current test checks only `HasUnknownEntries == false`, not ReadyGate/full transition.

## Repair

Introduce explicit bootstrap inventory context and distinguish persistent bootstrap controls from ephemeral migration controls.

---

# 10. CRUU13-008 — MED-HIGH: migration inventory remains fail-soft/path-based

**Maps to:** CRUU12-019  
**File:** `MigrationTargetInventoryInspector.cs`

Still uses:

```text
Directory.Exists
Directory.GetFiles
Directory.GetDirectories
```

instead of typed Missing/File/Directory/Unreadable/Reparse authority.

## Repair

Introduce strict inventory operations that fail closed on access-denied, I/O, sharing, and reparse anomalies.

---

# 11. CRUU13-009 — MED-HIGH: real migration metadata path still accepts UTF-16/UTF-32 BOM

**Maps to:** CRUU12-021  
**File:** `DataFolderMigrationService.cs`

`DecodeUtf8Text` still uses:

```csharp
detectEncodingFromByteOrderMarks: true
```

so BOM may switch decoding to UTF-16/32.

The current CRUU12 sentinel tests only `StrictUtf8Text` directly, not the migration path.

## Repair

Route every migration text role through `StrictUtf8Text.Decode`.

---

# 12. CRUU13-010 — MED-HIGH: rollback can retire marker while declared control residue remains

**Maps to:** CRUU12-025  
**File:** `DataFolderTransitionCoordinator.cs`

`cleanRollback` checks unknown entries, payload temps, final artifacts and failures, but not `DeclaredControls`.

A recognized stage/control residue can remain while the marker is still retired.

## Repair

Marker retirement requires zero payload temps, zero finals, zero ephemeral controls, zero attempt-created directories, zero unknowns, zero cleanup failures.

---

# 13. CRUU13-011 — MED-HIGH: wildcard capability cleanup and replaced-probe cleanup remain unsafe

**Maps to:** CRUU12-026 / 027  
**File:** `DataRootCapabilityValidator.cs`

Production still deletes `.prompthelper-capability-*.tmp` wildcard matches and swallows failures.

Catch cleanup of planned probe files also deletes by path, not same-object/hash authority.

## Repair

Remove wildcard deletion. Use attempt-specific declared probes plus verified cleanup.

---

# 14. CRUU13-012 — HIGH: public backup synchronization still has independent document authority

**Maps to:** CRUU12-028  
**File:** `LibraryRepository.cs`

`CommitCanonicalBytes(document, bytes)` is gone, but this remains public:

```csharp
SynchronizeBackup(LibraryDocument document)
```

So backup content can still be authored independently from the current primary.

## Repair

Remove the public `LibraryDocument` overload. Backup sync should accept only a strong primary-bound package.

---

# 15. CRUU13-013 — MED: startup maintenance warnings are still invisible

**Maps to:** CRUU12-007 / 030  
**Files:** `App.xaml.cs`, `DataRootTempReconciler.cs`, `StartupDiagnosticCollector.cs`

- `DataRootTempReconciler.Reconcile` failures are ignored.
- `MutationRecoveryResult.Warning` is ignored.
- orphan reconciliation is inside broad `catch { }`.
- `StartupDiagnosticCollector` exists but is not wired into `App`.

## Repair

Aggregate nonfatal startup warnings through one collector and show once. Programmer exceptions must not be swallowed.

---

# 16. CRUU13-014 — MED: initialization is still not a durable phase journal

**Maps to:** CRUU12-031  
**File:** `LibraryStartupService.cs`

The marker uses the correct durable class now, but still:

```text
ReplaceDurable
"initializing"
best-effort delete
```

No initialization ID, revision, phase, CAS, or postcommit finalization state exists.

The CRUU12 test named “Crash_after_metadata_before_journal_retire” does not construct that cut.

## Repair

Implement a real initialization journal:

```text
CreatingDefaults
MetadataDurable
```

with CreateNew publication, strict JSON, revision/CAS, and restart-finalizable retirement.

---

# 17. CRUU13-015 — MED-HIGH verification failure: sentinels still do not execute named behavior

Confirmed examples:

| Sentinel | Actual behavior | Missing |
|---|---|---|
| `CRUU11_001_Buffer_resize...` | normal helper call | no forced resize/call-count |
| `CRUU11_001_Reparse_artifact...` | regular file | no reparse object |
| `CRUU11_016_Settings_primary_recovery...` | default repo | no recording durable writer |
| `CRUU11_002_Duplicate_uses_Create_transaction_state_machine` | success path | no crash cuts |
| `CRUU11_025_*evidence_script*` | string/HashSet checks | no PowerShell/TRX execution |
| `CRUU12_012_Concurrent...` | sequential create calls | no concurrency |
| `CRUU12_013_Move_success_before_bookkeeping_failure...` | bookkeeping succeeds | no failure |
| `CRUU12_014_Declared_payload_temp...` | direct deleter test | no retry path |
| `CRUU12_016_V3...rejects_retry` | compares fingerprints | no retry call |
| `CRUU12_018_Custom_to...bootstrap...succeeds` | inventory only | no ReadyGate/full transition |
| `CRUU12_021_UTF16...source_library_rejected` | strict helper only | no migration parser |
| `CRUU12_023_Session_lease...identity` | normal success | no swap |
| `CRUU12_025_Rollback_stage_residue...` | inventory only | no rollback/marker assertion |
| `CRUU12_026_Foreign_capability...` | direct deleter | no wildcard cleanup |
| `CRUU12_027_Probe_current_replaced...` | direct deleter | no real probe cleanup |
| `CRUU12_031_Crash_after_metadata...` | early marker only | no committed metadata cut |
| `CRUU12_032_Evidence_script...TRX` | reads script text | no script execution |

Also, required regression tests contain no CRUU12 sentinel for IDs:

```text
005, 007, 009, 010, 017, 019, 022, 030, 033, 034
```

## Acceptance rule

Every sentinel must map:

```text
claimed property -> induced condition -> production path -> persistent-state assertion
```

---

# 18. CRUU13-016 — MED release gap: SVG → ICO → all EXE icon groups identity remains unproved

**Maps to:** CRUU12-033

Remaining issues:

1. SVG is rasterized once at 256 and smaller frames are downsized.
2. No approved SVG hash/identity manifest binds SVG to committed ICO.
3. PE verifier stops at the first `RT_GROUP_ICON`.
4. All icon groups are not verified.

## Repair

Create an approved release identity manifest containing SVG hash and normalized RGBA hashes per size. Independently render each native size. Enumerate and verify every relevant EXE icon group.

---

# 19. CRUU13-017 — RELEASE BLOCKER: approved logo is absent

**Maps to:** CRUU12-034

`src/PromptHelper/Assets/PromptHelperLogo.svg` is absent.

```text
CRUU12-034 = BLOCKED_EXTERNAL_ASSET
STRICT_RELEASE_READY = NO
```

Do not invent a placeholder.

---

# 20. CRUU13-018 — MED: mutation journal revision is optional in strict grammar

**Maps to:** CRUU12-004 / 005  
**File:** `LibraryMutationJournalRepository.cs`

`revision` is allowed but not required. Omission silently becomes zero due to the model default.

## Repair

Prefer schema v2 with required revision and an explicit compatibility path for schema v1 journals.

---

# 21. CRUU13-019 — MED-HIGH new authority gap: ordinary CRUD snapshot is not CAS-bound through commit

**New finding**

The coordinator captures and validates an exact primary snapshot, but that is before journal/body work.

An external writer can change `library.json` after snapshot capture and before `LibraryRepository.Commit`, and the current commit API can overwrite that valid concurrent version.

Likewise, Edit reads old body bytes before later replacing the body; a concurrent external body change can be silently overwritten.

## Repair

Introduce commit preconditions:

```text
CommitIfPrimaryUnchanged(package, expectedRawSha256)
ReplaceBodyIfUnchanged(expectedOldHash)
```

and hold a Windows handle/share-mode lease for the final verify→replace window where possible.

---

# 22. Mandatory implementation order

```text
PHASE 01  CRUU13-001 UI fatal postcommit handling
PHASE 02  CRUU13-002 body-only commit authority
PHASE 03  CRUU13-018 journal schema/revision exactness
PHASE 04  CRUU13-019 ordinary CRUD precondition/TOCTOU authority
PHASE 05  CRUU13-004 verified migration temp/control cleanup
PHASE 06  CRUU13-005 target-operation lease coverage
PHASE 07  CRUU13-006 v3 full-payload retry authority
PHASE 08  CRUU13-007/008 bootstrap-aware strict inventory
PHASE 09  CRUU13-009 strict migration UTF-8
PHASE 10  CRUU13-010 terminal rollback inventory
PHASE 11  CRUU13-011 capability cleanup ownership
PHASE 12  CRUU13-003 existing-target final content commit lease
PHASE 13  CRUU13-012 strong backup API only
PHASE 14  CRUU13-013 startup diagnostic wiring
PHASE 15  CRUU13-014 initialization journal
PHASE 16  CRUU13-015 rewrite false-positive tests + complete required sentinel list
PHASE 17  CRUU13-016 release identity chain
PHASE 18  integrate approved logo when supplied; strict release validation
PHASE 19  direct Windows fault matrix + 5x suite + publish + independent source re-audit
```

---

# 23. Final declaration

```text
SOURCE_AUDIT_CLEAN                = NO
CRUU12_ALL_FINDINGS_FIXED         = NO
REQUIRED_TESTS_BEHAVIORALLY_VALID = NO
WINDOWS_TESTS_DIRECTLY_EXECUTED   = NO
WINDOWS_RUNTIME_VALIDATION        = NOT_INDEPENDENTLY_VERIFIED
STRICT_RELEASE_READY              = NO
ZERO_DEFECT_VERIFIED              = NO
```

A future acceptance run must prove that the real production paths changed, then prove that each sentinel induces the named failure mechanism, and only then use passing test/CI counts as supporting evidence.
