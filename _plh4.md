# Prompt Helper — Fourth Bug-Hunting / Regression Audit (`_plh4.md`)

**Repository:** `Ceegore/AI_prompt_helper`  
**Branch:** `main`  
**Audited HEAD:** `8fe9b7483da93fece79c7c8e6e1ceb714ba14e51`  
**Previous audited HEAD:** `72ccddd7653bd84651c9562cfeb8a8377c49510c`  
**Audit date:** 2026-08-20  
**Purpose:** Re-test after the `_plh3.md` repair commit, verify all four third-pass findings, and perform another adversarial repository-wide bug hunt.

---

# 1. Executive verdict

## Status

**NOT CLEAN YET.**

The repository continues to improve substantially. The production fixes for all four `_plh3.md` findings are present, and I found **no confirmed Critical or High defect** in this pass.

However, this fourth audit found **2 concrete Medium-severity issues**:

| Severity | Count |
|---|---:|
| Critical | 0 |
| High | 0 |
| Medium | 2 |
| Low | 0 |

The remaining confirmed issues are:

1. **PLH4-001 — MEDIUM:** the newly added full-GUID destination-collision regression test is constructed in the wrong order and should fail deterministically. It does not actually exercise the fallback it claims to test, yet asserts that the fallback must occur.
2. **PLH4-002 — MEDIUM:** startup still reads `library.backup.json` before taking the “valid primary always wins” branch. Therefore an unreadable/locked safety backup can prevent the application from opening a perfectly valid `library.json`, even though backup synchronization failure is supposed to be nonfatal when the primary is valid.

There is still no GitHub CI evidence for this HEAD:

```text
commit status checks: 0
workflow runs:        0
```

The audit environment also does not provide the required Windows/.NET WPF execution environment, so actual compilation, MSTest execution, publish, clipboard, DPI, and GUI smoke testing could not be truthfully claimed.

The source is now close, but **this HEAD cannot be accepted as clean** because the test suite contains a logically failing test and the startup flow still has one demonstrated behavior defect.

---

# 2. Current HEAD and scope

The current `main` HEAD remained stable for this entire audit:

```text
8fe9b7483da93fece79c7c8e6e1ceb714ba14e51
```

Commit message:

```text
fix: resolve all remaining findings from third audit report _plh3.md
```

The commit changed:

```text
_plh3.md
src/PromptHelper/App.xaml.cs
src/PromptHelper/Services/PromptLibraryService.cs
tests/PromptHelper.Tests/AtomicTextWriterTests.cs
tests/PromptHelper.Tests/AuditDefectRegressionTests.cs
tests/PromptHelper.Tests/FaultInjectingAtomicTextWriter.cs
```

I reviewed those changes directly and then re-inspected the surrounding startup, repository, service, ViewModel, WPF UI, persistence, and test code.

---

# 3. `_plh3.md` finding verification

| Prior finding | Fourth-pass status | Result |
|---|---|---|
| PLH3-001 — missing persistence regression tests | **FIXED IN INTENT/IMPLEMENTATION** | Atomic failed-write test exists; fault writer can inject arbitrary exception; deterministic prompt-GUID collision seam/tests exist. |
| PLH3-002 — destination suffix exhaustion | **PRODUCTION FIXED, TEST BROKEN** | Production fallback now continues with `#2`, `#3`, etc. until unique. The new regression test does not actually create suffix exhaustion and is itself failing. See PLH4-001. |
| PLH3-003 — dispatcher fatal handler not marked handled | **FIXED** | Handler sets `e.Handled = true` in `finally` and immediately calls `Shutdown()`. |
| PLH3-004 — non-empty category text mismatch | **FIXED** | `CanDeleteCategory()` now returns the exact locked text for any non-empty category. |

---

# 4. Positive findings in this pass

## 4.1 Atomic failed-replacement coverage now exists

A test named:

```text
Failed_write_does_not_modify_existing_target
```

now:

1. writes original content;
2. opens the target with `FileShare.None`;
3. attempts replacement;
4. expects `IOException`;
5. verifies the original content remains intact;
6. verifies temporary files are cleaned.

That is the missing test requested by the previous audit.

This test still needs to be **actually executed on supported Windows/.NET**, but its structure matches the intended invariant.

