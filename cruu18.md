# CRUU18 — Independent Post-CRUU17 Adversarial Re-Audit and Fix Plan

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited HEAD:** `e95bc2be7bfaf65c4ad076a39f3e42535b29a64f`  
**Parent / CRUU17 audit snapshot:** `537f97cb7c4e5b5ccbed729c75f2fd6ac5cc9225`  
**Current repair commit:** `e95bc2be7bfaf65c4ad076a39f3e42535b29a64f` — `fix(recovery): close CRUU17 durability gaps`  
**Previous audit:** `cruu17.md`  
**Audit date:** 2026-08-23  
**Mode:** independent source, crash-cut, retry-recovery, caller-propagation, migration-rollback, ownership-authority, evidence-quality and release-evidence audit.

> Report only. No production source or GitHub repository content was modified by this audit.

---

# 1. Executive verdict

The CRUU17 repair round is a **real and substantial improvement**.

Several of the previous audit's central architectural defects are now fixed in source:

- the CAS has an honest `Prepared` phase before the sideline rename;
- the CAS distinguishes a post-publication bookkeeping failure with a dedicated `CommittedAtomicReplacementRequiresRestartException`;
- data-folder transitions treat that exception as committed instead of rolling back the target;
- backup synchronization no longer downgrades a committed CAS exception to an ordinary warning;
- non-empty ownership-journal rewrite is now bound to both the exact journal identity and hash;
- the ownership journal no longer rewrites itself through an unjournaled two-rename compaction protocol;
- migration ownership records both the temp and final locations, exact file identity, hash and length **before** final publication;
- `TempOwned` rollback now carries file identity;
- migration rollback now consumes typed reconciliation Warning/Fatal outcomes instead of throwing the `Result` away;
- the evidence classifier explicitly rejects the simplest `typeof(...)` / reflection-only false positive.

Those are meaningful closures.

However, the repository is **still not zero-defect** at the audited HEAD.

This audit found **7 new or reopened findings**:

```text
HIGH      = 3
MED-HIGH  = 4
TOTAL     = 7
```

The strongest new finding is an integration mismatch between the new migration ownership format and the actual retry-recovery deletion API:

> New `MigrationArtifact` records store the staging path in `RelativePath` and the published final path in `RestoreRelativePath`, but `RecoverForRetry` still calls a cleanup helper that recognizes ownership only when the requested path equals `record.RelativePath`.

Therefore a real interrupted migration created by the current code can have a perfectly valid durable identity record for its published final and still be rejected by the actual retry path as **`PreservedUnproven`**.

The CRUU17 sentinel named `Migration_retry_can_prove_final_from_prepublication_identity_record` does not execute `MigrationRecoveryService.RecoverForRetry`; it exercises `OwnedArtifactReconciler` directly. The lower layer and the real retry path therefore disagree while the required test remains green.

Two other high-severity issues remain:

1. A candidate-promotion failure can restore the previous target successfully while leaving the durable CAS phase at `PreimageSidelined`; restart then treats that already-restored state as `CAS_AMBIGUOUS`.
2. Category Create/Rename/Delete still catch the new committed-restart exception through their broad `IOException` handler, report the already-committed operation as failed, and keep the application running with stale in-memory metadata and unresolved recovery state.

The migration lifecycle also contains four medium-high gaps involving marker retirement, post-publish rollback state, ownership-record bootstrap failures, and the still-lexical evidence-quality classifier.

---

# 2. Audit freeze and evidence status

The branch was fetched at the start and again immediately before report generation.

Both reads returned:

```text
main = e95bc2be7bfaf65c4ad076a39f3e42535b29a64f
```

The current commit is directly based on the CRUU17 audited SHA.

```text
AUDITED_HEAD                         = e95bc2be7bfaf65c4ad076a39f3e42535b29a64f
HEAD_STABLE_DURING_AUDIT             = YES

SOURCE_AUDIT_CLEAN                   = NO
CRUU17_STRICTLY_CLOSED               = NO
NEW_OR_REOPENED_FINDINGS             = 7

HIGH                                 = 3
MED_HIGH                             = 4

WINDOWS_TESTS_DIRECTLY_EXECUTED      = NO
LOCAL_DOTNET                         = NOT AVAILABLE
LOCAL_PWSH                           = NOT AVAILABLE
LOCAL_WINDOWS_RUNTIME                = NOT AVAILABLE

GITHUB_COMBINED_STATUS_FOR_HEAD      = NO STATUSES EXPOSED
GITHUB_PR_WORKFLOW_LOOKUP_FOR_HEAD   = NO RUNS EXPOSED
STRICT_RELEASE_WORKFLOW_SOURCE       = PRESENT
EXACT_HEAD_CI_BINDING                = NOT INDEPENDENTLY OBTAINED

STRICT_RELEASE_READY                 = NO
ZERO_DEFECT_VERIFIED                 = NO
```

The GitHub status interfaces available to this audit returned no attached statuses and no pull-request-associated workflow runs for the exact SHA. This is **not** evidence that no push workflow ran; it means only that this audit cannot independently bind a clean Windows execution to this exact commit through the available interfaces.

---

# 3. CRUU17 closure matrix

| CRUU17 finding | CRUU18 status | Assessment |
|---|---|---|
| **CRUU17-001** | **CORE FIXED / caller propagation reopened** | CAS now throws a dedicated committed-restart exception after publication, and settings transition/backup/library backup paths largely propagate it correctly. Category CRUD UI still swallows it through `IOException`. See CRUU18-003. |
| **CRUU17-002** | **PARTIAL / REOPENED** | `Prepared` now honestly precedes the sideline rename. A later promotion failure that successfully restores the old target leaves `PreimageSidelined` as the latest durable phase and recovery does not recognize the restored old identity. See CRUU18-002. |
| **CRUU17-003** | **FIXED_SOURCE_CORE** | Non-empty journal rewrite is now explicitly same-object identity + hash bound. Same-byte/different-file replacement is rejected. |
| **CRUU17-004** | **FIXED_SOURCE_CORE** | Live-claim journal compaction was removed; non-empty journals remain append-only, eliminating the journal-self-rewrite crash window. |
| **CRUU17-005** | **PARTIAL / REOPENED** | Pre-publication migration record now carries both paths and exact identity. `OwnedArtifactReconciler` understands it, but `RecoverForRetry`'s deletion helper does not. The in-process published-record-failure path also corrupts the in-memory state. See CRUU18-001 and CRUU18-005. |
| **CRUU17-006** | **CORE FIXED / post-publish edge remains** | `TempOwned` has an identity token and normal rollback is identity-bound. A post-promotion exception can still overwrite `FinalOwned` with `TempAbandoned`. See CRUU18-005. |
| **CRUU17-007** | **FIXED_SOURCE_CORE** | Rollback now converts ownership reconciliation Warning/Fatal outcomes to rollback failures and stops directory retirement on Fatal. |
| **CRUU17-008** | **PARTIAL / REOPENED** | Simple reflection-only false positives are rejected, but the gate is still source-regex inference rather than runtime production-path evidence and remains bypassable. Several CRUU17 named sentinels do not execute their named integration path. See CRUU18-007. |

