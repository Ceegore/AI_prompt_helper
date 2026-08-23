# CRUU21 — Independent Post-CRUU20 Adversarial Re-Audit and Fix Plan

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited HEAD:** `c2dc3db4d9cfaffbe4fd9156722566ef5f150097`  
**Parent / CRUU20 audit snapshot:** `4456af855d98d62308d1a7eea606e504be18043d`  
**Repair commit:** `c2dc3db4d9cfaffbe4fd9156722566ef5f150097` — `fix(recovery): close CRUU20 crash-consistency gaps`  
**Previous audit:** `cruu20.md`  
**Audit date:** 2026-08-23  
**Mode:** independent source, exact-HEAD delta, hard-process-crash, migration-marker authority, persisted-protocol, crash-harness evidence and release-evidence audit.

> Report only. No production source or repository state was modified by this audit.

---

# 1. Executive verdict

The CRUU20 repair is **substantive and technically important**.

The implementation introduced real crash-consistency machinery rather than merely adding sentinel names:

- capability probes now have a dedicated durable ownership kind and explicit crash phases;
- every legal probe recovery location is durably predeclared before rename;
- probe content is not treated as durable until after `FlushDurable`;
- the stage primitive now supports a kernel delete-on-close bootstrap before its first durable claim;
- CAS, payload stage, Ready-manifest stage and capability-probe creation use that bootstrap;
- migration-owned directories now use a native `NtCreateFile` crash-atomic bootstrap with delete-on-close;
- the migration protocol is now schema v5;
- old v4 attempts receive an explicit legacy/manual-cleanup result instead of silently inheriting v5 identity assumptions;
- a real child-process crash harness exists and the parent forcibly terminates it instead of unwinding through product `catch`/`finally`;
- automatic production-symbol evidence was added at the MSTest execution-wrapper level.

Those changes convincingly close most of the exact CRUU20 source defects for **process termination**.

The repository is nevertheless **not zero-defect yet**.

CRUU21 found **4 actionable findings**:

```text
HIGH      = 2
MED-HIGH  = 2
TOTAL     = 4
```

The two High findings are concentrated in one remaining durable-authority subsystem: the migration marker itself.

1. **The first Copying marker is still written directly at its authoritative final pathname.** A process death during that write can leave a truncated `.prompthelper-migration.json`, which is then detected as an interrupted migration but cannot be parsed or automatically retired.

2. **The marker lifecycle is not bound to the exact marker object.** The Ready transition uses unconditional replace semantics against whatever currently occupies the marker pathname, and `DeleteStrict` later authorizes deletion from parsed attempt ID + phase rather than from the exact marker identity this attempt created. A foreign same-path marker can therefore be overwritten or deleted.

The two Medium-High findings are verification/acceptance boundaries:

3. The new hard-crash harness proves **process termination**. It does not by itself establish the same semantics for abrupt VM reset or physical power loss, while prior durability-acceptance language included process/power-loss cuts.

4. The new automatic evidence harness is good, but the CRUU20 finding map still allows a normal attributed test to satisfy a finding's required production symbol while the **specific subprocess hard-crash sentinels themselves** are not runtime-bound to the recovery integration layer they claim to prove.

The next repair should bring the migration marker under the same exact-object + durable-phase model that is now working for payloads, probes and directories.

---

# 2. Audit freeze and execution evidence

`main` was fetched at the start and re-fetched immediately before this report was generated.

Both reads returned:

```text
c2dc3db4d9cfaffbe4fd9156722566ef5f150097
```

The repair commit's parent is exactly the CRUU20 audited SHA.

