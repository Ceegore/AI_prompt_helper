# CRUU14 – Post-CRUU13 Independent Adversarial Audit and Fix Plan

**Repository:** `Ceegore/AI_prompt_helper`  
**Branch:** `main`  
**Audited HEAD:** `931502ebcda6c0bb73b3e239e725a9dea0cffc29`  
**Prior audited product HEAD:** `1c00161c5edc18f1ca2856dd3f5d1e2db6ea2555`  
**CRUU13 repair commit:** `51102fcba98ed739d8bb83d79653c41c0d284b9d`  
**Icon follow-up commit:** `931502ebcda6c0bb73b3e239e725a9dea0cffc29`  
**Audit date:** 2026-08-22

---

## 1. Purpose

This is a fresh adversarial audit after the implementation of the CRUU13 repair plan.

The audit had two independent goals:

1. Re-check every CRUU13 defect against the actual current source rather than accepting commit-message claims.
2. Hunt for second-order defects introduced or exposed by the fixes, especially around:
   - durable persistence,
   - point-of-no-return semantics,
   - TOCTOU/CAS behavior,
   - filesystem identity and ownership,
   - crash recovery,
   - migration,
   - startup recovery,
   - test-evidence integrity,
   - release-asset identity.

No production source was modified during this audit.

---

## 2. Validation scope and limitation

The current source tree was inspected directly at:

`931502ebcda6c0bb73b3e239e725a9dea0cffc29`

The audit included:

- CRUU13 finding-by-finding source verification;
- new implementation review;
- migration and mutation transaction review;
- startup/recovery review;
- filesystem authority review;
- persistence writer review;
- exact regression-test manifest review;
- regression-test semantic review;
- Windows CI workflow review;
- icon generation/release identity review;
- current branch/status evidence inspection.

### Runtime limitation

The available independent execution environment is Linux and does not contain:

- `dotnet`
- `pwsh`
- Windows PowerShell

Therefore Windows/.NET/WPF tests and the Windows publish path could not be executed independently in this audit environment.

The commit messages state that the implementer ran a passing test suite, including 536 tests on the final icon commit. Those statements are useful implementation evidence, but they are **not treated as independent verification**.

The current GitHub commit had no independently retrievable combined status entries through the available connector. The available commit-workflow lookup also returned no associated PR-triggered run. This does **not** prove that a push workflow never ran; it means this audit did not obtain independent attached CI evidence for the exact audited HEAD.

---

# 3. Executive verdict

```text
SOURCE_AUDIT_CLEAN                 = NO
CRUU13_ALL_FINDINGS_FIXED          = NO
NEW_ADVERSARIAL_FINDINGS_PRESENT   = YES

CRITICAL_FINDINGS                  = 1
HIGH_FINDINGS                      = 6
MED_HIGH_FINDINGS                  = 4
MED_RELEASE_GAPS                   = 1
TOTAL_FINDINGS                     = 12

REQUIRED_TESTS_BEHAVIORALLY_VALID  = NO
WINDOWS_TESTS_DIRECTLY_EXECUTED    = NO
WINDOWS_RUNTIME_VALIDATION         = NOT_INDEPENDENTLY_VERIFIED
STRICT_RELEASE_READY               = NO
ZERO_DEFECT_VERIFIED               = NO
```

The CRUU13 repair materially improved the product. Several previously serious defects are genuinely closed in source.

However, the new implementation still has a common underlying weakness:

> Many operations now *check* content/hash/identity before mutation, but the checked object is not always bound to the object that is subsequently promoted, replaced, or deleted.

This leaves several check-then-act races in the persistence and recovery authority model.

The most important newly identified defect is in the durable file writers themselves: a staging file is closed before a path-based `MoveFileExW` promotion, so the implementation loses same-object authority during the most important persistence step.

---

# 4. CRUU13 closure matrix

| CRUU13 | Status at current HEAD | CRUU14 assessment |
|---|---|---|
| 001 | **FIXED_SOURCE** | Fatal committed-mutation exception now has a specific UI path and requests shutdown before ordinary save-error handling. |
| 002 | **FIXED_SOURCE** | Body-only edit commit authority now uses durable journal phase before the content-neutral primary commit. |
| 003 | **PARTIAL / REOPENED** | `ExistingTargetCommitLease` was added, but individual payload files are opened with ordinary `FileStream`; no reparse/final-handle-path authority. See CRUU14-008. |
| 004 | **NOT FULLY FIXED / REOPENED** | Raw deletion was reduced, but `VerifyIdentityAndDelete` proves the identity of the *current* object, not ownership of the object created by the app. See CRUU14-004. |
| 005 | **FIXED_SOURCE_CORE** | Empty-target flow now acquires root operation lease before retry and can bind created child directories. |
| 006 | **FIXED_SOURCE** | Schema-v3 retry now checks full payload fingerprint rather than only library metadata hash. |
| 007 | **FIXED_SOURCE** | Bootstrap-root context and persistent bootstrap control classification are now represented explicitly. |
| 008 | **NOT FIXED** | Inventory remains primarily `Directory.*` path enumeration without typed unreadable/reparse authority. See CRUU14-006. |
| 009 | **PARTIAL** | Migration metadata decoding is strict UTF-8 now, but existing-target active prompt bodies are still only hashed during target inspection. See CRUU14-007. |
| 010 | **FIXED_SOURCE_CORE** | Rollback clean predicate now accounts for control/temp/final/directory residue more completely. |
| 011 | **PARTIAL / REOPENED** | Wildcard probe cleanup was removed, but ownership of same-path replacements is still not established. See CRUU14-004. |
| 012 | **NOT FIXED** | Public `LibraryDocument` overload is gone, but a public arbitrary `CanonicalLibraryPackage.Create(document)` can still feed public backup synchronization. See CRUU14-009. |
| 013 | **FIXED_SOURCE** | Startup diagnostics are now accumulated through `StartupDiagnosticCollector`. |
| 014 | **PARTIAL** | Initialization journal is now structured and revisioned. Journal retirement remains check-then-path-delete. See CRUU14-005. |
| 015 | **NOT FIXED** | Evidence manifest remains incomplete and important sentinels remain semantically weak. See CRUU14-011. |
| 016 | **PARTIAL** | Native per-size generator exists and real icon is present, but release identity chain is not yet canonical/fully enforced. See CRUU14-012. |
| 017 | **FIXED** | `PromptHelperLogo.svg` and `PromptHelper.ico` now exist. |
| 018 | **FIXED_SOURCE** | Mutation journal schema v2 requires `revision`; schema v1 has explicit compatibility handling. |
| 019 | **NOT FIXED / REOPENED** | `CommitIfPrimaryUnchanged` is still check-then-replace, and several metadata mutations bypass it. See CRUU14-002. |