---

# 4. Findings

---

## CRUU18-001 — HIGH
## `RecoverForRetry` cannot consume the new `MigrationArtifact` final-path authority

### Affected code

- `src/PromptHelper/Services/IMigrationFileOps.cs`
- `src/PromptHelper/Services/ProvenanceBoundCleanup.cs`
- `src/PromptHelper/Services/MigrationRecoveryService.cs`
- `src/PromptHelper/Services/OwnedArtifactReconciler.cs`
- `tests/PromptHelper.Tests/Cruu17RegressionTests.cs`
- `tests/PromptHelper.Tests/OwnedArtifactTestSupport.cs`

### The new durable migration record

The CRUU17 repair correctly creates one durable operation describing both sides of a payload rename.

For a migration payload it records:

```text
Kind                = MigrationArtifact
OperationId         = stable for this artifact transaction
RelativePath        = TEMP pathname
RestoreRelativePath = FINAL pathname
Identity            = exact NTFS file identity
CandidateSha256     = expected payload hash
CandidateLength     = expected payload length
```

This record exists **before** final publication.

After the exact object is renamed to the final path, the same operation may advance to `CandidatePublished`.

This is a strong design.

### `OwnedArtifactReconciler` understands the format

`ResolveMigrationArtifact` explicitly checks both:

```text
temp path  + recorded identity
final path + recorded identity + recorded content
```

Therefore it can prove the final even if the post-publication phase append never landed.

### The actual retry recovery uses a different authority reader

`MigrationRecoveryService.RecoverForRetry` removes each published final through:

```text
_fileOps.DeleteOwnedFinalIfProven(
    targetRoot,
    finalFullPath)
```

`DefaultMigrationFileOps.DeleteOwnedFinalIfProven` forwards to:

```text
ProvenanceBoundCleanup.DeleteFileIfProven(...)
```

That helper proves ownership only when:

```text
record.RelativePath == requested path
&&
record.Identity == actual file identity
```

It never checks `record.RestoreRelativePath`.

For a current `MigrationArtifact` record:

```text
record.RelativePath        = payload temp
requested path             = payload final
record.RestoreRelativePath = payload final
```

So the final is **not proven** by the helper the real retry path uses.

### Failure sequence

```text
T1  migration marker exists
T2  payload stage is created and owned
T3  MigrationArtifact record is durably written:
        temp path + final path + identity + content
T4  exact stage is promoted to final
T5  process fails before settings commit
T6  application restarts / user retries
T7  RecoverForRetry reads valid manifest
T8  temp path is absent
T9  final path contains the exact attempt-created object
T10 DeleteOwnedFinalIfProven(final) scans journal
T11 it compares only record.RelativePath (temp) with final
T12 no proof is found
T13 returns PreservedUnproven
T14 RecoverForRetry aborts and preserves the final
```

This fails safe with respect to data destruction, but **automatic retry recovery is broken for the current migration ownership format**.

### A second weakness in the same cleanup API

`ProvenanceBoundCleanup.DeleteFileIfProven` checks identity but does not verify the content authority carried by `MigrationArtifact`.

For old `MigrationFinal` records whose `RelativePath` is the final path, a same-object, in-place content modification can therefore still be deleted on the strength of identity alone.

That is weaker than the current `OwnedArtifactReconciler`, which explicitly treats:

```text
same identity + wrong length/hash
```

as `MIGRATION_FINAL_CONTENT_MISMATCH` and preserves the object.

The retry deletion path should have the same policy.

### Why the CRUU17 sentinel did not detect this

The test named:

```text
CRUU17_005_Migration_retry_can_prove_final_from_prepublication_identity_record
```

calls the helper:

```text
AssertPrepublicationMigrationClaimRecoversFinal(...)
```

That helper invokes:

```text
OwnedArtifactReconciler.Reconcile(...)
```

It does **not** invoke:

```text
MigrationRecoveryService.RecoverForRetry(...)
```

So the test proves the lower-level reconciler can understand the new record, not that the actual retry recovery can.

Older tests still seed ownership with:

```text
OwnedArtifactTestSupport.ClaimPromotedFinal(...)
```

which creates the legacy `MigrationFinal` shape with:

```text
RelativePath = final path
```

That shape happens to satisfy the old cleanup helper and therefore masks the incompatibility.

### Required fix

Do not maintain two different interpretations of migration ownership.

Preferred design:

```text
MigrationArtifactAuthority
    TryOpenStage(...)
    TryOpenFinal(...)
    VerifyIdentity(...)
    VerifyExpectedContent(...)
    DeleteExactFinalForRollback(...)
```

Both:

```text
OwnedArtifactReconciler
RecoverForRetry
MigrationTargetTransaction.Rollback
```

should consume that common authority.

At minimum, extend the deletion API to know whether the requested path is:

```text
record.RelativePath
or
record.RestoreRelativePath
```

and for final deletion require:

```text
exact file identity
AND expected length
AND expected SHA-256
AND exact physical-root containment
AND same retained handle for verification + deletion
```

For legacy `MigrationFinal` records, take expected length/hash from the manifest when the old journal record itself lacks content authority.

### Mandatory tests