---

## 4.2 Arbitrary backup exceptions can now be injected

`FaultInjectingAtomicTextWriter` now supports:

```csharp
Func<string, int, Exception?>? FailureFactory
```

This allows a test to inject:

```text
InvalidOperationException
```

rather than only `IOException`.

The new regression test confirms that a non-`IOException` thrown on the backup path becomes:

```text
BackupSynchronized = false
Warning != null
primary exists
```

instead of invalidating the already committed primary.

---

## 4.3 Prompt GUID collision handling is now deterministically testable

`PromptLibraryService` now accepts an optional:

```csharp
Func<Guid>
```

generator.

Production defaults to:

```csharp
Guid.NewGuid
```

while tests can provide a deterministic sequence.

New tests cover:

```text
metadata collision
→ retry

orphan-file collision
→ retry

free GUID
→ success

ten collisions
→ InvalidOperationException
```

The test also verifies that an existing orphan file is not overwritten.

This closes an important regression-coverage gap.

---

## 4.4 Destination production fallback is now total for processed labels

The production destination algorithm now does:

```text
8-char GUID suffix
12-char
16-char
20-char
24-char
28-char
32-char
```

and, if the 32-character form is already occupied:

```text
<full candidate> #2
<full candidate> #3
...
```

until an unused final display label is found.

That fixes the production defect reported in `_plh3.md`.

The remaining issue is the new regression test, not this fallback loop.

---

## 4.5 Fatal dispatcher handling is now controlled

Current fatal dispatcher policy is:

```text
show fatal message
↓
finally
↓
e.Handled = true
↓
Shutdown()
```

This avoids both undesirable outcomes:

```text
swallow + continue running
```

and:

```text
custom message + default WPF re-processing of same exception
```

The application deliberately exits.

---

## 4.6 Exact category deletion text now matches the locked plan

For any category containing either:

```text
direct child category
OR
direct prompt
```

the service now returns exactly:

```text
This category is not empty.

Move or delete its prompts and subcategories first.
```

The new regression test covers prompts-only and subcategories-only.

---

## 4.7 Earlier high-risk persistence fixes remain intact

This pass did not find a regression in:

```text
candidate-clone mutation semantics
library.json primary commit point
warning-only backup failure after commit
logical-delete-before-file-cleanup ordering
corrupt-primary recovery
future-schema protection
missing-vs-I/O metadata classification
zero-byte / whitespace metadata recovery
prompt-file missing-vs-I/O distinction
orphan preservation
move destination-end sorting
private in-memory state cloning
publish notice inclusion
0.1.0 version metadata
```

---

# 5. PLH4-001 — MEDIUM — New destination-exhaustion regression test should fail deterministically

## Affected file

```text
tests/PromptHelper.Tests/AuditDefectRegressionTests.cs
```

## Test

```text
PLH3002_Destination_paths_unique_even_with_32_char_guid_exhaustion
```

## Intended purpose

The test is meant to exercise this adversarial state:

```text
Home
Home [aaaaaaaa]
Home [aaaaaaaaaaaa]
Home [aaaaaaaaaaaaaaaa]
...
Home [aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa]
```

where the category named:

```text
Home
```

has ID:

```text
aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa
```

and therefore every generated GUID suffix from 8 through 32 characters is already occupied.

The expected final fallback is:

```text
Home [aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa] #2
```

That is a good edge case to test.

---

## Actual test setup

The test builds the categories in this order:

```text
1. Home                    (ID aaaaaaaa-....)
2. Home [aaaaaaaa]
3. Home [aaaaaaaaaaaa]
4. Home [aaaaaaaaaaaaaaaa]
5. ...
8. Home [32-char GUID]
```

The service processes `_document.Categories` in list order.

That means when category 1 (`Home`) is processed, `usedPaths` contains only:

```text
Home     # logical root
```

So its first generated candidate:

```text
Home [aaaaaaaa]
```

is still free.

It is accepted immediately.

No 12/16/20/.../32-character extension occurs.

No `#2` fallback occurs.

---

## What happens to the later literal category

When the second category with the literal name:

```text
Home [aaaaaaaa]
```

is processed, that raw path is now occupied.