---

# 5. New and reopened findings

---

## CRUU14-001 — CRITICAL
## Durable staging-file identity is lost before authoritative promotion

### Affected code

- `src/PromptHelper/Services/WindowsDurableAtomicFileWriter.cs`
- `src/PromptHelper/Services/WindowsDurableSettingsFileWriter.cs`
- related staging/promotion paths in `DataFolderMigrationService` / `DefaultMigrationFileOps`

### Observed behavior

The durable writers follow this pattern:

1. Generate an unpredictable temp path.
2. Create with `FileMode.CreateNew`.
3. Write bytes.
4. Flush to disk.
5. Dispose/close the file handle.
6. Call path-based `MoveFileExW(tempPath, targetPath, ...)`.

The handle that proved which object the application created is gone before promotion.

The cleanup path has the same problem:

- `WindowsDurableAtomicFileWriter.BestEffortDelete` performs a path check and then `File.Delete`.
- `WindowsDurableSettingsFileWriter` failure cleanup probes the path and then calls `File.Delete`.

### Failure scenario

A cooperating test process or unrelated local process can:

1. observe the staging pathname after the app closes its handle;
2. replace the staging object at the same pathname;
3. allow the writer to continue.

The writer then promotes the object currently found at that path rather than the exact object it created and flushed.

For authoritative file classes, that can include:

- `library.json`,
- library backup,
- prompt body,
- recovery artifact,
- mutation journal,
- migration journal/control,
- initialization journal/control,
- settings.

This is not merely a cleanup problem. It can change the bytes promoted into an authoritative destination.

### Why current tests can miss it

Tests generally inject a writer failure *before* or *during* the persistence call. They do not insert a synchronization barrier at the precise cut:

`flush/close staging handle -> MoveFileExW(path)`

A random GUID pathname lowers accidental collision probability, but randomness is not object authority.

### Required fix

Create a single reusable Windows durable-file primitive with **same-object authority from creation through promotion**.

Preferred design:

1. Create the staging file with Win32 or `FileStream` and retain its handle.
2. Write and `FlushFileBuffers` / `Flush(true)`.
3. Resolve and record:
   - file ID,
   - volume serial,
   - final DOS path,
   - reparse state.
4. Never close the authoritative handle before promotion.
5. Promote/rename the exact opened object using a handle-bound rename operation, e.g. `SetFileInformationByHandle` with the appropriate rename information structure/flags.
6. Re-check destination policy immediately before the handle-bound rename where needed.
7. On failure, delete/mark-for-delete the exact staging object through the retained handle.
8. Do not perform cleanup by pathname after ownership has been lost.

If handle-bound rename cannot be implemented correctly for every required platform/filesystem, the fallback must still prove that the path resolves to the same file ID and expected bytes immediately before an atomic rename while holding an exclusion mechanism that prevents replacement. A simple re-read followed by path rename is not sufficient if replacement can still occur afterward.

### Required regression tests

- `CRUU14_001_Durable_writer_replaced_stage_is_never_promoted`
- `CRUU14_001_Settings_writer_replaced_stage_is_never_promoted`
- `CRUU14_001_Failure_cleanup_preserves_replaced_stage`
- `CRUU14_001_Mutation_control_stage_replacement_never_promotes_foreign_bytes`
- `CRUU14_001_Initialization_control_stage_replacement_never_promotes_foreign_bytes`
- `CRUU14_001_Recovery_artifact_stage_replacement_never_promotes_foreign_bytes`

The test hook must stop execution **after durable staging write and before promotion**, replace the staging path with a distinct regular file, then resume.

Passing criterion:

- foreign replacement is never promoted;
- foreign replacement is never deleted merely because it occupies the old staging pathname;
- the operation fails closed with explicit evidence.

---

## CRUU14-002 — HIGH
## Library/prompt CRUD CAS remains check-then-act, and several metadata mutations bypass CAS

### Affected code

- `src/PromptHelper/Services/LibraryRepository.cs`
- `src/PromptHelper/Services/PromptMutationCoordinator.cs`
- `src/PromptHelper/Services/PromptLibraryService.cs`

### Problem A — `CommitIfPrimaryUnchanged` is not an atomic compare-and-swap

Current structure is conceptually:

```text
VerifyPrimaryUnchanged(expectedHash)
Commit(package)
```

The verification and replacement are separate operations.

An external writer can change `library.json` after verification succeeds but before the durable writer promotes the candidate.

### Problem B — category and move operations bypass CAS entirely

The following ordinary service operations still use direct `_libraryRepo.Commit(candidate)`:

- `CreateCategory`
- `RenameCategory`
- `DeleteCategory`
- `MovePrompt`

These can overwrite a valid external change made after the in-memory `_document` was loaded.

### Problem C — missing-body delete bypasses CAS

In the mutation coordinator, the branch where a prompt body is already absent commits candidate metadata without the same primary snapshot/CAS transaction used by the normal delete path.

### Problem D — edit body freshness is still check-then-replace

The fix added a body re-read before replacement, which closes one earlier window.

But the remaining sequence is still:

```text
read/hash expected current body
ReplaceDurable(bodyPath, newBytes)
```

A writer can change the body between the last read and the replace operation.

### Consequence

A valid external update can be silently overwritten even though the code now appears to have a CAS mechanism.

This is especially important because the application keeps a long-lived in-memory `LibraryDocument`; direct category/move commits can therefore overwrite disk state that changed after startup.

### Required fix

Build a **real write-bound CAS primitive**, not a verify-then-write API.

Recommended abstraction:

```text
CommitPrimaryIfSameObjectAndHash(
    candidatePackage,
    expectedFileIdentity,
    expectedRawSha256)
```

The expected object/hash must remain bound to the actual replacement operation.

The same pattern is needed for prompt body edits:

```text
ReplacePromptBodyIfSameObjectAndHash(
    promptId,
    expectedFileIdentity,
    expectedSha256,
    newBytes)
```

Then route **all** library mutations through it:

- category create/rename/delete;
- prompt move;
- prompt create;
- prompt edit;
- prompt delete including body-missing branch;
- duplicate;
- any startup or repair write that claims concurrent-change protection.

If external concurrent modifications are considered unsupported, the implementation must still fail closed instead of silently overwriting them.

