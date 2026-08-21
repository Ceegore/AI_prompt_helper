# CRUU10 — Cumulative Final-Consistency Audit & Weak-AI Implementation Blueprint

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited HEAD:** `be1da4fa49916a102616f82a6c74f5601ab5d2d6`  
**Audit date:** 2026-08-21  
**Relationship to prior documents:** CRUU10 is cumulative. It carries forward every still-open CRUU9 finding and adds new findings from a broader audit of startup package consistency, ordinary prompt CRUD, long-lived filesystem containment, JSON authority, orphan lifecycle, and test evidence.

---

# 0. Read this first

This is **not** a claim that CRUU9 was implemented.

At the start of this audit, the latest pushed `main` commit was still:

```text
be1da4fa49916a102616f82a6c74f5601ab5d2d6
```

That is the same commit audited for CRUU9.

Therefore:

```text
CRUU9-001 through CRUU9-025 remain OPEN/BLOCKED on pushed main.
```

No CRUU9 finding may be marked fixed merely because a repair plan exists.

This CRUU10 pass widened the review beyond data-folder migration and found additional defects in:

```text
strict directory presence authority
startup package completeness
library backup synchronization
backup recovery package completeness
normal prompt Create/Edit/Duplicate crash consistency
delete/orphan lifecycle
single-file crash durability
live-session prompts/recovery tree containment
recovery preservation of pre-existing empty directories
strict settings/library JSON member authority
exception filtering
prompt-title resource limits
reserved control namespace rules
CRUD crash test coverage
startup damaged-package test coverage
legacy fail-open case-sensitivity API
```

---

# 1. Current verdict

## 1.1 Source-level result

```text
AUDITED HEAD                             = be1da4fa49916a102616f82a6c74f5601ab5d2d6
NEWER PUSHED IMPLEMENTATION FOUND        = NO
CRUU9 CARRY-FORWARD FINDINGS             = 25
NEW CRUU10 FINDINGS                      = 15
TOTAL OPEN/BLOCKED ITEMS IN THIS DOC     = 40
NEW CRUU10 HIGH FINDINGS                 = 6
ZERO-DEFECT ACCEPTANCE                   = NOT GRANTED
STRICT RELEASE                           = BLOCKED
```

## 1.2 Runtime evidence

This audit environment does not provide the required Windows/.NET/WPF toolchain.

Therefore this document does **not** claim that it independently executed:

```text
dotnet restore
dotnet build
dotnet test
WPF integration tests
NTFS junction tests
case-sensitive-directory tests
win-x64 publish
Explorer/taskbar/Alt+Tab icon validation
```

The implementing AI must run those checks on Windows and return evidence.

## 1.3 Central architectural conclusion

The previous rounds hardened the data-folder transition itself, but Prompt Helper still has **two different consistency models**:

```text
A. data-folder migration:
   increasingly journaled / recovery-aware

B. ordinary prompt editing:
   multiple files changed with only in-process try/catch compensation
```

That split is the largest remaining architectural weakness.

After CRUU10, there must be one consistent rule:

> Any operation that changes more than one durable user-data object must have a restart-recoverable transaction authority. In-process catch/rollback is an optimization, not the only recovery mechanism.

---

# 2. Cumulative finding register

## 2.1 CRUU9 carry-forward

All of these remain open because `main` did not change after the CRUU9 audit.

| ID | Severity | Status | Carry-forward finding |
|---|---|---|---|
| CRUU9-001 | CRITICAL/HIGH | OPEN | Managed child directories (`prompts`, `recovery`) are not physically bound; junctions/symlinks can redirect writes/deletes outside bound target |
| CRUU9-002 | HIGH | OPEN | `File.Exists` fail-open semantics are used in authority decisions |
| CRUU9-003 | HIGH | OPEN | `TempRelativePath` is not bound to final directory/name/AttemptId grammar |
| CRUU9-004 | HIGH | OPEN | Final/temp collision checking is split into separate namespaces |
| CRUU9-005 | MED-HIGH | OPEN | Retry recovery does not require source identity match |
| CRUU9-006 | HIGH | OPEN | Capability-probe crash residue is not durably manifest-owned |
| CRUU9-007 | HIGH | OPEN | Manifest staging temp is not itself durably owned |
| CRUU9-008 | HIGH | OPEN | Authoritative settings pointer promotion is weaker than payload/Ready durability |
| CRUU9-009 | MED-HIGH | OPEN | Settings writer temps are not a strict recoverable bootstrap control class |
| CRUU9-010 | MED-HIGH | OPEN | Retry post-clean terminal verification is incomplete |
| CRUU9-011 | MED | OPEN | Recovery mixes `RecoveryResult` with raw escaping exceptions |
| CRUU9-012 | MED | OPEN | Successful new-root transition can produce false reservation cleanup failures |
| CRUU9-013 | MED | OPEN | Reservation ownership has check/create race and hides acquisition cleanup failures |
| CRUU9-014 | MED-HIGH | OPEN | Stale root `.app.lock` can make a retryable empty target look occupied |
| CRUU9-015 | MED | OPEN | Lexical/physical bootstrap identity is threaded inconsistently |
| CRUU9-016 | MED | OPEN | Nested control-looking filenames can be incorrectly ignored |
| CRUU9-017 | MED | OPEN | `ReadyToCommit` lacks one explicit terminal invariant gate |
| CRUU9-018 | MED | OPEN | `CopySnapshotToTarget` retains an unmanifested-temp escape hatch |
| CRUU9-019 | LOW-MED | OPEN | Manifest cleanup exception can mask original write failure |
| CRUU9-020 | MED | OPEN | Forced shutdown remains conditional on `DialogResult == true` |
| CRUU9-021 | MED | OPEN | Several CRUU8 tests overclaim what they execute |
| CRUU9-022 | MED | OPEN | CI does not enforce mandatory categories/sentinel evidence |
| CRUU9-023 | MED | OPEN | Exact SVG → ICO → EXE icon identity is not verified |
| CRUU9-024 | LOW-MED | OPEN | Migration JSON authority is not strict for all critical members/UTF-8 |
| CRUU9-025 | RELEASE BLOCKER | BLOCKED | Approved real `PromptHelperLogo.svg` / generated production ICO absent |

## 2.2 New CRUU10 findings

| ID | Severity | Finding |
|---|---|---|
| CRUU10-001 | HIGH | `Directory.Exists` / `DirectoryInfo.Exists` fail-open semantics remain in topology, resolver, baseline, reservation and recovery authority |
| CRUU10-002 | HIGH | Startup treats metadata-valid primary as healthy and can overwrite the safety backup before proving every referenced prompt body exists and is readable |
| CRUU10-003 | HIGH | Metadata-valid backup can be promoted as successful recovery even when its referenced prompt package is incomplete |
| CRUU10-004 | HIGH | Normal Create/Edit/Duplicate prompt operations are not crash-transactional across prompt body and `library.json` |
| CRUU10-005 | MED-HIGH | Deleted prompt bodies can become permanent orphans; there is no later safe reconciliation and migrations intentionally copy them forward |
| CRUU10-006 | HIGH | Generic `AtomicTextWriter` remains the durability primitive for library metadata and prompt bodies; final promotion is not write-through and crash temps have no systematic hygiene |
| CRUU10-007 | HIGH | Even after one-time child-tree validation, live runtime operations can be redirected if `prompts`/`recovery` directory nodes are swapped later |
| CRUU10-008 | MED-HIGH | Retry recovery cannot distinguish pre-existing empty `prompts`/`recovery` directories from attempt-created directories and may delete user-preexisting directory structure |
| CRUU10-009 | MED-HIGH | Settings/library JSON authority still accepts duplicate critical properties other than `schemaVersion` and unknown fields that can be silently collapsed/dropped on rewrite |
| CRUU10-010 | MED | `PromptLibraryService.GetPrompts()` catches all exceptions and can hide programming/system failures as a normal unavailable prompt |
| CRUU10-011 | LOW-MED | Prompt headline/title size is unbounded in domain metadata |
| CRUU10-012 | MED | Migration reserved-name validation is overbroad by basename/prefix and conflicts with the stated goal of preserving orphan/recovery files |
| CRUU10-013 | MED verification gap | CRUD tests prove only in-process compensation, not restart recovery from process/power-loss cut points |
| CRUU10-014 | MED verification gap | Startup tests do not cover metadata-valid but body-incomplete primary/backup packages |
| CRUU10-015 | LOW-MED hardening | Legacy `IDirectoryCaseSensitivityInspector.IsCaseSensitive()` retains a fail-open API surface and should be removed |

---

# 3. New global invariants introduced by CRUU10

CRUU9 invariants remain mandatory.

CRUU10 adds these:

```text
INVARIANT H — PACKAGE HEALTH
A library is not "healthy" merely because library.json parses.
Every active PromptRecord must have a readable body before:
  - primary is trusted as a complete package;
  - primary overwrites backup;
  - backup is promoted as recovery.

INVARIANT I — MULTI-FILE MUTATION RECOVERY
Create/Edit/Duplicate/Delete operations that cross prompt body + metadata
must survive process death without requiring their catch blocks to run.

INVARIANT J — LONG-LIVED TREE AUTHORITY
Managed directory identity must remain fixed for the lifetime of the
running application, not merely pass one startup check.

INVARIANT K — PROVABLE ORPHAN DELETION
An unreferenced .md file is not automatically safe to delete.
Deletion is permitted only when all authoritative current metadata states
that could refer to it are known and current.

INVARIANT L — STRICT DIRECTORY PRESENCE
Missing, present, and unreadable directories are distinct states.
Directory.Exists false is not sufficient evidence of absence.

INVARIANT M — STRICT JSON AUTHORITY
Control/user metadata cannot contain ambiguous duplicate members or
unknown members that will be silently normalized away during rewrite.
```

---

# 4. Unified implementation order

The weak model must follow this exact order.

```text
PHASE 00  Baseline, source map, no-change verification
PHASE 01  CRUU9 strict file authority
PHASE 02  CRUU10 strict directory authority
PHASE 03  CRUU9/10 physical managed-tree validation
PHASE 04  Long-lived managed-data-root session lease
PHASE 05  Migration manifest v3 + all CRUU9 ownership fixes
PHASE 06  Durable atomic user-data writer
PHASE 07  Durable settings pointer + settings temp recovery
PHASE 08  Library package inspector
PHASE 09  Startup complete-package authority and backup recovery
PHASE 10  Library mutation journal for Create/Edit/Duplicate
PHASE 11  Delete tombstones + safe orphan reconciler
PHASE 12  Recovery baseline directory ownership
PHASE 13  Strict settings/library JSON member authority
PHASE 14  Exception filtering + title bounds + namespace precision
PHASE 15  Postcommit shutdown invariant
PHASE 16  Test-suite replacement/additions
PHASE 17  CI evidence enforcement
PHASE 18  Release icon identity tooling
PHASE 19  Full 5x regression + publish + final source audit
```

Do not start ordinary CRUD journaling before the single-file durable writer exists.

Do not add orphan auto-deletion before strict backup authority and package inspection exist.

---

# 5. CRUU10-001 — Strict directory authority

**Severity:** HIGH

## 5.1 Confirmed source problem

The codebase still uses boolean directory existence probes in safety-critical logic, including patterns equivalent to:

```csharp
Directory.Exists(path)
DirectoryInfo.Exists
```

These APIs return `false` for more than a true missing directory; filesystem
errors/permissions can collapse into "does not exist."

Affected design areas include:

```text
WindowsPhysicalPathResolver nearest-existing-ancestor walk
DataRootTopologyValidator.FindNearestExistingDirectory
EmptyTargetBaselineInspector
DefaultMigrationFileOps
DefaultReservationFileOps
TargetRootReservation ownership checks
recovery directory checks
AppPaths directory preparation
```

For an authority decision, this is not safe enough.

## 5.2 Required model

Create:

```text
src/PromptHelper/Services/StrictPathAuthority.cs
```

Use explicit states:

```csharp
namespace PromptHelper.Services;

internal enum StrictPathKind
{
    Missing,
    File,
    Directory
}

internal sealed record StrictPathProbe(
    StrictPathKind Kind,
    FileAttributes? Attributes);
```

### Copy-ready Windows/.NET implementation

```csharp
using System;
using System.IO;
using System.Security;

namespace PromptHelper.Services;

internal sealed class StrictPathAuthority
{
    public StrictPathProbe Probe(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            FileAttributes attributes =
                File.GetAttributes(path);

            bool isDirectory =
                (attributes & FileAttributes.Directory) != 0;

            return new StrictPathProbe(
                isDirectory
                    ? StrictPathKind.Directory
                    : StrictPathKind.File,
                attributes);
        }
        catch (FileNotFoundException)
        {
            return new StrictPathProbe(
                StrictPathKind.Missing,
                null);
        }
        catch (DirectoryNotFoundException)
        {
            return new StrictPathProbe(
                StrictPathKind.Missing,
                null);
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (SecurityException)
        {
            throw;
        }
        catch (IOException)
        {
            // Do not reinterpret arbitrary I/O errors as Missing.
            throw;
        }
    }

    public bool RequireDirectory(string path)
    {
        StrictPathProbe result = Probe(path);

        return result.Kind switch
        {
            StrictPathKind.Directory => true,
            StrictPathKind.Missing => false,
            StrictPathKind.File =>
                throw new InvalidDataException(
                    $"Expected a directory but found a file: '{path}'."),
            _ =>
                throw new InvalidOperationException(
                    $"Unhandled strict path state: {result.Kind}.")
        };
    }
}
```

### Stronger resolver requirement

For physical topology, prefer opening an actual directory handle instead of
relying only on `File.GetAttributes`.

Create:

```text
IStrictDirectoryOpener.cs
WindowsStrictDirectoryOpener.cs
```

Contract:

```csharp
internal enum DirectoryOpenState
{
    Missing,
    Opened
}

internal sealed record DirectoryOpenResult(
    DirectoryOpenState State,
    SafeFileHandle? Handle);
```

Native errors:

```text
ERROR_FILE_NOT_FOUND = Missing
ERROR_PATH_NOT_FOUND = Missing
anything else         = throw/fail closed
```

## 5.3 Replace nearest-existing-ancestor algorithm

Forbidden:

```csharp
while (current != null && !current.Exists)
```

Required shape:

```csharp
private string FindNearestExistingDirectoryStrict(
    string path)
{
    string current = Path.GetFullPath(path);

    while (true)
    {
        StrictPathProbe probe =
            _strictPathAuthority.Probe(current);

        if (probe.Kind == StrictPathKind.Directory)
        {
            return current;
        }

        if (probe.Kind == StrictPathKind.File)
        {
            throw new InvalidDataException(
                $"Path component is a file: '{current}'.");
        }

        string? parent = Path.GetDirectoryName(current);

        if (string.IsNullOrEmpty(parent) ||
            PathIdentity.Equals(parent, current))
        {
            throw new DirectoryNotFoundException(
                $"No accessible existing ancestor exists for '{path}'.");
        }

        current = parent;
    }
}
```

## 5.4 Tests

```text
CRUU10_001_Access_denied_directory_is_not_Missing
CRUU10_001_Unreadable_target_ancestor_aborts_topology
CRUU10_001_Unreadable_prompts_directory_is_not_treated_absent
CRUU10_001_Reservation_does_not_create_over_unreadable_path
CRUU10_001_Recovery_does_not_skip_unreadable_directory
```

Use injected fake states.

Do not depend only on ACL setup.

---

# 6. CRUU10-002 — Metadata-valid primary may destroy a better backup

**Severity:** HIGH

## 6.1 Confirmed source flow

Current startup behavior conceptually does:

```text
read library.json
parse schema
validate category/prompt records
if metadata valid:
    synchronize library.backup.json from primary
    return primary
```

It does **not** first prove:

```text
every active prompt body exists
every active prompt body is readable
```

Later display code allows a prompt to be rendered as unavailable.

That means a primary metadata file can be structurally valid but package
incomplete.

If the existing backup still describes a complete package, startup can
overwrite it before discovering the body problem.

## 6.2 Define package health

New file:

```text
LibraryPackageInspector.cs
```

Models:

```csharp
internal abstract record LibraryPackageState
{
    public sealed record Healthy(
        LibraryDocument Document,
        IReadOnlyDictionary<Guid, PromptBodySnapshot> Bodies)
        : LibraryPackageState;

    public sealed record MetadataInvalid(Exception Error)
        : LibraryPackageState;

    public sealed record BodyMissing(
        LibraryDocument Document,
        Guid PromptId,
        string Path)
        : LibraryPackageState;

    public sealed record BodyUnreadable(
        LibraryDocument Document,
        Guid PromptId,
        string Path,
        Exception Error)
        : LibraryPackageState;
}

internal sealed record PromptBodySnapshot(
    Guid PromptId,
    long Length,
    byte[] Sha256);
```

## 6.3 Copy-ready inspector

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using PromptHelper.Models;

namespace PromptHelper.Services;

internal sealed class LibraryPackageInspector
{
    private readonly AppPaths _paths;

    public LibraryPackageInspector(AppPaths paths)
    {
        _paths =
            paths ??
            throw new ArgumentNullException(nameof(paths));
    }

    public LibraryPackageState Inspect(
        LibraryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        LibraryValidator.Validate(document);

        var bodies =
            new Dictionary<Guid, PromptBodySnapshot>();

        foreach (PromptRecord prompt in document.Prompts)
        {
            string path =
                _paths.GetPromptPath(prompt.Id);

            byte[] bytes;

            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (FileNotFoundException)
            {
                return new LibraryPackageState.BodyMissing(
                    LibraryDocumentCloner.Clone(document),
                    prompt.Id,
                    path);
            }
            catch (DirectoryNotFoundException)
            {
                return new LibraryPackageState.BodyMissing(
                    LibraryDocumentCloner.Clone(document),
                    prompt.Id,
                    path);
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                SecurityException)
            {
                return new LibraryPackageState.BodyUnreadable(
                    LibraryDocumentCloner.Clone(document),
                    prompt.Id,
                    path,
                    ex);
            }

            bodies[prompt.Id] =
                new PromptBodySnapshot(
                    prompt.Id,
                    bytes.LongLength,
                    SHA256.HashData(bytes));
        }

        return new LibraryPackageState.Healthy(
            LibraryDocumentCloner.Clone(document),
            bodies);
    }
}
```

## 6.4 Startup authority

Do not use:

```text
MetadataReadResult.Valid
```

as equivalent to complete health.

Use:

```text
MetadataCurrent
PackageHealthy
PackageIncomplete
```

Recommended startup decision table:

| Primary | Backup | Result |
|---|---|---|
| metadata+package healthy | any non-future | primary wins; backup sync allowed |
| metadata current, package incomplete | backup package healthy | recover metadata from complete backup; warn |
| metadata current, package incomplete | backup incomplete/missing/corrupt | stop without overwriting backup |
| primary corrupt/missing | backup package healthy | recover |
| primary corrupt/missing | backup metadata valid but package incomplete | stop; do not claim recovery |
| future primary | anything | stop future-schema |
| unreadable primary | anything | stop unreadable; no fallback |

## 6.5 Critical ordering

The key fix is:

```text
Inspect PRIMARY metadata
Inspect PRIMARY package
ONLY IF package healthy:
    synchronize backup
```

Never:

```text
synchronize backup
then discover missing bodies
```

## 6.6 Recovery-copy behavior

If primary metadata is current but package incomplete:

```text
do not call it "corrupt JSON"
```

Create an optional diagnostics copy under `recovery/`:

```text
library.incomplete-<timestamp>-<guid>.json
```

Best-effort only.

Do not modify prompt bodies during this diagnostic copy.

## 6.7 Tests

```text
CRUU10_002_Primary_missing_body_does_not_overwrite_complete_backup
CRUU10_002_Primary_unreadable_body_does_not_overwrite_complete_backup
CRUU10_002_Healthy_primary_still_synchronizes_backup
CRUU10_002_Incomplete_primary_complete_backup_recovers_safely
CRUU10_002_Incomplete_primary_no_complete_backup_stops
```

Preservation assertion:

```csharp
byte[] backupBefore =
    File.ReadAllBytes(paths.LibraryBackupPath);

Assert.Throws<...>(
    () => service.LoadOrInitialize());

CollectionAssert.AreEqual(
    backupBefore,
    File.ReadAllBytes(paths.LibraryBackupPath));
```

---

# 7. CRUU10-003 — Backup recovery may promote an incomplete package

**Severity:** HIGH

## 7.1 Confirmed source flow

When primary is missing/corrupt and backup metadata parses, current startup
commits the backup document to primary.

It does not first prove all bodies referenced by the backup exist/read.

So startup can report:

```text
RecoveredFromBackup = true
```

even though the recovered package is unusable/incomplete.

## 7.2 Required fix

Reuse `LibraryPackageInspector`.

Do not invent a second body-validation path.

Before:

```csharp
_libraryRepo.Commit(backupValid.Document);
```

require:

```csharp
LibraryPackageState backupPackage =
    packageInspector.Inspect(
        backupValid.Document);

if (backupPackage
    is not LibraryPackageState.Healthy)
{
    throw new InvalidDataException(
        "The safety backup metadata is valid, but " +
        "its referenced prompt package is incomplete. " +
        "Prompt Helper did not promote it to primary.");
}
```

## 7.3 Recovery warning semantics

Successful recovery warning must mean:

```text
metadata recovered
AND
all referenced prompt bodies were verified readable
```

If bodies are not verified:

```text
do not use the word "recovered" as success.
```

## 7.4 Tests

```text
CRUU10_003_Missing_primary_backup_missing_body_does_not_promote
CRUU10_003_Corrupt_primary_backup_unreadable_body_does_not_promote
CRUU10_003_Backup_package_failure_preserves_primary_bytes_if_present
CRUU10_003_Complete_backup_recovers
```

---

# 8. CRUU10-004 — Ordinary prompt mutations are not crash transactional

**Severity:** HIGH

## 8.1 Confirmed source behavior

### Create

Current high-level order:

```text
write new .md body
commit library.json
catch commit failure -> best-effort delete body
```

A process/power crash after body creation but before metadata commit never
runs the catch.

Result:

```text
orphan body
no metadata record
```

### Edit

Current high-level order:

```text
read old body
write new body
commit metadata/title
catch metadata failure -> best-effort restore old body
```

A crash between body and metadata leaves:

```text
new body
old metadata/title
```

### Duplicate

Uses Create semantics.

## 8.2 Required architecture

Add a durable **library mutation journal**.

New files:

```text
LibraryMutationJournal.cs
LibraryMutationJournalRepository.cs
LibraryMutationRecoveryService.cs
```

Use one root control file:

```text
.prompthelper-library-mutation.json
```

Only one app instance owns a library due `.app.lock`, so one active mutation
journal is sufficient.

## 8.3 Journal schema

```csharp
internal enum LibraryMutationKind
{
    CreatePrompt,
    EditPrompt,
    DuplicatePrompt,
    DeletePrompt
}

internal enum LibraryMutationPhase
{
    Prepared,
    BodyDurable,
    MetadataDurable
}

internal sealed class LibraryMutationJournal
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } =
        CurrentSchemaVersion;

    public Guid OperationId { get; set; }

    public LibraryMutationKind Kind { get; set; }

    public LibraryMutationPhase Phase { get; set; }

    public Guid PromptId { get; set; }

    public string BodyRelativePath { get; set; } =
        string.Empty;

    public string? OldBodySha256Hex { get; set; }

    public long? OldBodyLength { get; set; }

    public string? NewBodySha256Hex { get; set; }

    public long? NewBodyLength { get; set; }

    public string OldLibrarySha256Hex { get; set; } =
        string.Empty;

    public string NewLibrarySha256Hex { get; set; } =
        string.Empty;

    public string? RecoveryBodyRelativePath { get; set; }
}
```

## 8.4 Critical design decision

For EDIT, do **not** depend on holding old body only in memory.

Before overwriting body, write an operation-owned recovery copy:

```text
recovery\mutation-<operationIdN>-old-<promptIdN>.md
```

Flush/durable-promote it.

Journal declares that exact path.

## 8.5 Create sequence

Required:

```text
1. Build candidate metadata JSON bytes.
2. Calculate old/new library hashes.
3. Allocate prompt final path and mutation operation ID.
4. Build Prepared journal.
5. Durably create journal.
6. Durably create prompt body.
7. Update journal -> BodyDurable.
8. Durably commit library.json.
9. Update journal -> MetadataDurable.
10. Synchronize backup using existing backup authority rules.
11. Delete journal.
12. Return success/warning.
```

Crash recovery:

```text
Prepared:
    body should not be authoritative;
    strict cleanup exact declared temp/control.

BodyDurable:
    if library still old hash:
        delete newly-created prompt body after verifying expected new hash.
    if library is new hash:
        treat metadata commit as completed and finalize.

MetadataDurable:
    verify new metadata + new body;
    finalize journal cleanup.
```

## 8.6 Edit sequence

Required:

```text
1. Strict-read old body.
2. Build candidate metadata.
3. Write old-body recovery copy.
4. Durably publish Prepared journal including both hashes.
5. Durably replace body with new bytes.
6. journal -> BodyDurable.
7. Durably replace library.json with candidate.
8. journal -> MetadataDurable.
9. sync backup.
10. delete old-body recovery copy.
11. delete journal.
```

Recovery:

```text
library old + body new:
    restore old body from recovery copy.

library new + body new:
    commit completed; remove recovery copy.

library old + body old:
    operation never advanced; cleanup journal/recovery copy.

unexpected hash:
    fail closed; keep journal + recovery copy.
```

## 8.7 Duplicate

Reuse Create transaction.

Do not copy/paste a separate implementation.

## 8.8 Never make journal best-effort

If journal cannot be durably created:

```text
do not mutate body or metadata.
```

## 8.9 Copy-ready transaction facade

```csharp
internal sealed class PromptMutationCoordinator
{
    private readonly AppPaths _paths;
    private readonly PromptRepository _promptRepo;
    private readonly LibraryRepository _libraryRepo;
    private readonly LibraryMutationJournalRepository _journalRepo;
    private readonly IDurableAtomicFileWriter _writer;

    public OperationResult<PromptRecord> CreatePrompt(
        LibraryDocument current,
        LibraryDocument candidate,
        PromptRecord prompt,
        string body)
    {
        Guid operationId = Guid.NewGuid();

        byte[] bodyBytes =
            System.Text.Encoding.UTF8.GetBytes(body);

        string oldLibraryJson =
            LibraryRepository.SerializeCanonical(current);

        string newLibraryJson =
            LibraryRepository.SerializeCanonical(candidate);

        var journal =
            LibraryMutationJournalFactory.ForCreate(
                operationId,
                prompt.Id,
                oldLibraryJson,
                newLibraryJson,
                bodyBytes);

        _journalRepo.CreatePreparedDurable(journal);

        try
        {
            _promptRepo.CreateDurable(
                prompt.Id,
                body);

            _journalRepo.AdvanceDurable(
                journal,
                LibraryMutationPhase.BodyDurable);

            CommitResult commit =
                _libraryRepo.CommitDurable(candidate);

            _journalRepo.AdvanceDurable(
                journal,
                LibraryMutationPhase.MetadataDurable);

            _journalRepo.DeleteDurable();

            return new OperationResult<PromptRecord>(
                LibraryDocumentCloner.ClonePrompt(prompt),
                commit.Warning);
        }
        catch
        {
            // In-process recovery may be attempted here,
            // but startup recovery remains authoritative.
            throw;
        }
    }
}
```

The exact helper names may differ, but the state machine may not.

## 8.10 Tests

Crash fixtures, not only exceptions:

```text
CRUU10_004_Create_crash_after_journal_before_body_recovers
CRUU10_004_Create_crash_after_body_before_metadata_removes_orphan
CRUU10_004_Create_crash_after_metadata_before_journal_retirement_finalizes
CRUU10_004_Edit_crash_after_old_recovery_copy_recovers
CRUU10_004_Edit_crash_after_new_body_before_metadata_restores_old_body
CRUU10_004_Edit_crash_after_metadata_before_cleanup_keeps_new_body
CRUU10_004_Unexpected_body_hash_preserves_journal_and_stops
CRUU10_004_Duplicate_uses_same_transaction_state_machine
```

---

# 9. CRUU10-005 — Permanent orphan lifecycle

**Severity:** MEDIUM-HIGH

## 9.1 Confirmed source behavior

Delete currently may intentionally preserve the `.md` body when:

```text
library.backup.json was not synchronized
```

and may leave an orphan on body-delete failure.

This is initially conservative and good.

But there is no later reconciliation.

Subsequent data-folder migration intentionally copies orphan `*.md` files.

Therefore deleted prompt content can survive indefinitely and propagate to
future roots.

## 9.2 Do not fix with "delete all unreferenced .md"

That would be unsafe.

A body may still be referenced by:

```text
current primary
current backup
future-schema backup that this build must preserve
unreadable backup whose authority cannot be established
active mutation journal
migration journal
```

## 9.3 Create orphan reconciler

```text
PromptOrphanReconciler.cs
```

Models:

```csharp
internal sealed record OrphanReconciliationResult(
    IReadOnlyList<string> Deleted,
    IReadOnlyList<string> Preserved,
    string? Warning);
```

## 9.4 Safe deletion rule

A prompt body GUID may be deleted only if all are true:

```text
1. no active LibraryMutationJournal references it;
2. no active migration journal references it as owned source/target state;
3. primary metadata is current/readable and does not reference GUID;
4. backup metadata is current/readable and does not reference GUID;
5. body filename is exact <guidN>.md;
6. file is ordinary managed-tree content, not reparse escape;
7. no future-schema or unreadable backup exists.
```

If backup is:

```text
Future
Unreadable
```

preserve orphan.

## 9.5 When to run

At startup, after:

```text
migration recovery
mutation recovery
library package health established
backup synchronization attempt
```

Then reconcile.

Do not run before recovery.

## 9.6 Optional tombstone

For DeletePrompt, a tombstone improves determinism:

```text
recovery\delete-<operationIdN>-<promptIdN>.json
```

or include Delete in the same library mutation journal.

Preferred: same mutation journal.

Delete state machine:

```text
Prepared
MetadataDurable
BodyDeleted
```

If backup sync fails:

```text
leave body
remove active journal only after recording that it is a conservative orphan
```

A persistent orphan manifest is not necessary if reconciler proves references
from current metadata each startup.

## 9.7 Tests

```text
CRUU10_005_Orphan_referenced_by_backup_is_preserved
CRUU10_005_Orphan_unreferenced_by_current_primary_and_backup_is_deleted
CRUU10_005_Future_backup_preserves_orphan
CRUU10_005_Unreadable_backup_preserves_orphan
CRUU10_005_Active_mutation_journal_preserves_orphan
CRUU10_005_Reconciled_orphan_is_not_copied_by_later_migration
```

---

# 10. CRUU10-006 — General durable atomic user-data writer

**Severity:** HIGH

## 10.1 Confirmed source problem

`AtomicTextWriter`:

```text
writes temp
Flush(true)
File.Replace or File.Move
best-effort temp cleanup
```

The temp contents are flushed, but final promotion has no explicit
write-through ordering equivalent to the migration payload implementation.

This affects more than settings:

```text
library.json
library.backup.json
prompt .md bodies
initialization marker
recovery copies
```

CRUU9 already requires a stronger settings writer.

CRUU10 generalizes it.

## 10.2 New interface

```text
IDurableAtomicFileWriter.cs
WindowsDurableAtomicFileWriter.cs
```

```csharp
internal interface IDurableAtomicFileWriter
{
    void WriteTextDurable(
        string targetPath,
        string content,
        DurableFileClass fileClass);

    void WriteBytesDurable(
        string targetPath,
        ReadOnlySpan<byte> content,
        DurableFileClass fileClass);
}

internal enum DurableFileClass
{
    Settings,
    LibraryMetadata,
    PromptBody,
    RecoveryArtifact,
    ControlMarker
}
```

## 10.3 Reserved temp naming

Use deterministic parseable classes:

```text
.prompthelper-tmp-settings-<guidN>.tmp
.prompthelper-tmp-library-<guidN>.tmp
.prompthelper-tmp-prompt-<guidN>.tmp
.prompthelper-tmp-recovery-<guidN>.tmp
.prompthelper-tmp-control-<guidN>.tmp
```

Place temp in same directory as final for same-volume atomic promotion.

## 10.4 Copy-ready Windows promotion

```csharp
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace PromptHelper.Services;

internal sealed class WindowsDurableFilePromoter
{
    private const uint MOVEFILE_REPLACE_EXISTING =
        0x00000001;

    private const uint MOVEFILE_WRITE_THROUGH =
        0x00000008;

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern bool MoveFileExW(
        string lpExistingFileName,
        string lpNewFileName,
        uint dwFlags);

    public void PromoteReplaceWriteThrough(
        string source,
        string destination)
    {
        if (!MoveFileExW(
                source,
                destination,
                MOVEFILE_REPLACE_EXISTING |
                MOVEFILE_WRITE_THROUGH))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Durable promotion failed: " +
                $"'{source}' -> '{destination}'.");
        }
    }
}
```

Writer order:

```text
CreateNew temp
write bytes
Flush(true)
close temp
MoveFileEx(REPLACE_EXISTING | WRITE_THROUGH)
```

## 10.5 Stale temp cleaner

Create:

```text
DurableTempName.cs
DurableTempReconciler.cs
```

Only delete exact valid Prompt Helper temp grammar.

Never:

```csharp
Directory.EnumerateFiles(root, "*.tmp")
    .ForEach(File.Delete);
```

## 10.6 Cleanup authority

For each stale temp:

```text
if active mutation/migration journal declares it:
    journal recovery owns it.

else if name matches reserved durable-writer grammar:
    it is a pre-promotion staging file and may be removed after strict
    managed-tree containment check.

else:
    foreign; preserve.
```

## 10.7 Replace old writer

Long-term preferred:

```text
remove AtomicTextWriter from production user-data paths
```

Tests may retain it only if explicitly testing legacy compatibility.

## 10.8 Tests

```text
CRUU10_006_Durable_writer_flushes_before_promotion
CRUU10_006_Durable_writer_uses_write_through_replace
CRUU10_006_Promotion_failure_preserves_old_target
CRUU10_006_Stale_reserved_temp_reconciles
CRUU10_006_Similar_foreign_tmp_is_preserved
```

---

# 11. CRUU10-007 — One-time tree validation is not enough

**Severity:** HIGH

## 11.1 Problem

CRUU9 correctly requires validating:

```text
prompts
recovery
```

against reparse escape.

But normal runtime code later uses:

```csharp
Path.Combine(root, "prompts", ...)
```

for every read/write.

If another actor can rename/swap the `prompts` directory node after the
check, subsequent operations can be redirected.

## 11.2 Required long-lived session lease

New:

```text
ManagedDataRootSessionLease.cs
```

Hold handles for the whole application lifetime:

```text
root
prompts
recovery
```

Open directory handles with:

```text
FILE_FLAG_BACKUP_SEMANTICS
desired FILE_READ_ATTRIBUTES
share READ | WRITE
NO FILE_SHARE_DELETE
```

The absence of delete sharing prevents rename/delete replacement of the
managed directory node while the handle is held.

## 11.3 Copy-ready skeleton

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

internal sealed class ManagedDataRootSessionLease
    : IDisposable
{
    private readonly List<SafeFileHandle> _handles;

    private ManagedDataRootSessionLease(
        List<SafeFileHandle> handles)
    {
        _handles = handles;
    }

    public static ManagedDataRootSessionLease Acquire(
        string physicalRoot,
        IManagedDirectoryHandleApi? native = null)
    {
        var api =
            native ??
            new WindowsManagedDirectoryHandleApi();

        var handles =
            new List<SafeFileHandle>();

        try
        {
            foreach (string directory in new[]
                     {
                         physicalRoot,
                         Path.Combine(physicalRoot, "prompts"),
                         Path.Combine(physicalRoot, "recovery")
                     })
            {
                SafeFileHandle handle =
                    api.OpenManagedDirectoryWithoutDeleteShare(
                        directory);

                if (handle.IsInvalid)
                {
                    throw api.CreateLastError(
                        directory);
                }

                handles.Add(handle);
            }

            return new ManagedDataRootSessionLease(
                handles);
        }
        catch
        {
            foreach (SafeFileHandle handle in handles)
            {
                handle.Dispose();
            }

            throw;
        }
    }

    public void Dispose()
    {
        for (int i = _handles.Count - 1;
             i >= 0;
             i--)
        {
            _handles[i].Dispose();
        }

        _handles.Clear();
    }
}
```

## 11.4 App lifetime

In `App`:

```csharp
private ManagedDataRootSessionLease?
    _managedTreeLease;
```

Startup:

```text
after app lock
after migration/mutation recovery if those need to create dirs
after EnsureDataDirectories
after managed-tree validation
before repositories/MainWindow
```

Then:

```csharp
_managedTreeLease =
    ManagedDataRootSessionLease.Acquire(
        paths.RootDirectory);
```

Dispose in `OnExit`.

### Important sequencing nuance

Recovery itself also needs swap protection.

Use a **recovery lease** before recovery deletions.

After recovery and normal directory creation, replace it with the long-lived
session lease.

## 11.5 Tests

Real Windows:

```text
CRUU10_007_Prompts_directory_cannot_be_replaced_while_session_lease_held
CRUU10_007_Recovery_directory_cannot_be_replaced_while_session_lease_held
CRUU10_007_Lease_release_allows_normal_cleanup_after_shutdown
```

Never fake this and label it WindowsFilesystemIntegration.

---

# 12. CRUU10-008 — Recovery deletes pre-existing empty directories

**Severity:** MED-HIGH

## 12.1 Problem

Empty-target baseline currently permits pre-existing empty:

```text
prompts\
recovery\
```

A migration can then populate them.

On interrupted retry cleanup, recovery removes those directories if they
become empty.

But it does not know whether they:

```text
existed before attempt
or
were created by attempt
```

## 12.2 Persist baseline ownership in manifest v3

CRUU9 schema v3 must add:

```csharp
internal sealed class MigrationTargetBaseline
{
    public bool TargetRootExistedBefore { get; set; }

    public bool PromptsDirectoryExistedBefore { get; set; }

    public bool RecoveryDirectoryExistedBefore { get; set; }
}
```

Manifest:

```csharp
public MigrationTargetBaseline Baseline
    { get; set; } = new();
```

Capture **before reservation/copy mutation** using strict directory authority.

## 12.3 Recovery rule

After files are removed:

```csharp
if (!manifest.Baseline.PromptsDirectoryExistedBefore)
{
    DeleteIfEmptyStrict(prompts);
}

if (!manifest.Baseline.RecoveryDirectoryExistedBefore)
{
    DeleteIfEmptyStrict(recovery);
}
```

If pre-existing:

```text
leave directory even when empty
```

For root:

```text
TargetRootReservation owns root chain separately.
```

## 12.4 Tests

```text
CRUU10_008_Preexisting_empty_prompts_survives_retry_cleanup
CRUU10_008_Preexisting_empty_recovery_survives_retry_cleanup
CRUU10_008_Attempt_created_prompts_removed_on_retry
CRUU10_008_Attempt_created_recovery_removed_on_retry
```

---

# 13. CRUU10-009 — Strict settings/library JSON members

**Severity:** MED-HIGH

## 13.1 Current ambiguity

Existing raw validation explicitly counts duplicate:

```text
schemaVersion
```

but not all other critical properties.

Case-insensitive deserialization can collapse duplicate values such as:

```json
{
  "schemaVersion": 1,
  "dataRootPath": "C:\\A",
  "DataRootPath": "D:\\B"
}
```

or library data with duplicate arrays/record fields.

A later rewrite normalizes the ambiguous input into one chosen value.

That is not acceptable for an authority file.

## 13.2 Settings allowed root members

Exactly:

```text
schemaVersion
dataRootPath
```

Reject:

```text
duplicate case-insensitive member
unknown root member
```

## 13.3 Library root members

Exactly:

```text
schemaVersion
categories
prompts
```

Category object:

```text
id
parentId
name
sortOrder
```

Prompt object:

```text
id
categoryId
sortOrder
title
```

Reject duplicates and unknown members.

## 13.4 Helper

New:

```text
StrictJsonObjectAuthority.cs
```

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PromptHelper.Services;

internal static class StrictJsonObjectAuthority
{
    public static void ValidateExactMembers(
        JsonElement element,
        IReadOnlySet<string> allowed,
        string description)
    {
        if (element.ValueKind !=
            JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"{description} must be a JSON object.");
        }

        var seen =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (JsonProperty property
                 in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new InvalidDataException(
                    $"{description} contains duplicate " +
                    $"property '{property.Name}'.");
            }

            if (!allowed.Contains(property.Name))
            {
                throw new InvalidDataException(
                    $"{description} contains unknown " +
                    $"property '{property.Name}'.");
            }
        }
    }
}
```

Call recursively before deserialization.

## 13.5 Strict UTF-8

Read bytes with:

```csharp
new UTF8Encoding(
    encoderShouldEmitUTF8Identifier: false,
    throwOnInvalidBytes: true)
```

Do not silently replace invalid sequences.

## 13.6 Tests

```text
CRUU10_009_Settings_duplicate_dataRootPath_rejected
CRUU10_009_Settings_unknown_member_rejected
CRUU10_009_Library_duplicate_prompts_rejected
CRUU10_009_Category_duplicate_id_rejected
CRUU10_009_Prompt_duplicate_title_rejected
CRUU10_009_Library_unknown_root_member_rejected
CRUU10_009_Invalid_UTF8_library_rejected
```

---

# 14. CRUU10-010 — Overbroad exception swallowing in `GetPrompts`

**Severity:** MED

## 14.1 Current behavior

Prompt display loading catches:

```csharp
catch (Exception ex)
```

and converts every exception into:

```text
loadError
```

This can turn programming defects into ordinary unavailable-prompt UI.

Examples that should not be normalized:

```text
NullReferenceException
ArgumentOutOfRangeException
OutOfMemoryException
StackOverflowException
TypeInitializationException
```

## 14.2 Required filter

```csharp
catch (Exception ex) when (
    ex is IOException or
    UnauthorizedAccessException or
    SecurityException)
{
    loadError = ex.Message;
}
```

`FileNotFoundException` and `DirectoryNotFoundException` derive from
`IOException`.

Do not catch `Exception` here.

## 14.3 Tests

Create fake PromptRepository seam if needed.

```text
CRUU10_010_IOException_becomes_unavailable_prompt
CRUU10_010_Unauthorized_becomes_unavailable_prompt
CRUU10_010_Programmer_exception_propagates
```

---

# 15. CRUU10-011 — Prompt title/headline is unbounded

**Severity:** LOW-MED

## 15.1 Current behavior

Category names have a domain cap.

Prompt titles validate:

```text
trim
nonblank/null
single-line/control characters
```

but have no maximum.

UI preview truncates the display, not stored metadata.

An arbitrarily large headline can bloat:

```text
library.json
library.backup.json
WPF editing state
destination/preview processing
migration snapshots
```

## 15.2 Locked limit

Use:

```csharp
public const int MaximumPromptTitleTextElements = 160;
```

This is a domain limit in Unicode text elements, not UTF-16 code units.

## 15.3 Shared validation

Add:

```csharp
public static string? ValidatePromptTitleInput(
    string? input)
