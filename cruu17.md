# CRUU17 — Independent Post-CRUU16 Adversarial Re-Audit and Fix Plan

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited HEAD:** `537f97cb7c4e5b5ccbed729c75f2fd6ac5cc9225`  
**Main CRUU16 implementation commit:** `301c6ab5c1fc396b173f63f25723ab721e7ca842`  
**Latest follow-up:** `537f97cb7c4e5b5ccbed729c75f2fd6ac5cc9225`  
**Previous audit:** `cruu16.md`  
**Audit date:** 2026-08-23  
**Mode:** independent source, crash-cut, commit-boundary, ownership-authority, rollback, evidence-quality, CI/release-path audit.

> Report only. No product source was modified by this audit.

---

# 1. Executive verdict

CRUU16 produced another substantial architectural improvement.

The following prior defects are genuinely improved or closed at source level:

- CAS recovery no longer equates target pathname occupancy with commit.
- CAS records candidate hash/length and durable phases.
- the ownership journal now uses strict UTF-8, checksummed records, reparse-safe handle opens, and typed reconciliation outcomes;
- settings and data-root startup now stop on fatal unresolved ownership recovery;
- migration finals now gain an NTFS identity record;
- migration retry final deletion is identity-bound;
- raw `DeleteFile` / `DeleteDirectory` escape hatches were removed from `IMigrationFileOps`;
- attempt-created directory rollback uses an exact retained directory handle;
- production migration stage creation is root-bound;
- the release workflow now has the symlink capability its mandatory reparse tests need and can be run manually through `workflow_dispatch`.

The latest implementation commit reports **678/678** tests and **45 CRUU16 sentinels**.

However, the current tree is **not zero-defect**.

This audit found **8 new or reopened findings**:

```text
CRITICAL  = 1
HIGH      = 5
MED-HIGH  = 2
TOTAL     = 8
```

The most serious issue is a new commit-boundary defect in the CAS:

> The candidate file is actually published before the `CandidatePublished` ownership record is appended. If that append fails, the CAS throws an ordinary exception even though the write already committed.

For ordinary library mutations, higher-level recovery can sometimes infer the committed metadata from disk. For a **data-folder settings transition**, this distinction is critical: the caller sets `settingsCommitted = true` only after `SaveIfUnchanged` returns. Therefore an ownership-journal write failure after the settings candidate is already live can make the coordinator roll back the target migration even though `settings.json` already points at that target.

That is a point-of-no-return inversion.

The remaining findings are concentrated in the same architectural theme:

> durable state transitions still have a few places where object identity or commit state exists in the filesystem before the journal state describing it is durable.

There are also two concrete verification failures: a same-byte ledger replacement is not tested, and the evidence-quality gate labels reflection-only tests as `ProductionBehavior` merely because their source contains a production type name.

---

# 2. Audit status

```text
AUDITED_HEAD                         = 537f97cb7c4e5b5ccbed729c75f2fd6ac5cc9225
HEAD_RECONFIRMED_AT_REPORT_FREEZE    = YES

IMPLEMENTER_REPORTED_FULL_SUITE      = 678/678
IMPLEMENTER_REPORTED_CRUU16_TESTS    = 45 sentinels

INDEPENDENT_WINDOWS_EXECUTION        = NOT AVAILABLE
LOCAL_DOTNET                         = NOT AVAILABLE
LOCAL_PWSH                           = NOT AVAILABLE
LOCAL_WINDOWS_POWERSHELL             = NOT AVAILABLE

GITHUB_COMBINED_STATUS_FOR_HEAD      = NO STATUSES EXPOSED
GITHUB_PR_WORKFLOW_LOOKUP_FOR_HEAD   = NO RUNS EXPOSED
RELEASE_WORKFLOW_SOURCE              = PRESENT
RELEASE_WORKFLOW_MANUAL_TRIGGER      = PRESENT
RELEASE_SYMLINK_SETUP                = PRESENT

SOURCE_AUDIT_CLEAN                   = NO
CRUU16_STRICTLY_CLOSED               = NO
STRICT_RELEASE_READY                 = NO
ZERO_DEFECT_VERIFIED                 = NO
```

The absence of attached status data in the available GitHub status interfaces is **not** treated as proof that CI did not run. It means only that this audit cannot independently bind the reported test results to the exact HEAD through those interfaces.

---

# 3. CRUU16 closure matrix

| CRUU16 | CRUU17 assessment |
|---|---|
| CRUU16-001 | **Core foreign-target data-loss bug fixed, but CAS phase/commit boundaries remain incomplete. Reopened by CRUU17-001 and CRUU17-002.** |
| CRUU16-002 | **Core strict parser fixed.** Complete terminated malformed records now fail closed and UTF-8 is strict. |
| CRUU16-003 | **Partial / reopened.** Read/append/empty-retire are identity-aware, but non-empty rewrite is hash-bound rather than identity-bound, and self-compaction has an unjournaled crash gap. See CRUU17-003/004. |
| CRUU16-004 | **Core fixed.** Settings and data-root startup consume typed fatal reconciliation outcomes. |
| CRUU16-005 | **Partial / reopened.** Published finals gain identity records, but publication precedes the record and `TempOwned` rollback remains hash-only. See CRUU17-005/006. |
| CRUU16-006 | **Mostly fixed, but rollback result propagation remains incomplete. See CRUU17-007.** |
| CRUU16-007 | **Core production root-binding repair present.** |
| CRUU16-008 | **Partial / reopened.** The evidence-quality gate has a lexical false-positive that certifies reflection-only tests as behavioral. See CRUU17-008. |

---