It therefore receives a suffix based on **its own different GUID**, for example:

```text
Home [aaaaaaaa] [1f2e3d4c]
```

The other literal long-prefix categories remain distinct raw labels.

Again:

```text
no #2
```

is required.

---

## Why the test fails

At the end, the test asserts:

```csharp
Assert.IsTrue(
    destinations.Any(d => d.DisplayPath.EndsWith("#2")),
    "Expected #2 fallback destination label");
```

Given the test's own deterministic ordering, that condition is false.

The production implementation can be correct and this test will still fail.

Therefore this HEAD's test suite contains a **known logically failing test**.

---

## Correct reproduction

Put the literal blocker categories first:

```text
1. Home [aaaaaaaa]
2. Home [aaaaaaaaaaaa]
3. Home [aaaaaaaaaaaaaaaa]
4. ...
7. Home [32-char GUID]
8. Home     (ID aaaaaaaa-....)
```

Now, when the final `Home` category is processed:

```text
Home                      collides with root
Home [8]                  occupied
Home [12]                 occupied
Home [16]                 occupied
Home [20]                 occupied
Home [24]                 occupied
Home [28]                 occupied
Home [32]                 occupied
Home [32] #2              free
```

That actually exercises the terminal fallback.

---

## Required test repair

Change only the adversarial test data order, then strengthen the assertions.

Recommended assertions:

```text
1. Home root remains first.
2. All final labels are unique OrdinalIgnoreCase.
3. The exact category with ID aaaaaaaa-... receives a label ending in "#2".
4. Its CategoryId remains unchanged.
5. All category destinations remain sorted by final DisplayPath.
```

Do not merely assert:

```text
some destination ends with #2
```

because the test should prove that the intended collision subject reached the fallback.

---

## Severity

**MEDIUM**

This is not a production routing/data-loss defect.

However:

- it is a deterministic automated-test failure;
- it blocks the implementation plan's mandatory `dotnet test` gate;
- it falsely suggests the terminal fallback has executable coverage when the setup never reaches that path.

This must be fixed before claiming a clean automated suite.

---

# 6. PLH4-002 — MEDIUM — Valid primary can be blocked by an unreadable safety backup

## Affected file

```text
src/PromptHelper/Services/LibraryStartupService.cs
```

## Locked requirement

The authoritative implementation plan states:

```text
Primary valid | Backup any
→ use primary, synchronize backup
```

and separately:

```text
Valid primary always wins
```

The intended sequence is:

```text
read/validate primary
↓
primary valid
↓
load primary
↓
attempt backup synchronization
↓
backup failure becomes warning
```

This is consistent with the data model:

```text
library.json        = authoritative current metadata
library.backup.json = safety mirror
```

The backup is not authoritative when the primary is valid.

---

## Current control flow

Current startup does:

```csharp
MetadataReadResult primaryResult =
    ReadMetadataState(_paths.LibraryPath);

if (primaryResult is FutureSchema)
{
    throw ...
}

// backup is read unconditionally here
MetadataReadResult backupResult =
    ReadMetadataState(_paths.LibraryBackupPath);

// valid-primary branch only happens afterwards
if (primaryResult is Valid primaryValid)
{
    try
    {
        _libraryRepo.SynchronizeBackup(primaryValid.Document);
    }
    catch
    {
        backupWarning = ...
    }

    return primary;
}
```

The problem is the unconditional backup read before the valid-primary branch.

---

## Why this is a real defect

`ReadMetadataState()` deliberately converts only:

```text
FileNotFoundException
DirectoryNotFoundException
```

to `Missing`.

It deliberately allows:

```text
UnauthorizedAccessException
sharing/locking IOException
other I/O failure
security/path failure
```

to propagate.

That classification is correct when the backup is actually needed for recovery.

But when the primary is already valid, **the backup is not needed at all**.

The code should proceed directly to backup synchronization.

Instead, a transient inability to read the mirror can abort startup before synchronization is even attempted.

---

## Concrete Windows reproduction

Precondition:

```text
valid library.json
valid library.backup.json
```

Then:

1. Open `library.backup.json` in another process/test with a sharing mode that prevents reading, e.g. `FileShare.None`.
2. Keep `library.json` readable and valid.
3. Start Prompt Helper.