```text
AUDITED_HEAD                         = c2dc3db4d9cfaffbe4fd9156722566ef5f150097
HEAD_STABLE_DURING_AUDIT             = YES

CRUU21_FINDINGS                      = 4
HIGH                                 = 2
MED_HIGH                             = 2

CRUU20_CORE_REPAIRS_REAL             = YES
CRUU20_PROCESS_CRASH_CORE_CLOSED     = YES
CRUU20_STRICT_POWERLOSS_ACCEPTANCE   = NOT PROVEN

SOURCE_AUDIT_CLEAN                   = NO

WINDOWS_TESTS_DIRECTLY_EXECUTED      = NO
LOCAL_DOTNET                         = NOT AVAILABLE
LOCAL_PWSH                           = NOT AVAILABLE
LOCAL_WINDOWS_RUNTIME                = NOT AVAILABLE

GITHUB_COMBINED_STATUS_FOR_HEAD      = NO STATUSES EXPOSED
GITHUB_PR_WORKFLOW_LOOKUP_FOR_HEAD   = NO RUNS EXPOSED
EXACT_HEAD_WINDOWS_PASS_BOUND        = NO

STRICT_RELEASE_READY                 = NO
ZERO_DEFECT_VERIFIED                 = NO
```

The connected GitHub status surface returned no statuses for the exact SHA, and the available commit-workflow lookup returned no pull-request-associated runs. This is **not proof that no push CI ran**. It means only that this audit cannot independently attach a successful Windows execution to this exact SHA through the available interfaces.

---

# 3. CRUU20 closure matrix

| CRUU20 finding | CRUU21 status | Assessment |
|---|---|---|
| **CRUU20-001** | **FIXED FOR PROCESS CRASH** | Dedicated `CapabilityProbe` records now predeclare both legal paths, distinguish `ProbeCreatedClaimed` vs `ProbeContentDurable`, record rename intent before rename, and recover exact identity from either location. Real subprocess kill cuts exist for pre-write, partial-write and post-rename/pre-record states. |
| **CRUU20-002** | **FIXED FOR PROCESS TERMINATION / POWER-LOSS CLAIM NOT PROVEN** | File stages now use delete-on-close before first claim; directory creation uses `NtCreateFile` + `FILE_DELETE_ON_CLOSE`; real child-process kill tests cover first-claim windows. See CRUU21-003 for the remaining acceptance boundary. |
| **CRUU20-003** | **FIXED_SOURCE_CORE** | Current migration schema is v5; v5 control grammar is strict; old v4 attempts use explicit legacy recovery disposition rather than being treated as v5; historical-format tests were added. |
| **CRUU20-004** | **FIXED_HELPER_CORE / FINDING-LEVEL EVIDENCE GAP REMAINS** | `ProductionEvidenceTestClassAttribute` automatically validates all declared `ProductionSymbolEvidence` hits. The remaining problem is that subprocess crash sentinels themselves are not necessarily mapped to the integration symbol their names claim. See CRUU21-004. |

---

# 4. Positive rechecks to preserve

## 4.1 Capability probe now has an honest crash state machine

Current source contains:

```text
OwnedArtifactKind.CapabilityProbe
ProbeCreatedClaimed
ProbeContentDurable
ProbeRenamePrepared
ProbeRenamed
ProbeRetired
```

The initial record contains exact Windows file identity, the initial path, the allowed alternate recovery path, and expected eventual content. Before a rename, `ProbeRenamePrepared` is durably recorded. A crash after the rename but before `ProbeRenamed` is recoverable because the destination was already declared.

## 4.2 Partial probe writes are no longer confused with durable content

`FlushDurable()` performs the file flush before writing `ProbeContentDurable`. Recovery requires final content only once the phase is at least `ProbeContentDurable`. An exact identity in `ProbeCreatedClaimed` can therefore be cleaned even if the write is empty or partial.

## 4.3 First-claim file bootstrap is process-crash safe

`WindowsOwnedDurableStage.CreateCrashAtomicBootstrapUnderRoot` creates the stage with `FILE_FLAG_DELETE_ON_CLOSE`. After the exact identity record is durably appended, `PersistAfterDurableClaim()` clears the on-close deletion state.

## 4.4 Directory first-claim gap now has a native bootstrap