# 4. Findings

---

## CRUU17-001 — CRITICAL
## A post-publish ownership-record failure is reported as a failed CAS even though the target already committed

### Affected code

- `src/PromptHelper/Services/WindowsAtomicExpectedFileReplacer.cs`
- `src/PromptHelper/Services/AppSettingsRepository.cs`
- `src/PromptHelper/Services/DataFolderTransitionCoordinator.cs`
- `src/PromptHelper/Services/LibraryRepository.cs`
- `src/PromptHelper/Services/PromptMutationCoordinator.cs`

### Current ordering

For `ExpectedPresent`, production currently performs:

```text
1. Hold exact old target authority.
2. Verify old expected hash.
3. Create + flush candidate stage.
4. Record CasPreimage / PreimageSidelined.
5. Rename old target to pre-image.
6. Promote candidate into target pathname.
7. Record CasPreimage / CandidatePublished.
8. Delete pre-image.
9. Return success.
```

The critical boundary is between steps 6 and 7.

After step 6:

```text
THE NEW TARGET CONTENT IS ALREADY LIVE.
```

But if step 7 throws because `.prompthelper-owned.log` cannot append/flush, `ReplaceIfExpected` propagates that exception as an ordinary failure.

There is no `CommittedAtomicReplacementRequiresRecoveryException`, no committed return state, and no caller-visible `Published = true` authority.

### Why this is critical for settings transitions

`AppSettingsRepository.SaveCoreWithCas` calls the CAS and returns only if it completes.

`DataFolderTransitionCoordinator` does:

```text
saveResult = _settingsRepo.SaveIfUnchanged(...)
settingsCommitted = true
tx.Commit()
```

`settingsCommitted` becomes true **after** `SaveIfUnchanged` returns.

Its catch path does:

```text
if (!settingsCommitted)
    rollback target migration
```

Therefore this exact sequence exists:

```text
T1  target migration files are fully prepared
T2  settings CAS sidelines old settings.json
T3  new settings.json is promoted and now points to the new target
T4  append/flush of CandidatePublished ownership record fails
T5  SaveIfUnchanged throws
T6  coordinator observes settingsCommitted == false
T7  coordinator rolls back migrated target payload
T8  settings.json nevertheless already points to that target
```

The coordinator has crossed its documented point of no return but executes the pre-commit rollback path.

That can leave:

```text
settings.json -> new target
new target    -> rolled back / partially cleaned / rejected
UI result     -> transition failed
```

The source library remains, so this is not proven destruction of the original user library, but it is a **critical durable-state invariant violation** at the application’s data-root selector.

### The same primitive can poison later recovery elsewhere

For ordinary library/backup CAS:

- candidate may be published;
- `CandidatePublished` journal append fails;
- call reports failure;
- pre-image and `PreimageSidelined` record survive.

A higher layer may infer the write committed, but unless the app immediately reconciles the ownership ledger and prevents further edits, a later legitimate target update can make the stale pre-image transaction appear ambiguous at next startup.

### Required fix

The CAS API must distinguish:

```text
NOT_COMMITTED_FAILURE
COMMITTED_SUCCESS
COMMITTED_REQUIRES_RECOVERY
```

A post-publish bookkeeping failure must never be surfaced as an ordinary failed write.

Recommended shape:

```text
AtomicReplaceResult
{
    Outcome:
        NotCommitted
        Committed
        CommittedRecoveryRequired

    OperationId
    TargetPath
    CandidateHash
    RecoveryWarning / Error
}
```

Or throw a dedicated:

```text
CommittedAtomicReplacementRequiresRestartException
```

only after the candidate is known published.

Caller policy:

```text
Data-folder settings write:
    Committed / CommittedRecoveryRequired
        => settingsCommitted = true
        => DO NOT rollback target
        => force restart before further mutations

Library/prompt mutation:
    CommittedRecoveryRequired
        => treat metadata/body as committed
        => preserve journal evidence
        => force restart before another write

Backup sync:
    CommittedRecoveryRequired
        => do not keep running as if nothing committed
        => reconcile immediately or force restart
```

### Required tests

```text
CRUU17_001_CandidatePublished_record_failure_is_never_reported_as_not_committed
CRUU17_001_Settings_primary_published_then_ledger_append_failure_does_not_rollback_target
CRUU17_001_Settings_transition_marks_point_of_no_return_from_actual_publish_not_method_return
CRUU17_001_Postpublish_ledger_failure_forces_restart_before_further_mutation
CRUU17_001_Library_primary_published_then_ledger_failure_is_classified_committed
CRUU17_001_Backup_published_then_ledger_failure_cannot_leave_a_stale_inflight_CAS_silently
```

The first three must inject failure specifically on the `CandidatePublished` journal append after verifying that the candidate target file is already present.

---

## CRUU17-002 — HIGH
## `PreimageSidelined` is durably recorded before the pre-image has actually been sidelined

### Affected code

- `src/PromptHelper/Services/WindowsAtomicExpectedFileReplacer.cs`
- `src/PromptHelper/Services/OwnedArtifactReconciler.cs`
- `tests/PromptHelper.Tests/Cruu16CasRecoveryTests.cs`

### Current ordering

Production writes:

```text
CasPreimage
Phase = PreimageSidelined
PreimagePath = ...
OldIdentity = ...
CandidateHash = ...
```

and flushes that record **before** calling:

```text
authority.RenameExactNoOverwrite(preimagePath)
```

The phase name therefore claims a filesystem transition that has not happened yet.

### Failure without any process crash

If the sideline rename itself fails:

```text
1. PreimageSidelined record is durable.
2. Old target remains exactly where it was.
3. Candidate stage is cleaned.
4. CAS throws.
5. Pre-image pathname never existed.
6. Journal claim remains.
```

At next reconciliation:

```text
record says PreimageSidelined
pre-image is missing
target exists
target does not equal candidate
```

The recovery matrix treats this as an unresolved/unrecoverable transaction rather than recognizing:

```text
"the sideline rename never happened; the old committed object is still intact."
```

A simple sharing/rename failure can therefore poison the next startup.

### Crash cut

The same false phase exists if the process dies after the durable record but before the rename.

The existing CRUU16 crash helper fires **after** `RenameExactNoOverwrite`, so it never exercises this cut.

The “every durable phase” test also constructs `PreimageSidelined` using that same after-rename helper, so it verifies only one physical state for the phase even though production creates a different physical state before the rename.

### Required fix

Introduce an honest pre-rename phase:

```text
Prepared
```

Durably record before the rename:

```text
operation ID
old target identity
old target expected hash
candidate hash/length
planned pre-image path
target path
phase = Prepared
```

Then:

```text
rename old -> pre-image
append phase = PreimageSidelined
promote candidate
append phase = CandidatePublished
```

Recovery for `Prepared` must be able to distinguish:

```text
target still has old recorded identity/hash + pre-image missing
    => sideline never happened
    => transaction is safely abandoned

pre-image has old identity + target missing
    => rename happened before phase update
    => restore old

pre-image has old identity + target candidate
    => candidate published
    => complete

foreign/ambiguous
    => preserve + fail closed
```

### Required tests

```text
CRUU17_002_Sideline_rename_failure_does_not_poison_next_startup
CRUU17_002_Crash_after_Prepared_record_before_sideline_rename_keeps_old_target_healthy
CRUU17_002_Prepared_phase_recognizes_old_target_identity_as_not_started
CRUU17_002_Crash_after_sideline_before_phase_advance_restores_preimage
CRUU17_002_Durable_phase_matrix_tests_every_filesystem_state_each_phase_can_represent
```

---

## CRUU17-003 — HIGH
## Non-empty ownership-journal rewrite is hash-bound, not bound to the exact journal object that was read

### Affected code

- `src/PromptHelper/Services/IOwnedArtifactJournal.cs`
- `src/PromptHelper/Services/WindowsAtomicExpectedFileReplacer.cs`
- `tests/PromptHelper.Tests/Cruu16OwnershipJournalTests.cs`

### What the snapshot contains

`OwnedArtifactJournalSnapshot` captures:

```text
records
ledger NTFS file identity
ledger SHA-256
```

### Empty rewrite is correct

When no records survive, current code:

```text
open current journal authority
compare authority.Identity == snapshot.Identity
compare content hash
delete exact handle
```

That is same-object authority.

### Non-empty rewrite loses the identity

For non-empty survivors, current code calls:

```text
WindowsAtomicExpectedFileReplacer.ReplaceIfExpected(
    ...,
    ExpectedFileState.Present(expected.Sha256Hex),
    ...
)
```

Only the hash enters the CAS.

The snapshot’s file identity is discarded.

### Concrete replacement case

```text
T1 read journal object A, bytes X, identity IA
T2 external actor replaces journal with object B, bytes X, identity IB
T3 Rewrite(non-empty) starts
T4 CAS opens B
T5 B's hash == expected hash X
T6 CAS accepts B as expected target
T7 B is sidelined and ultimately deleted
T8 compacted application journal replaces it
```

A foreign replacement carrying byte-identical content is destroyed.

CRUU16-003 required rewrite to be bound to the object that was read, not merely to equivalent bytes.

### Why the current sentinel passes

`CRUU16_003_Journal_replaced_after_read_is_not_overwritten_by_nonempty_rewrite` replaces the journal with:

```text
"somebody else's file"
```

Different bytes make the hash precondition fail.

It does **not** replace the journal with a different object containing the same original bytes.

Therefore the test proves content CAS, not same-object CAS.

### Required fix

Extend expected file authority to optionally include object identity:

```text
ExpectedFileState.Present(
    expectedSha256Hex,
    expectedIdentityToken)
```

`WindowsExpectedTargetAuthority` must assert **both** from the same retained handle before any swap.

Journal rewrite must pass:

```text
snapshot.Identity
snapshot.Sha256Hex
```

No fallback from identity mismatch to content equivalence.

### Required tests

```text
CRUU17_003_Nonempty_journal_rewrite_rejects_same_bytes_different_file_identity
CRUU17_003_Nonempty_journal_rewrite_never_deletes_same_content_foreign_replacement
CRUU17_003_Journal_rewrite_requires_snapshot_identity_and_hash
CRUU17_003_ExpectedFileState_can_bind_exact_file_identity_when_required
```

---

## CRUU17-004 — HIGH
## Ownership-journal compaction has its own unjournaled two-rename crash window

### Affected code

- `src/PromptHelper/Services/IOwnedArtifactJournal.cs`
- `src/PromptHelper/Services/WindowsAtomicExpectedFileReplacer.cs`
- `src/PromptHelper/Services/OwnedArtifactReconciler.cs`
- `src/PromptHelper/Services/DataRootTempReconciler.cs`

### Current design

For non-empty surviving ownership records, `Rewrite` calls the ordinary two-rename CAS with:

```text
recordOwnership: false
```

The source explicitly explains why:

```text
the ownership ledger cannot appear in itself
```

That avoids infinite recursion, but it also removes the very crash-recovery mechanism that makes the two-rename CAS safe.

### Crash sequence

