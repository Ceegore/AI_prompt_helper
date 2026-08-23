# CRUU20 — Independent Post-CRUU19 Adversarial Re-Audit and Fix Plan

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited HEAD:** `4456af855d98d62308d1a7eea606e504be18043d`  
**Parent / CRUU19 audit snapshot:** `d7e7b4d8a1360f46de0a51d8caa0cda51f3c1d60`  
**Repair commit:** `4456af855d98d62308d1a7eea606e504be18043d` — `fix(recovery): close CRUU19 ownership gaps`  
**Previous audit:** `cruu19.md`  
**Audit date:** 2026-08-23  
**Mode:** independent source, hard-crash cut, durable-provenance, migration retry, protocol-versioning, directory-identity and regression-evidence re-audit.

> Report only. No production source or GitHub repository content was modified by this audit.

---

# 1. Executive verdict

The CRUU19 repair is a **substantial source-level improvement**. The live capability-probe foreign-object destruction path has been removed: probe objects now retain their creation handles across writes, renames and retirement; retry probe deletion requires exact identity plus content; the Ready-manifest stage exact-cleans a failed first claim; migration directories now carry exact filesystem identity; and expected-exception production-hit verification was repaired.

However, the new ownership implementation exposes the next layer of crash consistency.

This audit found **4 actionable findings**:

```text
HIGH      = 2
MED-HIGH  = 2
TOTAL     = 4
```

The two High findings are both about **hard process/power loss**, not ordinary caught exceptions:

1. The new capability-probe durable protocol is not crash-complete. It records intended final bytes before those bytes are necessarily durable, and performs a handle-bound rename before the new pathname is durably recorded.
2. The wider ownership-bootstrap pattern remains exception-safe rather than crash-atomic: filesystem objects are created before their first durable ownership claim exists.

Two medium-high issues remain:

3. The migration recovery protocol changed materially while the persisted manifest schema remains version `4`.
4. Production-symbol evidence is still opt-in per test body; attributes are structurally checked but not automatically enforced after every attributed test.

Strict release acceptance therefore remains blocked.

---

# 2. Audit freeze and execution-evidence status

`main` was fetched at audit start and again immediately before report generation.

Both reads returned:

```text
4456af855d98d62308d1a7eea606e504be18043d
```

The repair commit is directly based on the prior CRUU19 audited SHA.

