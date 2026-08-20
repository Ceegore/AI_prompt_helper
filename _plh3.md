# Prompt Helper — Third Bug-Hunting / Regression Audit (`_plh3.md`)

**Repository:** `Ceegore/AI_prompt_helper`  
**Branch:** `main`  
**Audited HEAD:** `72ccddd7653bd84651c9562cfeb8a8377c49510c`  
**Previous audited HEAD:** `d824e89d9092ec37c3b0d6b433d4c43a84746f3c`  
**Audit date:** 2026-08-20  
**Purpose:** Re-test after the `_plh2.md` repair commit, verify all seven second-pass findings, then hunt for regressions and previously missed defects.

---

# 1. Executive verdict

## Status

**NOT COMPLETELY CLEAN YET — BUT MUCH CLOSER.**

The new repair commit is substantially better than the previous state. I do **not** find a remaining Critical or High defect in the statically inspectable persistence/recovery paths.

All seven `_plh2.md` findings are materially addressed in implementation.

This third audit found **4 actionable remaining issues**:

| Severity | Count |
|---|---:|
| Critical | 0 |
| High | 0 |
| Medium | 1 |
| Low | 3 |

The remaining issues are:

1. **PLH3-001 — MEDIUM:** mandatory persistence regression coverage is still incomplete:
   - the required `Failed_write_does_not_modify_existing_target` atomic-writer test is absent;
   - the required non-`IOException` backup-exception test cannot currently be expressed because the fault writer is hard-coded to throw `IOException`;
   - the new ten-attempt GUID collision defense is not deterministically tested.
2. **PLH3-002 — LOW:** final destination-label uniqueness can still fail after the GUID suffix reaches all 32 hexadecimal characters.
3. **PLH3-003 — LOW:** the fatal WPF dispatcher handler calls `Shutdown()` but leaves `e.Handled == false`, so WPF still performs its default unhandled-exception processing instead of the intended controlled shutdown path.
4. **PLH3-004 — LOW:** non-empty category rejection text still deviates from the exact implementation-locked message.

There is also an important **verification limitation**:

- the current HEAD has **no GitHub commit status checks**;
- the current HEAD has **no GitHub Actions workflow runs**;
- there is no `.github` workflow directory in the repository root;
- this audit environment does not have the Windows/.NET WPF toolchain needed to execute the mandatory build/test/publish/manual GUI matrix.

Therefore the current repository should **not yet be labeled release-validated**, even after the four items above are fixed.

---

# 2. What changed since `_plh2.md`

The current commit explicitly targets the seven second-pass findings.

The changed implementation areas include:

```text
src/PromptHelper/App.xaml.cs
src/PromptHelper/MainWindow.xaml.cs
src/PromptHelper/PromptHelper.csproj
src/PromptHelper/Services/LibraryStartupService.cs
src/PromptHelper/Services/PromptLibraryService.cs
src/PromptHelper/Services/PromptRepository.cs
tests/PromptHelper.Tests/AuditDefectRegressionTests.cs
```

I did not trust the commit message as proof. Each affected path was re-read and compared against the implementation-locked plan.

---

# 3. `_plh2.md` finding status

| Finding | Third-pass status | Evidence/conclusion |
|---|---|---|
| PLH2-001 — mutable live service records | **FIXED** | `CurrentDocument` is cloned; `GetCategories()` returns cloned records; create-result records are cloned. |
| PLH2-002 — blanket recoverable exception handling | **FIXED IN CORE**, minor fatal-shutdown issue remains | UI mutation handlers now use filtered recoverable exception catches. Unknown dispatcher errors are no longer marked “handled and continue”. See PLH3-003 for the remaining shutdown detail. |
| PLH2-003 — lost deterministic ordering | **FIXED** | Category order is SortOrder → Name OrdinalIgnoreCase → Id; prompt order is SortOrder → Id; overflow resequencing uses the same tie-breaks. |
| PLH2-004 — missing 0.1.0 version | **FIXED** | `<Version>0.1.0</Version>` and `<InvariantGlobalization>false</InvariantGlobalization>` are restored. |
| PLH2-005 — lost unique prompt GUID defense | **FIXED IN CODE** | Ten-attempt metadata/orphan collision check is restored. Deterministic regression coverage is still missing; see PLH3-001. |
| PLH2-006 — destination sorting before disambiguation | **FIXED** | Categories are now sorted by final `DisplayPath` after disambiguation. |
| PLH2-007 — residual `File.Exists` classification traps | **FIXED IN THE IMPORTANT PATHS** | Prompt existence/update and initialization marker checks now use real file opens and distinguish true missing files from other I/O failures. |