```text
T1 .prompthelper-owned.log A is valid and contains live ownership records
T2 compaction creates/flushes candidate ledger C
T3 old ledger A is renamed aside to .prompthelper-preimage-...
T4 process/power dies before C is promoted
```

Because `recordOwnership:false`:

```text
no record identifies the pre-image as the previous ledger
no transaction record says the ledger rewrite was in flight
```

At restart:

```text
.prompthelper-owned.log is missing
WindowsOwnedArtifactJournal.Read() returns an empty/missing snapshot
old ledger pre-image is only an unproven orphan
the recovery authority that existed in A is no longer discoverable
```

`DurableTempReconciler` recognizes `.prompthelper-tmp-*`, not `.prompthelper-preimage-*`, so ordinary temp classification does not reconstruct the ledger.

### Why this matters during migration

`CopySnapshotToTarget` records `MigrationFinal` identities and then calls:

```text
RetireOwnedArtifacts(targetRoot)
```

`OwnedArtifactReconciler` preserves `MigrationFinal` records as survivors, so it performs a non-empty journal rewrite while the migration attempt is still active.

A crash in the ledger’s own rewrite window can therefore remove the only durable identity authority needed to delete those finals on retry.

The payload files remain safe, but automatic recovery is no longer possible.

### Required fix

Do **not** compact the authority journal by a protocol that requires that same journal to recover itself.

Recommended architecture: dual-generation ledger.

Example:

```text
.prompthelper-owned.0.log
.prompthelper-owned.1.log
```

Each file contains:

```text
schema
generation
complete ledger payload
whole-file checksum
```

Compaction:

```text
1. Read highest valid generation G.
2. Create inactive slot G+1 with CREATE_NEW / expected-slot identity policy.
3. Write complete compacted ledger.
4. Flush file data.
5. Flush directory/rename metadata as required.
6. Reader can now discover both G and G+1 and chooses highest valid generation.
7. Retire G only after G+1 is independently valid.
```

There is never a moment with no discoverable valid ledger.

An append-only journal with durable tombstones/checkpoints is another acceptable design if compaction similarly creates a new generation before retiring the old one.

### Required tests

Use a child-process kill or an exact injected crash hook.

```text
CRUU17_004_Crash_during_ledger_compaction_never_leaves_zero_discoverable_valid_ledgers
CRUU17_004_Crash_after_old_ledger_sideline_before_new_publish_recovers_old_generation
CRUU17_004_MigrationFinal_authority_survives_ledger_compaction_crash
CRUU17_004_Ledger_compaction_requires_no_recursive_self_journaling
CRUU17_004_Reader_selects_highest_complete_valid_generation_after_interrupted_compaction
```

---

## CRUU17-005 — HIGH
## Migration publishes a final object before durable final-identity ownership exists

### Affected code

- `src/PromptHelper/Services/DataFolderMigrationService.cs`
- `src/PromptHelper/Services/IMigrationFileOps.cs`
- `src/PromptHelper/Services/MigrationRecoveryService.cs`
- `src/PromptHelper/Services/MigrationAttemptManifest.cs`

### Current copy path

`CopyPayloadFileDurablyWithTemp` now correctly creates a root-bound owned stage.

After write + flush it performs:

```text
stage.PromoteNoOverwriteExact(finalPath)
_fileOps.RecordPromotedFinal(targetRoot, finalPath, stage.IdentityToken)
owned.MarkFinalOwnedAfterMove(stage.IdentityToken)
```

The final object is therefore published **before** its `MigrationFinal` record is durable.

The manifest still stores only:

```text
relative path
temp path
hash
length
role
```

It carries no final file ID.

### Crash/failure window

```text
T1 stage has durable Stage ownership record
T2 final promotion succeeds
T3 temp pathname disappears; final pathname contains same NTFS object
T4 crash OR RecordPromotedFinal throws
```

On restart:

```text
Stage record points to now-missing temp pathname
no MigrationFinal record exists
manifest has no final file identity
final exists, but nobody can prove that this attempt created it
```

Retry correctly fails closed and preserves the final, but the application cannot automatically roll back its own interrupted migration.

This is safer than CRUU16, but it is still incomplete crash recovery.

### Required fix

Ownership must describe the **transition before the rename**.

Recommended migration artifact transaction record:

```text
OperationId = migration AttemptId + artifact ID
Kind = MigrationArtifact
Phase = StageOwned
StagePath
FinalPath
FileIdentity
ExpectedHash
ExpectedLength
```

Before promotion, the same record already knows both possible locations of the same file identity.

Then append:

```text
Phase = FinalPublished
```

after rename.

Recovery can inspect both paths by exact file identity:

```text
identity at temp, final absent
    => stage not published

identity at final, temp absent
    => publish occurred, even if phase append never landed

identity nowhere
    => claim dead / inspect manifest state

same pathname but identity mismatch
    => foreign object, preserve + fail closed
```

Use a stable operation ID related to the migration attempt rather than a fresh unrelated GUID for each `RecordPromotedFinal`.

### Required tests

```text
CRUU17_005_Crash_after_migration_final_publish_before_final_record_is_recoverable
CRUU17_005_RecordPromotedFinal_failure_after_publish_preserves_automatic_retry_authority
CRUU17_005_Migration_artifact_record_knows_temp_and_final_path_before_promotion
CRUU17_005_Migration_retry_can_prove_final_from_prepublication_identity_record
CRUU17_005_Manifest_or_ownership_state_carries_final_identity_across_the_publish_cut
```

---

## CRUU17-006 — HIGH
## `TempOwned` in-process rollback still authorizes deletion by hash/length instead of object identity

