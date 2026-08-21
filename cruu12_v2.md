# CRUU12 — Post-CRUU11 Paranoid Re-Audit & Final Crash/Authority Repair Blueprint

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited commit:** `5c1904f870d0b2587407b4484e02e6ed889a4acd`  
**Parent / CRUU11 baseline:** `cca54bc1ed79cc69e60342e865573c16c77f9950`  
**Audit date:** 2026-08-21  
**Audit type:** source-level re-audit of the current pushed repository, with prior CRUU11 requirements used only as a checklist—not as evidence that fixes are correct.

> **CRUU12 v2 enhancement note (2026-08-21):** This revision completes the previously truncated per-finding repair material through CRUU12-034 and adds implementation kits, fault-injection infrastructure, phase-by-phase weak-model prompts, evidence requirements, anti-cheating test rules, and final acceptance/audit prompts. The original finding IDs and audited commit remain unchanged.

---

# 1. Executive verdict

The CRUU11 implementation is another large, serious improvement. It is **not** a superficial patch. The audited commit contains approximately:

```text
+9,338 lines
-1,426 lines
10,764 total changed lines
1 commit over the CRUU11 baseline
```

It materially landed many difficult controls:

```text
- boundary-aware verified deletion;
- handle-based physical resolution;
- strict managed-tree type/case checks;
- application-lifetime managed-tree lease;
- schema-v4 migration manifest with source payload fingerprint;
- mutation journal/recovery framework;
- orphan reconciler;
- true Win32 durable atomic writer;
- strict UTF-8 helper;
- settings durable writer unification;
- exact test-name evidence matching;
- new CI categories;
- ICO/EXE pixel identity verifier.
```

Those improvements must be preserved.

However, the new transactional and cleanup layers introduce **second-order authority bugs**. The most severe remaining issue is now the ordinary prompt mutation transaction:

```text
a post-primary-commit failure can trigger the catch block,
delete/restore prompt content,
delete the journal,
and leave library metadata inconsistent with the body
with no remaining recovery authority.
```

That defeats the purpose of the newly added crash journal.

This audit therefore does **not** grant zero-defect acceptance.

```text
AUDITED HEAD                              = 5c1904f870d0b2587407b4484e02e6ed889a4acd
CRUU11 IMPLEMENTATION                     = SUBSTANTIAL BUT NOT FINAL
SOURCE-LEVEL CRUU12 AUDIT                 = COMPLETE
CRUU12 FINDINGS                           = 34
CRITICAL                                  = 1
HIGH                                      = 9
MED-HIGH                                  = 12
MED / LOW-MED                             = 11
RELEASE BLOCKER                           = 1
INDEPENDENT WINDOWS/.NET TEST EXECUTION   = NOT AVAILABLE IN THIS AUDIT ENVIRONMENT
GITHUB COMBINED STATUS ENTRIES            = NONE RETURNED
ZERO-DEFECT ACCEPTANCE                    = NOT GRANTED
STRICT RELEASE                            = BLOCKED BY MISSING APPROVED LOGO
```

The absence of returned combined-status entries does **not** prove that GitHub Actions did or did not execute. The repository commit message and test source are implementation claims/evidence, not independent runtime verification.

---

# 2. Do not regress these CRUU11 fixes

The implementing model must preserve the following current improvements.

```text
1. WindowsVerifiedArtifactDeleter:
   - FILE_FLAG_OPEN_REPARSE_POINT
   - reparse-object rejection
   - same-handle length/hash verification
   - PathIdentity.IsStrictDescendant containment

2. WindowsFinalPathHelper:
   - dynamic GetFinalPathNameByHandleW buffer
   - \\?\ prefix normalization
   - \\?\UNC\ normalization

3. WindowsPhysicalPathResolver:
   - use the directory handle returned by the strict opener
   - do not restore probe-then-reopen-by-name behavior

4. DataRootTopologyValidator:
   - inaccessible directory is not "Missing"
   - strict nearest-existing ancestor semantics

5. ManagedTreeTopologyValidator:
   - File vs Directory vs Missing distinction
   - child reparse checks
   - child physical equality
   - per-child case sensitivity

6. App:
   - retains ManagedDataRootSessionLease until exit

7. durable writer:
   - FileMode.CreateNew temp ownership
   - Flush(true)
   - MOVEFILE_WRITE_THROUGH
   - create-no-overwrite mode

8. settings:
   - one IDurableSettingsFileWriter for normal settings mutation
   - strict UTF-8 parsing
   - primary + backup CAS under settings lease

9. migration:
   - schema v4 writes
   - schema v3 read/recovery compatibility
   - SourcePayloadFingerprintSha256Hex
   - final source re-capture in ReadyGate
   - marker-last retry intent
   - file-based planned probes rather than probe directories

10. ordinary CRUD:
    - durable mutation journal framework
    - recovery copies for edits
    - true create-no-overwrite prompt writes

11. test/CI:
    - exact-name lookup in VerifyTestEvidence.ps1
    - dedicated FilesystemAuthority, PackageIntegrity,
      MutationRecovery, MigrationReady, StrictUtf8 categories

12. release tooling:
    - decoded RGBA comparison
    - PE RT_GROUP_ICON/RT_ICON extraction
```

Do not solve CRUU12 by reverting to pre-CRUU11 code.

---

# 3. Finding register

| ID | Severity | Finding |
|---|---|---|
| CRUU12-001 | **CRITICAL** | `PromptMutationCoordinator` has generic catch cleanup that can delete/restore prompt content and delete the journal **after `library.json` already committed**, creating metadata/body inconsistency with no recovery authority; Create can also delete a pre-existing collision file if it happens to match attempted bytes |
| CRUU12-002 | **HIGH** | Mutation journal `OldLibrarySha256Hex` is computed from reserialized in-memory metadata rather than exact current `library.json` bytes, so valid noncanonical/BOM metadata makes post-crash recovery classify the old state as `Other` |
| CRUU12-003 | **HIGH** | Body-only edit can have identical old/new metadata hashes; recovery checks Old before New and ignores the durable phase, so a crash after `MetadataDurable` can restore the old body and silently undo a committed edit |
| CRUU12-004 | **HIGH** | `AdvanceDurable` mutates the caller journal phase before its durable write and performs no on-disk compare-and-swap; a write failure leaves RAM one phase ahead of disk |
| CRUU12-005 | MED-HIGH | Mutation journal validation is structurally incomplete: hashes are not validated as hexadecimal and kind-specific required/forbidden fields are not enforced |
| CRUU12-006 | **HIGH** | Settings temp cleanup calls the **full data-root reconciler** before `.app.lock`; on the default root a second process can delete another live process’s library/mutation temp, while custom data roots never receive equivalent startup data-root reconciliation |
| CRUU12-007 | MED-HIGH | Durable-temp reconciliation has incomplete location authority: recovery-directory temps are never scanned, broad `Exists`/catch behavior is fail-soft, and cleanup failures are silently swallowed |
| CRUU12-008 | **HIGH** | Settings CAS token capture catches every error and returns `Exists=false`, collapsing unreadable/I/O failures into “Missing” and weakening compare-and-swap authority |
| CRUU12-009 | MED | Settings load accepts schema versions below `CurrentSchemaVersion`; save rejects some of them, making read/write schema authority inconsistent |
| CRUU12-010 | MED-HIGH | Loaded/saved `dataRootPath` is not consistently passed through `NormalizeAndValidateDataRoot`; relative or noncanonical paths can enter active settings state |
| CRUU12-011 | **HIGH** | Migration retry and live rollback perform destructive path-based deletion of controls/temps without holding a target-tree handle lease and without proving the leaf object is the one the attempt created |
| CRUU12-012 | MED-HIGH | `EnsureDirectoryTracked` uses probe + `Directory.CreateDirectory` + TrackCreatedDirectory, so a concurrent creator can make Prompt Helper claim and later delete a directory it did not create |
| CRUU12-013 | MED-HIGH | Migration temp→final promotion has a bookkeeping gap: the move succeeds before `PromoteCreatedFile`; if promotion bookkeeping fails, the final exists but is not transaction-owned |
| CRUU12-014 | **HIGH** | Retry cleanup deletes manifest-declared payload temps and capability/stage controls merely because they exist at the declared path; a foreign replacement at that exact path can be deleted |
| CRUU12-015 | MED-HIGH | Migration control grammar is not schema-specific and schema-v4 capability file grammar is overly broad; arbitrary suffixes and legacy directory controls can be accepted |
| CRUU12-016 | MED | Schema-v3 reader derives a full payload fingerprint but retry recovery ignores it and compares only the legacy primary-library hash |
| CRUU12-017 | MED | New migration manifests capture `TargetRootExistedBefore` after target reservation has already created the root, so the baseline can falsely claim a newly created target pre-existed |
| CRUU12-018 | **HIGH functional** | Migration to the exact bootstrap/default root can be rejected because inventory hardcodes `targetIsBootstrapRoot:true`, classifies legitimate settings files as controls, and ReadyGate only allows migration marker + `.app.lock` |
| CRUU12-019 | MED-HIGH | Migration inventory uses permissive `Directory.Exists`/`GetFiles`/`GetDirectories`; access-denied/inaccessible nodes can be collapsed or mishandled instead of failing closed |
| CRUU12-020 | MED-HIGH | No startup lifecycle-journal conflict detector exists; migration, mutation, and initialization markers can coexist and be processed sequentially instead of failing closed before mutation |
| CRUU12-021 | MED-HIGH | Migration source/target text validation is not consistently strict UTF-8: StreamReader BOM auto-detection can accept UTF-16/32 and active prompt bodies are often hashed/readable-checked without strict UTF-8 decode |
| CRUU12-022 | MED | `WindowsStrictDirectoryOpener` returns any successfully opened handle as a Directory without verifying the handle's file attributes/type |
| CRUU12-023 | MED-HIGH | `ManagedDataRootSessionLease` still performs probe→open and does not verify final handle path/type/reparse identity after opening; a swap between probe and lease acquisition can bind the wrong object |
| CRUU12-024 | **HIGH** | Source and target payload files remain externally writable between ReadyGate validation and settings commit; directory leases prevent node deletion/rename but do not freeze file contents |
| CRUU12-025 | MED-HIGH | In-process migration rollback residue detection scans only manifest payload finals/temps, so a failed Ready-manifest stage cleanup can leave a control orphan after the marker is deleted |
| CRUU12-026 | MED-HIGH | Existing-target random capability cleanup deletes every `.prompthelper-capability-*.tmp` wildcard match and swallows errors; malformed/foreign lookalikes can be removed |
| CRUU12-027 | MED-HIGH | Capability-probe failure cleanup deletes by path after creation without same-object/hash proof; a probe file replaced after creation can be deleted as though still owned |
| CRUU12-028 | **HIGH** | `LibraryRepository` still exposes bare `SynchronizeBackup(LibraryDocument)` and `CommitCanonicalBytes(document, bytes)` can write bytes not proven to represent the supplied document, then synchronize backup from a different object |
| CRUU12-029 | MED-HIGH | Public compatibility adapters/constructors reintroduce weaker semantics: check-then-replace “CreateNew”, unverifying “verified” deletion, and potentially non-durable settings writes |
| CRUU12-030 | MED | Orphan reconciliation/startup maintenance broadly swallows failures and App drops successful mutation-recovery warnings; conservative residue can become invisible |
| CRUU12-031 | MED | Initialization control still uses `MutationControl`, stale-marker deletion is best-effort, and first marker creation uses replace semantics rather than strict create-new authority |
| CRUU12-032 | MED verification gap | Several required CRUU11 sentinel tests do not execute the scenario named by the test, allowing CI to claim coverage without proving the property |
| CRUU12-033 | MED release gap | Strict release compares committed ICO→EXE but still does not prove approved SVG→committed ICO; icon generation rasterizes once at 256 then downsizes, and PE verification chooses only the first icon group |
| CRUU12-034 | **RELEASE BLOCKER** | Approved real `src/PromptHelper/Assets/PromptHelperLogo.svg` is still absent at the audited commit |

---

# 4. Mandatory implementation order

The weak implementing AI MUST use this order.

```text
PHASE 00  baseline, tests, no implementation changes
PHASE 01  mutation transaction point-of-no-return semantics
PHASE 02  exact old-library snapshot + ambiguous hash handling
PHASE 03  mutation journal CAS / strict grammar
PHASE 04  settings token and schema/path authority
PHASE 05  split settings-temp vs data-root-temp reconciliation
PHASE 06  strict durable-temp location policy
PHASE 07  migration operation leases / verified cleanup
PHASE 08  atomic migration directory ownership / transaction bookkeeping
PHASE 09  schema-specific manifest control grammar + control content authority
PHASE 10  v3 payload-fingerprint retry authority
PHASE 11  target baseline / bootstrap inventory context
PHASE 12  lifecycle journal conflict detector
PHASE 13  strict UTF-8 migration package authority
PHASE 14  strict directory/lease handle identity
PHASE 15  Ready→settings payload commit lease
PHASE 16  rollback/control terminal inventory
PHASE 17  capability stale/probe cleanup
PHASE 18  LibraryRepository strong typed payload API
PHASE 19  remove unsafe compatibility adapters
PHASE 20  maintenance observability + initialization controls
PHASE 21  rewrite false-positive tests + evidence gates
PHASE 22  release identity chain
PHASE 23  5x Windows stress + publish + final source audit
```

Do not start release polish before the transaction/authority phases pass.

---

# 5. Global locked invariants

These rules control all fixes.

## 5.1 Durable journal beats catch cleanup

After a durable journal is created:

```text
NO arbitrary catch block may guess how to roll back multi-file state.
```

Only the central recovery state machine may:

```text
delete a new body
restore an old body
delete a recovery body
retire the journal
```

## 5.2 Primary metadata commit is a point of no return

Once exact new `library.json` bytes were durably promoted:

```text
the operation must never be "rolled back" by changing only prompt bodies.
```

If cleanup/final journal advancement fails postcommit:

```text
preserve the committed primary state
preserve enough journal evidence
force controlled restart if needed
recover/finalize on next startup
```

## 5.3 Missing and unreadable are never equivalent

Only:

```text
FileNotFoundException
DirectoryNotFoundException
Win32 ERROR_FILE_NOT_FOUND
Win32 ERROR_PATH_NOT_FOUND
```

may become Missing.

Access denied / sharing / device / generic I/O:

```text
FAIL CLOSED
```

## 5.4 Deletion requires ownership authority

A path name alone is never ownership.

Auto-delete is allowed only when at least one strong proof exists:

```text
- same open handle identity held since creation; or
- exact manifest/journal role + expected length/hash + contained non-reparse final handle; or
- exact product temp grammar under the required exclusive process lock,
  where the namespace itself is intentionally cleanup-authoritative.
```

## 5.5 No safety-only public adapter

Production/public constructors may not silently downgrade:

```text
create-no-overwrite
verified deletion
durable write
strict UTF-8
CAS
```

to a weaker test compatibility behavior.



---

# 6. CRUU12-001 — CRITICAL: mutation catch cleanup can destroy committed state

## 6.1 Exact failure

Current Create transaction:

```text
journal Prepared
body durable
journal BodyDurable
library primary commit
backup sync attempt
journal MetadataDurable
journal delete
```

Current generic catch then does:

```text
verified-delete body
delete journal
rethrow
```

That catch has no knowledge of which line failed.

Therefore this cut is catastrophic:

```text
library.json NEW already durable
↓
AdvanceDurable(MetadataDurable) fails
↓
catch deletes NEW body
↓
catch deletes journal
↓
throw
```

Persistent state becomes:

```text
library.json references new prompt
prompt body missing
no journal remains
```

Edit is equally dangerous:

```text
library.json NEW already durable
↓
MetadataDurable journal write / recovery cleanup fails
↓
catch restores OLD body
↓
catch deletes journal
```

Persistent state becomes:

```text
metadata NEW
body OLD
no journal
```

Create has a second ownership bug:

```text
CreateNewDurable fails because destination already exists
↓
catch calls verified deleter
↓
if foreign collision file happens to equal attempted body bytes
   it is deleted despite never being created by this invocation
```

## 6.2 Mandatory architecture

Delete the destructive catch logic.

Use one of two paths only:

```text
PRE-PRIMARY-COMMIT failure:
    call central mutation recovery using durable journal/disk state
    if recovery succeeds -> rethrow original operation failure
    if recovery fails    -> preserve journal, return typed recovery-required failure

POST-PRIMARY-COMMIT failure:
    NEVER restore/delete active body based only on catch position
    central recovery/finalization owns cleanup
    if finalization succeeds -> operation is committed, return committed result/warning
    if finalization fails    -> preserve journal, force controlled restart
```

## 6.3 Add transaction result type

```csharp
internal enum MutationCommitState
{
    NotCommitted,
    PrimaryCommitted,
    FullyFinalized
}

internal sealed record MutationCommitOutcome(
    MutationCommitState State,
    CommitResult? CommitResult,
    string? Warning = null);
```

## 6.4 Add typed postcommit exception

```csharp
internal sealed class
    CommittedMutationRequiresRestartException
    : IOException
{
    public Guid OperationId { get; }

    public CommittedMutationRequiresRestartException(
        Guid operationId,
        string message,
        Exception inner)
        : base(message, inner)
    {
        OperationId = operationId;
    }
}
```

The UI must treat this differently from a normal save failure:

```text
DO NOT keep editing in the current in-memory process.
Show:
"Your change reached durable storage, but Prompt Helper could not finish
its recovery bookkeeping. Prompt Helper must close now to recover safely."

Then shutdown.
```

## 6.5 Copy-ready coordinator pattern

```csharp
private MutationCommitOutcome
    ExecuteJournaledMutation(
        LibraryMutationJournal journal,
        Func<CommitResult> operation)
{
    Exception? original = null;

    try
    {
        CommitResult result = operation();

        return new MutationCommitOutcome(
            MutationCommitState.FullyFinalized,
            result);
    }
    catch (Exception ex)
    {
        original = ex;
    }

    MutationRecoveryResult recovery =
        _recovery.RecoverIfPresent();

    if (!recovery.Success)
    {
        LibraryMutationJournal? persisted =
            _journalRepo.TryReadStrict();

        bool primaryCommitted =
            persisted is not null &&
            persisted.Phase >=
                LibraryMutationPhase.MetadataDurable;

        if (primaryCommitted)
        {
            throw new
                CommittedMutationRequiresRestartException(
                    journal.OperationId,
                    "The prompt change reached the library " +
                    "metadata, but Prompt Helper could not " +
                    "finish durable recovery bookkeeping.",
                    original!);
        }

        throw new IOException(
            "The prompt change failed and automatic " +
            "rollback could not be completed. " +
            "Recovery evidence was preserved.",
            original);
    }

    // Recovery succeeded. Determine whether the durable
    // operation committed or rolled back from authoritative
    // recovery result, not from catch position.
    if (recovery.Committed)
    {
        return new MutationCommitOutcome(
            MutationCommitState.PrimaryCommitted,
            recovery.CommitResult,
            recovery.Warning);
    }

    throw original!;
}
```

Expand `MutationRecoveryResult`:

```csharp
public sealed record MutationRecoveryResult(
    bool Success,
    bool Committed = false,
    CommitResult? CommitResult = null,
    string? Warning = null,
    string? ErrorMessage = null);
```

## 6.6 Do not infer ownership after CreateNew failure

Create body ownership becomes true only after successful `CreateNewDurable`.

```csharp
bool bodyCreatedByThisOperation = false;

_writer.CreateNewDurable(...);
bodyCreatedByThisOperation = true;
```

But after journal publication, even that bool is only an optimization.
Persistent recovery authority remains the journal + disk content.

If CreateNew throws before successful creation:

```text
central recovery sees library OLD
body:
    Missing -> retire journal
    NEW exact but no durable BodyDurable phase -> DO NOT auto-delete unless
    the journal state/owned-create marker proves this operation created it
```

Preferred fix: make durable `BodyDurable` advancement the ownership publication
immediately after successful CreateNew; if that phase write fails, leave the
Prepared journal and do not blindly delete a matching collision.

## 6.7 Required tests

Fault inject at every line:

```text
CRUU12_001_Create_body_CreateNew_collision_same_bytes_preserves_foreign_file
CRUU12_001_Create_fail_advancing_BodyDurable_preserves_recovery_authority
CRUU12_001_Create_primary_committed_MetadataDurable_write_fails_does_not_delete_body
CRUU12_001_Create_journal_delete_failure_does_not_delete_committed_body
CRUU12_001_Edit_primary_committed_MetadataDurable_write_fails_does_not_restore_old_body
CRUU12_001_Edit_recovery_cleanup_failure_after_commit_keeps_new_body
CRUU12_001_Postcommit_unfinalizable_mutation_forces_restart
CRUU12_001_No_generic_destructive_catch_remains_in_PromptMutationCoordinator
```

For every postcommit case assert:

```text
library primary exact NEW bytes
active body exact NEW bytes
journal either safely retired OR preserved
old recovery body either safely removed OR preserved
never metadata NEW + body OLD/Missing
```

---

# 7. CRUU12-002 — Old library hash must use exact disk bytes

## 7.1 Current defect

Current code computes:

```csharp
byte[] oldLibrary =
    _libraryRepo.SerializeCanonicalBytes(current);
```

Recovery hashes:

```csharp
File.ReadAllBytes(library.json)
```

Those are not guaranteed identical.

Equivalent valid examples:

```json
{"schemaVersion":1,"categories":[],"prompts":[]}
```

versus pretty-indented canonical JSON.

Also:

```text
UTF-8 BOM legacy/current readable file
property whitespace differences
line-ending differences
```

The transaction journal must describe the actual durable old state, not a
re-rendering of it.

## 7.2 Add `LibraryPrimarySnapshot`

```csharp
internal sealed record LibraryPrimarySnapshot(
    byte[] RawBytes,
    LibraryDocument Document,
    byte[] CanonicalBytes,
    string RawSha256Hex,
    string CanonicalSha256Hex);
```

## 7.3 Repository capture API

```csharp
internal LibraryPrimarySnapshot
    CapturePrimarySnapshot()
{
    byte[] raw =
        File.ReadAllBytes(_paths.LibraryPath);

    string json =
        StrictUtf8Text.Decode(
            raw,
            "primary library metadata");

    LibraryDocument parsed =
        InspectAndDeserialize(json);

    LibraryValidator.Validate(parsed);

    byte[] canonical =
        SerializeCanonicalBytes(parsed);

    return new LibraryPrimarySnapshot(
        RawBytes: raw,
        Document: parsed,
        CanonicalBytes: canonical,
        RawSha256Hex: Hash(raw),
        CanonicalSha256Hex: Hash(canonical));
}
```

## 7.4 Verify in-memory state before mutation

Before journal creation:

```csharp
LibraryPrimarySnapshot disk =
    _libraryRepo.CapturePrimarySnapshot();

byte[] currentCanonical =
    _libraryRepo.SerializeCanonicalBytes(
        current);

if (!disk.CanonicalBytes.AsSpan()
        .SequenceEqual(currentCanonical))
{
    throw new InvalidOperationException(
        "The library changed outside the current " +
        "Prompt Helper state. Reload before editing.");
}
```

Journal:

```csharp
OldLibrarySha256Hex = disk.RawSha256Hex;
```

New hash:

```csharp
byte[] newLibrary =
    _libraryRepo.SerializeCanonicalBytes(candidate);

NewLibrarySha256Hex = Hash(newLibrary);
```

## 7.5 Tests

```text
CRUU12_002_Noncanonical_valid_primary_body_create_crash_recovers_old_state
CRUU12_002_UTF8_BOM_primary_old_hash_uses_actual_bytes
CRUU12_002_In_memory_document_mismatch_with_disk_refuses_mutation_before_journal
CRUU12_002_Journal_old_hash_equals_exact_preoperation_library_bytes
```

---

# 8. CRUU12-003 — identical old/new metadata hash makes body-only edit ambiguous

## 8.1 Failure

An edit that changes only body content but keeps title/category/order can have:

```text
OldLibrarySha256Hex == NewLibrarySha256Hex
```

Current recovery checks:

```csharp
if hash == old => Old
else if hash == new => New
```

So equal hashes always classify Old.

If the journal itself durably reached:

```text
MetadataDurable
```

and the process crashes before journal retirement, startup restores OLD body,
undoing the completed content edit.

## 8.2 Add explicit metadata classification

```csharp
internal enum LibraryMutationMetadataState
{
    Missing,
    OldOnly,
    NewOnly,
    OldAndNewSameBytes,
    Other
}
```

```csharp
private static
    LibraryMutationMetadataState
    ClassifyLibrary(
        byte[] bytes,
        LibraryMutationJournal journal)
{
    string sha = Hash(bytes);

    bool oldMatch =
        string.Equals(
            sha,
            journal.OldLibrarySha256Hex,
            StringComparison.OrdinalIgnoreCase);

    bool newMatch =
        string.Equals(
            sha,
            journal.NewLibrarySha256Hex,
            StringComparison.OrdinalIgnoreCase);

    if (oldMatch && newMatch)
        return LibraryMutationMetadataState
            .OldAndNewSameBytes;

    if (oldMatch)
        return LibraryMutationMetadataState.OldOnly;

    if (newMatch)
        return LibraryMutationMetadataState.NewOnly;

    return LibraryMutationMetadataState.Other;
}
```

## 8.3 Durable phase resolves the ambiguous state

For Edit:

```text
OldAndNewSameBytes + phase < MetadataDurable
    => rollback body to OLD

OldAndNewSameBytes + phase >= MetadataDurable
    => committed; body must be NEW
```

Do not use phase to override a physically contradictory `OldOnly/NewOnly`
state. Phase resolves only semantically/hash-equal ambiguity and validates
expected cut points.

## 8.4 Exact logic

```csharp
bool committed =
    libraryState switch
    {
        LibraryMutationMetadataState.NewOnly
            => true,

        LibraryMutationMetadataState.OldOnly
            => false,

        LibraryMutationMetadataState
            .OldAndNewSameBytes
            => journal.Phase >=
                LibraryMutationPhase.MetadataDurable,

        _ => throw new InvalidDataException(
            "Library metadata does not match " +
            "the mutation journal.")
    };
```

## 8.5 Tests

```text
CRUU12_003_Body_only_edit_old_new_library_hashes_are_equal_fixture
CRUU12_003_Body_only_edit_crash_at_BodyDurable_restores_old_body
CRUU12_003_Body_only_edit_crash_at_MetadataDurable_keeps_new_body
CRUU12_003_Body_only_edit_journal_retire_failure_keeps_new_body
```

---

# 9. CRUU12-004 — journal phase advance must be atomic with respect to RAM/disk authority

## 9.1 Current bug

Current pattern:

```csharp
journal.Phase = next;
_writer.ReplaceDurable(...);
```

If write fails:

```text
RAM journal says next
disk journal says old phase
```

## 9.2 Copy-on-write journal advance

```csharp
public void AdvanceDurable(
    LibraryMutationJournal journal,
    LibraryMutationPhase next)
{
    ArgumentNullException.ThrowIfNull(journal);

    LibraryMutationJournal persisted =
        TryReadStrict()
        ?? throw new InvalidDataException(
            "Mutation journal disappeared.");

    if (persisted.OperationId !=
        journal.OperationId)
    {
        throw new InvalidDataException(
            "Mutation journal operation changed.");
    }

    if (persisted.Phase != journal.Phase)
    {
        throw new InvalidDataException(
            $"Mutation journal phase changed. " +
            $"Expected {journal.Phase}, " +
            $"found {persisted.Phase}.");
    }

    ValidateTransition(
        journal.Kind,
        journal.Phase,
        next);

    LibraryMutationJournal candidate =
        Clone(journal);

    candidate.Phase = next;

    byte[] bytes =
        SerializeValidate(candidate);

    _writer.ReplaceDurable(
        _paths.LibraryMutationJournalPath,
        bytes,
        DurableFileClass.MutationControl);

    // Only after durable success.
    journal.Phase = next;
}
```

## 9.3 Add journal revision token

Preferred:

```csharp
public long Revision { get; set; }
```

Prepared:

```text
Revision = 0
```

Advance:

```text
expected disk revision == RAM revision
candidate revision = revision + 1
durable write
RAM revision = candidate revision
```

Strict parser requires nonnegative revision.

This is easier to reason about than phase-only CAS.

## 9.4 Safe delete CAS

```csharp
public void DeleteStrict(
    Guid expectedOperationId,
    long expectedRevision)
{
    LibraryMutationJournal current =
        TryReadStrict()
        ?? return;

    if (current.OperationId !=
        expectedOperationId ||
        current.Revision !=
        expectedRevision)
    {
        throw new InvalidDataException(
            "Mutation journal changed before retire.");
    }

    StrictFileAuthority
        .DeleteIfPresentStrict(
            _paths.LibraryMutationJournalPath);
}
```

## 9.5 Tests

```text
CRUU12_004_Advance_write_failure_does_not_mutate_RAM_phase
CRUU12_004_Advance_rejects_changed_disk_operation_id
CRUU12_004_Advance_rejects_changed_disk_revision
CRUU12_004_Delete_rejects_replaced_journal
```

---

# 10. CRUU12-005 — mutation journal grammar must be kind-specific

## 10.1 Hash helper

```csharp
private static void RequireSha256(
    string? value,
    string fieldName)
{
    if (value is null ||
        value.Length != 64 ||
        value.Any(c =>
            !Uri.IsHexDigit(c)))
    {
        throw new InvalidDataException(
            $"{fieldName} must contain exactly " +
            "64 hexadecimal characters.");
    }
}
```

Do not use `Uri.IsHexDigit` if you prefer an explicit helper; exact behavior
must be deterministic.

## 10.2 Per-kind invariants

### Create / Duplicate

Required:

```text
newBodyLength >= 0
newBodySha256Hex valid
oldBodyLength == null
oldBodySha256Hex == null
recoveryBodyRelativePath == null
```

### Edit

Required:

```text
oldBodyLength >= 0
oldBodySha valid
newBodyLength >= 0
newBodySha valid
recoveryBodyRelativePath exact:
recovery\mutation-<operationIdN>-old-<promptIdN>.md
```

### Delete

Required:

```text
oldBodyLength >= 0
oldBodySha valid
newBodyLength == null
newBodySha == null
recovery path == null
```

All kinds:

```text
oldLibrarySha valid
newLibrarySha valid
bodyRelativePath exact:
prompts\<promptIdN>.md
```

## 10.3 Enum strings only

Configure:

```csharp
new JsonStringEnumConverter(
    namingPolicy: null,
    allowIntegerValues: false)
```

## 10.4 Tests

```text
CRUU12_005_Nonhex_library_hash_rejected
CRUU12_005_Nonhex_body_hash_rejected
CRUU12_005_Create_missing_new_body_hash_rejected
CRUU12_005_Edit_missing_recovery_path_rejected
CRUU12_005_Edit_wrong_recovery_GUID_rejected
CRUU12_005_Delete_with_new_body_fields_rejected
CRUU12_005_Integer_kind_rejected
CRUU12_005_Integer_phase_rejected
```

---

# 11. CRUU12-006 / 007 — split settings-temp and data-root-temp reconciliation

## 11.1 Current bad startup order

Current settings load:

```text
settings lease
ReconcileSettingsTemps(...)
```

but `ReconcileSettingsTemps` calls:

```text
ReconcileDataRootTemps(settings directory, true)
```

On default configuration:

```text
settings directory == active data root
```

This happens before:

```text
.app.lock
managed-tree lease
```

A second process can therefore delete a live temp belonging to the first
process.

Custom data roots get the opposite problem: their data-root temps are never
reconciled.

## 11.2 Strict split

Create:

```text
SettingsTempReconciler
DataRootTempReconciler
```

Never call one from the other.

## 11.3 `SettingsTempReconciler`

Allowed directory:

```text
directory containing settings.json
```

Allowed exact classes:

```text
new settings durable temp
legacy settings.json temp
legacy settings.backup.json temp
```

No:

```text
library
prompt
recovery
init
migration
mutation
```

Copy-ready skeleton:

```csharp
internal static class SettingsTempReconciler
{
    public static TempReconciliationResult
        Reconcile(
            string settingsPath,
            string backupPath)
    {
        string root =
            Path.GetDirectoryName(settingsPath)
            ?? throw new InvalidOperationException(
                "Settings path has no directory.");

        var failures =
            new List<string>();

        foreach (string path in
                 EnumerateRootStrict(root))
        {
            string name =
                Path.GetFileName(path);

            bool owned =
                SettingsTempName.TryParse(
                    name,
                    out _) ||
                SettingsTempName
                    .TryParseLegacySettingsTemp(
                        name);

            if (!owned)
                continue;

            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                failures.Add(
                    $"{path}: {ex.Message}");
            }
        }

        return new(failures);
    }
}
```

Call only while `.settings.lock` is held.

## 11.4 `DataRootTempReconciler`

Call only after:

```text
physical root resolved
.app.lock acquired
managed tree runtime validated
ManagedDataRootSessionLease acquired
lifecycle-journal conflict check completed
```

Allowed locations:

| DurableFileClass | Exact directory |
|---|---|
| LibraryMetadata | root |
| InitializationControl | root |
| MigrationControl | root only if not active authoritative manifest/stage |
| MutationControl | root only if not active authoritative mutation journal |
| PromptBody | `prompts` |
| RecoveryArtifact | `recovery` |
| Settings | NEVER here |

## 11.5 Do not delete active authority files

Before reconciling:

```text
if .prompthelper-migration.json exists:
    migration control temps are recovery-owned, not generic-temp-owned

if .prompthelper-library-mutation.json exists:
    mutation control/recovery temps are mutation-recovery-owned

if initializing.marker exists:
    initialization temps are init-recovery-owned
```

Generic temp cleanup must defer those classes to the active recovery protocol.

## 11.6 Reconcile recovery directory

Current reconciler does not scan `recovery`.

Add:

```csharp
ReconcileDirectory(
    paths.RecoveryDirectory,
    allowedClass:
        DurableFileClass.RecoveryArtifact);
```

## 11.7 Cleanup failures are observable

```csharp
internal sealed record
    TempReconciliationResult(
        IReadOnlyList<TempCleanupFailure>
            Failures)
{
    public bool Success =>
        Failures.Count == 0;
}
```

At startup:

```text
failure to delete stale non-authoritative temp:
    warning, if no data authority ambiguity

failure caused by unreadable/unknown type:
    fail closed
```

## 11.8 Tests

```text
CRUU12_006_Settings_reconcile_does_not_touch_library_temp
CRUU12_006_Second_instance_settings_load_cannot_delete_live_data_temp
CRUU12_006_Custom_data_root_reconciled_after_app_lock
CRUU12_006_Default_data_root_reconciled_only_after_app_lock
CRUU12_007_RecoveryArtifact_temp_in_recovery_directory_is_reconciled
CRUU12_007_Settings_class_in_custom_data_root_is_preserved
CRUU12_007_Cleanup_failure_is_reported_not_swallowed
CRUU12_007_Active_mutation_temp_is_deferred_to_mutation_recovery
```

---

# 12. CRUU12-008 — settings CAS token must distinguish Missing from Unreadable

Current broad catch:

```csharp
catch
{
    return new SettingsFileToken(false, null);
}
```

Delete it.

Use:

```csharp
public SettingsFileToken
    CaptureFileToken(string path)
{
    try
    {
        byte[] bytes =
            File.ReadAllBytes(path);

        return new SettingsFileToken(
            Exists: true,
            Sha256: SHA256.HashData(bytes));
    }
    catch (FileNotFoundException)
    {
        return new SettingsFileToken(
            false,
            null);
    }
    catch (DirectoryNotFoundException)
    {
        return new SettingsFileToken(
            false,
            null);
    }
    catch (Exception ex) when (
        ex is IOException or
        UnauthorizedAccessException or
        SecurityException)
    {
        throw new SettingsReadException(
            path,
            "Settings CAS token could not be read.",
            ex);
    }
}
```

Tests:

```text
CRUU12_008_Primary_access_denied_token_is_not_Missing
CRUU12_008_Backup_access_denied_token_is_not_Missing
CRUU12_008_Sharing_violation_token_is_not_Missing
CRUU12_008_File_not_found_token_is_Missing
```

---

# 13. CRUU12-009 / 010 — settings schema and path authority

## 13.1 Schema

Current version must be exact.

```csharp
if (schemaVersion > Current)
    Future;

if (schemaVersion < Current)
    Corrupt/UnsupportedLegacy;
```

Do not return Valid for zero/negative/older without a real migration.

Save:

```csharp
if (settings.SchemaVersion !=
    AppSettings.CurrentSchemaVersion)
{
    throw new InvalidDataException(...);
}
```

## 13.2 Normalize loaded path

After deserialize:

```csharp
settings.DataRootPath =
    NormalizeAndValidateDataRoot(
        settings.DataRootPath);
```

Treat empty string as:

```text
null/default bootstrap
```

Recommended:

```csharp
public static string?
    NormalizeAndValidateDataRoot(
        string? path)
{
    if (string.IsNullOrWhiteSpace(path))
        return null;

    string trimmed = path.Trim();

    if (!Path.IsPathFullyQualified(trimmed))
    {
        throw new InvalidDataException(
            "Configured data-root path must be " +
            "fully qualified.");
    }

    return PathIdentity
        .NormalizeForComparison(trimmed);
}
```

Update `AppSettings.DataRootPath` type usage consistently.

## 13.3 Normalize before save

Clone settings rather than mutate caller:

```csharp
AppSettings normalized =
    new()
    {
        SchemaVersion =
            AppSettings.CurrentSchemaVersion,
        DataRootPath =
            NormalizeAndValidateDataRoot(
                settings.DataRootPath)
    };
```

Serialize normalized.

## 13.4 Tests

```text
CRUU12_009_Schema_zero_is_rejected_on_load
CRUU12_009_Schema_negative_is_rejected_on_load
CRUU12_009_Older_schema_is_not_silently_current
CRUU12_010_Relative_dataRootPath_rejected_on_load
CRUU12_010_Relative_dataRootPath_rejected_on_save
CRUU12_010_Load_returns_normalized_absolute_dataRootPath
CRUU12_010_Save_does_not_mutate_callers_AppSettings_object
```



---

# 14. CRUU12-011 — migration destructive cleanup needs a handle-bound target operation lease

## 14.1 Current destructive paths

Retry currently deletes:

```text
manifest controls by path
payload temps by path
attempt-created directories by path
```

Live transaction rollback deletes:

```text
tracked files by File.Delete(path)
tracked directories by Directory.Delete(path)
```

Tree topology is validated before these operations, but no lease holds the
target `prompts` / `recovery` directory identity for the full destructive
window.

A directory can be swapped after validation.

Final payload artifacts are safer because `WindowsVerifiedArtifactDeleter`
opens and verifies the leaf handle.

Controls/temps/dirs do not have equivalent protection.

## 14.2 Add `ManagedTargetOperationLease`

This is separate from the application-lifetime lease because a transition
operates on a different target root.