`WindowsCrashAtomicDirectoryBootstrap` uses relative `NtCreateFile` with `FILE_DIRECTORY_FILE`, `FILE_WRITE_THROUGH`, `FILE_DELETE_ON_CLOSE`, and `FILE_OPEN_REPARSE_POINT`, retaining the exact new directory handle until its identity claim is durable.

## 4.5 Real process-kill harness exists

The new `PromptHelper.CrashHarness` executes real product primitives, durably signals a named cut, then blocks until the parent kills the process tree. This is materially stronger than an injected `IOException`.

## 4.6 Schema v5 identifies the current recovery contract

`MigrationAttemptManifest.CurrentSchemaVersion` is now 5. v5 validates the current/replacement/displaced probe triplet and alternate-content relationships. Pre-v5 residue can return `LegacyManualCleanupRequired` rather than being interpreted under v5 identity semantics.

## 4.7 Production-symbol evidence is automatic within participating test classes

`ProductionEvidenceTestClassAttribute` wraps test execution and fails any attributed method whose declared production symbol was not actually hit.

---

# 5. Findings

## CRUU21-001 — HIGH
## Initial `Copying` migration marker is still a direct final-path write and can be torn by hard process death

### Affected code

- `src/PromptHelper/Services/MigrationManifestRepository.cs`
- `src/PromptHelper/Services/IMigrationManifestFileOps.cs`
- `src/PromptHelper/Services/DataFolderTransitionCoordinator.cs`
- `src/PromptHelper/Services/DataFolderMigrationService.cs`
- `src/PromptHelper/Services/MigrationRecoveryService.cs`
- `tests/PromptHelper.CrashHarness/Program.cs`
- `tests/PromptHelper.Tests/Cruu20RegressionTests.cs`

### Current source behavior

The Ready-phase marker transition uses an owned durable stage. The **initial** marker does not.

`CreateInitialCopyingManifestDurable` still does:

```csharp
using (Stream stream = _fileOps.CreateNew(markerPath))
{
    stream.Write(bytes, 0, bytes.Length);
    _fileOps.FlushToDisk(stream);
}
```

`DefaultMigrationManifestFileOps.CreateNew` returns a `FileStream` opened with:

```text
FileMode.CreateNew
FileAccess.ReadWrite
FileShare.None
```

The authoritative final pathname `.prompthelper-migration.json` therefore becomes visible immediately when `CreateNew` succeeds. There is no delete-on-close bootstrap and no unpublished staging path for this first marker.

### Hard-crash sequence

```text
T1 target reservation succeeds
T2 migration attempt + v5 manifest object are built
T3 CreateNew(".prompthelper-migration.json") succeeds
T4 authoritative marker pathname now exists
T5 only part of the JSON has been written
T6 process is killed before full write + durable flush completes
```

Possible restart state:

```text
.prompthelper-migration.json exists
bytes are empty / partial / truncated
payload copy may not even have started
```

### Recovery behavior

`DataFolderMigrationService.InspectTarget` checks marker existence first and classifies the target as `InterruptedMigration`.

`MigrationRecoveryService.RecoverForRetry` then calls `TryReadStrict`. A truncated marker fails strict UTF-8/JSON/manifest validation and recovery returns failure. Because the bytes cannot supply trustworthy attempt/phase authority, the application cannot simply guess-delete the marker.

The target can therefore be wedged by a control file the application created before any useful migration state was published.

### Why CRUU20 missed it

CRUU20's hard-crash matrix added cuts around CAS stages, payload stages, Ready-manifest stages, capability probes, migration directories and torn ownership-journal appends. It did not add a hard-crash cut around `CreateInitialCopyingManifestDurable`.

### Required fix

Make the first marker publication crash atomic.

Required externally visible invariant:

```text
before initial marker commit:
    .prompthelper-migration.json is absent

after initial marker commit:
    .prompthelper-migration.json is a complete, strictly parseable Copying marker

never:
    .prompthelper-migration.json exists with partial authoritative JSON
```

A suitable design can create the exact marker object with delete-on-close armed, write + flush the complete JSON through that retained handle, establish marker identity authority, and only then clear delete-on-close. An unpublished-stage/no-overwrite publication is also acceptable if marker-less target retry can always reconcile any stage/ledger residue left before publication.