```text
CRUU18_001_Real_RecoverForRetry_deletes_current_MigrationArtifact_final
CRUU18_001_RecoverForRetry_accepts_final_from_RestoreRelativePath_authority
CRUU18_001_RecoverForRetry_preserves_same_bytes_different_identity_final
CRUU18_001_RecoverForRetry_preserves_same_identity_tampered_final
CRUU18_001_Legacy_MigrationFinal_retry_uses_manifest_content_authority
CRUU18_001_CRUU17_005_retry_sentinel_executes_MigrationRecoveryService_RecoverForRetry
```

The first and last tests must execute the real `RecoverForRetry`, not `OwnedArtifactReconciler` directly.

---

## CRUU18-002 — HIGH
## Successful rollback after candidate-promotion failure leaves a durable `PreimageSidelined` state that restart misclassifies as fatal

### Affected code

- `src/PromptHelper/Services/WindowsAtomicExpectedFileReplacer.cs`
- `src/PromptHelper/Services/OwnedArtifactReconciler.cs`
- CAS fault-injection tests

### Current sequence

For an expected-present replacement:

```text
1. record Prepared
2. rename old target -> pre-image
3. record PreimageSidelined
4. promote candidate
5. record CandidatePublished
6. retire pre-image
```

This phase ordering is much better than CRUU17.

### The promotion-failure rollback path

If candidate promotion throws **after** `PreimageSidelined` is durable, production does:

```text
DeleteStageQuietly(stage)

if (authority.RenameExactNoOverwrite(fullTarget, out _))
{
    throw original promotion failure
}
```

When the target pathname is still vacant, that restore succeeds.

The exact previous committed object is moved back to the target.

But no durable record is written saying:

```text
the transaction rolled back
```

and the latest durable phase remains:

```text
PreimageSidelined
```

### Restart interpretation

Recovery has a special safe case:

```text
if phase == Prepared
AND target.MatchesRecordedOldIdentity
AND preimage is not ours
    => transaction never started / was restored
```

That rule is restricted to `Prepared`.

For a successfully restored `PreimageSidelined` transaction, restart sees:

```text
phase                  = PreimageSidelined
pre-image              = missing
target                 = exact old object
target.MatchesCandidate = false
```

It enters the generic no-preimage branch and returns:

```text
CAS_AMBIGUOUS
```

even though the operation already rolled back safely.

### Impact

A transient, non-commit promotion error can become a persistent startup-blocking recovery state.

The committed data is safe, but the application can refuse to start until manual cleanup because the durable phase no longer describes the terminal rollback that actually happened.

### Required fix

Two valid approaches:

#### Option A — explicit terminal rollback phase

Add:

```text
RolledBack
```

After successful restoration, durably append it.

Recovery:

```text
RolledBack + target exact old identity
    => discard transaction
```

If writing `RolledBack` fails, recovery must still infer the state from exact old identity.

#### Option B — identity-derived terminal rule

Recognize this as safe for both `Prepared` and `PreimageSidelined`:

```text
preimage missing
AND target exact identity == recorded old target identity
    => the old object is back at the authoritative target
    => transaction did not commit
    => drop the CAS record
```

This is safe because file identity, not hash equality, proves that the exact old object returned.

Option B is smaller and avoids needing another phase purely to describe a state already provable from the retained identity.

### Mandatory tests

```text
CRUU18_002_Promotion_failure_after_PreimageSidelined_successful_restore_is_not_fatal_on_restart
CRUU18_002_PreimageSidelined_plus_exact_old_identity_at_target_is_recognized_as_rolled_back
CRUU18_002_Restored_old_target_is_never_reported_CAS_AMBIGUOUS
CRUU18_002_Promotion_failure_restore_failure_still_preserves_preimage_and_fails_closed
CRUU18_002_CAS_matrix_includes_successful_runtime_rollback_after_sidelined_phase
```

At least one test must drive the **real promotion-failure catch path**, not merely hand-create the final disk state.

---

## CRUU18-003 — HIGH
## Category Create/Rename/Delete swallow committed-restart CAS exceptions as ordinary `IOException`

### Affected code

- `src/PromptHelper/MainWindow.xaml.cs`
- `src/PromptHelper/Services/PromptLibraryService.cs`
- `src/PromptHelper/Services/LibraryRepository.cs`
- `src/PromptHelper/Services/CommittedMutationRequiresRestartException.cs`

### New exception inheritance

The CRUU17 repair correctly introduces:

```text
CommittedAtomicReplacementRequiresRestartException
    : CommittedMutationRequiresRestartException
    : IOException
```

The contract is explicit:

```text
the candidate is already published;
the application must not make another mutation before restart reconciliation.
```

### Prompt handlers are correct

Prompt create/edit/delete/move/duplicate handlers catch:

```text
CommittedMutationRequiresRestartException
```

**before** their broad persistence catch and call the fatal shutdown path.

### Category handlers are not

The category handlers:

```text
AddCategoryButton_Click
RenameCategory(...)
DeleteCategory(...)
```

have only the broad handler:

```text
catch (Exception ex) when (
    ex is IOException
    or UnauthorizedAccessException
    or InvalidOperationException
    or SecurityException)
```

Because the committed-restart exception inherits `IOException`, these handlers swallow it.

### Service state makes this worse

Category service methods do:

```text
candidate = clone(_document)
commitResult = CommitCandidateIfUnchanged(candidate)
_document = candidate
```

The in-memory document is updated **only after the commit call returns**.

Therefore:

```text
T1 category candidate is built
T2 primary library CAS publishes candidate
T3 CandidatePublished journal bookkeeping or pre-image retirement fails
T4 CAS throws CommittedAtomicReplacementRequiresRestartException
T5 disk already contains new category metadata
T6 PromptLibraryService never executes _document = candidate
T7 MainWindow catches the exception as generic IOException
T8 UI says "Failed to create/rename/delete category"
T9 application keeps running
T10 in-memory metadata is old, disk metadata is new
T11 ownership recovery state remains unresolved until restart
```

This violates the new CAS contract.

A later mutation is likely to fail the stale-primary check rather than overwrite the new content, so direct subsequent data loss is not established. But the app is explicitly continuing in a state the new committed exception was designed to forbid.