```csharp
internal sealed class
    ManagedTargetOperationLease
    : IDisposable
{
    private readonly List<SafeFileHandle>
        _handles = [];

    public string PhysicalRoot { get; }

    private ManagedTargetOperationLease(
        string physicalRoot)
    {
        PhysicalRoot =
            PathIdentity.NormalizeForComparison(
                physicalRoot);
    }

    public static ManagedTargetOperationLease
        Acquire(
            string physicalRoot,
            bool promptsMayBeMissing,
            bool recoveryMayBeMissing,
            IStrictDirectoryOpener? opener = null)
    {
        var activeOpener =
            opener ??
            new WindowsStrictDirectoryOpener();

        var lease =
            new ManagedTargetOperationLease(
                physicalRoot);

        lease.AddRequired(
            physicalRoot,
            activeOpener);

        lease.AddOptionalOrRequired(
            Path.Combine(
                physicalRoot,
                "prompts"),
            promptsMayBeMissing,
            activeOpener);

        lease.AddOptionalOrRequired(
            Path.Combine(
                physicalRoot,
                "recovery"),
            recoveryMayBeMissing,
            activeOpener);

        return lease;
    }

    private void AddRequired(
        string expectedPhysicalPath,
        IStrictDirectoryOpener opener)
    {
        SafeFileHandle handle =
            opener.OpenManagedNodeLease(
                expectedPhysicalPath);

        AssertDirectoryHandleIdentity(
            handle,
            expectedPhysicalPath);

        _handles.Add(handle);
    }

    ...
}
```

The opener/identity hardening required by CRUU12-022/023 is a dependency.

## 14.3 Hold it for the entire destructive retry

Retry:

```text
read/validate manifest
validate source authority
acquire target operation lease
inventory
cleanup controls/temps/finals/dirs
terminal inventory
marker retire
release lease
```

No delete after release.

## 14.4 Hold it for live migration

After reservation + fresh precreation validation:

```text
create prompts/recovery as needed
acquire target operation lease
publish manifest
copy
capability
Ready
settings commit
transaction commit / rollback
marker cleanup
release target operation lease
release reservation
```

If children don't yet exist at lease time:

```text
create them atomically with ownership proof first
```

## 14.5 Tests

Real Windows:

```text
CRUU12_011_Retry_prompts_swap_attempt_fails_while_target_operation_lease_held
CRUU12_011_Retry_recovery_swap_attempt_fails_while_target_operation_lease_held
CRUU12_011_Rollback_prompts_swap_does_not_delete_outside_target
CRUU12_011_Rollback_recovery_swap_does_not_delete_outside_target
```

Use actual junction/rename operations.

---

# 15. CRUU12-012 — atomic directory ownership for migration-created children

Current helper:

```csharp
if (Probe(path) != Directory)
{
    Directory.CreateDirectory(path);
    tx.TrackCreatedDirectory(path);
}
```

This is check/create/claim.

Reuse the reservation ownership primitive.

## 15.1 Extract shared creator

```csharp
internal interface
    IOwnedDirectoryCreator
{
    DirectoryCreateOutcome TryCreateOwned(
        string path);
}

internal sealed class
    WindowsOwnedDirectoryCreator
    : IOwnedDirectoryCreator
{
    // CreateDirectoryW, ERROR_ALREADY_EXISTS
    ...
}
```

## 15.2 Transaction helper

```csharp
private static void EnsureDirectoryTracked(
    string path,
    MigrationTargetTransaction tx,
    IOwnedDirectoryCreator creator)
{
    DirectoryCreateOutcome result =
        creator.TryCreateOwned(path);

    switch (result)
    {
        case DirectoryCreateOutcome
            .CreatedByCaller:
            tx.TrackCreatedDirectory(path);
            return;

        case DirectoryCreateOutcome
            .AlreadyExists:
            StrictPathProbe probe =
                new StrictPathAuthority()
                    .Probe(path);

            if (probe.Kind !=
                StrictPathKind.Directory)
            {
                throw new InvalidDataException(
                    $"Expected directory at '{path}'.");
            }
            return;

        default:
            throw new InvalidOperationException();
    }
}
```

## 15.3 Test race seam

Fake creator:

```text
TryCreateOwned => AlreadyExists
```

and put a foreign file in that directory.

Rollback must:

```text
not delete directory
not track directory
preserve foreign file
```

Tests:

```text
CRUU12_012_Already_existing_directory_is_never_transaction_owned
CRUU12_012_Concurrent_directory_creator_foreign_content_is_preserved
CRUU12_012_CreatedByCaller_directory_is_rollback_owned
```

---

# 16. CRUU12-013 — remove temp→final bookkeeping ownership gap

## 16.1 Current sequence

```text
temp tracked
MoveNoOverwriteWriteThrough(temp, final)
PromoteCreatedFile(temp, final)
```

If move succeeds and bookkeeping throws:

```text
final exists
transaction list still names temp
rollback cannot find final
```

## 16.2 Track one file object with both identities

```csharp
internal enum MigrationOwnedFileState
{
    TempPlanned,
    TempOwned,
    FinalOwned
}

internal sealed class MigrationOwnedFile
{
    public required string TempPath { get; init; }
    public required string FinalPath { get; init; }
    public required long ExpectedLength { get; init; }
    public required string ExpectedSha256Hex { get; init; }

    public MigrationOwnedFileState State
        { get; private set; }

    public void MarkTempOwned()
    {
        if (State !=
            MigrationOwnedFileState.TempPlanned)
            throw new InvalidOperationException();

        State =
            MigrationOwnedFileState.TempOwned;
    }

    public void MarkFinalOwnedAfterMove()
    {
        // This operation must be non-throwing for a valid object.
        State =
            MigrationOwnedFileState.FinalOwned;
    }
}
```

Add object to transaction **before** creating temp.

```csharp
MigrationOwnedFile owned =
    tx.RegisterPlannedFile(
        tempPath,
        finalPath,
        expectedLength,
        expectedHash);
```

After CreateNew succeeds:

```csharp
owned.MarkTempOwned();
```

After move succeeds:

```csharp
owned.MarkFinalOwnedAfterMove();
```

No list search that can throw.

## 16.3 Rollback checks both paths conservatively

If state says TempOwned:

```text
temp expected
if final unexpectedly exists:
    verify exact expected bytes before deletion
```

If FinalOwned:

```text
verified-delete final
```

Never delete a mismatch.

## 16.4 Tests

```text
CRUU12_013_Move_success_before_bookkeeping_failure_final_is_recoverable
CRUU12_013_Unexpected_final_mismatch_is_preserved_and_reported
CRUU12_013_Transaction_has_no_FindIndex_promotion_path
```

---

# 17. CRUU12-014 — exact path is not enough to auto-delete migration controls/temps

## 17.1 Payload temp rule

Manifest knows final expected:

```text
length
SHA-256
role
```

A complete temp should match the final bytes.

Retry:

```csharp
private void DeletePayloadTempIfOwned(
    string targetRoot,
    MigrationManifestArtifact artifact)
{
    string path =
        ResolveManifestArtifactPath(
            targetRoot,
            artifact.TempRelativePath);

    StrictPathProbe probe =
        _strictPaths.Probe(path);

    if (probe.Kind ==
        StrictPathKind.Missing)
        return;

    if (probe.Kind !=
        StrictPathKind.File)
    {
        throw new InvalidDataException(
            $"Migration temp changed type: '{path}'.");
    }

    byte[] bytes =
        File.ReadAllBytes(path);

    string hash =
        Hash(bytes);

    if (bytes.LongLength !=
            artifact.Length ||
        !string.Equals(
            hash,
            artifact.Sha256Hex,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException(
            $"Migration temp '{path}' does not " +
            "match the exact attempt payload. " +
            "It was preserved.");
    }

    _verifiedDeleter.VerifyAndDelete(
        targetRoot,
        path,
        artifact.Length,
        artifact.Sha256Hex);
}
```

### Partial temp complication

A crash can legitimately leave a partial temp.

Do not auto-delete partial bytes unless ownership is independently proven.

Solutions:

**Preferred:** keep temp file handle ownership during live process; after
process death a partial temp is ambiguous and recovery preserves it with a
specific "manual cleanup/retry blocked" error.

Alternative: add durable per-artifact state before streaming, but a foreign
writer can still replace after crash.

Conservative preservation is acceptable.

## 17.2 Capability controls need content authority

Extend manifest control:

```csharp
public sealed class MigrationControlArtifact
{
    public string RelativePath { get; set; } = "";
    public MigrationControlArtifactKind Kind { get; set; }

    public long? ExpectedLength { get; set; }
    public string? ExpectedSha256Hex { get; set; }
}
```

For v4 probes:

```text
current     = UTF8("create")
replacement = UTF8("replace")
```

Builder stores exact hashes.

Retry deletes only exact bytes.

## 17.3 Stage control authority

Stage is JSON containing the Ready manifest.

Before deleting a stage:

```text
strict UTF-8 decode
strict manifest parse
AttemptId == marker AttemptId
TargetPhysicalRoot same
Phase == ReadyToCommit
same artifact/control set
```

Only then stage is attempt-owned.

If it does not parse/match:

```text
preserve
fail closed
```

## 17.4 Schema-v3 controls

Legacy control dirs/files:

```text
only exact v3 grammar
only empty directory or exact known legacy probe contents
```

Never recursive-delete a legacy probe directory containing unknown files.

## 17.5 Tests

```text
CRUU12_014_Declared_payload_temp_replaced_with_foreign_bytes_is_preserved
CRUU12_014_Exact_complete_payload_temp_is_verified_deleted
CRUU12_014_Partial_payload_temp_is_preserved_with_recovery_error
CRUU12_014_Declared_probe_control_wrong_content_is_preserved
CRUU12_014_Exact_probe_control_content_is_deleted
CRUU12_014_Stage_wrong_attempt_id_is_preserved
CRUU12_014_Stage_exact_ready_manifest_is_reconciled
CRUU12_014_Legacy_probe_directory_with_foreign_file_is_preserved
```

---

# 18. CRUU12-015 — schema-specific control grammar

Change:

```csharp
ValidateControlGrammar(
    manifest.SchemaVersion,
    manifest.AttemptId,
    control);
```

## 18.1 Schema 4 exact set

Only:

```text
.prompthelper-migration.stage-<attemptIdN>.tmp
.prompthelper-probe-<attemptIdN>-root-current.tmp
.prompthelper-probe-<attemptIdN>-root-replacement.tmp
prompts\.prompthelper-probe-<attemptIdN>-prompts-current.tmp
prompts\.prompthelper-probe-<attemptIdN>-prompts-replacement.tmp
```

No schema-v4 `CapabilityProbeDirectory`.

Copy-ready:

```csharp
private static void ValidateV4Control(
    Guid attemptId,
    MigrationControlArtifact control)
{
    string rel =
        NormalizeRelative(control.RelativePath);

    string[] allowedFiles =
    [
        $".prompthelper-probe-{attemptId:N}-root-current.tmp",
        $".prompthelper-probe-{attemptId:N}-root-replacement.tmp",
        Path.Combine(
            "prompts",
            $".prompthelper-probe-{attemptId:N}-prompts-current.tmp"),
        Path.Combine(
            "prompts",
            $".prompthelper-probe-{attemptId:N}-prompts-replacement.tmp")
    ];

    if (control.Kind ==
        MigrationControlArtifactKind
            .ManifestPhaseStaging)
    {
        RequireExact(
            rel,
            $".prompthelper-migration.stage-{attemptId:N}.tmp");
        return;
    }

    if (control.Kind ==
        MigrationControlArtifactKind
            .CapabilityProbeFile &&
        allowedFiles.Any(
            x => PathIdentityRelativeEquals(
                x,
                rel)))
    {
        return;
    }

    throw new InvalidDataException(
        $"Invalid schema-v4 control: '{rel}'.");
}
```

## 18.2 Schema 3 exact legacy set

Allow only the precise old planned locations.

Do not accept `probe-current.txt` merely by basename in arbitrary directories.

## 18.3 Tests

```text
CRUU12_015_V4_probe_arbitrary_suffix_rejected
CRUU12_015_V4_probe_directory_kind_rejected
CRUU12_015_V4_probe_file_wrong_directory_rejected
CRUU12_015_V3_legacy_probe_wrong_directory_rejected
CRUU12_015_V3_exact_legacy_controls_accepted
```

---

# 19. CRUU12-016 — use derived full payload fingerprint for schema-v3 retry

Current v3 loader computes/assigns a payload fingerprint derived from manifest
artifacts.

Retry must use it.

Remove schema branching that uses only primary hash as the main authority.

```csharp
if (!string.IsNullOrWhiteSpace(
        context.ExpectedSourcePayloadFingerprint))
{
    if (!string.Equals(
            context.ExpectedSourcePayloadFingerprint,
            manifest
                .SourcePayloadFingerprintSha256Hex,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException(
            "Source payload changed since the " +
            "interrupted migration attempt.");
    }
}
```

For v3, also retain primary SHA check when available as an extra invariant.

Tests:

```text
CRUU12_016_V3_same_library_json_changed_prompt_body_rejects_retry
CRUU12_016_V3_same_library_json_added_orphan_rejects_retry
CRUU12_016_V3_exact_payload_allows_retry
```

---

# 20. CRUU12-017 — target baseline must come from reservation authority

## 20.1 Current false field

Manifest builder runs after target reservation has created the target root.

It then probes:

```csharp
TargetRootExistedBefore =
    Probe(targetRoot) == Directory;
```

For a brand-new target this is now true.

## 20.2 Reservation exposes baseline

Add:

```csharp
public sealed record TargetReservationBaseline(
    bool RootExistedBefore,
    IReadOnlySet<string>
        CreatedDirectories);
```

`TargetRootReservation` stores:

```csharp
public TargetReservationBaseline Baseline
    { get; }
```

The reservation already knows which directories it created.

## 20.3 Builder accepts explicit baseline

```csharp
BuildCopying(
    source,
    target,
    snapshot,
    attemptId,
    probePlan,
    MigrationTargetBaseline baseline)
```

No probing in builder.

After old attempt recovery and target reinspection:

```csharp
var baseline =
    new MigrationTargetBaseline
    {
        TargetRootExistedBefore =
            reservation.Baseline
                .RootExistedBefore,

        PromptsDirectoryExistedBefore =
            StrictDirectoryExists(
                targetPrompts),

        RecoveryDirectoryExistedBefore =
            StrictDirectoryExists(
                targetRecovery)
    };
```

Children are captured after old-attempt cleanup as CRUU11 intended.

## 20.4 Root cleanup policy

Do not promise automatic deletion of a newly-created root after process-death
unless you have an authority path that can report cleanup failure after the
marker is gone.

Safe policy:

```text
all attempt files/children must be cleaned;
if root was attempt-created and ends empty, recovery MAY delete it only while
marker authority still exists and while no foreign entries exist;
if root deletion fails, keep marker and report;
do not retire marker first.
```

The `.app.lock` ownership complicates root deletion; order must be explicit.

For retry under reservation:

```text
do not delete root while reservation lock is held;
clean contents + marker
reservation.Release then performs owned root-chain cleanup and reports failures
```

Therefore manifest baseline is mainly documentary/recovery authority; the live
reservation remains root ownership authority.

## 20.5 Tests

```text
CRUU12_017_Brand_new_target_manifest_RootExistedBefore_false
CRUU12_017_Preexisting_empty_target_manifest_RootExistedBefore_true
CRUU12_017_Old_attempt_cleanup_does_not_change_original_root_baseline
```

---

# 21. CRUU12-018 / 019 — typed migration inventory context and strict enumeration

## 21.1 Current inventory conflates categories

It effectively groups:

```text
migration marker
.app.lock
settings files
mutation journal
initializing marker
manifest controls
```

into a generic control list.

ReadyGate then allows only two of them.

This both:

```text
rejects legitimate bootstrap settings
and
fails to model lifecycle conflicts explicitly.
```

## 21.2 New inventory model

```csharp
internal sealed record
    MigrationInventoryContext(
        bool IsExactBootstrapRoot,
        IReadOnlySet<string>
            AllowedPersistentRootPaths);

internal sealed record
    MigrationTargetInventory(
        IReadOnlyList<string> FinalArtifacts,
        IReadOnlyList<string> PayloadTemps,
        IReadOnlyList<string> AttemptControls,
        IReadOnlyList<string>
            AllowedPersistentControls,
        IReadOnlyList<string>
            ConflictingLifecycleControls,
        IReadOnlyList<string>
            AttemptCreatedDirectories,
        IReadOnlyList<string>
            PreExistingDirectories,
        IReadOnlyList<string>
            UnknownEntries);
```

## 21.3 Root classifications

At exact bootstrap, allowed persistent:

```text
settings.json
settings.backup.json
.settings.lock
```

During target reservation:

```text
.app.lock
```

Attempt control:

```text
.prompthelper-migration.json
manifest-declared stage/probes
```

Lifecycle conflicts:

```text
.prompthelper-library-mutation.json
initializing.marker
```

At non-bootstrap:

```text
settings.json / settings.backup.json / .settings.lock
are ordinary foreign/unknown files
```

## 21.4 ReadyGate allowances

Ready:

```text
AllowedPersistentControls => okay
.app.lock => okay
migration marker => okay
all other AttemptControls => must be absent
ConflictingLifecycleControls => must be zero
UnknownEntries => must be zero
```

## 21.5 Strict enumeration

Do not use:

```csharp
Directory.Exists
Directory.GetFiles
Directory.GetDirectories
```

as state classification where access failures could collapse.

Create `IStrictDirectoryEnumerator`.

```csharp
internal interface IStrictDirectoryEnumerator
{
    IReadOnlyList<string>
        EnumerateFileSystemEntries(
            string directory);
}
```

Implementation:

```csharp
public IReadOnlyList<string>
    EnumerateFileSystemEntries(
        string directory)
{
    StrictPathProbe probe =
        _paths.Probe(directory);

    if (probe.Kind ==
        StrictPathKind.Missing)
        return [];

    if (probe.Kind !=
        StrictPathKind.Directory)
        throw new InvalidDataException(...);

    // Exceptions propagate.
    return Directory
        .EnumerateFileSystemEntries(directory)
        .ToList();
}
```

Inventory uses strict probe for every returned entry.

## 21.6 Mandatory functional test: custom → default root

```text
current custom root healthy
bootstrap contains:
  settings.json pointing custom
  settings.backup.json
  .settings.lock possible
target is exact bootstrap root
no library.json there
select bootstrap target
migration succeeds
settings changes to default/null
restart succeeds
```

Named:

```text
CRUU12_018_Custom_to_empty_default_bootstrap_with_settings_controls_succeeds
```

Additional:

```text
CRUU12_018_Nonbootstrap_settings_json_is_foreign
CRUU12_018_Mutation_journal_is_lifecycle_conflict_not_allowed_control
CRUU12_019_Access_denied_prompts_inventory_fails_closed
CRUU12_019_Access_denied_recovery_inventory_fails_closed
```

---

# 22. CRUU12-020 — startup lifecycle journal conflict detector

## 22.1 Markers

```text
.prompthelper-migration.json
.prompthelper-library-mutation.json
initializing.marker
```

At most one recovery protocol may own startup mutation authority.

## 22.2 Detector

```csharp
internal sealed record
    LifecycleJournalPresence(
        bool Migration,
        bool Mutation,
        bool Initialization)
{
    public int Count =>
        (Migration ? 1 : 0) +
        (Mutation ? 1 : 0) +
        (Initialization ? 1 : 0);
}

internal sealed class
    RecoveryJournalConflictDetector
{
    private readonly StrictPathAuthority
        _paths = new();

    public LifecycleJournalPresence
        Inspect(AppPaths paths)
    {
        bool migration =
            RequireFileOrMissing(
                paths.MigrationMarkerPath);

        bool mutation =
            RequireFileOrMissing(
                paths.LibraryMutationJournalPath);

        bool init =
            RequireFileOrMissing(
                paths.InitializationMarkerPath);

        return new(
            migration,
            mutation,
            init);
    }

    private bool RequireFileOrMissing(
        string path)
    {
        StrictPathProbe probe =
            _paths.Probe(path);

        return probe.Kind switch
        {
            StrictPathKind.Missing => false,
            StrictPathKind.File => true,
            StrictPathKind.Directory =>
                throw new InvalidDataException(
                    $"Lifecycle marker is a directory: " +
                    $"'{path}'."),
            _ => throw new InvalidOperationException()
        };
    }
}
```