---

# 4. Positive third-pass findings

The following areas now look materially correct from static/control-flow inspection.

## 4.1 Private document state

The service now follows a much safer API boundary.

`GetCategories()` constructs fresh `CategoryRecord` objects rather than returning the records stored in `_document`.

Create operations likewise return fresh records.

That closes the mutation bypass identified in `_plh2.md`.

---

## 4.2 Deterministic ordering

Current category ordering:

```text
SortOrder
Name OrdinalIgnoreCase
Id
```

Current prompt ordering:

```text
SortOrder
Id
```

The same tie-breaks are used during sort-overflow resequencing.

This now matches the locked plan.

---

## 4.3 Move destination-end sorting

`MovePrompt()` computes the destination sort order while excluding the moving prompt.

Moving a high-sort prompt into an empty destination therefore produces:

```text
SortOrder = 10
```

instead of carrying source sort inflation into the destination.

---

## 4.4 Backup warnings

Startup backup synchronization failures now produce a user-visible warning rather than disappearing.

Clean initialization also propagates `CommitResult.Warning`.

---

## 4.5 Empty/whitespace metadata recovery

Empty and whitespace-only JSON is now classified as corrupt JSON and can recover from a valid backup.

This is covered by regression tests.

---

## 4.6 Metadata missing-vs-I/O distinction

The primary/backup metadata reader now directly attempts the read and maps only:

```text
FileNotFoundException
DirectoryNotFoundException
```

to Missing.

Permission and unrelated I/O failures remain real failures.

This matches the safety rule in the implementation plan.

---

## 4.7 Prompt-file state classification

`PromptRepository.Exists()` now attempts to open the file.

`Update()` similarly probes the actual file and only translates true file/directory absence into `FileNotFoundException`.

This is substantially safer than the old `File.Exists` precheck behavior.

---

## 4.8 UI mutation boundaries

Normal user mutations now catch a filtered set of expected recoverable errors rather than every `Exception`.

Create/Edit prompt text is kept in the retry loop when the save fails.

That addresses the major practical data-entry risk from the first audit.

---

## 4.9 Publish notices and version metadata

The application project now contains:

```xml
<Version>0.1.0</Version>
<InvariantGlobalization>false</InvariantGlobalization>
```

and links:

```text
LICENSE
THIRD_PARTY_NOTICES.md
```

with `CopyToPublishDirectory`.

The static project configuration is now aligned with the plan.

---

# 5. Remaining findings

# PLH3-001 — MEDIUM — Mandatory persistence regression coverage is still incomplete

## Affected files

```text
tests/PromptHelper.Tests/AtomicTextWriterTests.cs
tests/PromptHelper.Tests/FaultInjectingAtomicTextWriter.cs
tests/PromptHelper.Tests/AuditDefectRegressionTests.cs
src/PromptHelper/Services/PromptLibraryService.cs
```

## Why this matters

The implementation plan does not merely recommend these tests.

It explicitly makes them mandatory.

Two particularly important persistence guarantees still lack the required executable regression tests.

---

## Problem A — required atomic failure test is absent

The locked test list requires:

```text
Failed_write_does_not_modify_existing_target
```

Current `AtomicTextWriterTests.cs` contains:

```text
Write_new_file_creates_content
Replace_existing_file_changes_content
Unicode_round_trip
Markdown_round_trip
No_tmp_left_after_success
```

but no failed-replacement test.

