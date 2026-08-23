# CRUU19 — Independent Post-CRUU18 Adversarial Re-Audit and Fix Plan

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited HEAD:** `d7e7b4d8a1360f46de0a51d8caa0cda51f3c1d60`  
**Parent / CRUU18 audit snapshot:** `e95bc2be7bfaf65c4ad076a39f3e42535b29a64f`  
**Repair commit:** `d7e7b4d8a1360f46de0a51d8caa0cda51f3c1d60` — `fix(recovery): close CRUU18 durability gaps`  
**Previous audit:** `cruu18.md`  
**Audit date:** 2026-08-23  
**Mode:** independent source, crash-cut, ownership-authority, migration-retry, probe-lifecycle, directory-identity, caller-propagation and evidence-gate re-audit.

> Report only. No production source or GitHub repository content was modified by this audit.

---

# 1. Executive verdict

The CRUU18 repair is another **substantial improvement**.

The major CRUU18 findings were not merely renamed or hidden; most of their production fixes are actually present:

- current migration retry now understands `MigrationArtifact.RestoreRelativePath`;
- final deletion requires exact file identity plus expected manifest content;
- successful CAS rollback from `PreimageSidelined` is recognized from exact old-object identity;
- category Create/Rename/Delete now share the committed-restart boundary with prompt mutations;
- postcommit migration ownership-retirement failure keeps the Ready marker for startup retry;
- post-publication migration bookkeeping failure retains `FinalOwned`;
- `FinalOwned -> TempAbandoned` is illegal;
- CAS and payload stage factories exact-delete a just-created stage if the initial ownership claim fails;
- runtime production-symbol hooks were introduced for CRUU18 acceptance.

However, the repository is **still not zero-defect**.

This audit found **4 actionable findings**:

```text
HIGH      = 1
MED-HIGH  = 3
TOTAL     = 4
```

The most important new/reopened defect is a real foreign-data destruction path in the capability probe lifecycle:

> The validator creates probe files, closes their creation handles, and later uses path-based `File.Replace` / `File.Delete`. Its failure cleanup named `VerifyIdentityAndDelete` does not compare against any identity captured at creation, and retry recovery authorizes probe deletion from pathname + tiny fixed content only.

So another same-user process can substitute a foreign file at a probe pathname and Prompt Helper can replace or delete that foreign object.

There are also three narrower but still release-blocking problems:

1. `DefaultMigrationManifestFileOps.CreateOwnedStage` was missed by the CRUU18 ownership-bootstrap repair; an initial ownership-journal failure can leave an unproven Ready-manifest stage that later retry deliberately refuses to delete.
2. Attempt-created directory rollback/recovery remains path-only ownership. The exact retained handle protects the delete operation from TOCTOU, but it does not prove the current empty directory is the directory the migration originally created.
3. The new runtime-evidence helper does not verify a production hit when the tested action throws. Several high-risk tests wrap it inside `Assert.ThrowsExactly`, so the test can pass without the runtime-hit assertion ever executing.

The repair trajectory is good, but strict release acceptance remains blocked.

---

# 2. Audit freeze and execution evidence

`main` was fetched at the beginning and re-fetched immediately before report generation.

Both reads returned:

```text
d7e7b4d8a1360f46de0a51d8caa0cda51f3c1d60
```

Status:

```text
AUDITED_HEAD                         = d7e7b4d8a1360f46de0a51d8caa0cda51f3c1d60
HEAD_STABLE_DURING_AUDIT             = YES

CRUU19_FINDINGS                      = 4
HIGH                                 = 1
MED_HIGH                             = 3

SOURCE_AUDIT_CLEAN                   = NO
CRUU18_STRICTLY_CLOSED               = NO

WINDOWS_TESTS_DIRECTLY_EXECUTED      = NO
LOCAL_DOTNET                         = NOT AVAILABLE
LOCAL_PWSH                           = NOT AVAILABLE
LOCAL_WINDOWS_RUNTIME                = NOT AVAILABLE

GITHUB_COMBINED_STATUS_FOR_HEAD      = NO STATUSES EXPOSED
GITHUB_PR_WORKFLOW_LOOKUP_FOR_HEAD   = NO RUNS EXPOSED

STRICT_RELEASE_READY                 = NO
ZERO_DEFECT_VERIFIED                 = NO
```

The available GitHub status interface returned no attached statuses for the exact SHA, and the available commit workflow lookup returned no pull-request-triggered runs. This does **not** prove that a push workflow did not run; it means this audit cannot independently bind a successful Windows execution to this exact SHA through the available interfaces.

---

# 3. CRUU18 closure matrix