### Required fix

Every user-visible library mutation must share one committed-mutation boundary.

Preferred:

```text
ExecuteLibraryMutation(Action operation, string ordinaryFailureTitle)
{
    if (_fatalMutationShutdownRequested)
        return;

    try
    {
        operation();
    }
    catch (CommittedMutationRequiresRestartException ex)
    {
        HandleFatalMutationException(ex);
    }
    catch (ordinary persistence exceptions)
    {
        show ordinary error
    }
}
```

Use it for:

```text
CreateCategory
RenameCategory
DeleteCategory
CreatePrompt
EditPrompt
DeletePrompt
MovePrompt
DuplicatePrompt
```

Also:

- guard category handlers with `_fatalMutationShutdownRequested`;
- change the fatal message from `"A prompt change was saved"` to `"A library change was saved"` or equivalent;
- add a structural/behavioral test that every persistence mutation surface routes through the committed-restart boundary.

### Mandatory tests

```text
CRUU18_003_CreateCategory_postpublish_bookkeeping_failure_requests_shutdown
CRUU18_003_RenameCategory_postpublish_bookkeeping_failure_requests_shutdown
CRUU18_003_DeleteCategory_postpublish_bookkeeping_failure_requests_shutdown
CRUU18_003_Category_committed_exception_is_not_caught_by_generic_IOException_path
CRUU18_003_Category_committed_exception_does_not_leave_UI_running_with_stale_document
CRUU18_003_All_library_mutation_UI_paths_share_committed_restart_boundary
```

The first three must make the library primary actually contain the candidate before injecting the post-publish failure.

---

## CRUU18-004 — MED-HIGH
## Post-commit migration ownership-retirement failure does not preserve the Ready marker that is supposed to authorize startup retry

### Affected code

- `src/PromptHelper/Services/DataFolderTransitionCoordinator.cs`
- `src/PromptHelper/Services/MigrationRecoveryService.cs`
- `src/PromptHelper/App.xaml.cs`
- `src/PromptHelper/Services/OwnedArtifactReconciler.cs`

### Intended post-commit lifecycle

After settings commit, the payload is live user data.

The transition then calls:

```text
RetireCommittedMigrationArtifacts(targetRoot)
```

to remove **deletion authority** for the now-committed migration finals without deleting those files.

The code comment correctly says:

```text
"This is post-commit cleanup. Startup can retry it from the Ready marker."
```

### Actual ordering

If ownership retirement throws:

```text
ownershipCleanupWarning = ...
```

the code still proceeds to:

```text
_manifestRepo.DeleteStrict(markerPath, ...)
```

unconditionally.

Therefore:

```text
ownership retirement failed
+
marker deletion succeeded
=
the retry authority was removed
```

### Why startup no longer retries the intended operation

Startup first calls:

```text
MigrationRecoveryService.FinalizeCommittedStartup(...)
```

This can perform committed migration retirement only when a migration marker exists.

If the marker has already been deleted, it returns success with no migration finalization work.

Later `DataRootTempReconciler` invokes ordinary ownership reconciliation with:

```text
retireCommittedMigrationArtifacts = false
```

That mode intentionally keeps valid `MigrationArtifact` final claims alive.

So a transient postcommit ownership-retirement failure can result in:

```text
settings points at target
payload is valid
Ready marker is gone
ownership ledger still carries rollback/deletion authority
ordinary startup preserves those claims indefinitely
```

The comment says startup will retry, but the required marker has been removed.

### Required fix

Marker retirement must be conditional:

```text
ownershipRetired = false
try
{
    RetireCommittedMigrationArtifacts(...)
    ownershipRetired = true
}
catch
{
    warning = ...
}

if (ownershipRetired)
{
    DeleteStrict(marker)
}
else
{
    KEEP Ready marker
}
```

The transition is still reported:

```text
Changed = true
RestartRequired = true
```

because settings commit already happened.

At restart:

```text
Ready marker present
=> FinalizeCommittedStartup
=> verify finals
=> retry RetireCommittedMigrationArtifacts
=> only then delete marker
```

### Mandatory tests

```text
CRUU18_004_Postcommit_ownership_retirement_failure_keeps_Ready_marker
CRUU18_004_Ready_marker_is_deleted_only_after_committed_ownership_claims_retire
CRUU18_004_Restart_retries_failed_committed_ownership_retirement
CRUU18_004_Restart_retry_preserves_payload_then_retires_ledger_then_marker
CRUU18_004_No_success_path_can_delete_marker_while_migration_rollback_claims_survive
```

---

## CRUU18-005 — MED-HIGH
## Migration post-publication journal failure changes `FinalOwned` to `TempAbandoned` after a no-op stage delete

### Affected code

- `src/PromptHelper/Services/DataFolderMigrationService.cs`
- `src/PromptHelper/Services/WindowsOwnedDurableStage.cs`
- `src/PromptHelper/Services/IMigrationFileOps.cs`

### Current copy sequence

The repair now correctly does:

```text
Create stage
MarkTempOwned(identity)
write + flush
RecordMigrationArtifactPrepared(...)
PromoteNoOverwriteExact(final)
MarkFinalOwnedAfterMove(identity)
RecordMigrationArtifactPublished(...)
```

So immediately after promotion, the in-memory transaction correctly knows:

```text
State = FinalOwned
```

### The catch block corrupts that state

Any exception from `RecordMigrationArtifactPublished` enters:

```text
catch
{
    stage.DeleteExact();
    owned.MarkTempAbandoned();
    throw;
}
```

But `WindowsOwnedDurableStage.Promote...` sets:

```text
_terminal = true
```

and `DeleteExact()` begins:

```text
if (_terminal)
    return;
```

So after successful promotion:

```text
stage.DeleteExact()
```

does **nothing**.

Then `MarkTempAbandoned()` unconditionally changes the in-memory state, even if it was already `FinalOwned`.

It has no state-transition guard.

### Result

```text
T1 exact stage is promoted to final
T2 MigrationOwnedFile becomes FinalOwned
T3 post-publish journal append fails
T4 catch calls DeleteExact -> no-op because stage is terminal
T5 catch changes FinalOwned -> TempAbandoned
T6 outer transaction Rollback executes
T7 TempAbandoned is skipped by payload deletion loop
T8 published final remains
```