```text
AUDITED_HEAD                         = 4456af855d98d62308d1a7eea606e504be18043d
HEAD_STABLE_DURING_AUDIT             = YES

CRUU20_FINDINGS                      = 4
HIGH                                 = 2
MED_HIGH                             = 2

SOURCE_AUDIT_CLEAN                   = NO
CRUU19_STRICTLY_CLOSED               = NO

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

The available GitHub status interfaces do not independently bind a clean Windows run to this exact SHA. This is not proof that no push workflow ran.

---

# 3. CRUU19 closure matrix

| CRUU19 finding | CRUU20 status | Assessment |
|---|---|---|
| **CRUU19-001** | **LIVE CORE FIXED / HARD-CRASH PROTOCOL REOPENED** | Live probes now retain exact handles and retry requires identity + content. The new durable probe protocol is not complete across partial-write and rename-before-record cuts. See CRUU20-001. |
| **CRUU19-002** | **FIXED_SOURCE_CORE** | `DefaultMigrationManifestFileOps.CreateOwnedStage` now exact-deletes on first ownership-record exception. The remaining first-claim process-death gap is systemic. See CRUU20-002. |
| **CRUU19-003** | **CORE FIXED / BOOTSTRAP + VERSIONING REMAIN** | Rollback/retry now compare exact directory identity. Directory creation still precedes its first durable identity record, and old v4 attempts contain no such record. See CRUU20-002/003. |
| **CRUU19-004** | **SPECIFIC HELPER FIXED / EVIDENCE GATE REOPENED** | `AssertProductionHitThrows<T>` now validates expected-exception hits. Attribute evidence still is not automatically enforced per test. See CRUU20-004. |

---

# 4. Positive rechecks

The following source improvements are convincing and should remain closed unless new evidence appears.

## 4.1 Live capability probe authority

`IOwnedCapabilityProbe` now owns an exact retained file handle and exposes `Write`, `FlushDurable`, `RenameNoOverwriteRetainingOwnership`, and `DeleteExact`. `DataRootCapabilityValidator` no longer relies on raw `File.Replace` or `File.Delete` for the live owned probe transaction.

## 4.2 Retry capability-probe authority

The current manifest declares current, replacement and displaced probe paths. Retry deletion checks recorded identity, the manifest's allowed expected content, the journal's content authority and the current file content before same-handle deletion.

## 4.3 Ready-manifest stage exception cleanup

`DefaultMigrationManifestFileOps.CreateOwnedStage` now exact-deletes the newly-created stage before releasing its creation handle when initial ownership recording throws.

## 4.4 Directory ownership

`WindowsOwnedDirectoryCreator` captures `WindowsFileIdentity`; `MigrationTargetTransaction` stores `OwnedDirectoryClaim`; in-process rollback compares the exact current directory identity; restart cleanup requires a durable `MigrationDirectory` identity record.

## 4.5 Expected-exception evidence helper

`AssertProductionHitThrows<TException>` captures the exception, verifies the required hit, then validates the exact exception type. The CRUU19-004 expected-exception bypass is fixed.

---

# 5. Findings

## CRUU20-001 — HIGH
## Capability-probe durable ownership is not crash-complete across partial writes and renames

### Affected code

- `ICapabilityFileOps.cs`
- `DataRootCapabilityValidator.cs`
- `WindowsOwnedDurableStage.cs`
- `ProvenanceBoundCleanup.cs`
- `MigrationRecoveryService.cs`
- `MigrationManifestBuilder.cs`
- `Cruu19RegressionTests.cs`

### A. Intended bytes are recorded before they are necessarily durable

`DefaultCapabilityFileOps.CreateOwnedProbe` durably records:

```text
identity
path
CandidateLength
CandidateSha256Hex
```

for the intended probe payload **before** the caller performs:

```text
Write
FlushDurable
```

A hard crash after the ownership claim but before/during the write can therefore leave:

```text
exact app-created identity = known
actual bytes = empty / partial
journal bytes = final "create" or "replace"
```

Restart then calls `DeleteCapabilityProbeIfProven`. Identity matches, but the content check fails, so the exact app-owned partial probe becomes `PreservedUnproven` and retry aborts.

This is a real recovery mismatch: exact creation identity proves ownership of an ephemeral probe, while the journal's content field is being interpreted as a durable phase assertion that has not actually happened yet.

### B. Rename happens before the new pathname record

Current ordering is effectively:

```text
handle-bound rename
CurrentPath = new path
append RecordLocation(new path)
```

Crash cut 1:

```text
A recorded @ current
A renamed current -> displaced
HARD CRASH before displaced record
```

Restart sees exact A at `displaced`, but journal authority exists only for `current`.

Crash cut 2:

```text
B recorded @ replacement
B renamed replacement -> current
HARD CRASH before current record
```

Restart sees exact B at `current`, but B is only recorded at `replacement`; an older A-at-current record cannot prove B.

Both cuts produce `PreservedUnproven` and wedge automatic retry even though the object is genuinely application-owned.

### Why CRUU19 tests missed this

The CRUU19 tests exercise live substitution barriers and caught exceptions. They do not stop a process **after the real rename and before the durable location append**, nor after first claim but before content becomes durable. Normal exception cleanup is not equivalent to process death.

### Required fix

Introduce a dedicated durable probe transaction rather than mutable generic `Stage` path records.

Suggested model:

```text
OwnedArtifactKind.CapabilityProbe