That means the most important property of the atomic writer is not directly exercised:

```text
existing target contains OLD
↓
attempt replacement
↓
replacement fails
↓
existing target must still contain OLD
```

The writer implementation appears sensible from inspection, but this guarantee is important enough that the plan explicitly requires a test.

### Recommended Windows test

1. Write an existing target containing known original content.
2. Hold the target with a Windows sharing mode that prevents replacement/deletion.
3. Call `AtomicTextWriter.Write()` with new content.
4. Assert an exception.
5. Assert original target content is unchanged.
6. Assert temporary file cleanup was attempted/no expected temp remains after the lock is released.

The exact locking mechanism should be verified on the supported Windows/.NET runtime.

---

## Problem B — mandatory non-IOException backup test is impossible with the current fault writer

The implementation plan explicitly requires an injected backup failure where the writer may throw:

```text
any ordinary Exception
not only IOException
```

The purpose is to protect the commit-point invariant:

```text
primary committed
+
unexpected backup exception
→ primary remains committed
→ service state remains committed
→ warning only
→ no logical rollback
```

But the test helper currently hard-codes:

```csharp
throw new IOException("Injected write failure.");
```

So every backup-failure test exercises only an `IOException`.

The production `LibraryRepository.Commit()` uses a deliberately broad backup catch, which looks correct, but the required regression protection for that broad catch is absent.

### Required fix

Make the fault writer capable of throwing a supplied exception.

For example conceptually:

```csharp
public Func<string, int, Exception?>? FailureFactory { get; set; }
```

Then:

```csharp
Exception? failure = FailureFactory?.Invoke(path, callNumber);
if (failure != null)
{
    throw failure;
}
```

Add a test using something such as:

```text
InvalidOperationException
```

on the backup write and assert:

```text
primary committed
backup not synchronized
operation returns warning
in-memory candidate committed
created prompt file retained
no rollback performed
```

---

## Problem C — the restored ten-attempt GUID collision loop has no deterministic regression test

The code now correctly contains the required ten-attempt structure:

```text
generate GUID
reject metadata collision
reject orphan-file collision
retry
up to ten attempts
```

However it directly calls:

```csharp
Guid.NewGuid()
```

so the collision branches cannot realistically be forced in a deterministic test.

The second-pass regression suite therefore verifies the existence of the implementation only indirectly.

### Recommended fix

Introduce a tiny internal/testable GUID-generation seam, for example:

```text
Func<Guid>
```

with production default:

```csharp
Guid.NewGuid
```

Then add:

```text
first GUID collides with PromptRecord → retry
first GUID collides with orphan .md → retry
multiple collisions then free GUID → succeeds
ten collisions → InvalidOperationException
no collided/orphan file is overwritten
```

This is lower risk than Problems A/B but belongs in the same persistence-regression gap.

---

## Severity

**MEDIUM**

The production code does not currently demonstrate a direct data-loss bug here.

The severity comes from the fact that these are explicitly mandatory regression protections for the core persistence guarantees of the application.

A future refactor can break these invariants without the suite catching it.

---

# PLH3-002 — LOW — Destination display-label uniqueness can still fail after full GUID suffix exhaustion

## Affected code

```text
PromptLibraryService.GetDestinations()
```

## Current algorithm

For a collision, the service creates:

```text
<raw path> [first 8 GUID hex chars]
```

If that is already used, it extends the GUID suffix:

```text
8
12
16
20
24
28
32
```

characters.

The loop condition is effectively:

```csharp
while (usedPaths.Contains(candidatePath) && suffixLen < 32)
{
    ...
}
```

After `suffixLen == 32`, the code does not perform a fallback if the full-GUID label is also already occupied.

It then calls:

```csharp
usedPaths.Add(candidatePath);
```

without checking whether `Add()` returned `false`, and still adds the duplicate display record.

---

## Deterministic valid-document reproduction

Use a category with ID:

```text
aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa
```

and raw display path:

```text
Home
```

Before it in the valid metadata list, create sibling categories literally named:

```text
Home [aaaaaaaa]
Home [aaaaaaaaaaaa]
Home [aaaaaaaaaaaaaaaa]
Home [aaaaaaaaaaaaaaaaaaaa]
Home [aaaaaaaaaaaaaaaaaaaaaaaa]
Home [aaaaaaaaaaaaaaaaaaaaaaaaaaaa]
Home [aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa]
```

These are all:

- legal category names;
- below the 80-text-element limit;
- distinct sibling names;
- valid metadata.

When the `Home` category is processed:

1. it collides with logical root `Home`;
2. its 8-character generated label is occupied;
3. its 12-character generated label is occupied;
4. ...
5. its 32-character generated label is occupied;
6. the loop cannot extend further;
7. the already-used full-GUID label is still emitted.

Result:

```text
two DestinationRecord values
with identical DisplayPath
but different CategoryId
```

Internal routing remains safe because the IDs differ, but the UI becomes visually ambiguous.

---

## Why the existing regression test misses it

The current regression test verifies one generated suffix/literal collision and ordinary final-path sorting.

That is good coverage for the common case.

It does not cover terminal suffix exhaustion.

---

## Required fix

After the GUID reaches 32 characters, keep guaranteeing uniqueness.

For example:

```text
<raw> [full-guid]
<raw> [full-guid] #2
<raw> [full-guid] #3
...
```

or another deterministic fallback.

Most importantly:

```csharp
if (!usedPaths.Add(candidatePath))
{
    // continue deterministic disambiguation
}
```

Do not add the destination until `HashSet.Add()` succeeds.

### Regression test

Construct the deterministic suffix staircase above and assert:

```text
all final DisplayPath values unique OrdinalIgnoreCase
Home first
remaining labels sorted by final DisplayPath
CategoryId mapping preserved
```

---

## Severity

**LOW**

This requires deliberately adversarial but valid metadata.

It does not route a move to the wrong category because selection still carries the full `CategoryId`.

It is nevertheless a real failure of the promised global display-label uniqueness invariant.

---

# PLH3-003 — LOW — Fatal dispatcher handler does not mark the exception handled before controlled shutdown

## Affected code

```text
src/PromptHelper/App.xaml.cs
```

## Current handler

The application now correctly avoids the previous behavior of swallowing an unknown exception and continuing.

It shows:

```text
An unexpected error occurred and Prompt Helper must close
```

then calls:

```csharp
Shutdown();
```

However it does not set:

```csharp
e.Handled = true;
```

---

## WPF behavior

Microsoft's WPF documentation states that if a `DispatcherUnhandledException` handler leaves `Handled` false, WPF still considers the exception unhandled and returns to its default unhandled-exception processing.

The default path terminates the application.

The WPF application-management documentation further notes that when an exception remains unhandled, shutdown is immediate and other normal `Application` events are not raised.

That undermines the comment:

```text
shutdown safely
```

because `OnExit` is the place where this app explicitly disposes its application lock.

The OS will release process handles when the process terminates, so this is not a persistent-lock/data-loss defect.

But the custom “show fatal message, then perform controlled shutdown” path is not actually complete.

Depending on runtime behavior, the user may also still see framework/default unhandled-exception processing after the custom message.

---

## Required fix

For the “fatal but controlled” policy:

```csharp
private void App_DispatcherUnhandledException(
    object sender,
    DispatcherUnhandledExceptionEventArgs e)
{
    try
    {
        MessageBox.Show(...);
    }
    finally
    {
        e.Handled = true;
        Shutdown();
    }
}
```

The important distinction from the old PLH2 defect is:

```text
e.Handled = true
does NOT mean continue running
```

when the handler immediately calls `Shutdown()`.

It only prevents WPF from re-processing the same exception through its default unhandled path while the application deliberately exits.

A robust fatal handler should also avoid doing complex/resource-heavy work.

---

## Regression/manual test

Inject an unexpected dispatcher exception in a dedicated test/debug harness and verify:

```text
one fatal message
application closes
normal controlled Exit path is reached
app lock is disposed
next instance can immediately acquire lock
no continued normal UI operation
```

