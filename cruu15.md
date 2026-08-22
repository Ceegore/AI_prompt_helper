# CRUU15 — Independent Post-CRUU14 Adversarial Re-Audit and Fix Plan

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited HEAD:** `3d76b65fcaf6c775abf757c23efecd98c44a06dc`  
**Previous CRUU14 audited HEAD:** `931502ebcda6c0bb73b3e239e725a9dea0cffc29`  
**Primary CRUU14 implementation commit:** `3b4779dd991747b87895035767e882bfb21d3a91`  
**Follow-up / final claimed-fix commit:** `3d76b65fcaf6c775abf757c23efecd98c44a06dc`  
**Audit date:** 2026-08-22  
**Mode:** independent source, architecture, test-evidence, crash/recovery, filesystem-authority and release-path re-audit. Commit messages and test names are implementation evidence, not proof.

> Report only. No production source was changed by this audit.

---

# 1. Executive verdict

The CRUU14 implementation is substantial and closes several real defects.

Confirmed genuine improvements include:

- general `IDurableAtomicFileWriter` staging now retains the created staging handle through promotion/deletion;
- category CRUD and prompt move now at least route through a disk precondition path;
- the fatal committed-mutation UI path now honors the injectable restart-message hook;
- target active prompt bodies are strict-UTF-8 checked;
- existing-target and migration-payload commit leases now use a strict file opener that rejects file reparse points and proves physical containment;
- mutation, migration and initialization journal retirement now attempts same-handle read/validate/delete;
- the public arbitrary `SynchronizeBackup(CanonicalLibraryPackage)` API was removed;
- the icon source, ICO and approval manifest exist;
- PE icon verification now enumerates all `RT_GROUP_ICON` groups;
- several old false-positive tests were repaired.

However, **zero-defect acceptance is still not justified at the audited HEAD**.

The remaining defects are concentrated in a few architectural seams:

1. the new owned-stage primitive was not propagated into the old migration-specific writers;
2. the new CAS helper is explicitly still a verify-then-close-then-write design;
3. “handle-bound” retirement does not automatically mean “reparse-safe / root-bound”;
4. some cleanup code still proves only that the current object is a regular file, not that it is the object Prompt Helper created;
5. the evidence gate still cannot demonstrate that all historical findings are covered behaviorally;
6. the process helper timeout fix can still hang on a genuinely non-terminating child;
7. the icon/release chain is improved but still not reproducibly regenerated and required by an actual release gate.

```text
AUDITED_HEAD                       = 3d76b65fcaf6c775abf757c23efecd98c44a06dc

SOURCE_AUDIT_CLEAN                 = NO
CRUU14_ALL_FINDINGS_FIXED          = NO
NEW_OR_REOPENED_FINDINGS           = 12

CRITICAL                           = 0
HIGH                               = 6
MED_HIGH                           = 5
MED / RELEASE_GAP                  = 1

WINDOWS_TESTS_DIRECTLY_EXECUTED    = NO
WINDOWS_RUNTIME_VALIDATION         = NOT_INDEPENDENTLY_VERIFIED
IMPLEMENTER_REPORTED_FULL_SUITE    = 555/555 PASS IN ~54s
INDEPENDENT_ATTACHED_CI_STATUS     = NOT_OBTAINED
STRICT_RELEASE_READY               = NO
ZERO_DEFECT_VERIFIED               = NO
```

The audit environment is Linux and has neither `dotnet`, `pwsh`, nor Windows PowerShell, so the WPF/.NET Windows suite could not be independently executed here. The available GitHub combined-status query returned no status entries for the exact HEAD, and the available workflow-run lookup exposes only PR-triggered runs and returned none. The reported 555/555 run is therefore retained as implementation evidence, not independent runtime proof.

---

# 2. CRUU14 closure matrix

| CRUU14 | Current status | CRUU15 assessment |
|---|---|---|
| 001 | **PARTIAL / REOPENED** | General durable writers use `WindowsOwnedDurableStage`, but migration manifest updates and migration payload promotion still close their staging handle and later rename by pathname. See CRUU15-001/002. |
| 002 | **NOT FULLY FIXED** | All major user mutations now route through a precondition path, but `WindowsExpectedFileCasReplacer` explicitly releases exclusion before the actual write. See CRUU15-003. |
| 003 | **NOT FULLY FIXED** | Settings has the same verify-then-write gap, plus missing→created races. See CRUU15-004. |
| 004 | **NOT FULLY FIXED** | Hash-bound finals improved, but identity-only cleanup still proves only the current object’s type/location, not attempt ownership. See CRUU15-006/007. |
| 005 | **PARTIAL / REOPENED** | Journal retirement is same-handle, but `WindowsHandleBoundFile` follows reparse points; other stage/directory cleanup remains path-based. See CRUU15-005/006. |
| 006 | **PARTIAL** | Inventory now probes every enumerated entry and fails closed on many errors/reparse points, but enumeration/classification remains pathname-based and the source itself documents the residual object-swap TOCTOU. See CRUU15-008. |
| 007 | **FIXED_SOURCE** | Existing-target active prompt bodies are strict UTF-8 checked during inspection and final lease acquisition. |
| 008 | **FIXED_SOURCE_CORE** | Existing/migration commit leases now use reparse-safe final-handle-path file opening and keep those handles through commit. |
| 009 | **FIXED_SOURCE** | Backup synchronization package overload is internal. |
| 010 | **NOT FULLY FIXED** | Legacy temps are preserved, but current-format temp names are still auto-deleted without creation-bound provenance. See CRUU15-007. |
| 011 | **NOT FIXED AS ACCEPTANCE GATE** | CRUU13/14 entries were added, but ten CRUU12 finding IDs still have no dedicated sentinel, the manifest self-test is one-way only, and behavioral false positives remain. See CRUU15-009. |
| 012 | **PARTIAL** | Approved SVG/pixel manifest and all-PE-group comparison exist. Canonical native generation is still not executed/pinned in CI and the strict release check is opt-in manual workflow dispatch. See CRUU15-011. |

---

# 3. Findings

---

## CRUU15-001 — HIGH
## Migration manifest phase update still uses the exact closed-stage/path-promotion pattern CRUU14-001 was meant to eliminate

### Affected code

