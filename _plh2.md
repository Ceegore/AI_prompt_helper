# Prompt Helper — Second Bug-Hunting / Regression Audit (`_plh2.md`)

**Repository:** `Ceegore/AI_prompt_helper`  
**Branch:** `main`  
**Audited HEAD:** `d824e89d9092ec37c3b0d6b433d4c43a84746f3c`  
**Previous audited HEAD:** `c46419079eab56d0b66acf33e6e15d126b53d391`  
**Audit date:** 2026-08-20  
**Purpose:** Re-test after commit `fix: resolve all 14 issues from bug-hunting report _plh1.md`, verify the original findings, and hunt for regressions/new defects.

---

# 1. Executive verdict

**VERDICT: NOT CLEAN YET.**

The repair commit is meaningful and fixes most of the important defects from `_plh1.md`. In particular, the high-risk primary-metadata recovery path is materially safer now.

However, this second audit found **7 concrete remaining/new issues**:

| Severity | Count |
|---|---:|
| Critical | 0 |
| High | 0 |
| Medium | 2 |
| Low | 5 |

The most important remaining problems are:

1. **PLH-014 was only partially fixed:** the private in-memory document can still be mutated through `GetCategories()` results and through `OperationResult<T>.Value` returned by create/duplicate operations.
2. **The PLH-001 repair overcorrected into blanket exception swallowing:** WPF now marks every dispatcher exception as handled, and mutation handlers catch every `Exception`. Unknown programming faults can therefore be hidden while the application continues in an uncertain/partially refreshed state.
3. The rewrite of `PromptLibraryService` silently dropped required deterministic tie-break ordering.
4. The project version declaration `0.1.0` was accidentally removed, so release/help version metadata no longer follows the implementation plan.
5. The defensive unique-prompt-GUID algorithm was removed.
6. Move destinations are sorted by raw path before disambiguation instead of by final displayed path.
7. `File.Exists` classification traps still remain in some prompt/initialization paths.

A new full repair pass is required before declaring the repository final.

---

# 2. What was re-tested

This pass did **not** merely compare commit messages.

I re-inspected:

- the exact repair commit and file diff;
- current `App.xaml.cs`;
- current `MainWindow.xaml`;
- current `MainWindow.xaml.cs`;
- current `PromptLibraryService.cs`;
- current `PromptRepository.cs`;
- current `LibraryStartupService.cs`;
- current `LibraryRepository.cs`;
- current `FileDeleter.cs`;
- current `MainViewModel.cs`;
- current `OperationResults.cs`;
- current `NameDialog.xaml`;
- current `PromptHelper.csproj`;
- current `HelpDialog.xaml.cs`;
- the new `AuditDefectRegressionTests.cs`;
- the pre-existing service/recovery tests;
- the authoritative implementation-plan requirements for:
  - unique prompt IDs;
  - category ordering;
  - prompt ordering;
  - destination ordering;
  - application version;
  - persistence/recovery semantics.

I also checked GitHub status for the repaired HEAD:

- **no commit status checks are attached**;
- **no GitHub Actions workflow run exists for this commit**.

---

# 3. Dynamic execution limitation

The available execution container in this chat does **not** contain `dotnet`:

```text
dotnet: command not found
```

Therefore I cannot truthfully claim that I executed:

```text
dotnet restore
dotnet build
dotnet test
dotnet publish
```

or a real WPF GUI/DPI test in this environment.

This second report is therefore a **deep static/control-flow/regression audit**, not a fabricated build/test pass.

The repository itself also provides no CI result proving that the repaired commit builds/tests successfully.

This limitation must remain explicit until the Windows/.NET test matrix is actually run.

---

# 4. Status of the original 14 findings