```

Return null when valid.

Then service normalization calls it.

```csharp
if (TextUtilities.GetTextElementCount(trimmed) >
    MaximumPromptTitleTextElements)
{
    throw new InvalidOperationException(
        $"Headline cannot exceed " +
        $"{MaximumPromptTitleTextElements} characters.");
}
```

## 15.4 UI

Do not set XAML `MaxLength=160`.

That counts UTF-16 units, not grapheme/text elements.

Validate on Save and keep dialog open with an inline error.

## 15.5 Tests

```text
CRUU10_011_Title_160_text_elements_allowed
CRUU10_011_Title_161_text_elements_rejected
CRUU10_011_Emoji_grapheme_count_is_domain_correct
CRUU10_011_Loaded_library_with_oversize_title_rejected
```

---

# 16. CRUU10-012 — Reserved namespace rule conflicts with preservation

**Severity:** MED

## 16.1 Problem

Migration path validation rejects reserved-looking **basenames/prefixes**
broadly.

But source snapshot policy intentionally carries:

```text
orphan prompts/*.md
all recovery files
```

A legitimate historical/user file such as:

```text
prompts\.prompthelper-notes.md
recovery\settings.json
```

can therefore be captured as payload and then rejected as a migration
artifact only because its basename resembles a root control.

## 16.2 Required rule

Reserved controls are **exact root-relative paths**, not arbitrary basenames.

Exact root controls:

```text
.app.lock
.settings.lock
.prompthelper-migration.json
.prompthelper-library-mutation.json
settings.json
settings.backup.json
initializing.marker
```

Settings files are bootstrap-root controls only when operating on exact
bootstrap root.

Nested:

```text
prompts\.app.lock
recovery\settings.json
```

are not root controls.

They are either:

```text
valid data to preserve
or
foreign data that the source snapshot policy must explicitly reject
```

but not silently reclassified by basename.

## 16.3 Helper

```csharp
internal static bool IsReservedRootControl(
    string relativePath)
{
    string normalized =
        relativePath
            .Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar)
            .TrimStart(
                Path.DirectorySeparatorChar);

    return normalized.Equals(
               ".app.lock",
               StringComparison.OrdinalIgnoreCase) ||
           normalized.Equals(
               ".prompthelper-migration.json",
               StringComparison.OrdinalIgnoreCase) ||
           normalized.Equals(
               ".prompthelper-library-mutation.json",
               StringComparison.OrdinalIgnoreCase) ||
           normalized.Equals(
               "initializing.marker",
               StringComparison.OrdinalIgnoreCase);
}
```

Bootstrap settings controls handled separately.

## 16.4 Tests

```text
CRUU10_012_Root_migration_marker_rejected_as_payload
CRUU10_012_Nested_similar_name_not_mistaken_for_root_control
CRUU10_012_Source_snapshot_and_manifest_validation_share_same_namespace_policy
```

---

# 17. CRUU10-013 — CRUD crash coverage gap

**Severity:** MED verification gap

Existing tests inject ordinary exceptions while the same process remains
alive.

They prove catch blocks.

They do not prove crash recovery.

Current tests even explicitly accept states such as:

```text
Create cleanup failure leaves orphan
Delete file failure leaves orphan
Delete backup failure keeps file
```

That is useful for in-process behavior, but insufficient.

## Required fixture style

Do not kill the test runner.

Create exact on-disk cut-point fixtures representing states after process
death.

New helper:

```text
LibraryMutationCrashFixtureBuilder.cs
```

API:

```csharp
internal sealed class
    LibraryMutationCrashFixtureBuilder
{
    public LibraryMutationCrashFixtureBuilder
        WithJournal(
            LibraryMutationJournal journal);

    public LibraryMutationCrashFixtureBuilder
        WithPrimaryJson(string json);

    public LibraryMutationCrashFixtureBuilder
        WithBody(Guid id, byte[] bytes);

    public LibraryMutationCrashFixtureBuilder
        WithRecoveryBody(
            Guid id,
            byte[] bytes);

    public void Write();
}
```

Then instantiate a fresh:

```text
LibraryMutationRecoveryService
LibraryStartupService
```

as a simulated restart.

Mandatory tests are listed in CRUU10-004.

---

# 18. CRUU10-014 — Startup package tests are missing

**Severity:** MED verification gap

Current startup tests cover:

```text
valid metadata
corrupt metadata
future schema
missing metadata
backup sync
first-run marker
```

They do not cover:

```text
valid primary metadata + missing active .md
valid primary metadata + unreadable active .md
valid backup metadata + missing body
primary incomplete + backup complete
primary incomplete + backup incomplete
```

These tests must be added before startup package logic can be considered
accepted.

Exact tests:

```text
CRUU10_014_Valid_metadata_missing_body_is_not_Healthy
CRUU10_014_Incomplete_primary_does_not_sync_backup
CRUU10_014_Complete_backup_can_recover_incomplete_primary
CRUU10_014_Incomplete_backup_is_not_successful_recovery
```

---

# 19. CRUU10-015 — Remove legacy fail-open case-sensitivity API

**Severity:** LOW-MED hardening

The newer API:

```csharp
Inspect(...)
```

can fail closed.

The legacy convenience API:

```csharp
IsCaseSensitive(...)
```

still invites code such as:

```csharp
if (string.IsNullOrWhiteSpace(path) ||
    !Directory.Exists(path))
{
    return false;
}
```

That API surface makes future regression likely.

## Required change

Interface becomes:

```csharp
public interface
    IDirectoryCaseSensitivityInspector
{
    DirectoryCaseSensitivityState Inspect(
        string existingDirectory);
}
```

Delete:

```text
IsCaseSensitive
```

Production call sites:

```csharp
if (_caseInspector.Inspect(path) ==
    DirectoryCaseSensitivityState.CaseSensitive)
{
    ...
}
```

Missing/unreadable directory handling belongs to strict directory authority,
not to a boolean convenience method.

## Tests

Compiler + focused policy tests are sufficient.

```text
CRUU10_015_No_boolean_case_sensitivity_API_remains
CRUU10_015_Inspection_failure_propagates_fail_closed
```

---

# 20. New startup authority pipeline

After CRUU10, application startup must use this exact conceptual order:

```text
01 acquire settings mutation lease
02 reconcile strict stale settings writer temps
03 load/recover strict settings
04 resolve lexical configured root
05 resolve physical configured root
06 resolve physical bootstrap root
07 strict topology + case-sensitivity checks
08 acquire root app lock
09 strict managed-tree recovery lease
10 finalize data-folder migration journal if present
11 finalize library-mutation journal if present
12 ensure ordinary prompts/recovery dirs
13 validate managed child physical identity
14 acquire long-lived ManagedDataRootSessionLease
15 read primary metadata strictly
16 inspect primary PACKAGE health
17 inspect backup only as required by decision table
18 recover only from complete package
19 synchronize backup only from complete healthy primary
20 run safe orphan reconciler
21 construct repositories/services/viewmodels
22 show MainWindow
```

Never show the main window before:

```text
migration recovery complete
mutation recovery complete
package health established
long-lived managed-tree lease held
```

---

# 21. Library mutation recovery decision matrix

## 21.1 Create/Duplicate

| Journal | Body | Library | Action |
|---|---|---|---|
| Prepared | missing | old | remove journal |
| Prepared/BodyDurable | new exact | old | verify+delete new body, remove journal |
| BodyDurable | new exact | new | finalize commit, remove journal |
| any | unexpected bytes | any | fail closed; preserve journal |
| any | any | neither old nor new metadata hash | fail closed |

## 21.2 Edit

| Body | Primary | Old recovery copy | Action |
|---|---|---|---|
| old | old | present/optional | cleanup journal |
| new | old | exact old | restore old body, cleanup |
| new | new | exact old | commit completed, cleanup recovery copy |
| old | new | exact old | inconsistent; fail closed |
| unexpected | any | any | fail closed |

## 21.3 Delete

Preferred order:

```text
journal Prepared
metadata new (prompt removed)
journal MetadataDurable
backup sync
verified body delete if safe
journal BodyDeleted
retire journal
```

If backup is not synchronized:

```text
do not delete body
journal may retire after recording normal orphan state
orphan reconciler handles later proof
```

---

# 22. Copy-ready `LibraryMutationRecoveryService` skeleton

```csharp
internal sealed class
    LibraryMutationRecoveryService
{
    private readonly AppPaths _paths;
    private readonly LibraryMutationJournalRepository
        _journalRepo;
    private readonly IDurableAtomicFileWriter _writer;
    private readonly IVerifiedArtifactDeleter
        _verifiedDeleter;

    public void RecoverIfPresent()
    {
        LibraryMutationJournal? journal =
            _journalRepo.TryReadStrict();

        if (journal is null)
        {
            return;
        }

        switch (journal.Kind)
        {
            case LibraryMutationKind.CreatePrompt:
            case LibraryMutationKind.DuplicatePrompt:
                RecoverCreateLike(journal);
                return;

            case LibraryMutationKind.EditPrompt:
                RecoverEdit(journal);
                return;

            case LibraryMutationKind.DeletePrompt:
                RecoverDelete(journal);
                return;

            default:
                throw new InvalidDataException(
                    $"Unsupported library mutation kind: " +
                    $"{journal.Kind}.");
        }
    }

    private void RecoverCreateLike(
        LibraryMutationJournal journal)
    {
        LibraryHashState library =
            ReadLibraryHashStateStrict();

        PromptBodyHashState body =
            ReadBodyHashStateStrict(
                journal.PromptId);

        if (library.Matches(
                journal.NewLibrarySha256Hex))
        {
            if (!body.Matches(
                    journal.NewBodySha256Hex,
                    journal.NewBodyLength))
            {
                throw new InvalidDataException(
                    "Committed metadata references a prompt " +
                    "body that does not match its mutation journal.");
            }

            _journalRepo.DeleteDurable();
            return;
        }

        if (library.Matches(
                journal.OldLibrarySha256Hex))
        {
            if (body.Matches(
                    journal.NewBodySha256Hex,
                    journal.NewBodyLength))
            {
                _verifiedDeleter.VerifyAndDelete(
                    _paths.RootDirectory,
                    _paths.GetPromptPath(journal.PromptId),
                    journal.NewBodyLength!.Value,
                    journal.NewBodySha256Hex!);
            }
            else if (!body.IsMissing)
            {
                throw new InvalidDataException(
                    "Unexpected prompt body exists during " +
                    "create-mutation recovery.");
            }

            _journalRepo.DeleteDurable();
            return;
        }

        throw new InvalidDataException(
            "library.json matches neither the old nor " +
            "the new mutation state.");
    }

    // RecoverEdit and RecoverDelete follow the decision matrix.
}
```

The weak model may refactor helper records, but may not change state-machine
semantics.

---

# 23. Strict metadata serialization authority

Add one canonical serializer:

```csharp
internal static string SerializeCanonical(
    LibraryDocument document)
{
    LibraryValidator.Validate(document);

    return JsonSerializer.Serialize(
        document,
        JsonOptions);
}
```

Mutation hashes must hash the exact UTF-8 bytes that will be durably written.

Do not hash a differently formatted representation.

```csharp
byte[] newLibraryBytes =
    new UTF8Encoding(false)
        .GetBytes(
            LibraryRepository.SerializeCanonical(
                candidate));
```

Use those bytes for both:

```text
journal hash
actual durable write
```

---

# 24. Prevent package validation from corrupting backup authority

Do not hide package checks in `LibraryRepository.Commit` in a way that
creates circular dependencies.

Recommended layering:

```text
LibraryRepository:
    strict metadata read/write only

LibraryPackageInspector:
    metadata + prompt-body package health

PromptMutationCoordinator:
    creates consistent package transitions

LibraryStartupService:
    chooses authority using package states
```

Before `SynchronizeBackup(primary)` at startup:

```text
LibraryStartupService explicitly requires Healthy package.
```

---

# 25. Orphan reconciler exact pseudo-code

```csharp
public OrphanReconciliationResult Reconcile(
    LibraryDocument primary,
    LibraryMetadataAuthority backupAuthority,
    LibraryMutationJournal? activeJournal)
{
    var protectedIds =
        new HashSet<Guid>(
            primary.Prompts.Select(p => p.Id));

    if (backupAuthority
        is LibraryMetadataAuthority.Current currentBackup)
    {
        protectedIds.UnionWith(
            currentBackup.Document.Prompts.Select(p => p.Id));
    }
    else
    {
        // Future, unreadable, corrupt-with-uncertain-authority:
        // preserve all orphans conservatively.
        return PreserveAllWithWarning(...);
    }

    if (activeJournal is not null)
    {
        protectedIds.Add(activeJournal.PromptId);
    }

    foreach (string path in
             _promptRepo.EnumeratePromptFiles())
    {
        string stem =
            Path.GetFileNameWithoutExtension(path);

        if (!Guid.TryParseExact(
                stem,
                "N",
                out Guid id))
        {
            // Non-GUID orphan is foreign. Preserve.
            continue;
        }

        if (protectedIds.Contains(id))
        {
            continue;
        }

        _managedTreeValidator
            .ValidateArtifactPath(path);

        _deleter.DeleteIfExists(path);
    }
}
```

If current backup is merely corrupt but primary is known healthy:

Preferred conservative rule:

```text
do not delete orphans until backup is successfully repaired/synchronized.
```

Then run reconciler.

---

# 26. Recovery and mutation journals must not conflict

At startup, if both exist:

```text
.prompthelper-migration.json
.prompthelper-library-mutation.json
```

do not guess.

Expected normal product behavior:

```text
a data-folder migration is initiated only from a quiescent running app
with no active prompt mutation.
```

Therefore both simultaneously indicate an interrupted/unexpected state.

Locked rule:

```text
if both journals exist at the same root:
    fail closed with explicit conflict error.
```

Do not recover them in arbitrary order until a future schema explicitly
supports nesting.

Add test:

```text
CRUU10_JOURNAL_CONFLICT_Both_journals_present_blocks_startup
```

---

# 27. Required path-control namespace after CRUU10

Exact root-relative controls:

```text
.app.lock
.settings.lock                      (bootstrap root only)
settings.json                       (bootstrap root only)
settings.backup.json                (bootstrap root only)
.prompthelper-migration.json
.prompthelper-library-mutation.json
initializing.marker
```

Attempt-specific declared controls:

```text
manifest Ready stage
capability probes
durable writer temps
mutation recovery copies
```

Every class must be either:

```text
PersistentControl
AttemptOwnedControl
UserData
Foreign
```

No path may be both.

---

# 28. Combined CRUU9 + CRUU10 fault-injection matrix

The implementing AI must add deterministic seams for at least these cut
points:

```text
FILESYSTEM AUTHORITY
- file exists check -> access denied
- directory exists check -> access denied
- child prompts path -> junction
- child recovery path -> junction
- child node swap attempt while session lease held

MIGRATION
- Copying marker initial write partial
- Copying marker flush failure
- payload temp create
- payload temp write
- payload temp flush
- payload final promotion
- source changes after snapshot
- target changes after copy
- capability probe dir create
- capability current file create
- capability replacement file create
- Ready stage create
- Ready stage flush
- Ready replace
- settings durable temp write
- settings durable promotion
- marker retirement
- reservation release
- foreign entry injected after cleanup

STARTUP PACKAGE
- primary JSON current, active body missing
- primary JSON current, body access denied
- backup JSON current, body missing
- primary incomplete + backup complete
- both incomplete

CRUD CREATE
- journal create
- body create
- body durable
- metadata promotion
- backup sync
- journal retirement

CRUD EDIT
- old-body recovery copy
- journal create
- new body durable
- metadata durable
- backup sync
- recovery-copy delete
- journal retirement

CRUD DELETE
- journal create
- metadata durable
- backup sync fail
- body delete fail
- journal retirement

ORPHANS
- backup future
- backup unreadable
- backup corrupt then successfully synchronized
- active mutation journal

UI
- postcommit information message throws
- DialogResult null
- DialogResult false with RestartRequired true

CI
- zero tests
- one failed
- one skipped mandatory sentinel
- missing mandatory sentinel
- fake-only Windows category
```

No sleeps as synchronization.

---

# 29. Required test categories after CRUU10

Use exact categories:

```text
CrashRecovery
PackageIntegrity
MutationRecovery
FilesystemAuthority
WpfIntegration
WindowsFilesystemIntegration
ReleaseVerification
```

Tests may belong to more than one category.

CI must run:

```text
CrashRecovery
PackageIntegrity
MutationRecovery
WindowsFilesystemIntegration
WpfIntegration
```

as explicit commands.

Then full suite.

---

# 30. Exact CI evidence rules

`VerifyTestEvidence.ps1` must:

```text
fail if TRX missing
fail if total <= 0
fail if failed/error/timeout/aborted > 0
fail if required test name absent
fail if required test result != Passed
```

Do not use substring CRUU10 section IDs as the only sentinel.

Require exact names.

Example:

```powershell
$required = @(
  "CRUU10_001_Access_denied_directory_is_not_Missing",
  "CRUU10_002_Primary_missing_body_does_not_overwrite_complete_backup",
  "CRUU10_004_Edit_crash_after_new_body_before_metadata_restores_old_body",
  "CRUU10_007_Prompts_directory_cannot_be_replaced_while_session_lease_held",
  "CRUU9_001_Empty_prompts_junction_outside_target_is_rejected",
  "CRUU9_020_Restart_message_failure_still_requests_shutdown"
)
```

---

# 31. Weak-model "do not solve it this way" list

## 31.1 Do not

```text
replace Directory.Exists with File.Exists
```

Both are unsuitable as strict authority.

## 31.2 Do not

```text
call missing prompt body "corrupt metadata"
```

Keep metadata and package states distinct.

## 31.3 Do not

```text
sync backup from primary before package health
```

That is the exact CRUU10-002 defect.

## 31.4 Do not

```text
fix Create/Edit crash safety with more catch blocks
```

Power loss does not run catch.

## 31.5 Do not

```text
delete every orphan prompt on startup
```

Future/unreadable backup may still refer to it.

## 31.6 Do not

```text
validate prompts directory once and assume it remains safe
```

Hold a no-delete-share directory handle for process lifetime.

## 31.7 Do not

```text
remove all empty prompts/recovery dirs during migration recovery
```

Preserve pre-existing baseline dirs.

## 31.8 Do not

```text
turn JsonSerializer options stricter and assume duplicate keys are solved
```

Explicitly enumerate raw properties case-insensitively.

## 31.9 Do not

```text
catch Exception to keep UI alive
```

Catch expected recoverable filesystem classes only.

## 31.10 Do not

```text
set HeadlineTextBox.MaxLength=160
```

Domain uses Unicode text elements.

## 31.11 Do not

```text
reserve control names by basename at every nesting depth
```

Control authority is exact root-relative path.

## 31.12 Do not

```text
mark CRUU10 fixed because old tests pass
```

New crash/package tests are required.

---

# 32. Recommended production file map

New or significantly refactored:

```text
src/PromptHelper/Services/
    StrictPathAuthority.cs
    IStrictDirectoryOpener.cs
    WindowsStrictDirectoryOpener.cs
    ManagedTreeTopologyValidator.cs
    ManagedDataRootSessionLease.cs
    IManagedDirectoryHandleApi.cs
    WindowsManagedDirectoryHandleApi.cs

    IDurableAtomicFileWriter.cs
    WindowsDurableAtomicFileWriter.cs
    WindowsDurableFilePromoter.cs
    DurableTempName.cs
    DurableTempReconciler.cs

    LibraryPackageInspector.cs
    LibraryPackageState.cs

    LibraryMutationJournal.cs
    LibraryMutationJournalRepository.cs
    LibraryMutationJournalFactory.cs
    LibraryMutationRecoveryService.cs
    PromptMutationCoordinator.cs

    PromptOrphanReconciler.cs

    MigrationAttemptManifest.cs
    MigrationManifestBuilder.cs
    MigrationManifestRepository.cs
    MigrationRecoveryService.cs
    MigrationReadyGate.cs
    RecoveryBaselineVerifier.cs

    StrictJsonObjectAuthority.cs

    TargetRootReservation.cs
    DataRootRuntimeContext.cs
```

Modified:

```text
App.xaml.cs
MainWindow.xaml.cs
Views/SettingsDialog.xaml.cs
Services/AppPaths.cs
Services/AppSettingsRepository.cs
Services/LibraryRepository.cs
Services/LibraryStartupService.cs
Services/PromptRepository.cs
Services/PromptLibraryService.cs
Services/LibraryValidator.cs
Services/DataFolderMigrationService.cs
Services/DataFolderTransitionCoordinator.cs
Services/DataRootTopologyValidator.cs
Services/WindowsPhysicalPathResolver.cs
Services/WindowsDirectoryCaseSensitivityInspector.cs
Services/IDirectoryCaseSensitivityInspector.cs
```

---

# 33. Recommended test file map

```text
tests/PromptHelper.Tests/
    Cruu10StrictPathAuthorityTests.cs
    Cruu10ManagedTreeTests.cs
    Cruu10PackageIntegrityTests.cs
    Cruu10MutationJournalTests.cs
    Cruu10MutationRecoveryTests.cs
    Cruu10OrphanReconcilerTests.cs
    Cruu10DurableWriterTests.cs
    Cruu10JsonAuthorityTests.cs
    Cruu10UiBoundaryTests.cs
    Cruu10EvidenceTests.cs
    WindowsCruu10ManagedTreeIntegrationTests.cs
```

Do not keep adding hundreds of lines to one "ComprehensiveVerificationTests"
file.

Focused files make failures diagnosable.

---

# 34. Acceptance commands

Run on Windows.

## 34.1 Clean source status

```powershell
git status --short
git rev-parse HEAD
```

## 34.2 Restore

```powershell
dotnet restore PromptHelper.slnx
```

## 34.3 Build

```powershell
dotnet build PromptHelper.slnx `
  -c Release `
  --no-restore
```

Required:

```text
0 errors
0 warnings
```

## 34.4 Focused categories

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --filter "TestCategory=FilesystemAuthority" `
  --logger "trx;LogFileName=cruu10-authority.trx"
```

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --filter "TestCategory=PackageIntegrity" `
  --logger "trx;LogFileName=cruu10-package.trx"
```

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --filter "TestCategory=MutationRecovery" `
  --logger "trx;LogFileName=cruu10-mutation.trx"
```

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --filter "TestCategory=CrashRecovery" `
  --logger "trx;LogFileName=cruu10-crash.trx"
```

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --filter "TestCategory=WindowsFilesystemIntegration" `
  --logger "trx;LogFileName=cruu10-winfs.trx"
```

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --filter "TestCategory=WpfIntegration" `
  --logger "trx;LogFileName=cruu10-wpf.trx"
```

## 34.5 Full suite

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --logger "trx;LogFileName=cruu10-full.trx"
```

## 34.6 Five consecutive full suites

```powershell
1..5 | ForEach-Object {
    Write-Host "=== CRUU10 RUN $_ / 5 ==="

    dotnet test PromptHelper.slnx `
      -c Release `
      --no-build `
      --logger "trx;LogFileName=cruu10-full-$_.trx"

    if ($LASTEXITCODE -ne 0) {
        throw "CRUU10 full-suite run $_ failed."
    }
}
```

## 34.7 Publish

```powershell
Remove-Item `
  -Recurse `
  -Force `
  artifacts\publish-check `
  -ErrorAction SilentlyContinue

dotnet publish `
  src/PromptHelper/PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/publish-check
```

---

# 35. Final source grep gate

Review every hit:

```powershell
git grep -n "File.Exists" -- src/PromptHelper
git grep -n "Directory.Exists" -- src/PromptHelper
git grep -n "\.Exists" -- src/PromptHelper/Services/WindowsPhysicalPathResolver.cs
git grep -n "catch (Exception" -- src/PromptHelper
git grep -n "declaredTempMap"
git grep -n "IsCaseSensitive"
git grep -n "Assert.Throws<Exception>" -- tests
git grep -n "Write-Warning" -- tools/VerifyTestEvidence.ps1
```

Not every `File.Exists`/`Directory.Exists` is forbidden.

But every hit inside:

```text
authority classification
recovery ownership
safety topology
transaction decisions
```

must be replaced with strict state semantics.

---

# 36. Per-new-finding acceptance checklist

## CRUU10-001

```text
[ ] no boolean Directory.Exists authority classification
[ ] access-denied directory fails closed
[ ] physical resolver distinguishes missing vs unreadable
```

## CRUU10-002

```text
[ ] active bodies verified before backup sync
[ ] incomplete primary never overwrites complete backup
[ ] package state distinct from metadata state
```

## CRUU10-003

```text
[ ] backup bodies verified before promotion
[ ] incomplete backup never reported successful recovery
```

## CRUU10-004

```text
[ ] durable mutation journal exists
[ ] Create crash recovered
[ ] Edit crash recovered
[ ] Duplicate uses same Create transaction
[ ] unexpected hash fails closed
```

## CRUU10-005

```text
[ ] safe orphan reconciler exists
[ ] current/future/unreadable backup authority considered
[ ] no unproven orphan deletion
[ ] stale safely-deletable orphans eventually removed
```

## CRUU10-006

```text
[ ] user-data durable writer uses Flush(true)
[ ] final promotion uses write-through
[ ] strict stale temp grammar
[ ] foreign .tmp files not wildcard-deleted
```

## CRUU10-007

```text
[ ] long-lived root/prompts/recovery directory lease
[ ] no FILE_SHARE_DELETE
[ ] held until App.OnExit
[ ] real Windows node-swap test
```

## CRUU10-008

```text
[ ] migration manifest stores preexisting directory baseline
[ ] retry deletes only attempt-created dirs
```

## CRUU10-009

```text
[ ] all settings/library critical members duplicate-checked
[ ] unknown members rejected
[ ] invalid UTF-8 rejected
```

## CRUU10-010

```text
[ ] GetPrompts no broad Exception catch
[ ] expected IO still becomes unavailable UI
[ ] programmer fault propagates
```

## CRUU10-011

```text
[ ] 160 text-element headline domain cap
[ ] Unicode grapheme tests
[ ] no misleading XAML UTF-16 MaxLength substitute
```

## CRUU10-012

```text
[ ] root control names are exact relative paths
[ ] nested similarly named user/recovery data handled consistently
```

## CRUU10-013

```text
[ ] restart-style CRUD crash fixtures exist
[ ] not only in-process catch tests
```

## CRUU10-014

```text
[ ] incomplete primary/backup startup matrix tested
```

## CRUU10-015

```text
[ ] boolean IsCaseSensitive API removed
[ ] Inspect only
```

---

# 37. Cumulative definition of done

CRUU10 can be accepted at product/code level only when:

```text
ALL CRUU9-001..024 fixed
ALL CRUU10-001..015 fixed
CRUU9-025 explicitly remains BLOCKED only for absent approved logo
```

Additionally:

```text
[ ] Restore passes
[ ] Release build passes 0 warnings/errors
[ ] FilesystemAuthority category passes
[ ] PackageIntegrity category passes
[ ] MutationRecovery category passes
[ ] CrashRecovery category passes
[ ] WpfIntegration category passes
[ ] Real WindowsFilesystemIntegration category passes
[ ] Evidence verifier proves exact sentinels executed
[ ] Full suite passes
[ ] Full suite passes five consecutive runs
[ ] win-x64 self-contained publish passes
[ ] published payload contains EXE/LICENSE/THIRD_PARTY_NOTICES
[ ] no skipped mandatory test
[ ] no broad safety test `Assert.Throws<Exception>`
```

Strict release additionally needs:

```text
approved PromptHelperLogo.svg
generated exact ICO
SVG -> ICO normalized identity
ICO -> EXE embedded identity
manual Explorer/taskbar/Alt+Tab/window icon check
```

---

# 38. Final weak-AI implementation prompt

Copy this prompt to the weak implementing model **together with `cruu10.md`**.

```text
ROLE
You are a weak implementation model working on Prompt Helper.

You are not allowed to redesign the architecture.
CRUU10 is the implementation authority.

AUDITED BASELINE
be1da4fa49916a102616f82a6c74f5601ab5d2d6

IMPORTANT BASELINE FACT
CRUU9 was not implemented on pushed main at the time CRUU10 was created.
Therefore CRUU9-001 through CRUU9-025 are still mandatory.

GOAL
Implement every fix in:
- CRUU9-001 through CRUU9-024
- CRUU10-001 through CRUU10-015

CRUU9-025 remains BLOCKED only if the approved real logo is still absent.

DO NOT RESET NEWER WORK
If repository HEAD is newer:
- do not reset;
- compare newer changes against CRUU10;
- preserve equivalent or stronger fixes;
- implement only missing behavior.

MANDATORY PHASE ORDER
00 baseline
01 strict file authority
02 strict directory authority
03 physical managed-tree validation
04 long-lived managed-tree lease
05 migration manifest v3 / CRUU9 ownership fixes
06 durable atomic user-data writer
07 durable settings pointer / settings temp recovery
08 library package inspector
09 startup complete-package authority
10 CRUD mutation journal
11 delete/orphan reconciliation
12 recovery baseline directory ownership
13 strict JSON member authority
14 exception/title/namespace hardening
15 postcommit shutdown
16 tests
17 CI evidence
18 release identity tooling
19 full regression/publish/final audit

NON-NEGOTIABLE FILESYSTEM RULES
- File.Exists is not authority for migration/control state.
- Directory.Exists is not authority for safety state.
- Missing, Present, and Unreadable are distinct.
- prompts/recovery may not be reparse points.
- physical tree identity must remain fixed for the running session.
- hold managed-directory handles without FILE_SHARE_DELETE.

NON-NEGOTIABLE PACKAGE RULES
- Parsed library.json does NOT imply healthy library package.
- verify every active prompt body before synchronizing backup.
- verify every backup-referenced body before backup recovery.
- incomplete backup is not successful recovery.

NON-NEGOTIABLE CRUD RULES
- try/catch rollback is not crash recovery.
- Create/Edit/Duplicate/Delete crossing body+metadata require durable mutation authority.
- journal must exist before the first cross-file mutation.
- Edit must durably preserve old body before overwriting it.
- on restart, compare exact old/new body and metadata hashes.
- unexpected hashes fail closed.

NON-NEGOTIABLE ORPHAN RULES
- never wildcard-delete orphan prompts.
- current primary and current backup must both prove a GUID is unreferenced.
- future/unreadable backup preserves orphan.
- active transaction journal preserves its prompt body.

NON-NEGOTIABLE DURABILITY RULES
- temp content Flush(true).
- final authoritative promotion uses write-through.
- target payload durable before Ready.
- Ready durable before settings.
- settings durable before migration marker retirement.
- library mutation journal phase durable before moving to next cross-file authority.

NON-NEGOTIABLE JSON RULES
- reject duplicate critical properties case-insensitively.
- reject unknown settings/library properties.
- reject invalid UTF-8.
- do not silently normalize ambiguous metadata.

NON-NEGOTIABLE TEST RULES
- do not weaken tests.
- no Assert.Throws<Exception> for safety tests.
- construct on-disk crash fixtures and instantiate fresh recovery services.
- WindowsFilesystemIntegration must use real Windows filesystem primitives.
- required TRX sentinel absence is a CI failure.
- run full suite five consecutive times.

RELEASE
Do not fabricate PromptHelperLogo.svg.
If approved logo is absent:
  product/code may pass;
  strict release remains BLOCKED.

FINAL EVIDENCE
Return:
- starting HEAD
- final HEAD
- every CRUU9 finding status
- every CRUU10 finding status
- exact files changed
- exact focused test command/count
- real Windows integration evidence
- full suite result
- five run results
- publish result
- icon status
- remaining blockers

Never state PASS for a command you did not actually execute.
```

---

# 39. Auditor's final rule

Before every durable transition ask:

```text
"If the process dies immediately after this line, can the next startup
determine exactly which state is authoritative without guessing and
without deleting unproven user data?"
```

If the answer is not an unambiguous **yes**, the transition is incomplete.

Before every path operation ask:

```text
"Have I proven this is the same physical managed tree I intended to use,
and have I distinguished inaccessible from absent?"
```

Before every backup write ask:

```text
"Have I proven the source package is complete enough to deserve replacing
the safety copy?"
```

Those three questions are the core of CRUU10.

---

# PART II — FULL CRUU9 CARRY-FORWARD SPECIFICATION

The following prior audit is embedded verbatim so the weak implementing AI
does not need to locate a second file and cannot accidentally omit an
unfixed CRUU9 requirement.


# CRUU9 — Final-Authority, Managed-Tree, Crash-Durability & Evidence Audit

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited commit:** `be1da4fa49916a102616f82a6c74f5601ab5d2d6`  
**Previous audit chain:** `cruu1.md` → `cruu2.md` → `cruu3.md` → `cruu4.md` → `cruu5.md` → `cruu6.md` → `cruu7.md` → `cruu8.md` / `cruu8_v2.md`  
**Purpose:** independently test the CRUU8 implementation and provide a deterministic, weak-model-ready repair authority for every remaining issue.

---

# 1. Executive verdict

CRUU8 materially improved the codebase. The current commit contains real implementations for:

```text
migration manifest schema v2
predeclared payload temp paths
write-through payload promotion
write-through manifest promotion
Empty vs OccupiedNonLibrary target classification
unified retry/startup migration recovery
recursive prompts/recovery inventory
handle-bound verified final-artifact deletion
fail-closed case-sensitivity query
full target parent-chain reservation tracking
role-aware existing-target capability checks
shared library compatibility inspection
SettingsDialog postcommit RestartRequired protection
TRX evidence parser
stronger release-asset structure checking
```

The remaining problems now sit **below those abstractions**. The root may be physically safe while a child junction escapes it. A manifest may be structurally valid while its temp path points at unrelated data. A file may be present but `File.Exists` can report `false` because it is unreadable. Target payload and Ready marker may be write-through durable while the authoritative settings pointer still uses an ordinary rename.

Current audit status:

```text
CRUU8 STRUCTURAL IMPLEMENTATION          = SUBSTANTIALLY LANDED
AUDITED COMMIT                           = be1da4fa49916a102616f82a6c74f5601ab5d2d6
CRUU9 SOURCE-LEVEL AUDIT                 = COMPLETE
CRUU9 FINDINGS                           = OPEN
INDEPENDENT WINDOWS/.NET EXECUTION       = NOT AVAILABLE IN THIS AUDIT ENVIRONMENT
GITHUB COMBINED STATUS ENTRIES           = NONE RETURNED BY CONNECTOR
PR-TRIGGERED WORKFLOW EVIDENCE           = NONE RETURNED BY CONNECTOR
ZERO-DEFECT ACCEPTANCE                   = NOT GRANTED
STRICT RELEASE                           = BLOCKED
```

The connector evidence above does **not** prove that push CI did not run. It only means this audit did not receive usable direct CI evidence from the available endpoints.

---

# 2. Platform facts that control the repair

Microsoft documents that `File.Exists(path)` returns `false` rather than throwing when the caller lacks sufficient permission. Therefore `File.Exists == false` is not proof of absence for a migration authority file.

Reference:

```text
https://learn.microsoft.com/dotnet/api/system.io.file.exists
```

Microsoft documents `MOVEFILE_WRITE_THROUGH` as delaying return until the move is completed on disk. This is already used for migrated payload and manifest promotion, but the authoritative settings pointer still uses `File.Move` / `File.Replace` through `AtomicTextWriter`.

Reference:

```text
https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-movefileexw
```

`REPLACEFILE_WRITE_THROUGH` is documented as unsupported, so the settings fix should not attempt to add that flag to `ReplaceFileW`. Use a `MoveFileExW` same-volume replacement with `MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH` after flushing the staging file.

---

# 3. CRUU9 finding register

| ID | Severity | Finding |
|---|---|---|
| CRUU9-001 | CRITICAL/HIGH | Managed child directories (`prompts`, `recovery`) are not physically bound; a pre-existing junction/symlink can redirect migration writes and recovery deletion outside the bound target |
| CRUU9-002 | HIGH | `File.Exists` fail-open semantics are still used for migration markers, finals, temps and verified deletion, collapsing inaccessible authority files into “missing” |
| CRUU9-003 | HIGH | `TempRelativePath` is not constrained to the final file’s directory/name/AttemptId grammar; retry recovery deletes declared temps without hash verification |
| CRUU9-004 | HIGH | Manifest final and temp path uniqueness is checked in separate sets; a temp path can collide with another artifact’s final path |
| CRUU9-005 | MEDIUM-HIGH | Retry recovery validates target identity but not source identity; an interrupted attempt from another source can be auto-cleaned |
| CRUU9-006 | HIGH | Capability-probe directories/files are not durably represented by the migration manifest; crash during probe leaves unknown residue that cannot auto-recover |
| CRUU9-007 | HIGH | Migration-manifest staging temp is not owned by durable authority; crash during first publication or Ready replacement can strand blocking unknown residue |
| CRUU9-008 | HIGH | `settings.json` final pointer update is not write-through, so target/Ready durability is stronger than authoritative settings-pointer durability |
| CRUU9-009 | MEDIUM-HIGH | Settings atomic-writer temp files are not recognized/recoverable bootstrap control files and can block default-root recovery |
| CRUU9-010 | MEDIUM-HIGH | Retry recovery’s post-clean check tests remaining finals only; it does not reject new unknown entries or remaining temps/controls before deleting marker |
| CRUU9-011 | MEDIUM | Recovery converts some failures to `RecoveryResult` but lets temp deletion, directory cleanup, inventory and marker-delete exceptions escape raw |
| CRUU9-012 | MEDIUM | Successful migration to a newly-created target can report false cleanup failures because reservation `Release()` still attempts to delete the committed directory chain |
| CRUU9-013 | MEDIUM | Reservation ownership uses check-then-recursive-create and acquisition cleanup failures are swallowed; an externally-created directory can be misclassified as attempt-owned |
| CRUU9-014 | MEDIUM-HIGH | A stale root `.app.lock` left by a crash before manifest creation makes an otherwise empty target `OccupiedNonLibrary` and blocks retry |
| CRUU9-015 | MEDIUM | Bootstrap identity is threaded inconsistently: target is physical while several comparisons/recovery contexts use lexical bootstrap path; redundant `TargetIsBootstrapRoot` can disagree |
| CRUU9-016 | MEDIUM | Recovery ignores `.prompthelper-migration.json` and `.app.lock` by filename at every recursion depth, so nested foreign control-name files are silently ignored |
| CRUU9-017 | MEDIUM | `ReadyToCommit` is written without an explicit terminal invariant that all temps/ephemeral controls are absent and finals/tree are still stable |
| CRUU9-018 | MEDIUM | `CopySnapshotToTarget` still permits `declaredTempMap = null` and silently generates unmanifested temps; the internal helper uses the escape hatch |
| CRUU9-019 | LOW-MEDIUM | `ManifestWriteCleanupException` masks the original manifest write/promotion failure when staging-temp cleanup also fails |
| CRUU9-020 | MEDIUM | MainWindow still conditions forced shutdown on `DialogResult == true` and shows a MessageBox before requesting shutdown; process-boundary authority is not unconditional |
| CRUU9-021 | MEDIUM verification gap | Several CRUU8 tests do not prove their names: marker preservation, cleanup aggregation, real Windows case sensitivity, exhaustive dispatch, evidence-script execution and temp grammar |
| CRUU9-022 | MEDIUM verification gap | Windows CI does not separately execute mandatory categories and never invokes `VerifyTestEvidence.ps1`; missing sentinels are only warnings in the script |
| CRUU9-023 | MEDIUM release gap | Exact current SVG → committed ICO → published EXE icon identity is still not verified |
| CRUU9-024 | LOW-MEDIUM authority hardening | Migration JSON rejects duplicate `schemaVersion` only; duplicate critical properties and malformed UTF-8 are not strictly rejected before deserialization |
| CRUU9-025 | RELEASE BLOCKER | Approved `PromptHelperLogo.svg` / generated real ICO are still absent |

---

# 4. Final architecture invariants

The weak implementation model must not treat CRUU9 as 25 independent patches. The final design must satisfy these invariants:

```text
A — PHYSICAL TREE CONTAINMENT
Every managed operation remains inside the bound physical data root.
Managed child reparse points are rejected.

B — STRICT AUTHORITY PRESENCE
Missing, Present and Unreadable are different states.
File.Exists is not used to decide authority/control absence.

C — COMPLETE OWNERSHIP
Every precommit created path that can survive a crash is either declared
payload, declared payload temp, declared control, or a strict reserved
control path with deterministic recovery semantics.

D — MONOTONIC DURABILITY
Copying marker durable before payload.
Payload durable before Ready.
Ready durable before settings.
Settings pointer durable before marker retirement.

E — RECOVERY IDENTITY
Retry requires both target identity and source identity.
Unknown content is never auto-deleted.

F — MONOTONIC COMMIT
After durable settings commit, no path reports “not committed”, no target
rollback occurs, and shutdown is mandatory.

G — EXECUTED EVIDENCE
Mandatory test categories and exact sentinel names must actually appear as
passed in TRX; a script merely existing is not evidence.
```

---

# 5. Mandatory implementation phase order

```text
PHASE 0   Freeze baseline + source map
PHASE 1   Strict file authority operations                  (002)
PHASE 2   Managed physical tree containment                 (001)
PHASE 3   Manifest schema v3 authority                      (003,004,005,024)
PHASE 4   Migration control ownership + manifest staging    (006,007)
PHASE 5   Crash-durable settings pointer + temp controls    (008,009)
PHASE 6   Recovery terminal-state rewrite                   (010,011,014,015,016,017)
PHASE 7   Reservation ownership/commit semantics            (012,013)
PHASE 8   Remove unmanifested escape hatches                (018,019)
PHASE 9   Unconditional postcommit process boundary         (020)
PHASE 10  Replace weak tests + enforce CI evidence          (021,022)
PHASE 11  Exact release identity verifier                   (023)
PHASE 12  Full build/test/stress/publish
PHASE 13  Final source audit
```

CRUU9-025 remains blocked until the real approved artwork is supplied.

---

# 6. PHASE 0 — baseline instructions

Run and record:

```powershell
git status --short
git branch --show-current
git rev-parse HEAD
dotnet --info
pwsh --version
```

Audited baseline:

```text
be1da4fa49916a102616f82a6c74f5601ab5d2d6
```

If HEAD is newer, **do not reset or revert**. Compare and preserve equivalent/stronger newer fixes.

Open before editing:

```text
src/PromptHelper/App.xaml.cs
src/PromptHelper/MainWindow.xaml.cs
src/PromptHelper/Services/AppSettingsRepository.cs
src/PromptHelper/Services/AtomicTextWriter.cs
src/PromptHelper/Services/DataFolderMigrationService.cs
src/PromptHelper/Services/DataFolderTransitionCoordinator.cs
src/PromptHelper/Services/DataRootCapabilityValidator.cs
src/PromptHelper/Services/DataRootTopologyValidator.cs
src/PromptHelper/Services/EmptyTargetBaselineInspector.cs
src/PromptHelper/Services/IMigrationFileOps.cs
src/PromptHelper/Services/IMigrationManifestFileOps.cs
src/PromptHelper/Services/IVerifiedArtifactDeleter.cs
src/PromptHelper/Services/MigrationAttemptManifest.cs
src/PromptHelper/Services/MigrationManifestRepository.cs
src/PromptHelper/Services/MigrationRecoveryContext.cs
src/PromptHelper/Services/MigrationRecoveryService.cs
src/PromptHelper/Services/TargetRootReservation.cs
src/PromptHelper/Services/WindowsDirectoryCaseSensitivityInspector.cs
tests/PromptHelper.Tests/Cruu8ComprehensiveVerificationTests.cs
tools/VerifyTestEvidence.ps1
tools/VerifyReleaseAssets.ps1
.github/workflows/windows-ci.yml
```

---

# 7. CRUU9-001 — Managed child reparse escape

**Severity:** CRITICAL/HIGH

The root is physically bound, but later code performs path-based operations below it:

```text
<physicalRoot>\prompts\<id>.md
<physicalRoot>\recovery\<file>
```

`EmptyTargetBaselineInspector` currently permits pre-existing empty `prompts` and `recovery` directories. It does not reject a junction/symlink. An empty `prompts` junction to `D:\Outside` can therefore be classified as Empty, after which migration writes outside the bound target. Recovery can later delete through the same child redirect.

## Required policy

Inside the bound data root, Prompt Helper managed directories must be ordinary directories, not junctions/symlinks/mount points.

Managed directories:

```text
prompts
recovery
```

## Create `ManagedTreeTopologyValidator.cs`

```csharp
using System;
using System.IO;

namespace PromptHelper.Services;

internal sealed class ManagedTreeTopologyValidator
{
    private readonly IPhysicalPathResolver _resolver;

    public ManagedTreeTopologyValidator(
        IPhysicalPathResolver? resolver = null)
    {
        _resolver = resolver ?? new WindowsPhysicalPathResolver();
    }

    public void ValidateManagedTree(string physicalRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);

        string root = PathIdentity.NormalizeForComparison(physicalRoot);
        ValidateManagedDirectory(root, "prompts");
        ValidateManagedDirectory(root, "recovery");
    }

    public void ValidateManagedDirectory(
        string physicalRoot,
        string childName)
    {
        string child = Path.Combine(physicalRoot, childName);
        if (!Directory.Exists(child))
        {
            return;
        }

        FileAttributes attrs = File.GetAttributes(child);
        if ((attrs & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Prompt Helper managed directory '{child}' is a reparse point. " +
                "Managed data directories may not be junctions or symbolic links.");
        }

        string actual = DataRootTopologyValidator.ResolvePhysicalOrThrow(
            _resolver,
            child,
            $"managed '{childName}' directory");

        string expected = PathIdentity.NormalizeForComparison(
            Path.Combine(physicalRoot, childName));

        if (!PathIdentity.Equals(actual, expected))
        {
            throw new InvalidDataException(
                $"Managed directory '{child}' resolves outside its expected " +
                $"physical location. Expected '{expected}', resolved '{actual}'.");
        }
    }
}
```

Keep **both** the reparse attribute test and physical equality test.

## Mandatory call sites

```text
App startup after physical root + app lock, before migration recovery/library load
source snapshot before reading prompts/recovery
empty target after reservation before payload copy
existing target before accepting fingerprint
retry recovery before deletion
committed startup finalization before reads/hashes
```

## Stronger recommended lease

Add `ManagedTreeLease` that opens root/prompts/recovery directory handles with no `FILE_SHARE_DELETE` during migration/recovery. This makes later directory-node swapping harder and turns the previous repeated-check model into a bounded tree lease.

## Verified deletion containment

Change API to include physical root:

```csharp
void VerifyAndDelete(
    string physicalRoot,
    string path,
    long expectedLength,
    string expectedSha256Hex);
```

After opening the file handle, resolve the handle’s final path and require it to remain inside `physicalRoot` before marking the same handle for deletion.

## Tests

```text
CRUU9_001_Empty_prompts_junction_outside_target_is_rejected
CRUU9_001_Empty_recovery_junction_outside_target_is_rejected
CRUU9_001_Migration_never_writes_through_prompts_junction
CRUU9_001_Retry_never_deletes_through_prompts_junction
CRUU9_001_Committed_startup_rejects_managed_child_reparse
CRUU9_001_Normal_managed_directories_are_accepted
```

The junction tests must be real Windows tests and assert the outside directory remains byte-for-byte unchanged.

---

# 8. CRUU9-002 — `File.Exists` is fail-open for authority

**Severity:** HIGH

Do not use `File.Exists` to decide whether a marker/final/temp/control is absent. On Windows/.NET, insufficient permission can produce `false`.

## Create `StrictFileAuthority.cs`

```csharp
using System.IO;

namespace PromptHelper.Services;

internal enum StrictFilePresence
{
    Missing,
    Present
}

internal static class StrictFileAuthority
{
    public static StrictFilePresence GetPresence(string path)
    {
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            return StrictFilePresence.Present;
        }
        catch (FileNotFoundException)
        {
            return StrictFilePresence.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return StrictFilePresence.Missing;
        }
    }

    public static byte[]? ReadOptionalBytes(string path)
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

    public static void DeleteIfPresentStrict(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
```

Do not catch `UnauthorizedAccessException` or generic `IOException` as Missing.

## Required refactors

- `MigrationManifestRepository.TryRead`: read first; only FileNotFound/DirectoryNotFound means null.
- `MigrationRecoveryService`: no `FileExists(markerPath)` early success gate.
- `App.xaml.cs`: call committed-startup finalization unconditionally; service itself decides whether marker is truly absent.
- verified artifact deleter: remove `File.Exists` pre-check; inspect native error from `CreateFileW`.
- target primary/backup state: attempt read; FileNotFound => Missing, access/sharing => Unreadable.

## Tests

```text
CRUU9_002_Unreadable_marker_is_not_treated_as_missing
CRUU9_002_Unreadable_final_is_not_treated_as_missing
CRUU9_002_Unreadable_temp_is_not_treated_as_missing
CRUU9_002_Verified_deleter_access_denied_is_not_noop
CRUU9_002_Unreadable_library_primary_is_Unreadable_not_Empty
CRUU9_002_App_startup_does_not_skip_unreadable_marker
```

---

# 9. CRUU9-003 — TempRelativePath can name unrelated target data

**Severity:** HIGH

Current schema-v2 validation proves only that a temp path is canonical, inside the lexical root and different from its own final. Retry recovery deletes it without hash verification.

Required temp grammar:

```text
final: library.json
temp:  .library.json.migration-<attemptIdN>-<32 hex chars>.tmp

final: prompts\abc.md
temp:  prompts\.abc.md.migration-<attemptIdN>-<32 hex chars>.tmp
```

`32 hex chars` = 128-bit nonce. Change production generation from `GetHexString(16)` to `GetHexString(32)`.

## Copy-ready validator

```csharp
private static void ValidateTempPath(
    Guid attemptId,
    string finalRelative,
    string tempRelative)
{
    string finalDir = Path.GetDirectoryName(finalRelative) ?? string.Empty;
    string tempDir = Path.GetDirectoryName(tempRelative) ?? string.Empty;

    if (!string.Equals(finalDir, tempDir, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException(
            "Migration temp must be in the same directory as its final artifact.");
    }

    string finalName = Path.GetFileName(finalRelative);
    string tempName = Path.GetFileName(tempRelative);
    string prefix = $".{finalName}.migration-{attemptId:N}-";
    const string suffix = ".tmp";

    if (!tempName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
        !tempName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException($"Invalid migration temp name '{tempRelative}'.");
    }

    string nonce = tempName.Substring(
        prefix.Length,
        tempName.Length - prefix.Length - suffix.Length);

    if (nonce.Length != 32 || nonce.Any(c => !Uri.IsHexDigit(c)))
    {
        throw new InvalidDataException(
            "Migration temp nonce must contain exactly 32 hexadecimal characters.");
    }
}
```

## Tests

```text
CRUU9_003_Temp_in_different_directory_rejected
CRUU9_003_Temp_without_attempt_id_rejected
CRUU9_003_Temp_with_other_attempt_id_rejected
CRUU9_003_Temp_with_short_nonce_rejected
CRUU9_003_Temp_with_nonhex_nonce_rejected
CRUU9_003_Production_temp_grammar_accepted
CRUU9_003_Arbitrary_prompt_cannot_be_declared_as_temp
```

---

# 10. CRUU9-004 — Cross-artifact final/temp collision

**Severity:** HIGH

Current code stores final paths and temp paths in separate HashSets. A temp for artifact A can therefore equal final for artifact B.

Recovery deletes temps first, so that collision can delete B’s final before verified-final handling.

Use one set:

```csharp
var allOwnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
{
    string finalFull = ResolveManifestArtifactPath(...);
    string tempFull = ResolveManifestArtifactPath(...);

    if (!allOwnedPaths.Add(finalFull))
    {
        throw new InvalidDataException(
            $"Duplicate owned migration path '{artifact.RelativePath}'.");
    }

    if (!allOwnedPaths.Add(tempFull))
    {
        throw new InvalidDataException(
            $"Migration temp/final path collision '{artifact.TempRelativePath}'.");
    }
}
```

Tests:

```text
CRUU9_004_Temp_A_equal_Final_B_rejected
CRUU9_004_Final_A_equal_Temp_B_rejected
CRUU9_004_Two_temps_equal_rejected
CRUU9_004_Two_finals_equal_rejected
```

---

# 11. CRUU9-005 — Retry recovery ignores source identity

**Severity:** MEDIUM-HIGH

Retry currently requires target equality but does not require the interrupted manifest’s `SourcePhysicalRoot` to equal the active source root.

Extend context:

```csharp
internal sealed record MigrationRecoveryContext(
    string TargetPhysicalRoot,
    string PhysicalBootstrapRoot,
    string? ExpectedSourcePhysicalRoot);
```

For retry:

```csharp
if (string.IsNullOrWhiteSpace(context.ExpectedSourcePhysicalRoot) ||
    !PathIdentity.Equals(
        context.ExpectedSourcePhysicalRoot,
        manifest.SourcePhysicalRoot))
{
    throw new MigrationRecoveryException(
        context.TargetPhysicalRoot,
        "ValidateSourceIdentity",
        new InvalidDataException(
            "The interrupted migration belongs to a different source library. " +
            "Prompt Helper will not delete it automatically."));
}
```

Committed startup does not need the original source to exist.

Tests:

```text
CRUU9_005_Same_source_interrupted_retry_can_clean
CRUU9_005_Different_source_interrupted_retry_fails_closed
CRUU9_005_Different_source_files_preserved_byte_exact
CRUU9_005_Committed_startup_does_not_require_source_to_exist
```

---

# 12. CRUU9-006 — Capability probe has no crash ownership

**Severity:** HIGH

The empty-migration capability probe creates random directories/files after the Copying manifest exists, but those control paths are not represented in the durable manifest. If the process dies during the probe, retry sees unknown content and cannot recover automatically.

## Required manifest schema v3

Bump only the migration-control manifest:

```csharp
public const int CurrentSchemaVersion = 3;
```

Do not change library/settings schemas.

Add:

```csharp
internal enum MigrationControlArtifactKind
{
    CapabilityProbeDirectory,
    CapabilityProbeFile,
    ManifestPhaseStaging
}

internal sealed class MigrationControlArtifact
{
    public string RelativePath { get; set; } = string.Empty;
    public MigrationControlArtifactKind Kind { get; set; }
}
```

Manifest:

```csharp
public List<MigrationControlArtifact> ControlArtifacts { get; set; } = [];
```

## Predeclare a probe plan before Copying marker publication

Create `MigrationCapabilityProbePlan.cs`:

```csharp
internal sealed record CapabilityProbeLocationPlan(
    string DirectoryRelativePath,
    string CurrentFileRelativePath,
    string ReplacementFileRelativePath);

internal sealed record MigrationCapabilityProbePlan(
    CapabilityProbeLocationPlan RootProbe,
    CapabilityProbeLocationPlan? PromptsProbe);
```

Use deterministic AttemptId-derived directories:

```text
.prompthelper-write-probe-<attemptIdN>-root
prompts\.prompthelper-write-probe-<attemptIdN>-prompts
```

Files:

```text
probe-current.txt
probe-replacement.tmp
```

Add every path to `ControlArtifacts` before the initial Copying manifest is made durable.

## Capability API

For empty migration, require the plan:

```csharp
internal CapabilityValidationResult ValidateWritable(
    string root,
    ICreatedPathJournal journal,
    ExistingLibraryCapabilityContext? existing,
    MigrationCapabilityProbePlan probePlan)
```

Do not generate another hidden Guid path internally.

Recovery owns and may remove only the exact manifest-declared probe controls.

Ready gate requires every ephemeral probe control absent.

Tests:

```text
CRUU9_006_Crash_after_probe_dir_creation_recovers
CRUU9_006_Crash_after_probe_current_creation_recovers
CRUU9_006_Crash_after_probe_replacement_creation_recovers
CRUU9_006_Other_attempt_probe_is_foreign
CRUU9_006_Ready_manifest_with_probe_residue_rejected
```

---

# 13. CRUU9-007 — Manifest staging temp is itself unowned

**Severity:** HIGH

Current `MigrationManifestRepository.WriteDurable` writes an unclaimed random staging temp before publishing/replacing `.prompthelper-migration.json`.

## First Copying publication

Do **not** use a staging temp.

Write the final marker path directly with `FileMode.CreateNew`, write complete JSON, `Flush(true)`, then return.

```csharp
public void CreateInitialCopyingManifestDurable(
    string markerPath,
    MigrationAttemptManifest manifest)
{
    if (manifest.Phase != MigrationManifestPhase.Copying)
    {
        throw new InvalidDataException("Initial manifest must be Copying.");
    }

    byte[] bytes = SerializeValidated(manifest);

    using Stream stream = _fileOps.CreateNew(markerPath);
    stream.Write(bytes, 0, bytes.Length);
    _fileOps.FlushToDisk(stream);
}
```

No payload creation is allowed before this method returns.

If the process dies while writing the initial final marker, no payload can exist yet. A partial/corrupt marker fails closed instead of creating unowned payload.

## Copying -> Ready replacement

Use deterministic stage:

```text
.prompthelper-migration.stage-<attemptIdN>.tmp
```

Declare it in `ControlArtifacts` as `ManifestPhaseStaging`.

Sequence:

```text
CreateNew exact stage
write JSON
Flush(true)
MoveFileExW(stage, marker, REPLACE_EXISTING | WRITE_THROUGH)
```

No random phase-stage filename.

Tests:

```text
CRUU9_007_Crash_during_initial_manifest_write_leaves_final_marker_path
CRUU9_007_Corrupt_initial_marker_blocks_without_payload_mutation
CRUU9_007_Crash_during_ready_stage_is_recoverable
CRUU9_007_Other_attempt_stage_is_foreign
```

---

# 14. CRUU9-008 — Settings pointer durability is weaker than target durability

**Severity:** HIGH

Current target protocol is stronger than the final settings pointer:

```text
payload temp Flush(true)
payload MoveFileEx WRITE_THROUGH
Ready manifest Flush(true) + write-through promotion
settings.json AtomicTextWriter -> File.Replace/File.Move
marker retirement
```

The protocol has not established that the settings pointer’s final-name update is durably ordered before marker retirement.

## Create `IDurableSettingsFileWriter.cs`

```csharp
internal interface IDurableSettingsFileWriter
{
    void WriteDurable(string targetPath, string content);
}
```

## Create `WindowsDurableSettingsFileWriter.cs`

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PromptHelper.Services;

internal sealed class WindowsDurableSettingsFileWriter
    : IDurableSettingsFileWriter
{
    private const uint MOVEFILE_REPLACE_EXISTING = 0x00000001;
    private const uint MOVEFILE_WRITE_THROUGH = 0x00000008;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileExW(
        string source,
        string destination,
        uint flags);

    public void WriteDurable(string targetPath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(content);

        string directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Settings path has no directory.");

        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(
            directory,
            $".prompthelper-settings-{Path.GetFileName(targetPath)}-{Guid.NewGuid():N}.tmp");

        bool promoted = false;
        Exception? primaryFailure = null;

        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (!MoveFileExW(
                    tempPath,
                    targetPath,
                    MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"Durable settings promotion failed for '{targetPath}'.");
            }

            promoted = true;
        }
        catch (Exception ex)
        {
            primaryFailure = ex;
            throw;
        }
        finally
        {
            if (!promoted)
            {
                // Cleanup through strict settings-temp cleanup helper.
                // If cleanup also fails, preserve both failures in a typed exception.
            }
        }
    }
}
```

Do not use `File.Exists(targetPath)` to decide replace behavior.

## AppSettingsRepository

Inject durable writer separately:

```csharp
private readonly IDurableSettingsFileWriter _durableSettingsWriter;
```

Authoritative primary:

```csharp
_durableSettingsWriter.WriteDurable(_settingsPath, json);
```

Prefer using the same durable writer for settings backup too, but backup failure remains warning-level according to current product semantics.

`SaveIfUnchanged` returns only after durable primary promotion.

Coordinator sets:

```csharp
settingsCommitted = true;
```

only after that return.

Tests must prove exact operation order:

```text
FlushSettingsTemp
MoveSettingsWriteThrough
SaveIfUnchangedReturn
MarkerRetire
```

Required tests:

```text
CRUU9_008_Settings_primary_uses_write_through_promotion
CRUU9_008_Settings_failure_before_durable_promotion_rolls_target_back
CRUU9_008_Marker_not_retired_before_durable_settings_return
```

---

# 15. CRUU9-009 — Settings temp residue blocks default-root recovery

**Severity:** MEDIUM-HIGH

Use reserved grammar from CRUU9-008:

```text
.prompthelper-settings-settings.json-<guidN>.tmp
.prompthelper-settings-settings.backup.json-<guidN>.tmp
```

Create strict parser `SettingsTempName` and cleanup stale recognized temps **only while holding `.settings.lock`**.

Call cleanup at the beginning of:

```text
LoadOrRecover
LoadForTransitionAndCapturePrecondition
Save
SaveIfUnchanged
```

Copy-ready shape:

```csharp
private void CleanupStaleSettingsTempsCore()
{
    string? directory = Path.GetDirectoryName(_settingsPath);
    if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
    {
        return;
    }

    foreach (string path in Directory.EnumerateFiles(
                 directory,
                 ".prompthelper-settings-*.tmp",
                 SearchOption.TopDirectoryOnly))
    {
        string name = Path.GetFileName(path);
        if (!SettingsTempName.TryParse(name, out _))
        {
            continue;
        }

        File.Delete(path);
    }
}
```

If a recognized stale settings temp cannot be deleted, stop. Do not migrate into exact bootstrap root while bootstrap control state is unresolved.

Tests:

```text
CRUU9_009_Stale_primary_settings_temp_cleaned_under_lease
CRUU9_009_Stale_backup_settings_temp_cleaned_under_lease
CRUU9_009_Invalid_similar_filename_not_deleted
CRUU9_009_Unremovable_settings_temp_blocks_default_root_transition
```

---

# 16. CRUU9-010 — Retry post-clean validation is incomplete

**Severity:** MEDIUM-HIGH

Current retry rebuilds inventory after cleanup but checks remaining finals only. Before deleting marker, it must require:

```text
no unknown entries
no final artifact
no payload temp
no ephemeral control artifact
only permitted persistent baseline/control remains
```

Create `RecoveryBaselineVerifier.cs`:

```csharp
internal static class RecoveryBaselineVerifier
{
    public static void AssertRestored(
        string targetRoot,
        TargetRecoveryInventory inventory,
        IAuthorityFileOps authorityOps)
    {
        if (inventory.HasUnknownEntries)
        {
            throw new InvalidDataException(
                "Recovery target contains unknown entries: " +
                string.Join(", ", inventory.UnknownEntries));
        }

        foreach (string rel in inventory.ManifestFinals)
        {
            AssertMissing(targetRoot, rel, authorityOps);
        }

        foreach (string rel in inventory.ManifestTemps)
        {
            AssertMissing(targetRoot, rel, authorityOps);
        }

        foreach (string rel in inventory.EphemeralControlArtifacts)
        {
            AssertMissing(targetRoot, rel, authorityOps);
        }
    }
}
```

Marker delete is allowed only after this gate.

Race test:

```text
initial inventory clean
cleanup
second inventory injects foreign.txt
=> marker remains, foreign preserved, recovery fails
```

---

# 17. CRUU9-011 — Recovery error contract is inconsistent

**Severity:** MEDIUM

Choose one contract. Recommended: typed exceptions.

Create:

```csharp
internal sealed class MigrationRecoveryException : IOException
{
    public string TargetRoot { get; }
    public string Operation { get; }

    public MigrationRecoveryException(
        string targetRoot,
        string operation,
        Exception inner)
        : base(
            $"Migration recovery failed during '{operation}' for '{targetRoot}': {inner.Message}",
            inner)
    {
        TargetRoot = targetRoot;
        Operation = operation;
    }
}
```

Refactor service to either:

```csharp
void RecoverForRetry(MigrationRecoveryContext context)
void FinalizeCommittedStartup(MigrationRecoveryContext context)
```

or wrap the **entire** method body into `RecoveryResult`. Do not mix raw throws with partial result handling.

Expected filesystem failures at:

```text
temp delete
verified final delete
directory cleanup
inventory
marker retirement
```

must have one stable error contract.

Coordinator catches that typed recovery failure after reservation, releases reservation, and aggregates cleanup failure if necessary.

---

# 18. CRUU9-012 — Reservation deletes committed directory ownership

**Severity:** MEDIUM

On successful new-target migration, `Release()` sees the intentionally non-empty target as an undeletable created directory and can generate false cleanup warnings.

Add:

```csharp
private bool _createdDirectoriesCommitted;

public void CommitRootOwnership()
{
    _createdDirectoriesCommitted = true;
}
```

Release:

```csharp
if (!_createdDirectoriesCommitted && _createdDirectories.Count > 0)
{
    CleanupCreatedDirectories(_createdDirectories, _fileOps, failures);
}
```

Coordinator after durable settings commit:

```csharp
settingsCommitted = true;
tx.Commit();
reservation.CommitRootOwnership();
```

Then release only lock/control state.

Tests:

```text
CRUU9_012_New_target_success_has_no_false_cleanup_warning
CRUU9_012_New_target_failure_removes_created_chain
CRUU9_012_Postcommit_release_preserves_new_target
```

---

# 19. CRUU9-013 — Reservation ownership race and swallowed acquire cleanup

**Severity:** MEDIUM

Do not infer ownership from:

```text
DirectoryExists=false
then recursive Directory.CreateDirectory(root)
```

Use native per-segment create result.

Extend file ops:

```csharp
internal enum DirectoryCreateOutcome
{
    CreatedByCaller,
    AlreadyExists
}

DirectoryCreateOutcome TryCreateDirectoryOwned(string path);
```

Windows implementation uses `CreateDirectoryW`:

```text
success -> CreatedByCaller
ERROR_ALREADY_EXISTS -> AlreadyExists
other -> throw
```

Create shallowest -> deepest and record only paths for which this process received success.

If lock acquisition then fails, cleanup only those exact paths and surface cleanup failures in a typed `TargetRootReservationAcquireException`.

Tests:

```text
CRUU9_013_Concurrent_parent_creation_not_marked_owned
CRUU9_013_Only_actually_created_directories_removed
CRUU9_013_Acquire_failure_reports_parent_cleanup_failure
```

---

# 20. CRUU9-014 — Stale root `.app.lock` blocks retry

**Severity:** MEDIUM-HIGH

`.app.lock` presence is not active lock ownership. It is normal for the lock file to survive after the process handle closes.

Change Empty target baseline:

```text
allow exact root-relative .app.lock always
```

Do not allow:

```text
prompts\.app.lock
recovery\.app.lock
```

Reservation is the authority:

```text
can acquire FileShare.None -> stale/unheld -> proceed
sharing/lock violation -> target in use -> block
```

Tests:

```text
CRUU9_014_Stale_root_app_lock_does_not_make_target_occupied
CRUU9_014_Held_root_app_lock_blocks_transition
CRUU9_014_Nested_app_lock_is_foreign
CRUU9_014_Crash_after_reservation_before_manifest_is_retryable
```

---

# 21. CRUU9-015 — Physical/lexical bootstrap mismatch

**Severity:** MEDIUM

Create `DataRootRuntimeContext`:

```csharp
internal sealed record DataRootRuntimeContext(
    string ActivePhysicalRoot,
    string BootstrapLexicalRoot,
    string BootstrapPhysicalRoot);
```

Resolve physical bootstrap once with the same physical resolver:

```csharp
string physicalBootstrap =
    DataRootTopologyValidator.ResolvePhysicalOrThrow(
        _physicalPathResolver,
        bootstrapRoot,
        "bootstrap settings folder");
```

Use `BootstrapPhysicalRoot` for:

```text
EmptyTargetBaselineInspector
MigrationRecoveryContext
exact-bootstrap comparison
settings/control baseline
```

Remove redundant persisted `TargetIsBootstrapRoot` from manifest v3. Derive truth from physical paths.

Test fake resolver:

```text
lexical bootstrap -> redirected physical bootstrap
```

and verify exact-default migration preserves settings controls.

---

# 22. CRUU9-016 — Nested control filenames are silently ignored

**Severity:** MEDIUM

Recovery currently ignores `.prompthelper-migration.json` and `.app.lock` by filename even during recursive scans. Only root-relative controls are controls.

Use:

```csharp
private static bool IsRootControlPath(string relativePath)
{
    string rel = NormalizeRel(relativePath);

    return rel.Equals(
               ".app.lock",
               StringComparison.OrdinalIgnoreCase) ||
           rel.Equals(
               ".prompthelper-migration.json",
               StringComparison.OrdinalIgnoreCase);
}
```

Do not use only `Path.GetFileName`.

Tests:

```text
CRUU9_016_Root_app_lock_is_control
CRUU9_016_Nested_app_lock_is_unknown
CRUU9_016_Root_manifest_is_control
CRUU9_016_Nested_manifest_named_file_is_unknown
```

---

# 23. CRUU9-017 — ReadyToCommit has no explicit terminal invariant gate

**Severity:** MEDIUM

Create `MigrationReadyGate.cs`.

It must verify immediately before the phase change:

```text
managed tree physically safe
all final payload files present and exact hash+length
all declared payload temps strictly absent
all ephemeral control paths absent
no unknown entries
source snapshot still valid if the design requires one last source proof
```

Suggested shape:

```csharp
internal sealed class MigrationReadyGate
{
    private readonly IAuthorityFileOps _authority;
    private readonly ManagedTreeTopologyValidator _tree;

    public void AssertReady(
        string physicalTargetRoot,
        MigrationAttemptManifest manifest,
        MigrationPayloadSnapshot snapshot)
    {
        _tree.ValidateManagedTree(physicalTargetRoot);

        foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
        {
            AssertFinalMatches(physicalTargetRoot, artifact);
            AssertTempMissing(physicalTargetRoot, artifact);
        }

        foreach (MigrationControlArtifact control in manifest.ControlArtifacts)
        {
            if (IsEphemeral(control))
            {
                AssertControlMissing(physicalTargetRoot, control);
            }
        }
    }
}
```

Only after this returns:

```csharp
manifest.Phase = MigrationManifestPhase.ReadyToCommit;
```

Tests inject a declared temp, probe residue, unknown file, changed final and child junction between copy and Ready gate.

---

# 24. CRUU9-018 — Unmanifested temp fallback remains

**Severity:** MEDIUM

Remove the current nullable fallback completely.

Required production signature:

```csharp
internal void CopySnapshotToTarget(
    string currentRoot,
    string targetRoot,
    MigrationPayloadSnapshot snapshot,
    MigrationAttemptManifest manifest,
    MigrationTargetTransaction tx)
```

For every snapshot file:

```text
find exactly one manifest artifact by RelativePath
require role/length/hash equal snapshot
resolve exact TempRelativePath
copy using that temp
```

No artifact -> throw before mutation.

Extra artifact -> throw before mutation.

No overload may generate a hidden random temp.

The internal `PrepareTargetForMigrationUnitTest` must either be removed or construct a real manifest through the same `MigrationManifestBuilder` used by production.

Tests:

```text
CRUU9_018_Copy_requires_manifest_artifact_for_every_snapshot_file
CRUU9_018_Extra_manifest_artifact_rejected
CRUU9_018_No_CopySnapshot_overload_has_nullable_temp_map
```

---

# 25. CRUU9-019 — Manifest cleanup exception masks primary failure

**Severity:** LOW-MEDIUM

Create exception that preserves both failures:

```csharp
internal sealed class ManifestWriteCleanupException : IOException
{
    public string MarkerPath { get; }
    public string TempPath { get; }
    public Exception OriginalFailure { get; }
    public Exception CleanupFailure { get; }

    public ManifestWriteCleanupException(
        string markerPath,
        string tempPath,
        Exception originalFailure,
        Exception cleanupFailure)
        : base(
            $"Migration manifest write failed for '{markerPath}', and " +
            $"staging cleanup also failed for '{tempPath}'.",
            originalFailure)
    {
        MarkerPath = markerPath;
        TempPath = tempPath;
        OriginalFailure = originalFailure;
        CleanupFailure = cleanupFailure;
    }
}
```

Do not throw a cleanup-only exception from `finally` that replaces the primary promotion failure.

Test exact `OriginalFailure` and `CleanupFailure` references.

---

# 26. CRUU9-020 — Forced shutdown still depends on DialogResult

**Severity:** MEDIUM

`RestartRequired` is the process-boundary authority. `DialogResult` is not.

Refactor MainWindow:

```csharp
internal void CompleteSettingsDialog(
    bool? dialogResult,
    bool restartRequired)
{
    if (!restartRequired)
    {
        return;
    }

    try
    {
        MessageBox.Show(
            this,
            "Data folder changed\n\nPrompt Helper must close now so " +
            "the previous data folder cannot be modified after the migration snapshot.\n\n" +
            "Open Prompt Helper again to use the selected data folder.",
            "Restart Required",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
    finally
    {
        _applicationLifetime.RequestShutdown();
    }
}
```

Production:

```csharp
bool? result = dialog.ShowDialog();
CompleteSettingsDialog(result, dialog.RestartRequired);
```

Do not require `result == true`.

Tests:

```text
CRUU9_020_RestartRequired_true_Result_true_shuts_down
CRUU9_020_RestartRequired_true_Result_null_shuts_down
CRUU9_020_RestartRequired_true_Result_false_shuts_down
CRUU9_020_RestartRequired_false_does_not_shutdown
CRUU9_020_Restart_message_failure_still_requests_shutdown
```

---

# 27. CRUU9-021 — CRUU8 tests overclaim behavior

**Severity:** MEDIUM verification gap

Replace weak evidence, do not merely add comments.

Current examples:

```text
CRUU8_001 only injects flush failure and Assert.Throws<Exception>; it does not assert marker preservation.
CRUU8_002 uses temp names that violate the intended AttemptId grammar.
CRUU8_008 manually constructs MigrationRollbackException instead of inducing coordinator cleanup failure.
CRUU8_010 is tagged WindowsFilesystemIntegration but uses a fake inspector.
CRUU8_017 checks enum count, not dispatch behavior.
CRUU8_019 checks script existence, not TRX parsing.
```

Rules:

```text
No Assert.Throws<Exception> in CRUU9 safety tests.
A test named WindowsFilesystemIntegration performs real Windows filesystem behavior.
A test named cleanup failure injects an actual cleanup failure.
A test named evidence parsing invokes the script against a fixture TRX.
```

Required replacements:

```text
CRUU9_021_Rollback_delete_failure_preserves_actual_marker
CRUU9_021_Coordinator_cleanup_failure_contains_exact_operation
CRUU9_021_Real_case_sensitive_NTFS_test_executes_native_inspector
CRUU9_021_All_target_kinds_execute_real_dispatch_path
CRUU9_021_VerifyTestEvidence_accepts_valid_fixture_TRX
CRUU9_021_VerifyTestEvidence_rejects_missing_sentinel_TRX
```

---

# 28. CRUU9-022 — CI/evidence enforcement is not wired

**Severity:** MEDIUM verification gap

`VerifyTestEvidence.ps1` currently emits `Write-Warning` for missing sentinels and still succeeds. CI does not call the script and does not run the mandatory categories separately.

## Fix evidence script

Required parameters:

```powershell
param(
    [Parameter(Mandatory=$true)]
    [string]$TrxPath,

    [string[]]$RequiredTests = @()
)
```

Missing required test:

```powershell
if ($missingRequired.Count -gt 0) {
    throw (
        "Required tests were not executed: " +
        ($missingRequired -join ", ")
    )
}
```

Require exact names and successful outcomes.

## CI required steps

```yaml
- name: Test Crash Recovery
  shell: pwsh
  run: |
    dotnet test PromptHelper.slnx `
      -c Release `
      --no-build `
      --filter "TestCategory=CrashRecovery" `
      --logger "trx;LogFileName=cruu9-crash.trx"

- name: Test WPF Integration
  shell: pwsh
  run: |
    dotnet test PromptHelper.slnx `
      -c Release `
      --no-build `
      --filter "TestCategory=WpfIntegration" `
      --logger "trx;LogFileName=cruu9-wpf.trx"

- name: Test Windows Filesystem Integration
  shell: pwsh
  run: |
    dotnet test PromptHelper.slnx `
      -c Release `
      --no-build `
      --filter "TestCategory=WindowsFilesystemIntegration" `
      --logger "trx;LogFileName=cruu9-winfs.trx"
```

Then invoke verifier against actual produced TRX paths. Confirm the real path in CI rather than guessing it.

Mandatory exact sentinels should include at least:

```text
CRUU9_001_Empty_prompts_junction_outside_target_is_rejected
CRUU9_002_Unreadable_marker_is_not_treated_as_missing
CRUU9_008_Settings_primary_uses_write_through_promotion
CRUU9_020_Restart_message_failure_still_requests_shutdown
```

---

# 29. CRUU9-023 — Exact release icon identity still absent

**Severity:** MEDIUM release gap

Current strict verifier proves ICO structure and that the EXE exposes at least one icon group. It does not prove identity.

Final strict flow:

```text
current approved SVG
  -> regenerate temporary expected ICO
  -> compare normalized RGBA pixel hashes per required size to committed ICO
  -> extract RT_GROUP_ICON / RT_ICON resources from published EXE
  -> compare normalized RGBA hashes to committed ICO
```

Required sizes:

```text
16
24
32
48
64
128
256
```

Do not rely only on raw ICO byte equality.

Create fixture A/B tests now, even before product logo exists:

```text
A vs A pass
A vs B fail
EXE(A) vs A pass
EXE(A) vs B fail
missing required size fail
```

CRUU9-025 remains separate.

---

# 30. CRUU9-024 — Strict manifest JSON authority

**Severity:** LOW-MEDIUM

Raw JSON validation currently rejects duplicate `schemaVersion` only. Reject duplicate case-insensitive known properties for every object.

Root properties:

```text
schemaVersion
attemptId
sourcePhysicalRoot
targetPhysicalRoot
sourceLibrarySha256Hex
phase
artifacts
controlArtifacts
```

Payload artifact:

```text
relativePath
tempRelativePath
sha256Hex
length
role
```

Control artifact:

```text
relativePath
kind
```

Recommended: reject unknown properties in migration control manifests.

Strict UTF-8:

```csharp
string json = new UTF8Encoding(
    encoderShouldEmitUTF8Identifier: false,
    throwOnInvalidBytes: true)
    .GetString(rawBytes);
```

Tests:

```text
CRUU9_024_Duplicate_targetPhysicalRoot_rejected
CRUU9_024_Duplicate_artifacts_case_variant_rejected
CRUU9_024_Unknown_root_property_rejected
CRUU9_024_Invalid_UTF8_manifest_rejected
```

---

# 31. CRUU9-025 — Real logo remains absent

**Severity:** RELEASE BLOCKER

At audited commit, `src/PromptHelper/Assets` is not present through the repository contents endpoint.

Do not fabricate a logo, copy a fixture into product assets, or weaken the release gate.

Correct status until approved artwork is supplied:

```text
PRODUCT/CODE MAY BECOME CLEAN
STRICT RELEASE = BLOCKED
```

---

# 32. Exact proposed manifest schema v3

```csharp
internal enum MigrationManifestPhase
{
    Copying,
    ReadyToCommit
}

internal enum MigrationControlArtifactKind
{
    CapabilityProbeDirectory,
    CapabilityProbeFile,
    ManifestPhaseStaging
}

internal sealed class MigrationAttemptManifest
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid AttemptId { get; set; }
    public string SourcePhysicalRoot { get; set; } = string.Empty;
    public string TargetPhysicalRoot { get; set; } = string.Empty;
    public string SourceLibrarySha256Hex { get; set; } = string.Empty;
    public MigrationManifestPhase Phase { get; set; }
    public List<MigrationManifestArtifact> Artifacts { get; set; } = [];
    public List<MigrationControlArtifact> ControlArtifacts { get; set; } = [];
}

internal sealed class MigrationManifestArtifact
{
    public string RelativePath { get; set; } = string.Empty;
    public string TempRelativePath { get; set; } = string.Empty;
    public string Sha256Hex { get; set; } = string.Empty;
    public long Length { get; set; }
    public MigrationPayloadRole Role { get; set; }
}

internal sealed class MigrationControlArtifact
{
    public string RelativePath { get; set; } = string.Empty;
    public MigrationControlArtifactKind Kind { get; set; }
}
```

Remove redundant `TargetIsBootstrapRoot` and derive bootstrap identity from physical runtime context.

---

# 33. Schema-v2 compatibility policy

Do not blindly delete v2 residues and do not simply reject every v2 marker if safe compatibility can be proven.

Recommended reader:

```text
read raw schemaVersion first
schema 3 -> strict v3 parser
schema 2 -> schema2 DTO + NEW strict temp grammar + cross-collision checks + source identity
other -> fail closed
```

A v2 marker from the current CRUU8 implementation may be recoverable if:

```text
every temp matches production grammar
every path is safe
source matches retry source
no unknown probe/control residue exists
```

If those cannot be proven, fail closed without deletion.

---

# 34. `MigrationManifestBuilder` — one authority for generated names

Create one builder. Coordinator must not hand-build path strings.

```csharp
internal static class MigrationManifestBuilder
{
    public static MigrationAttemptManifest BuildCopying(
        string sourcePhysicalRoot,
        string targetPhysicalRoot,
        MigrationPayloadSnapshot snapshot,
        Guid attemptId)
    {
        var artifacts = new List<MigrationManifestArtifact>();

        foreach (MigrationPayloadFile file in snapshot.Files)
        {
            string directory = Path.GetDirectoryName(file.RelativePath) ?? string.Empty;
            string finalName = Path.GetFileName(file.RelativePath);
            string nonce = RandomNumberGenerator.GetHexString(32).ToLowerInvariant();

            string tempName =
                $".{finalName}.migration-{attemptId:N}-{nonce}.tmp";

            string tempRelative = string.IsNullOrEmpty(directory)
                ? tempName
                : Path.Combine(directory, tempName);

            artifacts.Add(new MigrationManifestArtifact
            {
                RelativePath = file.RelativePath,
                TempRelativePath = tempRelative,
                Sha256Hex = Convert.ToHexStringLower(file.Sha256),
                Length = file.Length,
                Role = file.Role
            });
        }

        List<MigrationControlArtifact> controls = BuildControlArtifacts(attemptId, snapshot);

        return new MigrationAttemptManifest
        {
            AttemptId = attemptId,
            SourcePhysicalRoot = sourcePhysicalRoot,
            TargetPhysicalRoot = targetPhysicalRoot,
            SourceLibrarySha256Hex = artifacts.Single(
                x => x.Role == MigrationPayloadRole.PrimaryMetadata).Sha256Hex,
            Phase = MigrationManifestPhase.Copying,
            Artifacts = artifacts,
            ControlArtifacts = controls
        };
    }
}
```

---

# 35. Stronger recommended `ManagedTreeLease`

For the strongest practical Windows design, after validating the managed tree, hold directory handles during transition/recovery.

Open:

```text
physical root
prompts if present
recovery if present
```

with:

```text
FILE_FLAG_BACKUP_SEMANTICS
FILE_SHARE_READ | FILE_SHARE_WRITE
NO FILE_SHARE_DELETE
```

This prevents ordinary rename/delete/reparse replacement of those directory nodes while the operation is active.

Use during:

```text
empty-target migration
existing-target final inspection
retry recovery
committed startup finalization
```

If implementing this, add Windows race tests attempting junction replacement while the lease is held.

---

# 36. Final required empty-target sequence

```text
01 acquire settings transition snapshot + dual CAS precondition
02 resolve active physical root
03 resolve physical bootstrap root
04 bind target physical root
05 validate managed tree
06 strict target classification
07 capture complete source snapshot
08 allocate AttemptId
09 build manifest v3 + payload temps + control paths
10 reserve target root
11 acquire managed-tree lease
12 revalidate lexical locator -> same physical target
13 if interrupted: recover only if source identity matches
14 strict Empty baseline recheck
15 validate managed tree again
16 create initial Copying marker directly at final marker path + Flush(true)
17 copy every payload artifact using manifest-declared temp
18 Flush(true) each temp
19 write-through promote each final
20 verify source set + source hashes
21 verify target hashes
22 capability probe using manifest-declared controls
23 remove probe controls
24 MigrationReadyGate.AssertReady
25 write deterministic Ready staging control
26 Flush(true) stage + write-through replace marker to Ready
27 revalidate locator
28 validate managed tree
29 compare settings CAS
30 durable write-through settings primary
31 POINT OF NO RETURN
32 commit target transaction
33 reservation.CommitRootOwnership
34 retire Ready marker
35 release reservation
36 combine postcommit warnings
37 return Changed=true RestartRequired=true
38 SettingsDialog preserves RestartRequired
39 MainWindow shuts down regardless DialogResult
```

---

# 37. Final required retry sequence

```text
01 strict-read marker; unreadable != missing
02 parse strict v3 or strictly-safe v2
03 require target physical identity
04 require manifest source == active source physical identity
05 validate managed tree
06 acquire tree/recovery lease
07 recursive inventory
08 reject unknown content
09 delete exact declared controls
10 delete exact declared payload temps
11 verify+delete exact finals using same-handle deleter constrained to physical root
12 remove empty attempt directories
13 rebuild full inventory
14 RecoveryBaselineVerifier.AssertRestored
15 delete marker LAST
16 strictly prove marker missing
17 return success
```

Any failure:

```text
marker remains
foreign data remains untouched
source settings remain authoritative
```

---

# 38. Final required startup sequence

```text
01 acquire settings lease
02 clean strict stale settings temps
03 load/recover settings
04 resolve configured physical root
05 resolve physical bootstrap root
06 validate topology + case sensitivity
07 acquire .app.lock
08 validate managed tree
09 call committed migration finalization unconditionally
10 if marker truly missing -> continue
11 if Ready -> verify all finals, no temps/controls, no unknowns
12 require marker retirement success
13 ensure normal data directories
14 validate managed tree again
15 load library
16 show MainWindow
```

No writable UI before migration authority is resolved.

---

# 39. Fault-injection matrix

| Fault | Required outcome |
|---|---|
| marker access denied | fail closed; no payload mutation |
| prompts child is junction | reject before write |
| recovery child is junction | reject before deletion |
| arbitrary manifest temp points to user file | manifest rejected |
| temp A == final B | manifest rejected |
| interrupted source != active source | no delete |
| capability probe crash | exact declared controls recover |
| initial marker partial write | corrupt marker at reserved path; no payload exists |
| Ready stage crash | Copying marker + declared stage recoverable |
| settings temp flush failure | no settings commit |
| settings MoveFileEx failure | no point of no return |
| stale settings temp | cleanup under settings lease |
| new foreign entry after retry cleanup | marker remains |
| remaining declared temp | marker remains |
| stale root `.app.lock` | retry permitted |
| held `.app.lock` | transition blocked |
| nested `.app.lock` | foreign |
| concurrent parent creation | not recorded as ours |
| successful new target release | no false directory warning |
| Restart MessageBox throws | shutdown still requested |
| required sentinel missing | CI fails |
| wrong published EXE icon | strict release gate fails |

---

# 40. Required preservation assertions

For failure tests, snapshot bytes before operation and compare afterward:

```text
settings.json
settings.backup.json
source library.json
source library.backup.json
source active prompt
source orphan prompt
source recovery artifact
foreign target file
other-attempt temp/control
bootstrap settings files
outside-junction target files
```

Use byte equality, not only `File.Exists`.

---

# 41. Weak-model traps — forbidden shortcuts

```text
Do not use File.Exists as authority presence.
Do not assume physical root makes child directories safe.
Do not accept arbitrary canonical temp paths.
Do not use separate uniqueness sets for finals and temps only.
Do not auto-clean a migration from another source.
Do not rely on in-memory transaction journal for crash ownership.
Do not create a random manifest staging temp before durable marker ownership.
Do not claim Flush(true) on staging file makes the final settings pathname durable.
Do not delete marker after checking only finals.
Do not mix raw recovery exceptions and RecoveryResult arbitrarily.
Do not call committed target nonempty state a cleanup failure.
Do not infer directory ownership from a stale Exists check.
Do not treat .app.lock presence as active lock ownership.
Do not compare physical target to lexical bootstrap.
Do not treat control filenames as controls at any depth.
Do not assign ReadyToCommit without a terminal invariant gate.
Do not keep nullable/unmanifested temp fallback for tests.
Do not replace primary failure with cleanup failure.
Do not use DialogResult as commit authority.
Do not tag a fake test as Windows integration evidence.
Do not let missing sentinels only warn.
Do not fabricate the product logo.
```

---

# 42. Recommended new files

```text
src/PromptHelper/Services/
    StrictFileAuthority.cs
    IAuthorityFileOps.cs
    ManagedTreeTopologyValidator.cs
    ManagedTreeLease.cs
    DataRootRuntimeContext.cs
    MigrationManifestBuilder.cs
    MigrationControlArtifact.cs
    MigrationCapabilityProbePlan.cs
    MigrationReadyGate.cs
    RecoveryBaselineVerifier.cs
    IDurableSettingsFileWriter.cs
    WindowsDurableSettingsFileWriter.cs
    SettingsTempName.cs
    MigrationRecoveryException.cs
    TargetRootReservationAcquireException.cs

tests/PromptHelper.Tests/
    Cruu9AuthorityFileTests.cs
    Cruu9ManagedTreeTests.cs
    Cruu9ManifestV3Tests.cs
    Cruu9MigrationControlTests.cs
    Cruu9DurableSettingsTests.cs
    Cruu9RecoveryTests.cs
    Cruu9ReservationTests.cs
    Cruu9WpfCommitBoundaryTests.cs
    Cruu9EvidenceTests.cs
    WindowsManagedTreeIntegrationTests.cs

tools/
    VerifyTestEvidence.ps1
    CompareIconIdentity.ps1
```

Do not create duplicate abstractions if newer HEAD already contains equivalent stronger ones.

---

# 43. Mandatory named tests

## Authority

```text
CRUU9_002_Unreadable_marker_is_not_treated_as_missing
CRUU9_002_Unreadable_final_is_not_treated_as_missing
CRUU9_002_Unreadable_temp_is_not_treated_as_missing
```

## Managed tree

```text
CRUU9_001_Empty_prompts_junction_outside_target_is_rejected
CRUU9_001_Empty_recovery_junction_outside_target_is_rejected
CRUU9_001_Retry_never_deletes_outside_bound_root
```

## Manifest

```text
CRUU9_003_Temp_without_attempt_id_rejected
CRUU9_003_Arbitrary_prompt_cannot_be_declared_as_temp
CRUU9_004_Temp_A_equal_Final_B_rejected
CRUU9_005_Different_source_interrupted_retry_fails_closed
CRUU9_024_Duplicate_targetPhysicalRoot_rejected
CRUU9_024_Invalid_UTF8_manifest_rejected
```

## Controls

```text
CRUU9_006_Crash_after_probe_dir_creation_recovers
CRUU9_007_Crash_during_ready_stage_is_recoverable
```

## Durable settings

```text
CRUU9_008_Settings_primary_uses_write_through_promotion
CRUU9_008_Settings_failure_before_durable_promotion_rolls_target_back
CRUU9_008_Marker_not_retired_before_durable_settings_return
CRUU9_009_Stale_primary_settings_temp_cleaned_under_lease
```

## Recovery

```text
CRUU9_010_New_foreign_entry_after_cleanup_preserves_marker
CRUU9_010_Remaining_temp_preserves_marker
CRUU9_011_Marker_delete_failure_is_typed_recovery_failure
```

## Reservation

```text
CRUU9_012_New_target_success_has_no_false_cleanup_warning
CRUU9_013_Concurrent_parent_creation_not_marked_owned
CRUU9_014_Stale_root_app_lock_does_not_make_target_occupied
```

## Bootstrap/control

```text
CRUU9_015_Physical_bootstrap_alias_is_recognized
CRUU9_016_Nested_app_lock_is_unknown
CRUU9_017_Ready_gate_rejects_declared_temp
CRUU9_018_Copy_requires_manifest_artifact_for_every_snapshot_file
```

## UI/evidence

```text
CRUU9_020_RestartRequired_true_Result_null_shuts_down
CRUU9_020_Restart_message_failure_still_requests_shutdown
CRUU9_021_VerifyTestEvidence_rejects_missing_sentinel_TRX
```

---

# 44. Windows integration requirements

Mandatory real Windows operations:

```text
real target-root junction
real prompts-child junction
real recovery-child junction
real persisted root alias
real case-sensitive directory
real MoveFileEx settings writer smoke test
real handle-bound verified deletion
real stale .app.lock acquisition
```

A fake inspector cannot satisfy the real case-sensitive test.

A skipped mandatory integration test is missing evidence, not PASS.

---

# 45. Phase implementation loop for weak AI

For every phase:

```text
1. Read current production code.
2. Read current related tests.
3. Implement production change.
4. Add direct negative + positive focused tests.
5. Build Release.
6. Run focused tests.
7. Inspect git diff for weakening/escape hatches.
8. Only then move to next phase.
```

Never implement all phases before compiling.

---

# 46. Focused test command examples

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --filter "FullyQualifiedName~Cruu9AuthorityFileTests"
```

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --filter "FullyQualifiedName~Cruu9ManagedTreeTests"
```

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --filter "FullyQualifiedName~Cruu9ManifestV3Tests"
```

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --filter "FullyQualifiedName~Cruu9RecoveryTests"
```

---

# 47. Full acceptance commands

## Restore

```powershell
dotnet restore PromptHelper.slnx
```

## Release build

```powershell
dotnet build PromptHelper.slnx `
  -c Release `
  --no-restore
```

Required:

```text
0 errors
0 warnings
```

## Mandatory categories

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --filter "TestCategory=CrashRecovery" `
  --logger "trx;LogFileName=cruu9-crash.trx"
```

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --filter "TestCategory=WpfIntegration" `
  --logger "trx;LogFileName=cruu9-wpf.trx"
```

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --filter "TestCategory=WindowsFilesystemIntegration" `
  --logger "trx;LogFileName=cruu9-winfs.trx"
```

## Full suite

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --logger "trx;LogFileName=cruu9-full.trx"
```

## Five runs

```powershell
1..5 | ForEach-Object {
    Write-Host "=== CRUU9 FULL RUN $_ / 5 ==="

    dotnet test PromptHelper.slnx `
      -c Release `
      --no-build `
      --logger "trx;LogFileName=cruu9-run-$_.trx"

    if ($LASTEXITCODE -ne 0) {
        throw "CRUU9 full-suite run $_ failed."
    }
}
```

## Publish

```powershell
Remove-Item -Recurse -Force artifacts\publish-check -ErrorAction SilentlyContinue

dotnet publish `
  src/PromptHelper/PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/publish-check
```

Require:

```text
PromptHelper.exe
LICENSE
THIRD_PARTY_NOTICES.md
```

---

# 48. Final source grep gate

After tests pass:

```powershell
git grep -n "File.Exists" -- `
  src/PromptHelper/Services/Migration* `
  src/PromptHelper/App.xaml.cs
```

Every result must be manually reviewed. Authority decisions should not rely on it.

Also:

```powershell
git grep -n "declaredTempMap"
git grep -n "Assert.Throws<Exception>"
git grep -n "Write-Warning" -- tools/VerifyTestEvidence.ps1
git grep -n "TargetIsBootstrapRoot"
```

Expected after CRUU9:

```text
nullable declaredTempMap escape: none
CRUU9 safety Assert.Throws<Exception>: none
missing-evidence warning-only path: none
redundant TargetIsBootstrapRoot: none
```

---

# 49. Per-finding acceptance criteria

## CRUU9-001

```text
PASS only if real child-junction tests prove no migration/recovery write/delete can escape physical root.
```

## CRUU9-002

```text
PASS only if unreadable marker/final/temp is never treated Missing and File.Exists is removed from authority gates.
```

## CRUU9-003

```text
PASS only if temp grammar includes exact final name + AttemptId + 32 hex nonce chars and arbitrary target files cannot be temp-owned.
```

## CRUU9-004

```text
PASS only if one unified path set rejects every final/temp/control collision.
```

## CRUU9-005

```text
PASS only if retry source identity must match and different-source bytes are preserved.
```

## CRUU9-006

```text
PASS only if crash at every capability-probe creation point auto-recovers from durable declared ownership.
```

## CRUU9-007

```text
PASS only if no manifest stage can exist without a reserved final marker or declared control authority.
```

## CRUU9-008

```text
PASS only if settings primary final promotion is write-through and marker retirement occurs afterward.
```

## CRUU9-009

```text
PASS only if stale settings temps have strict grammar, cleanup occurs under settings lease and similar foreign filenames survive.
```

## CRUU9-010

```text
PASS only if second full inventory proves no unknown/temp/control residue before marker deletion.
```

## CRUU9-011

```text
PASS only if all expected recovery filesystem failures use one typed contract.
```

## CRUU9-012

```text
PASS only if successful new root has no false cleanup warning and created directories are preserved after commit.
```

## CRUU9-013

```text
PASS only if directory ownership records native creation success, not a stale pre-check.
```

## CRUU9-014

```text
PASS only if stale root lock is retryable and held lock blocks.
```

## CRUU9-015

```text
PASS only if physical bootstrap is resolved once and used consistently.
```

## CRUU9-016

```text
PASS only if control paths are recognized by exact root-relative path, not filename.
```

## CRUU9-017

```text
PASS only if Ready phase has explicit terminal invariant gate.
```

## CRUU9-018

```text
PASS only if no production/internal copy API can generate unmanifested temps.
```

## CRUU9-019

```text
PASS only if both original and cleanup failures remain inspectable.
```

## CRUU9-020

```text
PASS only if RestartRequired always causes shutdown regardless modal result or restart-message failure.
```

## CRUU9-021 / 022

```text
PASS only if tests actually execute named behavior and CI fails when mandatory evidence is missing.
```

## CRUU9-023

```text
PASS code-preparation only when exact normalized icon identity verifier is implemented with fixtures.
```

## CRUU9-024

```text
PASS only if duplicate critical JSON fields and malformed UTF-8 fail closed.
```

## CRUU9-025

```text
BLOCKED until the real approved product artwork is supplied.
```

---

# 50. Required implementation evidence report

```text
CRUU9 IMPLEMENTATION EVIDENCE

BASELINE
- starting commit:
- final commit:
- branch:

FINDINGS
- CRUU9-001:
- CRUU9-002:
- CRUU9-003:
- CRUU9-004:
- CRUU9-005:
- CRUU9-006:
- CRUU9-007:
- CRUU9-008:
- CRUU9-009:
- CRUU9-010:
- CRUU9-011:
- CRUU9-012:
- CRUU9-013:
- CRUU9-014:
- CRUU9-015:
- CRUU9-016:
- CRUU9-017:
- CRUU9-018:
- CRUU9-019:
- CRUU9-020:
- CRUU9-021:
- CRUU9-022:
- CRUU9-023:
- CRUU9-024:
- CRUU9-025:

BUILD
- exact command:
- exit:
- warnings:
- errors:

FOCUSED TESTS
- authority:
- managed tree:
- manifest:
- control ownership:
- durable settings:
- recovery:
- reservation:
- WPF:
- Windows filesystem:
- evidence verifier:

FULL TEST
- total:
- passed:
- failed:
- skipped:
- duration:

FIVE RUNS
- 1:
- 2:
- 3:
- 4:
- 5:

MANDATORY WINDOWS EVIDENCE
- prompts junction:
- recovery junction:
- real case-sensitive NTFS:
- verified deletion:
- stale app lock:
- write-through settings wrapper:

CRASH FIXTURES
- corrupt initial marker:
- partial payload temp:
- capability probe:
- Ready stage:
- settings temp:
- marker unreadable:
- post-clean foreign race:

PUBLISH
- exit:
- PromptHelper.exe:
- LICENSE:
- THIRD_PARTY_NOTICES.md:

RELEASE ICON
- approved SVG present:
- committed ICO:
- SVG->ICO identity:
- ICO->EXE identity:
- strict release gate:

FINAL
- product/code:
- strict release:
```

Never claim PASS without direct command/result evidence.

---

# 51. Definition of done — product/code

```text
[ ] File.Exists removed from migration authority decisions.
[ ] Unreadable marker cannot become Missing.
[ ] prompts/recovery reparse points rejected.
[ ] Managed tree validated at startup/copy/recovery.
[ ] Recovery cannot delete outside physical root.
[ ] Manifest schema v3 implemented.
[ ] Temp grammar exact final+AttemptId+128-bit nonce.
[ ] One path set rejects final/temp/control collisions.
[ ] Retry requires matching source identity.
[ ] Capability probe controls durably owned.
[ ] Initial marker has no unowned staging-temp window.
[ ] Ready staging deterministic and owned.
[ ] settings.json final promotion write-through durable.
[ ] Marker retirement after durable settings only.
[ ] Stale settings temps cleaned under settings lease.
[ ] Retry second inventory rejects unknown/temp/control residue.
[ ] Recovery has one typed error contract.
[ ] New target success has no false directory warning.
[ ] Reservation records only actually-created directories.
[ ] Reservation acquisition cleanup failures surfaced.
[ ] Stale root .app.lock retryable.
[ ] Held root .app.lock blocks.
[ ] Nested control-name files are foreign.
[ ] Physical bootstrap resolved once and threaded consistently.
[ ] ReadyToCommit has explicit terminal gate.
[ ] Unmanifested temp fallback removed.
[ ] Manifest cleanup exception preserves both failures.
[ ] RestartRequired forces shutdown regardless DialogResult.
[ ] Restart notification failure still shuts down.
[ ] Safety tests use exact exceptions.
[ ] Real Windows category uses real Windows operations.
[ ] Missing test evidence fails CI.
[ ] CI runs categories separately.
[ ] Full Release suite passes.
[ ] Five consecutive suites pass.
[ ] Self-contained win-x64 publish passes.
```

---

# 52. Definition of done — release

```text
[ ] Approved PromptHelperLogo.svg supplied.
[ ] PromptHelper.ico generated from exact approved SVG.
[ ] Normalized SVG->ICO frame identity passes at every required size.
[ ] Published EXE embedded icon identity equals committed ICO.
[ ] Explorer icon manually verified.
[ ] Taskbar icon manually verified.
[ ] Alt+Tab icon manually verified.
[ ] Window icon manually verified.
```

Until approved SVG exists:

```text
STRICT RELEASE = BLOCKED
```

---

# 53. Copy-ready maximal weak-model implementation prompt

```text
ROLE
You are a weak implementation AI for Prompt Helper.
You are not designing architecture. CRUU9 defines the architecture.

AUDITED BASELINE
be1da4fa49916a102616f82a6c74f5601ab5d2d6

PRIMARY INPUT
cruu9.md

GOAL
Implement CRUU9-001 through CRUU9-024 exactly.
CRUU9-025 remains BLOCKED unless the real approved product logo is supplied.

IF HEAD IS NEWER
Do not reset. Compare the newer code to the audited baseline, preserve equivalent/stronger fixes, and implement only missing CRUU9 behavior.

MANDATORY PHASE ORDER
0 baseline
1 strict authority file operations
2 managed physical tree containment
3 manifest schema v3 authority
4 migration control ownership + manifest staging
5 crash-durable settings pointer
6 recovery terminal-state rewrite
7 reservation ownership/commit semantics
8 remove unmanifested escape hatches
9 unconditional postcommit shutdown
10 tests + CI evidence
11 exact release identity tooling
12 full build/test/stress/publish
13 final source audit

NON-NEGOTIABLE SAFETY RULES
- Do not use File.Exists to decide whether a migration authority/control file is absent.
- Missing and unreadable are different states.
- prompts/recovery may not be reparse points.
- No migration/recovery operation may escape the bound physical root.
- Every payload temp must encode exact final name, AttemptId and 32 hex nonce chars.
- One unified owned-path set must reject final/temp/control collisions.
- Retry cleanup requires manifest source physical root == current active source physical root.
- Every precommit control file that can survive a crash must have durable ownership.
- Do not create hidden random capability probe paths during manifested migration.
- Initial Copying marker must not depend on an unowned staging temp.
- Ready replacement staging must be deterministic/owned.
- settings.json final pointer promotion must be write-through durable before marker retirement.
- Stale settings temps use strict reserved grammar and are cleaned only under settings lease.
- Marker is deleted only after a second full inventory proves baseline restored.
- New target directory ownership becomes committed after settings commit and is never treated as cleanup residue.
- Record only directories this process actually created.
- Root .app.lock presence is not active lock ownership.
- Resolve physical bootstrap once; never compare physical target to lexical bootstrap.
- Root control names are controls only at exact root-relative paths.
- ReadyToCommit requires an explicit final invariant gate.
- Remove nullable/unmanifested temp fallback from CopySnapshotToTarget.
- Preserve original failure and cleanup failure separately.
- RestartRequired=true always forces process shutdown regardless DialogResult.
- Real Windows tests execute real Windows operations.
- Missing mandatory TRX sentinel fails CI.
- Do not fabricate PromptHelperLogo.svg.

TEST RULES
- Direct negative and positive tests for every CRUU9 finding.
- Exact exception types; no Assert.Throws<Exception> for safety tests.
- Byte-preservation assertions for source/bootstrap/foreign data.
- Fault injection callbacks, not sleeps.
- Mandatory categories run separately.
- Full Release suite five consecutive times.
- Self-contained win-x64 publish.

FINAL OUTPUT
Return the exact CRUU9 IMPLEMENTATION EVIDENCE template from cruu9.md.
Never claim any build/test/publish/manual check passed unless it actually ran.
```

---

# 54. Final audit conclusion

At:

```text
be1da4fa49916a102616f82a6c74f5601ab5d2d6
```

Prompt Helper is materially safer than at the CRUU7 baseline, but CRUU8 has not yet closed every terminal state.

The final CRUU9 repair is not “add more checks.” It is:

```text
PHYSICAL ROOT AUTHORITY
+
PHYSICAL MANAGED-CHILD AUTHORITY
+
STRICT PRESENT/MISSING/UNREADABLE STATES
+
COMPLETE PAYLOAD + CONTROL OWNERSHIP
+
DURABLE SETTINGS POINTER
+
TERMINAL RECOVERY VERIFICATION
+
EXECUTED TEST EVIDENCE
```

The implementation model must repeatedly ask:

```text
If the process dies on the next line, is every surviving path either:
  durably final,
  durably owned,
  strictly absent,
  or explicitly foreign and untouched?
```

For every managed path it must also ask:

```text
Is this only a lexical descendant, or have I proved that its physical
behavior remains inside the bound physical data tree?
```

CRUU9 is the implementation authority for the next repair pass.

---

# 55. FILE-BY-FILE IMPLEMENTATION MAP FOR THE WEAK MODEL

This section is mandatory. The implementer should use it as the edit checklist.

## `App.xaml.cs`

Must change:

```text
- resolve physical bootstrap root once;
- construct DataRootRuntimeContext;
- acquire app lock;
- validate managed tree;
- call migration startup finalization unconditionally rather than behind File.Exists;
- do not show MainWindow until marker finalization has definitely succeeded;
- validate managed tree again before repositories are exposed.
```

Expected final conceptual flow:

```csharp
var runtime = DataRootRuntimeContext.Create(...);
var paths = new AppPaths(runtime.ActivePhysicalRoot);

_appLock = AppInstanceLock.TryAcquire(paths.LockPath);
if (_appLock is null)
{
    ...
    return;
}

var tree = new ManagedTreeTopologyValidator(physicalResolver);
tree.ValidateManagedTree(runtime.ActivePhysicalRoot);

var recovery = new MigrationRecoveryService(...);
recovery.FinalizeCommittedStartup(
    new MigrationRecoveryContext(
        runtime.ActivePhysicalRoot,
        runtime.BootstrapPhysicalRoot,
        ExpectedSourcePhysicalRoot: null));

paths.EnsureDataDirectories();
tree.ValidateManagedTree(runtime.ActivePhysicalRoot);

// Only now load library and show UI.
```

Do not keep the old outer `File.Exists(paths.MigrationMarkerPath)` gate.

## `MainWindow.xaml.cs`

Must change:

```text
- forced shutdown depends on RestartRequired only;
- notification MessageBox is inside try/finally;
- finally always calls RequestShutdown.
```

## `AppSettingsRepository.cs`

Must change:

```text
- inject durable settings writer;
- clean strict settings temps while settings lease held;
- write authoritative primary through durable writer;
- keep dual-file CAS semantics;
- no public Core bypass;
- backup warning semantics preserved.
```

## `AtomicTextWriter.cs`

Do not necessarily replace globally if not needed.

But:

```text
- settings primary must not depend on its weaker final promotion;
- if generic writer remains for prompt/library writes, document that it is not the transition-pointer writer.
```

## `DataFolderTransitionCoordinator.cs`

Must change:

```text
- construct DataRootRuntimeContext;
- use ManifestBuilder, not hand-built temp strings;
- pass physical bootstrap root;
- retry recovery receives active source physical root;
- managed-tree validation/lease before copy and recovery;
- initial Copying marker uses direct durable final-path creation;
- capability validator receives manifest-declared probe plan;
- Ready gate before phase update;
- settings durable return is the point of no return;
- call reservation.CommitRootOwnership after commit;
- postcommit warning only after point of no return.
```

## `DataFolderMigrationService.cs`

Must change:

```text
- remove optional declaredTempMap fallback;
- require manifest object;
- validate snapshot <-> manifest one-to-one mapping before target mutation;
- validate managed child tree before write;
- use strict authority reads for source/target control where relevant;
- unit helper constructs a real manifest or is removed.
```

## `MigrationAttemptManifest.cs`

Must change:

```text
- schema 3;
- add ControlArtifacts;
- remove TargetIsBootstrapRoot;
- keep Artifact TempRelativePath;
```

## `MigrationManifestRepository.cs`

Must change:

```text
- no File.Exists authority gate;
- strict UTF-8;
- strict duplicate property detection;
- temp grammar validation;
- one unified owned path set;
- validate controls;
- initial marker direct writer;
- deterministic Ready staging path;
- preserve original+cleanup exception.
```

## `MigrationRecoveryService.cs`

Must change:

```text
- source identity required for retry;
- strict marker presence/read;
- managed-tree validation;
- exact root-relative control handling;
- manifest controls recognized;
- no wildcard temp cleanup;
- second full terminal inventory;
- marker delete last;
- one typed failure contract.
```

## `EmptyTargetBaselineInspector.cs`

Must change:

```text
- exact root .app.lock is allowed control;
- nested .app.lock is not allowed;
- exact bootstrap controls use PHYSICAL bootstrap identity;
- prompts/recovery must be empty ordinary non-reparse directories;
- strict unknown-entry reporting.
```

## `TargetRootReservation.cs`

Must change:

```text
- native per-component creation ownership;
- cleanup failures on acquisition surfaced;
- CommitRootOwnership state;
- committed target directory chain never cleaned.
```

## `IVerifiedArtifactDeleter.cs`

Must change:

```text
- remove File.Exists pre-check;
- distinguish native not-found from access denied;
- constrain opened handle final physical path to bound root;
- hash and delete same file handle identity.
```

## `DataRootCapabilityValidator.cs`

Must change:

```text
- empty manifested migration consumes predeclared probe plan;
- no hidden random probe names for manifested transition;
- current existing-library behavior remains role-aware;
- shared schema compatibility authority retained.
```

## `VerifyTestEvidence.ps1`

Must change:

```text
- missing required test => throw/fail;
- exact test-name matching;
- failed/skipped/not-executed required test => fail;
- support caller-supplied required test names.
```

## `.github/workflows/windows-ci.yml`

Must change:

```text
- separate CrashRecovery run;
- separate WpfIntegration run;
- separate WindowsFilesystemIntegration run;
- invoke evidence verifier for each;
- full suite still runs;
- stress five-run option retained;
- publish retained;
- strict icon gate remains conditional on real asset.
```

---

# 56. COPY-READY MANIFEST V3 VALIDATION CORE

The weak model should use this structure rather than inventing an alternative validation algorithm.

```csharp
private static void ValidateManifestInvariants(
    MigrationAttemptManifest manifest,
    string markerPath)
{
    if (manifest.SchemaVersion !=
        MigrationAttemptManifest.CurrentSchemaVersion)
    {
        throw new InvalidDataException(
            $"Unsupported migration manifest schema " +
            $"{manifest.SchemaVersion} at '{markerPath}'.");
    }

    if (manifest.AttemptId == Guid.Empty)
    {
        throw new InvalidDataException(
            "Migration AttemptId cannot be empty.");
    }

    RequireFullyQualified(
        manifest.SourcePhysicalRoot,
        "SourcePhysicalRoot");

    RequireFullyQualified(
        manifest.TargetPhysicalRoot,
        "TargetPhysicalRoot");

    if (PathIdentity.Equals(
            manifest.SourcePhysicalRoot,
            manifest.TargetPhysicalRoot))
    {
        throw new InvalidDataException(
            "Migration source and target roots must differ.");
    }

    ValidateSha256Hex(
        manifest.SourceLibrarySha256Hex,
        "SourceLibrarySha256Hex");

    if (!Enum.IsDefined(manifest.Phase))
    {
        throw new InvalidDataException(
            $"Undefined migration phase '{manifest.Phase}'.");
    }

    if (manifest.Artifacts is null ||
        manifest.Artifacts.Count == 0)
    {
        throw new InvalidDataException(
            "Migration manifest requires payload artifacts.");
    }

    if (manifest.ControlArtifacts is null)
    {
        throw new InvalidDataException(
            "Migration manifest ControlArtifacts cannot be null.");
    }

    var allOwned =
        new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

    int primaryCount = 0;
    int backupCount = 0;

    foreach (MigrationManifestArtifact artifact
             in manifest.Artifacts)
    {
        if (!Enum.IsDefined(artifact.Role))
        {
            throw new InvalidDataException(
                $"Undefined payload role '{artifact.Role}'.");
        }

        ValidateSha256Hex(
            artifact.Sha256Hex,
            $"SHA-256 for {artifact.RelativePath}");

        if (artifact.Length < 0)
        {
            throw new InvalidDataException(
                $"Negative artifact length: " +
                $"'{artifact.RelativePath}'.");
        }

        string finalFull =
            ResolveManifestArtifactPath(
                manifest.TargetPhysicalRoot,
                artifact.RelativePath);

        string tempFull =
            ResolveManifestArtifactPath(
                manifest.TargetPhysicalRoot,
                artifact.TempRelativePath);

        ValidateTempPath(
            manifest.AttemptId,
            artifact.RelativePath,
            artifact.TempRelativePath);

        AddUniqueOwnedPath(
            allOwned,
            finalFull,
            artifact.RelativePath);

        AddUniqueOwnedPath(
            allOwned,
            tempFull,
            artifact.TempRelativePath);

        ValidateRolePath(artifact);

        if (artifact.Role ==
            MigrationPayloadRole.PrimaryMetadata)
        {
            primaryCount++;

            if (!artifact.RelativePath.Equals(
                    "library.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "PrimaryMetadata must be library.json.");
            }

            if (!artifact.Sha256Hex.Equals(
                    manifest.SourceLibrarySha256Hex,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Primary library hash must equal " +
                    "SourceLibrarySha256Hex.");
            }
        }

        if (artifact.Role ==
            MigrationPayloadRole.SafetyBackup)
        {
            backupCount++;
        }
    }

    if (primaryCount != 1)
    {
        throw new InvalidDataException(
            $"Exactly one PrimaryMetadata artifact is required; " +
            $"found {primaryCount}.");
    }

    if (backupCount > 1)
    {
        throw new InvalidDataException(
            $"At most one SafetyBackup artifact is allowed; " +
            $"found {backupCount}.");
    }

    foreach (MigrationControlArtifact control
             in manifest.ControlArtifacts)
    {
        if (!Enum.IsDefined(control.Kind))
        {
            throw new InvalidDataException(
                $"Undefined control kind '{control.Kind}'.");
        }

        string full = ResolveControlPath(
            manifest,
            control);

        AddUniqueOwnedPath(
            allOwned,
            full,
            control.RelativePath);

        ValidateControlGrammar(
            manifest.AttemptId,
            control);
    }
}

private static void AddUniqueOwnedPath(
    HashSet<string> allOwned,
    string fullPath,
    string displayPath)
{
    if (!allOwned.Add(fullPath))
    {
        throw new InvalidDataException(
            $"Migration ownership path collision: " +
            $"'{displayPath}'.");
    }
}
```

The implementation must not split payload final/temp/control uniqueness into independent sets.

---

# 57. COPY-READY CONTROL ARTIFACT GRAMMAR

Recommended exact grammar:

```text
ManifestPhaseStaging:
.prompthelper-migration.stage-<attemptIdN>.tmp

Root CapabilityProbeDirectory:
.prompthelper-write-probe-<attemptIdN>-root

Root CapabilityProbeFile:
.prompthelper-write-probe-<attemptIdN>-root\probe-current.txt
.prompthelper-write-probe-<attemptIdN>-root\probe-replacement.tmp

Prompts CapabilityProbeDirectory:
prompts\.prompthelper-write-probe-<attemptIdN>-prompts

Prompts CapabilityProbeFile:
prompts\.prompthelper-write-probe-<attemptIdN>-prompts\probe-current.txt
prompts\.prompthelper-write-probe-<attemptIdN>-prompts\probe-replacement.tmp
```

No other `.prompthelper-write-probe-*` path is auto-owned.

Do not use wildcard deletion.

---

# 58. COPY-READY RETRY RECOVERY SKELETON

```csharp
public void RecoverForRetry(
    MigrationRecoveryContext context)
{
    ArgumentNullException.ThrowIfNull(context);

    string markerPath = Path.Combine(
        context.TargetPhysicalRoot,
        ".prompthelper-migration.json");

    MigrationAttemptManifest? manifest;

    try
    {
        manifest = _manifestRepo.TryReadStrict(markerPath);
    }
    catch (Exception ex)
    {
        throw Wrap(
            context,
            "ReadManifest",
            ex);
    }

    if (manifest is null)
    {
        return;
    }

    RequireTargetIdentity(
        context,
        manifest);

    RequireRetrySourceIdentity(
        context,
        manifest);

    _treeValidator.ValidateManagedTree(
        context.TargetPhysicalRoot);

    using ManagedTreeLease treeLease =
        ManagedTreeLease.Acquire(
            context.TargetPhysicalRoot);

    TargetRecoveryInventory before =
        BuildInventory(
            context,
            manifest);

    if (before.HasUnknownEntries)
    {
        throw RecoveryForeignData(
            context,
            before.UnknownEntries);
    }

    DeleteDeclaredControlsForRetry(
        context,
        manifest);

    DeleteDeclaredPayloadTemps(
        context,
        manifest);

    foreach (MigrationManifestArtifact artifact
             in manifest.Artifacts)
    {
        string finalPath =
            MigrationManifestRepository
                .ResolveManifestArtifactPath(
                    context.TargetPhysicalRoot,
                    artifact.RelativePath);

        _verifiedDeleter.VerifyAndDelete(
            context.TargetPhysicalRoot,
            finalPath,
            artifact.Length,
            artifact.Sha256Hex);
    }

    CleanupAttemptDirectories(context);

    TargetRecoveryInventory after =
        BuildInventory(
            context,
            manifest);

    RecoveryBaselineVerifier.AssertRestored(
        context.TargetPhysicalRoot,
        after,
        _authorityOps);

    _manifestRepo.DeleteStrict(markerPath);

    if (_authorityOps.GetPresenceStrict(markerPath) !=
        StrictFilePresence.Missing)
    {
        throw new MigrationRecoveryException(
            context.TargetPhysicalRoot,
            "RetireManifest",
            new IOException(
                "Migration marker still exists after deletion."));
    }
}
```

Important:

```text
marker deletion is last
source identity is checked before deletion
managed tree is validated before deletion
second inventory is mandatory
```

---

# 59. COPY-READY COMMITTED STARTUP FINALIZATION SKELETON

```csharp
public void FinalizeCommittedStartup(
    MigrationRecoveryContext context)
{
    string markerPath = Path.Combine(
        context.TargetPhysicalRoot,
        ".prompthelper-migration.json");

    MigrationAttemptManifest? manifest =
        _manifestRepo.TryReadStrict(markerPath);

    if (manifest is null)
    {
        return;
    }

    if (manifest.Phase !=
        MigrationManifestPhase.ReadyToCommit)
    {
        throw new MigrationRecoveryException(
            context.TargetPhysicalRoot,
            "ValidateCommittedPhase",
            new InvalidDataException(
                $"Configured target contains incomplete " +
                $"migration phase '{manifest.Phase}'."));
    }

    RequireTargetIdentity(context, manifest);
    _treeValidator.ValidateManagedTree(
        context.TargetPhysicalRoot);

    using ManagedTreeLease lease =
        ManagedTreeLease.Acquire(
            context.TargetPhysicalRoot);

    AssertNoPayloadTemps(
        context,
        manifest);

    AssertNoEphemeralControls(
        context,
        manifest);

    AssertEveryFinalMatches(
        context,
        manifest);

    TargetRecoveryInventory inventory =
        BuildInventory(
            context,
            manifest);

    if (inventory.HasUnknownEntries)
    {
        throw RecoveryForeignData(
            context,
            inventory.UnknownEntries);
    }

    _manifestRepo.DeleteStrict(markerPath);

    if (_authorityOps.GetPresenceStrict(markerPath) !=
        StrictFilePresence.Missing)
    {
        throw new MigrationRecoveryException(
            context.TargetPhysicalRoot,
            "RetireCommittedMarker",
            new IOException(
                "Ready migration marker could not be retired."));
    }
}
```

No MainWindow until this method returns.

---

# 60. COPY-READY RESERVATION DIRECTORY CREATION OUTLINE

```csharp
private static IReadOnlyList<string>
    CreateMissingDirectoryChainOwned(
        string root,
        IReservationFileOps ops)
{
    List<string> candidates =
        GetMissingDirectoryChain(root, ops)
            .ToList();

    var owned = new List<string>();

    try
    {
        foreach (string candidate in candidates)
        {
            DirectoryCreateOutcome result =
                ops.TryCreateDirectoryOwned(candidate);

            if (result ==
                DirectoryCreateOutcome.CreatedByCaller)
            {
                owned.Add(candidate);
            }
        }

        return owned;
    }
    catch (Exception original)
    {
        var failures =
            new List<MigrationRollbackFailure>();

        CleanupCreatedDirectories(
            owned,
            ops,
            failures);

        if (failures.Count > 0)
        {
            throw new TargetRootReservationAcquireException(
                root,
                original,
                failures);
        }

        throw;
    }
}
```

Never record an `AlreadyExists` directory as owned.

---

# 61. COPY-READY `VerifyTestEvidence.ps1` CORE

The final script must **fail**, not warn, when mandatory evidence is missing.

```powershell
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$TrxPath,

    [string[]]$RequiredTests = @()
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $TrxPath)) {
    throw "TRX file not found: $TrxPath"
}

[xml]$trx = Get-Content $TrxPath -Raw

$ns = [System.Xml.XmlNamespaceManager]::new($trx.NameTable)
$ns.AddNamespace(
    "t",
    "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")

$results = $trx.SelectNodes("//t:UnitTestResult", $ns)
if ($results.Count -eq 0) {
    $results = $trx.SelectNodes("//UnitTestResult")
}

if ($results.Count -eq 0) {
    throw "TRX contains no executed test results."
}

$byName = @{}
foreach ($result in $results) {
    $byName[[string]$result.testName] = $result
}

foreach ($required in $RequiredTests) {
    if (-not $byName.ContainsKey($required)) {
        throw "Required test was not executed: $required"
    }

    $outcome = [string]$byName[$required].outcome
    if ($outcome -ne "Passed") {
        throw (
            "Required test did not pass: " +
            "$required outcome=$outcome"
        )
    }
}

Write-Host (
    "Required test evidence verified: " +
    "$($RequiredTests.Count) sentinel(s)."
)
```

Keep the existing whole-run counters check too.

---

# 62. CRASH FIXTURE BUILDER FOR TESTS

Create a reusable test helper instead of hand-writing inconsistent manifests.

```csharp
internal sealed class MigrationCrashFixtureBuilder
{
    private readonly string _sourceRoot;
    private readonly string _targetRoot;
    private readonly Guid _attemptId;
    private readonly MigrationAttemptManifest _manifest;

    public MigrationCrashFixtureBuilder(
        string sourceRoot,
        string targetRoot,
        MigrationPayloadSnapshot snapshot)
    {
        _sourceRoot = sourceRoot;
        _targetRoot = targetRoot;
        _attemptId = Guid.NewGuid();

        _manifest = MigrationManifestBuilder.BuildCopying(
            sourceRoot,
            targetRoot,
            snapshot,
            _attemptId);
    }

    public MigrationCrashFixtureBuilder WithPhase(
        MigrationManifestPhase phase)
    {
        _manifest.Phase = phase;
        return this;
    }

    public MigrationCrashFixtureBuilder WithPartialTemp(
        string finalRelativePath,
        byte[] bytes)
    {
        MigrationManifestArtifact artifact =
            _manifest.Artifacts.Single(
                x => x.RelativePath.Equals(
                    finalRelativePath,
                    StringComparison.OrdinalIgnoreCase));

        string path =
            MigrationManifestRepository.ResolveManifestArtifactPath(
                _targetRoot,
                artifact.TempRelativePath);

        Directory.CreateDirectory(
            Path.GetDirectoryName(path)!);

        File.WriteAllBytes(path, bytes);
        return this;
    }

    public MigrationCrashFixtureBuilder WithForeignFile(
        string relativePath,
        byte[] bytes)
    {
        string path = Path.Combine(
            _targetRoot,
            relativePath);

        Directory.CreateDirectory(
            Path.GetDirectoryName(path)!);

        File.WriteAllBytes(path, bytes);
        return this;
    }

    public MigrationAttemptManifest Manifest => _manifest;
}
```

Use this helper so test manifests use the same valid production grammar except for the exact field under test.

---

# 63. REQUIRED NEGATIVE TESTS

The weak model must not stop at positive cases.

Mandatory negative states:

```text
unreadable marker
unreadable payload final
prompts junction to outside
recovery junction to outside
temp path unrelated to final
temp with wrong AttemptId
temp with too-short nonce
temp/final cross collision
different-source interrupted marker
probe residue from same attempt
probe residue from other attempt
Ready stage residue
settings write-through failure
stale settings temp cannot be deleted
second inventory gains foreign file
remaining temp after cleanup
stale root lock
held root lock
nested root-control name
Ready gate sees changed final
Ready gate sees child junction
postcommit MessageBox throws
missing required TRX sentinel
wrong EXE icon fixture
invalid UTF-8 manifest
duplicate critical manifest property
```

For each destructive/recovery negative test, assert foreign/source/bootstrap bytes remain exact.

---

# 64. REQUIRED POSITIVE TESTS

Mandatory success states:

```text
normal empty custom migration
normal existing-library switch
custom -> exact physical default bootstrap migration
retry same-source Copying marker with partial temp
retry same-source Copying marker with partial final
committed startup Ready marker
stale root app lock then successful reservation
normal non-reparse prompts/recovery tree
read-only safety backup warning semantics
successful new target with zero false cleanup warnings
normal shutdown after committed settings transition
```

---

# 65. FINAL WEAK-AI SELF-REVIEW QUESTIONS

Before claiming completion, answer each with YES and cite the code/test that proves it:

```text
1. Can an unreadable migration marker ever be interpreted as absent?
2. Can prompts/recovery physically resolve outside the bound root?
3. Can a manifest temp point at a user file that does not match production temp grammar?
4. Can any temp/control/final path collide with another owned path?
5. Can retry delete an interrupted migration belonging to another source?
6. Can a crash during capability probing leave an unowned path?
7. Can a crash during manifest phase replacement leave an unknown stage path?
8. Is settings.json final-name promotion write-through durable before marker retirement?
9. Can stale settings staging files block default-root recovery without a controlled cleanup path?
10. Does retry run a second complete inventory before deleting marker?
11. Can a recovery filesystem exception escape without the typed recovery contract?
12. Does successful new-root Release avoid trying to delete the committed root?
13. Does directory ownership rely only on actual native creation success?
14. Is stale .app.lock treated differently from a held lock?
15. Is exact bootstrap comparison physical-to-physical?
16. Can nested .app.lock or nested manifest filenames be silently ignored?
17. Is ReadyToCommit preceded by a terminal invariant gate?
18. Is there any copy API that can generate an unmanifested temp?
19. If manifest write and cleanup both fail, are both causes preserved?
20. Does RestartRequired force shutdown even if DialogResult is null/false?
21. Do CRUU9 test names correspond to operations actually executed?
22. Does CI fail when a required sentinel does not run?
23. Does strict icon verification prove identity, not presence?
24. Are duplicate critical JSON fields and malformed UTF-8 rejected?
25. Is the real product logo still correctly reported BLOCKED if absent?
```

Any NO means CRUU9 is not complete.

---

# 66. FINAL ACCEPTANCE RULE

The implementation model may state:

```text
CRUU9 PRODUCT/CODE = PASS
```

only when all CRUU9-001 through CRUU9-024 are fixed **and** direct Windows evidence is complete.

It may state:

```text
STRICT RELEASE = PASS
```

only when CRUU9-025 is also resolved with the real approved logo and exact identity verification.

If the toolchain cannot execute a required Windows check, use:

```text
NOT_RUN / BLOCKED_EVIDENCE
```

not PASS.


# PART III — MAXIMAL WEAK-AI REPAIR PACK

This part is intentionally prescriptive. It is written for a weak implementation model that should make almost no architectural decisions. The rules below override any weaker or ambiguous implementation suggestion elsewhere in this document.

---

# 40. Master implementation contract

Do **not** implement findings independently. Many findings share the same low-level primitives. Use this dependency graph:

```text
STRICT FILE/DIRECTORY AUTHORITY
        |
        +--> PHYSICAL TREE BINDING / SESSION LEASE
        |
        +--> DURABLE ATOMIC WRITER
        |        |
        |        +--> MANIFEST REPOSITORY
        |        +--> SETTINGS REPOSITORY
        |        +--> LIBRARY REPOSITORY
        |        +--> PROMPT REPOSITORY
        |
        +--> MIGRATION MANIFEST V3
        |        |
        |        +--> MIGRATION RECOVERY
        |        +--> CAPABILITY PROBE OWNERSHIP
        |        +--> RESERVATION OWNERSHIP
        |
        +--> LIBRARY PACKAGE INSPECTOR
                 |
                 +--> STARTUP AUTHORITY
                 +--> MUTATION JOURNAL
                 +--> ORPHAN RECONCILER
```

Before editing:

```powershell
git status --short
git rev-parse HEAD
git branch --show-current
```

Rules:

```text
- never reset newer user work;
- never use git clean -fd;
- never delete unknown files;
- never overwrite a real approved logo if it appears;
- never weaken tests to obtain green CI;
- build after each implementation phase;
- run focused tests before moving to the next phase.
```

After each phase:

```powershell
dotnet build PromptHelper.slnx -c Release --no-restore
```

---

# 41. Canonical strict path-state model

Boolean existence APIs are forbidden for authority decisions. Add `src/PromptHelper/Services/StrictPathAuthority.cs`.

```csharp
using System;
using System.IO;
using System.Security;

namespace PromptHelper.Services;

internal enum StrictPathEntryKind
{
    Missing,
    File,
    Directory
}

internal sealed record StrictPathEntry(
    StrictPathEntryKind Kind,
    FileAttributes? Attributes)
{
    public bool IsMissing => Kind == StrictPathEntryKind.Missing;
    public bool IsFile => Kind == StrictPathEntryKind.File;
    public bool IsDirectory => Kind == StrictPathEntryKind.Directory;
}

internal sealed class StrictPathInspectionException : IOException
{
    public string InspectedPath { get; }

    public StrictPathInspectionException(string path, Exception inner)
        : base($"Could not determine filesystem state for '{path}': {inner.Message}", inner)
    {
        InspectedPath = path;
    }
}

internal interface IStrictPathAuthority
{
    StrictPathEntry Probe(string path);
    byte[] ReadAllBytesRequired(string path);
    IReadOnlyList<string> EnumerateEntriesRequired(string directory);
    IReadOnlyList<string> EnumerateFilesRequired(string directory, string searchPattern = "*");
}

internal sealed class StrictPathAuthority : IStrictPathAuthority
{
    public StrictPathEntry Probe(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            FileAttributes attrs = File.GetAttributes(path);
            bool directory = (attrs & FileAttributes.Directory) != 0;
            return new StrictPathEntry(
                directory ? StrictPathEntryKind.Directory : StrictPathEntryKind.File,
                attrs);
        }
        catch (FileNotFoundException)
        {
            return new StrictPathEntry(StrictPathEntryKind.Missing, null);
        }
        catch (DirectoryNotFoundException)
        {
            return new StrictPathEntry(StrictPathEntryKind.Missing, null);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new StrictPathInspectionException(path, ex);
        }
    }

    public byte[] ReadAllBytesRequired(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new StrictPathInspectionException(path, ex);
        }
    }

    public IReadOnlyList<string> EnumerateEntriesRequired(string directory)
    {
        StrictPathEntry probe = Probe(directory);
        if (probe.IsMissing) return [];
        if (!probe.IsDirectory)
            throw new InvalidDataException($"Expected a directory: '{directory}'.");

        try
        {
            return Directory.EnumerateFileSystemEntries(directory).ToList();
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new StrictPathInspectionException(directory, ex);
        }
    }

    public IReadOnlyList<string> EnumerateFilesRequired(string directory, string searchPattern = "*")
    {
        StrictPathEntry probe = Probe(directory);
        if (probe.IsMissing) return [];
        if (!probe.IsDirectory)
            throw new InvalidDataException($"Expected a directory: '{directory}'.");

        try
        {
            return Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly).ToList();
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new StrictPathInspectionException(directory, ex);
        }
    }
}
```

### Mandatory replacement rule

For authority code, replace this:

```csharp
if (File.Exists(path)) { ... }
```

with explicit classification:

```csharp
StrictPathEntry state = _strictPaths.Probe(path);

if (state.IsFile)
{
    // exact Present/File
}
else if (state.IsDirectory)
{
    throw new InvalidDataException($"Expected file but found directory: '{path}'.");
}
else
{
    // exact Missing only
}
```

Do not retain `FileExists()` / `DirectoryExists()` in migration/recovery authority interfaces.

---

# 42. Strict Windows directory opener

Add `IStrictDirectoryOpener.cs` and `WindowsStrictDirectoryOpener.cs`.

```csharp
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

internal enum StrictDirectoryOpenKind
{
    Missing,
    Opened
}

internal sealed record StrictDirectoryOpenResult(
    StrictDirectoryOpenKind Kind,
    SafeFileHandle? Handle);

internal interface IStrictDirectoryOpener
{
    StrictDirectoryOpenResult OpenForIdentity(string path);
    SafeFileHandle OpenManagedNodeLease(string path);
}
```

```csharp
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

internal sealed class WindowsStrictDirectoryOpener : IStrictDirectoryOpener
{
    private const uint FILE_READ_ATTRIBUTES = 0x00000080;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint OPEN_EXISTING = 3;
    private const int ERROR_FILE_NOT_FOUND = 2;
    private const int ERROR_PATH_NOT_FOUND = 3;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        FileShare dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    public StrictDirectoryOpenResult OpenForIdentity(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        SafeFileHandle handle = CreateFileW(
            path,
            FILE_READ_ATTRIBUTES,
            FileShare.Read | FileShare.Write | FileShare.Delete,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);

        if (!handle.IsInvalid)
            return new StrictDirectoryOpenResult(StrictDirectoryOpenKind.Opened, handle);

        int error = Marshal.GetLastWin32Error();
        handle.Dispose();

        if (error is ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND)
            return new StrictDirectoryOpenResult(StrictDirectoryOpenKind.Missing, null);

        throw new Win32Exception(error, $"Could not open directory for identity inspection: '{path}'.");
    }

    public SafeFileHandle OpenManagedNodeLease(string path)
    {
        SafeFileHandle handle = CreateFileW(
            path,
            FILE_READ_ATTRIBUTES,
            // Deliberately NO FileShare.Delete.
            FileShare.Read | FileShare.Write,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);

        if (!handle.IsInvalid)
            return handle;

        int error = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new Win32Exception(error, $"Could not acquire managed directory node lease for '{path}'.");
    }
}
```

Do **not** add `FileShare.Delete` to `OpenManagedNodeLease`.

---

# 43. Rewrite `WindowsPhysicalPathResolver`

Current authority must stop using `Directory.Exists` / `DirectoryInfo.Exists` to find the nearest existing ancestor.

Constructor:

```csharp
private readonly IStrictDirectoryOpener _directoryOpener;

public WindowsPhysicalPathResolver()
    : this(new WindowsStrictDirectoryOpener())
{
}

internal WindowsPhysicalPathResolver(IStrictDirectoryOpener directoryOpener)
{
    _directoryOpener = directoryOpener ?? throw new ArgumentNullException(nameof(directoryOpener));
}
```

Replace the ancestor loop with:

```csharp
public string ResolveWithNearestExistingAncestor(string path)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(path);

    string full = Path.GetFullPath(path);
    var remainder = new Stack<string>();
    string current = full;

    while (true)
    {
        StrictDirectoryOpenResult opened = _directoryOpener.OpenForIdentity(current);

        if (opened.Kind == StrictDirectoryOpenKind.Opened)
        {
            using SafeFileHandle handle = opened.Handle!;
            string resolved = ResolveExistingDirectoryHandle(current, handle);

            while (remainder.Count > 0)
                resolved = Path.Combine(resolved, remainder.Pop());

            return Path.GetFullPath(resolved);
        }

        string trimmed = current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? name = Path.GetFileName(trimmed);

        if (string.IsNullOrEmpty(name))
            throw new DirectoryNotFoundException($"Could not find an accessible existing ancestor for '{full}'.");

        remainder.Push(name);
        string? parent = Path.GetDirectoryName(trimmed);

        if (string.IsNullOrEmpty(parent) || PathIdentity.Equals(parent, current))
            throw new DirectoryNotFoundException($"Could not find an accessible existing ancestor for '{full}'.");

        current = parent;
    }
}
```

Refactor the existing handle-based final-path code into:

```csharp
private static string ResolveExistingDirectoryHandle(string original, SafeFileHandle handle)
```

Do not reopen a path after a handle is already available.

---

# 44. Exact managed control namespace

Add `ManagedControlPathPolicy.cs`.

```csharp
using System;
using System.IO;

namespace PromptHelper.Services;

internal static class ManagedControlPathPolicy
{
    public const string AppLock = ".app.lock";
    public const string SettingsLock = ".settings.lock";
    public const string SettingsPrimary = "settings.json";
    public const string SettingsBackup = "settings.backup.json";
    public const string MigrationManifest = ".prompthelper-migration.json";
    public const string LibraryMutationJournal = ".prompthelper-library-mutation.json";
    public const string InitializationMarker = "initializing.marker";

    public static string NormalizeRelative(string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relative);
        string value = relative
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        if (Path.IsPathFullyQualified(value))
            throw new InvalidDataException($"Expected a relative path: '{relative}'.");

        return value;
    }

    public static bool IsRootControl(string relative, bool targetIsBootstrapRoot)
    {
        string p = NormalizeRelative(relative);

        if (p.Contains(Path.DirectorySeparatorChar))
            return false;

        if (Eq(p, AppLock) || Eq(p, MigrationManifest) || Eq(p, LibraryMutationJournal) || Eq(p, InitializationMarker))
            return true;

        if (targetIsBootstrapRoot &&
            (Eq(p, SettingsLock) || Eq(p, SettingsPrimary) || Eq(p, SettingsBackup)))
            return true;

        return false;
    }

    private static bool Eq(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
```

Do not reject a nested file merely because its basename is `settings.json`, `.app.lock`, or starts with `.prompthelper`.

---

# 45. Canonical migration temp grammar

Add `MigrationTempName.cs`.

Required grammar:

```text
.<final-file-name>.migration-<attemptIdN>-<nonce16lowerhex>.tmp
```

Temp must be in the same parent directory as final.

```csharp
using System;
using System.IO;

namespace PromptHelper.Services;

internal static class MigrationTempName
{
    public static string BuildRelative(string finalRelativePath, Guid attemptId, string nonce16Hex)
    {
        if (nonce16Hex.Length != 16 || nonce16Hex.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))))
            throw new ArgumentException("Migration temp nonce must be exactly 16 lower-case hexadecimal characters.", nameof(nonce16Hex));

        string canonicalFinal = ManagedControlPathPolicy.NormalizeRelative(finalRelativePath);
        string? parent = Path.GetDirectoryName(canonicalFinal);
        string name = Path.GetFileName(canonicalFinal);
        string tempName = $".{name}.migration-{attemptId:N}-{nonce16Hex}.tmp";

        return string.IsNullOrEmpty(parent) ? tempName : Path.Combine(parent, tempName);
    }

    public static void ValidateExactBinding(string finalRelativePath, string tempRelativePath, Guid attemptId)
    {
        string final = ManagedControlPathPolicy.NormalizeRelative(finalRelativePath);
        string temp = ManagedControlPathPolicy.NormalizeRelative(tempRelativePath);
        string? finalParent = Path.GetDirectoryName(final);
        string? tempParent = Path.GetDirectoryName(temp);

        if (!string.Equals(finalParent ?? string.Empty, tempParent ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Migration temp must be in the same directory as its final artifact.");

        string finalName = Path.GetFileName(final);
        string tempName = Path.GetFileName(temp);
        string prefix = $".{finalName}.migration-{attemptId:N}-";
        const string suffix = ".tmp";

        if (!tempName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !tempName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Temp path '{temp}' does not match the required artifact/AttemptId grammar.");

        string nonce = tempName[prefix.Length..^suffix.Length];
        if (nonce.Length != 16 || nonce.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))))
            throw new InvalidDataException($"Temp path '{temp}' has invalid nonce.");
    }
}
```

---

# 46. Migration manifest schema v3

Replace schema 2 with schema 3; do not silently change schema 2 semantics.

```csharp
internal enum MigrationManifestPhase
{
    Copying,
    ReadyToCommit
}

internal sealed class MigrationTargetBaseline
{
    public bool TargetRootExistedBefore { get; set; }
    public bool PromptsDirectoryExistedBefore { get; set; }
    public bool RecoveryDirectoryExistedBefore { get; set; }
    public bool AppLockFileExistedBefore { get; set; }
}

internal sealed class MigrationOwnedControl
{
    public string RelativePath { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
}

internal sealed class MigrationAttemptManifest
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid AttemptId { get; set; }
    public string SourcePhysicalRoot { get; set; } = string.Empty;
    public string TargetPhysicalRoot { get; set; } = string.Empty;
    public bool TargetIsBootstrapRoot { get; set; }
    public string SourceLibrarySha256Hex { get; set; } = string.Empty;
    public string SourcePackageFingerprintSha256Hex { get; set; } = string.Empty;
    public MigrationManifestPhase Phase { get; set; }
    public MigrationTargetBaseline Baseline { get; set; } = new();
    public List<MigrationManifestArtifact> Artifacts { get; set; } = [];
    public List<MigrationOwnedControl> OwnedControls { get; set; } = [];
}
```

The source package fingerprint must cover all migration payload items in stable sorted order. Hash relative path, role, length, and SHA-256 using explicit separators; never hash a representation that differs from what the migration actually copies.

---

# 47. One ownership namespace

The current separate final/temp sets must be replaced with one set.

```csharp
var ownedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

foreach (MigrationManifestArtifact artifact in manifest.Artifacts)
{
    string finalFull = ResolveManifestArtifactPath(manifest.TargetPhysicalRoot, artifact.RelativePath);
    string tempFull = ResolveManifestArtifactPath(manifest.TargetPhysicalRoot, artifact.TempRelativePath);

    if (!ownedPaths.Add(finalFull))
        throw new InvalidDataException($"Duplicate/colliding owned path: '{artifact.RelativePath}'.");

    if (!ownedPaths.Add(tempFull))
        throw new InvalidDataException($"Temp/final ownership collision: '{artifact.TempRelativePath}'.");

    MigrationTempName.ValidateExactBinding(artifact.RelativePath, artifact.TempRelativePath, manifest.AttemptId);
}

foreach (MigrationOwnedControl control in manifest.OwnedControls)
{
    string full = ResolveManifestArtifactPath(manifest.TargetPhysicalRoot, control.RelativePath);
    if (!ownedPaths.Add(full))
        throw new InvalidDataException($"Owned control collides with artifact/temp path: '{control.RelativePath}'.");
}
```

---

# 48. Strict manifest JSON

Validate every manifest object before deserialization.

Allowed root members:

```text
schemaVersion
attemptId
sourcePhysicalRoot
targetPhysicalRoot
targetIsBootstrapRoot
sourceLibrarySha256Hex
sourcePackageFingerprintSha256Hex
phase
baseline
artifacts
ownedControls
```

Allowed baseline members:

```text
targetRootExistedBefore
promptsDirectoryExistedBefore
recoveryDirectoryExistedBefore
appLockFileExistedBefore
```

Allowed artifact members:

```text
relativePath
tempRelativePath
sha256Hex
length
role
```

Allowed owned-control members:

```text
relativePath
purpose
```

Reject duplicate case-insensitive members, unknown members, missing required members, null arrays, undefined enums, invalid UTF-8.

Use strict UTF-8:

```csharp
private static readonly UTF8Encoding StrictUtf8 =
    new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

private static string DecodeStrictUtf8(byte[] bytes, string path)
{
    try
    {
        return StrictUtf8.GetString(bytes);
    }
    catch (DecoderFallbackException ex)
    {
        throw new InvalidDataException($"Invalid UTF-8 in '{path}'.", ex);
    }
}
```

---

# 49. Durable atomic writer

Create one shared implementation used by settings, library metadata, prompt bodies, migration controls, mutation controls, and recovery artifacts.

```csharp
internal interface IDurableAtomicFileWriter
{
    void WriteBytes(string targetPath, ReadOnlySpan<byte> bytes, DurableFileClass fileClass);
    void WriteText(string targetPath, string content, DurableFileClass fileClass);
}

internal enum DurableFileClass
{
    Settings,
    LibraryMetadata,
    PromptBody,
    MigrationControl,
    LibraryMutationControl,
    RecoveryArtifact,
    InitializationControl
}
```

Reference implementation:

```csharp
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PromptHelper.Services;

internal sealed class WindowsDurableAtomicFileWriter : IDurableAtomicFileWriter
{
    private const uint MOVEFILE_REPLACE_EXISTING = 0x00000001;
    private const uint MOVEFILE_WRITE_THROUGH = 0x00000008;
    private static readonly UTF8Encoding Utf8NoBom = new(false, true);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileExW(string lpExistingFileName, string lpNewFileName, uint dwFlags);

    public void WriteText(string targetPath, string content, DurableFileClass fileClass)
    {
        ArgumentNullException.ThrowIfNull(content);
        WriteBytes(targetPath, Utf8NoBom.GetBytes(content), fileClass);
    }

    public void WriteBytes(string targetPath, ReadOnlySpan<byte> bytes, DurableFileClass fileClass)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        string full = Path.GetFullPath(targetPath);
        string? directory = Path.GetDirectoryName(full);

        if (string.IsNullOrEmpty(directory))
            throw new InvalidOperationException($"Target has no parent directory: '{full}'.");

        Directory.CreateDirectory(directory);
        string temp = BuildTempPath(directory, fileClass);
        bool promoted = false;

        try
        {
            using (var stream = new FileStream(
                temp,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                options: FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (!MoveFileExW(temp, full, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Durable promotion failed: '{temp}' -> '{full}'.");

            promoted = true;
        }
        finally
        {
            if (!promoted)
            {
                try { File.Delete(temp); }
                catch { /* exact owned temp is reconciled next startup; preserve original exception */ }
            }
        }
    }

    private static string BuildTempPath(string directory, DurableFileClass fileClass)
    {
        string tag = fileClass switch
        {
            DurableFileClass.Settings => "settings",
            DurableFileClass.LibraryMetadata => "library",
            DurableFileClass.PromptBody => "prompt",
            DurableFileClass.MigrationControl => "migration-control",
            DurableFileClass.LibraryMutationControl => "mutation-control",
            DurableFileClass.RecoveryArtifact => "recovery",
            DurableFileClass.InitializationControl => "initialization",
            _ => throw new ArgumentOutOfRangeException(nameof(fileClass))
        };

        return Path.Combine(directory, $".prompthelper-tmp-{tag}-{Guid.NewGuid():N}.tmp");
    }
}
```

The cleanup in `finally` must not mask the original promotion failure. Exact owned temp names are reconciled later.

---

# 50. Durable temp reconciliation

Add `DurableTempName.cs` and `DurableTempReconciler.cs`.

Only exact names may be auto-deleted:

```text
.prompthelper-tmp-settings-<guidN>.tmp
.prompthelper-tmp-library-<guidN>.tmp
.prompthelper-tmp-prompt-<guidN>.tmp
.prompthelper-tmp-migration-control-<guidN>.tmp
.prompthelper-tmp-mutation-control-<guidN>.tmp
.prompthelper-tmp-recovery-<guidN>.tmp
.prompthelper-tmp-initialization-<guidN>.tmp
```

Never wildcard-delete `*.tmp`.

Bootstrap-root settings temp cleanup occurs while `.settings.lock` is held and before settings load.

At active data root, reconcile exact durable writer temps only after migration/mutation journal conflict checks and managed-tree containment proof.

---

# 51. `MigrationManifestRepository` exact rewrite

Constructor:

```csharp
private readonly IStrictPathAuthority _paths;
private readonly IDurableAtomicFileWriter _writer;