- `src/PromptHelper/Services/MigrationManifestRepository.cs`
- `src/PromptHelper/Services/IMigrationManifestFileOps.cs`
- `src/PromptHelper/Services/DataFolderTransitionCoordinator.cs`

### Current sequence

`WriteReadyManifestDurable` still does this:

```text
CreateNew(stagePath)
write bytes
FlushToDisk(stage stream)
dispose/close stage stream
FileExists(markerPath)
MoveFileExW(stagePath -> markerPath) by pathname
```

Failure cleanup also does:

```text
if (!promoted && FileExists(stagePath))
    DeleteFile(stagePath)
```

The default implementation uses path-based `MoveFileExW` and path-based delete.

### Why this is still a defect

The application no longer owns a retained handle to the exact stage object at the moment it promotes it.

A second process can replace the stage object after the flush/close and before `MoveFileExW`.

The manifest phase update can then promote whichever object currently occupies the staging pathname.

There is an additional destructive case:

1. the deterministic stage path already exists before this call;
2. `CreateNew(stagePath)` fails;
3. `promoted == false`;
4. `finally` observes a file at `stagePath`;
5. the repository deletes it by pathname.

That means a file the current invocation **never created** can be deleted simply because it occupied the declared stage pathname.

### Why the later ReadyGate does not save this

The coordinator runs `MigrationReadyGate.AssertReady(...)` **before** switching the manifest to `ReadyToCommit`.

It then writes the Ready manifest and proceeds to physical revalidation/payload commit lease/settings commit.

It does not re-read and revalidate the newly persisted marker from strict same-object authority before the settings point of no return.

Therefore substitution of the stage at the manifest-update cut is not proven impossible by the later payload lease.

### Required fix

Delete the separate “manifest staging” persistence implementation.

Route `WriteReadyManifestDurable` through one of:

- `WindowsOwnedDurableStage`; or
- the same lower-level owned-stage primitive used by the normal durable writers.

Requirements:

1. `CreateNew` retained handle.
2. Write exact serialized bytes.
3. Flush exact handle.
4. Verify non-reparse + physical root from the retained handle.
5. Promote exact retained object.
6. On failure, delete only the exact retained object.
7. If stage creation failed because something already exists, **preserve it** and fail closed.
8. Before settings commit, either:
   - keep authoritative marker authority across the commit, or
   - strictly reopen/read/validate the persisted Ready marker and hold/prove that exact marker as required by the recovery model.

### Required tests

```text
CRUU15_001_Preexisting_manifest_stage_CreateNew_failure_never_deletes_foreign_file
CRUU15_001_Manifest_stage_replacement_after_flush_before_promotion_is_never_promoted
CRUU15_001_Ready_marker_bytes_are_revalidated_after_phase_promotion_before_settings_commit
CRUU15_001_Failed_ready_promotion_preserves_foreign_stage_and_copying_marker
CRUU15_001_Ready_manifest_promotion_uses_owned_handle_not_path_MoveFileEx
```

The test must inject at the exact post-flush/pre-promotion barrier.

---

## CRUU15-002 — MED-HIGH
## Migration payload files still lose object ownership between durable copy and final promotion

### Affected code

- `src/PromptHelper/Services/DataFolderMigrationService.cs`
- `src/PromptHelper/Services/IMigrationFileOps.cs`

### Current sequence

`CopyPayloadFileDurablyWithTemp`:

```text
CreateNewFile(tempPath)
Copy source -> temp
FlushToDisk(temp)
dispose temp
MarkTempOwned()
MoveNoOverwriteWriteThrough(tempPath, finalPath)   // path based
MarkFinalOwned()
```

The default mover is `MoveFileExW(sourcePath, destinationPath, MOVEFILE_WRITE_THROUGH)`.

### Improvement that already exists

The migration later re-hashes source and target files, and rollback deletes finals only when expected content matches.

That substantially limits corruption.

### Remaining failure mechanism

The “owned temp” state is a string/path state, not a retained-object state.

A replacement object can occupy `tempPath` after the original temp closes but before promotion.

The path mover can promote that replacement.

If replacement bytes differ, later validation detects it and the transition aborts. If they happen to match, the system cannot distinguish foreign ownership from its own file.

### Required fix

Replace this migration-specific temp/move path with the same owned stage abstraction used by the durable writer.

The migration transaction should register an **owned object token**, not only `TempPath`.

Suggested model:

```text
MigrationOwnedFile
- expected content identity
- retained/transferable OwnedDurableStage authority until promotion
- final promoted exact-object authority
- transaction attempt ID
```

### Required tests

```text
CRUU15_002_Migration_payload_stage_replacement_after_flush_cannot_be_promoted
CRUU15_002_Migration_payload_foreign_same_bytes_does_not_become_attempt_owned_by_path
CRUU15_002_Migration_payload_promotion_is_same_handle_from_create_through_final_name
```

---

## CRUU15-003 — HIGH
## The new “CAS” implementation is still explicitly verify-then-close-then-write

### Affected code

- `src/PromptHelper/Services/WindowsExpectedFileCasReplacer.cs`
- `src/PromptHelper/Services/LibraryRepository.cs`
- `src/PromptHelper/Services/PromptMutationCoordinator.cs`
- indirectly all category/prompt metadata CRUD

### Current contract

The new verifier opens the current file with `FILE_SHARE_READ`, computes the hash, then disposes the handle when `VerifyCurrentMatches` returns.

The caller subsequently invokes a separate durable writer.

The source comment itself states that the exclusion **does not extend through the write**.

Therefore this is not a compare-and-swap operation.

It is a stronger last-moment compare, followed by a separate write.

### Concrete remaining race

```text
T1  CAS verifier opens old library, hashes it
T2  verifier closes handle
T3  external process replaces library.json
T4  Prompt Helper's durable writer replaces library.json
```

The valid external update at T3 is overwritten.

The same gap exists for prompt-body edit:

```text
VerifyCurrentMatches(bodyPath, oldHash)
_writer.ReplaceDurable(bodyPath, newBytes)
```

### Body-only edit is especially sensitive

The body-only path still does:

```text
VerifyPrimaryUnchanged(...)
Advance journal -> MetadataDurable
Commit(newPackage)
```

`VerifyPrimaryUnchanged` is a plain read/hash check.

Once `MetadataDurable` lands, recovery treats the body edit as committed.

A change after the check but before the later unguarded commit remains outside a write-bound CAS.