### Required regression tests

- `CRUU14_002_Primary_changes_after_verify_before_commit_aborts_without_overwrite`
- `CRUU14_002_Category_create_external_primary_race_preserves_foreign_bytes`
- `CRUU14_002_Category_rename_external_primary_race_preserves_foreign_bytes`
- `CRUU14_002_Category_delete_external_primary_race_preserves_foreign_bytes`
- `CRUU14_002_Move_prompt_external_primary_race_preserves_foreign_bytes`
- `CRUU14_002_Missing_body_delete_external_primary_race_preserves_foreign_bytes`
- `CRUU14_002_Edit_body_changes_after_last_read_before_replace_is_preserved`
- `CRUU14_002_Body_only_edit_primary_changes_after_last_check_is_preserved`

Tests must inject the external write at the last possible barrier, not before the public method is called.

---

## CRUU14-003 — HIGH
## Settings `SaveIfUnchanged` is not atomic against a non-cooperating writer

### Affected code

- `src/PromptHelper/Services/AppSettingsRepository.cs`
- `src/PromptHelper/Services/WindowsDurableSettingsFileWriter.cs`
- data-folder transition settings commit paths

### Observed behavior

`SaveIfUnchanged`:

1. acquires `.settings.lock`;
2. reconciles settings temps;
3. captures current primary/backup tokens;
4. compares those tokens with the transition snapshot;
5. calls `SaveCore`;
6. `SaveCore` invokes path-based durable writer replacement.

The settings lock prevents a second Prompt Helper instance that respects the same cooperative lock from racing.

It does not bind the checked file to the later replacement against an unrelated process.

### Why severity is high

For data-folder migration/transition, the settings write is the authoritative switch telling future application launches which data root is active.

That means a stale precondition that becomes invalid after the check can still be overwritten at the point of no return.

### Required fix

Use the same handle/file-ID/hash-bound CAS primitive designed for CRUU14-002.

The settings transition should retain authority over:

- expected settings primary;
- expected settings backup if backup participates in CAS policy;
- actual file that will be replaced;
- exact candidate bytes.

The save should fail if the expected object/hash changes at any moment before the atomic promotion.

### Required regression tests

- `CRUU14_003_Settings_change_after_CAS_check_before_promotion_is_preserved`
- `CRUU14_003_Settings_backup_change_after_CAS_check_is_preserved`
- `CRUU14_003_Data_folder_transition_does_not_commit_over_external_settings_change`

---

## CRUU14-004 — HIGH
## “Identity-only” deletion verifies the current object, not ownership of the object the app created

### Affected code

- `src/PromptHelper/Services/IVerifiedArtifactDeleter.cs`
- `WindowsVerifiedArtifactDeleter.VerifyIdentityAndDelete`
- `src/PromptHelper/Services/MigrationRecoveryService.cs`
- `src/PromptHelper/Services/DataRootCapabilityValidator.cs`
- related capability/migration cleanup paths

### Problem

`VerifyIdentityAndDelete` is useful for rejecting reparse/path-containment problems.

However, when no expected content/file identity from creation is supplied, it effectively proves:

> “there is currently a regular file at the expected path and it is inside the expected root.”

It does **not** prove:

> “this is the same file Prompt Helper created for this attempt.”

A foreign regular file can replace an app-created probe/temp after creation. If recovery later calls identity-only deletion on the current object, that foreign file is eligible for deletion.

### Important distinction

A pathname pattern plus a regular-file check is not ownership.

A current file ID is also not enough unless it is compared with a file ID captured while the application owned the original object.

### Required fix

Every automatically deletable artifact needs one of:

1. a retained creation handle used for cleanup; or
2. durable ownership metadata sufficient to prove the exact object, including at least expected attempt identity plus expected immutable content/length where appropriate; or
3. a previously captured file ID/volume identity that is revalidated before handle-bound deletion.

For partial staging files whose contents may be incomplete, hash alone is not sufficient. Use creation identity.

For control directories, capture/bind the directory object identity and verify contents/attempt ownership before deletion.

Remove or sharply restrict APIs whose contract is merely “delete whatever regular file currently exists at this managed-looking pathname.”

### Required regression tests

- `CRUU14_004_Recovery_partial_temp_replaced_by_foreign_regular_file_is_preserved`
- `CRUU14_004_Capability_probe_current_replaced_after_create_is_preserved_end_to_end`
- `CRUU14_004_Control_artifact_same_path_foreign_regular_file_is_preserved`
- `CRUU14_004_Identity_only_current_object_is_not_accepted_as_ownership_proof`

---

## CRUU14-005 — HIGH
## Journal/stage retirement still uses check-then-path-delete

### Affected code

- `src/PromptHelper/Services/LibraryMutationJournalRepository.cs`
- `src/PromptHelper/Services/LibraryInitializationJournalRepository.cs`
- `src/PromptHelper/Services/MigrationManifestRepository.cs`
- `src/PromptHelper/Services/MigrationRecoveryService.cs`
- `src/PromptHelper/Services/LibraryStartupService.cs`

### Examples

Mutation journal retirement:

1. read current journal;
2. verify operation ID/revision;
3. probe path;
4. `File.Delete(path)`.

Initialization journal retirement has the same shape.

Migration stage/marker cleanup still has paths that:

- compute/control a stage pathname;
- inspect it;
- later delete that pathname.

Healthy-primary startup also has best-effort stale initialization marker removal.

### Failure scenario

After the journal is validated but before pathname deletion, the validated file can be replaced.

The deletion then applies to the new object.

### Required fix

Journal retirement must be a handle-bound action:

1. open journal as a non-reparse file under strict path authority;
2. read/parse from that exact handle;
3. validate schema, attempt/operation ID, revision, and expected phase;
4. mark/delete that exact opened object through the retained handle.

For migration marker retirement, require explicit expected:

- attempt ID;
- revision;
- terminal phase.

Remove unqualified “DeleteStrict()” methods that delete whatever current object exists without caller-supplied transaction authority.

### Required regression tests

- `CRUU14_005_Migration_marker_replaced_between_validate_and_retire_is_preserved`
- `CRUU14_005_Mutation_journal_replaced_between_validate_and_retire_is_preserved`
- `CRUU14_005_Initialization_journal_replaced_between_validate_and_retire_is_preserved`
- `CRUU14_005_Stage_file_replaced_before_cleanup_is_preserved`

---

## CRUU14-006 — MED-HIGH
## Migration target inventory remains path-based and fail-soft relative to the authority model