### Affected code

- `src/PromptHelper/Services/DataFolderMigrationService.cs`
- `src/PromptHelper/Services/IVerifiedArtifactDeleter.cs`
- `tests/PromptHelper.Tests/Cruu16StartupAndProvenanceTests.cs`

### FinalOwned was fixed

For `MigrationOwnedFileState.FinalOwned`, rollback now uses:

```text
FinalIdentityToken
TryVerifyIdentityContentAndDelete(...)
```

That is the correct model.

### TempOwned was not converted

For `MigrationOwnedFileState.TempOwned`, rollback still does:

```text
Probe(tempPath)
VerifyAndDelete(
    targetRoot,
    tempPath,
    expectedLength,
    expectedSha256)
```

There is no stage identity check.

### Concrete destructive sequence

```text
T1 migration creates stage S with identity IS
T2 stage becomes TempOwned
T3 payload bytes X are written
T4 an operation fails
T5 catch attempts stage.DeleteExact()
T6 cleanup itself fails; state remains TempOwned
T7 retained stage handle is eventually disposed
T8 foreign process replaces temp pathname with new object F containing identical X
T9 transaction Rollback runs
T10 hash/length of F match expected X
T11 VerifyAndDelete deletes F
```

This is exactly the content-vs-ownership error CRUU16 was intended to remove from finals.

### Why the current sentinel missed it

`CRUU16_005_Inprocess_rollback_final_delete_requires_recorded_file_identity` does not execute rollback.

It reflects over:

```text
MigrationOwnedFile.FinalIdentityToken
IVerifiedArtifactDeleter.TryVerifyIdentityContentAndDelete
```

and passes if the members exist.

It says nothing about the `TempOwned` branch.

### Required fix

`MigrationOwnedFile` must store:

```text
TempIdentityToken
FinalIdentityToken
```

`TempIdentityToken` is captured immediately from `IOwnedFileStage.IdentityToken`.

Rollback:

```text
TempOwned:
    TryVerifyIdentityContentAndDelete(
        temp path,
        temp identity,
        expected hash/length)

FinalOwned:
    same with final identity
```

Or route both through journal-backed `DeleteOwnedFileIfProven`.

No hash-only destructive fallback may remain.

### Required tests

```text
CRUU17_006_TempOwned_rollback_same_bytes_different_identity_is_preserved
CRUU17_006_TempOwned_rollback_requires_stage_identity
CRUU17_006_Stage_cleanup_failure_then_foreign_same_byte_replacement_is_never_deleted
CRUU17_006_All_MigrationOwnedFile_states_have_identity_bound_destructive_authority
```

The first and third tests must execute `MigrationTargetTransaction.Rollback`, not a helper.

---

## CRUU17-007 — MED-HIGH
## In-process migration rollback discards fatal typed ownership-reconciliation results

### Affected code

- `src/PromptHelper/Services/DataFolderMigrationService.cs`
- `src/PromptHelper/Services/OwnedArtifactReconciler.cs`
- `src/PromptHelper/Services/MigrationTargetInventoryInspector.cs`

### Current rollback

After file cleanup, rollback does:

```text
OwnedArtifactReconciler.Reconcile(
    targetRoot,
    new WindowsOwnedArtifactJournal())
```

but discards the returned `Result`.

It only catches thrown:

```text
IOException
UnauthorizedAccessException
```

The CRUU16 design deliberately changed reconciliation so important safety conditions are normally expressed as:

```text
Result.Outcomes
Result.HasFatal
```

rather than thrown exceptions.

Examples:

```text
OWNERSHIP_JOURNAL_CORRUPT
CAS_AMBIGUOUS
CAS_UNRECOVERABLE
CAS_PREIMAGE_RESTORE_FAILED
authority violation
```

Those can therefore occur and be silently ignored by rollback.

### Why the later inventory check may not save this

`.prompthelper-owned.log` is classified as persistent managed control.

A corrupt or unresolved ledger can remain without appearing as an unknown migration entry.

Thus rollback can potentially report no `MigrationRollbackFailure` from the ownership subsystem even though that subsystem explicitly reported a fatal state.

### Required fix

Capture the result:

```text
OwnedArtifactReconciler.Result reconciliation = ...
```

Then:

```text
for every Warning/Fatal:
    add MigrationRollbackFailure

if HasFatal:
    stop further destructive retirement
    preserve manifest
    return failed rollback
```

`cleanRollback` must explicitly require:

```text
!reconciliation.HasFatal
```

Do not rely on filesystem inventory to reinterpret semantic recovery authority.

### Required tests

```text
CRUU17_007_Rollback_converts_fatal_ownership_reconciliation_to_MigrationRollbackFailure
CRUU17_007_Corrupt_ownership_ledger_prevents_cleanRollback
CRUU17_007_CAS_AMBIGUOUS_during_rollback_preserves_manifest_and_reports_failure
CRUU17_007_PersistentManagedControl_classification_cannot_hide_fatal_ledger_state
```

---

## CRUU17-008 — MED-HIGH / VERIFICATION DEFECT
## Evidence-quality gate treats source mention/reflection as production execution

### Affected code

- `tests/PromptHelper.Tests/Cruu16EvidenceQualityTests.cs`
- `tests/PromptHelper.Tests/Cruu16StartupAndProvenanceTests.cs`
- `tools/FindingCoverageMap.json`

### Current classifier

The test gate labels a sentinel `ProductionBehavior` if its source body contains **any production type name**:

```text
touchesProduction =
    ProductionTypeNames.Any(name => regex body contains name)

if touchesProduction:
    ProductionBehavior
```