### Required fix

Implement a single atomic contract that includes:

```text
expected object/hash validation
+
candidate owned stage
+
replace exact expected target
```

The exclusive expected-current authority must survive until the atomic replacement consumes it.

Do not preserve test injection by weakening production semantics.

Instead introduce an injectable abstraction around the **correct atomic primitive**.

Possible conceptual API:

```text
IAtomicExpectedFileReplacer.ReplaceIfCurrentMatches(
    targetPath,
    expectedAuthority,
    candidateBytes,
    DurableFileClass)
```

Where `expectedAuthority` includes, as appropriate:

- file identity / volume identity;
- exact raw SHA-256;
- final physical path;
- non-reparse proof.

### Required exact barrier tests

```text
CRUU15_003_Primary_changes_after_CAS_hash_before_atomic_replace_is_preserved
CRUU15_003_Category_create_change_at_post_CAS_prewrite_barrier_aborts
CRUU15_003_Category_rename_change_at_post_CAS_prewrite_barrier_aborts
CRUU15_003_Category_delete_change_at_post_CAS_prewrite_barrier_aborts
CRUU15_003_Move_prompt_change_at_post_CAS_prewrite_barrier_aborts
CRUU15_003_Edit_body_change_at_post_CAS_prewrite_barrier_aborts
CRUU15_003_Body_only_edit_primary_change_after_last_check_never_gets_overwritten
```

A test where the external write happens **before `CommitIfPrimaryUnchanged` is called** is not sufficient.

---

## CRUU15-004 — HIGH
## Missing-file settings preconditions and backup future-schema preservation still have race windows

### Affected code

- `src/PromptHelper/Services/AppSettingsRepository.cs`
- `src/PromptHelper/Services/LibraryRepository.cs`

### Settings primary: missing -> created race

`SaveCoreWithCas` only calls `VerifyCurrentMatches` when the captured precondition says the primary **already existed**.

If the transition snapshot recorded:

```text
Primary.Exists == false
```

there is no immediate “must still be missing” atomic precondition at write time.

A file can therefore be created after the precondition snapshot and before `_durableWriter.WriteDurable`, and the writer will replace it.

### Settings backup: same problem

When `expectedBackupToken.Exists == false`, the backup synchronization does not CAS the “still missing” state.

A future-schema or otherwise important backup can appear after the preceding inspection and before replacement.

### Library backup: future-schema preservation is also check-then-write

`LibraryRepository.SynchronizeBackup`:

1. inspects current backup state;
2. preserves it if it already sees future schema;
3. later calls `ReplaceDurable`.

A future-schema backup that appears between those two actions can still be overwritten.

### Why severity is HIGH

For settings, the primary write is the point of no return for a data-folder transition.

For backups, the code explicitly promises to preserve newer schema data.

The race violates those stated contracts.

### Required fix

The atomic expected-file primitive must support both:

```text
ExpectedPresent(exact identity/hash)
ExpectedMissing
```

`ExpectedMissing` must be enforced by the promotion operation itself (no-overwrite semantics), not by an earlier `File.Exists`/read.

Backup synchronization should use the same expected-state primitive when the preservation contract matters.

### Required tests

```text
CRUU15_004_Settings_primary_expected_missing_foreign_create_before_write_is_preserved
CRUU15_004_Settings_backup_expected_missing_future_schema_create_is_preserved
CRUU15_004_Settings_existing_change_at_post_verify_prewrite_barrier_is_preserved
CRUU15_004_Library_backup_future_schema_appearing_after_state_read_is_preserved
CRUU15_004_Data_folder_settings_point_of_no_return_has_atomic_expected_state
```

---

## CRUU15-005 — HIGH
## “Handle-bound” journal retirement follows file reparse points and is not physical-root-bound

### Affected code

- `src/PromptHelper/Services/WindowsHandleBoundFile.cs`
- `src/PromptHelper/Services/LibraryMutationJournalRepository.cs`
- `src/PromptHelper/Services/LibraryInitializationJournalRepository.cs`
- `src/PromptHelper/Services/MigrationManifestRepository.cs`

### Improvement

Retirement no longer validates one pathname and later deletes whatever a second path lookup finds.

That closes the original validate→replace→delete race.

### New authority gap

`WindowsHandleBoundFile.OpenExistingOrNull` uses `CreateFileW` with:

```text
GENERIC_READ | DELETE
FILE_SHARE_READ | FILE_SHARE_DELETE
OPEN_EXISTING
FILE_ATTRIBUTE_NORMAL
```

It does **not** use:

```text
FILE_FLAG_OPEN_REPARSE_POINT
```

and it does not:

- inspect/reject reparse tags;
- obtain the final physical path;
- assert physical containment under the expected data root.

Therefore a journal pathname that is a file symlink/reparse point can be followed.

The subsequent `DeleteExact()` is exact-object deletion — but it can be the exact object **the reparse point redirected to**, not the object at the intended managed journal location.

### Required fix

Merge the good properties of:

- `WindowsHandleBoundFile`
- `WindowsStrictFileOpener`

into one retirement authority.

Suggested API:

```text
WindowsStrictRetirableFile.Open(
    expectedPath,
    expectedPhysicalRoot)
```

It must:

1. open with `FILE_FLAG_OPEN_REPARSE_POINT`;
2. reject reparse file;
3. prove final physical path;
4. prove strict containment under root;
5. read/parse/validate from that exact handle;
6. delete that exact handle.

### Required tests

```text
CRUU15_005_Mutation_journal_file_symlink_is_never_followed_or_deleted
CRUU15_005_Initialization_journal_file_symlink_is_never_followed_or_deleted
CRUU15_005_Migration_marker_file_symlink_is_never_followed_by_retirement
CRUU15_005_Journal_retirement_final_handle_path_must_be_under_expected_root
```

Do not make these tests “best effort” for strict acceptance. CI must run in an environment where file-reparse creation is available.

---

## CRUU15-006 — HIGH
## Recovery cleanup still contains non-owner-bound and raw pathname deletion paths

### Affected code

- `src/PromptHelper/Services/MigrationRecoveryService.cs`
- `src/PromptHelper/Services/IMigrationFileOps.cs`
- `src/PromptHelper/Services/IVerifiedArtifactDeleter.cs`

### Cases still present

#### Manifest phase stage during retry

Uses:

```text
VerifyIdentityAndDelete(root, controlPath)
```

This proves that the current object is:

- regular;
- non-reparse;
- physically under root.

It does **not** prove that it is the stage object created by the migration attempt.

#### Payload temps during retry

Same identity-only deletion.

A partially written temp legitimately cannot be checked against the final payload hash — but that does not make “whatever regular file is currently at the pathname” owned.

#### Capability-probe directories

Current pattern:

```text
DirectoryExists(path)
EnumerateEntries(path) == empty
DeleteDirectory(path)
```

There is no retained directory identity from emptiness check through deletion.

#### Attempt-created prompts/recovery directories

Also removed by later path lookup/delete.

#### Committed-startup stage reconciliation

`FinalizeCommittedStartup` directly calls:

```text
_fileOps.DeleteFile(stagePath)
```

after a separate path existence check.

That bypasses the same-object retirement work entirely.

### Required fix

Every destructively cleaned transaction artifact must be backed by durable ownership authority.

For partial files:

- record a creation file ID/volume ID in a durable journal at the moment ownership is established; or
- retain an owned object handle if cleanup occurs in-process.

For control directories:

- record and revalidate directory file identity;
- inspect and delete through the same directory authority where Windows permits;
- never infer ownership from “expected name + currently empty.”

For committed-startup stage cleanup:

- if provenance cannot be proven, preserve it and fail/quarantine instead of deleting it.

### Required tests

```text
CRUU15_006_Recovery_payload_temp_replaced_by_foreign_regular_file_is_preserved
CRUU15_006_Recovery_manifest_stage_replaced_by_foreign_regular_file_is_preserved
CRUU15_006_Capability_probe_directory_swapped_after_empty_check_is_not_deleted
CRUU15_006_Attempt_created_directory_replacement_is_not_deleted_by_path
CRUU15_006_FinalizeCommittedStartup_never_raw_deletes_unproven_stage
```

---

## CRUU15-007 — MED-HIGH
## Startup temp reconciliation still treats a current-format filename plus “regular file” as ownership

### Affected code

- `src/PromptHelper/Services/DataRootTempReconciler.cs`
- `src/PromptHelper/Services/SettingsTempReconciler.cs`
- `src/PromptHelper/Services/SettingsTempName.cs`

### Improvement

Legacy temp names are now preserved.

Current-format candidates are deleted through a reparse-safe same-handle deleter instead of raw `File.Delete`.

### Why CRUU14-010 remains open

The source comments correctly state that a filename is not ownership evidence.

But the implementation then deletes every current-format candidate that:

1. matches the expected name grammar;
2. is currently a regular non-reparse descendant file.

There is no attempt journal, temp registry, creation file ID, or content authority proving this object is the orphan Prompt Helper created.

A foreign file can occupy the same current-format path after a crash or substitution.

The cleanup still deletes it.

### Required fix

Use explicit provenance classes:

```text
ProvenOwned
JournalOwned
LegacyUnverifiable
UnprovenCurrentFormat
Foreign
```

Only `ProvenOwned` / `JournalOwned` should be auto-destroyed.

For unproven current-format orphan files:

- preserve; or
- move to a non-destructive quarantine only if that move itself is safe and does not overwrite anything.

### Required tests

```text
CRUU15_007_Current_format_settings_temp_without_provenance_is_preserved
CRUU15_007_Current_format_prompt_temp_without_provenance_is_preserved
CRUU15_007_Current_format_recovery_temp_without_provenance_is_preserved
CRUU15_007_Recorded_owned_temp_is_cleaned_using_recorded_identity
```

---

## CRUU15-008 — MED-HIGH
## Migration inventory is safer but still not an object-bound strict inventory

### Affected code

- `src/PromptHelper/Services/MigrationTargetInventoryInspector.cs`

### Genuine improvement

The implementation now:

- probes the root;
- probes each enumerated file and directory;
- rejects recognized reparse entries;
- propagates many I/O/access errors instead of treating them as missing;
- classifies bootstrap controls separately.

### Residual gap

Enumeration is still:

```text
Directory.GetFiles(dir)
Directory.GetDirectories(dir)
```

followed by later `StrictPathAuthority.Probe(entry)` calls.

The source comment explicitly acknowledges an object-swap TOCTOU between probe/classification and later caller action.

That means CRUU14’s requested verified-directory-object inventory abstraction was not actually implemented.

### Required fix

Move from pathname inventory to directory-handle-bound enumeration/identity.

At minimum:

1. open and bind the root directory handle;
2. enumerate from verified directory authority;
3. for every entry that will later be acted on, carry identity into that action;
4. fail if the object changes between inventory and destructive use.

If all destructive callers are redesigned around creation-bound ownership as required by CRUU15-006, this inventory can become advisory classification rather than destructive authority, reducing the severity of this gap.

### Required tests

```text
CRUU15_008_Inventory_file_swap_between_enumeration_and_probe_fails_closed
CRUU15_008_Inventory_directory_swap_between_classification_and_cleanup_cannot_author_delete
CRUU15_008_Unreadable_entry_is_never_reclassified_as_absent
```

---

## CRUU15-009 — HIGH
## The regression evidence gate still proves “listed tests exist,” not “all historical findings are covered behaviorally”

### Affected code

- `tools/RequiredRegressionTests.psd1`
- `tests/PromptHelper.Tests/RequiredRegressionTestsManifestTests.cs`
- `tests/PromptHelper.Tests/Cruu12ComprehensiveVerificationTests.cs`
- `tests/PromptHelper.Tests/Cruu14ComprehensiveVerificationTests.cs`
- `tools/VerifyTestEvidence.ps1`

### A. Ten CRUU12 finding IDs remain explicitly uncovered

The manifest itself states that these have no dedicated exact-name sentinel:

```text
CRUU12-005
CRUU12-007
CRUU12-009
CRUU12-010
CRUU12-017
CRUU12-019
CRUU12-022
CRUU12-030
CRUU12-033
CRUU12-034
```

This alone means the “all historical required findings are sentinel-covered” condition is not met.

### B. The manifest self-test is one-way

The test named:

```text
CRUU14_011_Required_manifest_contains_all_CRUU12_CRUU13_CRUU14_sentinels
```

does not know a canonical expected set.

It only:

1. imports whatever names are already in the manifest;
2. reflects compiled test methods;
3. checks that every listed name exists.