| Original | Status | Second-pass conclusion |
|---|---|---|
| PLH-001 | **Mostly fixed, but repair introduced new error-handling risk** | Normal mutation failures are now caught and Create/Edit text is preserved. However blanket `catch (Exception)` + global `e.Handled = true` is unsafe. See PLH2-002. |
| PLH-002 | **Fixed** | Startup and initialization backup failures now produce warnings. |
| PLH-003 | **Core high-risk part fixed; residual remains** | Metadata reads and prompt-directory enumeration now distinguish missing from many real I/O failures. `File.Exists` is still used in other classification-sensitive paths. See PLH2-007. |
| PLH-004 | **Fixed** | Empty/whitespace JSON now becomes `JsonException`, allowing backup recovery. Regression tests were added. |
| PLH-005 | **Fixed** | Startup filesystem/composition is under the main startup exception boundary. |
| PLH-006 | **Fixed** | `FileDeleter` now calls `File.Delete` directly so real delete failures can surface. |
| PLH-007 | **Uniqueness fixed; sorting regression introduced** | Final labels are globally disambiguated, but result ordering is now based on raw paths, not final display paths. See PLH2-006. |
| PLH-008 | **Fixed** | `MaxLength="80"` was removed; domain validation owns Unicode text-element limit. |
| PLH-009 | **Static fix present; runtime QA still required** | Horizontal breadcrumb scrolling and category ellipsis/full-name tooltip were added. Actual 900×600 / 125% / 150% / keyboard behavior remains unexecuted here. |
| PLH-010 | **Fixed in project definition** | `LICENSE` and `THIRD_PARTY_NOTICES.md` are now linked with `CopyToPublishDirectory`. Actual publish folder still requires runtime verification. |
| PLH-011 | **Fixed** | Move calculation excludes the moving prompt. Regression test added. |
| PLH-012 | **Fixed** | Non-empty category is checked before destructive confirmation. |
| PLH-013 | **Primary diagnostic fixed; residual classifier issue remains** | Missing edit path now reports the actual `.md` filename, but `Update()` still uses `File.Exists`. See PLH2-007. |
| PLH-014 | **PARTIALLY FIXED** | Constructor input and `CurrentDocument` are cloned, but other APIs still expose live mutable records. See PLH2-001. |

---

# 5. New / remaining findings

## PLH2-001 — MEDIUM — PLH-014 is incomplete: public APIs still expose live mutable internal records

### Affected code

`src/PromptHelper/Services/PromptLibraryService.cs`

### What was fixed

The service now does:

```csharp
_document = LibraryDocumentCloner.Clone(initialDocument);

public LibraryDocument CurrentDocument =>
    LibraryDocumentCloner.Clone(_document);
```

That correctly prevents mutation through:

- the constructor's original `initialDocument`;
- `CurrentDocument`.

The new regression test checks exactly those two paths.

### What remains broken

Other public APIs still return references to objects stored directly inside `_document`.

#### Leak A — `GetCategories`

Current behavior is effectively:

```csharp
return _document.Categories
    .Where(...)
    .OrderBy(...)
    .ToList();
```

`ToList()` creates a new **list**, but it does **not** clone the contained `CategoryRecord` instances.

Therefore:

```csharp
var cat = service.GetCategories(null)[0];
cat.Name = "";
```

mutates the private canonical `_document` without:

- validation;
- primary persistence;
- backup persistence;
- operation transaction semantics.

#### Leak B — `CreateCategory`

`CreateCategory()`:

1. creates `newCategory`;
2. adds that same instance to `candidate.Categories`;
3. assigns `_document = candidate`;
4. returns `newCategory` inside `OperationResult<CategoryRecord>`.

The returned object is therefore the exact live instance inside `_document`.

Reproduction:

```csharp
var result = service.CreateCategory(null, "Good");
result.Value.ParentId = result.Value.Id;
```

The service's in-memory hierarchy is now self-parenting/corrupt without a commit or validation pass.

#### Leak C — `CreatePrompt`

The same issue exists for `CreatePrompt()`.

The returned `PromptRecord` is the same object stored inside the new `_document`.

A caller can mutate:

```text
Id
CategoryId
SortOrder
```

without validation or persistence.

`DuplicatePrompt()` delegates to `CreatePrompt()`, so it inherits the same leak.

### Impact

**Medium.**

The current WPF ViewModel does not intentionally mutate these objects, so this is not an immediate click-to-corrupt-user-data path.

But the service invariant claimed by the PLH-014 fix is false.

Any future caller/test/UI refactor can:

- mutate private canonical state;
- make UI state diverge from disk;
- introduce invalid category cycles;
- change IDs;
- break later operations;
- cause the next validated mutation to fail;
- lose the unpersisted mutation after restart.

### Required fix

Never return live mutable records from `_document`.

Recommended:

```text
GetCategories
→ return cloned CategoryRecord DTOs or immutable records

CreateCategory result
→ return clone of newCategory

CreatePrompt result
→ return clone of newPrompt

DuplicatePrompt
→ inherits safe cloned result
```