This is lexical presence, not execution.

### Concrete false positive already in the suite

`CRUU16_005_Inprocess_rollback_final_delete_requires_recorded_file_identity`:

```text
typeof(DataFolderMigrationService).GetNestedTypes()
GetProperty("FinalIdentityToken")
typeof(IVerifiedArtifactDeleter).GetMethod(...)
```

It never constructs a migration transaction and never calls `Rollback`.

Yet the classifier sees production type names and counts it as `ProductionBehavior`.

That exact sentinel remained green while CRUU17-006’s real `TempOwned` rollback branch still performs hash-only deletion.

This is direct evidence that the evidence-quality gate overstates what it proves.

### Required fix

First, rewrite the weak CRUU16 sentinels to execute the real production paths.

For the gate itself, do not infer runtime behavior from text tokens.

Recommended stronger approach:

```text
FindingCoverageMap:
    finding ID
    required test
    required production method/symbol
```

Run coverage instrumentation for required high-risk tests and verify that at least one mapped production method is actually executed.

Example:

```text
CRUU16-005
  test = CRUU17_006_TempOwned_rollback_same_bytes_different_identity_is_preserved
  production method =
      DataFolderMigrationService.MigrationTargetTransaction.Rollback
```

Code-coverage hits are not sufficient to prove the assertion is meaningful, but they eliminate the current class of reflection/source-token false positive.

At minimum, the current classifier must explicitly reject tests whose only interaction with production is:

```text
typeof(...)
GetMethod/GetProperty/GetField
source file reads
string search
```

### Required tests/gates

```text
CRUU17_008_Reflection_only_test_is_not_classified_ProductionBehavior
CRUU17_008_Type_name_mention_alone_is_not_production_execution
CRUU17_008_CRUU16_005_mapped_test_executes_MigrationTargetTransaction_Rollback
CRUU17_008_High_risk_evidence_gate_requires_runtime_hit_on_mapped_production_path
CRUU17_008_Source_or_reflection_only_sentinel_cannot_satisfy_high_risk_acceptance
```

---

# 5. Important positive findings

These CRUU16 improvements were real and should not be reopened without new evidence.

## 5.1 Foreign target no longer authorizes pre-image deletion

For an actual interrupted state after the old target has been sidelined:

```text
target missing           => restore exact pre-image
target == candidate      => candidate committed; retire pre-image
target == foreign bytes  => preserve both + fatal
```

The CRUU16-001 core data-loss case is closed.

## 5.2 Ownership journal parsing is substantially stronger

Current source uses:

- strict UTF-8;
- schema/version parsing;
- per-record checksums;
- fail-closed complete-record validation.

This audit did not reopen the CRUU16-002 core parser finding.

## 5.3 Settings/data-root startup now respects fatal reconciliation

`AppSettingsRepository` and `App.OnStartup` call the typed fatal gate before consuming normal state.

The prior “fatal recovery flattened to warning” architecture is closed in those paths.

## 5.4 Retry final deletion is identity-bound

`MigrationRecoveryService.RecoverForRetry` uses `DeleteOwnedFinalIfProven`.

A same-byte foreign final is preserved.

CRUU17-005 is specifically the publish-before-record crash gap; CRUU17-006 is specifically the in-process `TempOwned` branch.

## 5.5 Attempt-created directory rollback is handle-bound

`WindowsRetirableDirectory` now owns the destructive operation.

The old enumerate-then-`Directory.Delete(path)` branch is gone from that transaction path.

## 5.6 Release workflow repair is present

Current `release.yml`:

- triggers on `v*` tags;
- also supports `workflow_dispatch`;
- enables symlink creation before the full suite;
- runs strict icon/source verification;
- runs full Release tests;
- verifies exact sentinels;
- publishes self-contained `win-x64`;
- verifies the published executable icon chain.

This source defect found during the CRUU16 implementation follow-up is fixed.

---

# 6. Root architectural diagnosis

CRUU14–CRUU17 show a clear progression:

```text
CRUU14:
    pathname authority was too weak.

CRUU15:
    live object identity became strong.

CRUU16:
    identity began to survive restart through a durable ledger.

CRUU17:
    remaining defects are where the FILESYSTEM transition and the JOURNAL transition
    are not one honest state machine.
```

The problematic boundaries are:

```text
journal says sidelined
    BEFORE object is sidelined

object says published
    BEFORE journal says published

migration final exists
    BEFORE journal can prove final ownership

journal itself is rewritten
    WITHOUT a journal that can recover its own rewrite
```

The next repair should therefore be organized around a single rule:

> **A durable phase must describe a state that has actually happened, and recovery must be able to infer every between-phase crash cut from identities recorded before the cut.**

Do not fix this by adding more `File.Exists` or hash probes.

---

# 7. Ordered CRUU17 implementation plan

---

## PHASE 01 — Make CAS commit outcome explicit
### Fixes CRUU17-001

Introduce a committed-result / committed-exception contract.

Required behavior:

```text
before candidate promotion:
    failures are NotCommitted

after candidate promotion:
    failures are CommittedRecoveryRequired
```

Audit every caller:

```text
AppSettingsRepository
DataFolderTransitionCoordinator
LibraryRepository
PromptMutationCoordinator
backup synchronization
prompt body CAS
```

Exit criterion:

```text
No caller can execute pre-commit rollback after the candidate has actually been published.
```

---

## PHASE 02 — Repair the CAS transaction phase model
### Fixes CRUU17-002

Add `Prepared` before sideline.

Record old target identity + expected old hash + candidate identity/content before rename.

Advance to `PreimageSidelined` only after successful rename.