---

## Severity

**LOW**

Unknown fatal exceptions are now correctly fatal rather than silently swallowed.

The remaining defect is in shutdown cleanliness/control, not normal persistence behavior.

---

# PLH3-004 — LOW — Category non-empty rejection message still violates the locked UI text

## Affected code

```text
PromptLibraryService.CanDeleteCategory()
```

## Locked requirement

The implementation plan specifies the non-empty category message as:

```text
This category is not empty.

Move or delete its prompts and subcategories first.
```

## Current behavior

The exact required message is returned only when the category has both:

```text
subcategories
AND
prompts
```

For a category containing only subcategories, current text is:

```text
This category has subcategories.

Move or delete its subcategories first.
```

For a category containing only prompts:

```text
This category contains prompts.

Move or delete its prompts first.
```

These messages are arguably understandable, but the plan is explicitly implementation-locked and specifies the visible wording.

The existing PLH-012 regression test only checks:

```text
CanDeleteCategory == false
reason != null
```

so it does not detect this divergence.

---

## Required fix

Use the one locked message for any non-empty condition:

```csharp
if (hasSubcategories || hasPrompts)
{
    reason =
        "This category is not empty.\r\n\r\n" +
        "Move or delete its prompts and subcategories first.";
    return false;
}
```

### Regression tests

Assert exact text for:

```text
prompts only
subcategories only
both prompts + subcategories
```

---

## Severity

**LOW**

There is no destructive behavior bug.

Deletion remains safely blocked.

This is an exact UX/specification conformance defect.

---

# 6. Items investigated and NOT reported as defects

## 6.1 `File.Exists` inside AtomicTextWriter

The writer still checks whether the target exists before choosing:

```text
File.Replace
vs
File.Move
```

This is part of the locked reference algorithm.

If access problems cause the check to return false, `File.Move` should still fail rather than silently replace an existing target.

The high-risk missing-vs-I/O classification bugs were in startup/repository logic and have been repaired.

I am therefore not reopening PLH-003 merely because this implementation detail still exists.

---

## 6.2 Broad backup catch

`LibraryRepository.Commit()` catches any exception from the backup write after the primary commit point.

That broad catch is **intentional** and explicitly required by the plan.

Once `library.json` is committed, backup failure is warning-only.

The problem is missing non-IOException regression coverage, not the broad production catch itself.

---

## 6.3 Physical delete warning catch

After primary and backup both reflect a logical delete, physical `.md` deletion is cleanup only.

Converting cleanup exceptions to a warning is correct by design.

---

## 6.4 Stale initialization marker cleanup

Marker deletion is deliberately best-effort after authoritative metadata exists.

A failed stale-marker cleanup should not invalidate a valid library startup.

---

## 6.5 Prompt move sorting

The previous sort-inflation bug is fixed.

The moving prompt is excluded while computing its destination-end sort order.

---

## 6.6 Public object mutability

The previous private-state leak is fixed in the inspected public paths.

Returned category/create records are copies rather than live references into `_document`.

---

## 6.7 Version metadata

`0.1.0` is restored.

The previous release/help-version regression is no longer present.

---

## 6.8 Move-dialog Enter/Escape semantics

Current XAML has:

```text
Cancel: IsCancel=True
Action: IsDefault=True
```

which matches:

```text
Enter → Move/Copy
Escape → Cancel
```

---

## 6.9 Prompt-editor Enter behavior

The prompt editor Save button is not `IsDefault`.

This preserves Enter as editor input/newline rather than accidental Save.

---

# 7. Test-suite status

The automated suite is now broader than it was in `_plh1.md`.

Useful regression coverage now exists for:

```text
zero-byte primary recovery
whitespace primary recovery
valid-primary backup warning
first-run backup warning
global destination uniqueness/common collision case
final destination-path sorting
move-to-empty exact SortOrder
category delete precheck
missing edit file path
defensive public clones
deterministic category tie ordering
deterministic prompt tie ordering
assembly version 0.1.0
80/81 Unicode text-element boundary
60k prompt lifecycle
deep hierarchy
primary/backup corruption safety
```