Startup after `.app.lock`, before any recovery mutation:

```csharp
LifecycleJournalPresence presence =
    conflictDetector.Inspect(paths);

if (presence.Count > 1)
{
    throw new InvalidDataException(
        "Multiple interrupted Prompt Helper " +
        "transaction journals exist. Automatic " +
        "recovery was stopped to protect data.");
}
```

## 22.3 Tests

All pairs + triple:

```text
migration + mutation
migration + init
mutation + init
all three
```

Assert:

```text
startup refuses
every marker exact bytes unchanged
library/body exact bytes unchanged
```

---

# 23. CRUU12-021 — strict UTF-8 migration authority

## 23.1 Remove auto-detecting StreamReader helper

Do not use:

```csharp
detectEncodingFromByteOrderMarks: true
```

It can accept UTF-16/UTF-32 BOM.

Replace every migration metadata decode with:

```csharp
StrictUtf8Text.Decode(
    bytes,
    description);
```

That helper may accept UTF-8 BOM only.

## 23.2 Source active bodies

After reading every prompt `.md`:

```csharp
byte[] bytes =
    _fileOps.ReadAllBytes(promptFile);

StrictUtf8Text.Decode(
    bytes,
    $"source prompt body '{promptFile}'");
```

For orphan `.md`, choose one locked policy.

Recommended:

```text
all prompts\*.md are Prompt Helper textual prompt artifacts;
strict UTF-8 is required even for orphan prompt bodies.
```

## 23.3 Target content inspection

When a metadata document references active body:

```csharp
byte[] body =
    _fileOps.ReadAllBytes(promptPath);

StrictUtf8Text.Decode(
    body,
    $"target prompt body '{promptPath}'");

hash...
```

This prevents a target from being selected successfully only to fail startup.

## 23.4 Capability backup read

Replace:

```csharp
File.ReadAllText(backupPath)
```

with:

```csharp
StrictUtf8Text.ReadAllText(
    backupPath,
    "target safety backup");
```

## 23.5 Recovery artifacts

These remain opaque bytes.

Do not require UTF-8 for arbitrary recovery diagnostics unless their format
is explicitly textual.

## 23.6 Tests

```text
CRUU12_021_UTF16_BOM_source_library_rejected
CRUU12_021_UTF32_BOM_source_library_rejected
CRUU12_021_Invalid_UTF8_source_active_body_rejected_before_manifest
CRUU12_021_Invalid_UTF8_source_orphan_md_rejected_before_manifest
CRUU12_021_Invalid_UTF8_existing_target_body_rejected_before_settings
CRUU12_021_UTF8_BOM_prompt_body_accepted_if_product_policy_allows_UTF8_BOM
```

---

# 24. CRUU12-022 / 023 — strict opened-directory handle identity

## 24.1 Directory opener must prove Directory

Use `GetFileInformationByHandleEx(FileBasicInfo)` or equivalent.

Example:

```csharp
private const int FileBasicInfo = 0;

[StructLayout(LayoutKind.Sequential)]
private struct FILE_BASIC_INFO
{
    public long CreationTime;
    public long LastAccessTime;
    public long LastWriteTime;
    public long ChangeTime;
    public uint FileAttributes;
}

private const uint
    FILE_ATTRIBUTE_DIRECTORY = 0x10;

private const uint
    FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
```

After open:

```csharp
FILE_BASIC_INFO basic =
    QueryBasicInfo(handle);

if ((basic.FileAttributes &
     FILE_ATTRIBUTE_DIRECTORY) == 0)
{
    handle.Dispose();
    throw new InvalidDataException(
        $"Expected directory but opened non-directory: " +
        $"'{path}'.");
}
```

## 24.2 Managed lease must open the object, not a followed reparse target

For `OpenManagedNodeLease` include:

```text
FILE_FLAG_BACKUP_SEMANTICS
FILE_FLAG_OPEN_REPARSE_POINT
```

Then reject reparse attribute.

## 24.3 Session lease validates final handle path

Eliminate the separate probe/open authority split.

```csharp
SafeFileHandle handle =
    opener.OpenManagedNodeLease(path);

try
{
    string final =
        WindowsFinalPathHelper
            .GetNormalizedDosPath(handle);

    if (!PathIdentity.Equals(
            final,
            path))
    {
        throw new InvalidDataException(
            $"Managed lease opened unexpected " +
            $"physical node. Expected='{path}', " +
            $"Actual='{final}'.");
    }

    lease._handles.Add(handle);
}
catch
{
    handle.Dispose();
    throw;
}
```

The opener itself already proves:

```text
directory
non-reparse
```

## 24.4 Tests

```text
CRUU12_022_Regular_file_is_not_returned_as_open_directory
CRUU12_022_OpenManagedNodeLease_reparse_directory_rejected
CRUU12_023_Session_lease_validates_final_handle_identity
CRUU12_023_Injected_swap_between_probe_and_open_cannot_bind_wrong_node
CRUU12_023_No_separate_StrictPathAuthority_probe_required_before_managed_open
```

---

# 25. CRUU12-024 — freeze source and target files through settings commit

## 25.1 Current residual race

Current:

```text
ReadyGate verifies source + target
write Ready marker
physical/tree revalidation
SaveIfUnchanged(settings)
```

Directory lease prevents deleting/renaming the directory node.

It does not prevent:

```text
write library.json contents
replace prompt body
write source orphan/recovery file
```

between Ready and settings commit.

## 25.2 Add `MigrationPayloadCommitLease`

Open every relevant source + target payload file.

Share mode:

```text
FILE_SHARE_READ
```

Do not grant WRITE or DELETE share.

Open ordinary file handle with:

```text
GENERIC_READ
OPEN_EXISTING
```

For each handle:

```text
not reparse
final physical path expected
length expected
hash expected
```

Hold all handles until:

```text
settings SaveIfUnchanged returns
tx.Commit
```

Then release.

## 25.3 Model

```csharp
internal sealed record
    PayloadLeaseItem(
        string Path,
        long Length,
        string Sha256Hex);

internal sealed class
    MigrationPayloadCommitLease
    : IDisposable
{
    private readonly List<FileStream>
        _streams = [];

    public static
        MigrationPayloadCommitLease Acquire(
            IEnumerable<PayloadLeaseItem>
                items)
    {
        var lease =
            new MigrationPayloadCommitLease();

        foreach (PayloadLeaseItem item
                 in items)
        {
            FileStream stream =
                new(
                    item.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

            ValidateStreamIdentityAndHash(
                stream,
                item);

            lease._streams.Add(stream);
        }

        return lease;
    }

    public void Dispose()
    {
        foreach (FileStream s in _streams)
            s.Dispose();

        _streams.Clear();
    }
}
```

Production should use native handle final-path checks rather than only
`FileStream.Name`.

## 25.4 Source item set

Use original snapshot:

```text
every MigrationPayloadFile source path
```

Target item set:

```text
every manifest final path
```

For an existing-library switch:

```text
effective metadata + every active body + any backup state whose fingerprint
was used to authorize selection
```

## 25.5 Sequence

```text
ReadyGate
Write Ready marker
physical/tree revalidation
Acquire source+target payload commit lease
reverify via handles
SaveIfUnchanged settings
tx.Commit
release payload lease
```

## 25.6 Tests

Real Windows:

```text
CRUU12_024_Target_library_write_fails_while_commit_lease_held
CRUU12_024_Target_prompt_replace_fails_while_commit_lease_held
CRUU12_024_Source_prompt_write_fails_while_commit_lease_held
CRUU12_024_Prelease_mutation_detected_by_hash_validation
CRUU12_024_Settings_not_committed_if_commit_lease_cannot_be_acquired
```



---

# 26. CRUU12-025 — rollback must validate the entire terminal control inventory before retiring the marker

## 26.1 Current defect

The live rollback path determines whether residue remains primarily by checking manifest payload finals and payload temps. That is not enough.

A failed Ready-manifest stage operation or capability-probe cleanup can leave:

```text
.prompthelper-migration.stage-<attemptId>.tmp
planned root probe file
planned prompts probe file
other manifest-declared attempt control
```

If payload finals/temps are gone, the current logic can still retire:

```text
.prompthelper-migration.json
```

and therefore destroy the durable authority needed to explain and clean the remaining control artifact.

The invariant must be:

```text
THE MIGRATION MARKER IS THE LAST ATTEMPT-OWNED ARTIFACT RETIRED.
```

Not:

```text
the marker is retired when payload files appear clean.
```

## 26.2 Add one terminal rollback verifier

Create:

```csharp
internal sealed record MigrationRollbackTerminalState(
    bool IsClean,
    IReadOnlyList<string> RemainingPayloadFinals,
    IReadOnlyList<string> RemainingPayloadTemps,
    IReadOnlyList<string> RemainingAttemptControls,
    IReadOnlyList<string> RemainingAttemptDirectories,
    IReadOnlyList<string> UnknownEntries);

internal sealed class MigrationRollbackTerminalVerifier
{
    public MigrationRollbackTerminalState Inspect(
        string targetPhysicalRoot,
        MigrationAttemptManifest manifest,
        MigrationInventoryContext inventoryContext)
    {
        MigrationTargetInventory inventory =
            MigrationTargetInventoryInspector.Inspect(
                targetPhysicalRoot,
                manifest,
                inventoryContext);

        return new MigrationRollbackTerminalState(
            IsClean:
                inventory.FinalArtifacts.Count == 0 &&
                inventory.PayloadTemps.Count == 0 &&
                inventory.AttemptControls
                    .Where(x =>
                        !PathIdentity.Equals(
                            x,
                            Path.Combine(
                                targetPhysicalRoot,
                                ".prompthelper-migration.json")))
                    .Count() == 0 &&
                inventory.AttemptCreatedDirectories.Count == 0 &&
                inventory.UnknownEntries.Count == 0 &&
                inventory.ConflictingLifecycleControls.Count == 0,

            RemainingPayloadFinals:
                inventory.FinalArtifacts,

            RemainingPayloadTemps:
                inventory.PayloadTemps,

            RemainingAttemptControls:
                inventory.AttemptControls,

            RemainingAttemptDirectories:
                inventory.AttemptCreatedDirectories,

            UnknownEntries:
                inventory.UnknownEntries);
    }
}
```

Do not consider the marker itself a cleanup failure while the verifier is running.

## 26.3 Required rollback order

Use this exact order:

```text
1. stop producing new migration output
2. rollback transaction-owned payload files
3. reconcile current invocation's capability probes
4. reconcile Ready/stage file
5. reconcile attempt-created child directories
6. inspect full target inventory
7. if ANY attempt residue or unknown entry remains:
       preserve migration marker
       return/throw typed rollback failure
8. delete migration marker
9. verify marker Missing
10. release target operation lease
11. release target reservation
```

Do not release the target operation lease before terminal inventory verification.

## 26.4 Typed failure

```csharp
internal sealed class MigrationTerminalCleanupException
    : IOException
{
    public MigrationRollbackTerminalState State { get; }

    public MigrationTerminalCleanupException(
        string targetRoot,
        MigrationRollbackTerminalState state)
        : base(
            "Migration rollback could not return the target " +
            "to its pre-attempt state. The migration marker " +
            "was preserved for safe recovery.")
    {
        Data["TargetRoot"] = targetRoot;
        State = state;
    }
}
```

## 26.5 Copy-ready replacement pattern

Replace the ad-hoc `residueRemains` loop with:

```csharp
MigrationRollbackResult rollback = tx.Rollback();

var cleanupFailures =
    new List<MigrationRollbackFailure>(
        rollback.Failures);

cleanupFailures.AddRange(
    _probeCleanup.ReconcileAttemptControls(
        bound.PhysicalRoot,
        manifest));

cleanupFailures.AddRange(
    _stageCleanup.ReconcileStage(
        bound.PhysicalRoot,
        manifest));

MigrationRollbackTerminalState terminal =
    _rollbackTerminalVerifier.Inspect(
        bound.PhysicalRoot,
        manifest,
        inventoryContext);

if (!terminal.IsClean ||
    cleanupFailures.Count > 0)
{
    TargetReservationCleanupResult reservationCleanup =
        reservation.Release();

    cleanupFailures.AddRange(
        reservationCleanup.Failures);

    throw new MigrationRollbackException(
        original,
        bound.PhysicalRoot,
        cleanupFailures);
}

_manifestRepo.DeleteStrict(markerPath);

if (_authorityOps.GetPresenceStrict(markerPath) !=
    StrictFilePresence.Missing)
{
    throw new IOException(
        "Migration marker could not be retired after " +
        "a clean rollback.");
}
```

Important: if `reservation.Release()` is required to remove an attempt-created root, do not delete the marker first unless marker location/root lifetime has been deliberately designed around that. Prefer leaving the root with marker over deleting the marker early.

## 26.6 Tests

```text
CRUU12_025_Rollback_stage_residue_preserves_marker
CRUU12_025_Rollback_probe_residue_preserves_marker
CRUU12_025_Rollback_payload_temp_residue_preserves_marker
CRUU12_025_Rollback_unknown_entry_preserves_marker
CRUU12_025_Clean_terminal_inventory_deletes_marker_last
CRUU12_025_Marker_delete_failure_is_reported_after_clean_inventory
```

Every residue test must assert exact marker bytes remain unchanged.

---

# 27. CRUU12-026 — remove wildcard stale capability-file deletion

## 27.1 Current defect

The non-planned capability path currently scans:

```text
.prompthelper-capability-*.tmp
```

and deletes matching files before creating a new probe.

That namespace is too broad to prove ownership. A foreign file with a matching basename can be deleted.

Swallowed enumeration/deletion exceptions also make the behavior impossible to audit.

## 27.2 Locked fix

Delete this entire behavior:

```csharp
foreach (string file in
    _fileOps.EnumerateFiles(
        directory,
        ".prompthelper-capability-*.tmp"))
{
    _fileOps.DeleteFile(file);
}
```

A fresh probe uses a fresh cryptographic/non-reused nonce.

Therefore stale random probe files do **not** need pre-cleaning for correctness.

Policy:

```text
random historical capability-lookalikes:
    preserve
    optionally surface maintenance warning

current invocation's exact random probe files:
    cleanup using explicit ownership/hash authority

manifest-planned probe files:
    cleanup using manifest authority
```

## 27.3 Optional strict stale detector

If you want diagnostics:

```csharp
internal sealed record CapabilityProbeResidue(
    string Path,
    bool NameMatchesCurrentGrammar);

internal IReadOnlyList<CapabilityProbeResidue>
    FindCapabilityLookalikes(string directory)
{
    // Enumerate only.
    // Never delete here.
}
```

Warnings may say:

```text
"One or more old Prompt Helper capability-probe-looking files were found.
They were preserved because this process cannot prove ownership."
```

## 27.4 Tests

```text
CRUU12_026_Foreign_capability_lookalike_is_never_deleted
CRUU12_026_New_probe_does_not_require_stale_cleanup
CRUU12_026_Enumeration_failure_is_observable_not_swallowed
CRUU12_026_Current_probe_cleanup_does_not_touch_other_nonce
```

Static assertion:

```text
no production call to EnumerateFiles(... ".prompthelper-capability-*.tmp")
followed by unconditional deletion
```

---

# 28. CRUU12-027 — capability cleanup must verify the object/content it owns

## 28.1 Current defect

The code tracks booleans:

```text
currentCreated
replacementCreated
```

and later deletes those paths.

If the path is replaced after creation, the boolean proves historical creation only—not current-object ownership.

## 28.2 Add explicit probe artifact authority

```csharp
internal sealed record ProbeArtifactAuthority(
    string Path,
    byte[] ExpectedBytes)
{
    public long Length => ExpectedBytes.LongLength;

    public string Sha256Hex =>
        Convert.ToHexStringLower(
            SHA256.HashData(ExpectedBytes));
}
```

Current:

```csharp
new ProbeArtifactAuthority(
    currentFile,
    StrictUtf8Text.Encode("create"));
```

Replacement:

```csharp
new ProbeArtifactAuthority(
    replacementFile,
    StrictUtf8Text.Encode("replace"));
```

## 28.3 Cleanup through verified deleter

```csharp
private void CleanupOwnedProbe(
    string physicalRoot,
    ProbeArtifactAuthority artifact,
    List<MigrationRollbackFailure> failures)
{
    try
    {
        _verifiedDeleter.VerifyAndDelete(
            physicalRoot,
            artifact.Path,
            artifact.Length,
            artifact.Sha256Hex);
    }
    catch (FileNotFoundException)
    {
        // Already absent is okay only if your verified deleter
        // has an explicit Missing contract.
    }
    catch (Exception ex)
    {
        failures.Add(
            new MigrationRollbackFailure(
                artifact.Path,
                "DeleteVerifiedCapabilityProbe",
                ex.Message));
    }
}
```

If `WindowsVerifiedArtifactDeleter` currently treats Missing as success, document that contract. Otherwise add:

```csharp
VerifiedDeleteResult
{
    Missing,
    DeletedExact,
    RejectedMismatch
}
```

A mismatch must never be converted into success.

## 28.4 Replace operation ownership transfer

After:

```csharp
_fileOps.Replace(
    replacementFile,
    currentFile,
    null);
```

the content at `currentFile` must be the exact replacement bytes.

Update authority:

```text
current path expected bytes = "replace"
replacement path expected = Missing
```

Then delete `currentFile` with verified content authority.

## 28.5 Tests

```text
CRUU12_027_Probe_current_replaced_after_creation_is_preserved
CRUU12_027_Probe_replacement_replaced_after_creation_is_preserved
CRUU12_027_Post_replace_current_requires_replace_hash
CRUU12_027_Exact_probe_file_is_verified_deleted
CRUU12_027_Probe_cleanup_mismatch_becomes_cleanup_failure
```

---

# 29. CRUU12-028 — make LibraryRepository payload APIs impossible to misuse

## 29.1 Current defect

These APIs remain dangerous:

```csharp
public CommitResult SynchronizeBackup(
    LibraryDocument document)

public CommitResult CommitCanonicalBytes(
    LibraryDocument document,
    byte[] canonicalBytes)
```

The second accepts two independently supplied authorities:

```text
document A
bytes B
```

It writes B to primary and synchronizes backup from A.

That API permits split-brain metadata by construction.

## 29.2 Introduce one sealed payload type

```csharp
internal sealed record CanonicalLibraryPackage(
    LibraryDocument Document,
    byte[] CanonicalBytes,
    string Sha256Hex)
{
    public static CanonicalLibraryPackage Create(
        LibraryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        LibraryValidator.Validate(document);

        LibraryDocument clone =
            LibraryDocumentCloner.Clone(document);

        string json =
            JsonSerializer.Serialize(
                clone,
                LibraryRepository.JsonOptions);

        byte[] bytes =
            StrictUtf8Text.Encode(json);

        return new CanonicalLibraryPackage(
            clone,
            bytes,
            Convert.ToHexStringLower(
                SHA256.HashData(bytes)));
    }
}
```

The constructor should be private if practical:

```csharp
private CanonicalLibraryPackage(...)
```

so callers cannot inject mismatched bytes.

## 29.3 Repository API