Add recovery cases for:

```text
Prepared + old target still present
Prepared + old identity at preimage
PreimageSidelined + target missing
PreimageSidelined + candidate target
CandidatePublished
```

Exit criterion:

```text
A failed sideline rename leaves no false durable claim that the sideline happened.
```

---

## PHASE 03 — Bind expected-current journal rewrite to file identity
### Fixes CRUU17-003

Extend expected-state CAS with optional exact identity.

Use it for ownership journal rewrite.

Exit:

```text
same bytes + different NTFS file ID => stale/foreign, never overwritten.
```

---

## PHASE 04 — Replace recursive journal compaction with crash-safe generations
### Fixes CRUU17-004

Implement dual-generation or equivalent discoverable-old-until-new-valid protocol.

Requirements:

```text
at least one valid ledger always discoverable
generation + whole-ledger checksum
strict reparse/root authority for every generation
deterministic highest-valid-generation selection
old generation retired only after new generation is durable
```

Exit:

```text
process death at every compaction instruction boundary leaves at least one readable authority ledger.
```

---

## PHASE 05 — Make migration artifact ownership a pre-promotion transaction
### Fixes CRUU17-005

Before final promotion, durably bind:

```text
migration attempt
temp path
final path
file identity
hash/length
```

Then record final-published phase after rename.

Recovery checks where the exact identity lives.

Exit:

```text
crash immediately after promotion is automatically recoverable even if no post-promotion append happened.
```

---

## PHASE 06 — Identity-bind TempOwned rollback
### Fixes CRUU17-006

Add `TempIdentityToken` to `MigrationOwnedFile`.

Use exact identity+content+handle-bound delete.

Delete the remaining hash-only automatic destruction from rollback.

Exit:

```text
No MigrationOwnedFile state can destroy an object solely because bytes match.
```

---

## PHASE 07 — Consume typed reconciliation results in rollback
### Fixes CRUU17-007

Capture `OwnedArtifactReconciler.Result`.

Convert Warning/Fatal outcomes to rollback failures.

Stop retirement on Fatal.

Include fatal state in `cleanRollback`.

Exit:

```text
Rollback cannot return clean while ownership reconciliation says Fatal.
```

---

## PHASE 08 — Replace lexical evidence classification with actual production-path evidence
### Fixes CRUU17-008

Immediately rewrite reflection-only historical tests.

Then add runtime path evidence for CRITICAL/HIGH findings.

Preferred:

```text
per-finding production symbol map + per-test coverage verification
```

Exit:

```text
A test containing typeof(ProductionType) but never executing production code cannot satisfy ProductionBehavior.
```

---

## PHASE 09 — Add exact crash/failure injection points

Mandatory hooks or child-process kill cuts:

```text
CAS:
A. after Prepared record, before sideline rename
B. after sideline rename, before PreimageSidelined record
C. after PreimageSidelined record
D. after candidate promotion, before CandidatePublished record
E. CandidatePublished record append throws
F. after CandidatePublished record, before pre-image retire

Journal compaction:
G. after new generation write
H. after new generation flush
I. before old generation retire
J. during old generation retire

Migration:
K. after StageOwned record
L. after final rename, before FinalPublished record
M. FinalPublished record throws
N. TempOwned cleanup fails, then pathname is replaced
O. rollback reconciliation returns Fatal
```

Do not simulate only by manually creating final states. At least one test per critical cut must run the real production primitive to the cut.

---

## PHASE 10 — Exact final acceptance

On the exact final commit:

```text
1. Fresh checkout.
2. Confirm no pre-existing node_modules.
3. npm ci for pinned icon renderer.
4. Verify icon reproducibility.
5. dotnet restore Release.
6. dotnet build Release.
7. CRUU17 CAS commit-boundary suite.
8. CRUU17 ledger identity/compaction crash suite.
9. CRUU17 migration provenance/rollback suite.
10. filesystem/reparse integration suite.
11. full suite once.
12. full suite five consecutive times.
13. exact sentinel verification across retained TRX.
14. finding-coverage completeness.
15. evidence-quality/runtime-path gate.
16. self-contained win-x64 publish.
17. strict published-EXE icon chain.
18. run the now-available release workflow with workflow_dispatch.
19. bind the passing CI/release evidence to the exact tested SHA.
20. fresh independent source audit.
```

---

# 8. Mandatory CRUU17 sentinels