The ownership journal can still prove the final, so data remains safe.

But in-process rollback has thrown away the exact state it needed to remove its own attempt-created final.

With CRUU18-001 still present, the later retry path cannot consume that new final authority either, making this failure combination especially sticky.

### Required fix

Separate pre-publication and post-publication failure handling.

Example:

```text
stage.Write(...)
stage.Flush(...)
claim = RecordPrepared(...)

try
{
    stage.Promote(...)
}
catch
{
    delete exact stage
    MarkTempAbandoned()
    throw
}

owned.MarkFinalOwnedAfterMove(identity)

try
{
    RecordPublished(...)
}
catch
{
    // DO NOT MarkTempAbandoned
    // FinalOwned remains authoritative
    throw
}
```

Then the outer `MigrationTargetTransaction.Rollback()` can remove the final via exact identity + expected content.

Also harden the state machine:

```text
MarkTempAbandoned()
    allowed only from TempPlanned / TempOwned
    MUST throw from FinalOwned
```

Consider making `WindowsOwnedDurableStage.DeleteExact()` throw or expose a terminal-state result when called after promotion, rather than silently pretending the caller deleted something.

### Mandatory tests

```text
CRUU18_005_Real_copy_postpublish_record_failure_keeps_MigrationOwnedFile_FinalOwned
CRUU18_005_Postpublish_record_failure_inprocess_rollback_deletes_exact_final
CRUU18_005_MarkTempAbandoned_rejects_FinalOwned_transition
CRUU18_005_DeleteExact_after_promotion_cannot_be_mistaken_for_final_cleanup
CRUU18_005_DataFolderTransition_postpublish_migration_record_failure_rolls_back_cleanly
```

The last test must drive the real transition/copy path with an injected failure in `RecordMigrationArtifactPublished`.

---

## CRUU18-006 — MED-HIGH
## Ownership-record bootstrap failure releases the exact staging handle without deleting the unproven stage

### Affected code

- `src/PromptHelper/Services/WindowsAtomicExpectedFileReplacer.cs`
- `src/PromptHelper/Services/IMigrationFileOps.cs`
- `src/PromptHelper/Services/WindowsOwnedDurableStage.cs`
- migration rollback/retry

### CAS path

The CAS creates a new exact stage:

```text
using var stage = CreateNewUnderRoot(...)
```

then immediately calls:

```text
_ownedArtifacts.Record(Stage / Claimed)
```

That record call is outside the stage-cleanup `try`.

If journal recording throws:

```text
stage.Dispose()
```

runs because of `using`.

`Dispose()` only closes the handle.

It does **not** delete an unpromoted stage.

The file is left at a current-format temp name with no durable ownership record.

Startup must preserve it as unproven.

### Migration factory has the same problem

`DefaultMigrationFileOps.CreateOwnedStage` does:

```text
stage = new OwnedMigrationStage(...)
try
{
    RecordStageOwnership(...)
    return stage
}
catch
{
    stage.Dispose()
    throw
}
```

Again, the exact creation handle is still available, but the failure path closes it without `DeleteExact()`.

### Migration impact

This can occur after the migration marker is already durable.

Then:

```text
1. stage is created
2. ownership-record append/flush fails
3. stage handle is closed without deletion
4. transaction never receives a usable stage / TempOwned state
5. rollback cannot claim the leftover
6. inventory sees declared temp residue
7. marker must remain
8. RecoverForRetry sees the temp
9. DeleteOwnedFileIfProven has no durable claim
10. returns PreservedUnproven
11. retry aborts
```

A failure in the mechanism intended to establish ownership can therefore create an artifact that the application deliberately can never auto-clean later.

### Required fix

The ownership bootstrap must be all-or-clean-exactly-now:

```text
stage = CreateNew...

try
{
    RecordOwnership(stage.Identity)
}
catch (recordFailure)
{
    try
    {
        stage.DeleteExact()
    }
    catch (cleanupFailure)
    {
        throw composite exception preserving both failures
    }

    throw
}
```

Because the retained creation handle still exists, this deletion requires no journal authority: it is the exact object just created by this call.

Apply this rule everywhere ownership is first attached to a new object.

If journal append may have physically landed before returning failure (e.g. flush failure), that is fine:

```text
stage is deleted exactly
stale journal record later resolves to missing object and is dropped
```

### Mandatory tests

```text
CRUU18_006_CAS_stage_claim_failure_deletes_exact_stage_before_releasing_handle
CRUU18_006_Migration_CreateOwnedStage_claim_failure_leaves_no_unproven_temp
CRUU18_006_Ownership_append_flush_failure_cleans_exact_stage_even_if_record_may_exist
CRUU18_006_Migration_stage_claim_failure_does_not_wedge_RecoverForRetry
CRUU18_006_No_stage_factory_closes_unclaimed_creation_handle_without_exact_cleanup
```

---

## CRUU18-007 — MED-HIGH / VERIFICATION DEFECT
## Evidence-quality acceptance is still lexical source inference, and several required sentinels do not execute the integration path their names claim

### Affected code

- `tests/PromptHelper.Tests/Cruu16EvidenceQualityTests.cs`
- `tests/PromptHelper.Tests/Cruu17RegressionTests.cs`
- `tools/FindingCoverageMap.json`
- `tools/RequiredRegressionTests.psd1`

### Improvement

The classifier now explicitly rejects the simplest cases:

```text
typeof(ProductionType).GetMethod(...)
nameof(ProductionType)
```

when nothing else in the test body looks executable.

That is an improvement over CRUU17.

### It is still regex inference

A test is considered production behavior when:

```text
body contains any production type token
AND
body contains something that regex interprets as a production-looking call
```

The "instance call" regex is not tied to the production type token.

A synthetic body such as:

```text
string marker = nameof(DataFolderMigrationService);
fake.DoWork();
```

can satisfy:

```text
touchesProduction       = true
callsProductionInstance = true
reflectionOrMentionOnly = false
```

and therefore be classified `ProductionBehavior` even though no production object was invoked.

This is the same architectural problem in a narrower form:

```text
source text is being used as execution evidence
```