Better long-term:

- immutable public DTO/record projections;
- mutable persistence entities remain private to the service.

### Missing regression tests

Add:

```text
GetCategories_result_cannot_mutate_internal_document
CreateCategory_result_cannot_mutate_internal_document
CreatePrompt_result_cannot_mutate_internal_document
DuplicatePrompt_result_cannot_mutate_internal_document
```

The existing PLH-014 test is insufficient.

---

## PLH2-002 — MEDIUM — Error-handling fix now blanket-swallows arbitrary programming exceptions

### Affected code

- `src/PromptHelper/App.xaml.cs`
- `src/PromptHelper/MainWindow.xaml.cs`

### Global dispatcher problem

The application now subscribes:

```csharp
DispatcherUnhandledException += App_DispatcherUnhandledException;
```

and the handler does:

```csharp
MessageBox.Show(...);
e.Handled = true;
```

for **every** dispatcher exception.

This tells WPF:

```text
the exception was handled
continue running
```

regardless of the exception type or application state.

### UI mutation problem

The repaired UI handlers also use broad:

```csharp
catch (Exception ex)
```

around category/prompt mutations.

That includes more than expected filesystem/domain failures.

It can swallow:

- programming errors;
- invariant bugs;
- unexpected framework exceptions;
- unexpected ViewModel refresh failures;
- potentially serious runtime exceptions that should not be treated as an ordinary "save failed".

### Why this is a bug

The previous audit explicitly recommended:

```text
catch expected recoverable persistence failures
do not blanket-swallow arbitrary programmer bugs
```

The repair solved the crash path by catching **everything**, which moves the problem in the opposite direction.

An unexpected exception can happen after some portion of a UI operation has already completed.

Example structure:

```text
service mutation commits
↓
MainViewModel.Refresh()
↓
unexpected exception
↓
MainWindow catches Exception
↓
UI tells user operation failed
```

The underlying operation may actually already be committed.

For Create/Edit, the repair loop may then reopen the editor and encourage a retry.

If a post-commit exception was the true failure point, retrying a Create can create a second prompt.

Even where no duplicate occurs, continuing after an unknown dispatcher exception can leave:

- partially refreshed collections;
- mismatched navigation state;
- inconsistent visual state;
- repeated exception/message-box loops.

### Required fix

Use two levels:

#### User-operation boundary

Catch only known expected/recoverable exceptions, for example:

```text
IOException
UnauthorizedAccessException
SecurityException
expected InvalidOperationException domain failures
```

Do not treat arbitrary `Exception` as an ordinary mutation failure.

#### Global dispatcher boundary

The global handler can:

1. show a final unexpected-error message;
2. optionally log diagnostic details locally;
3. **shut down safely / allow fatal handling**.

It should not automatically set every unknown dispatcher exception as handled and continue normal operation.

### Required tests

- expected primary-write `IOException` -> app stays alive;
- expected permission error -> app stays alive;
- unknown injected programmer exception -> not silently treated as recoverable Save error;
- post-commit refresh failure must not tell the user to retry a Create as though nothing committed;
- global unhandled exception must not leave app pretending to be healthy.

---

## PLH2-003 — LOW — Deterministic category/prompt tie-break ordering was removed by the rewrite

### Affected code

`src/PromptHelper/Services/PromptLibraryService.cs`

### Authoritative behavior

The implementation plan explicitly requires:

```text
Category visible ordering:
SortOrder ascending
then Name OrdinalIgnoreCase
then Id
```

and:

```text
Prompt visible ordering:
SortOrder ascending
then Id
```

Duplicate `SortOrder` values are explicitly legal.

### Current category query

Current code uses only:

```csharp
.OrderBy(c => c.SortOrder)
```

The required:

```text
ThenBy Name
ThenBy Id
```

are gone.

### Current prompt query

Current code uses only:

```csharp
.OrderBy(p => p.SortOrder)
```

The required:

```text
ThenBy Id
```

is gone.

### Overflow resequencing also regressed

`CalculateNextCategorySortOrder()` now resequences with only:

```csharp
siblings.OrderBy(c => c.SortOrder)
```

instead of the required deterministic category visible order.

`CalculateNextPromptSortOrder()` similarly uses only `SortOrder`.

### Reproduction

Load a valid library with two sibling categories:

```text
B  SortOrder 10
A  SortOrder 10
```

The validator allows this.

Required display:

```text
A
B
```

Current display follows underlying collection order:

```text
B
A
```

Likewise two prompts with equal sort order should be secondarily ordered by GUID; current code retains list order.

### Impact

**Low**, because normal app-created entries usually receive different sort orders.

Still, duplicate sort values are explicitly legal, so this is a real specification regression and can be visible after:

- loaded metadata containing ties;
- imported/future-generated metadata;
- edge-case resequencing;
- manual data repair.

### Required fix

Restore the exact ordering:

```csharp
// Categories
.OrderBy(c => c.SortOrder)
.ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
.ThenBy(c => c.Id)

// Prompts
.OrderBy(p => p.SortOrder)
.ThenBy(p => p.Id)
```

Use those same deterministic comparators when overflow-resequencing.

### Required tests

```text
Category_equal_sort_orders_use_name_then_id
Category_equal_sort_orders_rename_reorders_by_name
Prompt_equal_sort_orders_use_id
Category_overflow_resequence_uses_visible_tie_order
Prompt_overflow_resequence_uses_visible_tie_order
```

---

## PLH2-004 — LOW — Project version 0.1.0 was accidentally removed

### Affected code

- `src/PromptHelper/PromptHelper.csproj`
- `src/PromptHelper/Views/HelpDialog.xaml.cs`

### Required project configuration

The authoritative plan specifies:

```xml
<Version>0.1.0</Version>
```

### Previous implementation

The pre-repair project contained:

```xml
<Version>0.1.0</Version>
```

### Current repaired project

The current `PromptHelper.csproj` no longer contains any `Version` property.

It also dropped the explicit:

```xml
<InvariantGlobalization>false</InvariantGlobalization>
```

The globalization omission is functionally benign because false is the normal default, but the version omission is not merely stylistic.

### User-visible effect

`HelpDialog` reads:

```csharp
Assembly.GetExecutingAssembly()
    .GetName()
    .Version?
    .ToString(3)
```

and only falls back to `"0.1.0"` when the assembly version is null.

With normal SDK defaults, an unspecified SDK-style project version is not null; it defaults to the 1.0.0 version family.

Therefore the Help dialog/release assembly metadata can report:

```text
v1.0.0
```

instead of:

```text
v0.1.0
```

### Impact

**Low release/versioning defect.**

It makes the application claim the wrong product version and violates the locked project definition.

### Required fix

Restore:

```xml
<Version>0.1.0</Version>
<InvariantGlobalization>false</InvariantGlobalization>
```

The second line mainly restores exact plan conformance; the first is the functional fix.

### Required test

Add a release metadata test or publish-smoke assertion verifying:

```text
Help version == v0.1.0
assembly product/version metadata == expected 0.1.0 family
```

---

## PLH2-005 — LOW — Defensive unique prompt-GUID generation was removed

### Affected code

`src/PromptHelper/Services/PromptLibraryService.cs`

### Required behavior

The implementation plan explicitly requires that a generated prompt ID must not collide with:

```text
existing PromptRecord
OR
existing orphan .md file
```

and specifies up to 10 fresh-GUID attempts.

### Previous implementation

The original service had:

```csharp
GenerateUniquePromptGuid()
```

which checked both:

```text
CurrentDocument.Prompts
_promptRepo.Exists(candidate)
```

before accepting the ID.

### Current implementation

`CreatePrompt()` now simply does:

```csharp
var newPromptId = Guid.NewGuid();
```

once.

No metadata collision test.

No orphan-file collision test.

No ten-attempt defensive loop.

### Practical probability

A random GUID collision is extraordinarily unlikely.

That makes this **Low severity**, not a practical high-risk data-loss issue.

But it is still a regression from an explicit defensive invariant, and the rewrite deleted working protection without replacement.

### Additional edge behavior

If the new GUID collides with an existing metadata ID whose `.md` is missing:

1. new file creation can succeed;
2. the duplicate metadata ID is added;
3. `LibraryValidator.Validate(candidate)` fails;
4. that validation happens before the commit try/catch cleanup block;
5. the newly written file is not rolled back by that catch.

Again, the random collision probability is tiny, but this demonstrates why the original invariant existed.

### Required fix

Restore a private equivalent of:

```text
GenerateUniquePromptGuid()
```