public MigrationManifestRepository(
    IStrictPathAuthority? paths = null,
    IDurableAtomicFileWriter? writer = null)
{
    _paths = paths ?? new StrictPathAuthority();
    _writer = writer ?? new WindowsDurableAtomicFileWriter();
}
```

`TryRead` sequence:

```text
1 strict Probe marker
2 Missing => null
3 Directory => InvalidDataException
4 strict read exact bytes
5 strict UTF-8 decode
6 strict member validation
7 deserialize
8 invariant validation
9 return
```

`WriteDurable`:

```text
1 ValidateManifestInvariants
2 canonical serialize
3 durable writer WriteBytes(markerPath, bytes, MigrationControl)
```

`DeleteDurable`:

```csharp
public void DeleteDurable(string markerPath)
{
    StrictPathEntry state = _paths.Probe(markerPath);
    if (state.IsMissing) return;
    if (!state.IsFile)
        throw new InvalidDataException($"Migration marker path is not a file: '{markerPath}'.");

    File.Delete(markerPath);

    StrictPathEntry after = _paths.Probe(markerPath);
    if (!after.IsMissing)
        throw new IOException($"Migration marker still exists after retirement: '{markerPath}'.");
}
```

---

# 52. Capability probe ownership

Current random `.prompthelper-write-probe-*` directories must not survive a crash without manifest ownership.

Preferred migration plan: predeclare two exact control files per probed directory:

```text
.prompthelper-capability-<attemptIdN>-current.tmp
.prompthelper-capability-<attemptIdN>-replacement.tmp
```

Add them to `manifest.OwnedControls` before creating either file.

Use:

```csharp
internal sealed record CapabilityProbePlan(
    string CurrentRelativePath,
    string ReplacementRelativePath);