### Current result

The code reaches:

```text
ReadMetadataState(library.backup.json)
```

which throws an I/O/sharing exception.

The exception propagates out of startup.

The application shows a fatal startup error and closes.

### Required result

The application should:

```text
load valid library.json
attempt to overwrite/synchronize library.backup.json
that write fails
return nonfatal backup warning
show MainWindow
```

The user must still be able to use the valid authoritative primary library.

---

## Why existing tests miss this

There is already a useful test:

```text
PLH002_Valid_primary_with_backup_sync_failure_returns_warning
```

but it injects failure into the **backup writer**.

The existing backup file remains readable.

So current startup does:

```text
read backup successfully
↓
reach valid-primary branch
↓
injected SynchronizeBackup failure
↓
warning
```

That test does not exercise a read failure on the backup before the branch.

Other tests cover:

```text
valid primary + missing backup
valid primary + corrupt backup
```

but neither reproduces an unreadable/locked backup.

---

## Required code repair

Resolve the valid primary immediately after reading it.

Conceptually:

```csharp
MetadataReadResult primaryResult =
    ReadMetadataState(_paths.LibraryPath);

if (primaryResult is FutureSchema future)
{
    throw new UnsupportedLibrarySchemaException(...);
}

if (primaryResult is Valid primaryValid)
{
    string? warning = null;

    try
    {
        _libraryRepo.SynchronizeBackup(primaryValid.Document);
    }
    catch (Exception)
    {
        warning =
            "The library was loaded from library.json, " +
            "but its safety backup could not be synchronized.";
    }

    TryRemoveStaleMarker();

    return new StartupResult(
        primaryValid.Document,
        false,
        warning);
}

// Only now inspect backup.
// Primary is corrupt or missing and backup may actually be needed.
MetadataReadResult backupResult =
    ReadMetadataState(_paths.LibraryBackupPath);
```

This has another advantage:

```text
valid primary + future-schema-looking backup
```

will correctly use the primary and overwrite the stale/irrelevant mirror rather than allowing the mirror to influence startup.

---

## Required regression test

Add a Windows filesystem test:

```text
Valid_primary_with_locked_unreadable_backup_loads_and_warns
```

Suggested sequence:

```text
commit valid primary + backup
↓
open backup using FileShare.None
↓
call LoadOrInitialize
↓
assert no startup exception
assert result.Document == primary
assert RecoveredFromBackup == false
assert Warning != null
```

The backup write should fail while locked, producing the warning.

After releasing the lock, a second startup should synchronize the backup normally.

A companion test may verify:

```text
valid primary + backup path access problem
→ primary still loads
→ warning
```

where feasible without platform-fragile ACL setup.

---

## Severity

**MEDIUM**

This does not corrupt data.

The authoritative primary remains untouched.

But it is a real availability/startup correctness defect:

```text
a non-authoritative safety mirror can prevent access to a valid authoritative library
```

and it directly contradicts the “valid primary always wins” recovery model.

---

# 7. Areas re-audited without a new confirmed defect

## 7.1 Candidate-clone business mutations

The service continues to:

```text
clone current
modify candidate
validate
commit
swap current
```

rather than mutating live state and attempting rollback.

Good.

---

## 7.2 Prompt create rollback

Create flow remains:

```text
write .md
add metadata to candidate
commit candidate
```

If primary commit throws:

```text
best-effort delete new .md
rethrow
live metadata unchanged
```

Backup-only failure is returned by `Commit()` instead of thrown, so the new prompt is not incorrectly rolled back after primary commit.

Good.

---

## 7.3 Prompt delete ordering

Current flow:

```text
remove metadata candidate
commit primary
attempt backup
swap in-memory
```

Only when backup synchronized:

```text
attempt physical .md deletion
```

If backup failed:

```text
logical delete stays committed
.md retained
warning
```

This remains consistent with safe recovery semantics.

---

## 7.4 Prompt update missing-file behavior

`PromptRepository.Update()` attempts to open the real file and translates only true file/directory absence to:

```text
FileNotFoundException
```

Access/sharing failures remain real I/O failures.

Good.

---

## 7.5 Prompt availability probe

`PromptRepository.Exists()` now opens the path directly rather than using `File.Exists()`.