OperationId
Identity
AllowedRecoveryPaths[]
Phase
ExpectedLength/hash
```

Suggested phases:

```text
CreatedClaimed
ContentDurable
RenamePrepared
Renamed
Retired
```

Required invariant:

> Every pathname at which the exact object may survive a process death is durably predeclared before the transition that can place it there.

For `CreatedClaimed`, exact identity should be enough to clean the known ephemeral probe even if its content is partial. Once `ContentDurable` is recorded, content mismatch can become a fail-closed condition.

### Mandatory tests

```text
CRUU20_001_Crash_after_probe_claim_before_full_write_is_recoverable
CRUU20_001_Crash_during_probe_write_partial_content_is_recoverable
CRUU20_001_Crash_after_probe_data_write_before_flush_is_recoverable
CRUU20_001_Crash_after_current_probe_rename_before_new_location_record_is_recoverable
CRUU20_001_Crash_after_replacement_probe_rename_before_new_location_record_is_recoverable
CRUU20_001_Probe_claim_predeclares_all_recovery_locations_before_first_rename
CRUU20_001_Probe_recovery_matrix_covers_every_durable_phase_and_location
CRUU20_001_Partial_exact_owned_probe_does_not_become_PreservedUnproven
CRUU20_001_Foreign_partial_probe_same_path_is_still_preserved
CRUU20_001_Foreign_same_content_different_identity_is_still_preserved
```

The rename tests must terminate execution after the actual handle-bound rename and before the post-rename journal append; they must not merely throw an exception that lets normal cleanup run.

---

## CRUU20-002 — HIGH
## First ownership claims are exception-safe, not process-crash-atomic

### Affected production families

- CAS staging
- payload migration staging
- Ready-manifest staging
- capability-probe creation
- migration-owned directory creation
- ownership journal first-append semantics

### Current common pattern

```text
create filesystem object
then append durable ownership claim
```

Repair rounds correctly added:

```text
if Record(...) throws:
    exact-delete through the retained handle
```

but a process death bypasses that catch.

### File cut

```text
T1 CREATE_NEW succeeds
T2 file exists
T3 process dies
T4 first ownership claim never becomes durable
```

On restart the declared temp/control exists but has no identity record:

```text
DeleteOwnedFileIfProven
-> PreservedUnproven
-> retry aborts
```

This applies to at least payload stages, Ready-manifest stages and capability-probe files; the same underlying pattern exists around other owned stages.

### Torn first journal append

The strict ownership parser correctly discards only a final non-newline torn append. But if that torn line is the **first claim** for a newly-created live artifact, the artifact survives while its only claim disappears from parsed authority.

The parser is not the defect; the creation protocol is.

### Directory cut

Current directory ownership is:

```text
CreateDirectoryW
open directory
capture identity
Record(MigrationDirectory)
```

A hard crash between creation and the durable identity claim leaves a real attempt-created directory but no authority current retry may safely use. Retry correctly preserves it, but automatic recovery is wedged.

### Required fix

This needs a crash-atomic bootstrap contract, not another catch block.

For files, investigate a proven protocol where an unclaimed object cannot survive process death, for example a carefully verified delete-on-close/bootstrap state:

```text
create temporary object in auto-delete state
persist + flush ownership claim
only then clear automatic deletion
```

Use this only if Windows/filesystem semantics are proven for supported systems.

For directories, choose an equivalent design that supplies creation authority across process death, or restructure migration ownership so a child directory never has an unrecorded destructive-ownership window.

Required invariant:

```text
before durable claim:
    process death cannot leave a persistent unclaimed artifact

after durable claim:
    recovery has exact object identity