### Mandatory tests

```text
CRUU21_001_Hard_crash_after_initial_marker_create_before_write_leaves_no_final_marker
CRUU21_001_Hard_crash_during_initial_Copying_marker_write_leaves_no_truncated_final_marker
CRUU21_001_Hard_crash_after_initial_marker_write_before_flush_is_retryable
CRUU21_001_Hard_crash_after_initial_marker_commit_leaves_strictly_parseable_Copying_marker
CRUU21_001_Initial_Copying_marker_has_no_partial_authoritative_final_state
CRUU21_001_Initial_marker_crash_before_payload_copy_does_not_wedge_target
CRUU21_001_Real_transition_retry_recovers_each_initial_marker_crash_cut
```

At least one test must execute the real `DataFolderTransitionCoordinator -> CreateInitialCopyingManifestDurable` path rather than only the repository helper.

---

## CRUU21-002 — HIGH
## Migration marker update and retirement are not exact-marker-identity bound

### Affected code

- `MigrationManifestRepository.CreateInitialCopyingManifestDurable`
- `MigrationManifestRepository.WriteReadyManifestDurable`
- `MigrationManifestRepository.AssertPersistedMarkerMatches`
- `MigrationManifestRepository.DeleteStrict`
- `DataFolderTransitionCoordinator`
- ownership-journal marker authority design

### Marker semantics remain weaker than the rest of the repository

Most destructive recovery code now follows:

```text
pathname != ownership
exact Windows file identity = ownership
```

The migration marker still does not.

### A. Ready update can overwrite a foreign replacement

`WriteReadyManifestDurable` correctly creates the new Ready marker in an owned crash-atomic stage, but publication is:

```csharp
stage.PromoteReplaceExact(markerPath);
```

This binds the **source** to the exact stage. It does not bind the **destination** to the original Copying marker.

Concrete sequence:

```text
T1 Prompt Helper creates valid Copying marker M1
T2 M1 handle closes
T3 another same-user process replaces M1 with foreign regular file F
T4 Ready candidate M2 is created in an exact owned stage
T5 PromoteReplaceExact(markerPath) replaces whatever is at the destination
T6 F is destroyed/replaced by M2
```

A same-byte replacement is still foreign. The current code does not compare destination identity with the Copying marker identity.

### B. Marker retirement can delete a foreign same-attempt/phase replacement

`DeleteStrict` is same-handle safe but not creation-identity safe. It:

```text
opens current marker
strictly parses it
checks AttemptId
checks Phase
deletes that same current object
```

Concrete sequence:

```text
T1 legitimate Ready marker M2 exists
T2 external process replaces M2 with foreign object F
T3 F contains a valid copied/constructed marker with same AttemptId and ReadyToCommit phase
T4 DeleteStrict parses F successfully
T5 attempt + phase match
T6 DeleteStrict deletes F
```

That is foreign-file destruction.

### C. Byte comparison does not restore identity ownership

`AssertPersistedMarkerMatches` compares the persisted marker bytes with expected serialized bytes. A byte-identical foreign replacement passes this content check.

### Required fix

Introduce a first-class durable `MigrationMarkerAuthority` across:

```text
initial Copying publication
Copying -> Ready transition
restart/retry
final marker retirement
```

Recommended authority record:

```text
OwnedArtifactKind.MigrationMarker
AttemptId
MarkerRelativePath
WindowsFileIdentity
MarkerPhase
ExpectedLength
ExpectedSha256Hex
```

Ready publication must be an exact-current CAS:

```text
open current marker exact handle
require Identity == durable Copying marker identity
require content == expected Copying marker
create exact Ready candidate
publish only against that exact current object
record new Ready marker identity
```

A different destination object must be preserved.

Marker retirement must require exact durable marker identity in addition to strict semantic/content validation.

### Mandatory tests