### Required CRUU17 tests still demonstrate the gap

#### `CRUU17_005_Migration_retry_can_prove_final_from_prepublication_identity_record`

The name says **migration retry**.

The implementation calls:

```text
OwnedArtifactReconciler.Reconcile(...)
```

through `AssertPrepublicationMigrationClaimRecoversFinal`.

It never calls:

```text
MigrationRecoveryService.RecoverForRetry(...)
```

CRUU18-001 is therefore present while the named retry sentinel is green.

#### `CRUU17_001_Library_primary_published_then_ledger_failure_is_classified_committed`

and:

```text
CRUU17_001_Backup_published_then_ledger_failure_cannot_leave_a_stale_inflight_CAS_silently
```

both call the same direct CAS helper.

They do not drive:

```text
LibraryRepository.CommitIfPrimaryUnchanged
LibraryRepository.SynchronizeBackup
```

The source implementations happen to propagate the exception correctly, but these required names do not prove that.

#### CRUU17-004 "compaction crash" sentinels

The implementation legitimately chose the audit's append-only alternative and removed live-claim compaction.

Several required sentinel names still say:

```text
Crash_during_ledger_compaction...
Crash_after_old_ledger_sideline...
Reader_selects_highest_complete_valid_generation...
```

but the tests no longer perform a crash or generation selection.

That does not make the append-only product design wrong, but it means exact required names are being satisfied by semantically different tests.

### Required fix

Stop trying to infer high-risk runtime coverage from C# source regexes.

Preferred authority format:

```json
{
  "CRUU18-001": {
    "tests": ["..."],
    "requiredProductionSymbols": [
      "MigrationRecoveryService.RecoverForRetry"
    ]
  }
}
```

For CRITICAL/HIGH/MED-HIGH recovery findings, collect runtime coverage or explicit test-only hit probes and verify:

```text
required test ran
AND
required production symbol was hit
```

A hit is not by itself sufficient to prove the assertion is good, but it prevents the current class of "right name, wrong layer" acceptance.

Additionally:

- if an implementation legitimately chooses an alternative architecture, update the audit coverage mapping to a new behaviorally accurate sentinel instead of retaining a misleading required name merely to satisfy an exact-name list;
- add a semantic review gate for required sentinel changes.

### Mandatory tests / gates

```text
CRUU18_007_Nameof_production_type_plus_fake_instance_call_is_not_ProductionBehavior
CRUU18_007_Reflection_Invoke_without_mapped_production_hit_is_not_ProductionBehavior
CRUU18_007_CRUU18_001_sentinel_hits_MigrationRecoveryService_RecoverForRetry
CRUU18_007_CRUU18_003_sentinel_hits_real_MainWindow_category_committed_exception_path
CRUU18_007_High_risk_finding_map_requires_explicit_production_symbol
CRUU18_007_Required_test_name_cannot_substitute_a_different_integration_layer
```

---

# 5. Important verified improvements that should stay closed

## 5.1 CAS post-publication outcome is now truthful at the primitive boundary

After candidate publication, failures in:

```text
CandidatePublished journal append
pre-image retirement
```

are converted into:

```text
CommittedAtomicReplacementRequiresRestartException
```

This is the correct fundamental distinction and fixes the CRUU17 point-of-no-return inversion at the CAS primitive.

CRUU18-003 is specifically about callers that fail to honor that new contract.

## 5.2 Data-folder settings transition no longer rolls back after settings publication

Both empty-target and existing-target transition paths catch the committed CAS exception and treat the selection as committed/restart-required.

The original CRUU17-001 critical transition bug is therefore closed at those paths.

## 5.3 Prepared vs sidelined phase order is materially improved

The durable `Prepared` record now exists before the sideline rename.

`PreimageSidelined` is appended only after the old object has actually been moved.

CRUU18-002 is a later terminal-rollback edge, not a reappearance of the exact old false-phase bug.

## 5.4 Ownership journal rewrite is same-object bound

Non-empty rewrite now:

```text
opens exact current ledger
compares file identity to snapshot identity
compares content to snapshot hash
```

and does not overwrite anything.

A same-byte foreign replacement is preserved.

## 5.5 Live ownership journal is append-only

The journal does not compact itself while live claims remain.

This validly removes the CRUU17-004 recursive self-recovery problem.

The report does **not** require dual-generation compaction if append-only operation remains bounded and correct.

## 5.6 Migration identity is recorded before publication

`RecordMigrationArtifactPrepared` now carries:

```text
temp path
final path
identity
length
hash
```

before rename.

This is the right durable model.

CRUU18-001 is an API-consumer mismatch, not a rejection of this design.

## 5.7 Normal TempOwned rollback is identity-bound

`MigrationOwnedFile` now has:

```text
TempIdentityToken
FinalIdentityToken
```

and normal rollback verifies exact identity + content through one retained deletion handle.

That closes the direct CRUU17-006 hash-only branch.

## 5.8 Rollback now consumes fatal reconciliation outcomes

`MigrationTargetTransaction.Rollback()` adds Warning/Fatal reconciliation outcomes to `MigrationRollbackFailure` and returns early on Fatal.

The CRUU17-007 source defect is closed.

---

# 6. Root architectural diagnosis

CRUU18 is no longer primarily about missing primitives.

The primitives have become strong.

The remaining defects are now mostly **semantic integration mismatches between strong primitives**:

```text
MigrationArtifact durable format
        ≠
RecoverForRetry cleanup reader

Committed-restart exception contract
        ≠
Category UI error handling

PreimageSidelined runtime rollback
        ≠
Startup recovery state matrix

Committed ownership cleanup dependency
        ≠
Migration marker retirement ordering

FinalOwned in-memory state
        ≠
Generic catch's TempAbandoned transition

"Behavioral evidence" requirement
        ≠
Regex source classifier
```

That suggests a change in repair strategy.

Do not add another parallel helper for each edge.

Instead, reduce the number of independent authorities.

The target architecture should have:

```text
ONE migration artifact authority reader
ONE CAS state matrix
ONE committed-mutation UI boundary
ONE migration finalization state machine
ONE high-risk runtime evidence model
```

---

# 7. Ordered CRUU18 implementation plan