```

Factory:

```csharp
internal static CapabilityProbePlan BuildCapabilityProbePlan(Guid attemptId, string parentRelative = "")
{
    string current = $".prompthelper-capability-{attemptId:N}-current.tmp";
    string replacement = $".prompthelper-capability-{attemptId:N}-replacement.tmp";

    if (!string.IsNullOrEmpty(parentRelative))
    {
        current = Path.Combine(parentRelative, current);
        replacement = Path.Combine(parentRelative, replacement);
    }

    return new CapabilityProbePlan(current, replacement);
}
```

For existing-library switch where no migration manifest is created, either make the probe fully self-cleaning with typed cleanup failure before settings commit, or avoid an on-disk crash-surviving probe and use existing-file read/write-open validation plus a root scratch probe whose failure blocks commit.

---

# 53. Existing-file capability validation

For existing libraries, validate actual managed files without modifying them:

```csharp
private static void AssertFileWritable(string path, string description)
{
    FileAttributes attrs = File.GetAttributes(path);

    if ((attrs & FileAttributes.ReadOnly) != 0)
        throw new UnauthorizedAccessException($"{description} is read-only: '{path}'.");

    using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

    if (!stream.CanWrite)
        throw new UnauthorizedAccessException($"{description} is not writable: '{path}'.");
}
```

Validate:

```text
library.json
current library.backup.json when this version is allowed to update it
all active prompt bodies
```

Do not clear read-only attributes.

---

# 54. Reservation ownership commit

Add to `TargetRootReservation`:

```csharp
private bool _rootOwnershipCommitted;