If a required name is deleted from **both** the manifest and the intended coverage set, this test stays green.

So it proves:

```text
manifest entry -> test method exists
```

It does not prove:

```text
all required finding IDs -> manifest contains required behavioral sentinel
```

### C. CRUU12-032 is still a source-string test

`CRUU12_032_Evidence_script_rejects_substring_only_TRX` still merely reads the PowerShell script and accepts it if the source contains `testName` or `Exact`.

It does not execute the verifier against a substring-only TRX and prove rejection.

### D. Several older CRUU12 names still overstate behavior

Examples still visible:

- `CRUU12_012_Concurrent_directory_creator_foreign_content_is_preserved` is sequential and creates no foreign content.
- `CRUU12_013_Move_success_before_bookkeeping_failure_final_is_recoverable` injects no bookkeeping failure.
- `CRUU12_014_Declared_payload_temp_replaced_with_foreign_bytes_is_preserved` calls the deleter directly rather than `RecoverForRetry`.
- `CRUU12_016_V3_same_library_json_changed_prompt_body_rejects_retry` compares fingerprints but does not call retry recovery.
- `CRUU12_018_Custom_to_empty_default_bootstrap_with_settings_controls_succeeds` tests inventory classification, not the full transition.
- `CRUU12_021_UTF16_BOM_source_library_rejected` tests the UTF-8 helper, not the migration source path.
- `CRUU12_023_Session_lease_validates_final_handle_identity` performs a normal acquisition; it does not force an identity swap.
- `CRUU12_025_Rollback_stage_residue_preserves_marker` only checks inventory; it does not execute rollback/marker retirement.
- `CRUU12_026/027` exercise the deleter directly rather than the real capability cleanup path.

### E. New CRUU14 CAS tests miss the exact remaining race

The current CRUU14 tests change the file **before** the CAS method is called.

They do not inject after CAS verification but before the writer replacement.

Therefore they cannot detect CRUU15-003.

### Required fix

Create a canonical, machine-readable coverage authority independent of the mutable sentinel manifest.

Example:

```json
{
  "CRUU12-001": ["ExactBehavioralTestA", "..."],
  "CRUU12-002": ["..."],
  ...
  "CRUU15-012": ["..."]
}
```

The build must verify:

1. every historical finding ID requiring regression coverage exists in the coverage map;
2. every mapped test exists;
3. every mapped test executes and passes;
4. no mapped test is inconclusive/skipped;
5. each named adversarial cut has a deterministic fault hook.

Rewrite weak tests to execute their real named path.

### Mandatory missing-evidence tests

```text
CRUU15_009_Canonical_finding_coverage_map_contains_every_CRUU12_through_CRUU15_required_ID
CRUU15_009_Removing_required_ID_from_manifest_fails_coverage_gate
CRUU15_009_Substring_only_TRX_fixture_is_rejected_by_executing_VerifyTestEvidence
CRUU15_009_Missing_required_TRX_fixture_is_rejected_by_executing_VerifyTestEvidence
CRUU15_009_Failed_required_TRX_fixture_is_rejected_by_executing_VerifyTestEvidence
CRUU15_009_Inconclusive_required_TRX_fixture_is_rejected
```

---

## CRUU15-010 — MED-HIGH
## The test-process pipe fix still has an unbounded hang path after timeout

### Affected code

At least:

- `tests/PromptHelper.Tests/IconAssetTests.cs`
- `tests/PromptHelper.Tests/RequiredRegressionTestsManifestTests.cs`
- other newly changed child-process test helpers

### Current pattern

The repaired helpers correctly begin draining both pipes concurrently:

```text
stdoutTask = StandardOutput.ReadToEndAsync()
stderrTask = StandardError.ReadToEndAsync()
exited = WaitForExit(60000 or 30000)
stdout = stdoutTask.GetAwaiter().GetResult()
stderr = stderrTask.GetAwaiter().GetResult()
Assert.IsTrue(exited)
```

### Remaining hang

If the child process genuinely does not exit:

1. `WaitForExit(timeout)` returns `false`;
2. child is still alive;
3. stdout/stderr remain open;
4. `ReadToEndAsync` tasks do not complete;
5. `GetResult()` blocks indefinitely;
6. the code never reaches `Assert.IsTrue(exited)`.

So the former pipe-buffer deadlock is fixed, but the timeout cannot actually guarantee termination.

This is directly relevant because the preceding implementation cycle already experienced deterministic test hangs.

### Required fix

After timeout:

```text
if (!exited)
{
    Kill(entireProcessTree: true)
    await/finish pipe drains with a bounded secondary timeout
    fail test
}
```

Better: create one reusable `ProcessTestRunner` abstraction that:

- concurrently drains both streams;
- has one absolute timeout;
- kills the process tree on timeout;
- bounds post-kill drain;
- returns exit code/stdout/stderr;
- never blocks forever.

Use it everywhere rather than open-coding process handling.

### Required tests

```text
CRUU15_010_Process_runner_large_stdout_and_stderr_does_not_deadlock
CRUU15_010_Process_runner_hung_child_is_killed_at_timeout
CRUU15_010_Process_runner_hung_child_with_open_pipes_still_returns
CRUU15_010_Process_runner_kills_descendant_process_tree
```

---

## CRUU15-011 — MED / RELEASE GAP
## Icon identity is pinned, but canonical SVG→native-render generation is still not reproduced by CI and strict release checking remains opt-in

### Affected code

- `tools/GenerateAppIconNative.js`
- `tests/PromptHelper.Tests/IconAssetTests.cs`
- `src/PromptHelper/Assets/PromptHelperIcon.approved.json`
- `.github/workflows/windows-ci.yml`
- `tools/VerifyReleaseAssets.ps1`

### Improvements

The current chain now proves useful independent facts:

- source SVG hash matches approval manifest;
- checked-in ICO decoded pixels match approved pixel hashes;
- all executable icon groups are compared with the checked-in ICO.

### Remaining source→render gap

`CRUU14_012_Canonical_generator_renders_each_size_from_vector_source` still only searches JavaScript source text for:

- size literals;
- `sharp(svgPath)`;
- absence of `auto-resize`.

It does not run the generator.

`CRUU14_012_Each_native_frame_matches_approved_normalized_RGBA_hash` only verifies that the approval manifest contains every required size.