```

### Mandatory tests

These must use a subprocess/kill harness rather than ordinary injected exceptions:

```text
CRUU20_002_Hard_crash_between_CAS_stage_create_and_first_claim_leaves_no_unproven_stage
CRUU20_002_Hard_crash_between_payload_stage_create_and_first_claim_leaves_no_unproven_temp
CRUU20_002_Hard_crash_between_manifest_stage_create_and_first_claim_does_not_wedge_retry
CRUU20_002_Hard_crash_between_probe_create_and_first_claim_does_not_wedge_retry
CRUU20_002_Hard_crash_between_directory_create_and_identity_claim_does_not_wedge_retry
CRUU20_002_Torn_first_ownership_append_cannot_leave_live_unproven_artifact
CRUU20_002_First_claim_protocol_is_crash_atomic_not_only_exception_safe
```

Recommended harness:

```text
parent starts helper child
child executes real production primitive
child signals exact cut
parent kills child without cleanup
parent inspects disk
parent executes real recovery
parent asserts terminal invariants
```

---

## CRUU20-003 — MED-HIGH
## Migration ownership protocol changed materially while manifest schema remains v4

### Current fact

The parent CRUU19-audited build declares:

```text
MigrationAttemptManifest.CurrentSchemaVersion = 4
```

The current repair build also declares:

```text
CurrentSchemaVersion = 4
```

Yet the recovery protocol changed.

### Old v4 behavior

The parent v4 protocol:

- declared current/replacement probe controls only;
- did not declare a displaced probe control;
- had no alternate expected-content fields;
- did not persist probe creation identity;
- did not persist `MigrationDirectory` identity for attempt-created prompts/recovery directories.

### Current v4 behavior

Current v4:

- declares current/replacement/displaced probe controls;
- adds `alternateExpectedLength` and `alternateExpectedSha256Hex`;
- requires exact ownership-journal identity for probe deletion;
- requires `MigrationDirectory` identity for attempt-created directory deletion.

These are protocol semantics, not cosmetic fields.

### Old v4 attempt -> new binary

A legitimate interrupted old v4 migration may have attempt-created directories or probe residue but cannot have identity records the old build never produced.

The new binary sees `schemaVersion = 4`, applies current identity rules, gets `PreservedUnproven`, and fails retry.

That is fail-safe but not compatible v4 recovery.

### New v4 marker -> old binary

The current v4 marker can contain alternate probe fields. The old strict v4 parser's allowed-member list did not include them. Therefore an old reader can reject a newer same-version marker as malformed/unknown-member content rather than as a newer protocol.

The schema version no longer uniquely identifies the recovery contract.

### Existing regression change masks upgrade compatibility

Historical tests that used `Directory.CreateDirectory` to simulate an interrupted attempt were changed to use `WindowsOwnedDirectoryCreator`, thereby generating new-protocol identity records. That validates the current protocol but no longer tests a real previous-v4 attempt.

Both tests should exist separately.

### Required fix

Bump to:

```text
CurrentSchemaVersion = 5
```

Define strict v5 invariants for:

```text
probe control paths
alternate content authority
displaced controls
ownership-protocol expectations
```

Keep explicit v3/v4 parsing/recovery branches.

Do not infer v5 identity guarantees from a v4 marker.

If legacy residue cannot be safely auto-deleted, preserve it and emit a dedicated legacy-recovery result rather than a generic current-protocol ownership failure.

### Mandatory tests

Use frozen serialized historical fixtures:

```text
CRUU20_003_Current_schema_version_bumps_when_ownership_protocol_changes
CRUU20_003_Parent_v4_interrupted_attempt_is_not_treated_as_v5_identity_protocol
CRUU20_003_Parent_v4_attempt_created_dirs_have_explicit_legacy_recovery_outcome
CRUU20_003_Parent_v4_probe_residue_has_explicit_legacy_recovery_outcome
CRUU20_003_Legacy_v4_clean_attempt_can_retire_when_no_destructive_inference_is_needed
CRUU20_003_New_protocol_marker_roundtrips_as_v5
CRUU20_003_v5_requires_displaced_probe_control
CRUU20_003_v5_requires_consistent_alternate_probe_content_authority
CRUU20_003_Old_v4_reader_rejects_v5_by_schema_not_same_version_unknown_member
```

If v4 builds have already been distributed to real users, operational severity should be raised to HIGH.

---

## CRUU20-004 — MED-HIGH / VERIFICATION DEFECT
## Production-symbol evidence remains opt-in rather than automatically enforced per attributed test

### What is fixed

`AssertProductionHitThrows<T>` correctly validates the hit when the tested action throws.

### Remaining gate design

Production instrumentation is still essentially:

```csharp
ProductionRuntimeEvidence.SinkForTests
ProductionRuntimeEvidence.Hit(symbol)
```

A test must explicitly install/validate the sink via a helper.

The coverage map verifies required symbol strings. Meta-tests verify matching `ProductionSymbolEvidenceAttribute` metadata exists on mapped tests.

But the attribute itself does not automatically assert runtime execution.

A test can therefore look like:

```csharp
[TestMethod]
[ProductionSymbolEvidence("MigrationRecoveryService.RecoverForRetry")]
public void LooksCovered()
{
    Assert.IsTrue(true);
}
```

and satisfy an attribute-presence structural check unless another mechanism notices that no runtime hit was captured.

Current CRUU19 tests generally call the helpers correctly; this finding is about the evidence system's bypassability.

### Required fix

Make evidence automatic for every attributed test.

Practical MSTest pattern:

```text
[TestInitialize]
    install a runtime hit collector