```text
CRUU17_001_CandidatePublished_record_failure_is_never_reported_as_not_committed
CRUU17_001_Settings_primary_published_then_ledger_append_failure_does_not_rollback_target
CRUU17_001_Settings_transition_marks_point_of_no_return_from_actual_publish_not_method_return
CRUU17_001_Postpublish_ledger_failure_forces_restart_before_further_mutation
CRUU17_001_Library_primary_published_then_ledger_failure_is_classified_committed
CRUU17_001_Backup_published_then_ledger_failure_cannot_leave_a_stale_inflight_CAS_silently

CRUU17_002_Sideline_rename_failure_does_not_poison_next_startup
CRUU17_002_Crash_after_Prepared_record_before_sideline_rename_keeps_old_target_healthy
CRUU17_002_Prepared_phase_recognizes_old_target_identity_as_not_started
CRUU17_002_Crash_after_sideline_before_phase_advance_restores_preimage
CRUU17_002_Durable_phase_matrix_tests_every_filesystem_state_each_phase_can_represent

CRUU17_003_Nonempty_journal_rewrite_rejects_same_bytes_different_file_identity
CRUU17_003_Nonempty_journal_rewrite_never_deletes_same_content_foreign_replacement
CRUU17_003_Journal_rewrite_requires_snapshot_identity_and_hash
CRUU17_003_ExpectedFileState_can_bind_exact_file_identity_when_required

CRUU17_004_Crash_during_ledger_compaction_never_leaves_zero_discoverable_valid_ledgers
CRUU17_004_Crash_after_old_ledger_sideline_before_new_publish_recovers_old_generation
CRUU17_004_MigrationFinal_authority_survives_ledger_compaction_crash
CRUU17_004_Ledger_compaction_requires_no_recursive_self_journaling
CRUU17_004_Reader_selects_highest_complete_valid_generation_after_interrupted_compaction

CRUU17_005_Crash_after_migration_final_publish_before_final_record_is_recoverable
CRUU17_005_RecordPromotedFinal_failure_after_publish_preserves_automatic_retry_authority
CRUU17_005_Migration_artifact_record_knows_temp_and_final_path_before_promotion
CRUU17_005_Migration_retry_can_prove_final_from_prepublication_identity_record
CRUU17_005_Manifest_or_ownership_state_carries_final_identity_across_the_publish_cut

CRUU17_006_TempOwned_rollback_same_bytes_different_identity_is_preserved
CRUU17_006_TempOwned_rollback_requires_stage_identity
CRUU17_006_Stage_cleanup_failure_then_foreign_same_byte_replacement_is_never_deleted
CRUU17_006_All_MigrationOwnedFile_states_have_identity_bound_destructive_authority

CRUU17_007_Rollback_converts_fatal_ownership_reconciliation_to_MigrationRollbackFailure
CRUU17_007_Corrupt_ownership_ledger_prevents_cleanRollback
CRUU17_007_CAS_AMBIGUOUS_during_rollback_preserves_manifest_and_reports_failure
CRUU17_007_PersistentManagedControl_classification_cannot_hide_fatal_ledger_state

CRUU17_008_Reflection_only_test_is_not_classified_ProductionBehavior
CRUU17_008_Type_name_mention_alone_is_not_production_execution
CRUU17_008_CRUU16_005_mapped_test_executes_MigrationTargetTransaction_Rollback
CRUU17_008_High_risk_evidence_gate_requires_runtime_hit_on_mapped_production_path
CRUU17_008_Source_or_reflection_only_sentinel_cannot_satisfy_high_risk_acceptance
```

Total new mandatory CRUU17 sentinels proposed: **39**.

---

# 9. Acceptance invariants after repair

```text
CAS-COMMIT-01
Any failure before candidate promotion is NotCommitted.

CAS-COMMIT-02
Any failure after candidate promotion is explicitly Committed or CommittedRecoveryRequired.

CAS-COMMIT-03
Data-folder rollback can never run after settings candidate publication.

CAS-PHASE-01
A durable phase name never claims a filesystem transition that has not happened.

CAS-PHASE-02
Every between-phase crash state can be resolved from previously recorded identity.

JOURNAL-ID-01
Journal rewrite requires both expected content and expected file identity.

JOURNAL-ID-02
Same bytes / different object is foreign, not current.

JOURNAL-DUR-01
At least one complete valid authority ledger is discoverable after every compaction crash cut.

MIG-ID-01
Migration ownership knows final destination before final publication.

MIG-ID-02
A post-publish/pre-record crash is recoverable.

MIG-ID-03
TempOwned and FinalOwned deletion both require exact object identity.

ROLLBACK-01
Fatal ownership reconciliation is always a rollback failure.

ROLLBACK-02
Manifest retirement requires no fatal semantic recovery state, not merely clean pathname inventory.

EVIDENCE-01
Reflection/source mention alone never counts as ProductionBehavior.

EVIDENCE-02
Every CRITICAL/HIGH finding has a real execution path through the affected production method.

REL-01
Fresh Release build green.

REL-02
Full Release suite ×5 green.

REL-03
Exact sentinels green.

REL-04
workflow_dispatch Release gate green on exact final SHA.

REL-05
Self-contained publish and executable icon identity green.

REL-06
Final independent source audit = zero findings.
```

---

# 10. Final assessment

The implementation is getting materially stronger.

CRUU16 successfully fixed the previous direct data-loss bug where recovery deleted the old committed pre-image merely because something existed at the target pathname. That deserves to remain closed.

The current defects are subtler, but they sit at equally important durability boundaries.

The most important repair is **not** another hash check.

It is to make the CAS API tell callers the truth about whether the filesystem commit already happened.

Right now:

```text
candidate published
journal bookkeeping fails
method throws "failure"
caller may rollback as pre-commit
```

That violates the transaction model above the CAS.

After CRUU17-001/002 are fixed, the next priority is the ownership ledger itself: its non-empty rewrite must be identity-bound and its compaction protocol must not require the ledger to journal its own disappearance.

Finally, migration provenance must be durable **before** promotion, not appended after the object has already moved.

---

# 11. Final status

```text
AUDITED_HEAD                         = 537f97cb7c4e5b5ccbed729c75f2fd6ac5cc9225

CRUU17_FINDINGS                      = 8
CRITICAL                             = 1
HIGH                                 = 5
MED_HIGH                             = 2

CRUU16_CORE_IMPROVEMENT_REAL         = YES
CRUU16_STRICTLY_CLOSED               = NO

IMPLEMENTER_REPORTED_FULL_SUITE      = 678/678
INDEPENDENT_WINDOWS_EXECUTION        = NO

SOURCE_AUDIT_CLEAN                   = NO
STRICT_RELEASE_READY                 = NO
ZERO_DEFECT_VERIFIED                 = NO
```