---

## PHASE 01 — Unify migration rollback/retry ownership authority
### Fixes CRUU18-001

Create one production primitive that understands:

```text
MigrationArtifact:
    temp path
    final path
    exact identity
    expected length
    expected hash
```

Use it from:

```text
OwnedArtifactReconciler
RecoverForRetry
MigrationTargetTransaction.Rollback
```

Support legacy `MigrationFinal` using manifest content authority.

Delete or narrow `ProvenanceBoundCleanup.DeleteFileIfProven` so it cannot silently apply the wrong record-path semantics to finals.

**Exit criterion**

```text
A real current-format interrupted migration final is deleted by RecoverForRetry
iff exact identity AND expected content still match.
```

---

## PHASE 02 — Close the CAS successful-rollback terminal state
### Fixes CRUU18-002

Teach recovery:

```text
target exact old identity
+ preimage absent
+ candidate not committed
= rolled back
```

for every pre-commit durable phase where that state is reachable.

Optionally add explicit `RolledBack`, but exact identity inference must still work if the phase append fails.

**Exit**

A promotion failure whose restore succeeded never poisons startup.

---

## PHASE 03 — Centralize committed-mutation UI handling
### Fixes CRUU18-003

Route every persistence mutation through a single wrapper.

Catch:

```text
CommittedMutationRequiresRestartException
```

before generic `IOException`.

Set one global fatal mutation flag and request shutdown.

Use library-neutral restart wording.

**Exit**

No library-mutating event handler contains a broad persistence catch without the committed-restart boundary.

---

## PHASE 04 — Repair post-publish migration in-memory state
### Fixes CRUU18-005

Split copy failure handling at the publication boundary.

Before publication:

```text
failure => exact stage cleanup => TempAbandoned
```

After publication:

```text
failure => retain FinalOwned => outer rollback exact-deletes final
```

Make state transitions explicit and illegal transitions throw.

**Exit**

`RecordMigrationArtifactPublished` failure does not prevent clean in-process rollback.

---

## PHASE 05 — Make Ready marker the actual committed-finalization retry token
### Fixes CRUU18-004

After settings commit:

```text
retire committed ownership claims
IF success:
    delete Ready marker
ELSE:
    keep Ready marker
```

Startup finalization then becomes the sole retry path.

**Exit**

Marker absence implies no migration rollback/deletion claims remain.

---

## PHASE 06 — Make ownership bootstrap all-or-exact-cleanup
### Fixes CRUU18-006

At every new stage:

```text
create exact object
attempt durable claim
if claim fails:
    DeleteExact while creation handle is retained
    then dispose
```

Do not leave an unproven application-created file simply because the provenance write failed.

**Exit**

No ownership-record creation failure can create a current-format unproven temp residue.

---

## PHASE 07 — Replace lexical evidence quality with mapped runtime evidence
### Fixes CRUU18-007

Extend `FindingCoverageMap.json` for high-risk findings with required production symbols.

Use coverage or explicit instrumentation to bind:

```text
test name
→ actual production method execution
```

Retire misleading historical aliases where the architecture changed.

**Exit**

A test cannot satisfy a high-risk finding by mentioning a production type and executing an unrelated helper.

---

## PHASE 08 — Full acceptance matrix

On the exact final SHA:

```text
1. Fresh checkout.
2. Restore/build Release.
3. CRUU18 retry recovery suite.
4. CRUU18 CAS rollback-terminal-state suite.
5. CRUU18 category committed-exception UI suite.
6. CRUU18 migration postpublish rollback suite.
7. CRUU18 marker finalization suite.
8. CRUU18 ownership-bootstrap failure suite.
9. filesystem/reparse integration suite.
10. full suite once.
11. full suite five consecutive times.
12. exact required sentinel verification across retained TRX.
13. finding-map completeness gate.
14. production-symbol runtime evidence gate.
15. pinned icon regeneration verification.
16. self-contained win-x64 publish.
17. strict published executable icon verification.
18. execute release workflow manually on exact final SHA.
19. bind CI evidence to the exact SHA.
20. fresh independent source/recovery audit.
```

---

# 8. Mandatory new CRUU18 sentinels

```text
# CRUU18-001
CRUU18_001_Real_RecoverForRetry_deletes_current_MigrationArtifact_final
CRUU18_001_RecoverForRetry_accepts_final_from_RestoreRelativePath_authority
CRUU18_001_RecoverForRetry_preserves_same_bytes_different_identity_final
CRUU18_001_RecoverForRetry_preserves_same_identity_tampered_final
CRUU18_001_Legacy_MigrationFinal_retry_uses_manifest_content_authority
CRUU18_001_CRUU17_005_retry_sentinel_executes_MigrationRecoveryService_RecoverForRetry

# CRUU18-002
CRUU18_002_Promotion_failure_after_PreimageSidelined_successful_restore_is_not_fatal_on_restart
CRUU18_002_PreimageSidelined_plus_exact_old_identity_at_target_is_recognized_as_rolled_back
CRUU18_002_Restored_old_target_is_never_reported_CAS_AMBIGUOUS
CRUU18_002_Promotion_failure_restore_failure_still_preserves_preimage_and_fails_closed
CRUU18_002_CAS_matrix_includes_successful_runtime_rollback_after_sidelined_phase

# CRUU18-003
CRUU18_003_CreateCategory_postpublish_bookkeeping_failure_requests_shutdown
CRUU18_003_RenameCategory_postpublish_bookkeeping_failure_requests_shutdown
CRUU18_003_DeleteCategory_postpublish_bookkeeping_failure_requests_shutdown
CRUU18_003_Category_committed_exception_is_not_caught_by_generic_IOException_path
CRUU18_003_Category_committed_exception_does_not_leave_UI_running_with_stale_document
CRUU18_003_All_library_mutation_UI_paths_share_committed_restart_boundary

# CRUU18-004
CRUU18_004_Postcommit_ownership_retirement_failure_keeps_Ready_marker
CRUU18_004_Ready_marker_is_deleted_only_after_committed_ownership_claims_retire
CRUU18_004_Restart_retries_failed_committed_ownership_retirement
CRUU18_004_Restart_retry_preserves_payload_then_retires_ledger_then_marker
CRUU18_004_No_success_path_can_delete_marker_while_migration_rollback_claims_survive

# CRUU18-005
CRUU18_005_Real_copy_postpublish_record_failure_keeps_MigrationOwnedFile_FinalOwned
CRUU18_005_Postpublish_record_failure_inprocess_rollback_deletes_exact_final
CRUU18_005_MarkTempAbandoned_rejects_FinalOwned_transition
CRUU18_005_DeleteExact_after_promotion_cannot_be_mistaken_for_final_cleanup
CRUU18_005_DataFolderTransition_postpublish_migration_record_failure_rolls_back_cleanly

# CRUU18-006
CRUU18_006_CAS_stage_claim_failure_deletes_exact_stage_before_releasing_handle
CRUU18_006_Migration_CreateOwnedStage_claim_failure_leaves_no_unproven_temp
CRUU18_006_Ownership_append_flush_failure_cleans_exact_stage_even_if_record_may_exist
CRUU18_006_Migration_stage_claim_failure_does_not_wedge_RecoverForRetry
CRUU18_006_No_stage_factory_closes_unclaimed_creation_handle_without_exact_cleanup

# CRUU18-007
CRUU18_007_Nameof_production_type_plus_fake_instance_call_is_not_ProductionBehavior
CRUU18_007_Reflection_Invoke_without_mapped_production_hit_is_not_ProductionBehavior
CRUU18_007_CRUU18_001_sentinel_hits_MigrationRecoveryService_RecoverForRetry
CRUU18_007_CRUU18_003_sentinel_hits_real_MainWindow_category_committed_exception_path
CRUU18_007_High_risk_finding_map_requires_explicit_production_symbol
CRUU18_007_Required_test_name_cannot_substitute_a_different_integration_layer
```