Only true missing-file/directory cases return `false`.

This preserves the distinction between:

```text
missing
```

and:

```text
present but inaccessible
```

in the safety-critical ID-generation path.

---

## 7.6 Future-schema protection

Primary future schema is still detected before ordinary v1 required-field validation and before any backup recovery.

The application does not restore an old schema-1 backup over a newer primary.

Good.

---

## 7.7 Empty/whitespace metadata

Whitespace-only or zero-byte JSON is classified as corruption and can recover from a valid backup.

Good.

---

## 7.8 Destination routing

Even under display-path collision handling, the actual destination identity remains the full:

```text
Guid? CategoryId
```

The display label is not used for routing.

Good.

---

## 7.9 Move prompt destination order

The moving prompt is excluded from the destination sibling set used to determine destination-end SortOrder.

This avoids source sort inflation.

Good.

---

## 7.10 Defensive public state

`CurrentDocument` returns a deep clone.

`GetCategories()` returns copied records.

Create prompt/category results return copies rather than the live record stored in `_document`.

No reopened live-state bypass was confirmed.

---

## 7.11 Category-name Unicode length

The domain validator still measures:

```text
Unicode text elements
```

rather than UTF-16 code units.

The UI no longer imposes the old `MaxLength=80` mismatch.

Good.

---

## 7.12 Breadcrumb and category-name overflow

Breadcrumbs have horizontal scrolling.

Category card names use ellipsis and expose the full name in the tooltip.

The previously reported long/deep path issue remains addressed.

---

## 7.13 Prompt editor semantics

Prompt editor remains:

```text
AcceptsReturn = true
AcceptsTab = true
NoWrap
horizontal scroll Auto
vertical scroll Auto
```

and Save is not the default button.

This matches the locked Enter semantics.

---

## 7.14 Clipboard

Copy re-reads current prompt content from disk before placing it on the clipboard.

Clipboard retry remains bounded.

No hidden success popup was introduced.

---

## 7.15 Hidden networking / execution

No intentional:

```text
HttpClient
Process.Start
AI API
WebView
telemetry
```

was found in the audited runtime source.

---

## 7.16 Publish metadata

The application project retains:

```text
Version 0.1.0
InvariantGlobalization false
LICENSE copy-to-output/publish
THIRD_PARTY_NOTICES.md copy-to-output/publish
```

Static project configuration remains correct.

---

# 8. Small observations deliberately not promoted to defects

## 8.1 Shared GUID test seam also affects category creation

The new `_guidGenerator` is used by both:

```text
GenerateUniquePromptGuid()
CreateCategory()
```

although it was primarily introduced to test prompt collision handling.

With the production default:

```text
Guid.NewGuid
```

this does not change production behavior.

A test supplying a deterministic generator needs to understand that category creation consumes the same source.

This is a testability/design detail, not currently a demonstrated product defect.

A cleaner design would use a dedicated private/internal prompt-ID generator seam, but that is not required to accept the product.

---

## 8.2 Startup warning title

A normal backup-sync warning may be shown under a title containing “Recovery Notice”.

That wording is not ideal for every warning type, but the user still receives the required warning and the locked plan does not require an exact title here.

Not promoted.

---

## 8.3 Public JsonSerializerOptions

`LibraryRepository.JsonOptions` is public static readonly even though the plan reference showed a private field.

No runtime caller in the repository mutates it, and this audit found no demonstrated behavior defect from the exposure.

Not promoted.

---

# 9. Automated-test assessment

The test suite is now meaningfully stronger.

It contains coverage for:

```text
strict required JSON
future schema
empty GUIDs
corrupt primary
double corruption
first-run initialization
interrupted initialization
unknown-file safety stop
backup write failure
delete cleanup failure
candidate-state behavior
large prompt lifecycle
deep hierarchy
Unicode text elements
deterministic visible ordering
move destination sort
defensive clone behavior
non-IOException backup exception
prompt GUID metadata collision
prompt GUID orphan collision
ten GUID collision exhaustion
failed atomic replacement
destination collision uniqueness
exact category non-empty text
```

However:

```text
PLH3002_Destination_paths_unique_even_with_32_char_guid_exhaustion
```