```text
CRUU21_002_Ready_marker_update_rejects_same_path_foreign_replacement
CRUU21_002_Ready_marker_update_preserves_same_bytes_different_identity_replacement
CRUU21_002_Ready_marker_update_requires_exact_Copying_marker_identity
CRUU21_002_DeleteStrict_preserves_same_attempt_phase_foreign_marker_identity
CRUU21_002_DeleteStrict_preserves_byte_identical_foreign_marker_identity
CRUU21_002_DeleteStrict_requires_durable_marker_identity_not_attempt_phase_only
CRUU21_002_Copying_marker_identity_survives_restart
CRUU21_002_Ready_marker_identity_survives_restart
CRUU21_002_Marker_identity_authority_advances_atomically_Copying_to_Ready
CRUU21_002_No_marker_write_path_unconditionally_replaces_current_marker_path
```

At least the first five must use real Windows file-identity substitution.

---

## CRUU21-003 — MED-HIGH / ACCEPTANCE BOUNDARY
## Current hard-crash evidence proves process termination, not abrupt machine/power-loss durability

### What current tests prove

`RunCrash` starts `PromptHelper.CrashHarness.exe`, waits for a durably written cut signal, kills the entire child process tree, waits for process exit and handle release, then inspects/reconciles the filesystem.

This is real process termination and is much stronger than normal exception injection.

### What it does not prove

It does not emulate:

```text
abrupt VM power-off
host reset
kernel crash
physical power loss
storage-controller persistence/reordering across machine failure
```

The new first-claim strategy intentionally relies on delete-on-close before durable claim. Process-kill testing proves behavior where the OS remains alive and tears down process handles. It does not itself prove the post-reboot disk state when normal process teardown does not occur.

This is **not** a claim that the current NTFS design necessarily fails under power loss. It is a claim that the current evidence does not prove the broader guarantee.

### Required resolution

Choose an explicit contract.

**Option A — process-termination guarantee only:** document that automatic recovery is proven for application/process termination and the enumerated durable-write cuts, while machine/power-loss behavior is best-effort/fail-closed unless separately verified.

**Option B — power-loss is a release invariant:** add abrupt Windows VM reset testing on a dedicated virtual disk and run real recovery after reboot.

### Mandatory gates if power loss stays in scope

```text
CRUU21_003_VM_abrupt_reset_before_first_file_claim_leaves_no_unrecoverable_artifact
CRUU21_003_VM_abrupt_reset_after_first_claim_before_persist_transition_is_recoverable
CRUU21_003_VM_abrupt_reset_during_first_journal_append_is_recoverable_or_fail_closed
CRUU21_003_VM_abrupt_reset_after_probe_rename_before_phase_advance_is_recoverable
CRUU21_003_VM_abrupt_reset_during_initial_marker_publication_is_recoverable
CRUU21_003_Release_claim_distinguishes_process_kill_from_power_loss
```

If power loss is intentionally out of scope, replace these with an explicit contract sentinel instead of claiming they ran.

---

## CRUU21-004 — MED-HIGH / VERIFICATION DEFECT
## Finding-level runtime evidence can still substitute a normal test for the specific hard-crash integration path

### What is fixed

The automatic evidence wrapper is real. If a test carries `ProductionSymbolEvidence`, the wrapper requires the declared symbol to be hit during that test.

### Remaining gap: which test carries the authority

For `CRUU20-001`, the finding map requires `DefaultCapabilityFileOps.CreateOwnedProbe` at the **finding** level. The hard-crash tests launch/kill the child and then often call a lower-level cleanup primitive directly. They are not individually required to prove that `MigrationRecoveryService.RecoverForRetry` processed the crash state.

For `CRUU20-002`, the specific kill sentinels are exact required names, but the multi-symbol production evidence is concentrated in the separate normal-success test `CRUU20_002_First_claim_protocol_is_crash_atomic_not_only_exception_safe`.

The gate can therefore prove:

```text
the crash sentinel name ran
AND
some mapped test for the same finding hit a production symbol
```

without proving:

```text
the hard-crash sentinel itself reached its exact product cut
AND
the parent then executed the intended real recovery integration path
```

### Required fix

Make crash evidence **per sentinel**, not merely per finding.

Recommended mapping:

```text
exact sentinel
-> exact child crash cut
-> exact child production symbol
-> exact parent recovery symbol(s)
```

The crash harness signal should durably identify the exact cut reached. The parent sentinel should execute the real mapped recovery surface and carry automatic evidence for it.

### Mandatory tests

```text
CRUU21_004_CRUU20_001_each_hard_crash_sentinel_has_child_cut_authority
CRUU21_004_CRUU20_001_retry_sentinels_execute_real_MigrationRecoveryService
CRUU21_004_CRUU20_002_each_kill_sentinel_is_runtime_bound_to_its_production_creator
CRUU21_004_Normal_success_test_cannot_substitute_for_hard_crash_runtime_authority
CRUU21_004_CrashHarness_signal_contains_exact_production_cut_identity
CRUU21_004_Hard_crash_evidence_map_is_per_sentinel_not_only_per_finding
CRUU21_004_Release_gate_validates_child_and_parent_crash_evidence
```

---

# 6. Architectural diagnosis

The source is now much closer to a coherent authority model.

The remaining product findings share one root cause:

> The migration marker still behaves like a trusted semantic pathname, while the rest of the recovery architecture now treats durable control objects as exact filesystem identities.

Mature subsystems now use:

```text
exact object identity
+ strict location
+ durable phase
+ expected content where relevant
+ same-handle destructive action
```

The marker still uses:

```text
special pathname
+ valid JSON
+ expected AttemptId/Phase
```

That is now the weakest destructive authority in the migration pipeline.

The verification findings have a similar consolidation theme:

```text
process-kill evidence != machine-power-loss evidence
finding-level symbol evidence != exact crash-sentinel integration evidence
```

---

# 7. Ordered CRUU21 implementation plan

## PHASE 01 — Create a first-class `MigrationMarkerAuthority`

Define one marker authority record/lease containing:

```text
AttemptId
marker relative path
exact WindowsFileIdentity
phase
expected serialized length/hash
```

Keep it valid from initial Copying publication until final marker retirement.

## PHASE 02 — Make initial Copying marker publication crash atomic

Replace direct final-path `FileStream.CreateNew` publication. Add real child-process cuts after create, during write, after write/before flush, after flush/before commit, and immediately after commit.

## PHASE 03 — Make Copying -> Ready an exact-current CAS

Verify exact current marker identity + content before publishing Ready. Preserve any different occupant. Advance durable marker identity authority to the new Ready object after publication.

## PHASE 04 — Identity-bind marker retirement

`DeleteStrict` must require expected marker identity plus semantic/content checks. Same-content and same-attempt foreign objects must be preserved.

## PHASE 05 — Extend marker recovery matrix across restart

Test Copying marker, Ready marker, post-publication bookkeeping failure, authority append failure, foreign replacement, missing marker and stale authority records.

## PHASE 06 — Decide and encode machine-power-loss scope

Either narrow the guarantee to process termination or add abrupt VM-reset evidence.

## PHASE 07 — Make crash evidence per sentinel

Create an authority mapping from exact sentinel to child cut, child product symbol and parent recovery symbol(s). Verify it in CI.

## PHASE 08 — Full Windows acceptance

On the exact candidate SHA:

```text
1. Fresh checkout.
2. Restore + Release build of full solution.
3. Marker hard-process-crash suite.
4. Marker foreign-identity substitution suite.
5. CRUU20 subprocess suite.
6. CRUU16–CRUU19 recovery regression suite.
7. v3/v4 -> v5 compatibility suite.
8. Automatic production-evidence suite.
9. Per-sentinel crash-evidence gate.
10. Filesystem/reparse integration suite.
11. Full suite once.
12. Full suite five consecutive times.
13. Exact sentinel verification across retained TRX.
14. Finding coverage gate.
15. If power loss remains in scope: abrupt-VM-reset matrix.
16. Pinned icon reproduction.
17. Self-contained win-x64 publish.
18. Strict published executable icon verification.
19. Release workflow on exact candidate SHA/tag.
20. Independent fresh source/recovery audit.
```