public void CommitRootOwnership()
{
    _rootOwnershipCommitted = true;
}
```

On `Release()`:

```csharp
if (!_rootOwnershipCommitted && _createdDirectories.Count > 0)
{
    CleanupCreatedDirectories(_createdDirectories, _fileOps, failures);
}
```

After successful empty-target settings commit, call:

```csharp
reservation.CommitRootOwnership();
```

before normal release.

This prevents successful migrations from reporting false directory-cleanup failures.

---

# 55. Strict directory creation ownership

Replace check-then-create ownership inference with an API that reports whether this process actually created the directory.

```csharp
internal enum DirectoryCreateResult
{
    CreatedByCaller,
    AlreadyExists
}

internal interface IStrictDirectoryCreator
{
    DirectoryCreateResult CreateOne(string path);
}
```

Windows implementation should call `CreateDirectoryW` directly:

```text
success => CreatedByCaller
ERROR_ALREADY_EXISTS => AlreadyExists
anything else => throw
```

Walk from nearest existing parent downward and record only segments returning `CreatedByCaller`.

---

# 56. Rich empty-target baseline

Replace the current bool-only shape with:

```csharp
internal sealed record EmptyTargetBaselineInspection(
    bool IsAcceptable,
    bool TargetRootExisted,
    bool PromptsDirectoryExisted,
    bool RecoveryDirectoryExisted,
    bool AppLockFileExisted,
    IReadOnlyList<string> UnexpectedEntries);
```

Rules:

```text
- strict enumerate; unreadable is an error, never empty;
- stale unlocked root .app.lock may be allowed after exclusive lock acquisition proves it is not active;
- empty prompts/recovery may be allowed only after topology validation proves they are ordinary in-root directories;
- bootstrap exact may allow settings.json/settings.backup.json/.settings.lock;
- foreign entries => OccupiedNonLibrary.
```

---

# 57. Managed tree topology validator

Add `ManagedTreeTopologyValidator.cs`.

```csharp
internal sealed class ManagedTreeTopologyValidator
{
    private readonly IPhysicalPathResolver _resolver;
    private readonly IStrictPathAuthority _paths;
    private readonly IDirectoryCaseSensitivityInspector _caseInspector;

    public ManagedTreeTopologyValidator(
        IPhysicalPathResolver resolver,
        IStrictPathAuthority paths,
        IDirectoryCaseSensitivityInspector caseInspector)
    {
        _resolver = resolver;
        _paths = paths;
        _caseInspector = caseInspector;
    }

    public void Validate(string physicalRoot)
    {
        ValidateChild(physicalRoot, "prompts");
        ValidateChild(physicalRoot, "recovery");
    }