It does not generate native frames and compare those generated pixels with the approved frame hashes.

The generator itself states that `sharp` must be externally installed. There is no root `package.json` pinning the generator dependency in the audited tree.

### Strict release gate remains manual

The workflow performs strict published-EXE icon verification only for:

```text
workflow_dispatch + release_gate=true
```

Five consecutive full-suite runs are also opt-in via `stress=true`.

There is no tag/release workflow making the strict asset chain a mandatory release invariant.

### Required fix

1. Add a small pinned tool package:
   - `package.json`
   - lockfile
   - pinned `sharp` version.
2. CI installs exactly the lockfile.
3. CI runs `GenerateAppIconNative.js` into a temporary ICO.
4. Decode generated frames and compare normalized pixels against approval manifest.
5. Compare checked-in ICO against those approved pixels.
6. Compare all EXE groups against approved/checked-in frames.
7. Add actual release/tag gate where this verification is mandatory.
8. Do not let the normal icon-presence test silently return success when the required icon is missing.

### Required tests/gates

```text
CRUU15_011_Fresh_checkout_can_run_pinned_canonical_icon_generator
CRUU15_011_Generated_native_frames_match_approved_RGBA_hashes
CRUU15_011_Deleting_required_ICO_fails_required_icon_test
CRUU15_011_Release_tag_path_requires_strict_icon_verification
CRUU15_011_All_EXE_icon_groups_have_no_unapproved_required_frame_content
```

---

## CRUU15-012 — MED-HIGH
## The new handle-bound promotion removes pathname substitution but no longer has an explicit rename-metadata write-through guarantee

### Affected code

- `src/PromptHelper/Services/WindowsOwnedDurableStage.cs`
- `src/PromptHelper/Services/WindowsDurableAtomicFileWriter.cs`
- `src/PromptHelper/Services/WindowsDurableSettingsFileWriter.cs`

### Current sequence

The owned stage:

1. writes data;
2. calls `FlushFileBuffers`;
3. performs a handle-bound `SetFileInformationByHandle(FileRenameInfo)` rename;
4. marks terminal.

The file is opened with `FILE_ATTRIBUTE_NORMAL`, not `FILE_FLAG_WRITE_THROUGH`.

There is no explicit flush after the rename.

### Why this needs proof/fix

The old path-based implementation used `MOVEFILE_WRITE_THROUGH` during rename.

The new design correctly fixes same-object authority, but it changed the persistence primitive.

Microsoft documents that `FILE_FLAG_WRITE_THROUGH` causes NTFS to flush metadata changes such as a rename that result from processing the request. The current stage flush happens **before** the rename metadata change.

Therefore the code no longer demonstrates an equivalent explicit “rename metadata reached durable storage before success is reported” contract.

This is a durability-evidence gap, not proof that every rename will be lost on crash.

For a codebase whose contracts repeatedly use names such as `ReplaceDurable`, this must be established rather than assumed.

### Additional physical-binding gap

`WindowsOwnedDurableStage` contains:

```text
AssertNonReparseAndUnderRoot(...)
```

but the general durable writers do not call it.

The abstraction therefore has the check, but normal production promotion does not systematically prove the stage’s final physical path is under the intended root before promotion.

### Required fix

Define the durable-promotion contract explicitly and implement it once.

Candidate requirements:

1. create owned stage with appropriate write-through semantics where supported;
2. flush content;
3. verify stage physical containment from retained handle;
4. perform handle-bound rename;
5. perform/document the required post-rename durability barrier, or use a Windows primitive/flag whose documented semantics cover rename metadata;
6. only then return success.

Do not guess the correct Windows durability barrier. Document the selected Win32 contract with platform references and test power-cut/crash behavior as far as practical.

### Required tests

```text
CRUU15_012_Durable_stage_asserts_physical_root_before_promotion
CRUU15_012_Durable_promotion_uses_documented_rename_metadata_write_through_contract
CRUU15_012_ReplaceDurable_does_not_report_success_before_required_post_rename_barrier
```

---

# 4. Additional positive verification

The following previously reported items were rechecked and should **not** be reopened without new evidence.

## Fatal mutation UI hang

`HandleFatalMutationException` now:

- sets fatal mutation state;
- constructs the restart-required message;
- uses `_showRestartMessage` when injected;
- falls back to modal `MessageBox.Show` only when no hook exists;
- requests shutdown in `finally`.

This resolves the deterministic automated-test modal hang described in the implementation notes.

## CRUU14-007

Target prompt bodies now receive strict UTF-8 decoding during target content capture.

The exact held bytes are also strict-decoded again inside `ExistingTargetCommitLease`.

## CRUU14-008

`WindowsStrictFileOpener` now:

- opens with `FILE_FLAG_OPEN_REPARSE_POINT`;
- rejects reparse-point files;
- resolves final path from the same handle;
- proves strict descendant relation.

Existing and migrated payload commit leases use this opener and retain handles through the commit window.

## CRUU14-009

`SynchronizeBackup(CanonicalLibraryPackage)` is internal.

The public arbitrary package-to-backup authority reported in CRUU14 is closed.

## Icon PE group enumeration

`PeIconResourceReader` now continues `EnumResourceNamesW` through every group instead of returning after the first.

The EXE comparer iterates all returned groups.

---

# 5. Root architectural diagnosis

The remaining defects reduce to five principles.

## 5.1 One persistence primitive is not actually one persistence primitive

The general writer was fixed, but migration has separate implementations for:

- manifest staging;
- payload staging.

Those older paths retained the old race model.

**Fix primitives globally, then ban alternative promotion implementations.**

## 5.2 A last-moment check is not a CAS

A check can be perfect and still be racy if the object is released before the write.

The correct boundary is:

```text
validate expected state
        |
        | same authority remains live
        v
atomic replacement
```

## 5.3 Same-handle deletion is necessary but not sufficient

The handle must also be proven to represent:

- a non-reparse object;
- the intended physical root;
- the intended transaction/creation identity.

## 5.4 A syntactically managed pathname is not provenance

GUIDs solve collision probability.

They do not prove ownership after a crash.

## 5.5 An acceptance-test manifest needs an external completeness authority

A list cannot prove its own completeness simply by confirming that every item currently in the list exists.

---

# 6. New ordered fix plan

The order below is intentionally dependency-first. Do not patch individual call sites with more pre-checks.