---

# 8. Proposed CRUU21 sentinel set

```text
# CRUU21-001
CRUU21_001_Hard_crash_after_initial_marker_create_before_write_leaves_no_final_marker
CRUU21_001_Hard_crash_during_initial_Copying_marker_write_leaves_no_truncated_final_marker
CRUU21_001_Hard_crash_after_initial_marker_write_before_flush_is_retryable
CRUU21_001_Hard_crash_after_initial_marker_commit_leaves_strictly_parseable_Copying_marker
CRUU21_001_Initial_Copying_marker_has_no_partial_authoritative_final_state
CRUU21_001_Initial_marker_crash_before_payload_copy_does_not_wedge_target
CRUU21_001_Real_transition_retry_recovers_each_initial_marker_crash_cut

# CRUU21-002
CRUU21_002_Ready_marker_update_rejects_same_path_foreign_replacement
CRUU21_002_Ready_marker_update_preserves_same_bytes_different_identity_replacement
CRUU21_002_Ready_marker_update_requires_exact_Copying_marker_identity
CRUU21_002_DeleteStrict_preserves_same_attempt_phase_foreign_marker_identity
CRUU21_002_DeleteStrict_preserves_byte_identical_foreign_marker_identity
CRUU21_002_DeleteStrict_requires_durable_marker_identity_not_attempt_phase_only
CRUU21_002_Copying_marker_identity_survives_restart
CRUU21_002_Ready_marker_identity_survives_restart
CRUU21_002_Marker_identity_authority_advances_atomically_Copying_to_Ready
CRUU21_002_No_marker_write_path_unconditionally_replaces_current_marker_path

# CRUU21-003
CRUU21_003_VM_abrupt_reset_before_first_file_claim_leaves_no_unrecoverable_artifact
CRUU21_003_VM_abrupt_reset_after_first_claim_before_persist_transition_is_recoverable
CRUU21_003_VM_abrupt_reset_during_first_journal_append_is_recoverable_or_fail_closed
CRUU21_003_VM_abrupt_reset_after_probe_rename_before_phase_advance_is_recoverable
CRUU21_003_VM_abrupt_reset_during_initial_marker_publication_is_recoverable
CRUU21_003_Release_claim_distinguishes_process_kill_from_power_loss

# CRUU21-004
CRUU21_004_CRUU20_001_each_hard_crash_sentinel_has_child_cut_authority
CRUU21_004_CRUU20_001_retry_sentinels_execute_real_MigrationRecoveryService
CRUU21_004_CRUU20_002_each_kill_sentinel_is_runtime_bound_to_its_production_creator
CRUU21_004_Normal_success_test_cannot_substitute_for_hard_crash_runtime_authority
CRUU21_004_CrashHarness_signal_contains_exact_production_cut_identity
CRUU21_004_Hard_crash_evidence_map_is_per_sentinel_not_only_per_finding
CRUU21_004_Release_gate_validates_child_and_parent_crash_evidence
```

**Total proposed CRUU21 sentinels: 30**

---

# 9. Required post-repair invariants