is logically constructed to fail its own `#2` assertion.

Therefore the current automated suite cannot be treated as green.

Even if all other tests pass, this test must be corrected.

---

# 10. CI / executable-validation status

For audited HEAD:

```text
GitHub combined status checks:
none

GitHub workflow runs:
none
```

No repository evidence proves:

```text
dotnet restore PASS
dotnet build PASS
dotnet test PASS
Release build PASS
Release tests PASS
win-x64 self-contained publish PASS
published executable smoke PASS
```

This audit environment also cannot execute the required Windows WPF stack.

Therefore:

```text
SOURCE/CONTROL-FLOW AUDIT:
2 confirmed Medium issues remain

ACTUAL TEST STATUS:
not executed here

CI TEST STATUS:
not available

RELEASE STATUS:
not proven
```

---

# 11. Required repair order

## Repair 1 — PLH4-001

Fix the adversarial destination test first, because current `dotnet test` should otherwise be red.

Move all literal suffix blockers before the `Home` collision subject.

Assert that the **specific** `Home` category receives `#2`.

---

## Repair 2 — PLH4-002

Change startup ordering:

```text
read primary
↓
future? fatal
↓
valid? use immediately + try backup synchronization
↓
only if corrupt/missing:
    inspect backup
```

Add a locked/unreadable-backup regression test.

---

## Repair 3 — execute real Windows gates

After source fixes:

```powershell
dotnet --info

dotnet restore

dotnet build

dotnet test

dotnet build -c Release

dotnet test -c Release

dotnet publish `
  src/PromptHelper/PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/publish/win-x64
```

All must exit zero.

---

# 12. Mandatory targeted regression checklist after fixes

At minimum:

```text
[ ] PLH3002 corrected test actually reaches #2 fallback
[ ] all destination labels unique OrdinalIgnoreCase
[ ] final destination labels sorted
[ ] correct CategoryId retained through disambiguation

[ ] valid primary + normal backup loads
[ ] valid primary + missing backup loads and recreates
[ ] valid primary + corrupt backup loads and replaces
[ ] valid primary + locked/unreadable backup loads and warns
[ ] second startup after lock release repairs backup

[ ] corrupt primary + valid backup recovers
[ ] corrupt primary + corrupt backup fails safely
[ ] missing primary + valid backup recovers
[ ] future primary never falls back

[ ] atomic failed replacement leaves old content
[ ] non-IOException backup exception is warning-only
[ ] prompt GUID metadata collision retries
[ ] prompt GUID orphan collision retries
[ ] ten prompt GUID collisions fail safely

[ ] create/edit/delete/move/duplicate
[ ] missing prompt behavior
[ ] orphan preservation
[ ] 60k prompt
[ ] Unicode
[ ] Markdown
[ ] clipboard
[ ] keyboard
[ ] 900×600
[ ] 125% DPI
[ ] 150% DPI
[ ] offline
[ ] second-process lock
[ ] LICENSE in publish folder
[ ] THIRD_PARTY_NOTICES.md in publish folder
[ ] published EXE restart persistence
```

---

# 13. Final fourth-pass conclusion

This iteration fixed the four production issues identified in `_plh3.md`.

The project is substantially closer to a clean state.

I found:

```text
Critical: 0
High:     0
Medium:   2
Low:      0
```

The two remaining issues are concrete and reproducible from source/control flow:

```text
PLH4-001
new destination-exhaustion regression test is deterministically wrong
and should fail

PLH4-002
valid authoritative primary is still unnecessarily dependent on
successfully reading the non-authoritative backup during startup
```

Neither is a data-loss defect.

But both prevent a “zero known issues / final” verdict:

- one means the mandatory automated test gate is not clean;
- the other means a valid library can be made unavailable by a safety-backup I/O problem.

## Fourth-pass status

```text
STATIC ACCEPTANCE:
FAIL — 2 confirmed issues

AUTOMATED TEST ACCEPTANCE:
FAIL / UNPROVEN — one logically failing test + no execution evidence

RELEASE ACCEPTANCE:
FAIL / UNPROVEN

NEXT:
repair PLH4-001 and PLH4-002,
run the complete Windows/.NET test and publish matrix,
then perform another final audit.
```