[TestCleanup]
    identify current TestContext.TestName
    reflect ProductionSymbolEvidenceAttribute values
    require every declared symbol in the collected hit set
    restore prior sink
```

If MSTest cleanup semantics are insufficient, use a custom execution wrapper/attribute.

Helper methods may remain convenience APIs but must not be the only authority.

### Mandatory tests

```text
CRUU20_004_Attributed_test_without_runtime_hit_fails_automatically
CRUU20_004_Attributed_expected_exception_test_without_hit_fails_automatically
CRUU20_004_Attributed_test_with_required_hit_passes_automatically
CRUU20_004_Multi_symbol_attribute_requires_every_symbol_hit
CRUU20_004_Evidence_enforcement_does_not_depend_on_AssertProductionHit_helper
CRUU20_004_All_ProductionSymbolEvidence_tests_use_automatic_runtime_harness
CRUU20_004_Nested_evidence_capture_restores_previous_sink
```

A deliberately fake attributed test should be executed in an isolated harness and demonstrably fail because the production symbol was never hit.

---

# 6. Architectural diagnosis

The repository has largely converged on the correct **live authority** rule:

```text
pathname is not ownership
filesystem identity is ownership
verification and destruction stay on the exact retained handle
```

The remaining hard problem is the transition from:

```text
volatile retained-handle authority
```

to:

```text
durable restart authority
```

The current pattern often does:

```text
create exact object
then record ownership
```

and:

```text
rename exact object
then record new path
```

That works for caught exceptions because cleanup can execute. It does not guarantee recovery after process death.

The next repair should enforce this rule:

> Every externally visible filesystem state that can survive process death must already have durable recovery authority capable of explaining that state before the transition which creates it.

For a moving file: predeclare every legal location.

For a newly-created artifact: either the unclaimed object cannot survive process death, or durable authority must already exist.

For persisted recovery formats: the schema must identify which authority contract created the state.

---

# 7. Ordered implementation plan

## PHASE 01 — Dedicated durable capability-probe transaction

Introduce `OwnedArtifactKind.CapabilityProbe` and explicit phases.

Predeclare all possible recovery paths before rename.

Distinguish creation/partial-write state from content-durable state.

Do not overload generic `Stage` records with post-hoc path updates.

## PHASE 02 — Crash-atomic first-claim protocol

Inventory every owned creation path and replace exception-only bootstrap safety with a process-death-safe contract.

Do not close this phase with injected `IOException` tests.

## PHASE 03 — Manifest schema v5 and legacy recovery

Bump current migration schema to v5.

Define strict v5 invariants.

Route v3/v4 through explicit legacy recovery behavior.

Keep frozen historical serialized fixtures.

## PHASE 04 — Automatic per-test runtime evidence

Install runtime evidence collection automatically and validate every `ProductionSymbolEvidenceAttribute` after each test.

Remove reliance on test authors remembering helper calls.

## PHASE 05 — Subprocess hard-crash harness

Create deterministic named crash cuts and kill a helper process without cleanup.

Cover creation, first claim, torn append, partial write, rename-before-phase-record, phase-record boundaries and terminal cleanup.

## PHASE 06 — Historical regression replay

Re-run CRUU16–CRUU19 invariants under the new protocol, especially CAS preimages, migration final identity/content, Ready marker retirement, directory substitutions and committed-restart handling.

## PHASE 07 — Final Windows acceptance

On the exact candidate SHA:

```text
fresh clone
Release build
CRUU20 targeted suite
filesystem/reparse suite
hard-crash subprocess matrix
v3/v4 -> v5 compatibility suite
full suite once
full suite x5
exact sentinel verification from retained TRX
automatic runtime-evidence gate
finding coverage gate
self-contained win-x64 publish
strict icon/release-asset verification
exact-SHA workflow evidence
tag/release workflow exercise
fresh independent re-audit
```

---

# 8. Proposed CRUU20 sentinels

```text
# CRUU20-001
CRUU20_001_Crash_after_probe_claim_before_full_write_is_recoverable
CRUU20_001_Crash_during_probe_write_partial_content_is_recoverable
CRUU20_001_Crash_after_probe_data_write_before_flush_is_recoverable
CRUU20_001_Crash_after_current_probe_rename_before_new_location_record_is_recoverable
CRUU20_001_Crash_after_replacement_probe_rename_before_new_location_record_is_recoverable
CRUU20_001_Probe_claim_predeclares_all_recovery_locations_before_first_rename
CRUU20_001_Probe_recovery_matrix_covers_every_durable_phase_and_location
CRUU20_001_Partial_exact_owned_probe_does_not_become_PreservedUnproven
CRUU20_001_Foreign_partial_probe_same_path_is_still_preserved
CRUU20_001_Foreign_same_content_different_identity_is_still_preserved