### Affected code

- `src/PromptHelper/Services/MigrationTargetInventoryInspector.cs`

### Observed implementation

Inventory still relies heavily on:

- `Directory.Exists`
- `Directory.GetFiles`
- `Directory.GetDirectories`
- path-based checks of attempt-created directories

### Problem

These APIs do not provide the same typed authority model used elsewhere.

The inventory needs to distinguish at least:

- Missing
- Regular file
- Directory
- Reparse point
- Unreadable/access denied
- Sharing violation / transient I/O
- Object changed during enumeration

A path that cannot be inspected safely must not be interpreted as equivalent to “nothing dangerous is present.”

### Required fix

Create an inventory-specific strict filesystem abstraction that:

1. opens/verifies the root directory;
2. enumerates from a verified directory object;
3. rejects directory/file reparse points;
4. propagates access denied/sharing/I/O as an explicit unsafe inventory state;
5. binds inspected child paths back to the verified root;
6. supports deterministic classification of bootstrap persistent controls, transaction controls, temps, finals, unknown files, and unknown directories.

### Required regression tests

- `CRUU14_006_Inventory_access_denied_is_not_empty_or_missing`
- `CRUU14_006_Inventory_sharing_violation_fails_closed`
- `CRUU14_006_Inventory_reparse_directory_fails_closed`
- `CRUU14_006_Inventory_reparse_file_fails_closed`
- `CRUU14_006_Inventory_object_swap_during_enumeration_fails_closed`

---

## CRUU14-007 — MED-HIGH
## Existing-target active prompt bodies are not strictly UTF-8 validated during target acceptance

### Affected code

- `src/PromptHelper/Services/DataFolderMigrationService.cs`
- specifically `CaptureTargetContentPass`

### Observed behavior

For target metadata:

- bytes are read;
- strict UTF-8 decode is performed;
- JSON is validated.

For each referenced prompt body:

- bytes are read;
- SHA-256 is computed.

There is no strict UTF-8 decode in that existing-target content pass.

### Consequence

A target can have:

- valid UTF-8 `library.json`;
- correct prompt filenames;
- stable fingerprints;
- but a UTF-16 or otherwise invalid UTF-8 active prompt body.

That target can satisfy content stability/fingerprint checks even though normal prompt reading is strict UTF-8.

### Required fix

During **both** target inspection passes:

- strict-decode every active prompt body;
- classify decode failures as invalid target.

The final existing-target commit lease must also validate the exact held file bytes as strict UTF-8, not merely their hashes.

### Required regression tests

- `CRUU14_007_Existing_target_UTF16_prompt_body_is_rejected`
- `CRUU14_007_Existing_target_invalid_UTF8_prompt_body_is_rejected`
- `CRUU14_007_Final_existing_target_commit_lease_revalidates_body_UTF8`

---

## CRUU14-008 — HIGH
## Existing/migration commit leases are content-bound but not strict physical-file/reparse-bound

### Affected code

- `src/PromptHelper/Services/ExistingTargetCommitLease.cs`
- `src/PromptHelper/Services/MigrationPayloadCommitLease.cs`

### Observed behavior

The leases open payload files using ordinary `FileStream` read handles and hold them with restrictive sharing while computing hashes.

This improves content stability.

However, the file opens do not themselves establish:

- `FILE_FLAG_OPEN_REPARSE_POINT`;
- rejection of file reparse points;
- final DOS path from the exact opened handle;
- strict descendant relationship between the opened file and the expected physical root.

Directory-level topology validation does not prove that every payload **file** is a non-reparse regular file under the same physical root.

### Failure scenario

A file reparse point can exist at, or be substituted into, a payload pathname before lease acquisition.

An ordinary `FileStream` can follow that redirect.

If the external target happens to have the expected bytes, hash verification succeeds while the logical managed path resolves outside the intended data root.

### Required fix

Introduce a strict Windows file-open authority primitive analogous to the strict directory opener:

1. open with reparse-point-safe flags;
2. reject reparse-point files;
3. inspect attributes/tag;
4. resolve final path from the same handle;
5. assert strict physical containment under expected root;
6. hash using the same retained handle;
7. hold that handle through the authoritative settings commit.

Apply to:

- existing-target `library.json`;
- all referenced active prompt files;
- source migration payload;
- target migration payload;
- safety backup/recovery files wherever they participate in point-of-no-return authority.

### Required regression tests

- `CRUU14_008_Existing_target_prompt_file_symlink_is_rejected`
- `CRUU14_008_Existing_target_metadata_symlink_is_rejected`
- `CRUU14_008_Migration_commit_lease_reparse_file_is_rejected`
- `CRUU14_008_Lease_final_handle_path_must_be_under_expected_physical_root`

---

## CRUU14-009 — MED-HIGH
## Backup synchronization still exposes independent document authority through public package creation

### Affected code

- `src/PromptHelper/Models/CanonicalLibraryPackage.cs`
- `src/PromptHelper/Services/LibraryRepository.cs`

### Improvement since CRUU13

The unsafe public overload:

```text
SynchronizeBackup(LibraryDocument)
```

was removed.

### Remaining escape hatch

`CanonicalLibraryPackage` is public and exposes:

```text
public static CanonicalLibraryPackage Create(LibraryDocument document)
```

`LibraryRepository` exposes public synchronization that accepts such a package.

Therefore a caller can:

1. create any valid `LibraryDocument`;
2. wrap it in a public canonical package;
3. synchronize backup independently of the actual current primary.

The current CRUU13 regression test checks only that the old `LibraryDocument` overload no longer exists. It does not prove the package is primary-bound.

### Required fix

The backup API must require authority derived from the actual primary.

Options:

- make `CanonicalLibraryPackage.Create` internal/private to repository mutation infrastructure;
- make arbitrary canonical packages non-authoritative for backup synchronization;
- accept a `HealthyLibraryPackage` / `LibraryPrimarySnapshot` carrying exact primary hash/file identity;
- make backup sync internal and callable only after successful primary read/commit.

### Required regression tests

- `CRUU14_009_Public_API_cannot_author_backup_without_primary_bound_authority`
- `CRUU14_009_Backup_sync_rejects_package_not_bound_to_current_primary`
- `CRUU14_009_Current_primary_package_can_synchronize_backup_exactly`

---

## CRUU14-010 — MED-HIGH
## Startup temp reconciliation still equates a recognizable filename with ownership

### Affected code