That is meaningful improvement.

However the suite is still not at the plan's mandatory completion state because of PLH3-001.

---

# 8. Release-validation evidence is still missing

This is separate from the four source/test findings.

The current HEAD has:

```text
GitHub commit statuses: none
GitHub workflow runs: none
```

The repository root has no:

```text
.github/
```

workflow directory.

This audit environment also lacks the Windows WPF/.NET execution environment, so I cannot truthfully claim to have run:

```powershell
dotnet restore
dotnet build
dotnet test
dotnet build -c Release
dotnet test -c Release
dotnet publish ...
```

or:

```text
real WPF startup
clipboard
900×600
125% DPI
150% DPI
keyboard QA
two-process lock QA
offline GUI smoke
published executable smoke
```

Therefore:

```text
STATIC / CONTROL-FLOW AUDIT
≈ close to clean

ACTUAL RELEASE QA
NOT PROVEN
```

---

# 9. Required final repair order

## 1. Fix PLH3-001 first

Complete the mandatory persistence regression suite.

At minimum:

```text
Failed_write_does_not_modify_existing_target

Unexpected_non_IO_backup_exception_is_warning_only
```

Also add deterministic GUID-generation collision tests if practical.

## 2. Fix PLH3-002

Make destination disambiguation provably total.

Never emit a label unless:

```csharp
usedPaths.Add(label) == true
```

## 3. Fix PLH3-003

For fatal dispatcher errors:

```text
show one fatal message
mark handled
Shutdown
```

Do not continue the app.

## 4. Fix PLH3-004

Use the exact locked non-empty category message and test it.

---

# 10. Mandatory post-fix execution

On Windows 11 with a stable .NET 10 SDK:

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

Then execute the published app outside the IDE.

At minimum verify:

```text
[ ] fresh first start
[ ] second start does not duplicate defaults
[ ] create/edit/delete/move/duplicate
[ ] prompt text survives injected save failure
[ ] failed atomic replacement preserves prior content
[ ] ordinary non-IOException backup failure becomes warning only
[ ] primary remains commit point
[ ] backup failure never triggers rollback
[ ] destination labels unique under suffix-exhaustion adversarial document
[ ] exact non-empty category message
[ ] fatal dispatcher exception produces one controlled shutdown
[ ] immediate restart reacquires app lock
[ ] corrupt primary recovery
[ ] corrupt primary + corrupt backup safety stop
[ ] future schema safety stop
[ ] orphan preservation
[ ] missing prompt behavior
[ ] Unicode
[ ] Markdown
[ ] 60k prompt
[ ] deep hierarchy
[ ] 900×600
[ ] 125% DPI
[ ] 150% DPI
[ ] keyboard
[ ] clipboard
[ ] offline
[ ] LICENSE in publish output
[ ] THIRD_PARTY_NOTICES.md in publish output
[ ] published EXE start/navigate/edit/copy/move/restart smoke
```

---

# 11. Final third-pass conclusion

The repository has improved substantially across the three rounds.

The original serious issues around:

```text
primary metadata recovery
backup warning loss
File.Exists startup classification
zero-byte recovery
normal mutation crash behavior
private live-state exposure
move sort inflation
publish notices
version metadata
deterministic ordering
```

are now materially repaired.

I do **not** currently see a source-confirmed High or Critical defect in the main persistence/recovery workflow.

However the repository is **not yet issue-free**.

Four concrete items remain:

```text
1 Medium
3 Low
```

and actual release/build/manual-WPF validation remains unproven.

## Third-pass status

```text
CRITICAL: 0
HIGH:     0
MEDIUM:   1
LOW:      3

STATIC ACCEPTANCE:
NOT YET

RELEASE ACCEPTANCE:
NOT YET

NEXT ACTION:
repair PLH3-001 through PLH3-004,
run the complete Windows/.NET test + publish matrix,
then perform one final regression audit.
```