| CRUU18 finding | CRUU19 status | Assessment |
|---|---|---|
| **CRUU18-001** | **FIXED_SOURCE_CORE** | Retry final cleanup now accepts current `MigrationArtifact.RestoreRelativePath` or legacy `MigrationFinal.RelativePath`, requires exact identity, and verifies manifest length/hash before same-handle deletion. |
| **CRUU18-002** | **FIXED_SOURCE_CORE** | Recovery now treats `Prepared` **or** `PreimageSidelined` + exact old target identity + no owned preimage as a completed rollback. Real promotion-failure injection was added. |
| **CRUU18-003** | **FIXED_SOURCE_CORE** | Category and prompt mutations share `ExecuteLibraryMutation`; committed-restart exceptions are caught before ordinary `IOException`, fatal flag is set, and shutdown is requested. |
| **CRUU18-004** | **FIXED_SOURCE_CORE** | The Ready marker is deleted only when committed migration ownership retirement succeeds; startup finalization retries retirement before marker deletion. |
| **CRUU18-005** | **FIXED_SOURCE_CORE** | Post-publication record failure leaves `FinalOwned`; state transitions are guarded; `DeleteExact` after publication now throws rather than pretending to delete the final. |
| **CRUU18-006** | **PARTIAL / REOPENED** | CAS and payload stage factories exact-clean failed first claims, but the Ready-manifest stage factory still only `Dispose()`s. See CRUU19-002. |
| **CRUU18-007** | **PARTIAL / REOPENED** | Production-symbol authority exists, but expected-exception tests can skip the actual hit assertion, and CRUU18-006's symbol authority omitted the manifest-stage factory. See CRUU19-004. |

---

# 4. Findings

---

## CRUU19-001 — HIGH
## Capability probe lifecycle loses creation identity and can replace/delete foreign files

### Affected code

- `src/PromptHelper/Services/DataRootCapabilityValidator.cs`
- `src/PromptHelper/Services/ICapabilityFileOps.cs`
- `src/PromptHelper/Services/IVerifiedArtifactDeleter.cs`
- `src/PromptHelper/Services/MigrationRecoveryService.cs`
- `src/PromptHelper/Services/MigrationManifestBuilder.cs`
- `tests/PromptHelper.Tests/Cruu12ComprehensiveVerificationTests.cs`
- `tests/PromptHelper.Tests/Cruu13ComprehensiveVerificationTests.cs`

### Why this matters

The probe exists specifically to verify that a selected data root safely supports create/replace/delete operations.

A safety probe must never make foreign data less safe than the operation it is testing.

Today it can.

---

## 4.1 Live successful probe: handles are closed, then replace/delete are pathname operations

`ProbeLocationWithPlan` does:

```text
CreateNew(currentFile)
write "create"
flush
CLOSE

CreateNew(replacementFile)
write "replace"
flush
CLOSE

File.Replace(replacementFile, currentFile, null)
File.Delete(currentFile)
```

The default capability implementation confirms:

```csharp
public void Replace(...) =>
    File.Replace(sourceFileName, destinationFileName, destinationBackupFileName);

public void DeleteFile(string path)
{
    if (_authority.Probe(path).Kind == StrictPathKind.File)
        File.Delete(path);
}
```

The creation handles no longer exist when replace/delete happens.

The path is therefore not bound to the object this invocation created.

### Destructive substitution sequence A — destination replacement

```text
T1 Prompt Helper creates current probe A
T2 closes A
T3 attacker / concurrent same-user process removes A
T4 attacker puts foreign file F at current probe pathname
T5 Prompt Helper executes File.Replace(replacement, current, null)
T6 F is replaced/destroyed
```

### Destructive substitution sequence B — final delete

```text
T1 Prompt Helper completes File.Replace
T2 current pathname contains the probe object it intends to delete
T3 before File.Delete, another process substitutes foreign file F
T4 StrictPathAuthority says "regular file"
T5 File.Delete(path) deletes F
```

The strict path probe proves only type and path safety.

It does not prove ownership.

---

## 4.2 Failure cleanup: `VerifyIdentityAndDelete` has no expected identity

The failure branch comments say:

```text
Identity-verified cleanup
```

but the API is:

```csharp
VerifyIdentityAndDelete(string physicalRoot, string path)
```

There is no expected file identity parameter.

`WindowsVerifiedArtifactDeleter.VerifyIdentityAndDelete`:

1. opens the **current** object at the path;
2. rejects reparse points;
3. proves physical containment;
4. deletes that same current object.

That is same-object deletion, not creation-identity deletion.

If a foreign regular file has replaced the probe, the helper faithfully deletes the foreign file.

---

## 4.3 Retry recovery: pathname + tiny fixed content is treated as ownership

The migration manifest records capability probe controls with:

```text
ExpectedLength
ExpectedSha256Hex
```

for literal payloads:

```text
"create"
"replace"
```

Retry recovery calls:

```text
_verifiedDeleter.VerifyAndDelete(
    root,
    controlPath,
    expectedLength,
    expectedHash)
```

That verifies content and same-handle deletion, but it still has no creation identity.

A foreign file containing exactly `"create"` or `"replace"` at the declared path satisfies the deletion authority.

Content equality is not ownership — the repository already enforces that principle for migration finals.

---

## 4.4 Existing sentinels do not test the real unsafe paths

`CRUU12_027_Probe_current_replaced_after_creation_is_preserved` does **not** execute `DataRootCapabilityValidator`.

It directly creates a file and calls:

```csharp
VerifyAndDelete(
    wrongExpectedLength,
    wrongExpectedHash)
```

The file is preserved because its bytes do not match.

That does not test a same-content replacement and does not test `VerifyIdentityAndDelete`.

`CRUU13_004_Foreign_content_at_declared_probe_control_path_is_not_deleted` executes retry recovery, but deliberately uses content that **does not** match `"create"`.

Again, it proves only that a wrong-content file is preserved.

Neither sentinel asks the ownership question:

```text
same pathname
same expected bytes
different filesystem identity
```

---

## Required architectural fix

The probe needs one object-authority model from creation through retirement.

Recommended design:

```text
OwnedCapabilityProbe
    Current:
        retained creation handle
        WindowsFileIdentity
        expected bytes
    Replacement:
        retained creation handle
        WindowsFileIdentity
        expected bytes
```

For live operation:

```text
1. create current with retained owned handle
2. write + flush
3. create replacement with retained owned handle
4. write + flush
5. move exact current aside through retained handle
6. promote exact replacement into current name with no-overwrite
7. delete exact displaced current through retained handle
8. delete exact resulting probe through retained handle
```

Do **not** use `File.Replace` / `File.Delete` on reopened pathnames.

For migration probe crash recovery:

- durably record each probe's exact identity when created;
- either use `OwnedArtifactKind.Stage` or introduce a dedicated `CapabilityProbe` kind;
- the manifest's length/hash remains content authority;
- the ownership journal supplies object authority.

Retry deletion must require:

```text
declared control path
AND exact recorded file identity
AND expected length
AND expected SHA-256
AND strict physical-root containment
AND same retained handle for verification + deletion
```

A missing identity record must preserve and fail closed.

### Mandatory tests

```text
CRUU19_001_Live_probe_destination_same_content_foreign_replacement_is_not_replaced
CRUU19_001_Live_probe_source_same_content_foreign_replacement_is_not_promoted
CRUU19_001_Live_probe_before_final_delete_foreign_replacement_is_not_deleted
CRUU19_001_Probe_failure_cleanup_requires_creation_identity_not_current_path_identity
CRUU19_001_Retry_same_content_different_identity_probe_is_preserved
CRUU19_001_Retry_exact_owned_probe_identity_and_content_is_deleted
CRUU19_001_CRUU12_027_executes_real_DataRootCapabilityValidator_substitution_path
CRUU19_001_CRUU13_004_tests_same_content_different_identity_not_only_wrong_content
CRUU19_001_DefaultCapabilityFileOps_exposes_no_raw_path_replace_or_delete_for_owned_probe
```

At least the first five must use real Windows file identities.

---

## CRUU19-002 — MED-HIGH
## Ready-manifest stage factory still releases an unclaimed creation handle without exact cleanup

### Affected code

- `src/PromptHelper/Services/IMigrationManifestFileOps.cs`
- `src/PromptHelper/Services/MigrationManifestRepository.cs`
- `src/PromptHelper/Services/MigrationRecoveryService.cs`
- `tests/PromptHelper.Tests/Cruu18RegressionTests.cs`
- `tools/FindingCoverageMap.json`

### What CRUU18 fixed elsewhere

Both of these now do the right thing:

```text
WindowsAtomicExpectedFileReplacer stage claim
DefaultMigrationFileOps payload stage claim
```

Their pattern is:

```text
create exact stage
try durable ownership record
catch:
    DeleteExact() while creation handle is still retained
    then dispose
```

### The manifest stage factory was missed

`DefaultMigrationManifestFileOps.CreateOwnedStage` still does:

```csharp
var stage = new OwnedManifestStage(
    WindowsOwnedDurableStage.CreateNewUnderRoot(path, physicalRoot));

try
{
    DefaultMigrationFileOps.RecordStageOwnership(
        _ownedArtifacts,
        path,
        stage.IdentityToken);

    return stage;
}
catch
{
    stage.Dispose();
    throw;
}
```

No `DeleteExact()`.

So on first ownership-record failure:

```text
T1 .prompthelper-migration.stage-<attempt>.tmp is created
T2 exact creation handle exists
T3 ownership journal append/flush fails
T4 catch closes handle
T5 stage file remains
T6 no guaranteed durable ownership record exists
```

This is precisely the CRUU18-006 invariant that should have been eliminated.

---

## Why it can wedge migration recovery

`WriteReadyManifestDurable` uses this factory for the manifest phase-transition stage.

If the first claim fails:

```text
1. migration is still precommit;
2. transition rollback removes payload;
3. manifest marker remains while declared residue exists;
4. stage path remains;
5. later RecoverForRetry sees ManifestPhaseStaging;
6. it calls DeleteOwnedFileIfProven(stagePath);
7. if the ownership record did not land, the stage is PreservedUnproven;
8. retry aborts by design.
```

A transient provenance write failure can therefore leave a target that cannot self-recover.

This is safe against accidental deletion, but it is not a completed recovery design.

---

## Why the new CRUU18 test missed it

The test named:

```text
CRUU18_006_No_stage_factory_closes_unclaimed_creation_handle_without_exact_cleanup
```

only calls:

```text
CRUU18_006_CAS_stage_claim_failure...
CRUU18_006_Migration_CreateOwnedStage_claim_failure...
```

It never invokes:

```text
DefaultMigrationManifestFileOps.CreateOwnedStage
```

The new `requiredProductionSymbols` entry for CRUU18-006 likewise names only:

```text
WindowsAtomicExpectedFileReplacer.ReplaceIfExpected
DefaultMigrationFileOps.CreateOwnedStage
```

The manifest-stage factory is absent from the authority map.

---

## Required fix

Apply the same all-or-exact-cleanup pattern:

```csharp
stage = CreateNewUnderRoot(...)

try
{
    RecordStageOwnership(...)
    return stage;
}
catch (Exception recordFailure)
{
    try
    {
        stage.DeleteExact();
    }
    catch (Exception cleanupFailure)
    {
        stage.Dispose();
        throw CompositeManifestStageClaimException(
            recordFailure,
            cleanupFailure);
    }

    stage.Dispose();
    throw;
}
```

If the ownership append physically landed and only its flush/reporting failed:

```text
exact stage is still deleted;
stale journal record later resolves to Missing;
reconciler retires it safely.
```

Also add the factory to runtime evidence authority.

### Mandatory tests

```text
CRUU19_002_Manifest_stage_claim_failure_deletes_exact_stage_before_handle_release
CRUU19_002_Manifest_stage_postappend_failure_deletes_stage_and_reconciles_stale_record
CRUU19_002_Ready_manifest_stage_claim_failure_does_not_wedge_RecoverForRetry
CRUU19_002_CRUU18_006_all_stage_factories_test_includes_DefaultMigrationManifestFileOps
CRUU19_002_CRUU18_006_required_symbols_include_manifest_stage_factory
```

---

## CRUU19-003 — MED-HIGH
## Attempt-created directory deletion is same-object-safe but still not creation-identity-bound

### Affected code

- `src/PromptHelper/Services/IOwnedDirectoryCreator.cs`
- `src/PromptHelper/Services/IReservationFileOps.cs`
- `src/PromptHelper/Services/DataFolderMigrationService.cs`
- `src/PromptHelper/Services/MigrationRecoveryService.cs`
- `src/PromptHelper/Services/WindowsRetirableDirectory.cs`
- `tests/PromptHelper.Tests/Cruu16StartupAndProvenanceTests.cs`

### Current ownership bookkeeping

When migration creates a directory:

```csharp
DirectoryCreateOutcome outcome = TryCreateOwned(path);

if (CreatedByCaller)
    tx.TrackCreatedDirectory(path);
```

The transaction stores:

```text
List<string> _createdDirectories
```

Only pathnames.

No volume/file ID is captured.

No durable directory identity record exists for crash recovery.

---

## Current delete primitive

`WindowsRetirableDirectory` is good at a different property:

```text
open current directory once
reject reparse
prove physical containment
delete that same handle
kernel re-checks emptiness
```

This correctly closes:

```text
enumerate empty
→ path gets swapped
→ Directory.Delete deletes another object
```

But it does **not** answer:

```text
Is this current directory the object the migration created earlier?
```

---

## In-process substitution sequence

```text
T1 migration creates prompts directory D1
T2 records only path "...\prompts"
...
T3 rollback deletes its owned files
T4 D1 becomes empty
T5 concurrent process removes D1
T6 concurrent process creates different empty directory D2 at same path
T7 rollback opens current path
T8 D2 is a regular empty non-reparse directory under root
T9 DeleteExact removes D2
```

The operation is same-object from T7 to T9.

It is nevertheless the wrong object.

The same principle applies after a crash when retry uses baseline absence plus current pathname as authority for deleting attempt-created empty directories.