- `src/PromptHelper/Services/DataRootTempReconciler.cs`
- `src/PromptHelper/Services/DurableTempReconciler.cs`
- `src/PromptHelper/Services/SettingsTempReconciler.cs`

### Observed behavior

The reconcilers recognize names such as:

```text
.prompthelper-tmp-<class>-<guid>.tmp
```

and several legacy formats.

Matching files are then deleted by current pathname when no active journal is considered to own them.

### Problem

A syntactically valid Prompt Helper temp filename is not ownership evidence.

A foreign file can occupy a matching pathname after:

- a crash,
- staging-file replacement,
- manual file creation,
- unrelated interference.

The current startup cleanup can delete it merely because its name parses.

This is particularly inconsistent with the stronger ownership model the migration recovery code is attempting to establish.

### Required fix

Do not automatically destroy an orphan merely because its filename matches an app pattern.

Use one of:

- durable owner journals containing the exact temp identity;
- a separate durable temp registry;
- creation-bound file IDs;
- quarantine/rename-to-safe-review behavior for unverifiable legacy candidates.

For legacy temp formats lacking ownership metadata, prefer preservation/quarantine over deletion unless there is a formally justified migration policy.

### Required regression tests

- `CRUU14_010_Startup_temp_reconciler_preserves_unowned_matching_regular_file`
- `CRUU14_010_Startup_recovery_temp_pattern_is_not_ownership_proof`
- `CRUU14_010_Legacy_temp_without_provenance_is_preserved_or_quarantined`
- `CRUU14_010_Owned_recorded_temp_is_removed_safely`

---

## CRUU14-011 — HIGH
## Regression evidence gate remains incomplete and several “sentinels” do not exercise the claimed behavior

### Affected code

- `tools/RequiredRegressionTests.psd1`
- `tools/VerifyTestEvidence.ps1`
- `.github/workflows/windows-ci.yml`
- `tests/PromptHelper.Tests/Cruu11ComprehensiveVerificationTests.cs`
- `tests/PromptHelper.Tests/Cruu12ComprehensiveVerificationTests.cs`
- `tests/PromptHelper.Tests/Cruu13ComprehensiveVerificationTests.cs`
- `tests/PromptHelper.Tests/IconAssetTests.cs`

### Problem A — CRUU13 tests are not required evidence

`RequiredRegressionTests.psd1` currently contains CRUU11 and selected CRUU12 names.

It contains **no CRUU13 sentinel names**.

Therefore the exact-name evidence gate can pass even if every CRUU13 regression test disappears or is renamed.

### Problem B — earlier CRUU12 required coverage is still incomplete

The previous audit explicitly identified missing CRUU12 IDs including:

- 005
- 007
- 009
- 010
- 017
- 019
- 022
- 030
- 033
- 034

The current required manifest still does not provide complete required evidence for that set.

### Problem C — confirmed semantic false positives remain

Examples from the current source:

#### `CRUU11_001_Buffer_resize_retries_when_API_returns_required_size`

The test opens a normal file and calls the helper. It does not force the first API call to return a larger required buffer and does not assert a retry count.

#### `CRUU11_001_Reparse_artifact_is_rejected_before_deletion`

The test creates a regular file and successfully deletes it. It does not create a reparse point.

#### `CRUU13_019_CommitIfPrimaryUnchanged_rejects_stale_precondition`

The external modification occurs **before** `CommitIfPrimaryUnchanged` is called. It verifies stale-input rejection but not the remaining verify→commit race.

#### `CRUU13_019_Edit_rejects_body_changed_externally_between_read_and_replace`

The injected external change occurs early enough for the added re-read to notice it. It does not inject after the final re-read and before replacement.

#### `CRUU13_001_Fatal_mutation_exception_requests_shutdown...`

The test directly invokes the fatal handler. It does not drive a real Create/Edit/Delete/Duplicate UI handler through the catch ordering that originally caused the defect.

#### `CRUU13_002_Body_only_edit_postcommit_failure...`

One assertion contains `|| true`, making that assertion tautological. Other assertions in the test still provide value, but this should not exist in an acceptance sentinel.

### Problem D — release test passes because of a comment

`IconAssetTests.CRUU4_012_Release_asset_script_supports_published_exe_icon_check` searches the release script for the text:

`ExtractIconEx`

The current script contains that phrase in a comment stating that the old `ExtractIconEx` check was superseded. Thus the test can pass from a comment rather than behavior.

### Problem E — icon test asserts the obsolete generator design

`GenerateAppIcon_script_exists_and_contains_square_padding_and_validation` still asserts presence of ImageMagick:

`auto-resize=256,128,64,48,32,24,16`

That is the exact single-raster/downsample pattern the later native generator was created to replace.

### Consequence

A green suite is not sufficient evidence of closure when required sentinels are absent or do not trigger the vulnerable cut point.

### Required fix

1. Build a canonical required-test registry containing **all** CRUU12, CRUU13, and CRUU14 acceptance sentinels.
2. Remove source-substring “tests” for behaviors that can be executed.
3. Add explicit fault-injection barriers at:
   - after staging flush/before promotion;
   - after CAS verification/before replace;
   - after journal validation/before retire;
   - after final body read/before replace.
4. Create actual Windows reparse points for reparse tests.
5. Abstract the Windows final-path API enough to deterministically force the buffer-too-small retry path and assert call count.
6. Execute `VerifyTestEvidence.ps1` against synthetic TRX fixtures:
   - exact required test -> pass;
   - substring-only test -> fail;
   - missing test -> fail;
   - failed required execution -> fail.
7. Remove tautologies and comment/string-only acceptance checks.
8. Make the evidence manifest itself tested and versioned.

### Required regression tests

- `CRUU14_011_Required_manifest_contains_all_CRUU12_CRUU13_CRUU14_sentinels`
- `CRUU14_011_Evidence_script_is_executed_against_substring_only_TRX_and_fails`
- `CRUU14_011_Evidence_script_is_executed_against_missing_required_TRX_and_fails`
- `CRUU14_011_Reparse_sentinel_creates_actual_reparse_point`
- `CRUU14_011_Buffer_resize_sentinel_forces_two_call_resize_path`
- `CRUU14_011_UI_fatal_sentinel_drives_real_CRUD_catch_order`
- `CRUU14_011_No_required_acceptance_assertion_contains_tautological_true_clause`

---

## CRUU14-012 — MED / RELEASE GAP
## Icon source→ICO→EXE identity chain is still not canonical and fully enforced

### Affected code