# CRUU20-002
CRUU20_002_Hard_crash_between_CAS_stage_create_and_first_claim_leaves_no_unproven_stage
CRUU20_002_Hard_crash_between_payload_stage_create_and_first_claim_leaves_no_unproven_temp
CRUU20_002_Hard_crash_between_manifest_stage_create_and_first_claim_does_not_wedge_retry
CRUU20_002_Hard_crash_between_probe_create_and_first_claim_does_not_wedge_retry
CRUU20_002_Hard_crash_between_directory_create_and_identity_claim_does_not_wedge_retry
CRUU20_002_Torn_first_ownership_append_cannot_leave_live_unproven_artifact
CRUU20_002_First_claim_protocol_is_crash_atomic_not_only_exception_safe

# CRUU20-003
CRUU20_003_Current_schema_version_bumps_when_ownership_protocol_changes
CRUU20_003_Parent_v4_interrupted_attempt_is_not_treated_as_v5_identity_protocol
CRUU20_003_Parent_v4_attempt_created_dirs_have_explicit_legacy_recovery_outcome
CRUU20_003_Parent_v4_probe_residue_has_explicit_legacy_recovery_outcome
CRUU20_003_Legacy_v4_clean_attempt_can_retire_when_no_destructive_inference_is_needed
CRUU20_003_New_protocol_marker_roundtrips_as_v5
CRUU20_003_v5_requires_displaced_probe_control
CRUU20_003_v5_requires_consistent_alternate_probe_content_authority
CRUU20_003_Old_v4_reader_rejects_v5_by_schema_not_same_version_unknown_member