No file contents are destroyed because non-empty deletion fails, so severity is lower than CRUU19-001. But deleting a foreign directory object, including its metadata/ACL semantics, is still destructive behavior.

---

## Existing CRUU16 sentinel does not test this

`CRUU16_006_Inprocess_rollback_swapped_attempt_directory_is_never_deleted` replaces the tracked directory with a **file**.

The test passes because `DeleteDirectoryExact` rejects the type mismatch.

It never replaces:

```text
directory D1
with
different empty directory D2
```

which is the actual ownership question.

---

## Required fix

Create a directory ownership token.

For every attempt-created directory:

```text
1. CreateDirectoryW returns CreatedByCaller.
2. Immediately open the exact directory non-reparse under bound root.
3. Capture WindowsFileIdentity from that handle.
4. Store identity in the in-process transaction.
5. For crash recovery, durably record directory identity before destructive reliance.
```

Possible model:

```text
OwnedDirectoryClaim
    OperationId
    RelativePath
    WindowsFileIdentity
    Kind = MigrationDirectory
```

Rollback/retry:

```text
open current directory
verify exact identity == claim identity
verify non-reparse + root containment
DeleteExact on same handle
```

If identity differs:

```text
preserve
report unresolved / foreign replacement
```

Do not infer ownership merely from:

```text
baseline says absent
+
current path exists
```

after a crash.

### Mandatory tests

```text
CRUU19_003_Inprocess_rollback_same_path_different_empty_directory_is_preserved
CRUU19_003_Retry_same_path_different_empty_directory_is_preserved
CRUU19_003_Attempt_created_directory_records_WindowsFileIdentity
CRUU19_003_Exact_owned_empty_directory_identity_is_removed
CRUU19_003_Foreign_nonempty_directory_remains_preserved
CRUU19_003_CRUU16_006_swapped_directory_test_uses_directory_to_directory_identity_swap
```

---

## CRUU19-004 — MED-HIGH / VERIFICATION DEFECT
## Runtime production-hit evidence is skipped whenever the tested action throws

### Affected code

- `tests/PromptHelper.Tests/Cruu18RegressionTests.cs`
- `src/PromptHelper/Services/ProductionRuntimeEvidence.cs`
- `tools/FindingCoverageMap.json`
- `tools/VerifyFindingCoverage.ps1`

### Intended mechanism

Production methods call:

```csharp
ProductionRuntimeEvidence.Hit("Type.Method");
```

Tests use:

```csharp
AssertProductionHit(requiredSymbol, action)
```

The helper:

```csharp
T result = action();

Assert.IsTrue(
    hits.Contains(requiredSymbol));

return result;
```

This works only if `action()` returns normally.

---

## Expected-exception tests bypass the evidence assertion

Several high-risk tests intentionally test a failure path:

```csharp
Assert.ThrowsExactly<IOException>(() =>
    AssertProductionHit(
        "WindowsAtomicExpectedFileReplacer.ReplaceIfExpected",
        () => replacer.ReplaceIfExpected(...)));
```

Execution is:

```text
1. AssertProductionHit installs sink
2. action starts
3. action throws IOException
4. control jumps to AssertProductionHit finally
5. sink is restored
6. AssertProductionHit never reaches Assert.IsTrue(hits.Contains(...))
7. outer Assert.ThrowsExactly sees expected IOException
8. TEST PASSES
```

The runtime evidence assertion was never executed.

The same structure appears in CRUU18-006 stage-claim tests.

So an expected-failure sentinel can carry:

```text
[ProductionSymbolEvidence("X")]
```

and satisfy the exact test-name gate while never proving that `X` was hit.

---

## Why the static gate does not rescue this

`VerifyFindingCoverage.ps1` checks that every CRUU18 finding has one or more syntactically valid symbol strings.

`CRUU18_007_Required_test_name_cannot_substitute_a_different_integration_layer` checks that mapped tests carry matching `ProductionSymbolEvidenceAttribute` values.

Neither proves that the runtime hit assertion actually succeeded.

The runtime helper is the only enforcement layer, and it is bypassed on expected exceptions.

---

## This already contributed to CRUU19-002

For CRUU18-006 the authority map lists:

```text
WindowsAtomicExpectedFileReplacer.ReplaceIfExpected
DefaultMigrationFileOps.CreateOwnedStage
```

It omits:

```text
DefaultMigrationManifestFileOps.CreateOwnedStage
```

The test named:

```text
No_stage_factory_closes_unclaimed_creation_handle_without_exact_cleanup
```

therefore certified "all stage factories" while never touching the manifest factory.

This demonstrates why evidence authority must enumerate the actual production surfaces, not only a conceptual finding name.

---