Total proposed new CRUU18 sentinels: **38**.

---

# 9. Required acceptance invariants after repair

```text
MIG-AUTH-01
One authority model interprets current MigrationArtifact temp/final identity everywhere.

MIG-AUTH-02
RecoverForRetry recognizes RestoreRelativePath as the final authority.

MIG-AUTH-03
Automatic final deletion requires identity AND expected content.

MIG-AUTH-04
Legacy MigrationFinal cleanup gains manifest content authority.

CAS-ROLLBACK-01
If the exact old object is restored to the target and the pre-image is absent,
startup recognizes the transaction as rolled back.

CAS-ROLLBACK-02
A safe runtime rollback can never become CAS_AMBIGUOUS solely because the durable
phase still says PreimageSidelined.

UI-COMMIT-01
Every committed CAS exception reaches one fatal restart boundary before IOException handling.

UI-COMMIT-02
No mutation remains enabled after committed recovery bookkeeping fails.

MIG-STATE-01
FinalOwned can never transition to TempAbandoned.

MIG-STATE-02
Post-publication bookkeeping failure preserves exact final deletion authority for rollback.

MIG-MARKER-01
Ready marker absence implies committed migration ownership claims are retired.

MIG-MARKER-02
Ownership retirement failure keeps Ready marker for startup retry.

OWN-BOOT-01
Failure to durably claim a just-created stage triggers exact handle-bound cleanup before handle release.

OWN-BOOT-02
No provenance failure creates an unproven application-created stage when the creation handle is still owned.

EVIDENCE-01
High-risk evidence is tied to an explicit production symbol/method.

EVIDENCE-02
A required test name alone never proves an integration layer it did not execute.

EVIDENCE-03
Source regex heuristics cannot independently satisfy CRITICAL/HIGH/MED-HIGH acceptance.

REL-01
Release build passes.

REL-02
Full suite passes five consecutive times.

REL-03
All exact required sentinels pass from retained TRX.

REL-04
Runtime production-symbol evidence passes.

REL-05
Release workflow passes on exact final SHA.

REL-06
Self-contained published EXE passes strict asset/icon verification.

REL-07
Fresh independent source audit reports zero findings.
```

---

# 10. Repair priority

Recommended order:

```text
P0 / first:
    CRUU18-001  retry recovery authority mismatch
    CRUU18-002  restored CAS misclassified as fatal
    CRUU18-003  committed category mutation swallowed

P1:
    CRUU18-005  FinalOwned -> TempAbandoned post-publish bug
    CRUU18-004  Ready marker retired despite ownership-retirement failure
    CRUU18-006  ownership-record bootstrap leaks unproven stages

P2 / acceptance infrastructure:
    CRUU18-007  runtime evidence gate
```

CRUU18-001 and CRUU18-005 should preferably be fixed in the same migration-authority refactor because they are two sides of the same state model:

```text
prepublication durable authority
→ publication
→ in-process rollback
→ restart retry
```

Do not patch only the test helper.

---

# 11. Final assessment

The current commit is closer to a robust design than the CRUU17 snapshot.

The repair correctly solved the hardest CRUU17 concept:

> **after publication, a bookkeeping failure is still a committed write.**

It also created the right pre-publication migration identity record and removed unsafe self-compaction.

The defects now arise where older consumers still speak the previous protocol.

The clearest example is CRUU18-001:

```text
writer/reconciler says:
    final authority is RestoreRelativePath

retry deleter says:
    I only understand RelativePath

test says:
    migration retry works

actual RecoverForRetry:
    never executed by that sentinel
```

That is exactly the sort of cross-layer mismatch the next repair should eliminate.

The desired end state is not more defensive adapters.

It is fewer authorities.

---

# 12. Final status

```text
AUDITED_HEAD                         = e95bc2be7bfaf65c4ad076a39f3e42535b29a64f

CRUU18_FINDINGS                      = 7
HIGH                                 = 3
MED_HIGH                             = 4

CRUU17_CORE_IMPROVEMENT_REAL         = YES
CRUU17_STRICTLY_CLOSED               = NO

SOURCE_AUDIT_CLEAN                   = NO

WINDOWS_RUNTIME_DIRECTLY_EXECUTED    = NO
EXACT_HEAD_CI_STATUS                 = NOT INDEPENDENTLY BOUND

STRICT_RELEASE_READY                 = NO
ZERO_DEFECT_VERIFIED                 = NO