    private void ValidateChild(string physicalRoot, string childName)
    {
        string lexicalChild = Path.Combine(physicalRoot, childName);
        StrictPathEntry state = _paths.Probe(lexicalChild);

        if (state.IsMissing) return;
        if (!state.IsDirectory)
            throw new InvalidDataException($"Managed path '{lexicalChild}' must be a directory.");

        if ((state.Attributes!.Value & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Managed directory '{lexicalChild}' must not be a reparse point.");

        string resolved = PathIdentity.NormalizeForComparison(
            _resolver.ResolveWithNearestExistingAncestor(lexicalChild));
        string expected = PathIdentity.NormalizeForComparison(lexicalChild);

        if (!PathIdentity.Equals(resolved, expected))
            throw new InvalidDataException($"Managed directory '{lexicalChild}' resolves outside expected physical location ('{resolved}').");

        DirectoryCaseSensitivityState caseState = _caseInspector.Inspect(lexicalChild);
        if (caseState == DirectoryCaseSensitivityState.CaseSensitive)
            throw new InvalidDataException($"Managed directory '{lexicalChild}' uses unsupported Windows per-directory case sensitivity.");
    }
}
```

---

# 58. Long-lived managed tree lease

Add `ManagedDataRootSessionLease.cs` and hold handles to root/prompts/recovery without `FileShare.Delete` for the process lifetime.

```csharp
internal sealed class ManagedDataRootSessionLease : IDisposable
{
    private readonly List<SafeFileHandle> _handles = [];
    private bool _disposed;

    private ManagedDataRootSessionLease() { }

    public static ManagedDataRootSessionLease Acquire(string physicalRoot, IStrictDirectoryOpener? opener = null)
    {
        IStrictDirectoryOpener native = opener ?? new WindowsStrictDirectoryOpener();
        var lease = new ManagedDataRootSessionLease();

        try
        {
            foreach (string path in new[]
                     {
                         physicalRoot,
                         Path.Combine(physicalRoot, "prompts"),
                         Path.Combine(physicalRoot, "recovery")
                     })
            {
                SafeFileHandle handle = native.OpenManagedNodeLease(path);
                if (handle.IsInvalid)
                {
                    handle.Dispose();
                    throw new IOException($"Invalid managed directory lease handle: '{path}'.");
                }
                lease._handles.Add(handle);
            }

            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        for (int i = _handles.Count - 1; i >= 0; i--)
            _handles[i].Dispose();

        _handles.Clear();
    }
}
```

Keep it in an `App` field and dispose in `OnExit`.

---

# 59. Startup sequencing with tree authority

Required conceptual order:

```text
01 settings mutation lease
02 reconcile strict settings temps
03 load/recover strict settings
04 resolve configured lexical root
05 resolve physical configured root
06 resolve physical bootstrap root
07 root safety/case policy
08 acquire .app.lock
09 reconcile journal conflicts
10 migration recovery
11 library-mutation recovery
12 ensure ordinary prompts/recovery directories
13 validate managed child topology
14 acquire long-lived managed-tree lease
15 strict primary metadata read
16 primary package-health inspection
17 backup authority decision if required
18 recover only from complete package
19 synchronize backup only from healthy primary
20 safe orphan reconciliation
21 construct view models and show UI
```

Never show the main window before migration recovery, mutation recovery, package authority, and managed-tree lease succeed.

---

# 60. Library package inspector

Add `LibraryPackageInspector.cs`.

```csharp
internal abstract record LibraryPackageState
{
    public sealed record Healthy(
        LibraryDocument Document,
        IReadOnlyDictionary<Guid, PromptBodySnapshot> Bodies)
        : LibraryPackageState;

    public sealed record BodyMissing(
        LibraryDocument Document,
        Guid PromptId,
        string Path)
        : LibraryPackageState;

    public sealed record BodyUnreadable(
        LibraryDocument Document,
        Guid PromptId,
        string Path,
        Exception Error)
        : LibraryPackageState;
}

internal sealed record PromptBodySnapshot(
    Guid PromptId,
    long Length,
    string Sha256Hex);
```

Implementation:

```csharp
internal sealed class LibraryPackageInspector
{
    private readonly AppPaths _paths;
    private readonly IStrictPathAuthority _strictPaths;

    public LibraryPackageInspector(AppPaths paths, IStrictPathAuthority? strictPaths = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _strictPaths = strictPaths ?? new StrictPathAuthority();
    }

    public LibraryPackageState Inspect(LibraryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        LibraryValidator.Validate(document);
        var snapshots = new Dictionary<Guid, PromptBodySnapshot>();

        foreach (PromptRecord prompt in document.Prompts)
        {
            string path = _paths.GetPromptPath(prompt.Id);
            StrictPathEntry state = _strictPaths.Probe(path);

            if (state.IsMissing)
                return new LibraryPackageState.BodyMissing(LibraryDocumentCloner.Clone(document), prompt.Id, path);

            if (!state.IsFile)
                return new LibraryPackageState.BodyUnreadable(
                    LibraryDocumentCloner.Clone(document),
                    prompt.Id,
                    path,
                    new InvalidDataException($"Prompt body path is not a file: '{path}'."));

            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return new LibraryPackageState.BodyUnreadable(LibraryDocumentCloner.Clone(document), prompt.Id, path, ex);
            }

            snapshots[prompt.Id] = new PromptBodySnapshot(
                prompt.Id,
                bytes.LongLength,
                Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }

        return new LibraryPackageState.Healthy(LibraryDocumentCloner.Clone(document), snapshots);
    }
}
```


---

# 61. Startup authority resolver

Do not embed another long implicit if/else tree in `LibraryStartupService`. Add `LibraryStartupAuthorityResolver.cs`.

```csharp
internal abstract record LibraryMetadataAuthority
{
    public sealed record Missing : LibraryMetadataAuthority;
    public sealed record Current(LibraryDocument Document) : LibraryMetadataAuthority;
    public sealed record Corrupt(Exception Error, string? RawText) : LibraryMetadataAuthority;
    public sealed record Future(int Version) : LibraryMetadataAuthority;
    public sealed record Unreadable(Exception Error) : LibraryMetadataAuthority;
}

internal enum LibraryStartupAction
{
    UsePrimary,
    RecoverPrimaryFromBackup,
    InitializeDefaults,
    Stop
}

internal sealed record LibraryStartupDecision(
    LibraryStartupAction Action,
    LibraryDocument? Document,
    string? Warning,
    Exception? Error);
```

Mandatory decision table:

| Primary metadata/package | Backup metadata/package | Required action |
|---|---|---|
| Future | anything | STOP; never downgrade |
| Unreadable | anything | STOP; never fallback |
| Current + Healthy | any non-future | USE PRIMARY; backup synchronization may occur **after** package health |
| Current + Incomplete | Current + Healthy | RECOVER FROM BACKUP; preserve damaged primary diagnostic |
| Current + Incomplete | anything else | STOP; do not overwrite backup |
| Corrupt/Missing | Current + Healthy | RECOVER FROM BACKUP |
| Corrupt/Missing | Current + Incomplete | STOP |
| Corrupt/Missing | Future | STOP future-schema |
| Corrupt/Missing | Unreadable | STOP |
| Missing | Missing | first-run logic only after foreign-file checks |

The weak model must encode this decision table in tests before changing startup implementation.

---

# 62. Make backup synchronization type-safe

Prevent future accidental calls that synchronize backup from metadata-only state.

Add:

```csharp
internal sealed record HealthyLibraryPackage(
    LibraryDocument Document,
    IReadOnlyDictionary<Guid, PromptBodySnapshot> Bodies);
```

Change the startup-facing API from:

```csharp
_libraryRepo.SynchronizeBackup(document);
```

to:

```csharp
_libraryRepo.SynchronizeBackup(package);
```

where only `LibraryPackageInspector` can produce `HealthyLibraryPackage`.

Do **not** keep a public/internal overload accepting bare `LibraryDocument` for startup use. If repository-internal tests need metadata-only synchronization, name that test-only helper explicitly and keep it `internal`.

---

# 63. `LibraryStartupService` exact rewrite outline

Use this structure:

```csharp
public StartupResult LoadOrInitialize()
{
    _paths.EnsureDataDirectories();

    LibraryMetadataAuthority primary = ReadMetadataAuthority(_paths.LibraryPath);

    if (primary is LibraryMetadataAuthority.Future pf)
        throw new UnsupportedLibrarySchemaException(pf.Version);

    if (primary is LibraryMetadataAuthority.Unreadable pu)
        throw new IOException("Primary library metadata is unreadable.", pu.Error);

    if (primary is LibraryMetadataAuthority.Current pc)
    {
        LibraryPackageState primaryPackage = _packageInspector.Inspect(pc.Document);

        if (primaryPackage is LibraryPackageState.Healthy healthy)
        {
            var healthyPackage = new HealthyLibraryPackage(healthy.Document, healthy.Bodies);
            CommitResult sync = _libraryRepo.SynchronizeBackup(healthyPackage);
            TryRemoveStaleInitializationMarker();
            return new StartupResult(
                healthy.Document,
                RecoveredFromBackup: false,
                sync.Warning);
        }

        // Primary metadata is structurally current but package incomplete.
        LibraryMetadataAuthority backup = ReadMetadataAuthority(_paths.LibraryBackupPath);
        return RecoverFromBackupOrStop(primary, primaryPackage, backup);
    }

    LibraryMetadataAuthority backupForRecovery = ReadMetadataAuthority(_paths.LibraryBackupPath);
    return RecoverMissingOrCorruptPrimary(primary, backupForRecovery);
}
```

`RecoverFromBackupOrStop` must call package inspection on backup before writing primary.

On incomplete primary + healthy backup:

```text
1 optional best-effort diagnostic copy of damaged primary metadata
2 durable commit backup document to primary
3 package re-inspection of new primary
4 only then report RecoveredFromBackup=true
```

On incomplete backup:

```text
throw InvalidDataException
preserve backup bytes
preserve primary bytes
```

---

# 64. Canonical library serialization bytes

The mutation journal must hash the exact bytes actually written by `LibraryRepository`.

Add:

```csharp
internal static readonly UTF8Encoding CanonicalUtf8 = new(false, true);

internal static byte[] SerializeCanonicalBytes(LibraryDocument document)
{
    LibraryValidator.Validate(document);
    string json = JsonSerializer.Serialize(document, JsonOptions);
    return CanonicalUtf8.GetBytes(json);
}
```

`Commit` must write those exact bytes via `IDurableAtomicFileWriter`.

Do not hash one serialization and write another.

---

# 65. Durable `PromptRepository`

Change repository constructor to use `IDurableAtomicFileWriter`.

Required API:

```csharp
public string Read(Guid id);
internal StrictPathEntry InspectBody(Guid id);
public void CreateDurable(Guid id, string content);
public void UpdateDurable(Guid id, string content);
public void DeleteIfExists(Guid id);
public IReadOnlyList<string> EnumeratePromptFilesStrict();
```

Reference methods:

```csharp
public void CreateDurable(Guid id, string content)
{
    string path = _paths.GetPromptPath(id);
    StrictPathEntry state = _strictPaths.Probe(path);

    if (!state.IsMissing)
        throw new InvalidOperationException($"Prompt path is already occupied: '{path}'.");

    _writer.WriteText(path, content, DurableFileClass.PromptBody);
}

public void UpdateDurable(Guid id, string content)
{
    string path = _paths.GetPromptPath(id);
    StrictPathEntry state = _strictPaths.Probe(path);

    if (state.IsMissing)
        throw new FileNotFoundException("Prompt file does not exist.", path);

    if (!state.IsFile)
        throw new InvalidDataException($"Prompt path is not a file: '{path}'.");

    _writer.WriteText(path, content, DurableFileClass.PromptBody);
}
```

`GenerateUniquePromptGuid` must classify candidate body path strictly. Unreadable means **not available** and must abort instead of choosing the ID.

---

# 66. Library mutation journal model

Add `LibraryMutationJournal.cs`.

Use the preferred phase set including recovery-copy durability:

```csharp
internal enum LibraryMutationKind
{
    CreatePrompt,
    EditPrompt,
    DuplicatePrompt,
    DeletePrompt
}

internal enum LibraryMutationPhase
{
    Prepared,
    RecoveryBodyDurable,
    BodyDurable,
    MetadataDurable,
    BodyDeleted
}

internal sealed class LibraryMutationJournal
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid OperationId { get; set; }
    public LibraryMutationKind Kind { get; set; }
    public LibraryMutationPhase Phase { get; set; }
    public Guid PromptId { get; set; }
    public string BodyRelativePath { get; set; } = string.Empty;

    public string OldLibrarySha256Hex { get; set; } = string.Empty;
    public string NewLibrarySha256Hex { get; set; } = string.Empty;

    public long? OldBodyLength { get; set; }
    public string? OldBodySha256Hex { get; set; }
    public long? NewBodyLength { get; set; }
    public string? NewBodySha256Hex { get; set; }

    public string? RecoveryBodyRelativePath { get; set; }
}
```

In `AppPaths`:

```csharp
public string LibraryMutationJournalPath =>
    Path.Combine(RootDirectory, ".prompthelper-library-mutation.json");
```

---

# 67. Mutation journal validation

Exact invariant rules:

```text
SchemaVersion == 1
OperationId != Guid.Empty
PromptId != Guid.Empty
BodyRelativePath == prompts\<PromptId:N>.md exactly
OldLibrarySha256Hex = 64 hex
NewLibrarySha256Hex = 64 hex
OldLibraryHash != NewLibraryHash for operations that change metadata
Kind defined
Phase defined
```

For Create/Duplicate:

```text
OldBodyLength == null
OldBodySha256Hex == null
NewBodyLength >= 0
NewBodySha256Hex valid
RecoveryBodyRelativePath == null
```

For Edit:

```text
OldBodyLength >= 0
OldBodySha256Hex valid
NewBodyLength >= 0
NewBodySha256Hex valid
RecoveryBodyRelativePath == recovery\mutation-<OperationIdN>-old-<PromptIdN>.md exactly
```

For Delete:

```text
Old body hash/length required
New body fields null
RecoveryBodyRelativePath null
```

Reject unknown/duplicate JSON members and invalid UTF-8 exactly like other authority documents.

---

# 68. Mutation journal repository

Add `LibraryMutationJournalRepository.cs` using strict path + durable writer.

Required API:

```csharp
internal sealed class LibraryMutationJournalRepository
{
    public LibraryMutationJournal? TryReadStrict();
    public void WritePreparedDurable(LibraryMutationJournal journal);
    public void AdvanceDurable(LibraryMutationJournal journal, LibraryMutationPhase phase);
    public void DeleteDurable();
}
```

Monotonic phase rules:

```csharp
private static bool IsValidTransition(LibraryMutationPhase from, LibraryMutationPhase to) =>
    (from, to) switch
    {
        (LibraryMutationPhase.Prepared, LibraryMutationPhase.RecoveryBodyDurable) => true, // Edit
        (LibraryMutationPhase.Prepared, LibraryMutationPhase.BodyDurable) => true,         // Create/Duplicate
        (LibraryMutationPhase.Prepared, LibraryMutationPhase.MetadataDurable) => true,     // Delete
        (LibraryMutationPhase.RecoveryBodyDurable, LibraryMutationPhase.BodyDurable) => true,
        (LibraryMutationPhase.BodyDurable, LibraryMutationPhase.MetadataDurable) => true,
        (LibraryMutationPhase.MetadataDurable, LibraryMutationPhase.BodyDeleted) => true,
        _ => false
    };
```

`AdvanceDurable` mutates a clone, validates, durably writes, then copies phase back to caller object only after success.

Do not change the in-memory phase before durable write succeeds.

---

# 69. Mutation hash helper

```csharp
internal static class MutationHash
{
    public static string Sha256Hex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static bool Matches(byte[] bytes, long? expectedLength, string? expectedSha)
    {
        if (expectedLength is null || expectedSha is null)
            return false;

        return bytes.LongLength == expectedLength.Value &&
               string.Equals(Sha256Hex(bytes), expectedSha, StringComparison.OrdinalIgnoreCase);
    }
}
```

Never compare only length.

---

# 70. Create transaction exact sequence

`PromptLibraryService.CreatePrompt` must delegate multi-file persistence to a coordinator instead of directly writing body then metadata.

Create `PromptMutationCoordinator.cs`.

Create sequence:

```text
1 validate category/title/content
2 clone current document
3 generate strictly unique GUID
4 add prompt to candidate
5 validate candidate
6 oldLibraryBytes = canonical current
7 newLibraryBytes = canonical candidate
8 bodyBytes = UTF-8 exact body
9 construct Prepared journal
10 durable write journal
11 durable create body
12 journal -> BodyDurable
13 durable commit primary library
14 journal -> MetadataDurable
15 synchronize backup using current backup authority rules
16 delete journal durably
17 only now publish `_document = candidate`
18 return warning/result
```

A crash after step 11 must be recoverable without any `catch` executing.

---

# 71. Duplicate transaction

Do **not** write a separate transaction state machine.

`DuplicatePrompt`:

```text
read source body strictly
validate destination category
call same Create transaction with copied content/title and new GUID
```

Required assertion:

```text
Create and Duplicate both call one coordinator method
```

---

# 72. Edit transaction exact sequence

Edit must durably preserve old body before replacing it.

Sequence:

```text
1 locate prompt in metadata
2 strict read old body bytes
3 clone current document
4 apply title to candidate
5 validate candidate
6 compute exact old/new library hashes
7 compute exact old/new body hashes
8 create Prepared journal naming recovery copy
9 durable write Prepared journal
10 durable write old body to recovery\mutation-<opN>-old-<promptN>.md
11 journal -> RecoveryBodyDurable
12 durable replace active prompt body with new content
13 journal -> BodyDurable
14 durable commit candidate library metadata
15 journal -> MetadataDurable
16 synchronize backup
17 verify recovery copy still matches old body
18 delete recovery copy
19 delete journal
20 only then publish in-memory candidate
```

The recovery copy path is a transaction-owned recovery artifact; do not use a random unparseable filename.

---

# 73. Delete transaction exact sequence

Safe data-preserving order:

```text
1 verify prompt metadata + old body
2 candidate = metadata without prompt
3 compute old/new metadata hashes and old body hash
4 durable Prepared journal
5 durable commit candidate metadata
6 journal -> MetadataDurable
7 synchronize backup
8 if backup synchronized and no other authority references prompt:
      verified delete body
      journal -> BodyDeleted
   else:
      preserve body as conservative orphan and return warning
9 delete journal
10 publish in-memory candidate
```

Do not restore metadata merely because body deletion failed after metadata commit.

---

# 74. Mutation content classifier

```csharp
internal enum MutationContentState
{
    Missing,
    Old,
    New,
    Other
}

internal static MutationContentState Classify(
    StrictPathEntry entry,
    Func<byte[]> read,
    long? oldLength,
    string? oldSha,
    long? newLength,
    string? newSha)
{
    if (entry.IsMissing)
        return MutationContentState.Missing;

    if (!entry.IsFile)
        return MutationContentState.Other;

    byte[] bytes = read();

    if (oldLength is not null && oldSha is not null && MutationHash.Matches(bytes, oldLength, oldSha))
        return MutationContentState.Old;

    if (newLength is not null && newSha is not null && MutationHash.Matches(bytes, newLength, newSha))
        return MutationContentState.New;

    return MutationContentState.Other;
}
```

Unreadable input should throw before classification, not become `Other`.

---

# 75. Create/Duplicate crash recovery matrix

| Primary metadata | Body | Journal | Action |
|---|---|---|---|
| old | missing | Prepared | delete journal; operation never started |
| old | new exact | BodyDurable | verified delete new body; delete journal |
| new | new exact | BodyDurable/MetadataDurable | commit completed; finalize backup as safe; delete journal |
| old/new | unexpected bytes | any | STOP; keep journal |
| neither old nor new hash | any | any | STOP; keep journal |
| unreadable metadata/body | any | any | STOP; keep journal |

Do not use phase alone; verify actual durable hashes.

---

# 76. Edit crash recovery matrix

| Primary | Body | Recovery copy | Required action |
|---|---|---|---|
| old | old | missing/old | cleanup journal/recovery; old state authoritative |
| old | new | exact old | restore old body from recovery; cleanup |
| new | new | exact old | new state authoritative; remove recovery copy + journal |
| new | old | exact old | inconsistent; STOP |
| old/new | Other | any | STOP |
| any | unreadable | any | STOP |
| any | any | Other recovery bytes | STOP |

After restoring old body, verify hash before deleting recovery copy/journal.

---

# 77. Delete crash recovery matrix

| Primary | Body | Backup authority | Action |
|---|---|---|---|
| old | old | any | delete journal; delete never committed |
| new | old | backup current and does not reference prompt | verified delete body; retire journal |
| new | old | backup future/unreadable/stale references prompt | preserve body; retire journal with warning/orphan state |
| new | missing | any | deletion completed; retire journal |
| neither old nor new | any | any | STOP |
| unexpected body bytes | any | any | STOP |

Never delete a body while a future/unreadable metadata authority might still require it.

---

# 78. `LibraryMutationRecoveryService` skeleton

```csharp
internal sealed class LibraryMutationRecoveryService
{
    private readonly AppPaths _paths;
    private readonly LibraryMutationJournalRepository _journalRepo;
    private readonly IStrictPathAuthority _strictPaths;
    private readonly IDurableAtomicFileWriter _writer;
    private readonly IVerifiedArtifactDeleter _verifiedDeleter;

    public void RecoverIfPresent()
    {
        LibraryMutationJournal? journal = _journalRepo.TryReadStrict();
        if (journal is null) return;

        switch (journal.Kind)
        {
            case LibraryMutationKind.CreatePrompt:
            case LibraryMutationKind.DuplicatePrompt:
                RecoverCreateLike(journal);
                break;
            case LibraryMutationKind.EditPrompt:
                RecoverEdit(journal);
                break;
            case LibraryMutationKind.DeletePrompt:
                RecoverDelete(journal);
                break;
            default:
                throw new InvalidDataException($"Unsupported mutation kind: {journal.Kind}.");
        }
    }

    private void RecoverCreateLike(LibraryMutationJournal journal)
    {
        byte[] primary = ReadRequiredPrimaryBytes();
        string primaryHash = MutationHash.Sha256Hex(primary);
        string bodyPath = Path.Combine(_paths.RootDirectory, journal.BodyRelativePath);
        StrictPathEntry bodyState = _strictPaths.Probe(bodyPath);

        bool primaryOld = string.Equals(primaryHash, journal.OldLibrarySha256Hex, StringComparison.OrdinalIgnoreCase);
        bool primaryNew = string.Equals(primaryHash, journal.NewLibrarySha256Hex, StringComparison.OrdinalIgnoreCase);

        if (!primaryOld && !primaryNew)
            throw new InvalidDataException("library.json matches neither old nor new mutation state.");

        if (primaryNew)
        {
            if (bodyState.IsMissing)
                throw new InvalidDataException("Committed metadata references a missing mutation body.");

            byte[] body = _strictPaths.ReadAllBytesRequired(bodyPath);
            if (!MutationHash.Matches(body, journal.NewBodyLength, journal.NewBodySha256Hex))
                throw new InvalidDataException("Committed metadata references an unexpected mutation body.");

            _journalRepo.DeleteDurable();
            return;
        }

        // primary old
        if (bodyState.IsMissing)
        {
            _journalRepo.DeleteDurable();
            return;
        }

        byte[] orphan = _strictPaths.ReadAllBytesRequired(bodyPath);
        if (!MutationHash.Matches(orphan, journal.NewBodyLength, journal.NewBodySha256Hex))
            throw new InvalidDataException("Unexpected body exists during create recovery.");

        _verifiedDeleter.VerifyAndDelete(bodyPath, journal.NewBodyLength!.Value, journal.NewBodySha256Hex!);
        _journalRepo.DeleteDurable();
    }

    private byte[] ReadRequiredPrimaryBytes()
    {
        StrictPathEntry state = _strictPaths.Probe(_paths.LibraryPath);
        if (state.IsMissing)
            throw new InvalidDataException("library.json is missing during mutation recovery.");
        if (!state.IsFile)
            throw new InvalidDataException("library.json is not a file during mutation recovery.");
        return _strictPaths.ReadAllBytesRequired(_paths.LibraryPath);
    }

    // RecoverEdit and RecoverDelete must implement sections 76 and 77 exactly.
}
```

Do not retire a journal while an unexpected hash exists.

---

# 79. Journal conflict policy

Before recovery, classify these root controls strictly:

```text
.prompthelper-migration.json
.prompthelper-library-mutation.json
initializing.marker
```

Allowed: zero or exactly one.

Disallowed combinations:

```text
migration + mutation
migration + initialization
mutation + initialization
all three
```

On conflict:

```text
STOP
show exact paths
do not delete any marker
```

Add tests for all pairwise conflicts.

---

# 80. Safe orphan reconciler

Add `PromptOrphanReconciler.cs`.

Run only after:

```text
migration recovery complete
mutation recovery complete
primary package Healthy
backup synchronized/readable/current
no active journal
```

Algorithm:

```csharp
internal sealed record OrphanReconciliationAuthority(
    LibraryDocument Primary,
    LibraryDocument Backup);

public OrphanReconciliationResult Reconcile(OrphanReconciliationAuthority authority)
{
    var protectedIds = new HashSet<Guid>(authority.Primary.Prompts.Select(p => p.Id));
    protectedIds.UnionWith(authority.Backup.Prompts.Select(p => p.Id));

    var deleted = new List<string>();
    var preserved = new List<string>();

    foreach (string file in _promptRepo.EnumeratePromptFilesStrict())
    {
        string stem = Path.GetFileNameWithoutExtension(file);

        if (!Guid.TryParseExact(stem, "N", out Guid id))
        {
            preserved.Add(file);
            continue;
        }

        if (protectedIds.Contains(id))
        {
            preserved.Add(file);
            continue;
        }

        _managedTreeArtifactValidator.AssertOrdinaryFileUnderPrompts(file);
        File.Delete(file);
        deleted.Add(file);
    }

    return new OrphanReconciliationResult(deleted, preserved, null);
}
```

If backup is Future, Unreadable, or could not be synchronized from healthy primary, preserve all orphans.

---

# 81. Strict JSON authority helper

Add `StrictJsonObjectAuthority.cs`.

```csharp
internal static class StrictJsonObjectAuthority
{
    public static void ValidateExactObject(
        JsonElement element,
        IEnumerable<string> allowedMembers,
        IEnumerable<string> requiredMembers,
        string description)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{description} must be a JSON object.");

        var allowed = new HashSet<string>(allowedMembers, StringComparer.OrdinalIgnoreCase);
        var required = new HashSet<string>(requiredMembers, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw new InvalidDataException($"{description} contains duplicate property '{property.Name}'.");

            if (!allowed.Contains(property.Name))
                throw new InvalidDataException($"{description} contains unknown property '{property.Name}'.");
        }

        foreach (string requiredName in required)
        {
            if (!seen.Contains(requiredName))
                throw new InvalidDataException($"{description} is missing required property '{requiredName}'.");
        }
    }
}
```

Apply before deserialization to settings, library, migration manifest, and mutation journal.

Settings root allowed:

```text
schemaVersion
dataRootPath
```

Library root:

```text
schemaVersion
categories
prompts
```

Category:

```text
id
parentId
name
sortOrder
```

Prompt:

```text
id
categoryId
sortOrder
title
```

---

# 82. Title/headline limit

Add to `LibraryValidator`:

```csharp
public const int MaximumPromptTitleTextElements = 160;

public static string? ValidatePromptTitleInput(string? title)
{
    string trimmed = (title ?? string.Empty).Trim();
    if (trimmed.Length == 0) return null;

    if (TextUtilities.ContainsForbiddenSingleLineCharacter(trimmed))
        return "Headline cannot contain line breaks, tabs, or other control characters.";

    if (TextUtilities.GetTextElementCount(trimmed) > MaximumPromptTitleTextElements)
        return $"Headline cannot exceed {MaximumPromptTitleTextElements} characters.";

    return null;
}
```

Do not use WPF `MaxLength=160`; that counts UTF-16 code units instead of text elements.

The editor should validate on Save and keep the dialog open with inline error.

---

# 83. Narrow `GetPrompts()` exception filter

Replace broad:

```csharp
catch (Exception ex)
```

with:

```csharp
catch (Exception ex) when (
    ex is IOException or
    UnauthorizedAccessException or
    System.Security.SecurityException)
{
    loadError = ex.Message;
}
```

Programming faults must propagate.

---

# 84. Settings durable writer and temp reconciliation

Replace settings repository writer with `IDurableAtomicFileWriter`.

Primary:

```csharp
_writer.WriteText(_settingsPath, json, DurableFileClass.Settings);
```

Backup:

```csharp
_writer.WriteText(_backupPath, json, DurableFileClass.Settings);
```

Before any settings load, under `.settings.lock`:

```text
remove only exact `.prompthelper-tmp-settings-<guidN>.tmp`
preserve similar foreign files
```

`LoadForTransitionAndCapturePrecondition()` order:

```text
1 acquire settings lease
2 reconcile settings temps
3 LoadOrRecoverCore (may write)
4 capture primary token
5 capture backup token
6 return snapshot
```

Never capture precondition before a recovery/sync call that can mutate settings.

---

# 85. Physical settings-root comparison

Persisted settings may use a junction/symlink alias while active startup root is physical. Compare physical identities.

```csharp
SettingsTransitionSnapshot snapshot = _settingsRepo.LoadForTransitionAndCapturePrecondition();
string lexicalSettingsRoot = _settingsRepo.GetEffectiveDataRoot(snapshot.Settings);
string physicalSettingsRoot = _rootPolicy.ValidateConfiguredRootForStartup(
    lexicalSettingsRoot,
    bootstrapPhysicalRoot);

if (!PathIdentity.Equals(physicalSettingsRoot, _activeCurrentRoot))
    throw new InvalidOperationException("The persisted data-folder setting no longer resolves to the active data folder.");
```

Do not automatically rewrite the user's lexical alias just to make the comparison pass.

---

# 86. Bound target revalidation

Use one helper after reservation and immediately before settings commit:

```csharp
private void AssertBoundTargetStillValid(
    string activePhysicalRoot,
    BoundTargetRoot bound,
    string bootstrapPhysicalRoot)
{
    DataRootRelationship relationship = _rootPolicy.ValidateTransition(
        activePhysicalRoot,
        bound.LocatorPath,
        bootstrapPhysicalRoot);

    if (!PathIdentity.Equals(relationship.PhysicalTarget, bound.PhysicalRoot))
        throw new InvalidOperationException("The selected target changed physical identity during the transition.");

    _managedTreeValidator.Validate(bound.PhysicalRoot);
}
```

Call it:

```text
after reservation acquired
after copy/probe before Ready
immediately before settings SaveIfUnchanged
```

---

# 87. `MigrationReadyGate`

Only this class may set `ReadyToCommit`.

Checks:

```text
all final artifacts present and exact length/hash
all declared temps absent
all owned controls that should be retired absent
no foreign entries
physical target identity unchanged
managed-tree topology valid
source package fingerprint unchanged
baseline invariants preserved
```

Then:

```csharp
manifest.Phase = MigrationManifestPhase.ReadyToCommit;
_manifestRepo.WriteDurable(markerPath, manifest);
```

No other caller may assign `ReadyToCommit`.

---

# 88. Source revalidation before Ready

Immediately before Ready:

```text
re-enumerate eligible source payload
rehash exact current source items
recompute package fingerprint
compare to manifest.SourcePackageFingerprintSha256Hex
```

Mismatch => rollback target; do not Ready.

This test must change a prompt body after copy but before Ready and expect a safe abort.

---

# 89. Retry source identity

Extend `MigrationRecoveryContext`:

```csharp
internal sealed record MigrationRecoveryContext(
    string TargetPhysicalRoot,
    string BootstrapPhysicalRoot,
    string? ExpectedSourcePhysicalRoot = null,
    string? ExpectedSourcePackageFingerprint = null,
    IReadOnlyList<string>? AllowedPersistentRelativePaths = null);
```

For user retry from an active source, source physical root and package fingerprint are mandatory and must match manifest. If they do not match, recovery must preserve target and return/throw a typed failure.

---

# 90. Recovery terminal verification

Before deleting migration marker after retry cleanup, prove:

```text
all manifest finals absent
all manifest temps absent
all owned controls absent
no new unknown entries
all allowed baseline files byte-identical
pre-existing prompts/recovery dirs still exist if baseline says they existed
attempt-created empty dirs removed if baseline says they did not exist
```

Only then retire marker.

---

# 91. Typed recovery boundary

Preferred API:

```csharp
public void RecoverForRetryOrThrow(MigrationRecoveryContext context);
public void FinalizeCommittedStartupOrThrow(MigrationRecoveryContext context);
```

with:

```csharp
internal sealed class MigrationRecoveryException : IOException
{
    public MigrationRecoveryException(string message, Exception? inner = null)
        : base(message, inner) { }
}
```

Do not mix raw exceptions and `RecoveryResult(false)` unpredictably.

---

# 92. Baseline directory ownership in recovery

Manifest v3 baseline controls directory cleanup:

```csharp
if (!manifest.Baseline.PromptsDirectoryExistedBefore)
    DeleteIfEmptyStrict(Path.Combine(target, "prompts"));

if (!manifest.Baseline.RecoveryDirectoryExistedBefore)
    DeleteIfEmptyStrict(Path.Combine(target, "recovery"));
```

If they existed before, leave them even when empty.

---

# 93. Root-control inventory must use relative path, not basename

Correct:

```csharp
string rel = NormalizeRel(Path.GetRelativePath(targetRoot, file));

if (string.Equals(rel, ".prompthelper-migration.json", StringComparison.OrdinalIgnoreCase))
    continue;

if (string.Equals(rel, ".app.lock", StringComparison.OrdinalIgnoreCase))
    continue;
```

Incorrect:

```csharp
if (Path.GetFileName(file) == ".app.lock") continue;
```

because `prompts\.app.lock` is not the root lock.

---

# 94. Remove optional `declaredTempMap`

`CopySnapshotToTarget` must require a complete manifest.

Replace optional parameter/fallback with:

```csharp
internal void CopySnapshotToTarget(
    string currentRoot,
    string targetRoot,
    MigrationPayloadSnapshot snapshot,
    MigrationAttemptManifest manifest,
    MigrationTargetTransaction tx)
```

Build lookup once:

```csharp
var artifactsByPath = manifest.Artifacts.ToDictionary(
    a => a.RelativePath,
    StringComparer.OrdinalIgnoreCase);
```

For each source payload item:

```csharp
if (!artifactsByPath.TryGetValue(item.RelativePath, out MigrationManifestArtifact? declared))
    throw new InvalidDataException($"Payload item has no declared manifest artifact: '{item.RelativePath}'.");

string tempPath = MigrationManifestRepository.ResolveManifestArtifactPath(targetRoot, declared.TempRelativePath);
```

No random temp fallback.

---

# 95. Rollback-safe copy pattern

For each artifact:

```text
1 strict final absent
2 strict temp absent
3 open source
4 CreateNew declared temp
5 stream copy
6 Flush(true)
7 move temp -> final, no-overwrite + write-through
8 verify final exact hash/length
```

Because final and temp were manifest-owned before creation, crash recovery has authority even if the in-memory transaction object never tracks the path.

---

# 96. Recovery-safe deletion

Final artifact deletion:

```text
manifest ownership
strict path state
managed-tree containment
exact length/hash
open exact object handle
handle-bound deletion
```

Declared temp deletion:

```text
manifest ownership
AttemptId-bound exact temp grammar
strict path state
managed-tree containment
```

If an owned path has wrong object type (directory instead of file), STOP rather than deleting recursively.

---

# 97. Initialization marker hardening

Keep first-run initialization narrow in scope; no need to merge with mutation journal if not necessary.

Required order:

```text
durable initialization marker
durable default bodies
durable primary metadata
durable backup sync
retire marker
```

Interrupted initialization recovery must use strict path state and exact default body content/hash. Unknown files stop recovery.

---

# 98. `SettingsDialog` future-schema boundaries

Catch exact schema exceptions before generic recoverable filter:

```csharp
catch (UnsupportedLibrarySchemaException ex)
{
    MessageBox.Show(
        this,
        $"The selected folder uses a newer Prompt Helper library schema ({ex.SchemaVersion}).\r\n\r\nThis version will not modify it.",
        "Newer Library Version",
        MessageBoxButton.OK,
        MessageBoxImage.Warning);
    return;
}
catch (UnsupportedSettingsSchemaException ex)
{
    MessageBox.Show(
        this,
        $"Prompt Helper settings use a newer schema ({ex.SchemaVersion}). Nothing was changed by this dialog.",
        "Newer Settings Version",
        MessageBoxButton.OK,
        MessageBoxImage.Warning);
    return;
}
```

Do not add `catch (Exception)`.

---

# 99. Monotonic post-commit shutdown

Once a data-root change commits:

```text
RestartRequired=true is irreversible
```

MainWindow must request shutdown even when ShowDialog result is null/false or the information message throws.

```csharp
private void SettingsButton_Click(object sender, RoutedEventArgs e)
{
    var dialog = new SettingsDialog(_viewModel.DataFolderPath, _settingsRepo, _migrationService)
    {
        Owner = this
    };

    try
    {
        _ = dialog.ShowDialog();
    }
    finally
    {
        if (dialog.RestartRequired)
        {
            try
            {
                MessageBox.Show(
                    this,
                    "Data folder changed.\r\n\r\nPrompt Helper must close now. Open it again to use the new data folder.",
                    "Restart Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            finally
            {
                _applicationLifetime.RequestShutdown();
            }
        }
    }
}
```

Do not gate shutdown on `DialogResult == true`.

---

# 100. Configured-root unavailable diagnostic

Add a dedicated physical-unavailable exception and map resolver failures to it without fallback.

```csharp
internal sealed class PhysicalDataRootUnavailableException : IOException
{
    public string ConfiguredPath { get; }

    public PhysicalDataRootUnavailableException(string configuredPath, Exception inner)
        : base($"Configured data folder is unavailable: '{configuredPath}'.", inner)
    {
        ConfiguredPath = configuredPath;
    }
}
```

User message:

```text
Configured data folder is unavailable.
Prompt Helper did not create or modify another library.
Reconnect the drive/network location or repair settings.
```

Never silently create/use the default library instead.


---

# 101. Remove legacy boolean case-sensitivity API

Change:

```csharp
public interface IDirectoryCaseSensitivityInspector
{
    DirectoryCaseSensitivityState Inspect(string existingDirectory);
}
```

Delete any `IsCaseSensitive(...)` member. Allow compile errors to reveal stale usage.

Do not leave a convenience wrapper that returns `false` when directory inspection fails.

---

# 102. Test fixture: deterministic strict-path fake

Add `tests/PromptHelper.Tests/FakeStrictPathAuthority.cs`.

```csharp
internal sealed class FakeStrictPathAuthority : IStrictPathAuthority
{
    public Dictionary<string, StrictPathEntry> States { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, byte[]> Bytes { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, IReadOnlyList<string>> Entries { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Exception? ProbeException { get; set; }

    public StrictPathEntry Probe(string path)
    {
        if (ProbeException is not null)
            throw ProbeException;

        string full = Path.GetFullPath(path);
        return States.TryGetValue(full, out StrictPathEntry? state)
            ? state
            : new StrictPathEntry(StrictPathEntryKind.Missing, null);
    }

    public byte[] ReadAllBytesRequired(string path)
    {
        string full = Path.GetFullPath(path);
        if (Bytes.TryGetValue(full, out byte[]? bytes))
            return bytes.ToArray();

        throw new FileNotFoundException(null, full);
    }

    public IReadOnlyList<string> EnumerateEntriesRequired(string directory)
    {
        string full = Path.GetFullPath(directory);
        if (Entries.TryGetValue(full, out IReadOnlyList<string>? entries))
            return entries;
        return [];
    }

    public IReadOnlyList<string> EnumerateFilesRequired(string directory, string searchPattern = "*")
    {
        return EnumerateEntriesRequired(directory)
            .Where(File.Exists)
            .Where(p => searchPattern == "*" || Path.GetFileName(p).EndsWith(searchPattern.TrimStart('*'), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
```

The fake's `File.Exists` use is test-fixture convenience only; do not copy it into production authority code.

---

# 103. Test fixture: fault-injecting durable writer

```csharp
internal sealed class FaultInjectingDurableWriter : IDurableAtomicFileWriter
{
    private readonly IDurableAtomicFileWriter _inner;

    public Func<string, DurableFileClass, int, bool>? ShouldFail { get; set; }
    public List<(string Path, DurableFileClass Class, int Call)> Writes { get; } = [];
    private int _callCount;

    public FaultInjectingDurableWriter(IDurableAtomicFileWriter inner)
    {
        _inner = inner;
    }

    public void WriteBytes(string targetPath, ReadOnlySpan<byte> bytes, DurableFileClass fileClass)
    {
        int call = ++_callCount;
        Writes.Add((targetPath, fileClass, call));

        if (ShouldFail?.Invoke(targetPath, fileClass, call) == true)
            throw new IOException($"Injected durable write failure #{call}: '{targetPath}'.");

        _inner.WriteBytes(targetPath, bytes, fileClass);
    }

    public void WriteText(string targetPath, string content, DurableFileClass fileClass)
    {
        WriteBytes(targetPath, new UTF8Encoding(false, true).GetBytes(content), fileClass);
    }
}
```

Add a separate lower-level fake for failures at:

```text
temp CreateNew
body write
Flush(true)
MoveFileEx promotion
temp cleanup
```

Do not pretend a high-level `ShouldFail` writer proves all durable cut points.

---

# 104. Test fixture: crash-state builder

Add `LibraryMutationCrashFixtureBuilder.cs`.

```csharp
internal sealed class LibraryMutationCrashFixtureBuilder
{
    private readonly AppPaths _paths;
    private readonly IDurableAtomicFileWriter _writer;

    public LibraryMutationCrashFixtureBuilder(string root)
    {
        _paths = new AppPaths(root);
        _writer = new WindowsDurableAtomicFileWriter();
        _paths.EnsureDataDirectories();
    }

    public AppPaths Paths => _paths;

    public LibraryMutationCrashFixtureBuilder WithPrimary(LibraryDocument document)
    {
        _writer.WriteBytes(
            _paths.LibraryPath,
            LibraryRepository.SerializeCanonicalBytes(document),
            DurableFileClass.LibraryMetadata);
        return this;
    }

    public LibraryMutationCrashFixtureBuilder WithBackup(LibraryDocument document)
    {
        _writer.WriteBytes(
            _paths.LibraryBackupPath,
            LibraryRepository.SerializeCanonicalBytes(document),
            DurableFileClass.LibraryMetadata);
        return this;
    }

    public LibraryMutationCrashFixtureBuilder WithBody(Guid id, string body)
    {
        _writer.WriteText(_paths.GetPromptPath(id), body, DurableFileClass.PromptBody);
        return this;
    }

    public LibraryMutationCrashFixtureBuilder WithRawBody(Guid id, byte[] body)
    {
        _writer.WriteBytes(_paths.GetPromptPath(id), body, DurableFileClass.PromptBody);
        return this;
    }

    public LibraryMutationCrashFixtureBuilder WithRecoveryFile(string relativePath, byte[] bytes)
    {
        _writer.WriteBytes(
            Path.Combine(_paths.RootDirectory, relativePath),
            bytes,
            DurableFileClass.RecoveryArtifact);
        return this;
    }

    public LibraryMutationCrashFixtureBuilder WithJournal(LibraryMutationJournal journal)
    {
        var repo = BuildMutationJournalRepository(_paths, _writer);
        repo.WriteExactForFixture(journal);
        return this;
    }
}
```

`WriteExactForFixture` must be `internal` and exist only to construct crash states in tests; production code must still enforce legal phase transitions.

---

# 105. Test helper: byte-preservation snapshot

Add:

```csharp
internal sealed class TreeByteSnapshot
{
    public Dictionary<string, byte[]> Files { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Directories { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static TreeByteSnapshot Capture(string root)
    {
        var snapshot = new TreeByteSnapshot();

        if (!Directory.Exists(root))
            return snapshot;

        foreach (string dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            snapshot.Directories.Add(Path.GetRelativePath(root, dir));

        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            snapshot.Files[Path.GetRelativePath(root, file)] = File.ReadAllBytes(file);

        return snapshot;
    }
}
```

Add assertion helper:

```csharp
internal static void AssertPreserved(
    TreeByteSnapshot before,
    string root,
    params string[] allowedRemovedRelativePaths)
{
    var removed = new HashSet<string>(allowedRemovedRelativePaths, StringComparer.OrdinalIgnoreCase);

    foreach ((string rel, byte[] bytes) in before.Files)
    {
        if (removed.Contains(rel))
            continue;

        string full = Path.Combine(root, rel);
        Assert.IsTrue(File.Exists(full), $"Expected preserved file missing: {rel}");
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(full), $"Preserved file changed: {rel}");
    }

    foreach (string rel in before.Directories)
    {
        if (removed.Contains(rel))
            continue;
        Assert.IsTrue(Directory.Exists(Path.Combine(root, rel)), $"Expected preserved directory missing: {rel}");
    }
}
```

Use this in negative safety tests; do not assert only `Throws`.

---

# 106. CRUU10-001 strict directory tests

Mandatory deterministic tests:

```text
CRUU10_001_Access_denied_directory_is_not_Missing
CRUU10_001_Unreadable_target_ancestor_aborts_topology
CRUU10_001_Unreadable_prompts_directory_is_not_treated_absent
CRUU10_001_Reservation_does_not_create_over_unreadable_path
CRUU10_001_Recovery_does_not_skip_unreadable_directory
```

Example:

```csharp
[TestMethod]
[TestCategory("FilesystemAuthority")]
public void CRUU10_001_Access_denied_directory_is_not_Missing()
{
    var fake = new FakeStrictDirectoryOpener
    {
        OnOpenForIdentity = _ => throw new UnauthorizedAccessException("injected")
    };

    var resolver = new WindowsPhysicalPathResolver(fake);

    Assert.ThrowsException<UnauthorizedAccessException>(
        () => resolver.ResolveWithNearestExistingAncestor(@"C:\restricted\child"));
}
```

If `WindowsPhysicalPathResolver` wraps the error in `InvalidOperationException`, assert that exact typed contract instead; do not use base `Exception`.

---

# 107. CRUU9 managed-child real junction tests

Use real Windows junctions.

```csharp
private static void CreateJunction(string junction, string target)
{
    Directory.CreateDirectory(target);

    using var process = Process.Start(new ProcessStartInfo
    {
        FileName = "cmd.exe",
        Arguments = $"/c mklink /J \"{junction}\" \"{target}\"",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    })!;

    process.WaitForExit();

    if (process.ExitCode != 0)
    {
        string error = process.StandardError.ReadToEnd();
        Assert.Fail($"mklink /J failed on Windows CI: {error}");
    }
}
```

Tests:

```text
CRUU9_001_Empty_prompts_junction_outside_target_is_rejected
CRUU9_001_Empty_recovery_junction_outside_target_is_rejected
CRUU9_001_Prompts_junction_to_bootstrap_is_rejected
CRUU9_001_Recovery_junction_to_current_root_is_rejected
```

All must be `[TestCategory("WindowsFilesystemIntegration")]`.

Do not substitute fake resolver tests for these real tests.

---

# 108. CRUU10-007 long-lived lease integration test

```csharp
[TestMethod]
[TestCategory("WindowsFilesystemIntegration")]
[TestCategory("FilesystemAuthority")]
public void CRUU10_007_Prompts_directory_cannot_be_replaced_while_session_lease_held()
{
    if (!OperatingSystem.IsWindows())
        Assert.Inconclusive("Windows-only integration test.");

    using var dir = new TestDirectory();
    string root = Path.Combine(dir.Root, "data");
    string prompts = Path.Combine(root, "prompts");
    string recovery = Path.Combine(root, "recovery");

    Directory.CreateDirectory(prompts);
    Directory.CreateDirectory(recovery);

    using var lease = ManagedDataRootSessionLease.Acquire(root);

    Assert.ThrowsException<IOException>(
        () => Directory.Move(prompts, prompts + "-moved"));

    Assert.IsTrue(Directory.Exists(prompts));
}
```

Companion recovery directory test is mandatory.

After `lease.Dispose()`, add a positive assertion that a rename succeeds so the test proves the handle is the blocker rather than another unrelated condition.

---

# 109. Manifest v3 tests

Add at least:

```text
CRUU9_003_Temp_must_share_final_parent
CRUU9_003_Temp_must_embed_exact_attempt_id
CRUU9_003_Temp_must_use_exact_filename_grammar
CRUU9_003_Temp_nonce_must_be_16_lower_hex
CRUU9_004_Final_of_A_cannot_equal_temp_of_B
CRUU9_004_Owned_control_cannot_equal_artifact_final
CRUU9_004_Owned_control_cannot_equal_artifact_temp
CRUU9_024_Duplicate_attemptId_member_rejected
CRUU9_024_Duplicate_artifact_relativePath_member_rejected
CRUU9_024_Unknown_manifest_member_rejected
CRUU9_024_Invalid_UTF8_manifest_rejected
CRUU10_008_Baseline_directory_flags_round_trip
```

Every rejection test must capture target tree before call and prove exact preservation after exception.

---

# 110. Retry source identity tests

```text
CRUU9_005_Retry_source_physical_root_mismatch_preserves_target
CRUU9_005_Retry_source_package_fingerprint_mismatch_preserves_target
CRUU9_005_Retry_exact_source_identity_allows_cleanup
```

For mismatch tests:

```text
manifest remains
all finals remain
all temps remain unless precondition says untouched
foreign baseline remains byte-identical
settings unchanged
```

Do not partially clean before source identity validation.

---

# 111. Capability crash-ownership tests

Mandatory:

```text
CRUU9_006_Probe_paths_are_declared_before_creation
CRUU9_006_Crash_after_probe_current_creation_is_recoverable
CRUU9_006_Crash_after_probe_replacement_creation_is_recoverable
CRUU9_006_Probe_cleanup_failure_blocks_settings_commit
CRUU9_006_Foreign_similar_probe_name_is_preserved
```

Use a file-ops fake that records call order. Assert:

```text
manifest durable write index < first probe CreateNew index
```

---

# 112. Manifest staging tests

```text
CRUU9_007_Manifest_writer_temp_uses_reserved_durable_control_grammar
CRUU9_007_Crash_before_manifest_promotion_leaves_reconcilable_temp
CRUU9_019_Manifest_temp_cleanup_failure_does_not_mask_original_promotion_failure
```

For CRUU9-019, assert exception chain contains the original promotion failure as the primary thrown error. Cleanup residue is reported separately or left for startup reconciliation.

---

# 113. Settings durability tests

Mandatory:

```text
CRUU9_008_Settings_primary_uses_write_through_promotion
CRUU9_009_Stale_exact_settings_temp_is_removed_before_load
CRUU9_009_Similar_foreign_settings_tmp_is_preserved
CRUU6_003_Backup_change_invalidates_settings_precondition
CRUU6_003_Settings_compare_and_save_share_mutation_lease
CRUU6_004_Post_recovery_precondition_does_not_self_invalidate
```

Add call-order fake proving:

```text
recovery/sync write occurs
then token capture
not token capture first
```

---

# 114. Package integrity tests

Mandatory:

```text
CRUU10_002_Primary_missing_body_does_not_overwrite_complete_backup
CRUU10_002_Primary_unreadable_body_does_not_overwrite_complete_backup
CRUU10_002_Healthy_primary_still_synchronizes_backup
CRUU10_002_Incomplete_primary_complete_backup_recovers_safely
CRUU10_002_Incomplete_primary_no_complete_backup_stops
CRUU10_003_Missing_primary_backup_missing_body_does_not_promote
CRUU10_003_Corrupt_primary_backup_unreadable_body_does_not_promote
CRUU10_003_Complete_backup_recovers
CRUU10_014_Valid_metadata_missing_body_is_not_Healthy
CRUU10_014_Incomplete_backup_is_not_successful_recovery
```

Example critical test:

```csharp
[TestMethod]
[TestCategory("PackageIntegrity")]
public void CRUU10_002_Primary_missing_body_does_not_overwrite_complete_backup()
{
    using var dir = new TestDirectory();
    var paths = new AppPaths(dir.Root);
    paths.EnsureDataDirectories();

    Guid primaryPrompt = Guid.NewGuid();
    Guid backupPrompt = Guid.NewGuid();

    var primary = new LibraryDocument
    {
        Prompts = [new PromptRecord { Id = primaryPrompt, SortOrder = 10 }]
    };

    var backup = new LibraryDocument
    {
        Prompts = [new PromptRecord { Id = backupPrompt, SortOrder = 10 }]
    };

    var writer = new WindowsDurableAtomicFileWriter();
    writer.WriteBytes(paths.LibraryPath, LibraryRepository.SerializeCanonicalBytes(primary), DurableFileClass.LibraryMetadata);
    writer.WriteBytes(paths.LibraryBackupPath, LibraryRepository.SerializeCanonicalBytes(backup), DurableFileClass.LibraryMetadata);
    writer.WriteText(paths.GetPromptPath(backupPrompt), "healthy backup body", DurableFileClass.PromptBody);

    byte[] backupBefore = File.ReadAllBytes(paths.LibraryBackupPath);

    StartupResult result = BuildStartupService(paths, writer).LoadOrInitialize();

    Assert.IsTrue(result.RecoveredFromBackup);
    CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(paths.LibraryBackupPath));
    Assert.AreEqual(backupPrompt, result.Document.Prompts.Single().Id);
}
```

---

# 115. Create crash fixture tests

Mandatory:

```text
CRUU10_004_Create_crash_after_journal_before_body_recovers
CRUU10_004_Create_crash_after_body_before_metadata_removes_orphan
CRUU10_004_Create_crash_after_metadata_before_journal_retirement_finalizes
CRUU10_004_Create_unexpected_body_hash_preserves_journal_and_stops
```

Critical case:

```csharp
[TestMethod]
[TestCategory("MutationRecovery")]
[TestCategory("CrashRecovery")]
public void CRUU10_004_Create_crash_after_body_before_metadata_removes_orphan()
{
    using var dir = new TestDirectory();
    var paths = new AppPaths(dir.Root);
    paths.EnsureDataDirectories();

    var oldDoc = new LibraryDocument();
    Guid promptId = Guid.NewGuid();
    var newDoc = LibraryDocumentCloner.Clone(oldDoc);
    newDoc.Prompts.Add(new PromptRecord { Id = promptId, SortOrder = 10 });

    byte[] oldLibrary = LibraryRepository.SerializeCanonicalBytes(oldDoc);
    byte[] newLibrary = LibraryRepository.SerializeCanonicalBytes(newDoc);
    byte[] body = new UTF8Encoding(false, true).GetBytes("new prompt");

    var journal = new LibraryMutationJournal
    {
        OperationId = Guid.NewGuid(),
        Kind = LibraryMutationKind.CreatePrompt,
        Phase = LibraryMutationPhase.BodyDurable,
        PromptId = promptId,
        BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
        OldLibrarySha256Hex = MutationHash.Sha256Hex(oldLibrary),
        NewLibrarySha256Hex = MutationHash.Sha256Hex(newLibrary),
        NewBodyLength = body.LongLength,
        NewBodySha256Hex = MutationHash.Sha256Hex(body)
    };

    var writer = new WindowsDurableAtomicFileWriter();
    writer.WriteBytes(paths.LibraryPath, oldLibrary, DurableFileClass.LibraryMetadata);
    writer.WriteBytes(paths.GetPromptPath(promptId), body, DurableFileClass.PromptBody);
    BuildMutationJournalRepository(paths, writer).WriteExactForFixture(journal);

    BuildMutationRecovery(paths, writer).RecoverIfPresent();

    Assert.IsFalse(File.Exists(paths.GetPromptPath(promptId)));
    Assert.IsFalse(File.Exists(paths.LibraryMutationJournalPath));
    CollectionAssert.AreEqual(oldLibrary, File.ReadAllBytes(paths.LibraryPath));
}
```

---

# 116. Edit crash fixture tests

Mandatory:

```text
CRUU10_004_Edit_crash_after_prepared_before_recovery_copy_cleans_safely
CRUU10_004_Edit_crash_after_recovery_copy_before_new_body_preserves_old
CRUU10_004_Edit_crash_after_new_body_before_metadata_restores_old_body
CRUU10_004_Edit_crash_after_metadata_before_cleanup_keeps_new_body
CRUU10_004_Edit_unexpected_recovery_hash_preserves_everything_and_stops
CRUU10_004_Edit_primary_neither_old_nor_new_stops
```

Critical test skeleton:

```csharp
[TestMethod]
[TestCategory("MutationRecovery")]
[TestCategory("CrashRecovery")]
public void CRUU10_004_Edit_crash_after_new_body_before_metadata_restores_old_body()
{
    using var dir = new TestDirectory();
    var paths = new AppPaths(dir.Root);
    paths.EnsureDataDirectories();

    Guid promptId = Guid.NewGuid();
    Guid operationId = Guid.NewGuid();

    var oldDoc = new LibraryDocument
    {
        Prompts = [new PromptRecord { Id = promptId, SortOrder = 10, Title = "Old" }]
    };
    var newDoc = LibraryDocumentCloner.Clone(oldDoc);
    newDoc.Prompts.Single().Title = "New";

    byte[] oldLibrary = LibraryRepository.SerializeCanonicalBytes(oldDoc);
    byte[] newLibrary = LibraryRepository.SerializeCanonicalBytes(newDoc);
    byte[] oldBody = Encoding.UTF8.GetBytes("old body");
    byte[] newBody = Encoding.UTF8.GetBytes("new body");
    string recoveryRel = Path.Combine("recovery", $"mutation-{operationId:N}-old-{promptId:N}.md");

    var journal = BuildEditJournal(
        operationId,
        promptId,
        oldLibrary,
        newLibrary,
        oldBody,
        newBody,
        recoveryRel,
        LibraryMutationPhase.BodyDurable);

    var writer = new WindowsDurableAtomicFileWriter();
    writer.WriteBytes(paths.LibraryPath, oldLibrary, DurableFileClass.LibraryMetadata);
    writer.WriteBytes(paths.GetPromptPath(promptId), newBody, DurableFileClass.PromptBody);
    writer.WriteBytes(Path.Combine(paths.RootDirectory, recoveryRel), oldBody, DurableFileClass.RecoveryArtifact);
    BuildMutationJournalRepository(paths, writer).WriteExactForFixture(journal);

    BuildMutationRecovery(paths, writer).RecoverIfPresent();

    CollectionAssert.AreEqual(oldBody, File.ReadAllBytes(paths.GetPromptPath(promptId)));
    Assert.IsFalse(File.Exists(Path.Combine(paths.RootDirectory, recoveryRel)));
    Assert.IsFalse(File.Exists(paths.LibraryMutationJournalPath));
}
```

---

# 117. Delete crash fixture tests

Mandatory:

```text
CRUU10_DELETE_Crash_before_metadata_commit_retires_journal_and_keeps_body
CRUU10_DELETE_Metadata_committed_backup_still_references_body_preserves_body
CRUU10_DELETE_Metadata_and_backup_unreference_body_deletes_body
CRUU10_DELETE_Future_backup_preserves_body
CRUU10_DELETE_Unreadable_backup_preserves_body
CRUU10_DELETE_Body_already_missing_finalizes_without_error
```

Do not treat body preservation as a failed delete when metadata deletion is already committed; surface a warning and allow later orphan reconciliation.

---

# 118. Orphan reconciliation tests

Mandatory:

```text
CRUU10_005_Orphan_referenced_by_backup_is_preserved
CRUU10_005_Orphan_unreferenced_by_current_primary_and_backup_is_deleted
CRUU10_005_Future_backup_preserves_orphan
CRUU10_005_Unreadable_backup_preserves_orphan
CRUU10_005_Active_mutation_journal_preserves_prompt_body
CRUU10_005_NonGUID_md_is_foreign_and_preserved
CRUU10_005_Reconciled_orphan_is_not_copied_by_later_migration
```

For deleted orphan tests, first synchronize backup successfully so deletion authority is unambiguous.

---

# 119. Strict JSON tests

Settings duplicate:

```csharp
string json =
    """
    {
      "schemaVersion": 1,
      "dataRootPath": "C:\\A",
      "DataRootPath": "D:\\B"
    }
    """;
```

Must reject before deserialization.

Library duplicate member:

```csharp
string json =
    """
    {
      "schemaVersion": 1,
      "categories": [],
      "prompts": [
        {
          "id": "11111111-1111-1111-1111-111111111111",
          "Id": "22222222-2222-2222-2222-222222222222",
          "categoryId": null,
          "sortOrder": 10,
          "title": null
        }
      ]
    }
    """;
```

Must reject.

Mandatory names:

```text
CRUU10_009_Settings_duplicate_dataRootPath_rejected
CRUU10_009_Settings_unknown_member_rejected
CRUU10_009_Library_duplicate_prompts_rejected
CRUU10_009_Category_duplicate_id_rejected
CRUU10_009_Prompt_duplicate_title_rejected
CRUU10_009_Library_unknown_root_member_rejected
CRUU10_009_Invalid_UTF8_library_rejected
```

---

# 120. Title tests

Mandatory:

```text
CRUU10_011_Title_160_text_elements_allowed
CRUU10_011_Title_161_text_elements_rejected
CRUU10_011_Emoji_grapheme_count_is_domain_correct
CRUU10_011_Loaded_library_with_oversize_title_rejected
CRUU10_011_Editor_keeps_open_on_oversize_title
```

Use emoji + combining sequences to prove the domain uses `StringInfo.ParseCombiningCharacters`, not UTF-16 length.

---

# 121. `GetPrompts` exception-boundary tests

If needed, extract an `IPromptBodyReader` seam so service tests can inject exact errors.

Mandatory:

```text
CRUU10_010_IOException_becomes_unavailable_prompt
CRUU10_010_Unauthorized_becomes_unavailable_prompt
CRUU10_010_SecurityException_becomes_unavailable_prompt
CRUU10_010_Programmer_exception_propagates
```

Programmer exception example: injected `InvalidOperationException("programmer bug")` if the chosen contract regards it as non-filesystem. The exact class may be a custom sentinel exception to avoid overlap with legitimate service errors.

---

# 122. CRUU10-008 baseline-directory tests

Mandatory:

```text
CRUU10_008_Preexisting_empty_prompts_survives_retry_cleanup
CRUU10_008_Preexisting_empty_recovery_survives_retry_cleanup
CRUU10_008_Attempt_created_prompts_removed_on_retry
CRUU10_008_Attempt_created_recovery_removed_on_retry
```

Critical assertion: preserve pre-existing empty directory even though it is empty after cleanup.

---

# 123. Recovery terminal verifier tests

Mandatory:

```text
CRUU9_010_Postcleanup_foreign_file_blocks_marker_retirement
CRUU9_010_Remaining_manifest_temp_blocks_marker_retirement
CRUU9_010_Remaining_manifest_final_blocks_marker_retirement
CRUU9_010_Changed_baseline_file_blocks_marker_retirement
CRUU9_010_Clean_terminal_state_retires_marker
```

Inject foreign entry **after** initial inventory to prove final verification is not merely reusing stale inventory state.

---

# 124. Reservation tests

Mandatory:

```text
CRUU9_012_Successful_new_root_does_not_report_nonempty_created_directory_cleanup_failure
CRUU9_013_Reservation_acquire_cleanup_failure_is_reported
CRUU9_013_Race_existing_directory_is_not_claimed_owned
CRUU9_014_Stale_unlocked_root_app_lock_is_accepted
CRUU9_014_Actively_locked_root_app_lock_is_rejected
```

Use an injected strict directory creator to deterministically simulate `AlreadyExists` after a prior missing state.

---

# 125. Existing-library switch tests

Mandatory:

```text
CRUU6_009_Future_target_schema_is_controlled_dialog_error
CRUU6_010_Readonly_existing_library_primary_rejects_switch
CRUU6_010_Readonly_active_prompt_file_rejects_existing_target
CRUU9_EXISTING_Probe_cleanup_failure_leaves_settings_unchanged
CRUU9_EXISTING_Target_physical_identity_changes_before_commit_aborts
CRUU9_EXISTING_Target_fingerprint_changes_before_commit_aborts
```

Every test must prove settings primary and backup bytes unchanged on precommit failure.

---

# 126. Monotonic shutdown tests

Introduce/inject an `IUserMessageService` if direct `MessageBox` fault injection is otherwise impossible.

Required seam:

```csharp
public interface IUserMessageService
{
    void ShowInformation(Window owner, string message, string title);
    void ShowWarning(Window owner, string message, string title);
    void ShowError(Window owner, string message, string title);
}
```

Then test:

```text
CRUU9_020_RestartRequired_true_requests_shutdown_when_dialog_result_true
CRUU9_020_RestartRequired_true_requests_shutdown_when_dialog_result_null
CRUU9_020_Restart_message_failure_still_requests_shutdown
CRUU9_020_RestartRequired_false_never_requests_shutdown
```

Do not rely only on reflection invoking a private event without running the actual control flow.

---

# 127. Dedicated unavailable-root tests

Mandatory:

```text
CRUU6_011_Unavailable_configured_root_uses_dedicated_safety_error
CRUU10_UNAVAILABLE_DriveNotFound_does_not_fallback_to_default_root
CRUU10_UNAVAILABLE_PathNotFound_does_not_create_default_library
CRUU10_UNAVAILABLE_Resolver_access_denied_does_not_mutate_settings
```

Assert default root `library.json` remains absent if it did not exist before.

---

# 128. Required test categories

Use exact MSTest categories:

```text
FilesystemAuthority
PackageIntegrity
MutationRecovery
CrashRecovery
WpfIntegration
WindowsFilesystemIntegration
ReleaseVerification
```

A critical test may have multiple categories.

Do not create a category containing zero tests; CI should fail if a focused command executes zero tests.

---

# 129. Replace `VerifyTestEvidence.ps1`

The current warning-only sentinel behavior is insufficient. Use exact test names and fail closed.

```powershell
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string[]]$TrxPath,

    [Parameter(Mandatory=$false)]
    [string[]]$RequiredTestName = @()
)

$ErrorActionPreference = 'Stop'
$allResults = @()

foreach ($path in $TrxPath) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "TRX file not found: $path"
    }

    [xml]$trx = Get-Content -LiteralPath $path -Raw
    $ns = [System.Xml.XmlNamespaceManager]::new($trx.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')

    $results = @($trx.SelectNodes('//t:UnitTestResult', $ns))
    if ($results.Count -eq 0) {
        $results = @($trx.SelectNodes('//UnitTestResult'))
    }

    foreach ($result in $results) {
        $allResults += [pscustomobject]@{
            TestName = [string]$result.testName
            Outcome  = [string]$result.outcome
            Source   = $path
        }
    }
}

if ($allResults.Count -le 0) {
    throw 'No executed test results found.'
}

$bad = @($allResults | Where-Object { $_.Outcome -ne 'Passed' })
if ($bad.Count -gt 0) {
    $detail = $bad | ForEach-Object { "$($_.TestName)=$($_.Outcome)" }
    throw "Non-passing tests found: $($detail -join ', ')"
}

foreach ($required in $RequiredTestName) {
    $matches = @($allResults | Where-Object { $_.TestName -eq $required })

    if ($matches.Count -eq 0) {
        throw "Required test did not execute: $required"
    }

    if (@($matches | Where-Object { $_.Outcome -ne 'Passed' }).Count -gt 0) {
        throw "Required test did not pass: $required"
    }
}

Write-Host "Verified $($allResults.Count) passed test results and $($RequiredTestName.Count) exact required tests."
```

No wildcard sentinel matching.

---

# 130. Required regression test manifest

Create `tools/RequiredRegressionTests.psd1`.

At minimum:

```powershell
@{
    Required = @(
        'CRUU9_001_Empty_prompts_junction_outside_target_is_rejected',
        'CRUU9_003_Temp_must_embed_exact_attempt_id',
        'CRUU9_004_Final_of_A_cannot_equal_temp_of_B',
        'CRUU9_005_Retry_source_package_fingerprint_mismatch_preserves_target',
        'CRUU9_006_Probe_paths_are_declared_before_creation',
        'CRUU9_007_Crash_before_manifest_promotion_leaves_reconcilable_temp',
        'CRUU9_010_Postcleanup_foreign_file_blocks_marker_retirement',
        'CRUU9_012_Successful_new_root_does_not_report_nonempty_created_directory_cleanup_failure',
        'CRUU9_020_Restart_message_failure_still_requests_shutdown',
        'CRUU9_024_Unknown_manifest_member_rejected',
        'CRUU10_001_Access_denied_directory_is_not_Missing',
        'CRUU10_002_Primary_missing_body_does_not_overwrite_complete_backup',
        'CRUU10_003_Missing_primary_backup_missing_body_does_not_promote',
        'CRUU10_004_Create_crash_after_body_before_metadata_removes_orphan',
        'CRUU10_004_Edit_crash_after_new_body_before_metadata_restores_old_body',
        'CRUU10_005_Orphan_referenced_by_backup_is_preserved',
        'CRUU10_007_Prompts_directory_cannot_be_replaced_while_session_lease_held',
        'CRUU10_008_Preexisting_empty_prompts_survives_retry_cleanup',
        'CRUU10_009_Settings_duplicate_dataRootPath_rejected',
        'CRUU10_010_Programmer_exception_propagates',
        'CRUU10_011_Title_161_text_elements_rejected'
    )
}
```

Add more exact tests for all high findings; this is a floor, not ceiling.

---

# 131. CI focused gates

Replace the single generic test step with focused categories plus full suite.

```yaml
      - name: Test filesystem authority
        shell: pwsh
        run: |
          dotnet test PromptHelper.slnx `
            -c Release `
            --no-build `
            --filter "TestCategory=FilesystemAuthority" `
            --logger "trx;LogFileName=filesystem-authority.trx"

      - name: Test package integrity
        shell: pwsh
        run: |
          dotnet test PromptHelper.slnx `
            -c Release `
            --no-build `
            --filter "TestCategory=PackageIntegrity" `
            --logger "trx;LogFileName=package-integrity.trx"

      - name: Test mutation recovery
        shell: pwsh
        run: |
          dotnet test PromptHelper.slnx `
            -c Release `
            --no-build `
            --filter "TestCategory=MutationRecovery" `
            --logger "trx;LogFileName=mutation-recovery.trx"

      - name: Test crash recovery
        shell: pwsh
        run: |
          dotnet test PromptHelper.slnx `
            -c Release `
            --no-build `
            --filter "TestCategory=CrashRecovery" `
            --logger "trx;LogFileName=crash-recovery.trx"

      - name: Test Windows filesystem integration
        shell: pwsh
        run: |
          dotnet test PromptHelper.slnx `
            -c Release `
            --no-build `
            --filter "TestCategory=WindowsFilesystemIntegration" `
            --logger "trx;LogFileName=windows-filesystem.trx"

      - name: Test WPF integration
        shell: pwsh
        run: |
          dotnet test PromptHelper.slnx `
            -c Release `
            --no-build `
            --filter "TestCategory=WpfIntegration" `
            --logger "trx;LogFileName=wpf-integration.trx"

      - name: Full Release test suite
        shell: pwsh
        run: |
          dotnet test PromptHelper.slnx `
            -c Release `
            --no-build `
            --logger "trx;LogFileName=full-release.trx"

      - name: Verify mandatory regression evidence
        shell: pwsh
        run: |
          $required = Import-PowerShellDataFile ./tools/RequiredRegressionTests.psd1
          $trx = Get-ChildItem -Path . -Filter '*.trx' -Recurse | Select-Object -ExpandProperty FullName
          ./tools/VerifyTestEvidence.ps1 -TrxPath $trx -RequiredTestName $required.Required
```

If a focused category command returns 0 executed tests, the command/evidence step must fail.

---

# 132. Five-run final gate

For final acceptance:

```powershell
1..5 | ForEach-Object {
    Write-Host "=== FINAL RUN $_ / 5 ==="

    dotnet test PromptHelper.slnx `
      -c Release `
      --no-build `
      --logger "trx;LogFileName=final-full-$_.trx"

    if ($LASTEXITCODE -ne 0) {
        throw "Full-suite run $_ failed."
    }
}
```

The implementing AI must report all five run counts, not only "5x passed".

---

# 133. Release icon identity implementation

When the real approved SVG exists, verify identity end-to-end.

Required canonical comparison:

```text
SVG rendered to 256x256 RGBA
ICO 256x256 frame decoded to RGBA
EXE embedded 256x256 icon frame decoded to RGBA
SHA-256 raw RGBA bytes
all three equal
```

Do not compare compressed PNG bytes because encoder metadata/compression can differ while pixels match.

If the real approved SVG remains absent:

```text
product/code acceptance may pass
strict release remains BLOCKED
```

Never fabricate an icon.

---

# 134. Final source grep gate

Run and review every hit:

```powershell
git grep -n "File.Exists" -- src/PromptHelper
git grep -n "Directory.Exists" -- src/PromptHelper
git grep -n "DirectoryInfo" -- src/PromptHelper
git grep -n "FileInfo" -- src/PromptHelper
git grep -n "catch (Exception" -- src/PromptHelper
git grep -n "Assert.Throws<Exception>" -- tests
git grep -n "Assert.ThrowsException<Exception>" -- tests
git grep -n "declaredTempMap" -- .
git grep -n "IsCaseSensitive" -- .
git grep -n "Write-Warning" -- tools
git grep -n "RandomNumberGenerator.GetHexString" -- src/PromptHelper
git grep -n "Guid.NewGuid().*tmp" -- src/PromptHelper
```

For every hit, implementation evidence must classify it as:

```text
SAFE NON-AUTHORITY
REQUIRED OWNED RANDOMNESS
BUG
```

Unexplained authority hits block acceptance.

---

# 135. Performance constraints

Correctness first, but do not introduce accidental O(N²) hashing.

Rules:

```text
- read each payload body once per snapshot pass;
- compute length/hash in the same read where possible;
- do not reopen library.json inside fingerprint helper;
- use dictionary lookup for manifest artifacts;
- do not store all prompt body bytes in manifest;
- stream migration copies.
```

Build lookup once:

```csharp
var artifactsByPath = manifest.Artifacts.ToDictionary(
    a => a.RelativePath,
    StringComparer.OrdinalIgnoreCase);
```

---

# 136. Safety non-goals

Do not expand scope into:

```text
ACL management
encryption
cloud synchronization
database conversion
automatic arbitrary JSON repair
multi-process shared editing
symbolic-link support
```

The intended behavior is fail-closed local persistence with exact crash recovery.

---

# 137. Phase-by-phase implementation order

## Phase 00 — Baseline/source map

No edits. Record HEAD/status and locate every named type.

Exit criteria:

```text
source map exists
no newer work reset
```

## Phase 01 — Strict authority primitives

Implement:

```text
StrictPathAuthority
WindowsStrictDirectoryOpener
StrictJsonObjectAuthority
strict UTF-8 helper
```

Focused tests only.

## Phase 02 — Durable writer

Implement:

```text
IDurableAtomicFileWriter
WindowsDurableAtomicFileWriter
DurableTempName
DurableTempReconciler
```

Tests for create, replace, write-through seam, cleanup, foreign temp preservation.

## Phase 03 — Physical tree/session lease

Implement:

```text
WindowsPhysicalPathResolver strict walk
ManagedTreeTopologyValidator
case API removal
ManagedDataRootSessionLease
```

Real Windows junction/node-swap tests.

## Phase 04 — Manifest v3

Implement schema/invariants/temp binding/one namespace/source fingerprint/baseline/owned controls. Do not yet modify coordinator behavior beyond compile needs.

## Phase 05 — Manifest repository/recovery

Use strict authority + durable writer; implement source identity and terminal-state verification.

## Phase 06 — Capability/reservation

Predeclare probe controls; add `CommitRootOwnership`; strict directory creation ownership; truthful cleanup failures.

## Phase 07 — Coordinator

Physical settings alias consistency, repeated bound-target validation, Ready gate, no optional temp fallback, monotonic point of no return.

## Phase 08 — Settings

Durable writer, exact temp reconciliation, strict JSON, dual precondition preserved.

## Phase 09 — Package integrity/startup

Package inspector, type-safe backup synchronization, startup decision table.

## Phase 10 — Mutation journal

Create/Edit/Duplicate/Delete crash state machines and restart fixtures.

## Phase 11 — Orphan reconciler

Only after primary/backup authority is correct.

## Phase 12 — UI/domain

Title bound, narrow exception filter, future schema UI, unavailable-root error, monotonic shutdown.

## Phase 13 — CI/evidence

Exact sentinels and focused categories.

## Phase 14 — Release identity

Only if real logo exists.

## Phase 15 — full regression

5x suite, publish, source grep, manual GUI.

---

# 138. Per-finding production file / test map

| Finding | Production edits | Required minimum proof |
|---|---|---|
| CRUU9-001 | `ManagedTreeTopologyValidator`, migration/coordinator, session lease | real prompts + recovery junction tests |
| CRUU9-002 | strict path authority, migration/recovery interfaces | unreadable file != missing |
| CRUU9-003 | `MigrationTempName`, manifest validator | same-parent/exact AttemptId grammar |
| CRUU9-004 | manifest ownership namespace | cross final/temp/control collisions rejected |
| CRUU9-005 | recovery context/service | source root + package fingerprint mismatch preserves target |
| CRUU9-006 | capability validator + manifest controls | control declared before first create |
| CRUU9-007 | durable writer + manifest repo | crash staging temp is reconcilable |
| CRUU9-008 | settings repo + durable writer | write-through promotion seam |
| CRUU9-009 | durable temp reconciler | exact temp removed; foreign similar preserved |
| CRUU9-010 | recovery terminal verifier | late foreign injection blocks marker retirement |
| CRUU9-011 | typed recovery boundary | no raw unexpected recovery escape |
| CRUU9-012 | reservation ownership | success does not clean committed root |
| CRUU9-013 | strict creator/reservation | cleanup failure reported; race ownership correct |
| CRUU9-014 | baseline + reservation | stale unlocked lock accepted, active rejected |
| CRUU9-015 | physical bootstrap runtime context | lexical/physical aliases consistent |
| CRUU9-016 | control-path policy | nested lookalike not ignored |
| CRUU9-017 | `MigrationReadyGate` | cannot Ready with temp/foreign/source mismatch |
| CRUU9-018 | copy API | undeclared payload fails before write |
| CRUU9-019 | durable writer | original failure not masked |
| CRUU9-020 | MainWindow/SettingsDialog/message seam | shutdown always after commit |
| CRUU9-021 | tests | replace fake/weak assertions with actual behavior |
| CRUU9-022 | CI/evidence tool | missing exact sentinel fails |
| CRUU9-023 | release tooling | normalized pixel identity |
| CRUU9-024 | strict manifest JSON | duplicate/unknown/UTF-8 rejection |
| CRUU9-025 | real asset | BLOCKED until approved SVG |
| CRUU10-001 | strict path/resolver/baseline/reservation | access denied directory never missing |
| CRUU10-002 | package inspector/startup | incomplete primary cannot overwrite backup |
| CRUU10-003 | package inspector/startup | incomplete backup cannot recover |
| CRUU10-004 | mutation journal/coordinator | restart create/edit crash fixtures |
| CRUU10-005 | orphan reconciler | current/future/unreadable backup rules |
| CRUU10-006 | durable writer/repos | Flush(true)+write-through+temp hygiene |
| CRUU10-007 | session lease | real directory replacement blocked |
| CRUU10-008 | manifest baseline/recovery | pre-existing empty dirs preserved |
| CRUU10-009 | strict JSON settings/library | duplicate + unknown rejection |
| CRUU10-010 | `PromptLibraryService` | programmer fault propagates |
| CRUU10-011 | `LibraryValidator` + editor | 160/161 text-element boundary |
| CRUU10-012 | control-path policy | nested lookalike handled consistently |
| CRUU10-013 | tests | fresh-instance crash fixtures |
| CRUU10-014 | startup tests | body-incomplete primary/backup matrix |
| CRUU10-015 | case inspector API | boolean API removed |

---

# 139. Evidence log required after every phase

The implementing AI must maintain:

```text
PHASE:
START_HEAD:
FILES_CHANGED:
NEW_TYPES:
REMOVED_OR_REPLACED_LEGACY_APIS:
TESTS_ADDED:
FOCUSED_COMMAND:
RESULT_TOTAL:
RESULT_PASSED:
RESULT_FAILED:
RESULT_SKIPPED:
BUILD_WARNINGS:
SOURCE_GREP_REVIEW:
KNOWN_BLOCKERS:
```

A prose sentence such as "all tests pass" is not sufficient.

---

# 140. Final implementation evidence template

```text
START_HEAD=
END_HEAD=

RESTORE=
BUILD=
WARNINGS=
ERRORS=

FILESYSTEM_AUTHORITY_TESTS=
PACKAGE_INTEGRITY_TESTS=
MUTATION_RECOVERY_TESTS=
CRASH_RECOVERY_TESTS=
WINDOWS_FILESYSTEM_INTEGRATION_TESTS=
WPF_INTEGRATION_TESTS=
FULL_SUITE=
FULL_RUN_1=
FULL_RUN_2=
FULL_RUN_3=
FULL_RUN_4=
FULL_RUN_5=

PUBLISH=
PUBLISHED_EXE=
LICENSE_PRESENT=
THIRD_PARTY_NOTICES_PRESENT=

REQUIRED_SENTINELS=
SKIPPED_MANDATORY_TESTS=

CRUU9_001=
CRUU9_002=
CRUU9_003=
CRUU9_004=
CRUU9_005=
CRUU9_006=
CRUU9_007=
CRUU9_008=
CRUU9_009=
CRUU9_010=
CRUU9_011=
CRUU9_012=
CRUU9_013=
CRUU9_014=
CRUU9_015=
CRUU9_016=
CRUU9_017=
CRUU9_018=
CRUU9_019=
CRUU9_020=
CRUU9_021=
CRUU9_022=
CRUU9_023=
CRUU9_024=
CRUU9_025=

CRUU10_001=
CRUU10_002=
CRUU10_003=
CRUU10_004=
CRUU10_005=
CRUU10_006=
CRUU10_007=
CRUU10_008=
CRUU10_009=
CRUU10_010=
CRUU10_011=
CRUU10_012=
CRUU10_013=
CRUU10_014=
CRUU10_015=

ICON_SOURCE=
ICON_GENERATED=
ICON_IDENTITY=
STRICT_RELEASE=

REMAINING_BLOCKERS=
```


---

# 141. Dependency wiring — exact target architecture

The weak model must avoid creating two parallel service graphs (legacy + hardened). Production should have one canonical graph.

Recommended `App`-level composition:

```text
StrictPathAuthority
WindowsStrictDirectoryOpener
WindowsPhysicalPathResolver
WindowsDirectoryCaseSensitivityInspector
ManagedTreeTopologyValidator
WindowsDurableAtomicFileWriter
DurableTempReconciler
AppSettingsRepository
ManagedDataRootPolicy
AppInstanceLock
MigrationManifestRepository
MigrationRecoveryService
LibraryMutationJournalRepository
LibraryMutationRecoveryService
LibraryRepository
PromptRepository
LibraryPackageInspector
PromptOrphanReconciler
PromptMutationCoordinator
PromptLibraryService
MainViewModel
MainWindow
```

Do not instantiate separate default path resolvers inside individual services when the root decision depends on a shared physical identity model. Pass the same resolver/policy where identity consistency matters.

---

# 142. `App.xaml.cs` reference composition sequence

This is a reference structure. Adapt existing startup UI/error message helpers, but preserve ordering.

```csharp
private AppInstanceLock? _instanceLock;
private ManagedDataRootSessionLease? _managedTreeLease;

protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);

    try
    {
        var strictPaths = new StrictPathAuthority();
        var durableWriter = new WindowsDurableAtomicFileWriter();
        var directoryOpener = new WindowsStrictDirectoryOpener();
        var physicalResolver = new WindowsPhysicalPathResolver(directoryOpener);
        var caseInspector = new WindowsDirectoryCaseSensitivityInspector();
        var treeValidator = new ManagedTreeTopologyValidator(
            physicalResolver,
            strictPaths,
            caseInspector);

        var settingsRepo = new AppSettingsRepository(
            durableWriter: durableWriter,
            strictPaths: strictPaths);

        SettingsLoadResult settingsLoad = settingsRepo.LoadOrRecover();
        string lexicalRoot = settingsRepo.GetEffectiveDataRoot(settingsLoad.Settings);
        string bootstrapLexical = settingsRepo.BootstrapRoot;

        string bootstrapPhysical = physicalResolver.ResolveWithNearestExistingAncestor(bootstrapLexical);

        var rootPolicy = new ManagedDataRootPolicy(
            physicalResolver,
            caseInspector,
            treeValidator);

        string physicalRoot = rootPolicy.ValidateConfiguredRootForStartup(
            lexicalRoot,
            bootstrapPhysical);

        var paths = new AppPaths(physicalRoot);

        _instanceLock = AppInstanceLock.TryAcquire(paths.LockPath);
        if (_instanceLock is null)
        {
            ShowAlreadyRunningAndShutdown();
            return;
        }

        var manifestRepo = new MigrationManifestRepository(strictPaths, durableWriter);
        var migrationRecovery = new MigrationRecoveryService(
            manifestRepo,
            strictPaths,
            physicalResolver,
            treeValidator);

        var mutationJournalRepo = new LibraryMutationJournalRepository(
            paths,
            strictPaths,
            durableWriter);

        var mutationRecovery = new LibraryMutationRecoveryService(
            paths,
            mutationJournalRepo,
            strictPaths,
            durableWriter,
            new WindowsVerifiedArtifactDeleter());

        AssertNoConflictingControlJournals(paths, strictPaths);

        // If this root is the committed target of a prior migration, finalize it.
        migrationRecovery.FinalizeCommittedStartupOrThrow(
            new MigrationRecoveryContext(
                physicalRoot,
                bootstrapPhysical));

        mutationRecovery.RecoverIfPresent();

        paths.EnsureDataDirectories();
        treeValidator.Validate(physicalRoot);

        _managedTreeLease = ManagedDataRootSessionLease.Acquire(
            physicalRoot,
            directoryOpener);

        var libraryRepo = new LibraryRepository(
            paths,
            durableWriter,
            strictPaths);

        var promptRepo = new PromptRepository(
            paths,
            durableWriter,
            new FileDeleter(),
            strictPaths);

        var packageInspector = new LibraryPackageInspector(paths, strictPaths);

        var startupService = new LibraryStartupService(
            paths,
            libraryRepo,
            promptRepo,
            new FileDeleter(),
            durableWriter,
            packageInspector);

        StartupResult startup = startupService.LoadOrInitialize();

        var orphanReconciler = new PromptOrphanReconciler(
            paths,
            promptRepo,
            libraryRepo,
            strictPaths,
            treeValidator);

        orphanReconciler.TryReconcileAfterHealthyStartup(startup.Document);

        var mutationCoordinator = new PromptMutationCoordinator(
            paths,
            libraryRepo,
            promptRepo,
            mutationJournalRepo,
            mutationRecovery,
            durableWriter,
            strictPaths);

        var libraryService = new PromptLibraryService(
            startup.Document,
            libraryRepo,
            promptRepo,
            mutationCoordinator);

        var vm = new MainViewModel(
            libraryService,
            promptRepo,
            physicalRoot);

        var window = new MainWindow(
            vm,
            new ClipboardService(),
            settingsRepo,
            new DataFolderMigrationService(
                durableWriter,
                strictPaths,
                physicalResolver,
                treeValidator),
            new WpfApplicationLifetime());

        MainWindow = window;
        window.Show();
    }
    catch (UnsupportedSettingsSchemaException ex)
    {
        ShowFutureSettingsError(ex);
        Shutdown();
    }
    catch (UnsupportedLibrarySchemaException ex)
    {
        ShowFutureLibraryError(ex);
        Shutdown();
    }
    catch (PhysicalDataRootUnavailableException ex)
    {
        ShowConfiguredRootUnavailable(ex);
        Shutdown();
    }
    catch (Exception ex) when (IsExpectedStartupFailure(ex))
    {
        ShowStartupError(ex);
        Shutdown();
    }
}
```

The exact constructors can be simplified, but **do not** allow services to silently instantiate weaker legacy dependencies after this wiring is established.

---

# 143. Remove legacy constructor bypasses

After migration to hardened dependencies, delete or internalize constructors that allow production to bypass them.

Examples to remove/replace:

```text
new AtomicTextWriter() inside production repositories
new WindowsPhysicalPathResolver() inside every independent service
IMigrationFileOps.FileExists
IMigrationFileOps.DirectoryExists
legacy IsCaseSensitive
optional declaredTempMap fallback
PrepareTarget legacy public bypass
bare SynchronizeBackup(LibraryDocument) startup overload
```

If a convenience constructor remains, it must instantiate the hardened dependencies only.

Example acceptable:

```csharp
public PromptRepository(AppPaths paths)
    : this(
        paths,
        new WindowsDurableAtomicFileWriter(),
        new FileDeleter(),
        new StrictPathAuthority())
{
}
```

Not acceptable:

```csharp
public PromptRepository(AppPaths paths)
    : this(paths, new AtomicTextWriter(), new FileDeleter())
{
}
```

---

# 144. `IMigrationFileOps` replacement strategy

The current broad interface contains weak booleans. Do not keep adding members forever.

Preferred split:

```text
IMigrationStreamOps
IStrictPathAuthority
IStrictDirectoryCreator
IDurableMoveOps
IVerifiedArtifactDeleter
```

Example:

```csharp
internal interface IMigrationStreamOps
{
    Stream OpenRead(string path);
    Stream CreateNew(string path);
    void FlushToDisk(Stream stream);
    void MoveNoOverwriteWriteThrough(string source, string destination);
}
```

Use strict path authority for presence/state and enumeration.

This prevents fake implementations from accidentally returning `false` for an injected access error.

---

# 145. `ManagedTreeArtifactValidator`

Add a narrow helper used before deleting or rewriting prompt/recovery files by name.

```csharp
internal sealed class ManagedTreeArtifactValidator
{
    private readonly string _physicalRoot;
    private readonly IPhysicalPathResolver _resolver;
    private readonly IStrictPathAuthority _paths;

    public ManagedTreeArtifactValidator(
        string physicalRoot,
        IPhysicalPathResolver resolver,
        IStrictPathAuthority paths)
    {
        _physicalRoot = PathIdentity.NormalizeForComparison(physicalRoot);
        _resolver = resolver;
        _paths = paths;
    }

    public void AssertOrdinaryFileUnderPrompts(string filePath)
    {
        AssertOrdinaryFileUnder(filePath, Path.Combine(_physicalRoot, "prompts"));
    }

    public void AssertOrdinaryFileUnderRecovery(string filePath)
    {
        AssertOrdinaryFileUnder(filePath, Path.Combine(_physicalRoot, "recovery"));
    }

    private void AssertOrdinaryFileUnder(string filePath, string expectedParent)
    {
        string full = Path.GetFullPath(filePath);

        if (!PathIdentity.IsStrictDescendant(full, expectedParent))
            throw new InvalidDataException($"Managed artifact escapes expected directory: '{filePath}'.");

        StrictPathEntry state = _paths.Probe(full);
        if (!state.IsFile)
            throw new InvalidDataException($"Managed artifact is not an ordinary file: '{filePath}'.");

        if ((state.Attributes!.Value & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Managed artifact must not be a reparse point: '{filePath}'.");

        string resolvedParent = _resolver.ResolveWithNearestExistingAncestor(Path.GetDirectoryName(full)!);
        if (!PathIdentity.Equals(resolvedParent, expectedParent))
            throw new InvalidDataException($"Managed artifact parent changed physical identity: '{filePath}'.");
    }
}
```

The long-lived directory lease already reduces swap risk; this helper supplies local fail-closed defense for destructive operations.

---

# 146. Source payload snapshot — coherent implementation

Migration source capture should produce one immutable list.

```csharp
internal sealed record MigrationPayloadItem(
    string RelativePath,
    MigrationPayloadRole Role,
    long Length,
    string Sha256Hex);

internal sealed record MigrationPayloadSnapshot(
    IReadOnlyList<MigrationPayloadItem> Items,
    string PackageFingerprintSha256Hex);
```

Capture rules:

```text
library.json required
library.backup.json optional according to authority
all active prompt bodies required
orphan GUID .md files included if migration policy preserves them
recovery artifacts included according to existing product scope
root control files excluded
```

Compute each item hash from the same bytes/stream that establish length.

After collection:

```csharp
string fingerprint = MigrationPackageFingerprint.Compute(items);
```

Do not reopen `library.json` inside a separate fingerprint method.

---

# 147. Target inspection coherent snapshot

Current target inspection had a historical hybrid-read risk. Keep the CRUU6/CRUU9 fix strict.

Create:

```csharp
internal sealed record TargetContentSnapshot(
    byte[] MetadataBytes,
    LibraryDocument Document,
    IReadOnlyDictionary<Guid, PromptBodySnapshot> PromptBodies,
    string FingerprintSha256Hex);
```

Capture:

```text
read exact metadata bytes
parse those exact bytes
read/hash active bodies
compute fingerprint from captured metadata bytes + captured body hashes
second stability pass: re-read metadata/body hashes
if any changed => TargetInspectionUnstableException
```

No hidden metadata reread inside fingerprint calculation.

---

# 148. Settings strict JSON exact-member code

Before `JsonSerializer.Deserialize<AppSettings>`:

```csharp
using JsonDocument doc = JsonDocument.Parse(json);

StrictJsonObjectAuthority.ValidateExactObject(
    doc.RootElement,
    allowedMembers: ["schemaVersion", "dataRootPath"],
    requiredMembers: ["schemaVersion"],
    description: "settings root");
```

Then validate `schemaVersion` type/value.

Unknown field policy is intentionally strict. If a future version needs more fields, it must increment schema or explicitly extend allowed current schema through an audited change.

---

# 149. Library strict JSON nested validation code

Before deserialization:

```csharp
using JsonDocument doc = JsonDocument.Parse(json);
JsonElement root = doc.RootElement;

StrictJsonObjectAuthority.ValidateExactObject(
    root,
    ["schemaVersion", "categories", "prompts"],
    ["schemaVersion", "categories", "prompts"],
    "library root");

JsonElement categories = root.GetProperty("categories");
if (categories.ValueKind != JsonValueKind.Array)
    throw new InvalidDataException("library.categories must be an array.");

int categoryIndex = 0;
foreach (JsonElement category in categories.EnumerateArray())
{
    StrictJsonObjectAuthority.ValidateExactObject(
        category,
        ["id", "parentId", "name", "sortOrder"],
        ["id", "parentId", "name", "sortOrder"],
        $"library.categories[{categoryIndex}]");
    categoryIndex++;
}

JsonElement prompts = root.GetProperty("prompts");
if (prompts.ValueKind != JsonValueKind.Array)
    throw new InvalidDataException("library.prompts must be an array.");

int promptIndex = 0;
foreach (JsonElement prompt in prompts.EnumerateArray())
{
    StrictJsonObjectAuthority.ValidateExactObject(
        prompt,
        ["id", "categoryId", "sortOrder", "title"],
        ["id", "categoryId", "sortOrder", "title"],
        $"library.prompts[{promptIndex}]");
    promptIndex++;
}
```

If existing canonical serialization omits null fields, either change serializer to emit them consistently or make only the nullable property optional. Do not create reader/writer disagreement.

---

# 150. Library mutation in-memory state rule

`PromptLibraryService._document` must represent the last **fully retired durable transaction**, not merely the last primary write.

Rules:

```text
Create/Edit/Duplicate:
    _document = candidate only after journal is safely retired.

Delete:
    _document = candidate after metadata commit and journal resolution determines the operation is committed; body may remain as warned orphan.
```

If an operation throws while a journal remains unresolved:

```text
prefer fail-fast/restart requirement over continuing normal mutations with uncertain in-memory state.
```

Add an internal service state:

```csharp
private bool _requiresRecoveryRestart;
```

If in-process recovery cannot conclusively resolve a failed transaction:

```text
set _requiresRecoveryRestart=true
block subsequent mutating operations
show user restart/recovery message
```

Do not keep allowing edits after a failed transaction whose durable state is unknown.

---

# 151. Mutation gate

At start of every mutating service method:

```csharp
private void AssertMutationsAllowed()
{
    if (_requiresRecoveryRestart)
        throw new InvalidOperationException(
            "Prompt Helper must be restarted before making more changes because the previous save could not be fully resolved.");

    if (_mutationJournalRepo.TryReadStrict() is not null)
        throw new InvalidOperationException(
            "An unfinished library mutation is present. Restart Prompt Helper to recover it before making more changes.");
}
```

This is defense-in-depth; normally startup recovery ensures no journal remains.

---

# 152. UI behavior after unresolved normal mutation

If Create/Edit/Delete/Duplicate throws `LibraryMutationRecoveryException` or leaves `RequiresRecoveryRestart=true`:

```text
preserve editor text
show explicit data-safety message
request application shutdown/restart after user acknowledges
```

Do not loop the editor and allow repeated saves against uncertain disk state.

Suggested message:

```text
Prompt Helper could not safely finish or roll back this save.
Your prompt text is still shown in the editor, but the application must close now so recovery can complete on the next start.
No additional changes should be made in this session.
```

---

# 153. Orphan cleanup timing

Run orphan reconciliation only when all conditions hold:

```text
no migration marker
no mutation journal
no initialization marker
primary metadata current
primary package healthy
backup metadata current
backup synchronization successful or backup already exact current state
managed-tree lease held
```

If any condition is false:

```text
skip orphan deletion
optionally return a warning for diagnostics
```

Orphan cleanup is never required to let startup succeed.

---

# 154. Default-library first-run foreign data rules

First-run may initialize defaults only if:

```text
library.json missing
library.backup.json missing
no unfinished journal conflict
prompts contains no foreign/non-default data
recovery does not contain user recovery artifacts indicating prior install
```

Be conservative. If the data root contains unexplained user files, stop rather than create defaults over them.

The existing initialization marker can recover only exact known default prompt bodies.

---

# 155. Manual WPF test matrix

After automated tests, run published EXE and execute:

```text
01 first launch clean root
02 second launch same root
03 create category at Home
04 create nested category
05 rename category
06 reject duplicate sibling name
07 create prompt with automatic headline
08 create prompt with custom headline
09 reject 161-text-element headline without closing editor
10 edit body only
11 edit headline only
12 clear headline back to automatic
13 duplicate prompt same category
14 duplicate prompt other category
15 move prompt
16 copy prompt to clipboard
17 recent-copy bar updates
18 delete prompt
19 delete empty category
20 delete non-empty category blocked
21 switch to existing valid library and confirm
22 cancel existing-library switch
23 choose future-schema library: controlled warning, app alive
24 choose read-only existing library: controlled rejection
25 migrate to clean empty target
26 verify app shuts down after committed migration
27 restart into new target
28 verify old source remains intact
29 choose target with empty prompts junction outside root: reject
30 choose target with empty recovery junction outside root: reject
31 configured drive unavailable on startup: dedicated error, no default fallback
32 simulate safe orphan and restart: orphan reconciles only when backup authority allows
33 simulate future backup + orphan: orphan preserved
34 simulate mutation journal fixture and restart: recovery occurs before UI
35 verify no unexplained Prompt Helper temp/control residue after normal exit
```

Record failures by step number.

---

# 156. Negative-test preservation contract

Every failure-path test involving persistent data should verify at least:

```text
expected exception/result
settings.json exact bytes
settings.backup.json exact bytes
library.json exact bytes
library.backup.json exact bytes
relevant prompt body exact bytes
foreign target files exact bytes
manifest/journal existence and bytes according to state machine
pre-existing directory existence according to baseline
```

A test named `...preserves_target` that checks only one file is insufficient.

---

# 157. Final no-bypass grep

In addition to section 134, run:

```powershell
git grep -n "new AtomicTextWriter" -- src/PromptHelper
git grep -n "SynchronizeBackup(" -- src/PromptHelper
git grep -n "TryRead(" -- src/PromptHelper/Services
git grep -n "File.Delete" -- src/PromptHelper/Services
git grep -n "Directory.Delete" -- src/PromptHelper/Services
git grep -n "CreateDirectory(" -- src/PromptHelper/Services
git grep -n "File.Replace" -- src/PromptHelper
git grep -n "File.Move" -- src/PromptHelper
git grep -n "MoveFileEx" -- src/PromptHelper
```

Review each destructive/durable primitive for the hardened contract.

Expected pattern after refactor:

```text
most durable promotion concentrated in WindowsDurableAtomicFileWriter / migration no-overwrite move helper
most strict deletion concentrated in verified deletion / reconciler helpers
few ad hoc File.Delete calls
```

---

# 158. Final exact commands

```powershell
git status --short
git rev-parse HEAD

dotnet restore PromptHelper.slnx

dotnet build PromptHelper.slnx -c Release --no-restore

dotnet test PromptHelper.slnx -c Release --no-build --filter "TestCategory=FilesystemAuthority" --logger "trx;LogFileName=filesystem-authority.trx"
dotnet test PromptHelper.slnx -c Release --no-build --filter "TestCategory=PackageIntegrity" --logger "trx;LogFileName=package-integrity.trx"
dotnet test PromptHelper.slnx -c Release --no-build --filter "TestCategory=MutationRecovery" --logger "trx;LogFileName=mutation-recovery.trx"
dotnet test PromptHelper.slnx -c Release --no-build --filter "TestCategory=CrashRecovery" --logger "trx;LogFileName=crash-recovery.trx"
dotnet test PromptHelper.slnx -c Release --no-build --filter "TestCategory=WindowsFilesystemIntegration" --logger "trx;LogFileName=windows-filesystem.trx"
dotnet test PromptHelper.slnx -c Release --no-build --filter "TestCategory=WpfIntegration" --logger "trx;LogFileName=wpf-integration.trx"
dotnet test PromptHelper.slnx -c Release --no-build --logger "trx;LogFileName=full-release.trx"

$required = Import-PowerShellDataFile ./tools/RequiredRegressionTests.psd1
$trx = Get-ChildItem -Path . -Filter '*.trx' -Recurse | Select-Object -ExpandProperty FullName
./tools/VerifyTestEvidence.ps1 -TrxPath $trx -RequiredTestName $required.Required

1..5 | ForEach-Object {
    dotnet test PromptHelper.slnx -c Release --no-build --logger "trx;LogFileName=final-full-$_.trx"
    if ($LASTEXITCODE -ne 0) { throw "Final full run $_ failed." }
}

Remove-Item -Recurse -Force artifacts\publish-check -ErrorAction SilentlyContinue

dotnet publish src/PromptHelper/PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/publish-check

$requiredPublish = @(
  'artifacts/publish-check/PromptHelper.exe',
  'artifacts/publish-check/LICENSE',
  'artifacts/publish-check/THIRD_PARTY_NOTICES.md'
)

foreach ($path in $requiredPublish) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing publish artifact: $path" }
}
```

If real icon exists, run strict release verifier after publish.

---

# 159. Maximal weak-AI implementation prompt

Copy the following entire prompt to the weak implementation model together with this expanded document.

```text
ROLE
You are the implementation agent for Prompt Helper. You are deliberately
being given a highly prescriptive repair blueprint because you must make as
few architecture decisions as possible.

AUTHORITY
CRUU10 EXPANDED is the implementation authority.
The audit baseline was:
be1da4fa49916a102616f82a6c74f5601ab5d2d6

If repository HEAD is newer:
- DO NOT reset it;
- DO NOT discard newer fixes;
- compare newer implementation against every requirement in CRUU10 EXPANDED;
- retain equivalent or stronger implementations;
- fill only remaining gaps.

MISSION
Resolve every open code/product issue:
- CRUU9-001 through CRUU9-024
- CRUU10-001 through CRUU10-015

CRUU9-025 is a strict-release blocker only if the approved real logo is
still absent. Never invent or fabricate the logo.

WORK ORDER
You MUST use this order:
00 baseline/source map
01 strict path/file/directory authority
02 durable atomic writer/temp reconciliation
03 physical managed-tree validator + session lease
04 migration manifest schema v3/invariants
05 manifest repository + recovery
06 capability probe + reservation ownership
07 transition coordinator + Ready gate
08 settings durability/CAS
09 package-integrity startup authority
10 library mutation journal/recovery
11 orphan reconciler
12 UI/domain boundaries
13 CI/test evidence
14 icon identity if real asset exists
15 five-run full regression + publish

DO NOT IMPLEMENT FINDINGS ONE-BY-ONE OUT OF ORDER.

STRICT PATH RULES
- File.Exists and Directory.Exists cannot decide authority state.
- Missing, File, Directory, Unreadable are distinct.
- Use strict path-state helpers.
- Access denied is never Missing.
- prompts/recovery must be ordinary in-root directories, not reparse points.
- hold root/prompts/recovery directory handles without FILE_SHARE_DELETE for
  the live application session.

DURABILITY RULES
Every authoritative write uses the canonical durable writer:
- same-directory CreateNew temp;
- write exact bytes;
- Flush(true);
- close temp;
- MoveFileEx WRITE_THROUGH promotion;
- exact owned temp grammar;
- cleanup failure must not mask original operation failure;
- startup reconciles only exact Prompt Helper temp names.

MIGRATION RULES
- schema v3;
- exact strict JSON;
- exact UTF-8;
- exact root-control relative paths;
- every final/temp/control path declared before creation;
- temp same parent as final;
- temp embeds exact AttemptId;
- one ownership namespace across finals/temps/controls;
- record source package fingerprint;
- record pre-existing target/prompts/recovery/app-lock baseline;
- capability probe paths predeclared before creation;
- no optional/random temp fallback;
- revalidate physical target + managed tree after reservation and before
  settings commit;
- revalidate source package immediately before Ready;
- only MigrationReadyGate may set ReadyToCommit;
- retry recovery validates expected source identity before deleting anything;
- final terminal verification occurs before marker retirement.

RESERVATION RULES
- do not infer ownership from check-then-create;
- record only directories actually created by this attempt;
- cleanup failures are reported;
- successful migration calls CommitRootOwnership so committed root is not
  treated as rollback residue;
- stale unlocked root .app.lock may be tolerated only after exclusive lock
  acquisition proves it is not active.

SETTINGS RULES
- use durable writer;
- reconcile exact settings temps under settings lease;
- strict JSON duplicate/unknown rejection;
- future schema preserved;
- dual primary+backup CAS token;
- transition precondition captured AFTER recovery/sync writes;
- compare+save under same settings lease;
- compare physical settings root to active physical root;
- never fallback to default root if configured custom root is unavailable.

PACKAGE RULES
- parsed library.json is NOT enough to call a library healthy;
- verify every active prompt body exists and is readable;
- never synchronize backup from incomplete primary;
- never promote incomplete backup;
- recovery success means metadata AND referenced bodies are usable.

NORMAL CRUD RULES
- catch-based rollback is not crash recovery;
- Create/Edit/Duplicate/Delete require durable library mutation journal;
- journal exists before first cross-file mutation;
- Create/Duplicate body-before-metadata crash must recover;
- Edit must durably preserve old body before replacement;
- Delete metadata commit may preserve body conservatively if backup authority
  cannot prove deletion safe;
- fresh startup recovery compares exact old/new hashes;
- unknown hash state => stop and preserve journal;
- unresolved mutation blocks additional writes and may force restart.

ORPHAN RULES
- never wildcard-delete orphan prompt files;
- current primary AND current backup must prove a GUID is unreferenced;
- future/unreadable backup preserves orphan;
- active journal protects its prompt;
- non-GUID .md files are foreign and preserved.

JSON RULES
- strict UTF-8;
- duplicate properties rejected case-insensitively;
- unknown properties rejected;
- nested library category/prompt objects validated before deserialization.

UI RULES
- Prompt title max = 160 Unicode text elements;
- do not use XAML MaxLength as domain validation;
- GetPrompts catches expected filesystem exceptions only;
- future library/settings schema errors are controlled dialog warnings;
- once data-root change commits, RestartRequired is monotonic;
- application shutdown is requested even if informational message or
  DialogResult behavior fails after commit.

TEST RULES
- add deterministic injected fault seams;
- add real Windows junction tests;
- add live directory-session-lease replacement tests;
- add restart-style on-disk crash fixtures using fresh recovery service
  instances;
- no Assert.Throws<Exception> for safety tests;
- negative tests prove byte preservation, not just throwing;
- exact mandatory test names must appear in TRX with Passed outcome;
- skipped/inconclusive mandatory tests are failures for acceptance.

CI RULES
Run focused categories:
FilesystemAuthority
PackageIntegrity
MutationRecovery
CrashRecovery
WindowsFilesystemIntegration
WpfIntegration
then full suite.
Run exact test-evidence verifier.
Run full suite five consecutive times for final acceptance.

RELEASE RULES
Do not fabricate PromptHelperLogo.svg.
If real approved logo exists:
verify SVG -> ICO -> EXE normalized 256x256 RGBA identity.
If absent:
code/product may pass, strict release remains BLOCKED.

PHASE EVIDENCE
After every phase report:
PHASE
START_HEAD
FILES_CHANGED
NEW_TYPES
REMOVED_OR_REPLACED_LEGACY_APIS
TESTS_ADDED
FOCUSED_COMMAND
RESULT_TOTAL
RESULT_PASSED
RESULT_FAILED
RESULT_SKIPPED
BUILD_WARNINGS
SOURCE_GREP_REVIEW
KNOWN_BLOCKERS

FINAL EVIDENCE
Use section 140 exactly.
Never claim a test/build/publish command you did not actually execute.
Never mark a finding PASS based only on source inspection when its required
Windows/runtime test was not executed.
```

---

# 160. Final stop conditions for the weak model

The implementation model must stop and report instead of guessing if any of these occurs:

```text
- current HEAD contains newer conflicting architecture it cannot reconcile;
- a future schema authority file is encountered during a destructive test;
- a path is unreadable and code cannot prove Missing;
- recovery state matches neither old nor new expected hashes;
- a foreign entry appears in migration target during recovery;
- source identity differs from interrupted migration manifest;
- both migration and normal mutation journals are present;
- mandatory Windows filesystem tests cannot execute;
- real production logo is absent for strict-release gate.
```

Stopping under these conditions is correct. Silent fallback or destructive guessing is not.

The final acceptance principle remains:

> If the process dies after any durable write, the next process must be able to determine the exact authoritative state from durable evidence, while every destructive cleanup must prove both ownership and physical containment before deletion.