# CRUU20-004
CRUU20_004_Attributed_test_without_runtime_hit_fails_automatically
CRUU20_004_Attributed_expected_exception_test_without_hit_fails_automatically
CRUU20_004_Attributed_test_with_required_hit_passes_automatically
CRUU20_004_Multi_symbol_attribute_requires_every_symbol_hit
CRUU20_004_Evidence_enforcement_does_not_depend_on_AssertProductionHit_helper
CRUU20_004_All_ProductionSymbolEvidence_tests_use_automatic_runtime_harness
CRUU20_004_Nested_evidence_capture_restores_previous_sink
```

**Total proposed CRUU20 sentinels: 33**

---

# 9. Acceptance invariants

```text
PROBE-CRASH-01
Exact owned partial probes are recoverable.

PROBE-CRASH-02
No probe rename can create an undeclared durable recovery location.

PROBE-CRASH-03
A hard crash after either probe rename is automatically resolvable.

PROBE-CRASH-04
Foreign same-path objects remain preserved regardless of content equality.

CLAIM-BOOT-01
No owned file can survive process death before its first durable claim.

CLAIM-BOOT-02
No attempt-created directory can survive process death in a state retry cannot explain.

CLAIM-BOOT-03
A torn first journal append cannot strand a live unproven application artifact.

CLAIM-BOOT-04
Hard-crash acceptance uses killed subprocesses, not normal exception unwinding.

PROTO-01
Material recovery-protocol changes increment the persisted schema.

PROTO-02
v5 is strictly validated as v5.

PROTO-03
v3/v4 never inherit v5 identity assumptions.

PROTO-04
Old readers reject v5 by schema version.

EVIDENCE-01
Every ProductionSymbolEvidence attribute is automatically enforced at runtime.

EVIDENCE-02
An attribute alone cannot satisfy evidence.

EVIDENCE-03
Expected exceptions cannot bypass hit verification.

EVIDENCE-04
Multi-symbol evidence requires every declared symbol.

REL-01
Full suite passes once.

REL-02
Full suite passes five consecutive times.

REL-03
All exact sentinels pass from retained evidence.

REL-04
Hard-crash matrix passes.

REL-05
Strict release workflow is attached to exact final SHA.

REL-06
Self-contained win-x64 artifact passes strict release verification.

REL-07
Fresh independent audit reports no remaining findings.
```

---

# 10. Priority

```text
P0:
    CRUU20-001  capability-probe hard-crash protocol
    CRUU20-002  systemic first-claim crash atomicity

P1:
    CRUU20-003  schema v5 + legacy migration recovery
    CRUU20-004  automatic runtime-evidence enforcement
```

CRUU20-001 and CRUU20-002 should be designed together. A perfect probe phase machine still fails if the object can survive before its first claim; a perfect first-claim primitive still does not fix rename-before-record cuts.

---

# 11. Final assessment

The current commit is materially stronger than the CRUU19 snapshot.

The dangerous live CRUU19 probe bug appears closed in source. Directory replacement is identity-bound. The manifest stage exact-cleans first-record exceptions. Expected-exception runtime-hit checking is fixed.

The remaining defects are deeper:

```text
volatile authority -> durable authority
process death -> restart
old persisted protocol -> new persisted protocol
test metadata -> automatic runtime proof
```

The next repair should therefore build a **hard-crash-capable durable ownership state machine**, not another set of exception cleanup branches.

---

# 12. Final status

```text
AUDITED_HEAD                         = 4456af855d98d62308d1a7eea606e504be18043d

CRUU20_FINDINGS                      = 4
HIGH                                 = 2
MED_HIGH                             = 2

CRUU19_LIVE_OWNERSHIP_FIXES_REAL     = YES
CRUU19_STRICTLY_CLOSED               = NO

SOURCE_AUDIT_CLEAN                   = NO

WINDOWS_RUNTIME_DIRECTLY_EXECUTED    = NO
EXACT_HEAD_CI_STATUS                 = NOT INDEPENDENTLY BOUND

STRICT_RELEASE_READY                 = NO
ZERO_DEFECT_VERIFIED                 = NO
```