```csharp
internal CanonicalLibraryPackage
    CreateCanonicalPackage(
        LibraryDocument document)
    => CanonicalLibraryPackage.Create(document);

internal CommitResult Commit(
    CanonicalLibraryPackage package)
{
    ValidatePrimaryReplacementAuthority();

    _durableWriter.ReplaceDurable(
        _paths.LibraryPath,
        package.CanonicalBytes,
        DurableFileClass.LibraryMetadata);

    return SynchronizeBackup(
        package);
}

internal CommitResult SynchronizeBackup(
    CanonicalLibraryPackage package)
{
    // Same package bytes.
}
```

For startup health-authorized backup sync, create a distinct stronger type:

```csharp
internal sealed record HealthyCanonicalLibraryPackage(
    CanonicalLibraryPackage Canonical,
    IReadOnlyDictionary<Guid, PromptBodySnapshot> Bodies);
```

Only `LibraryPackageInspector` can construct it.

## 29.4 Remove bare public overload

Delete or make inaccessible:

```csharp
SynchronizeBackup(LibraryDocument)
```

The weak model must update all call sites.

## 29.5 Mutation coordinator

Construct once:

```csharp
CanonicalLibraryPackage oldPackage =
    _libraryRepo.CreateCanonicalPackage(current);

CanonicalLibraryPackage newPackage =
    _libraryRepo.CreateCanonicalPackage(candidate);
```

But remember CRUU12-002: old journal hash comes from exact disk bytes, not `oldPackage.Sha256Hex`.

New durable primary bytes come from:

```csharp
newPackage.CanonicalBytes
```

Backup gets the same bytes.

## 29.6 Tests

```text
CRUU12_028_No_repository_API_accepts_document_and_independent_bytes
CRUU12_028_Primary_and_backup_commit_use_same_CanonicalLibraryPackage_bytes
CRUU12_028_Bare_SynchronizeBackup_LibraryDocument_API_removed
CRUU12_028_CanonicalLibraryPackage_clones_document_before_serialization
```

Compile-time API tests may use Roslyn or simple source assertions if needed, but behavioral tests are still required.

---

# 30. CRUU12-029 — remove production compatibility adapters that downgrade safety contracts

## 30.1 Current unsafe adapters

Examples include adapters with semantics equivalent to:

```text
CreateNewDurable:
    if File.Exists(path) throw
    then generic writer.Write(path)
```

This is check-then-write rather than atomic create-no-overwrite.

Another adapter named as verified deletion can simply call:

```text
DeleteIfExists(path)
```

without verifying hash, containment, or reparse state.

A settings adapter can route durable writes through the old atomic text writer,
which does not prove the same durability contract.

These are dangerous even if intended only for older tests because public/
production constructors can select them.

## 30.2 Locked policy

Production `src/PromptHelper` must have exactly one semantic contract per safety primitive:

```text
IDurableAtomicFileWriter       = durable + atomic + no-overwrite when requested
IVerifiedArtifactDeleter       = containment + non-reparse + exact length/hash
IDurableSettingsFileWriter     = durable write contract
```

No adapter may advertise one of these interfaces while intentionally doing less.

## 30.3 Preferred implementation

Remove from production:

```text
AtomicTextWriterDurableAdapter
FileDeleterVerifiedAdapter
AtomicTextWriterSettingsDurableAdapter
```

Then remove public constructors accepting weaker interfaces:

```csharp
PromptRepository(
    AppPaths,
    IAtomicTextWriter,
    IFileDeleter)

LibraryRepository(
    AppPaths,
    IAtomicTextWriter)

AppSettingsRepository(
    IAtomicTextWriter, ...)
```

Tests inject real fakes of the strong interfaces.

## 30.4 Copy-ready test fakes

```csharp
internal sealed class FakeDurableAtomicFileWriter
    : IDurableAtomicFileWriter
{
    public Action<string, byte[], DurableFileClass>?
        OnReplace { get; set; }

    public Action<string, byte[], DurableFileClass>?
        OnCreateNew { get; set; }

    public void ReplaceDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        byte[] copy = bytes.ToArray();

        if (OnReplace is not null)
        {
            OnReplace(
                targetPath,
                copy,
                fileClass);
            return;
        }

        new WindowsDurableAtomicFileWriter()
            .ReplaceDurable(
                targetPath,
                copy,
                fileClass);
    }

    public void CreateNewDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        byte[] copy = bytes.ToArray();

        if (OnCreateNew is not null)
        {
            OnCreateNew(
                targetPath,
                copy,
                fileClass);
            return;
        }

        new WindowsDurableAtomicFileWriter()
            .CreateNewDurable(
                targetPath,
                copy,
                fileClass);
    }
}
```

Verified-deleter fake:

```csharp
internal sealed class RecordingVerifiedDeleter
    : IVerifiedArtifactDeleter
{
    public List<(string Root,
                 string Path,
                 long Length,
                 string Hash)> Calls { get; } = [];

    public Action<string, string, long, string>?
        OnDelete { get; set; }

    public void VerifyAndDelete(
        string physicalRoot,
        string filePath,
        long expectedLength,
        string expectedSha256Hex)
    {
        Calls.Add(
            (physicalRoot,
             filePath,
             expectedLength,
             expectedSha256Hex));

        OnDelete?.Invoke(
            physicalRoot,
            filePath,
            expectedLength,
            expectedSha256Hex);
    }
}
```

Do not put weaker semantic fallbacks inside these fakes unless that individual
test explicitly owns all paths and is not testing durability/authority.

## 30.5 Tests / static gates

```text
CRUU12_029_No_public_constructor_accepts_IAtomicTextWriter_for_persistence
CRUU12_029_No_FileDeleterVerifiedAdapter_in_production
CRUU12_029_No_CreateNewDurable_implementation_uses_FileExists_precheck_then_replace
CRUU12_029_All_App_production_composition_uses_WindowsDurableAtomicFileWriter
```

---

# 31. CRUU12-030 — maintenance failures and recovery warnings must be surfaced

## 31.1 Current observability gaps

Examples:

```text
App catches every orphan-reconcile exception and discards it
mutation recovery can return Warning but startup does not show it
best-effort cleanup paths often swallow failures without a durable/user signal
```

Conservative preservation is correct. Invisible preservation is not.

## 31.2 Add startup maintenance diagnostics

```csharp
internal enum StartupDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

internal sealed record StartupDiagnostic(
    string Code,
    StartupDiagnosticSeverity Severity,
    string Message);

internal sealed class StartupDiagnosticCollector
{
    private readonly List<StartupDiagnostic> _items = [];

    public IReadOnlyList<StartupDiagnostic> Items => _items;

    public void Warning(
        string code,
        string message)
        => _items.Add(
            new StartupDiagnostic(
                code,
                StartupDiagnosticSeverity.Warning,
                message));
}
```

## 31.3 Orphan reconciler

Change from:

```csharp
catch
{
}
```

to:

```csharp
catch (Exception ex) when (
    ex is IOException or
    UnauthorizedAccessException or
    SecurityException or
    InvalidDataException or
    UnsupportedLibrarySchemaException)
{
    diagnostics.Warning(
        "ORPHAN_RECONCILIATION_DEFERRED",
        "Prompt Helper preserved possible orphan prompt files " +
        "because cleanup authority could not be established. " +
        ex.Message);
}
```

Do not catch programmer exceptions.

## 31.4 Mutation recovery warning

Immediately add:

```csharp
if (!string.IsNullOrWhiteSpace(mutResult.Warning))
{
    diagnostics.Warning(
        "MUTATION_RECOVERY_WARNING",
        mutResult.Warning);
}
```

## 31.5 Display aggregation

Avoid 5 sequential MessageBoxes.

```csharp
string maintenanceWarning =
    string.Join(
        "\r\n\r\n",
        diagnostics.Items
            .Where(x =>
                x.Severity ==
                StartupDiagnosticSeverity.Warning)
            .Select(x =>
                $"[{x.Code}] {x.Message}"));
```

Display one post-window warning.

## 31.6 Optional diagnostic log

A local offline app can additionally write:

```text
%LOCALAPPDATA%\PromptHelper\diagnostics\startup-YYYYMMDD.log
```

but this is optional. Do not add telemetry.

If implemented, log writing itself must not become startup-blocking.

## 31.7 Tests

```text
CRUU12_030_Orphan_reconcile_io_failure_produces_warning
CRUU12_030_Mutation_recovery_warning_reaches_startup_diagnostics
CRUU12_030_Programmer_exception_is_not_swallowed_by_maintenance_catch
CRUU12_030_Multiple_startup_warnings_are_aggregated
```

---

# 32. CRUU12-031 — initialization must have its own durable control semantics

## 32.1 Current issues

The initialization marker uses a generic/mismatched durable class and is created
with replacement semantics.

Marker deletion is best effort.

The marker is a transaction authority and deserves its own exact policy.

## 32.2 Use `InitializationControl`

Current enum already contains:

```csharp
DurableFileClass.InitializationControl
```

Use it.

## 32.3 Strict create-new marker

First run:

```csharp
_writer.CreateNewDurable(
    _paths.InitializationMarkerPath,
    StrictUtf8Text.Encode(
        "schemaVersion=1\r\n"),
    DurableFileClass.InitializationControl);
```

Do not use `ReplaceDurable`.

If marker appears concurrently:

```text
fail closed / treat as interrupted initialization
```

Do not overwrite it.

## 32.4 Better marker format

Recommended JSON:

```json
{
  "schemaVersion": 1,
  "initializationId": "00000000-0000-0000-0000-000000000000",
  "phase": "CreatingDefaults"
}
```

Model:

```csharp
internal sealed class InitializationJournal
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } =
        CurrentSchemaVersion;

    public Guid InitializationId { get; set; }

    public InitializationPhase Phase { get; set; }
}

internal enum InitializationPhase
{
    CreatingDefaults,
    MetadataDurable
}
```

This is preferable to an unstructured marker.

## 32.5 Commit semantics

```text
journal CreatingDefaults durable
create missing default bodies using CreateNew
commit default primary + backup
journal MetadataDurable durable
retire journal
```

Recovery:

```text
CreatingDefaults:
    verify every existing default body exact
    preserve unknown/mismatched body and stop
    create missing defaults
    commit metadata
    advance MetadataDurable

MetadataDurable:
    verify primary + required default bodies
    retire journal
```

## 32.6 Marker delete failure

After primary/default package is committed, inability to retire initialization
journal is **postcommit cleanup**, not a reason to destroy data.

Return warning:

```text
"Initialization completed, but Prompt Helper could not retire its
initialization journal. Restart will re-verify the completed initialization."
```

## 32.7 Tests

```text
CRUU12_031_First_run_uses_CreateNew_for_initialization_journal
CRUU12_031_Preexisting_initialization_journal_is_never_overwritten
CRUU12_031_Crash_after_some_default_bodies_recovers
CRUU12_031_Crash_after_metadata_before_journal_retire_finalizes
CRUU12_031_Foreign_default_GUID_body_is_preserved_and_startup_stops
CRUU12_031_Journal_delete_failure_after_commit_returns_warning
```

---

# 33. CRUU12-032 — replace false-positive sentinels with tests that execute the named property

## 33.1 Rule

A regression test named:

```text
X_happens
```

must make `X` happen.

It is not sufficient to:

```text
instantiate the normal success path
assert a related helper boolean
compare two strings
comment that a property is true
```

## 33.2 Known weak CRUU11 sentinel patterns to replace

### A. Buffer-resize test

Current name claims API buffer retry but simply calls the helper on a normal
path.

Required seam:

```csharp
internal interface IFinalPathNativeApi
{
    uint GetFinalPathNameByHandle(
        SafeFileHandle handle,
        char[] buffer,
        uint bufferLength,
        uint flags);
}
```

Fake:

```csharp
internal sealed class ResizeDemandFinalPathApi
    : IFinalPathNativeApi
{
    public int Calls { get; private set; }

    public uint GetFinalPathNameByHandle(
        SafeFileHandle handle,
        char[] buffer,
        uint bufferLength,
        uint flags)
    {
        Calls++;

        const string final =
            @"\\?\C:\Very\Long\Final\Path";

        if (Calls == 1)
        {
            return (uint)(bufferLength + 50);
        }

        final.CopyTo(
            0,
            buffer,
            0,
            final.Length);

        return (uint)final.Length;
    }
}
```

Assert:

```text
Calls >= 2
returned normalized final path exact
```

### B. Reparse-artifact rejection test

Do not test a regular file.

Create an actual file symlink on Windows:

```text
cmd /c mklink <link> <target>
```

or use `File.CreateSymbolicLink` when supported.

Call verified deleter on the **link path**.

Assert:

```text
throws
link remains
target remains
target bytes unchanged
```

### C. Settings durable-writer test

Inject a recording `IDurableSettingsFileWriter`.

Arrange corrupt primary + valid backup.

Assert:

```text
recorded WriteDurable target == settings.json
bytes/text exactly recovered settings
no weaker writer called
```

### D. Duplicate state-machine test

Fault after duplicate body durable but before metadata.

Then call recovery.

Assert:

```text
duplicate body removed
metadata old
journal retired
source prompt unchanged
```

Also fault after metadata durable:

```text
duplicate preserved
metadata new
```

### E. Evidence-script tests

Current C# `HashSet` examples do not prove the PowerShell script.

Best fix: extract a testable .NET `TrxEvidenceVerifier` used by both test code and
the PowerShell wrapper.

If retaining PowerShell logic, run it.

Windows integration helper:

```csharp
private static ProcessResult RunPwsh(
    string script,
    params string[] args)
{
    var psi = new ProcessStartInfo
    {
        FileName = "pwsh",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    psi.ArgumentList.Add("-NoProfile");
    psi.ArgumentList.Add("-File");
    psi.ArgumentList.Add(script);

    foreach (string arg in args)
        psi.ArgumentList.Add(arg);

    using Process p = Process.Start(psi)!;
    string stdout = p.StandardOutput.ReadToEnd();
    string stderr = p.StandardError.ReadToEnd();

    p.WaitForExit();

    return new ProcessResult(
        p.ExitCode,
        stdout,
        stderr);
}
```

Create synthetic TRX with:

```text
NOT_REALLY_RequiredTest
```

Require:

```text
RequiredTest
```

Assert non-zero exit.

Create TRX missing one sentinel. Assert non-zero.

Create exact passing TRX. Assert zero.

## 33.3 Add test-scenario metadata

For high-risk regression tests, use:

```csharp
[TestCategory("CRUU12")]
[TestCategory("CrashRecovery")]
[TestProperty("Scenario", "PostPrimaryCommitJournalAdvanceFailure")]
```

This is optional metadata but useful for audit.

## 33.4 Anti-cheating rule for required tests

A required sentinel must satisfy at least one:

```text
- invokes production API under test and asserts externally observable result;
- injects the exact documented fault and asserts final persistent bytes/state;
- performs a real Windows integration action required by the finding.
```

Not allowed:

```text
Assert.IsTrue(true)
string equality proving naming
HashSet subset proving what a script should do
comments as evidence
testing only a helper clone of production logic
```

## 33.5 Required replacement tests

```text
CRUU12_032_FinalPath_buffer_resize_is_actually_forced
CRUU12_032_Verified_deleter_actual_file_symlink_is_rejected
CRUU12_032_Settings_recovery_calls_injected_durable_writer
CRUU12_032_Duplicate_fault_before_metadata_recovers_old_state
CRUU12_032_Duplicate_fault_after_metadata_keeps_new_state
CRUU12_032_Evidence_script_rejects_substring_only_TRX
CRUU12_032_Evidence_script_rejects_missing_required_TRX
CRUU12_032_Evidence_script_accepts_exact_passed_TRX
```

---

# 34. CRUU12-033 — prove SVG → ICO → EXE identity without relying on renderer coincidence

## 34.1 Current state

Current strict release can compare:

```text
committed PromptHelper.ico
→ published PromptHelper.exe icon pixels
```

That is useful.

It does not prove that the committed ICO corresponds to the approved SVG.

The generation script also starts with a 256x256 raster and auto-downsizes,
which is not an ideal source-of-truth pipeline for small icon sizes.

## 34.2 Add an approved icon identity manifest

File:

```text
src/PromptHelper/Assets/PromptHelperIconIdentity.json
```

Schema:

```json
{
  "schemaVersion": 1,
  "sourceSvgSha256": "<64 hex>",
  "frames": [
    { "size": 16,  "rgbaSha256": "<64 hex>" },
    { "size": 24,  "rgbaSha256": "<64 hex>" },
    { "size": 32,  "rgbaSha256": "<64 hex>" },
    { "size": 48,  "rgbaSha256": "<64 hex>" },
    { "size": 64,  "rgbaSha256": "<64 hex>" },
    { "size": 128, "rgbaSha256": "<64 hex>" },
    { "size": 256, "rgbaSha256": "<64 hex>" }
  ]
}
```

This manifest becomes the checked-in release identity capsule.

It proves:

```text
exact approved SVG bytes
expected normalized RGBA for each ICO frame
```

Then existing PE comparison proves:

```text
ICO normalized RGBA
== EXE normalized RGBA
```

## 34.3 Extend verifier commands

Add:

```text
verify-approved-ico <identity.json> <source.svg> <actual.ico>
compare-exe        <expected.ico>  <PromptHelper.exe>
```

Pseudo-code:

```csharp
IconIdentityManifest manifest =
    ReadStrictManifest(identityPath);

string svgHash =
    Hash(File.ReadAllBytes(svgPath));

RequireEqual(
    manifest.SourceSvgSha256,
    svgHash,
    "approved source SVG hash");

Dictionary<(int,int), string>
    icoFrames =
        IcoReader.ReadFrames(icoPath);

foreach (IconFrameIdentity frame
         in manifest.Frames)
{
    string actual =
        icoFrames[
            (frame.Size, frame.Size)];

    RequireEqual(
        frame.RgbaSha256,
        actual,
        $"{frame.Size}x{frame.Size}");
}
```

## 34.4 Do not auto-update identity manifest in CI

Generating/updating the manifest is an explicit release-authority action.

CI only verifies.

Otherwise a bad regenerated icon and bad regenerated manifest could self-approve.

## 34.5 Improve generation script

Render each size from SVG separately.

Conceptual PowerShell:

```powershell
$sizes = @(16,24,32,48,64,128,256)
$temp = Join-Path $env:TEMP ("PromptHelperIcon-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    $pngs = @()

    foreach ($size in $sizes) {
        $png = Join-Path $temp "icon-$size.png"

        & magick `
            -background none `
            $effectiveSourceSvg `
            -resize "${size}x${size}" `
            -gravity center `
            -extent "${size}x${size}" `
            $png

        if ($LASTEXITCODE -ne 0) {
            throw "Failed rendering $size x $size frame"
        }

        $pngs += $png
    }

    & magick $pngs $effectiveOutputIco

    if ($LASTEXITCODE -ne 0) {
        throw "Failed assembling ICO"
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
```

Pin or record the ImageMagick version used for release generation.

Do not require CI to reproduce pixels from SVG using an unpinned renderer;
verify the approved identity manifest instead.

## 34.6 PE group selection

Do not simply choose "first RT_GROUP_ICON".

Preferred:

```text
enumerate all icon groups
identify the application icon group that contains the expected mandatory frame set
require exactly one matching full group
compare that group
```

If zero:

```text
fail
```

If multiple differing groups could be interpreted as application icon:

```text
fail closed / require explicit resource ID
```

Option:

```text
store expected RT_GROUP_ICON resource ID in release config
```

## 34.7 CI strict gate

Strict release sequence:

```powershell
dotnet run --project tools/IconIdentityVerifier/IconIdentityVerifier.csproj -- `
    verify-approved-ico `
    src/PromptHelper/Assets/PromptHelperIconIdentity.json `
    src/PromptHelper/Assets/PromptHelperLogo.svg `
    src/PromptHelper/Assets/PromptHelper.ico

dotnet publish ...

dotnet run --project tools/IconIdentityVerifier/IconIdentityVerifier.csproj -- `
    compare-exe `
    src/PromptHelper/Assets/PromptHelper.ico `
    artifacts/publish-check/PromptHelper.exe
```

## 34.8 Tests

```text
CRUU12_033_Identity_manifest_wrong_SVG_hash_fails
CRUU12_033_Identity_manifest_wrong_16px_RGBA_hash_fails
CRUU12_033_Identity_manifest_missing_required_size_fails
CRUU12_033_ICO_exact_manifest_passes
CRUU12_033_EXE_wrong_icon_group_fails
CRUU12_033_EXE_exact_full_group_passes
```

---

# 35. CRUU12-034 — approved real logo remains a release blocker

This is intentionally **not** something the implementing AI may solve by
inventing artwork.

Required files:

```text
src/PromptHelper/Assets/PromptHelperLogo.svg
src/PromptHelper/Assets/PromptHelper.ico
src/PromptHelper/Assets/PromptHelperIconIdentity.json
```

The SVG must come from the actual approved Prompt Helper artwork.

## 35.1 Implementing-AI rule

If the approved SVG is not supplied:

```text
DO NOT:
- draw a placeholder;
- generate an AI logo;
- copy a generic icon;
- create an empty SVG;
- create a colored square to satisfy tests;
- weaken RequireIcon;
- remove release-gate checks;
- mark strict release as passed.
```

Instead:

```text
SOURCE/TEST FIXES = may complete
NORMAL CI         = may pass
STRICT RELEASE    = BLOCKED_EXTERNAL_ASSET
```

## 35.2 Once the approved SVG exists

Exact operator workflow:

```powershell
pwsh -NoProfile -File .\tools\GenerateAppIcon.ps1 `
    -SourceSvg .\src\PromptHelper\Assets\PromptHelperLogo.svg `
    -OutputIco .\src\PromptHelper\Assets\PromptHelper.ico