```text
MARKER-01
The first authoritative migration marker is either absent or complete and strictly parseable.

MARKER-02
A partial initial marker cannot survive a supported process-crash cut.

MARKER-03
The marker has exact durable filesystem identity authority from Copying publication through retirement.

MARKER-04
Ready publication never overwrites an occupant whose identity differs from the exact Copying marker.

MARKER-05
Same bytes are not marker ownership.

MARKER-06
Same AttemptId/Phase are not marker ownership.

MARKER-07
Marker deletion requires exact identity plus semantic/content validation.

MARKER-08
Marker authority advances atomically when Copying becomes Ready.

MARKER-09
A foreign marker replacement is always preserved.

CRASH-EVIDENCE-01
Every kill-test sentinel is bound to the exact child production cut it claims.

CRASH-EVIDENCE-02
Every recovery sentinel executes the mapped real parent recovery integration surface.

CRASH-EVIDENCE-03
A normal success test cannot substitute for crash-test runtime authority.

CRASH-SCOPE-01
Process-kill evidence is labeled as process-kill evidence.

CRASH-SCOPE-02
Power-loss guarantees require separate abrupt-machine evidence.

REL-01
All CRUU21 sentinels pass.

REL-02
Full suite passes five consecutive times.

REL-03
Exact required sentinel list passes from retained TRX.

REL-04
Automatic production evidence passes.

REL-05
Per-sentinel crash evidence passes.

REL-06
Exact final SHA has independently bindable Windows CI evidence.

REL-07
Fresh independent re-audit reports zero findings.
```

---

# 10. Repair priority

```text
P0:
    CRUU21-001  initial marker crash atomicity
    CRUU21-002  marker exact-identity authority

P1 / acceptance:
    CRUU21-004  per-sentinel crash evidence
    CRUU21-003  explicit process-kill vs power-loss contract
```

CRUU21-001 and CRUU21-002 should be implemented together. If the first marker becomes crash-atomic but no durable marker identity exists, Ready/delete remain foreign-object unsafe. If marker identity is added but the first marker can still survive truncated, startup can still become wedged before that identity is useful.

---

# 11. Most relevant source files

```text
src/PromptHelper/Services/MigrationManifestRepository.cs
src/PromptHelper/Services/IMigrationManifestFileOps.cs
src/PromptHelper/Services/MigrationAttemptManifest.cs
src/PromptHelper/Services/MigrationRecoveryService.cs
src/PromptHelper/Services/DataFolderTransitionCoordinator.cs
src/PromptHelper/Services/DataFolderMigrationService.cs
src/PromptHelper/Services/IOwnedArtifactJournal.cs
src/PromptHelper/Services/OwnedArtifactReconciler.cs
src/PromptHelper/Services/WindowsOwnedDurableStage.cs
src/PromptHelper/Services/WindowsExpectedTargetAuthority.cs
src/PromptHelper/Services/WindowsAtomicExpectedFileReplacer.cs
tests/PromptHelper.CrashHarness/Program.cs
tests/PromptHelper.Tests/Cruu20RegressionTests.cs
tests/PromptHelper.Tests/ProductionEvidenceHarness.cs
tools/FindingCoverageMap.json
tools/VerifyFindingCoverage.ps1
.github/workflows/release.yml
```

---

# 12. Final assessment

This repair round is a real step forward.

The codebase now has credible process-crash machinery rather than only exception injection:

```text
delete-on-close bootstrap
exact file/directory identity
predeclared probe locations
durable phase records
a killed subprocess harness
explicit migration protocol versioning
automatic per-test symbol evidence
```

CRUU21 is therefore no longer finding broad weaknesses across every persistence primitive. The remaining product defects are concentrated in one old authority island:

```text
.prompthelper-migration.json
```

It is still treated as "the valid marker at this pathname" rather than "the exact marker object this attempt created", and its first publication still exposes the final pathname before a complete durable marker is guaranteed.

Bringing the marker under the same identity/phase model should remove the last obvious inconsistency in the migration transaction.

---

# 13. Final status

```text
AUDITED_HEAD                         = c2dc3db4d9cfaffbe4fd9156722566ef5f150097

CRUU21_FINDINGS                      = 4
HIGH                                 = 2
MED_HIGH                             = 2

CRUU20_CORE_REPAIRS_REAL             = YES
CRUU20_PROCESS_CRASH_CORE_CLOSED     = YES

SOURCE_AUDIT_CLEAN                   = NO

WINDOWS_RUNTIME_DIRECTLY_EXECUTED    = NO
EXACT_HEAD_CI_STATUS                 = NOT INDEPENDENTLY BOUND

STRICT_RELEASE_READY                 = NO
ZERO_DEFECT_VERIFIED                 = NO
```