## Required fix

Use an exception-safe evidence primitive.

Example:

```csharp
private static TException AssertProductionHitThrows<TException>(
    string requiredSymbol,
    Action action)
    where TException : Exception
{
    var hits = new HashSet<string>();

    using var evidence = ProductionRuntimeEvidence.Capture(hits);

    TException ex = Assert.ThrowsExactly<TException>(action);

    Assert.IsTrue(
        hits.Contains(requiredSymbol),
        $"Required production symbol was not hit: {requiredSymbol}");

    return ex;
}
```

Or implement the existing helper so hit validation runs in a `finally` **without masking the original test exception**.

A robust pattern is:

```text
execute action and capture exception/result
restore sink
assert required hit
then rethrow/corroborate captured exception
```

Do not put the hit assertion after an action that may throw.

For multi-surface findings, the coverage map must name **all required production symbols**.

### Mandatory tests

```text
CRUU19_004_AssertProductionHit_expected_exception_still_requires_runtime_hit
CRUU19_004_Expected_exception_without_hit_fails_evidence_test
CRUU19_004_Expected_exception_with_hit_passes_evidence_test
CRUU19_004_CRUU18_002_failure_sentinel_proves_CAS_runtime_hit
CRUU19_004_CRUU18_006_failure_sentinels_prove_all_stage_factory_runtime_hits
CRUU19_004_Required_symbol_map_detects_omitted_manifest_stage_factory
```

A useful meta-test should deliberately execute:

```text
action throws expected IOException
without calling ProductionRuntimeEvidence.Hit
```

and prove that the evidence assertion fails.

---

# 5. Positive rechecks that should remain closed

## 5.1 Migration final retry authority

Current final deletion is materially stronger:

```text
exact current Windows file identity
+
current/legacy ownership-record path interpretation
+
manifest length
+
manifest SHA-256
+
current record's own expected content for MigrationArtifact
+
same retained handle delete
```

This closes the CRUU18-001 core mismatch.

## 5.2 CAS runtime rollback recovery

Recovery now accepts an exact old target object for:

```text
Prepared
or
PreimageSidelined
```

when the pre-image path no longer holds the recorded old object.

This correctly resolves a runtime restore that succeeded after candidate promotion failed.

## 5.3 Category committed-restart boundary

All category mutations now use `ExecuteLibraryMutation`.

The specific committed exception is caught before generic persistence errors.

The fatal flag prevents a second mutation and shutdown is requested.

## 5.4 Ready marker finalization ordering

Postcommit ownership cleanup sets an `ownershipRetired` flag.

Marker deletion is conditional on that success.

Startup finalization still performs:

```text
verify finals
retire committed migration ownership
delete Ready marker
```

which is the correct retry dependency.

## 5.5 Post-publication migration state

`MarkFinalOwnedAfterMove` is guarded.

`MarkTempAbandoned` rejects `FinalOwned`.

The post-publication journal append lives outside the prepublication cleanup block.

This closes the state corruption identified in CRUU18-005.

## 5.6 CAS and payload first-claim cleanup

Both now exact-delete a just-created stage before releasing the creation handle if the initial ownership record fails.

CRUU19-002 is specifically the third factory that was missed.

---

# 6. Architectural diagnosis

CRUU19 has one repeated theme:

> **same-object safety is still being confused with creation ownership in a few older subsystems.**

The repository now has strong primitives for:

```text
exact file identity
retained handles
root containment
same-handle verification/deletion
durable ownership records
```

But the capability probe and directories do not consistently use them.

The distinction is:

```text
"this is the object currently at the pathname"
```

versus:

```text
"this is the object this operation created and is authorized to destroy"
```

The first is necessary for TOCTOU safety.

The second is necessary for ownership safety.

Both are required.

The next repair should therefore avoid adding separate "verified delete" helpers with subtly different semantics.

Prefer:

```text
OwnedFileClaim
OwnedDirectoryClaim
```

with an explicit expected identity carried from creation to destruction.

---

# 7. Ordered CRUU19 implementation plan

---

## PHASE 01 — Rebuild capability probing on owned handles
### Fixes CRUU19-001 live path

Replace:

```text
CreateNew -> close -> File.Replace -> File.Delete
```

with a retained-handle probe transaction.

Requirements:

```text
- exact current probe handle retained;
- exact replacement probe handle retained;
- no raw File.Replace;
- no raw File.Delete;
- no path reopen can authorize replacement/destruction;
- no-overwrite semantics on promotion;
- exact handle deletion.
```

Add test hooks immediately before:

```text
current sideline
replacement promotion
final probe retirement
```

to inject substitutions.

---

## PHASE 02 — Persist capability-probe provenance
### Fixes CRUU19-001 crash/retry path