```

Then explicitly create/update:

```text
PromptHelperIconIdentity.json
```

using an audited one-shot identity generation command.

Review:

```text
SVG diff
ICO frame preview at every mandatory size
identity-manifest diff
```

Then strict verification.

## 35.3 Acceptance

```text
CRUU12-034 remains OPEN until approved artwork is actually present.
```

No source code change can close it by itself.



---

# 36. Weak-implementer master contract

This section is intentionally repetitive and prescriptive. A weak implementing
model should follow it literally.

## 36.1 One phase at a time

For each phase:

```text
1. read this CRUU12 phase completely
2. inspect only the relevant current files
3. write down CURRENT BEHAVIOR in 5–15 bullets
4. write down TARGET INVARIANTS
5. implement the smallest coherent change
6. add fault-injection tests before claiming completion
7. run the narrow test category
8. run full suite
9. inspect git diff
10. produce implementation evidence
11. STOP
```

Do not opportunistically refactor unrelated code.

## 36.2 Never "make tests pass" by weakening authority

Forbidden:

```text
catch(Exception) and continue
replace strict probe with File.Exists
replace exact hash with filename checks
remove failing sentinel from RequiredRegressionTests.psd1
rename a test without implementing scenario
mark integration test Inconclusive because environment is inconvenient
skip Windows behavior on windows-latest
change expected error into warning without CRUU12 permission
delete a marker early so startup stops complaining
```

## 36.3 Every recovery protocol has exactly three outputs

```text
SUCCESS_FINALIZED
SUCCESS_PRESERVED_WITH_WARNING
FAIL_CLOSED_WITH_AUTHORITY_PRESERVED
```

Never:

```text
FAILURE + authority deleted
```

## 36.4 Preserve the original exception

When cleanup also fails:

```csharp
catch (Exception original)
{
    IReadOnlyList<CleanupFailure> cleanup =
        TryCleanup();

    if (cleanup.Count > 0)
    {
        throw new TypedCleanupException(
            original,
            cleanup);
    }

    throw;
}
```

Do not replace the original failure with only the last cleanup failure.

---

# 37. File-by-file implementation map

The implementer should expect changes in these files.

| File | Required CRUU12 work |
|---|---|
| `Services/PromptMutationCoordinator.cs` | CRUU12-001/002/003: remove destructive generic catches; use central recovery; exact old snapshot; postcommit semantics |
| `Services/LibraryMutationJournal.cs` | CRUU12-004/005: revision + stricter fields/invariants |
| `Services/LibraryMutationJournalRepository.cs` | CRUU12-004/005: copy-on-write CAS advance, exact retire CAS, strict grammar |
| `Services/LibraryMutationRecoveryService.cs` | CRUU12-001/003: phase-aware hash-equal recovery, committed result, no unsafe delete |
| `Services/LibraryRepository.cs` | CRUU12-002/028: exact primary snapshot; canonical package API; remove bare split-authority APIs |
| `Services/PromptLibraryService.cs` | consume typed postcommit/restart result; no weaker fallback coordinator |
| `Services/AppSettingsRepository.cs` | CRUU12-008/009/010; settings-only temp reconcile |
| `Services/DurableTempReconciler.cs` | split or replace with SettingsTempReconciler + DataRootTempReconciler |
| `App.xaml.cs` | order `.app.lock`/tree lease/conflict detection/data-root temp cleanup; surface warnings |
| `Services/DataFolderTransitionCoordinator.cs` | CRUU12-011/017/018/024/025; target operation lease, baseline, bootstrap context, commit lease, terminal cleanup |
| `Services/DataFolderMigrationService.cs` | CRUU12-012/013/021; atomic child ownership, transaction object, strict UTF-8 |
| `Services/MigrationRecoveryService.cs` | CRUU12-011/014/016/019/025 |
| `Services/MigrationManifestRepository.cs` | CRUU12-014/015/016 |
| `Services/MigrationManifestBuilder.cs` | explicit baseline; exact control hashes |
| `Services/MigrationAttemptManifest.cs` | control expected hash/length if adopted |
| `Services/MigrationTargetInventoryInspector.cs` | typed inventory context; strict enumeration |
| `Services/MigrationReadyGate.cs` | bootstrap persistent controls + conflict controls |
| `Services/DataRootCapabilityValidator.cs` | CRUU12-026/027; no wildcard deletion; verified probe cleanup |
| `Services/WindowsStrictDirectoryOpener.cs` | CRUU12-022/023; type/reparse verification |
| `Services/ManagedDataRootSessionLease.cs` | CRUU12-023; handle-final identity |
| new `Services/ManagedTargetOperationLease.cs` | CRUU12-011 |
| new `Services/MigrationPayloadCommitLease.cs` | CRUU12-024 |
| new `Services/RecoveryJournalConflictDetector.cs` | CRUU12-020 |
| new `Services/SettingsTempReconciler.cs` | CRUU12-006 |
| new `Services/DataRootTempReconciler.cs` | CRUU12-006/007 |
| `Services/LibraryStartupService.cs` | CRUU12-030/031 |
| `Services/AtomicTextWriterDurableAdapter.cs` | CRUU12-029: remove from production or eliminate safety downgrade |
| tests | replace weak sentinels; add CRUU12 fault matrix |
| `tools/RequiredRegressionTests.psd1` | add real CRUU12 sentinel names only after tests exist |
| `tools/VerifyTestEvidence.ps1` | retain exact-name semantics; test script itself |
| `tools/IconIdentityVerifier/*` | CRUU12-033 |
| `tools/GenerateAppIcon.ps1` | independent SVG render per frame |
| `tools/VerifyReleaseAssets.ps1` | approved identity manifest gate |

If a different file split is chosen, document why. Do not hide behavior in an
unrelated "Helper.cs".

---

# 38. Prompt mutation authoritative state machine

This table is normative.

## 38.1 Create / Duplicate

| Durable journal phase | Primary metadata | Body | Meaning | Recovery action |
|---|---|---|---|---|
| Prepared | OLD | Missing | nothing published | retire journal |
| Prepared | OLD | NEW exact | ambiguous ownership if body-phase publication failed | preserve unless same-operation ownership is independently proven |
| BodyDurable | OLD | NEW exact | body created, metadata not committed | verified-delete NEW, retire |
| BodyDurable | NEW | NEW exact | primary committed but phase write lagged | finalize as committed; advance/retire |
| MetadataDurable | NEW | NEW exact | committed | retire |
| any | Other | any | external/unknown mutation | preserve journal, fail closed |
| any | NEW | Missing/Other | committed metadata lacks correct body | preserve journal, fail closed |

A journal phase is evidence, but physical exact bytes remain authoritative.

## 38.2 Edit

Let:

```text
LIB = OldOnly | NewOnly | OldAndNewSameBytes | Other
BODY = Old | New | Missing | Other
RECOVERY = Old | Missing | Other
```

Rules:

```text
LIB OldOnly:
    committed = false

LIB NewOnly:
    committed = true

LIB OldAndNewSameBytes:
    phase < MetadataDurable  => committed=false
    phase >= MetadataDurable => committed=true

LIB Other:
    fail closed
```

If not committed:

```text
BODY Old:
    okay; delete exact OLD recovery copy if present

BODY New or Missing:
    require RECOVERY Old
    restore OLD body durably
    verify OLD body
    delete exact OLD recovery copy

BODY Other:
    fail closed
```

If committed:

```text
BODY must be New
RECOVERY Old may be verified-deleted
RECOVERY Other => preserve + fail closed
```

## 38.3 Delete

Rules:

```text
LIB OldOnly:
    operation not committed
    body may be Old
    retire journal only after consistency check

LIB NewOnly:
    operation committed

LIB OldAndNewSameBytes:
    should be impossible for delete because metadata membership changes;
    if encountered, treat as invalid journal/candidate construction
```

Committed delete:

```text
if backup exact NEW:
    verified-delete OLD body
else:
    preserve body as orphan
    retire journal with explicit warning
```

If body mismatch:

```text
preserve journal
fail closed
```

## 38.4 Add a pure decision function

Make recovery logic testable without touching disk.

```csharp
internal sealed record MutationObservedState(
    LibraryMutationMetadataState Library,
    MutationContentState Body,
    MutationContentState Recovery);

internal enum MutationRecoveryAction
{
    RetireOnly,
    DeleteNewBodyThenRetire,
    RestoreOldBodyThenRetire,
    PreserveOldBodyAsOrphanThenRetire,
    FinalizeCommittedThenRetire,
    FailClosed
}

internal static class MutationRecoveryPlanner
{
    public static MutationRecoveryAction Plan(
        LibraryMutationJournal journal,
        MutationObservedState state)
    {
        ...
    }
}
```

Then filesystem recovery executes the plan with verified operations.

This removes branch duplication and makes the state table exhaustively testable.

---

# 39. Copy-ready fault-injection vocabulary

Do not create one-off booleans such as:

```text
FailNextWrite
FailAfterCopy
ThrowOnThirdCall
```

spread across many fakes.

Use named fault points.

```csharp
internal enum FaultPoint
{
    None,

    MutationJournalCreatePrepared,
    MutationRecoveryBodyCreate,
    MutationBodyCreate,
    MutationBodyReplace,
    MutationJournalAdvanceBodyDurable,
    MutationPrimaryCommit,
    MutationBackupSync,
    MutationJournalAdvanceMetadataDurable,
    MutationRecoveryArtifactDelete,
    MutationJournalRetire,

    SettingsTempReconcile,
    SettingsTokenReadPrimary,
    SettingsTokenReadBackup,
    SettingsPrimaryWrite,
    SettingsBackupWrite,

    MigrationManifestCreate,
    MigrationTempCreate,
    MigrationCopyMidStream,
    MigrationTempFlush,
    MigrationTempPromote,
    MigrationTransactionBookkeeping,
    MigrationCapabilityCurrentCreate,
    MigrationCapabilityReplacementCreate,
    MigrationCapabilityReplace,
    MigrationCapabilityCleanup,
    MigrationReadyStageWrite,
    MigrationReadyStagePromote,
    MigrationReadyGate,
    MigrationPayloadLeaseAcquire,
    MigrationSettingsCommit,
    MigrationRollbackFileDelete,
    MigrationRollbackDirectoryDelete,
    MigrationMarkerRetire,

    InitializationJournalCreate,
    InitializationBodyCreate,
    InitializationPrimaryCommit,
    InitializationJournalRetire
}
```

Controller:

```csharp
internal sealed class FaultPlan
{
    private readonly Dictionary<FaultPoint, Queue<Exception>>
        _faults = [];

    public FaultPlan Fail(
        FaultPoint point,
        Exception exception)
    {
        if (!_faults.TryGetValue(
                point,
                out Queue<Exception>? q))
        {
            q = new Queue<Exception>();
            _faults.Add(point, q);
        }

        q.Enqueue(exception);
        return this;
    }

    public void Hit(FaultPoint point)
    {
        if (_faults.TryGetValue(
                point,
                out Queue<Exception>? q) &&
            q.Count > 0)
        {
            throw q.Dequeue();
        }
    }

    public bool IsExhausted =>
        _faults.Values.All(x => x.Count == 0);
}
```

Production code must not depend directly on `FaultPlan`.

Inject wrappers around strong interfaces in tests.

---

# 40. Copy-ready durable writer fault wrapper

```csharp
internal sealed class FaultInjectingDurableWriter
    : IDurableAtomicFileWriter
{
    private readonly IDurableAtomicFileWriter _inner;
    private readonly FaultPlan _faults;
    private readonly Func<string, DurableFileClass, bool> _isPrimary;
    private readonly Func<string, DurableFileClass, bool> _isJournal;
    private readonly Func<string, DurableFileClass, bool> _isRecovery;

    public FaultInjectingDurableWriter(
        IDurableAtomicFileWriter inner,
        FaultPlan faults,
        Func<string, DurableFileClass, bool> isPrimary,
        Func<string, DurableFileClass, bool> isJournal,
        Func<string, DurableFileClass, bool> isRecovery)
    {
        _inner = inner;
        _faults = faults;
        _isPrimary = isPrimary;
        _isJournal = isJournal;
        _isRecovery = isRecovery;
    }

    public void ReplaceDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        if (_isJournal(targetPath, fileClass))
        {
            // Test-specific wrapper may map exact expected phase
            // to a more precise FaultPoint.
        }

        if (_isPrimary(targetPath, fileClass))
        {
            _faults.Hit(
                FaultPoint.MutationPrimaryCommit);
        }

        if (_isRecovery(targetPath, fileClass))
        {
            _faults.Hit(
                FaultPoint.MutationRecoveryBodyCreate);
        }

        _inner.ReplaceDurable(
            targetPath,
            bytes,
            fileClass);
    }

    public void CreateNewDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass)
    {
        if (fileClass ==
            DurableFileClass.PromptBody)
        {
            _faults.Hit(
                FaultPoint.MutationBodyCreate);
        }

        _inner.CreateNewDurable(
            targetPath,
            bytes,
            fileClass);
    }
}
```

Better still: inject fault points at the coordinator/repository semantic layer
instead of guessing by file path. The code above is a starting test helper, not
a requirement to blur semantic stages.

---

# 41. Persistent-state assertion kit

After a crash/fault simulation, tests should assert bytes—not only objects.

```csharp
internal sealed record PersistentLibraryState(
    byte[]? Primary,
    byte[]? Backup,
    byte[]? Journal,
    IReadOnlyDictionary<Guid, byte[]> PromptBodies,
    IReadOnlyDictionary<string, byte[]> RecoveryFiles);

internal static PersistentLibraryState CapturePersistentState(
    AppPaths paths)
{
    byte[]? Read(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    var bodies =
        new Dictionary<Guid, byte[]>();

    if (Directory.Exists(paths.PromptsDirectory))
    {
        foreach (string path in
                 Directory.EnumerateFiles(
                     paths.PromptsDirectory,
                     "*.md"))
        {
            if (Guid.TryParseExact(
                    Path.GetFileNameWithoutExtension(path),
                    "N",
                    out Guid id))
            {
                bodies[id] =
                    File.ReadAllBytes(path);
            }
        }
    }

    var recovery =
        new Dictionary<string, byte[]>(
            StringComparer.OrdinalIgnoreCase);

    if (Directory.Exists(paths.RecoveryDirectory))
    {
        foreach (string path in
                 Directory.EnumerateFiles(
                     paths.RecoveryDirectory))
        {
            recovery[
                Path.GetFileName(path)] =
                    File.ReadAllBytes(path);
        }
    }

    return new PersistentLibraryState(
        Read(paths.LibraryPath),
        Read(paths.LibraryBackupPath),
        Read(paths.LibraryMutationJournalPath),
        bodies,
        recovery);
}
```

Use this to compare:

```text
before
after fault
after recovery
```

Do not rely only on service `_document` state.

---

# 42. Cleanup authority decision table

Before every delete, answer all columns.

| Artifact | Why can Prompt Helper delete it? | Required proof | If proof fails |
|---|---|---|---|
| Create/Duplicate new prompt body before metadata commit | mutation journal + BodyDurable + exact bytes | contained non-reparse file + expected len/hash | preserve journal, fail closed |
| Edit recovery copy | edit journal + exact old bytes | expected path grammar + len/hash | preserve, fail closed |
| Deleted prompt body after metadata commit | new primary + synchronized backup + old hash | exact old body len/hash | preserve as orphan/warn |
| Migration final | manifest | exact path + len/hash + verified contained handle | preserve marker, fail |
| Migration complete payload temp | manifest | exact path + exact final len/hash | delete |
| Migration partial payload temp | ambiguous after crash | no adequate content proof | preserve marker, fail |
| Migration planned probe | manifest control + expected content | exact path + exact len/hash | preserve marker, fail |
| Random current-process probe | in-process owned creation + expected content | exact nonce + verified bytes | preserve/warn on mismatch |
| Historical wildcard probe lookalike | none | none | never auto-delete |
| Ready stage | manifest attempt identity | strict parse + exact AttemptId/content authority | preserve marker, fail |
| `.prompthelper-migration.json` | terminal cleanup complete | full clean inventory | retire last |
| Mutation journal | mutation finalized/rolled back consistently | expected operation/revision | preserve |
| Initialization journal | defaults package finalized | exact journal operation/phase | preserve/warn |
| Generic durable temp | exclusive namespace under proper lock and no active protocol owns class | exact grammar + location | cleanup/report |
| Unknown file | none | none | preserve |

If the implementing AI cannot fill the "Required proof" cell, it must not delete.

---

# 43. Path/handle authority checklist

For any new filesystem code, answer:

```text
[ ] Is the path fully qualified?
[ ] Is it normalized only for comparison, not used as ownership proof?
[ ] Can a File occupy this path where a Directory is expected?
[ ] Can a Directory occupy this path where a File is expected?
[ ] Can the node be a reparse point?
[ ] Is an inaccessible path being misread as Missing?
[ ] Is there a probe→open race?
[ ] Is there an open→use-by-name race?
[ ] Is final physical handle path checked?
[ ] Is delete/replace performed on a path whose identity may have changed?
[ ] Is a directory lease required?
[ ] Is a file content lease required?
[ ] Is FileShare.Delete unintentionally granted?
[ ] Is FileShare.Write unintentionally granted?
[ ] Is current-object ownership proven?
```

No filesystem-sensitive phase is complete until this checklist is included in
the implementation evidence.

---

# 44. Exact mutation fault matrix

The implementing AI must build at least the following matrix.

## 44.1 Create

For each fault:

```text
journal Prepared create fails
body CreateNew fails
BodyDurable journal advance fails
primary commit fails
backup sync fails
MetadataDurable journal advance fails
journal retire fails
```

For each assert:

```text
operation result
primary exact bytes
backup exact bytes
body state
journal state
startup recovery result
second startup idempotence
```

## 44.2 Edit

Faults:

```text
recovery-copy creation
RecoveryBodyDurable advance
new body replace
BodyDurable advance
primary commit
backup sync
MetadataDurable advance
recovery-copy delete
journal retire
```

Run twice:

```text
A. title + body edit => metadata hashes differ
B. body-only edit   => metadata hashes equal
```

## 44.3 Delete

Faults:

```text
Prepared journal
primary commit
backup sync
MetadataDurable advance
body verified delete
journal retire
```

Backup variants:

```text
backup new
backup old
backup missing
backup future
backup unreadable
```

Every combination does not need Cartesian explosion, but every semantic branch
must have a deterministic test.

---

# 45. Exact migration fault matrix

Run failures at:

```text
target reservation acquire
old-attempt recovery
fresh target inspection
manifest creation
first payload temp creation
mid-copy after N bytes
temp Flush(true)
temp→final move
transaction ownership update
root probe current create
root probe replacement create
root probe replace
root probe cleanup
prompts probe create/replace/cleanup
ReadyGate source re-capture
Ready stage create
Ready stage flush
Ready stage promote
physical revalidation
payload commit-lease source open
payload commit-lease target open
payload commit-lease hash validation
settings CAS token read
settings primary write
settings backup sync
transaction commit
marker retire
target operation lease release
reservation release
```

For each pre-settings failure:

```text
settings unchanged
source unchanged
marker preserved if residue/ambiguity remains
target either exact baseline or has authoritative marker explaining residue
no foreign file deleted
```

For each post-settings success with cleanup failure:

```text
new settings preserved
target payload preserved
cleanup failure reported
do not roll settings back by deleting target data
```

---

# 46. Test utility: deterministic mid-stream copy failure

```csharp
internal sealed class FailAfterNBytesWriteStream
    : Stream
{
    private readonly Stream _inner;
    private readonly long _failAfter;
    private long _written;

    public FailAfterNBytesWriteStream(
        Stream inner,
        long failAfter)
    {
        _inner = inner;
        _failAfter = failAfter;
    }

    public override void Write(
        byte[] buffer,
        int offset,
        int count)
    {
        long remaining =
            _failAfter - _written;

        if (remaining <= 0)
            throw new IOException(
                "Injected mid-copy failure.");

        int allowed =
            (int)Math.Min(
                count,
                remaining);

        _inner.Write(
            buffer,
            offset,
            allowed);

        _written += allowed;

        if (allowed < count)
            throw new IOException(
                "Injected mid-copy failure.");
    }

    public override void Flush() => _inner.Flush();
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(
        byte[] buffer,
        int offset,
        int count)
        => throw new NotSupportedException();

    public override long Seek(
        long offset,
        SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => _inner.SetLength(value);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();

        base.Dispose(disposing);
    }
}
```

Use this inside a fault-injecting `IMigrationFileOps.CreateNewFile`.

Assert partial temp behavior follows CRUU12-014.

---

# 47. Idempotence requirements

Every recovery test must run recovery twice.

Pattern:

```csharp
MutationRecoveryResult first =
    recovery.RecoverIfPresent();

PersistentLibraryState afterFirst =
    CapturePersistentState(paths);

MutationRecoveryResult second =
    recovery.RecoverIfPresent();

PersistentLibraryState afterSecond =
    CapturePersistentState(paths);

AssertEquivalent(
    afterFirst,
    afterSecond);
```

For a successful finalized recovery:

```text
first.Success  == true
second.Success == true
second does no mutation
```

For a fail-closed ambiguous recovery:

```text
first.Success  == false
second.Success == false
authority files exact unchanged
```

The same rule applies to migration recovery.

---

# 48. No-sleep concurrency testing

Do not use:

```csharp
Thread.Sleep(...)
Task.Delay(100)
```

to "make races happen".

Use barriers/events.

```csharp
var opened = new ManualResetEventSlim(false);
var continueAfterSwap = new ManualResetEventSlim(false);

fake.OnAfterProbe = () =>
{
    opened.Set();
    continueAfterSwap.Wait();
};

Task operation = Task.Run(
    () => sut.Execute());

opened.Wait();

// deterministically perform the competing mutation here

continueAfterSwap.Set();

operation.GetAwaiter().GetResult();
```

Windows lock tests should use a child process when testing process-level file
sharing behavior.

---

# 49. Child-process Windows filesystem harness

Some sharing/rename semantics should not be tested only in-process.

Add a tiny test helper executable or test-host mode:

```text
PromptHelper.Tests.ChildProcess
```

Commands:

```text
hold-file-readonly <path>
hold-file-no-delete-share <path>
hold-directory-no-delete-share <path>
attempt-write <path>
attempt-replace <source> <dest>
attempt-rename <source> <dest>
```

Parent test:

```text
start child holding handle
wait for "READY" line
perform competing operation
assert Windows error
tell child EXIT
assert clean child exit
```

This is especially useful for:

```text
CRUU12-011
CRUU12-023
CRUU12-024
```

Do not rely on Unix behavior.



---

# 50. Phase-by-phase execution helpers for a weak implementing AI

Each phase below is a self-contained implementation helper. The implementing AI
must not skip a phase merely because later code appears to solve the same issue.

---

## PHASE 00 — establish baseline and evidence

### Goal

Prove what repository state is being modified.

### Instructions

```text
1. Run git status --short.
2. Record HEAD SHA.
3. Confirm HEAD equals the intended CRUU12 implementation base.
4. Run Release build.
5. Run full test suite once.
6. Save TRX.
7. Do not modify code yet.
8. If baseline tests fail, record exact failures before implementation.
```

### Commands

```powershell
git rev-parse HEAD
git status --short

dotnet restore .\PromptHelper.slnx
dotnet build .\PromptHelper.slnx -c Release --no-restore

dotnet test .\PromptHelper.slnx `
  -c Release `
  --no-build `
  --logger "trx;LogFileName=cruu12-baseline.trx"
```

### Required output

```text
BASE_SHA=
WORKTREE_CLEAN_OR_EXPLAINED=
BUILD=
TEST_TOTAL=
TEST_PASSED=
TEST_FAILED=
```

Gate:

```text
No implementation phase begins without this evidence.
```

---

## PHASE 01 — mutation point-of-no-return

### Findings

```text
CRUU12-001
```

### Allowed primary files

```text
PromptMutationCoordinator.cs
LibraryMutationRecoveryService.cs
related exception/result models
tests only
```

### Required work

```text
- remove generic destructive catches;
- establish central recovery as sole rollback/finalize authority;
- model committed vs not committed;
- introduce controlled restart exception for unfinalizable postcommit state;
- never delete/restore active body after primary commit based on catch location.
```

### Must add tests

```text
CRUU12_001_Create_primary_committed_MetadataDurable_write_fails_does_not_delete_body
CRUU12_001_Edit_primary_committed_MetadataDurable_write_fails_does_not_restore_old_body
CRUU12_001_Create_body_CreateNew_collision_same_bytes_preserves_foreign_file
CRUU12_001_Postcommit_unfinalizable_mutation_forces_restart
```

### Gate

After injected postcommit failure:

```text
primary = NEW
body    = NEW
journal = retired safely OR preserved
```

Never:

```text
primary NEW + body OLD/Missing + journal Missing
```

---

## PHASE 02 — exact old-library snapshot and hash-equal edit recovery

### Findings

```text
CRUU12-002
CRUU12-003
```

### Work

```text
- add LibraryPrimarySnapshot from exact disk bytes;
- compare canonical parsed disk document to in-memory current document;
- journal old hash = exact raw disk hash;
- add OldAndNewSameBytes classification;
- use phase only to resolve hash-equal edit ambiguity;
- add body-only edit crash matrix.
```

### Tests

```text
CRUU12_002_Noncanonical_valid_primary_body_create_crash_recovers_old_state
CRUU12_002_UTF8_BOM_primary_old_hash_uses_actual_bytes
CRUU12_003_Body_only_edit_crash_at_BodyDurable_restores_old_body
CRUU12_003_Body_only_edit_crash_at_MetadataDurable_keeps_new_body
```

Gate:

```text
body-only content edit survives crash after MetadataDurable.
```

---

## PHASE 03 — mutation journal CAS and grammar

### Findings

```text
CRUU12-004
CRUU12-005
```

### Work

```text
- add revision;
- copy-on-write phase advance;
- read disk journal before every phase advance;
- require operation ID + revision + phase match;
- mutate caller object only after durable write;
- strict hash grammar;
- strict kind-specific fields;
- strict enum strings;
- retire journal with expected operation/revision CAS.
```

### Tests

Use all CRUU12-004/005 names from this document.

Gate:

```text
A failed journal write cannot make RAM phase differ from disk phase.
A replaced journal cannot be advanced or deleted.
```

---

## PHASE 04 — settings authority

### Findings

```text
CRUU12-008
CRUU12-009
CRUU12-010
```

### Work

```text
- token Missing only for actual not-found;
- propagate unreadable token failure;
- require exact current settings schema;
- normalize/validate loaded dataRootPath;
- normalize/validate before save;
- do not mutate caller object.
```

### Narrow test categories

```text
SettingsDurability
StrictUtf8
```

Gate:

```text
relative path rejected
old/negative schema rejected
sharing/access error is not Missing
```

---

## PHASE 05 — split temp reconciliation and startup ordering

### Findings

```text
CRUU12-006
CRUU12-007
```

### Work

```text
- create SettingsTempReconciler;
- create DataRootTempReconciler;
- settings reconciler touches settings namespace only;
- data-root reconciler runs after .app.lock + managed tree lease;
- scan recovery directory;
- active lifecycle protocol owns its own temps;
- cleanup failures are returned, not swallowed.
```

### Required startup order after phase

```text
settings lease
settings-only temp reconcile
settings load/recover
physical root resolution
.app.lock
tree validation
create required managed dirs
managed-tree session lease
lifecycle journal conflict detector
authoritative recovery protocol
data-root generic temp reconcile when safe
library startup
```

Do not blindly run generic data-root cleanup before lifecycle recovery.

---

## PHASE 06 — lifecycle journal conflict detector

### Finding

```text
CRUU12-020
```

### Work

Add the conflict detector before any startup recovery writes.

Test all marker pairs + triple.

Gate:

```text
multiple lifecycle journals => zero mutation
```

---

## PHASE 07 — target operation lease and strict opened-directory identity

### Findings

```text
CRUU12-011
CRUU12-022
CRUU12-023
```

### Work

```text
- opener verifies opened handle is Directory;
- managed lease opens with no DELETE sharing;
- reject reparse node on handle;
- validate final handle path;
- add ManagedTargetOperationLease for transition/retry;
- destructive migration cleanup occurs only while lease held.
```

### Windows tests

Actual:

```text
rename
junction/symlink
child process sharing
```

No fake-only acceptance for these findings.

---

## PHASE 08 — atomic migration directory/file ownership bookkeeping

### Findings

```text
CRUU12-012
CRUU12-013
CRUU12-017
```

### Work

```text
- atomic owned directory creation;
- transaction registers file object before temp creation;
- non-throwing state transition temp-owned -> final-owned;
- exact rollback authority;
- manifest baseline comes from reservation authority, not post-creation probe.
```

Gate:

```text
concurrent directory creator cannot become transaction-owned
move success cannot create untracked final
brand-new target baseline says RootExistedBefore=false
```

---

## PHASE 09 — migration control grammar and verified cleanup

### Findings

```text
CRUU12-014
CRUU12-015
CRUU12-026
CRUU12-027
```

### Work

```text
- remove wildcard stale probe deletion;
- control artifacts carry exact expected content authority where applicable;
- strict schema-v4 exact control set;
- strict schema-v3 legacy set;
- payload temp mismatch preserved;
- stage must parse/match attempt before deletion;
- probe cleanup verifies exact content.
```

Gate:

```text
a foreign file at an expected attempt path is preserved unless exact ownership proof exists.
```

---

## PHASE 10 — migration v3 retry authority and strict UTF-8

### Findings

```text
CRUU12-016
CRUU12-021
```

### Work

```text
- use derived full payload fingerprint for v3 retry;
- replace migration StreamReader auto-detect decoding with StrictUtf8Text;
- strict UTF-8 active and orphan prompt .md files;
- target active bodies strict UTF-8;
- capability backup read strict UTF-8.
```

Gate:

```text
same library.json + changed prompt body rejects v3 retry
UTF-16/UTF-32 metadata does not count as UTF-8
```

---

## PHASE 11 — bootstrap-aware strict inventory

### Findings

```text
CRUU12-018
CRUU12-019
```

### Work

```text
- inventory receives explicit bootstrap context;
- settings files are allowed persistent only at exact bootstrap;
- lifecycle controls are separately classified;
- strict enumeration; access denied propagates;
- custom -> default bootstrap transition integration test.
```

Gate:

```text
custom library can migrate back to default bootstrap without treating legitimate settings files as migration residue.
```

---

## PHASE 12 — Ready→settings payload commit lease

### Finding

```text
CRUU12-024
```

### Work

```text
- acquire read/no-write/no-delete handles for source and target payload files;
- validate final handle identity + exact hash/length;
- hold through SaveIfUnchanged + tx.Commit;
- release afterward.
```

Gate:

```text
external write/replace attempts fail while commit lease held.
```

---

## PHASE 13 — terminal rollback inventory

### Finding

```text
CRUU12-025
```

### Work

Use full inventory, not payload-only residue loop.

Gate:

```text
marker is never retired with attempt control residue.
```

---

## PHASE 14 — strong LibraryRepository payload API

### Finding

```text
CRUU12-028
```

### Work

```text
- add CanonicalLibraryPackage;
- one package owns document + bytes;
- remove independent document/bytes commit API;
- remove bare public SynchronizeBackup(LibraryDocument);
- primary and backup consume same package bytes.
```

Gate:

```text
API cannot express "write bytes B, backup document A".
```

---

## PHASE 15 — remove safety-downgrading compatibility adapters

### Finding

```text
CRUU12-029
```

### Work

Remove production constructors/adapters that implement strong interfaces with
weaker semantics.

Tests must use strong-interface fakes.

Gate:

```text
production composition contains no fake verified deletion or check-then-create durable writer.
```

---

## PHASE 16 — startup maintenance observability

### Finding

```text
CRUU12-030
```

### Work

```text
- add diagnostic collector;
- orphan reconciliation catches only expected operational classes;
- preserve programmer exceptions;
- mutation recovery warning reaches user;
- aggregate warnings.
```

Gate:

```text
conservative preservation is visible.
```

---

## PHASE 17 — initialization journal authority

### Finding

```text
CRUU12-031
```

### Work

```text
- InitializationControl file class;
- CreateNew journal;
- structured schema + operation ID + phase;
- crash recovery;
- postcommit marker-retire warning.
```

Gate:

```text
preexisting initialization authority is never overwritten.
```

---

## PHASE 18 — rewrite weak sentinels

### Finding

```text
CRUU12-032
```

### Work

Replace tests that merely imply behavior.

Required true scenarios:

```text
forced final-path buffer resize
actual reparse file rejection
recording settings durable writer
duplicate crash before/after metadata
actual evidence-script execution
```

Gate:

```text
each test name is mechanically supported by arrangement that creates the named condition.
```

---

## PHASE 19 — regression evidence manifest

Update:

```text
tools/RequiredRegressionTests.psd1
```

Only after corresponding tests exist.

Minimum required CRUU12 sentinels should include at least:

```text
CRUU12_001_Create_primary_committed_MetadataDurable_write_fails_does_not_delete_body
CRUU12_001_Edit_primary_committed_MetadataDurable_write_fails_does_not_restore_old_body
CRUU12_002_Noncanonical_valid_primary_body_create_crash_recovers_old_state
CRUU12_003_Body_only_edit_crash_at_MetadataDurable_keeps_new_body
CRUU12_004_Advance_write_failure_does_not_mutate_RAM_phase
CRUU12_006_Second_instance_settings_load_cannot_delete_live_data_temp
CRUU12_008_Primary_access_denied_token_is_not_Missing
CRUU12_011_Retry_prompts_swap_attempt_fails_while_target_operation_lease_held
CRUU12_012_Concurrent_directory_creator_foreign_content_is_preserved
CRUU12_013_Move_success_before_bookkeeping_failure_final_is_recoverable
CRUU12_014_Declared_payload_temp_replaced_with_foreign_bytes_is_preserved
CRUU12_015_V4_probe_arbitrary_suffix_rejected
CRUU12_016_V3_same_library_json_changed_prompt_body_rejects_retry
CRUU12_018_Custom_to_empty_default_bootstrap_with_settings_controls_succeeds
CRUU12_020_Migration_and_mutation_journals_conflict_without_mutation
CRUU12_021_UTF16_BOM_source_library_rejected
CRUU12_023_Session_lease_validates_final_handle_identity
CRUU12_024_Target_prompt_replace_fails_while_commit_lease_held
CRUU12_025_Rollback_stage_residue_preserves_marker
CRUU12_026_Foreign_capability_lookalike_is_never_deleted
CRUU12_027_Probe_current_replaced_after_creation_is_preserved
CRUU12_028_Primary_and_backup_commit_use_same_CanonicalLibraryPackage_bytes
CRUU12_029_No_public_constructor_accepts_IAtomicTextWriter_for_persistence
CRUU12_031_Crash_after_metadata_before_journal_retire_finalizes
CRUU12_032_Evidence_script_rejects_substring_only_TRX
```

Do not reduce the old CRUU11 required list unless a test is deliberately replaced
by a stronger CRUU12 sentinel; document exact replacement mapping.

---

## PHASE 20 — release identity tooling

### Finding

```text
CRUU12-033
```

### Work

```text
- source SVG SHA authority manifest;
- per-size normalized RGBA hashes;
- verify-approved-ico command;
- robust EXE icon-group selection;
- independent per-size generation.
```

This phase can be implemented/tested using fixture SVG/ICO assets in tests even
if the real product logo remains absent.

---

## PHASE 21 — approved product logo

### Finding

```text
CRUU12-034
```

Only execute after approved artwork is supplied.

No placeholder is acceptable.

---

## PHASE 22 — Windows stress and publish verification

Run:

```powershell
dotnet restore .\PromptHelper.slnx
dotnet build .\PromptHelper.slnx -c Release --no-restore
```

Then categories individually:

```powershell
$categories = @(
  "FilesystemAuthority",
  "PackageIntegrity",
  "MutationRecovery",
  "MigrationReady",
  "StrictUtf8",
  "CrashRecovery",
  "WpfIntegration",
  "WindowsFilesystemIntegration",
  "SettingsDurability",
  "MigrationRecovery",
  "OrphanReconciliation",
  "ReleaseVerification"
)

foreach ($category in $categories) {
    dotnet test .\PromptHelper.slnx `
      -c Release `
      --no-build `
      --filter "TestCategory=$category" `
      --logger "trx;LogFileName=cruu12-$category.trx"

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
```

Full suite five times:

```powershell
1..5 | ForEach-Object {
    $n = $_

    dotnet test .\PromptHelper.slnx `
      -c Release `
      --no-build `
      --logger "trx;LogFileName=cruu12-full-$n.trx"

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
```

Exact evidence verification:

```powershell
$required =
    Import-PowerShellDataFile `
      .\tools\RequiredRegressionTests.psd1

$trx =
    Get-ChildItem `
      . `
      -Recurse `
      -Filter "*.trx" |
    Select-Object -ExpandProperty FullName

.\tools\VerifyTestEvidence.ps1 `
  -TrxPath $trx `
  -RequiredTests $required.Required
```

Publish:

```powershell
dotnet publish `
  .\src\PromptHelper\PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o .\artifacts\cruu12-publish
```

Payload:

```powershell
$requiredPublish = @(
  ".\artifacts\cruu12-publish\PromptHelper.exe",
  ".\artifacts\cruu12-publish\LICENSE",
  ".\artifacts\cruu12-publish\THIRD_PARTY_NOTICES.md"
)

foreach ($path in $requiredPublish) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing publish artifact: $path"
    }
}
```

Strict icon gate only if approved asset exists.

---

## PHASE 23 — final source audit

Do not stop at green tests.

Re-read every changed source file and answer:

```text
1. Can a broad catch now suppress an authority failure?
2. Can Missing be confused with inaccessible?
3. Can a path be deleted without current-object ownership proof?
4. Can a recovery marker be deleted while residue remains?
5. Can old/new metadata hashes be equal?
6. Can external writes occur after validation before commit?
7. Can a public compatibility constructor select weaker persistence?
8. Can a test pass without creating the scenario in its name?
9. Can bootstrap settings be misclassified as migration residue?
10. Can two lifecycle journals coexist?
```

Only after this audit may the implementing model write:

```text
SOURCE_AUDIT_CLEAN
```

It may write:

```text
ZERO_DEFECT_VERIFIED
```

only with direct Windows execution evidence and no remaining external release
blocker.



---

# 51. Machine-checkable anti-regression scans

These scans are not substitutes for tests. They catch common weak-model
shortcuts.

## 51.1 Broad catch audit

PowerShell:

```powershell
$serviceFiles =
  Get-ChildItem `
    .\src\PromptHelper\Services `
    -Filter *.cs `
    -Recurse

$matches =
  $serviceFiles |
  Select-String `
    -Pattern 'catch\s*\(\s*Exception(?:\s+\w+)?\s*\)|catch\s*\{\s*\}'

$matches |
  Format-Table Path, LineNumber, Line -AutoSize
```

Every broad catch must be manually justified.

Expected legitimate examples should be rare and non-authoritative, such as:

```text
best-effort UI icon display
nonessential diagnostic logging
```

Not acceptable in:

```text
migration recovery
mutation recovery
settings CAS
ownership cleanup
filesystem authority
startup lifecycle recovery
```

## 51.2 Unsafe existence check audit

```powershell
Get-ChildItem `
  .\src\PromptHelper\Services `
  -Filter *.cs `
  -Recurse |
Select-String `
  -Pattern '\b(File|Directory)\.(Exists|GetFiles|GetDirectories)\b' |
Format-Table Path, LineNumber, Line -AutoSize
```

Every hit in an authority path must be reviewed.

Do not mechanically ban `File.Exists` everywhere; require proof that the call is
not classifying Missing vs inaccessible for a safety decision.

## 51.3 Direct deletion audit

```powershell
Get-ChildItem `
  .\src\PromptHelper\Services `
  -Filter *.cs `
  -Recurse |
Select-String `
  -Pattern '\bFile\.Delete\s*\(|\bDirectory\.Delete\s*\(' |
Format-Table Path, LineNumber, Line -AutoSize
```

For every `File.Delete`, implementation evidence must identify:

```text
artifact class
ownership proof
containment proof
reparse policy
mismatch behavior
```

## 51.4 Unsafe persistence adapter audit

```powershell
$forbidden = @(
  'AtomicTextWriterDurableAdapter',
  'FileDeleterVerifiedAdapter',
  'AtomicTextWriterSettingsDurableAdapter'
)

foreach ($name in $forbidden) {
  $hits =
    Get-ChildItem `
      .\src\PromptHelper `
      -Filter *.cs `
      -Recurse |
    Select-String -SimpleMatch $name

  if ($hits) {
    Write-Host "REVIEW REQUIRED: $name"
    $hits
  }
}
```

After CRUU12-029, expected production hits:

```text
0
```

unless the adapter has been reimplemented to satisfy the full strong contract,
in which case it should be renamed and independently tested.

## 51.5 Independent document/bytes repository API audit

```powershell
Get-ChildItem `
  .\src\PromptHelper\Services `
  -Filter *.cs `
  -Recurse |
Select-String `
  -Pattern 'CommitCanonicalBytes|SynchronizeBackup\s*\(\s*LibraryDocument' |
Format-Table Path, LineNumber, Line -AutoSize
```

Expected after CRUU12-028:

```text
0 unsafe API declarations/callers
```

## 51.6 Wildcard capability deletion audit

```powershell
Get-ChildItem `
  .\src\PromptHelper\Services `
  -Filter *.cs `
  -Recurse |
Select-String `
  -SimpleMatch '.prompthelper-capability-*.tmp'
```

Expected after CRUU12-026:

```text
0 destructive wildcard cleanup paths
```

## 51.7 Test anti-cheating audit

Find suspicious trivial assertions:

```powershell
Get-ChildItem `
  .\tests\PromptHelper.Tests `
  -Filter *.cs `
  -Recurse |
Select-String `
  -Pattern 'Assert\.IsTrue\s*\(\s*true\s*\)|Assert\.IsFalse\s*\(\s*false\s*\)'
```

Expected:

```text
0
```

Find CRUU12 tests:

```powershell
$cruu12 =
  Get-ChildItem `
    .\tests\PromptHelper.Tests `
    -Filter *.cs `
    -Recurse |
  Select-String `
    -Pattern 'public\s+void\s+(CRUU12_[A-Za-z0-9_]+)\s*\('

$cruu12.Count
```

Then verify required names through the actual TRX verifier, not source search.

---

# 52. Required implementation evidence report

The weak implementing AI must create:

```text
cruu12_implementation_evidence.md
```

Do not accept a prose-only “done”.

Use this exact template.

```markdown
# CRUU12 Implementation Evidence

## 1. Repository identity
- Base SHA:
- Final SHA:
- Branch:
- Worktree status:

## 2. Scope
- Findings attempted:
- Findings completed:
- Findings blocked:
- External blockers:

## 3. Changed files
| File | Findings | What changed | Why |
|---|---|---|---|

## 4. API changes
### Removed unsafe APIs
- ...

### Added strong APIs
- ...

## 5. Mutation transaction evidence
### Create
- Fault points executed:
- Persistent-state assertions:
- Recovery idempotence:

### Edit
- Metadata-different case:
- Body-only hash-equal case:
- Fault points executed:

### Delete
- Backup synchronized:
- Backup stale:
- Backup missing:
- Backup future:
- Backup unreadable:

## 6. Settings authority evidence
- Missing vs unreadable tests:
- Schema tests:
- path-normalization tests:
- CAS tests:

## 7. Migration authority evidence
- target-operation lease:
- atomic directory ownership:
- temp/final ownership:
- schema-v3 retry:
- schema-v4 controls:
- bootstrap target:
- strict UTF-8:
- payload commit lease:
- terminal rollback inventory:

## 8. Lifecycle recovery evidence
- migration+mutation conflict:
- migration+initialization conflict:
- mutation+initialization conflict:
- triple conflict:

## 9. Windows filesystem integration evidence
| Test | Real Windows behavior exercised | Result |
|---|---|---|

## 10. Evidence-script evidence
- substring-only synthetic TRX:
- missing-sentinel synthetic TRX:
- exact passing synthetic TRX:

## 11. Static anti-regression scan
### broad catch hits
...

### direct delete hits
...

### unsafe File.Exists/Directory.Exists authority hits
...

### unsafe adapters
...

## 12. Test results
- Build:
- Full suite run 1:
- Full suite run 2:
- Full suite run 3:
- Full suite run 4:
- Full suite run 5:
- Required exact sentinels:
- Failed:
- Skipped:
- Inconclusive:

## 13. Publish evidence
- win-x64 self-contained:
- PromptHelper.exe:
- LICENSE:
- THIRD_PARTY_NOTICES.md:

## 14. Release asset state
- approved SVG present:
- ICO present:
- identity manifest present:
- strict icon identity:
- strict release status:

## 15. Remaining concerns
- ...

## 16. Final declaration
SOURCE_AUDIT_CLEAN = YES/NO
WINDOWS_TESTS_DIRECTLY_EXECUTED = YES/NO
ZERO_DEFECT_VERIFIED = YES/NO
STRICT_RELEASE_READY = YES/NO
```

A `YES` without the matching evidence is invalid.

---

# 53. Definition of Done per finding

A finding is `FIXED` only if all are true:

```text
[ ] production code changed where required
[ ] original defect scenario is represented by a deterministic test
[ ] test actually executes the scenario named
[ ] success path test exists
[ ] failure path test exists
[ ] crash/restart path exists when applicable
[ ] idempotent second recovery tested when applicable
[ ] exact persistent bytes/state asserted
[ ] no foreign artifact is deleted in adversarial test
[ ] no broad catch masks the failure
[ ] narrow tests pass
[ ] full suite passes
[ ] required sentinel is in TRX evidence
[ ] source diff re-audited
```

If one checkbox is false:

```text
finding status != FIXED
```

Use:

```text
PARTIAL
BLOCKED
NOT_IMPLEMENTED
```

instead.

---

# 54. Copy-ready implementation prompt for the weak coding AI

Use the following prompt with this document and the repository.

```text
ROLE
You are the implementation AI repairing Prompt Helper after the CRUU12
post-CRUU11 paranoid audit.

AUTHORITY
1. The current repository source is runtime implementation authority.
2. cruu12_v2.md is defect/fix authority for this repair pass.
3. Existing passing behavior not contradicted by CRUU12 must be preserved.
4. Do not weaken an existing safety property to satisfy a CRUU12 finding.

PRIMARY GOAL
Implement every non-external CRUU12 finding completely and prove it with
deterministic tests. Do not merely make current tests green.

STRICT EXECUTION RULE
Work PHASE 00 through PHASE 23 in the exact order defined by cruu12_v2.md.

AT THE START OF EACH PHASE
- quote the phase number;
- list the exact findings;
- inspect the relevant current source;
- state the current defect mechanism;
- state the target invariants;
- list files you will change.

IMPLEMENTATION RULES
- Never use catch(Exception) to make recovery continue.
- Never treat access denied, sharing violation, generic IOException, or
  security failure as Missing.
- Never auto-delete a file because its name/path merely looks owned.
- Never delete a recovery/migration/mutation marker before terminal state is
  proven clean.
- Never add a test that only restates expected logic without executing the
  production path.
- Never remove a required regression sentinel merely because it fails.
- Never replace true durability with a non-durable test adapter in production.
- Never use Thread.Sleep/Task.Delay to create a race test.
- Never fabricate the missing approved Prompt Helper logo.
- Never claim Windows/.NET execution unless you actually ran it and have
  command/TRX evidence.

MUTATION RULE
PromptMutationCoordinator generic catch blocks are not rollback authority.
LibraryMutationRecoveryService plus the durable journal is the single authority.
After primary metadata is durably NEW, active body content must never be
rolled back independently.

MIGRATION RULE
The migration marker is deleted last, only after complete terminal inventory
proves all attempt residue is gone. A foreign mismatch at an attempt-looking
path is preserved and causes fail-closed recovery.

TEST RULE
Every high-risk scenario must assert persistent on-disk bytes, not only
in-memory service objects. Every successful or fail-closed recovery scenario
must run recovery a second time and prove idempotence.

WINDOWS RULE
Filesystem identity, reparse, sharing, replace, and rename properties must be
tested on Windows. Use barriers/child processes for deterministic concurrency.

AFTER EACH PHASE
1. run the narrow affected tests;
2. run the full suite once;
3. inspect git diff;
4. update cruu12_implementation_evidence.md;
5. do not continue if the phase gate is not satisfied.

FINAL REQUIRED EXECUTION
- Release build.
- All relevant categories.
- Full suite five times.
- VerifyTestEvidence.ps1 using RequiredRegressionTests.psd1.
- self-contained win-x64 publish.
- publish payload verification.
- strict release icon gate only if the approved real SVG exists.
- final source audit against all CRUU12 findings.

OUTPUT
Do not respond merely "done".
Return:
- final SHA;
- exact findings fixed/partial/blocked;
- test totals from each full run;
- required sentinel verification result;
- Windows integration evidence;
- publish evidence;
- strict release state;
- remaining concerns;
- path to cruu12_implementation_evidence.md.

STOP CONDITION
If an approved real logo is absent, finish all code/test work but report
CRUU12-034 = BLOCKED_EXTERNAL_ASSET and STRICT_RELEASE_READY = NO.
Do not invent an asset.
```

---

# 55. Copy-ready independent verification prompt after implementation

Run this with a stronger audit model after the implementation AI claims success.

```text
ROLE
You are the independent final adversarial auditor for Prompt Helper CRUU12.

DO NOT TRUST
- commit messages;
- implementation evidence claims;
- test names;
- comments;
- "all tests passed" summaries;
- prior CRUU12 status declarations.

INPUTS
1. the final repository;
2. cruu12_v2.md;
3. cruu12_implementation_evidence.md;
4. CI/TRX evidence if available.

TASK
Reconstruct every CRUU12 defect independently and determine whether the final
source actually prevents it.

MANDATORY AUDIT ORDER

A. REPOSITORY IDENTITY
- record HEAD;
- compare against CRUU12 base;
- list changed files;
- check uncommitted changes.

B. CRUU12-001..005 MUTATION
- trace every Create/Edit/Delete cut point;
- specifically inject/reason about failure after primary metadata commit but
  before MetadataDurable journal persistence;
- test body-only edit where old/new metadata bytes are identical;
- verify journal CAS/revision behavior;
- verify exact old disk bytes are journal authority;
- verify collision same-bytes foreign prompt is not deleted.

C. CRUU12-006..010 SETTINGS/TEMPS
- prove settings reconciliation cannot delete data-root temps before .app.lock;
- prove custom data-root temps are reconciled under the correct lock;
- prove access errors are not Missing;
- prove exact schema authority;
- prove dataRootPath normalization.

D. CRUU12-011..027 MIGRATION
- trace all destructive cleanup paths;
- search for raw File.Delete/Directory.Delete;
- inspect target operation leases;
- inspect atomic directory ownership;
- force temp→final bookkeeping edge;
- replace planned temp/control with foreign bytes and confirm preservation;
- inspect schema-v3 and v4 grammar separately;
- verify full v3 source payload fingerprint;
- test custom→default bootstrap migration with real settings files;
- force access denied during target inventory;
- test lifecycle journal conflicts;
- test UTF-16/UTF-32/invalid UTF-8;
- test target/source write attempts after Ready but before settings commit;
- force Ready-stage cleanup residue and verify marker preservation;
- verify no wildcard capability cleanup.

E. CRUU12-028..031 PERSISTENCE/STARTUP
- prove no API accepts independent LibraryDocument + bytes authorities;
- prove no production safety adapter downgrades durability/deletion semantics;
- verify maintenance failures are visible;
- test initialization create-new and crash recovery.

F. CRUU12-032 TEST QUALITY
For every required sentinel:
- inspect test body;
- determine whether it actually creates its named scenario.
Reject name-only tests.

Specifically run/inspect:
- forced final-path resize;
- actual reparse file;
- injected settings durable writer;
- duplicate before/after metadata crash;
- synthetic TRX evidence-script tests.

G. CRUU12-033/034 RELEASE
- verify SVG hash identity manifest;
- verify ICO normalized RGBA against manifest;
- verify EXE resource icon group;
- confirm real approved logo exists;
- do not accept a placeholder.

H. EXECUTION EVIDENCE
If Windows/.NET execution is available:
- Release build;
- narrow categories;
- full suite 5x;
- exact sentinel verification;
- self-contained publish;
- strict release gate if asset available.

If Windows execution is unavailable:
- label runtime claims NOT_INDEPENDENTLY_VERIFIED.
Do not convert source review into runtime PASS.

FINAL OUTPUT
Create cruu13.md if ANY defect, regression, incomplete fix, false-positive test,
or unverified release blocker remains.

For every new finding include:
- severity;
- exact file/method;
- failure mechanism;
- persistent consequence;
- deterministic reproduction;
- exact repair architecture;
- copy-ready code where useful;
- required tests;
- phase/order dependency;
- acceptance gate.

Only return ZERO_DEFECT if:
- no source defect remains;
- all CRUU12 scenarios are truly implemented;
- required tests execute their named behavior;
- direct Windows evidence passes;
- no release blocker remains.
```

---

# 56. Final acceptance checklist

## Mutation

```text
[ ] no destructive generic catch rollback
[ ] exact raw old primary snapshot
[ ] body-only edit hash-equal case correct
[ ] journal revision/CAS
[ ] strict journal grammar
[ ] recovery twice is idempotent
```

## Settings / temp

```text
[ ] settings-only temp reconciler
[ ] data-root temp reconciler under .app.lock/tree lease
[ ] recovery directory covered
[ ] unreadable token != Missing
[ ] exact schema
[ ] normalized fully-qualified data root
```

## Migration

```text
[ ] target operation lease
[ ] atomic child directory ownership
[ ] no temp→final ownership gap
[ ] verified cleanup of temp/control content
[ ] schema-specific control grammar
[ ] v3 full payload fingerprint
[ ] reservation-derived baseline
[ ] bootstrap-aware inventory
[ ] strict enumeration
[ ] lifecycle journal conflict detector
[ ] strict UTF-8
[ ] source+target commit lease
[ ] marker-last terminal rollback
[ ] no wildcard probe deletion
[ ] probe cleanup verifies content
```

## Persistence API

```text
[ ] CanonicalLibraryPackage or equivalent single authority
[ ] no independent document/bytes commit API
[ ] no bare public backup-sync document API
[ ] no safety-downgrading production adapter
```

## Startup / initialization

```text
[ ] maintenance warnings surfaced
[ ] programmer exceptions not swallowed
[ ] initialization uses own durable class
[ ] initialization journal CreateNew
[ ] initialization crash recovery
```

## Test quality

```text
[ ] forced API buffer resize
[ ] actual reparse artifact test
[ ] injected settings writer proof
[ ] duplicate state-machine faults
[ ] actual evidence script executed
[ ] no name-only sentinels
[ ] no sleeps for race tests
[ ] real Windows process/handle tests where required
```

## Release

```text
[ ] approved real SVG
[ ] SVG hash in identity manifest
[ ] all required ICO RGBA hashes
[ ] published EXE group exact
[ ] no placeholder
```

## Evidence

```text
[ ] Release build
[ ] all categories
[ ] full suite 5/5
[ ] exact required sentinels
[ ] win-x64 self-contained publish
[ ] cruu12_implementation_evidence.md complete
[ ] final source audit
```

---

# 57. Final CRUU12 v2 verdict

At the audited commit:

```text
5c1904f870d0b2587407b4484e02e6ed889a4acd
```

CRUU11 made major architectural progress, but CRUU12 identifies remaining
transaction, authority, migration, test-evidence, and release problems that are
not safe to dismiss.

The highest-priority invariant is:

```text
A DURABLE PRIMARY COMMIT MAY NEVER BE UNDONE BY AN UNCOORDINATED BODY-ONLY
CATCH CLEANUP.
```

The highest-priority migration invariant is:

```text
NO DESTRUCTIVE RECOVERY OPERATION MAY RELY ON PATH NAME ALONE, AND THE
MIGRATION MARKER IS RETIRED LAST.
```

The highest-priority verification invariant is:

```text
A TEST NAME IS NOT EVIDENCE. THE TEST MUST CREATE THE NAMED FAILURE CONDITION.
```

The implementing AI should therefore execute the prescribed phases in order,
produce the evidence report, and then submit the resulting repository to the
independent verification prompt above.

Until that occurs:

```text
SOURCE_ZERO_DEFECT = NOT ESTABLISHED
WINDOWS_RUNTIME_ZERO_DEFECT = NOT ESTABLISHED
STRICT_RELEASE_READY = NO
```

CRUU12-034 remains externally blocked until approved Prompt Helper artwork is
provided.