- `tools/GenerateAppIcon.ps1`
- `tools/GenerateAppIconNative.js`
- `tools/VerifyReleaseAssets.ps1`
- `tools/IconIdentityVerifier/PeIconResourceReader.cs`
- `tests/PromptHelper.Tests/IconAssetTests.cs`
- `.github/workflows/windows-ci.yml`

### Improvements

The real source SVG now exists.

The checked-in ICO now exists.

`GenerateAppIconNative.js` independently renders:

- 16
- 24
- 32
- 48
- 64
- 128
- 256

The project embeds the ICO.

### Remaining gaps

#### 1. Two competing generation paths

`GenerateAppIcon.ps1` still rasterizes at 256 and uses ImageMagick `icon:auto-resize`.

`GenerateAppIconNative.js` is the new per-size path.

There is no single canonical generator enforced by CI.

#### 2. Native generator is not part of the CI reproducibility gate

Windows CI validates the checked-in ICO, but does not regenerate the icon from the approved SVG using the canonical generator and compare the result/normalized pixels.

#### 3. No approved artwork identity manifest

`VerifyReleaseAssets.ps1` does not bind:

- approved SVG SHA-256;
- normalized RGBA hash for each required raster size.

Checking that compressed ICO frame byte blobs differ is not equivalent to proving they are approved native renders of the committed SVG.

#### 4. PE reader stops at first `RT_GROUP_ICON`

`PeIconResourceReader` enumerates group-icon resource names but deliberately returns `false` after the first group.

The verifier therefore validates only one group.

#### 5. Strict release gate is opt-in

The strict published-EXE release check runs only when manually dispatching with `release_gate=true`.

It is not a required ordinary push/release invariant.

### Required fix

1. Choose **one** canonical icon generator.
2. Render every required size directly from SVG.
3. Add an approval manifest containing:
   - SHA-256 of approved source SVG;
   - expected dimensions;
   - normalized RGBA SHA-256 for every required size.
4. CI regenerates into a temporary artifact and verifies normalized raster identity.
5. Compare checked-in ICO frames against the same normalized hashes.
6. Enumerate and validate all relevant `RT_GROUP_ICON` resources in the published EXE.
7. Make strict release-asset validation mandatory on the actual release/tag workflow.
8. Replace tests that assert the obsolete ImageMagick auto-resize path.

Do not invent an approval hash. Generate and review the manifest from the committed approved artwork, then lock it deliberately.

### Required regression tests

- `CRUU14_012_Approved_SVG_hash_matches_manifest`
- `CRUU14_012_Each_native_frame_matches_approved_normalized_RGBA_hash`
- `CRUU14_012_Checked_in_ICO_matches_approved_normalized_RGBA_hashes`
- `CRUU14_012_All_EXE_RT_GROUP_ICON_resources_match_approved_frames`
- `CRUU14_012_Canonical_generator_renders_each_size_from_vector_source`
- `CRUU14_012_Release_pipeline_requires_strict_icon_identity_gate`

---

# 6. Cross-cutting architectural diagnosis

The remaining findings are not twelve unrelated mistakes.

They largely come from four recurring authority-model problems.

## 6.1 Path authority is being used after object authority is lost

Examples:

- staging file closed before path rename;
- journal validated then path-deleted;
- temp recognized by pathname then deleted;
- body checked then separately replaced.

The correct invariant should be:

> Once a file object becomes transaction-authoritative, every destructive or point-of-no-return operation must remain bound to that exact object until the operation terminates.

## 6.2 Hash preconditions are treated as CAS but are not write-bound

A hash comparison can prove what existed at one instant.

It does not create an atomic compare-and-swap unless the verified state is bound to the actual replacement.

## 6.3 “Managed-looking path” is still sometimes treated as “owned object”

Random GUIDs and strict naming rules are useful collision avoidance and classification mechanisms.

They are not proof of ownership after a crash or replacement.

## 6.4 Test names are sometimes stronger than the test behavior

A test named “reparse rejected” must create a reparse point.

A test named “buffer resize retries” must force and observe a retry.

A test named “race between read and replace” must inject at that exact race cut.

Acceptance should be based on behavior, not the semantic promise of the method name.

---

# 7. Mandatory implementation plan

This plan intentionally fixes primitives before callers.

Do **not** patch each reported call site independently with another read/check. That would reproduce the same class of race.

---

## PHASE 01 — Build a same-object durable staging/promotion primitive
### Covers CRUU14-001 and foundation for 002/003/004/005

### Implement

Create a Windows-specific authoritative staging object abstraction.

Suggested conceptual responsibilities:

```text
OwnedDurableStage
- exact staging path
- retained SafeFileHandle
- volume/file ID
- non-reparse proof
- final physical path proof
- expected byte length/hash after flush
- PromoteReplaceExact(...)
- PromoteNoOverwriteExact(...)
- DeleteExact()
```

### Requirements

- CreateNew semantics.
- Retain handle from creation through promotion.
- Durable flush.
- No pathname-only promotion after handle close.
- No pathname-only rollback cleanup.
- Correct error propagation.
- Unit-testable low-level Win32 adapter.

### Expected result

There is exactly one reviewed primitive responsible for durable promotion of owned staging objects.

---

## PHASE 02 — Build true handle-bound CAS for primary metadata, settings, and prompt bodies
### Covers CRUU14-002 and CRUU14-003

### Implement

Create expected-current-file authority:

```text
ExpectedFileAuthority
- path
- volume/file ID
- SHA-256
- length
- final physical path
```

Provide:

```text
ReplaceIfCurrentMatches(expectedAuthority, candidateBytes)
```

The expected object must be checked and held/bound until atomic replacement.

### Apply to

- `library.json`
- `settings.json`
- settings backup if used in transition precondition
- prompt body edit

### Expected result

No mutation uses “verify hash; later replace path” as its concurrency contract.

---

## PHASE 03 — Route every user mutation through the same CAS transaction model
### Covers remaining CRUU14-002 call-site bypasses

### Convert

- CreateCategory
- RenameCategory
- DeleteCategory
- MovePrompt
- CreatePrompt
- EditPrompt
- DeletePrompt
- body-missing DeletePrompt branch
- DuplicatePrompt

### Required invariant

Every operation starts from a disk-bound primary snapshot, not solely an in-memory document.

Every commit either:

- atomically replaces the exact expected version; or
- aborts without overwriting external bytes.

### Expected result

The service layer has no direct unguarded metadata commit for ordinary mutations.

---

## PHASE 04 — Replace deletion-by-current-path with creation/transaction-bound ownership
### Covers CRUU14-004, CRUU14-005, CRUU14-010 foundations