When a migration probe object is created:

```text
record path + WindowsFileIdentity + expected content
```

before releasing the only creation authority.

Retry must require journal identity + manifest content.

If either authority is absent/mismatched:

```text
preserve + fail closed
```

Remove content-only capability-control deletion.

---

## PHASE 03 — Finish all stage-factory ownership bootstrap paths
### Fixes CRUU19-002

Audit every production call to:

```text
WindowsOwnedDurableStage.CreateNew*
```

and every wrapper implementing:

```text
IOwnedFileStage
```

For every factory that records ownership:

```text
record failure => DeleteExact before Dispose
```

Include at least:

```text
CAS stage
payload migration stage
manifest Ready stage
```

Create one reusable helper if possible to prevent a fourth copy from diverging later.

---

## PHASE 04 — Add durable directory ownership
### Fixes CRUU19-003

Replace:

```text
TrackCreatedDirectory(string path)
```

with:

```text
TrackCreatedDirectory(
    string path,
    WindowsFileIdentity identity)
```

or a durable `OwnedDirectoryClaim`.

Record the identity immediately after successful `CreateDirectoryW`.

Use it for both:

```text
in-process rollback
restart recovery
```

Delete only same-identity current directories.

---

## PHASE 05 — Make runtime evidence exception-safe
### Fixes CRUU19-004

Implement:

```text
AssertProductionHitReturns(...)
AssertProductionHitThrows<TException>(...)
```

or a unified capture API.

Required properties:

```text
- hit assertion always runs;
- expected exception does not skip it;
- hit assertion failure cannot be hidden by outer Assert.Throws;
- original exception remains available for assertion;
- sink is restored in finally.
```

---

## PHASE 06 — Expand production-symbol authority

For every CRUU19 finding, map exact production surfaces.

For CRUU18-006 / CRUU19-002 include:

```text
WindowsAtomicExpectedFileReplacer.ReplaceIfExpected
DefaultMigrationFileOps.CreateOwnedStage
DefaultMigrationManifestFileOps.CreateOwnedStage
```

For probe finding include:

```text
DataRootCapabilityValidator.ValidateWritable
owned probe transaction primitive
MigrationRecoveryService.RecoverForRetry
```

For directory finding include:

```text
directory creation/claim primitive
MigrationTargetTransaction.Rollback
MigrationRecoveryService.RecoverForRetry
```

---

## PHASE 07 — Windows adversarial acceptance

On exact final SHA:

```text
1. Fresh checkout.
2. Release build.
3. capability-probe same-content substitution suite.
4. live probe replace/delete race suite.
5. retry probe provenance suite.
6. manifest-stage first-claim failure suite.
7. directory same-path/different-identity suite.
8. CRUU18 regression suite.
9. CRUU19 regression suite.
10. all filesystem/reparse integration tests.
11. full suite once.
12. full suite five consecutive times.
13. exact required sentinel verification across retained TRX.
14. runtime-symbol evidence gate.
15. meta-test that expected exceptions cannot bypass hit evidence.
16. pinned icon regeneration verification.
17. self-contained win-x64 publish.
18. strict executable asset/icon verification.
19. exact-SHA release workflow.
20. fresh independent source/recovery audit.
```

---

# 8. Proposed CRUU19 sentinels

```text
# CRUU19-001
CRUU19_001_Live_probe_destination_same_content_foreign_replacement_is_not_replaced
CRUU19_001_Live_probe_source_same_content_foreign_replacement_is_not_promoted
CRUU19_001_Live_probe_before_final_delete_foreign_replacement_is_not_deleted
CRUU19_001_Probe_failure_cleanup_requires_creation_identity_not_current_path_identity
CRUU19_001_Retry_same_content_different_identity_probe_is_preserved
CRUU19_001_Retry_exact_owned_probe_identity_and_content_is_deleted
CRUU19_001_CRUU12_027_executes_real_DataRootCapabilityValidator_substitution_path
CRUU19_001_CRUU13_004_tests_same_content_different_identity_not_only_wrong_content
CRUU19_001_DefaultCapabilityFileOps_exposes_no_raw_path_replace_or_delete_for_owned_probe

# CRUU19-002
CRUU19_002_Manifest_stage_claim_failure_deletes_exact_stage_before_handle_release
CRUU19_002_Manifest_stage_postappend_failure_deletes_stage_and_reconciles_stale_record
CRUU19_002_Ready_manifest_stage_claim_failure_does_not_wedge_RecoverForRetry
CRUU19_002_CRUU18_006_all_stage_factories_test_includes_DefaultMigrationManifestFileOps
CRUU19_002_CRUU18_006_required_symbols_include_manifest_stage_factory

# CRUU19-003
CRUU19_003_Inprocess_rollback_same_path_different_empty_directory_is_preserved
CRUU19_003_Retry_same_path_different_empty_directory_is_preserved
CRUU19_003_Attempt_created_directory_records_WindowsFileIdentity
CRUU19_003_Exact_owned_empty_directory_identity_is_removed
CRUU19_003_Foreign_nonempty_directory_remains_preserved
CRUU19_003_CRUU16_006_swapped_directory_test_uses_directory_to_directory_identity_swap

# CRUU19-004
CRUU19_004_AssertProductionHit_expected_exception_still_requires_runtime_hit
CRUU19_004_Expected_exception_without_hit_fails_evidence_test
CRUU19_004_Expected_exception_with_hit_passes_evidence_test
CRUU19_004_CRUU18_002_failure_sentinel_proves_CAS_runtime_hit
CRUU19_004_CRUU18_006_failure_sentinels_prove_all_stage_factory_runtime_hits
CRUU19_004_Required_symbol_map_detects_omitted_manifest_stage_factory
```