checking:

```text
_document.Prompts
and
_promptRepo.Exists / safe file-existence collision check
```

for up to 10 attempts.

Better for deterministic testing:

- inject an `IGuidGenerator` or small delegate.

### Required tests

With a deterministic GUID source:

```text
first generated ID collides with PromptRecord -> retry
first generated ID collides with orphan .md -> retry
10 collisions -> explicit failure
collision path leaves no unexpected file
```

---

## PLH2-006 — LOW — Destination list is sorted by raw path, not final displayed path

### Affected code

`PromptLibraryService.GetDestinations()`

### Required behavior

The implementation plan requires:

```text
Home always first.
Other options sorted by complete display path.
```

### Current sequence

Current implementation:

1. computes raw category paths;
2. sorts categories by `RawPath`;
3. adds collision suffixes while iterating;
4. returns in that old raw-path order.

It does **not** sort the final `DestinationRecord.DisplayPath` values.

### Deterministic example

Categories:

```text
Home
Home A
```

The category named `Home` collides with logical Home and may become:

```text
Home [1234abcd]
```

Raw ordering was:

```text
Home
Home A
```

But final display ordering under `OrdinalIgnoreCase` can be:

```text
Home A
Home [1234abcd]
```

because the generated `[` suffix changes the comparison result.

Current method still returns the raw ordering.

### Impact

**Low.**

Internal destination IDs remain correct and global label uniqueness is fixed.

This is a presentation/specification regression only.

### Required fix

After disambiguation:

```csharp
var categories = results
    .Where(r => r.CategoryId.HasValue)
    .OrderBy(r => r.DisplayPath, StringComparer.OrdinalIgnoreCase)
    .ThenBy(r => r.CategoryId);

return [home, .. categories];
```

### Required test

Create at least one disambiguated label whose suffix changes final lexical order and assert final `DisplayPath` sorting.

---

## PLH2-007 — LOW — `File.Exists` still misclassifies some prompt/initialization I/O failures

### Affected code

- `PromptRepository.Exists`
- `PromptRepository.Update`
- `PromptRepository.Create`
- `LibraryStartupService.HandleFirstRunOrInterruptedInit`
- `LibraryStartupService.TryRemoveStaleMarker`

### What was correctly fixed

The dangerous metadata classifier now directly calls:

```csharp
File.ReadAllText(path)
```

and only maps:

```text
FileNotFoundException
DirectoryNotFoundException
```

to Missing.

`EnumeratePromptFiles()` likewise enumerates directly and only maps `DirectoryNotFoundException` to an empty/missing directory.

Those were the important parts of the original high-severity PLH-003.

### What remains

`PromptRepository.Update()` still does:

```csharp
if (!File.Exists(path))
{
    throw new FileNotFoundException(...);
}
```

`File.Exists` deliberately returns `false` for multiple error conditions, not only true nonexistence.

Therefore an existing prompt that becomes inaccessible can be reported as:

```text
Prompt file does not exist.
```

instead of:

```text
access denied / I/O failure
```

The same helper remains in interrupted initialization:

```csharp
if (!_promptRepo.Exists(kvp.Key))
{
    _promptRepo.Create(...)
}
```

and marker detection still uses:

```csharp
File.Exists(_paths.InitializationMarkerPath)
```

### Why severity is now Low

The repaired metadata state machine no longer uses this trap for the primary/backup files.

The remaining flows generally fail safely later rather than overwriting known metadata.

So the original **High** PLH-003 is not still present at High severity.

The residual problem is now primarily:

- incorrect diagnostics;
- wrong branching before a later safe failure;
- unnecessary create attempts;
- inability to distinguish inaccessible from missing prompt files in races/edge conditions.

### Required fix

For `Update()` simply rely on the real operation:

```text
attempt atomic update
catch true FileNotFound/DirectoryNotFound as missing
allow access/I/O errors to remain their real type
```

For interrupted initialization, replace `Exists`-based truth with a read/open operation whose exceptions remain distinguishable.

For best-effort marker cleanup, `File.Exists` is less important, but a direct delete is cleaner because `File.Delete` already tolerates missing files.

---

# 6. Regression test-suite assessment

The new file:

```text
tests/PromptHelper.Tests/AuditDefectRegressionTests.cs
```

is useful, but it does **not** establish that all 14 original findings are protected.

It directly covers parts of:

```text
PLH-002
PLH-004
PLH-007
PLH-008
PLH-011
PLH-012
PLH-013
PLH-014
```

Important gaps include:

```text
PLH-001 actual WPF failure handling
PLH-003 access-denied vs missing behavior
PLH-005 full startup composition failures
PLH-006 production FileDeleter access failure
PLH-009 real UI size/DPI/keyboard behavior
PLH-010 actual publish-folder notice presence
```

Additionally, the new tests missed all new regressions in this report:

```text
live CategoryRecord/result-value leaks
sort tie-break regression
project version regression
unique GUID defense regression
post-disambiguation destination sort regression
blanket dispatcher exception handling
remaining Exists classifiers
```

---

# 7. Checks that still require a real Windows/.NET environment

After repairing PLH2-001 through PLH2-007, run the full suite:

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

Then execute the published application outside the IDE.

Mandatory confirmation list:

```text
[ ] all automated tests pass
[ ] no warnings indicating accidental API/build drift
[ ] first run creates defaults
[ ] second run does not duplicate defaults
[ ] zero-byte primary restores valid backup
[ ] whitespace primary restores valid backup
[ ] valid primary + backup sync failure visibly warns
[ ] first initialization + backup failure visibly warns
[ ] access-denied metadata is never classified as missing
[ ] access-denied prompt update reports access failure, not fake missing-file error
[ ] physical prompt delete failure produces warning
[ ] category/prompt write failures do not terminate app
[ ] unexpected programming exception is not silently swallowed while app continues
[ ] failed Create/Edit preserves typed text
[ ] category equal-sort ordering follows SortOrder/Name/Id
[ ] prompt equal-sort ordering follows SortOrder/Id
[ ] overflow resequencing uses deterministic tie ordering
[ ] Move to empty category assigns SortOrder 10
[ ] globally unique destination labels
[ ] final destination labels are sorted by final DisplayPath
[ ] 80 Unicode text elements accepted
[ ] 81 Unicode text elements rejected
[ ] category query results cannot mutate private service state
[ ] create result objects cannot mutate private service state
[ ] unique GUID collision retries work under injected GUID source
[ ] Help shows v0.1.0
[ ] 900×600 layout
[ ] 125% DPI
[ ] 150% DPI
[ ] deep hierarchy breadcrumb remains reachable with keyboard
[ ] long category names remain identifiable
[ ] 50k prompt display/edit/copy
[ ] clipboard retry behavior
[ ] missing prompt actions match specification
[ ] orphan preservation
[ ] corrupt primary + corrupt backup safely stops
[ ] future schema safely stops
[ ] second instance is rejected
[ ] offline functionality
[ ] LICENSE exists in publish folder
[ ] THIRD_PARTY_NOTICES.md exists in publish folder
[ ] published PromptHelper.exe launches outside IDE
[ ] close/restart persistence smoke passes
```

---

# 8. Recommended repair order

## First

### 1. PLH2-001 — close all live-object state leaks

This is the only remaining direct breach of the new private-document invariant.

### 2. PLH2-002 — stop treating arbitrary bugs as recoverable exceptions

Preserve expected filesystem resilience without continuing after unknown dispatcher/programming faults.

## Then

### 3. PLH2-003 — restore deterministic ordering
### 4. PLH2-004 — restore version 0.1.0
### 5. PLH2-005 — restore unique GUID generation
### 6. PLH2-006 — sort final destination display labels
### 7. PLH2-007 — remove remaining classification-sensitive `File.Exists` prechecks

Then add targeted regression tests for every item.

---

# 9. Final second-pass conclusion

The first repair commit is **substantially better** than the original implementation and does genuinely resolve the most concerning backup/recovery defects.

It should **not**, however, be accepted as the final clean baseline yet.

The repair rewrote a large part of `PromptLibraryService`, and that rewrite introduced/retained several regressions that the new regression suite does not test:

- incomplete private-state encapsulation;
- missing deterministic tie-break ordering;
- lost unique-GUID defense;
- raw-vs-final destination sorting;
- missing `0.1.0` project version;
- residual `File.Exists` classification behavior.

The WPF error-handling change also needs correction because the current strategy converts **all** unexpected dispatcher exceptions into “handled, continue running,” which is unsafe for an application whose core goal is reliable local persistence.

**Final status: another fix + regression round is required.**