### Implement

For every transaction-owned artifact define durable ownership proof.

Examples:

- mutation recovery artifact;
- migration payload temp;
- migration capability probe;
- migration stage;
- mutation journal;
- migration marker;
- initialization journal.

### Remove/restrict

- identity-only “delete current regular file” for transaction cleanup;
- parameterless journal retirement;
- cleanup that depends solely on parseable filename.

### Journal retirement

Read/validate/delete the same handle.

### Expected result

A file that replaces a transaction-owned object cannot be deleted merely because it occupies the old pathname.

---

## PHASE 05 — Finish strict physical filesystem authority
### Covers CRUU14-006 and CRUU14-008

### Implement

1. strict file opener:
   - OPEN_REPARSE_POINT;
   - file type check;
   - reparse rejection;
   - final handle path;
   - strict descendant proof;
   - file ID.

2. strict directory inventory enumerator:
   - verified root handle;
   - fail-closed errors;
   - explicit typed entries.

3. update:
   - ExistingTargetCommitLease;
   - MigrationPayloadCommitLease;
   - MigrationTargetInventoryInspector.

### Expected result

Both directory topology and every commit-authoritative file are physically bound to the intended root.

---

## PHASE 06 — Complete strict UTF-8 target validation
### Covers CRUU14-007

### Implement

- strict-decode active prompt bodies on each target content pass;
- strict-decode again from the final held commit-lease handle;
- preserve exact error context.

### Expected result

No target can be accepted if any active prompt body violates the app’s own strict UTF-8 persistence contract.

---

## PHASE 07 — Close backup authority API
### Covers CRUU14-009

### Implement

Choose one:

- internal package factory + internal backup sync; or
- primary-bound snapshot token required by backup sync.

### Prohibit

Public caller ability to create arbitrary valid document bytes and independently publish them to backup.

### Expected result

Backup is always a synchronization artifact of proven primary state, never an independent second source.

---

## PHASE 08 — Redesign stale-temp reconciliation
### Covers CRUU14-010

### Implement

Classify startup candidates:

```text
ProvenOwned
JournalOwned
LegacyUnverifiable
Foreign
```

Policy:

- ProvenOwned -> safe exact cleanup.
- JournalOwned -> transaction recovery owns it.
- LegacyUnverifiable -> preserve/quarantine unless a formally justified migration rule proves safety.
- Foreign -> preserve.

### Expected result

Startup maintenance cannot destroy an arbitrary regular file solely because its filename resembles a Prompt Helper temp.

---

## PHASE 09 — Rebuild regression evidence as behavioral acceptance
### Covers CRUU14-011

### Implement

1. Add every missing CRUU12 sentinel.
2. Add all CRUU13 sentinels.
3. Add all CRUU14 sentinels from this report.
4. Rewrite false-positive CRUU11/12/13 tests.
5. Add deterministic race barriers.
6. Add actual Windows reparse integration tests.
7. Test `VerifyTestEvidence.ps1` by running it on synthetic TRX.
8. Remove tests that only inspect comments/source strings when behavior is executable.
9. Remove tautological assertions.
10. Make required-sentinel manifest integrity itself a test.

### Expected result

A required finding cannot be marked covered unless the executable test actually reaches the relevant failure cut.

---

## PHASE 10 — Canonicalize release icon identity
### Covers CRUU14-012

### Implement

- one canonical native per-size SVG renderer;
- approval manifest;
- SVG SHA;
- normalized RGBA hashes;
- checked-in ICO comparison;
- all PE icon groups;
- mandatory release workflow gate.

### Expected result

Release identity forms one reproducible chain:

```text
approved SVG
  -> approved native raster frames
  -> checked-in ICO
  -> published EXE icon resources
```

---

## PHASE 11 — Independent Windows acceptance matrix

After source fixes, run on Windows:

### Required commands/gates

1. clean restore
2. Release build
3. each critical test category
4. full suite
5. full suite **five consecutive times**
6. exact required-sentinel verification
7. self-contained `win-x64` publish
8. strict release asset verification against published EXE
9. startup/mutation/migration fault matrix
10. fresh independent source audit at the exact tested commit

### Evidence to retain

- exact commit SHA;
- toolchain versions;
- all TRX files;
- sentinel verifier output;
- publish hash list;
- icon approval verifier output;
- release artifact hashes.

---

# 8. Required CRUU14 sentinel list

At minimum, add these exact behavioral tests to `RequiredRegressionTests.psd1` after implementation.