Total proposed CRUU19 sentinels: **26**.

---

# 9. Acceptance invariants after repair

```text
PROBE-OWN-01
A probe pathname is never ownership authority.

PROBE-OWN-02
Every live probe mutation remains bound to the exact objects created by that probe.

PROBE-OWN-03
Live capability probing uses no raw pathname File.Replace/File.Delete after creation handles close.

PROBE-OWN-04
Retry cleanup requires exact recorded probe identity plus expected content.

PROBE-OWN-05
Same-content foreign probe replacement is preserved.

STAGE-CLAIM-01
Every stage factory exact-cleans a just-created object when its first durable ownership claim fails.

STAGE-CLAIM-02
Manifest Ready-stage factory satisfies the same ownership bootstrap contract as CAS and payload stages.

DIR-OWN-01
Attempt-created directory ownership includes exact Windows filesystem identity.

DIR-OWN-02
Rollback/retry never delete a same-path different-identity directory.

DIR-OWN-03
Same-handle deletion is used only after creation identity has been proven.

EVIDENCE-01
A production-hit assertion executes whether the tested action returns or throws.

EVIDENCE-02
Expected-exception tests cannot pass if the required production symbol was never hit.

EVIDENCE-03
High-risk production-symbol authority enumerates every required production surface.

REL-01
Full suite passes.

REL-02
Full suite passes five consecutive times.

REL-03
All required exact sentinels pass from retained TRX.

REL-04
Runtime-symbol evidence passes on the exact final SHA.

REL-05
Strict release workflow passes on the exact final SHA.

REL-06
Self-contained published EXE passes strict release asset verification.

REL-07
Fresh independent re-audit reports zero findings.
```

---

# 10. Repair priority

```text
P0:
    CRUU19-001  capability probe foreign-file destruction path

P1:
    CRUU19-002  manifest stage first-claim cleanup
    CRUU19-003  directory creation-identity authority

P1 / acceptance infrastructure:
    CRUU19-004  expected-exception runtime evidence bypass
```

CRUU19-001 should be fixed before trying to strengthen its tests around the old `ICapabilityFileOps` design. The production design itself should stop using path replacement/deletion.

---

# 11. Final assessment

The current repair commit is substantially healthier than the CRUU18 snapshot.

Five of the seven CRUU18 findings are now convincingly closed in source, and the two remaining CRUU18 areas are narrower:

```text
CRUU18-006 -> one missed stage factory
CRUU18-007 -> exception-path runtime evidence is not enforced
```

The most important discovery in this pass is outside the direct CRUU18 delta: the capability probe is still using the older notion that:

```text
regular file at our nonce path
+
expected tiny content
```

is enough authority to replace or delete.

The rest of the repository has already moved beyond that model.

Migration finals now correctly require identity **and** content.

Capability probes should use the same rule.

The directory finding is the corresponding directory version of the same distinction:

```text
same object I opened
!=
object my operation created
```

Once these remaining older subsystems are migrated to the same ownership model, the codebase should have far fewer independent authority semantics to audit.

---

# 12. Final status

```text
AUDITED_HEAD                         = d7e7b4d8a1360f46de0a51d8caa0cda51f3c1d60

CRUU19_FINDINGS                      = 4
HIGH                                 = 1
MED_HIGH                             = 3

CRUU18_CORE_IMPROVEMENT_REAL         = YES
CRUU18_STRICTLY_CLOSED               = NO

SOURCE_AUDIT_CLEAN                   = NO

WINDOWS_RUNTIME_DIRECTLY_EXECUTED    = NO
EXACT_HEAD_CI_STATUS                 = NOT INDEPENDENTLY BOUND

STRICT_RELEASE_READY                 = NO
ZERO_DEFECT_VERIFIED                 = NO