---

## PHASE 01 — Make owned staging the only legal durable promotion primitive
### Covers CRUU15-001, 002 and foundation for 012

Refactor:

- normal library/prompt/control durable writer;
- settings durable writer;
- migration manifest Ready transition;
- migration payload temp promotion.

Delete or deprecate path-based:

```text
MoveNoOverwriteWriteThrough(sourcePath, destinationPath)
ReplaceWriteThrough(sourcePath, destinationPath)
```

for transaction-owned stages.

### Exit criteria

No persisted Prompt Helper transaction stage is:

```text
close stage handle
then rename stage pathname
```

---

## PHASE 02 — Complete durability semantics for handle-bound rename
### Covers CRUU15-012

Specify and implement the exact Windows durability contract.

Include physical-root validation before promotion.

### Exit criteria

`Durable` has one documented meaning across all writers.

---

## PHASE 03 — Replace verifier + writer pairs with a real atomic expected-state replacer
### Covers CRUU15-003

Implement production-correct injectable primitive.

Support:

```text
ExpectedPresent(identity/hash)
ExpectedMissing
```

and bind expectation through final replacement.

### Exit criteria

No production mutation contains:

```text
VerifyCurrentMatches(...)
<handle closes>
ReplaceDurable(...)
```

---

## PHASE 04 — Apply atomic expected-state replacement to all settings and backup contracts
### Covers CRUU15-004

Apply to:

- settings primary;
- settings backup;
- library primary;
- library backup future-schema-preservation contract;
- prompt-body edit.

### Exit criteria

A change inserted at the exact last pre-write barrier is always preserved/fails closed.

---

## PHASE 05 — Build one strict retirable-file authority
### Covers CRUU15-005

Merge:

- same-handle read/delete;
- OPEN_REPARSE_POINT;
- tag rejection;
- final path;
- root containment.

Use for:

- mutation journal;
- initialization journal;
- migration marker;
- any future transaction marker.

---

## PHASE 06 — Introduce durable provenance for partial migration controls/temps
### Covers CRUU15-006

Persist creation identity where content cannot be used.

No recovery auto-delete should rely on:

```text
expected pathname + current regular file
```

or:

```text
expected pathname + current empty directory
```

---

## PHASE 07 — Redesign startup temp reconciliation around provenance
### Covers CRUU15-007

Current-format without provenance must not be auto-deleted.

If disk-format compatibility requires cleanup of old versions, define an explicit conservative migration/quarantine policy.

---

## PHASE 08 — Finish inventory object authority
### Covers CRUU15-008

Either:

- implement verified handle-based enumeration; or
- demote inventory to non-destructive classification and ensure every destructive operation carries its own exact ownership authority.

The second design is often simpler and safer.

---

## PHASE 09 — Fix all process-runner timeout semantics
### Covers CRUU15-010

Create one `ProcessTestRunner`.

Migrate every redirected child-process test helper to it.

Run stress tests with children that:

- flood both pipes;
- never exit;
- spawn descendants.

---

## PHASE 10 — Rebuild regression coverage authority
### Covers CRUU15-009

Create canonical finding→test coverage map.

Add the ten missing CRUU12 IDs.

Rewrite remaining helper-only/source-string tests.

Add all CRUU15 exact barrier tests.

### Exit criteria

Removing one required finding ID or one required behavioral sentinel makes CI fail.

---

## PHASE 11 — Complete reproducible icon/release chain
### Covers CRUU15-011

Pin native icon generator dependencies.

Execute generator in CI.

Compare generated frames to approval manifest.

Make strict icon/EXE check mandatory on the actual release/tag path.

---

## PHASE 12 — Windows adversarial acceptance

Run, on Windows at the exact final commit:

1. clean restore;
2. Release build;
3. targeted filesystem-authority suite;
4. targeted mutation/crash suite;
5. targeted migration/recovery suite;
6. targeted settings durability suite;
7. process-runner timeout/deadlock suite;
8. release/icon suite;
9. complete full suite;
10. full suite five consecutive times;
11. exact finding→sentinel coverage verification;
12. self-contained `win-x64` publish;
13. strict published-EXE icon chain;
14. retain all TRX and publish artifacts;
15. independent source re-audit against the exact tested SHA.

---

# 7. CRUU15 required sentinel additions

Add at least these exact tests to the required evidence authority after implementation:

```text
CRUU15_001_Preexisting_manifest_stage_CreateNew_failure_never_deletes_foreign_file
CRUU15_001_Manifest_stage_replacement_after_flush_before_promotion_is_never_promoted
CRUU15_001_Ready_marker_bytes_are_revalidated_after_phase_promotion_before_settings_commit
CRUU15_001_Failed_ready_promotion_preserves_foreign_stage_and_copying_marker
CRUU15_001_Ready_manifest_promotion_uses_owned_handle_not_path_MoveFileEx

CRUU15_002_Migration_payload_stage_replacement_after_flush_cannot_be_promoted
CRUU15_002_Migration_payload_foreign_same_bytes_does_not_become_attempt_owned_by_path
CRUU15_002_Migration_payload_promotion_is_same_handle_from_create_through_final_name

CRUU15_003_Primary_changes_after_CAS_hash_before_atomic_replace_is_preserved
CRUU15_003_Category_create_change_at_post_CAS_prewrite_barrier_aborts
CRUU15_003_Category_rename_change_at_post_CAS_prewrite_barrier_aborts
CRUU15_003_Category_delete_change_at_post_CAS_prewrite_barrier_aborts
CRUU15_003_Move_prompt_change_at_post_CAS_prewrite_barrier_aborts
CRUU15_003_Edit_body_change_at_post_CAS_prewrite_barrier_aborts
CRUU15_003_Body_only_edit_primary_change_after_last_check_never_gets_overwritten

CRUU15_004_Settings_primary_expected_missing_foreign_create_before_write_is_preserved
CRUU15_004_Settings_backup_expected_missing_future_schema_create_is_preserved
CRUU15_004_Settings_existing_change_at_post_verify_prewrite_barrier_is_preserved
CRUU15_004_Library_backup_future_schema_appearing_after_state_read_is_preserved
CRUU15_004_Data_folder_settings_point_of_no_return_has_atomic_expected_state

CRUU15_005_Mutation_journal_file_symlink_is_never_followed_or_deleted
CRUU15_005_Initialization_journal_file_symlink_is_never_followed_or_deleted
CRUU15_005_Migration_marker_file_symlink_is_never_followed_by_retirement
CRUU15_005_Journal_retirement_final_handle_path_must_be_under_expected_root

CRUU15_006_Recovery_payload_temp_replaced_by_foreign_regular_file_is_preserved
CRUU15_006_Recovery_manifest_stage_replaced_by_foreign_regular_file_is_preserved
CRUU15_006_Capability_probe_directory_swapped_after_empty_check_is_not_deleted
CRUU15_006_Attempt_created_directory_replacement_is_not_deleted_by_path
CRUU15_006_FinalizeCommittedStartup_never_raw_deletes_unproven_stage

CRUU15_007_Current_format_settings_temp_without_provenance_is_preserved
CRUU15_007_Current_format_prompt_temp_without_provenance_is_preserved
CRUU15_007_Current_format_recovery_temp_without_provenance_is_preserved
CRUU15_007_Recorded_owned_temp_is_cleaned_using_recorded_identity

CRUU15_008_Inventory_file_swap_between_enumeration_and_probe_fails_closed
CRUU15_008_Inventory_directory_swap_between_classification_and_cleanup_cannot_author_delete
CRUU15_008_Unreadable_entry_is_never_reclassified_as_absent

CRUU15_009_Canonical_finding_coverage_map_contains_every_CRUU12_through_CRUU15_required_ID
CRUU15_009_Removing_required_ID_from_manifest_fails_coverage_gate
CRUU15_009_Substring_only_TRX_fixture_is_rejected_by_executing_VerifyTestEvidence
CRUU15_009_Missing_required_TRX_fixture_is_rejected_by_executing_VerifyTestEvidence
CRUU15_009_Failed_required_TRX_fixture_is_rejected_by_executing_VerifyTestEvidence
CRUU15_009_Inconclusive_required_TRX_fixture_is_rejected

CRUU15_010_Process_runner_large_stdout_and_stderr_does_not_deadlock
CRUU15_010_Process_runner_hung_child_is_killed_at_timeout
CRUU15_010_Process_runner_hung_child_with_open_pipes_still_returns
CRUU15_010_Process_runner_kills_descendant_process_tree

CRUU15_011_Fresh_checkout_can_run_pinned_canonical_icon_generator
CRUU15_011_Generated_native_frames_match_approved_RGBA_hashes
CRUU15_011_Deleting_required_ICO_fails_required_icon_test
CRUU15_011_Release_tag_path_requires_strict_icon_verification
CRUU15_011_All_EXE_icon_groups_have_no_unapproved_required_frame_content

CRUU15_012_Durable_stage_asserts_physical_root_before_promotion
CRUU15_012_Durable_promotion_uses_documented_rename_metadata_write_through_contract
CRUU15_012_ReplaceDurable_does_not_report_success_before_required_post_rename_barrier
```

---

# 8. Special test-design requirements

## Exact race barriers

The new race tests need deterministic synchronization, not `Thread.Sleep`.

Example:

```text
production thread:
    validates expected target
    signals barrier A
    waits barrier B
    performs atomic replacement

test:
    waits barrier A
    performs external replacement
    signals barrier B
```

The desired result is that the final atomic operation fails because expected authority no longer matches.

## Reparse tests

Do not use:

```text
try create symlink
if fail -> Inconclusive
```

for a required acceptance suite.

Make CI environment capable of creating the required reparse points, or provide a controlled native test helper.

## Child-process tests

Do not rely only on `--blame-hang-timeout` to rescue a helper that has no correct timeout semantics.

Each process helper must terminate itself safely.

---

# 9. Acceptance criteria after CRUU15 implementation

Do not grant zero-defect status until all are true:

```text
A. No path-based promotion of any owned staging artifact.
B. No verify-then-close-then-write path is called a CAS.
C. ExpectedMissing is enforced atomically, not by an earlier absence check.
D. Every journal retirement is same-handle + non-reparse + root-bound.
E. No partial temp/control cleanup treats pathname syntax as ownership.
F. No committed-startup recovery raw-deletes a stage by path.
G. Every historical required finding ID has an explicit behavioral regression mapping.
H. The finding->sentinel completeness authority is independent of the mutable sentinel list.
I. No required test is helper-only when the production path can be executed.
J. No required test is inconclusive/skipped.
K. Process helpers cannot block after their timeout expires.
L. Canonical icon generator is pinned and executed in CI.
M. Release/tag path requires strict SVG->approved frames->ICO->all EXE groups validation.
N. Durable rename metadata semantics are explicitly documented and enforced.
O. Full Windows suite passes 5 consecutive times at the exact final SHA.
P. Exact TRX evidence is retained and independently reviewable.
Q. Final independent source re-audit finds zero remaining defects.
```

Only then:

```text
STRICT_RELEASE_READY = YES
ZERO_DEFECT_VERIFIED = YES
```

---

# 10. Final assessment

The latest implementation is clearly stronger than the CRUU14 audited version, and the reported 555/555 run is consistent with the functional fixes that landed.

The remaining findings are not evidence that the repair effort failed. They show that the project has reached the stage where **primitive semantics** matter more than ordinary feature behavior.

The largest remaining implementation mistake is architectural:

> Prompt Helper now has a correct same-object stage primitive, but not every persistence path uses it; and it now has a stronger pre-write verifier, but the verifier is still separated from the atomic write.

The next pass should therefore avoid adding more checks around existing paths.

It should remove the remaining alternative persistence/cleanup implementations and converge all destructive authority onto a small set of rigorously defined primitives.

---

## Final status

```text
AUDITED_HEAD                       = 3d76b65fcaf6c775abf757c23efecd98c44a06dc
IMPLEMENTER_REPORTED_TESTS         = 555/555 PASS
INDEPENDENT_WINDOWS_EXECUTION      = NOT AVAILABLE IN THIS AUDIT ENVIRONMENT

CRUU15_FINDINGS                    = 12
CRITICAL                           = 0
HIGH                               = 6
MED_HIGH                           = 5
MED / RELEASE_GAP                  = 1

SOURCE_AUDIT_CLEAN                 = NO
CRUU14_ALL_FINDINGS_FIXED          = NO
STRICT_RELEASE_READY               = NO
ZERO_DEFECT_VERIFIED               = NO
```