```text
CRUU14_001_Durable_writer_replaced_stage_is_never_promoted
CRUU14_001_Settings_writer_replaced_stage_is_never_promoted
CRUU14_001_Failure_cleanup_preserves_replaced_stage
CRUU14_001_Mutation_control_stage_replacement_never_promotes_foreign_bytes
CRUU14_001_Initialization_control_stage_replacement_never_promotes_foreign_bytes
CRUU14_001_Recovery_artifact_stage_replacement_never_promotes_foreign_bytes

CRUU14_002_Primary_changes_after_verify_before_commit_aborts_without_overwrite
CRUU14_002_Category_create_external_primary_race_preserves_foreign_bytes
CRUU14_002_Category_rename_external_primary_race_preserves_foreign_bytes
CRUU14_002_Category_delete_external_primary_race_preserves_foreign_bytes
CRUU14_002_Move_prompt_external_primary_race_preserves_foreign_bytes
CRUU14_002_Missing_body_delete_external_primary_race_preserves_foreign_bytes
CRUU14_002_Edit_body_changes_after_last_read_before_replace_is_preserved
CRUU14_002_Body_only_edit_primary_changes_after_last_check_is_preserved

CRUU14_003_Settings_change_after_CAS_check_before_promotion_is_preserved
CRUU14_003_Settings_backup_change_after_CAS_check_is_preserved
CRUU14_003_Data_folder_transition_does_not_commit_over_external_settings_change

CRUU14_004_Recovery_partial_temp_replaced_by_foreign_regular_file_is_preserved
CRUU14_004_Capability_probe_current_replaced_after_create_is_preserved_end_to_end
CRUU14_004_Control_artifact_same_path_foreign_regular_file_is_preserved
CRUU14_004_Identity_only_current_object_is_not_accepted_as_ownership_proof

CRUU14_005_Migration_marker_replaced_between_validate_and_retire_is_preserved
CRUU14_005_Mutation_journal_replaced_between_validate_and_retire_is_preserved
CRUU14_005_Initialization_journal_replaced_between_validate_and_retire_is_preserved
CRUU14_005_Stage_file_replaced_before_cleanup_is_preserved

CRUU14_006_Inventory_access_denied_is_not_empty_or_missing
CRUU14_006_Inventory_sharing_violation_fails_closed
CRUU14_006_Inventory_reparse_directory_fails_closed
CRUU14_006_Inventory_reparse_file_fails_closed
CRUU14_006_Inventory_object_swap_during_enumeration_fails_closed

CRUU14_007_Existing_target_UTF16_prompt_body_is_rejected
CRUU14_007_Existing_target_invalid_UTF8_prompt_body_is_rejected
CRUU14_007_Final_existing_target_commit_lease_revalidates_body_UTF8

CRUU14_008_Existing_target_prompt_file_symlink_is_rejected
CRUU14_008_Existing_target_metadata_symlink_is_rejected
CRUU14_008_Migration_commit_lease_reparse_file_is_rejected
CRUU14_008_Lease_final_handle_path_must_be_under_expected_physical_root

CRUU14_009_Public_API_cannot_author_backup_without_primary_bound_authority
CRUU14_009_Backup_sync_rejects_package_not_bound_to_current_primary
CRUU14_009_Current_primary_package_can_synchronize_backup_exactly

CRUU14_010_Startup_temp_reconciler_preserves_unowned_matching_regular_file
CRUU14_010_Startup_recovery_temp_pattern_is_not_ownership_proof
CRUU14_010_Legacy_temp_without_provenance_is_preserved_or_quarantined
CRUU14_010_Owned_recorded_temp_is_removed_safely

CRUU14_011_Required_manifest_contains_all_CRUU12_CRUU13_CRUU14_sentinels
CRUU14_011_Evidence_script_is_executed_against_substring_only_TRX_and_fails
CRUU14_011_Evidence_script_is_executed_against_missing_required_TRX_and_fails
CRUU14_011_Reparse_sentinel_creates_actual_reparse_point
CRUU14_011_Buffer_resize_sentinel_forces_two_call_resize_path
CRUU14_011_UI_fatal_sentinel_drives_real_CRUD_catch_order
CRUU14_011_No_required_acceptance_assertion_contains_tautological_true_clause

CRUU14_012_Approved_SVG_hash_matches_manifest
CRUU14_012_Each_native_frame_matches_approved_normalized_RGBA_hash
CRUU14_012_Checked_in_ICO_matches_approved_normalized_RGBA_hashes
CRUU14_012_All_EXE_RT_GROUP_ICON_resources_match_approved_frames
CRUU14_012_Canonical_generator_renders_each_size_from_vector_source
CRUU14_012_Release_pipeline_requires_strict_icon_identity_gate
```

---

# 9. Acceptance criteria after CRUU14 implementation

The next implementation must not be accepted merely because “all tests pass.”

Acceptance requires all of the following:

```text
A. No open CRUU14 source finding.
B. No reopened CRUU13 finding.
C. All required CRUU12 sentinels present and behavioral.
D. All required CRUU13 sentinels present and behavioral.
E. All required CRUU14 sentinels present and behavioral.
F. Exact-name evidence verifier run against real TRX.
G. Full Windows test suite passes 5 consecutive runs.
H. No required test is skipped.
I. No release test treats missing approved assets as success.
J. Clean self-contained win-x64 publish succeeds.
K. Strict source SVG -> native frames -> ICO -> all relevant EXE icon groups verification succeeds.
L. Race tests use deterministic synchronization barriers at the exact vulnerable cutpoint.
M. Reparse tests create actual reparse points.
N. No destructive recovery path relies solely on a managed-looking pathname.
O. No CAS path consists only of check-now / path-replace-later.
P. Current tested commit has retained, reviewable CI/TRX evidence.
Q. Final independent source re-audit finds zero defects.
```

Only then is the appropriate conclusion:

```text
STRICT_RELEASE_READY = YES
ZERO_DEFECT_VERIFIED = YES
```

---

# 10. Implementation guidance for a weaker coding model

The next implementer should follow these rules strictly.

## Do not solve races with one more pre-check

Bad pattern:

```text
hash file
if expected:
    later replace path
```

Still vulnerable.

The fix must bind validation and mutation to the same object/transaction authority.

## Do not use filename randomness as proof of ownership

GUID temp names reduce accidental collision. They do not prove that the object currently at the pathname is the one the application created.

## Do not add “best effort” deletion to safety-critical recovery

If safe ownership cannot be proven:

- preserve;
- quarantine;
- report.

Do not delete.

## Do not duplicate filesystem authority logic

Create a small number of reusable primitives and make higher-level services depend on them.

Suggested primitives:

```text
StrictWindowsFileAuthority
OwnedDurableStage
ExpectedFileAuthority
AtomicCasReplacer
TransactionOwnedArtifact
StrictDirectoryInventory
```

## Do not weaken fault injection for convenience

Tests must place barriers at exact transitions.

For example:

```text
1. Writer flushes stage.
2. TEST BARRIER fires.
3. Test process replaces stage pathname.
4. Writer resumes.
5. Expected: operation refuses to promote replacement.
```

A test that changes the file before step 1 does not validate this race.

---

# 11. Final assessment

The CRUU13 implementation was not superficial. It closed a substantial number of real issues:

- fatal postcommit UI handling;
- body-only journal semantics;
- v3 payload fingerprinting;
- bootstrap target classification;
- strict migration metadata UTF-8;
- startup diagnostic aggregation;
- structured initialization journal;
- mutation journal schema/revision;
- real icon asset presence.

The remaining defects are more architectural and therefore easier to miss with ordinary functional tests.

The principal unresolved requirement is to move from:

> **path + pre-check authority**

to:

> **same-object / handle-bound authority through the destructive or point-of-no-return operation.**

Until that is done, the repository should not be declared zero-defect or strict-release-ready.

---

## Final status

```text
AUDITED_HEAD                      = 931502ebcda6c0bb73b3e239e725a9dea0cffc29
SOURCE_AUDIT_CLEAN                = NO
CRUU13_ALL_FINDINGS_FIXED         = NO
CRUU14_FINDINGS                   = 12
CRITICAL                          = 1
HIGH                              = 6
MED_HIGH                          = 4
MED_RELEASE_GAP                   = 1
WINDOWS_RUNTIME_VALIDATION        = NOT_INDEPENDENTLY_VERIFIED
STRICT_RELEASE_READY              = NO
ZERO_DEFECT_VERIFIED              = NO
```
