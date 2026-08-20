# Prompt Helper Bug-Hunting Report — `_plh1.md`

**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited HEAD:** `c46419079eab56d0b66acf33e6e15d126b53d391`  
**Audit date:** 2026-08-20  
**Scope:** application source, persistence/recovery code, WPF UI/XAML, unit/integration tests, project/publish configuration, and the repository's implementation-plan requirements.

## 1. Executive summary

The implementation is substantially better than a typical first pass: metadata writes are designed around a primary commit point, prompt files use atomic replacement, recovery behavior is deliberately conservative, future-schema handling exists, prompt-file orphans are generally preserved rather than destructively cleaned up, and the test suite contains meaningful failure injection.

However, this audit found **14 actionable defects/issues**:

| Severity | Count |
|---|---:|
| Critical | 0 |
| High | 3 |
| Medium | 7 |
| Low | 4 |

The most important problems are:

1. **Expected filesystem failures during normal UI mutations can escape WPF event handlers and shut down the application.**
2. **Backup failures during startup/initialization can be silently swallowed, leaving the user without the safety backup they believe exists.**
3. **`File.Exists` / `Directory.Exists` are used where the design requires “missing” to be distinguished from permission/I/O failure; .NET deliberately returns `false` for many such failures.**
4. **A zero-byte/whitespace `library.json` bypasses the intended corrupt-primary recovery path even when a valid backup exists.**
5. **The publish project does not include the repository's `LICENSE` / `THIRD_PARTY_NOTICES.md`, despite final QA explicitly requiring them.**

I would **not call the current build issue-free or final** until at least PLH-001 through PLH-010 are repaired and regression-tested.

---

# 2. Method and audit limits

I inspected the complete application structure and the important implementation paths rather than sampling isolated files. The audit covered, among others:

- `App.xaml` / `App.xaml.cs`
- `MainWindow.xaml` / `MainWindow.xaml.cs`
- all core models
- `LibraryRepository`
- `LibraryStartupService`
- `PromptRepository`
- `PromptLibraryService`
- `AtomicTextWriter`
- `FileDeleter`
- `AppInstanceLock`
- `ClipboardService`
- ViewModels
- dialogs and theme XAML
- the MSTest project and the major test classes
- `PromptHelper.csproj`
- `global.json`
- the implementation plan and mandatory QA matrix

I also checked the current GitHub repository state:

- no existing GitHub issues are recorded for this repository;
- the audited HEAD has no GitHub commit status checks attached;
- no CI workflow is present in the repository.

### Dynamic-execution limitation

This chat environment does **not** provide a Windows WPF runner or a .NET SDK, so I could not honestly execute `dotnet build`, `dotnet test`, the WPF GUI, DPI tests, or the self-contained publish smoke here. The findings below are therefore based on source-level control-flow/data-flow analysis, tests already in the repository, and framework behavior.

That limitation does **not** make the deterministic findings below speculative. Several are direct control-flow defects that can be reproduced with small tests.

---

# 3. Findings

## PLH-001 — HIGH — Normal save/write failures can terminate the WPF application

### Affected code

- `src/PromptHelper/MainWindow.xaml.cs`
- `src/PromptHelper/ViewModels/MainViewModel.cs`
- `src/PromptHelper/Services/PromptLibraryService.cs`
- `src/PromptHelper/App.xaml.cs`

### Problem

Most mutating UI event handlers invoke persistence operations without an exception boundary.

Examples include:

- Add category
- Rename category
- Add prompt
- Save edited prompt
- Delete prompt
- Move prompt
- Duplicate prompt

`DeleteCategoryButton_Click` catches only the expected `InvalidOperationException` for a non-empty category. `EditPromptButton_Click` catches failures when *opening* the file, but not failures when *saving* it. Clipboard copy has its own catch, but persistence actions generally do not.

The business/repository layer intentionally throws if the primary write fails. Existing tests confirm that failures such as an injected primary metadata write error are expected to propagate.

There is also no application-level `DispatcherUnhandledException` handler.

In WPF, an unhandled exception on the main UI dispatcher follows the framework's unhandled-exception path and normally shuts down the application.

### Real-world triggers

This is not an exotic programming-error-only path. It can happen due to:

- disk full;
- temporary antivirus/file lock;
- denied ACL/permission;
- broken user profile/local-app-data storage;
- filesystem or device I/O error;
- failed atomic replacement;
- external process holding a file.

### Impact

**High.**

A routine Save/Delete/Move operation can turn into an application crash.

The Create/Edit prompt flow is particularly bad: the modal editor closes **before** the write operation is attempted. If the save then fails, the text the user just entered is no longer present in the editor, so a crash/error can also cause practical loss of unsaved user input.

### Required fix

Add a deliberate UI operation boundary for expected recoverable persistence failures.

Recommended structure:

- central helper around user-triggered operations;
- catch `IOException`, `UnauthorizedAccessException`, `SecurityException`, and other explicitly recoverable filesystem exceptions;
- show a clear error;
- keep/reopen the editor with the user's attempted content when Create/Edit fails;
- do **not** blanket-swallow arbitrary programmer bugs.

A `DispatcherUnhandledException` handler may be useful as a final safety net, but it should not replace per-operation handling.

### Regression tests

Add tests/manual QA for:

1. Add category + primary metadata write failure.
2. Rename + primary metadata write failure.
3. Create prompt + primary write failure while preserving entered text.
4. Edit prompt + `.md` update failure while preserving entered text.
5. Delete/move/duplicate + primary write failure.
6. Application stays alive and old committed state remains visible.

---

## PLH-002 — HIGH — Backup failures are silently swallowed during startup/initialization

### Affected code

`src/PromptHelper/Services/LibraryStartupService.cs`

### Problem A — valid primary startup

When the primary metadata is valid:

```csharp
try
{
    _libraryRepo.SynchronizeBackup(primaryValid.Document);
}
catch
{
    // Best effort backup sync
}
...
return new StartupResult(primaryValid.Document, false, null);
```

Any backup synchronization failure is deliberately discarded and the returned warning is `null`.

The user is therefore told nothing.

### Problem B — first initialization

Clean initialization calls:

```csharp
_libraryRepo.Commit(defaultPkg.Document);
```

but ignores the returned `CommitResult`.

`LibraryRepository.Commit()` explicitly reports whether the backup synchronized. If primary creation succeeds and backup creation fails, startup proceeds, removes the initialization marker, and returns with no warning.

### Problem C — interrupted initialization

The same problem exists after interrupted-initialization recovery: the result of `_libraryRepo.Commit(defaultPkg.Document)` is ignored.

### Impact

**High data-safety/recovery issue.**

The application presents itself as healthy while:

- `library.json` may be the only current metadata copy;
- `library.backup.json` may be missing or stale;
- a later primary corruption can therefore have no current recovery source.

The normal mutation paths correctly expose backup failures as warnings, so silently suppressing the same safety failure during startup is inconsistent.

### Required fix

Propagate backup problems through `StartupResult.Warning`.

For valid-primary startup:

- attempt backup synchronization;
- on failure, return a clear nonfatal warning.

For first/interrupted initialization:

- capture `CommitResult`;
- return its warning if `BackupSynchronized == false`.

If recovery itself already has a warning, combine warnings rather than dropping one.

### Regression tests

Add:

- valid primary + backup sync writer failure -> startup succeeds **with warning**;
- clean first run + backup write failure -> primary exists, startup succeeds **with warning**;
- interrupted initialization + backup write failure -> startup succeeds **with warning**;
- next successful startup repairs backup.

---

## PLH-003 — HIGH — `File.Exists`/`Directory.Exists` destroy the required distinction between “missing” and “I/O/access failure”

### Affected code

- `LibraryStartupService.ReadMetadataState`
- `LibraryStartupService.HandleFirstRunOrInterruptedInit`
- `PromptRepository.Exists`
- `PromptRepository.Update`
- `PromptRepository.EnumeratePromptFiles`
- `FileDeleter.DeleteIfExists`
- parts of `AtomicTextWriter`

### Problem

The recovery design requires an important safety distinction:

- file genuinely missing -> may enter missing/recovery/initialization branch;
- access denied / broken disk / path error / other I/O failure -> **fatal/explicit error**, not “missing.”

But startup begins with:

```csharp
if (!File.Exists(path))
{
    return new MetadataReadResult.Missing();
}
```

and prompt enumeration begins with:

```csharp
if (!Directory.Exists(_paths.PromptsDirectory))
{
    return [];
}
```

On .NET, `File.Exists` and `Directory.Exists` intentionally return `false` for many errors, including insufficient permissions and various filesystem/path failures. They are therefore unsuitable as authoritative state classifiers when the error category matters.

### Impact

**High because it weakens a central data-safety invariant.**

Depending on the exact filesystem condition, the program can:

- label inaccessible metadata as missing;
- enter the wrong recovery branch;
- label an inaccessible prompt directory as empty;
- label inaccessible prompt content as “not found”;
- attempt initialization under an ambiguous filesystem state;
- produce misleading errors instead of immediately stopping on uncertain data.

Atomic no-overwrite behavior reduces some worst-case overwrite scenarios, but the state machine is still incorrect and can perform work it explicitly promised not to perform under uncertainty.

### Required fix

For metadata:

- directly attempt `File.ReadAllText`;
- catch **only** `FileNotFoundException` / `DirectoryNotFoundException` as Missing;
- let `UnauthorizedAccessException`, other `IOException`, path/security failures propagate.

For prompt directory enumeration:

- enumerate directly;
- treat `DirectoryNotFoundException` as empty/missing only where appropriate;
- propagate access/I/O errors.

For individual prompt reads, prefer “attempt operation + catch” over `Exists`-then-operate where error classification matters.

### Regression tests

Add Windows filesystem tests or an abstracted filesystem layer for:

- metadata path access denied;
- prompt directory access denied;
- prompt file access denied;
- missing vs denied behavior must differ;
- no initialization when state is uncertain.

---

## PLH-004 — MEDIUM — Zero-byte or whitespace metadata bypasses backup recovery

### Affected code

- `LibraryRepository.InspectAndDeserialize`
- `LibraryStartupService.ReadMetadataState`

### Deterministic control flow

`InspectAndDeserialize()` starts with:

```csharp
ArgumentException.ThrowIfNullOrWhiteSpace(json);
```

`ReadMetadataState()` classifies only these parse/validation exceptions as corruption:

```csharp
catch (Exception ex) when (ex is JsonException or InvalidDataException)
{
    return new MetadataReadResult.Corrupt(raw);
}
```

Therefore:

- zero-byte `library.json`;
- whitespace-only `library.json`;
- effectively empty text after reading;

produce an `ArgumentException`, not a `Corrupt` result.

That exception escapes the corruption-classification logic.

### Reproduction

1. Start from valid `library.json` + valid `library.backup.json`.
2. Replace `library.json` with an empty file.
3. Start Prompt Helper.

### Expected

Primary is corrupt -> recover from valid backup -> show recovery warning.

### Actual from code

`ArgumentException` escapes `ReadMetadataState` -> startup fails instead of recovering.

### Impact

**Medium.**

A very common corruption form—a zero-byte file—is exactly the kind of failure the backup is supposed to recover from.

### Required fix

Do not use `ArgumentException.ThrowIfNullOrWhiteSpace` for persisted file content.

For example:

- `null` may remain a programming-argument error if the API is public;
- empty/whitespace persisted JSON should throw `JsonException` or `InvalidDataException`, so startup classifies it as corrupt.

### Regression tests

- empty primary + valid backup -> recover;
- whitespace primary + valid backup -> recover;
- BOM-only/empty logical content + valid backup -> recover;
- empty primary + no valid backup -> fatal corruption without defaults.

---

## PLH-005 — MEDIUM — Several startup filesystem operations occur outside the controlled startup error handler

### Affected code

`src/PromptHelper/App.xaml.cs`

### Problem

The `try`/`catch` that shows a controlled startup error begins only around `startupService.LoadOrInitialize()`.

Before that, the application executes:

```csharp
paths.EnsureRootDirectory();
_appLock = AppInstanceLock.TryAcquire(paths.LockPath);
...
paths.EnsureDataDirectories();
```

All three can throw expected environment/filesystem exceptions.

For example:

- LocalAppData permission failure;
- directory creation failure;
- unexpected lock-file I/O failure;
- path/device failure.

`AppInstanceLock.TryAcquire()` intentionally converts only sharing/lock violations into “another instance.” Other exceptions correctly propagate—but `App.OnStartup` does not catch them at that point.

### Impact

**Medium.**

Instead of the application's controlled “Prompt Helper Startup Error” message, WPF can enter its default unhandled-exception behavior and terminate.

### Required fix

Wrap the entire startup filesystem/composition sequence in the startup exception boundary, while preserving the special case:

- expected sharing violation -> “another instance” information message;
- future schema -> dedicated future-schema message;
- other expected startup failure -> controlled startup error;
- release lock during shutdown.

Also remove unnecessary duplicate `EnsureDataDirectories()` calls if possible.

---

## PLH-006 — MEDIUM — Physical prompt deletion can silently fail without the required warning

### Affected code

`src/PromptHelper/Services/FileDeleter.cs`

Current implementation:

```csharp
public void DeleteIfExists(string path)
{
    if (File.Exists(path))
    {
        File.Delete(path);
    }
}
```

### Problem

Again, `File.Exists` returns `false` for many access/errors.

`PromptLibraryService.DeletePrompt()` is carefully designed to catch a physical deletion exception and return a warning while keeping the metadata commit.

But if `File.Exists()` hides an access problem by returning `false`, `File.Delete()` is never attempted, no exception is raised, and `DeletePrompt()` reports complete success even though the orphan file may remain.

### Impact

**Medium.**

The persistence semantics explicitly distinguish:

- metadata deleted successfully;
- old `.md` cleanup failed -> preserve committed state but tell user.

This implementation can silently violate that contract.

### Required fix

`File.Delete(path)` already tolerates a genuinely missing file. Prefer direct deletion:

```csharp
public void DeleteIfExists(string path)
{
    File.Delete(path);
}
```

Then:

- missing file -> no error;
- access/I/O failure -> exception reaches the service and becomes the intended warning.

### Regression tests

- deleting an absent file is harmless;
- access-denied deletion produces a warning;
- metadata remains committed when cleanup fails;
- physical file remains when cleanup fails.

---

## PLH-007 — MEDIUM — Destination-label disambiguation can itself create duplicate final labels

### Affected code

`PromptLibraryService.GetDestinations()`

### Current algorithm

1. Build raw paths.
2. Group raw collisions.
3. For colliding category paths, append first 8 GUID hex digits:
   `Path [1234abcd]`.
4. Stop; final labels are not checked again for uniqueness.

### Deterministic reproduction

1. Create a root category named `Home`.
2. In the Move dialog it is disambiguated against logical Home, for example:
   `Home [1234abcd]`.
3. Create another root category literally named:
   `Home [1234abcd]`.
4. Open Move again.

The first category receives the generated label:

`Home [1234abcd]`

The second category's **raw** path is already:

`Home [1234abcd]`

It belongs to a different raw grouping, so it remains unchanged.

Result: **two visually identical destination entries with different IDs.**

A similar construction is possible with `A > B` path-collision labels.

There is also a theoretical second collision source if two colliding category GUIDs share the same first eight hex digits.

### Impact

**Medium UX/data-organization risk.**

The ComboBox still stores separate IDs, so the internal data structure is not corrupted, but the user cannot distinguish the two choices and can move/duplicate a prompt into the wrong category.

### Required fix

Make uniqueness a property of the **final display strings**, not only raw groups.

Good options:

- iteratively test generated labels against the full used-label set;
- progressively lengthen the GUID suffix until unique;
- fall back to full GUID when necessary;
- reserve/escape the disambiguation format.

### Regression tests

- `Home` category + literal `Home [suffix]` category;
- separator collision + literal suffixed path;
- two synthetic GUIDs with identical first-eight prefix;
- assert **all final `DisplayPath` values are unique case-insensitively**.

---

## PLH-008 — MEDIUM — Category-name UI length enforcement disagrees with the model's Unicode rule

### Affected code

- `Views/NameDialog.xaml`
- `LibraryValidator`
- `TextUtilities`

### Design/model rule

The validator allows up to **80 Unicode text elements**:

```csharp
TextUtilities.GetTextElementCount(trimmed)
```

The test suite explicitly recognizes that an emoji surrogate pair can be one text element.

### UI rule

`NameDialog.xaml` sets:

```xml
MaxLength="80"
```

WPF `TextBox.MaxLength` is a TextBox character/input limit; it is not the same semantic as `StringInfo` text-element/grapheme counting.

Therefore valid Unicode names can hit the UI limit before the validator's 80-text-element limit, and the two layers can disagree.

### Impact

**Medium correctness/Unicode UX issue.**

A category name that the domain validator explicitly accepts may not be enterable through the actual UI.

### Required fix

Remove the semantic `MaxLength="80"` from the TextBox and let the existing validator enforce the 80-text-element rule.

If a defensive raw-input cap is desired, make it comfortably larger and explicitly separate from the domain limit.

### Regression tests

Use:

- 80 ASCII characters -> accepted;
- 81 ASCII -> rejected by validator;
- 80 surrogate-pair emoji/text elements -> accepted;
- 81 -> rejected;
- combining-character graphemes;
- ensure the UI does not truncate a valid 80-text-element name before Submit.

---

## PLH-009 — MEDIUM — Legal long/deep category paths have no usable overflow strategy

### Affected code

`src/PromptHelper/MainWindow.xaml`

### Breadcrumb

Breadcrumbs use an `ItemsControl` with:

```xml
<StackPanel Orientation="Horizontal"/>
```

There is:

- no wrapping;
- no horizontal scrolling;
- no collapsing/ellipsis strategy;
- no “…” middle breadcrumb;
- no maximum rendered depth.

The model deliberately allows deep hierarchies and category names up to 80 text elements.

A six-level hierarchy of long legal names can far exceed the minimum 900-DIP window width.

### Category cards

Category cards are only 230 DIP wide and reserve a significant right-hand area for Rename/Delete. The category-name button has no text trimming/wrapping strategy and its tooltip says only `Open category`, not the full category name.

### Impact

**Medium UI/navigation defect.**

Valid data can produce:

- clipped/off-window breadcrumb ancestors/current category;
- inaccessible navigation targets;
- unreadable category names;
- possible name/action overlap or abrupt clipping depending WPF layout behavior.

This undermines the “effectively arbitrary depth” and long-name support even though the data layer accepts the hierarchy.

### Required fix

For breadcrumbs, use one of:

- horizontal ScrollViewer;
- wrap panel;
- middle-collapse (`Home › … › Parent › Current`) with a menu for hidden ancestors;
- width-aware text trimming plus full-name tooltips.

For category cards:

- `TextTrimming="CharacterEllipsis"` through a TextBlock content template;
- full category name in tooltip/automation label.

### Regression/manual QA

At 900×600 and 125%/150% scaling:

- 6+ hierarchy levels;
- each name near 80 text elements;
- all required navigation remains reachable;
- action controls do not overlap;
- full names can be discovered.

---

## PLH-010 — MEDIUM — Required license/notices are not configured to be copied into publish output

### Affected code

`src/PromptHelper/PromptHelper.csproj`

### Repository requirement

The implementation plan's final QA matrix explicitly includes:

`QA-042 | license/notices retained | PASS`

The repository contains:

- root `LICENSE`
- root `THIRD_PARTY_NOTICES.md`

### Current project file

`PromptHelper.csproj` contains no `Content`/`None` entries linking those root files and no `CopyToPublishDirectory` instruction.

Since the files live outside the application project directory, this project definition does not arrange for them to be copied into the application's publish folder.

### Impact

**Medium release/compliance issue.**

The release folder produced by the specified `dotnet publish` procedure can omit project license/notices even though final QA requires them.

### Required fix

Add explicit linked content items, for example conceptually:

```xml
<ItemGroup>
  <Content Include="..\..\LICENSE" Link="LICENSE">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
  </Content>

  <Content Include="..\..\THIRD_PARTY_NOTICES.md" Link="THIRD_PARTY_NOTICES.md">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
  </Content>
</ItemGroup>
```

Then assert their presence in `artifacts/publish/win-x64`.

---

## PLH-011 — LOW — Moving a prompt calculates the destination sort order after the moved item is already in the destination

### Affected code

`PromptLibraryService.MovePrompt()`

Current order:

```csharp
target.CategoryId = destinationCategoryId;
target.SortOrder = CalculateNextPromptSortOrder(candidate, destinationCategoryId);
```

`CalculateNextPromptSortOrder()` queries every prompt already in that category—including `target`, because its category has just changed.

### Deterministic example

- Category A: prompt P has `SortOrder = 10`.
- Category B: empty.
- Move P to B.

Expected destination-end sort order for the first item in an empty category:

`10`

Actual calculation:

- target is already considered a B sibling with old sort 10;
- max = 10;
- result = 20.

If the source item's old sort is 1,000,000, an empty destination receives 1,000,010.

Near overflow, the moved record can also cause unnecessary resequencing of existing destination siblings.

### Impact

**Low currently**, because visible ordering still puts the moved item at the end.

But it violates sort-order semantics, unnecessarily inflates persisted values, and makes later ordering behavior harder to reason about.

### Required fix

Calculate destination next-sort **before** changing `CategoryId`, or add an `excludePromptId` parameter to the calculation.

### Regression tests

- move into empty destination -> sort 10;
- move after destination 10/20 -> sort 30;
- source sort must not influence destination result;
- near-overflow destination only -> correct local resequence.

---

## PLH-012 — LOW — Deleting a non-empty category asks for destructive confirmation before telling the user deletion is impossible

### Affected code

`MainWindow.DeleteCategoryButton_Click`

### Problem

The UI always opens:

> Delete category "..."?  
> This action cannot be undone.

Only after the user confirms does the service discover that the category contains prompts/subcategories and throw `InvalidOperationException`.

The implementation plan explicitly describes the service re-check as necessary **even though the UI checks first**, but the UI pre-check is absent.

The service also gives two narrower messages:

- move/delete subcategories first; or
- move/delete prompts first;

rather than the planned combined guidance when relevant.

### Impact

**Low UX defect.**

The user receives a scary irreversible-action confirmation for an action the application already knows it will reject, then has to dismiss a second dialog.

### Required fix

Expose a read-only deletion eligibility/block-reason query to the UI.

Flow:

1. Check emptiness.
2. If non-empty -> show explanatory message immediately.
3. If empty -> show destructive confirmation.
4. Service still re-checks on actual delete.

---

## PLH-013 — LOW — `EditPrompt` reports the repository object's type name as the missing filename

### Affected code

`PromptLibraryService.EditPrompt()`

Current code:

```csharp
throw new FileNotFoundException(
    "Prompt file does not exist.",
    _promptRepo.ToString());
```

`_promptRepo.ToString()` is not the prompt path; with the current class it will normally be a type-like string such as:

`PromptHelper.Services.PromptRepository`

### Impact

**Low diagnostic defect.**

After external deletion/race or permission-related misclassification, error reporting can show a meaningless “filename.”

### Required fix

Either:

- let `PromptRepository.Update()` throw its own `FileNotFoundException` with the actual path; or
- expose a safe path accessor and pass the real prompt path.

Prefer eliminating the pre-`Exists` check and letting the repository own this filesystem concern.

---

## PLH-014 — LOW — `CurrentDocument` exposes a mutable object and allows persistence invariants to be bypassed by future callers

### Affected code

`PromptLibraryService.CurrentDocument`

Current property:

```csharp
public LibraryDocument CurrentDocument { get; private set; }
```

The setter is private, but `LibraryDocument`, `CategoryRecord`, `PromptRecord`, and their lists/properties are mutable.

Any caller holding `CurrentDocument` can therefore mutate:

- category IDs;
- parent links;
- names;
- prompt category references;
- sort orders;
- list membership;

without:

- validation;
- cloning;
- primary commit;
- backup synchronization.

The constructor also stores the supplied `initialDocument` reference directly rather than cloning it, so an external holder of that original reference can mutate live service state after construction.

### Impact

**Low in the current UI**, because the present application code mostly treats the object as read-only.

It is nevertheless a real invariant hole and makes future changes/tests/plugins much easier to get wrong.

### Required fix

At minimum:

- clone `initialDocument` in the constructor;
- keep mutable state private;
- expose query DTOs/read-only projections rather than the live document.

A deeper future improvement would make persisted records immutable or mutation internal to the service.

---

# 4. Test and release verification gaps

These are not counted as additional product bugs, but they materially explain why the defects above escaped.

## 4.1 No automated UI-level failure-path coverage

The repository has useful service/repository tests, but there are no tests proving that a real WPF click workflow survives persistence exceptions.

That is why PLH-001 remains possible even though the underlying failure behavior is well-tested.

## 4.2 Recovery tests miss empty/whitespace JSON

Malformed JSON is tested, but the important zero-byte/whitespace case that triggers `ArgumentException` is not covered.

## 4.3 Move tests verify category membership, not resulting sort semantics

Existing move tests assert the new `CategoryId`, but do not assert the expected destination `SortOrder`.

## 4.4 Destination collision tests stop too early

Tests cover:

- logical Home collision;
- `A > B` raw-path collision;

but do not assert global uniqueness **after** suffixes are applied.

## 4.5 Failure-injection deletion tests bypass the real `FileDeleter` behavior

The service-level fault deleter proves the warning path if an exception is thrown, but it does not test that the production `FileDeleter` actually surfaces permission/I/O failures instead of hiding them with `File.Exists`.

## 4.6 Required manual/release QA has no repository evidence

The implementation plan requires, among other things:

- application-start smoke;
- 900×600;
- 125% scaling;
- 150% scaling;
- keyboard navigation;
- 50k prompt UI behavior;
- corruption/recovery;
- offline behavior;
- Release build/tests;
- self-contained publish;
- publish smoke;
- retained license/notices.

There is no QA result artifact in the repository and no commit status attached to the audited HEAD.

This does **not** prove the tests were never run, but it means the repository cannot currently demonstrate that the mandatory final QA matrix passed.

---

# 5. Investigated items that I am **not** reporting as bugs

This section is deliberate: these looked suspicious during the audit but were cleared, so they should not be turned into unnecessary work.

## 5.1 Custom TextBox scrollbar TemplateBindings

The custom `PromptDisplayTextBoxStyle` / `PromptEditorTextBoxStyle` do not explicitly TemplateBind scrollbar visibility onto `PART_ContentHost`.

At first glance that looks like a scrollbar bug.

However, WPF's `TextBoxBase` internally owns the ScrollViewer visibility dependency properties and propagates them to its discovered `PART_ContentHost` ScrollViewer via `OnScrollViewerPropertyChanged` during template attachment/property changes.

Therefore I am **not** counting the missing explicit TemplateBinding as a defect.

A runtime UI smoke is still appropriate because the 50k-prompt editor is a mandatory QA item.

## 5.2 Orphan prompt retention after backup/delete failures

Retaining `.md` files when metadata has committed but backup/file cleanup has not is intentional safety behavior. It avoids making a stale backup point to deleted physical content.

The orphan-preservation policy is not itself a bug.

## 5.3 Stale `.app.lock` file remaining on disk

The lock's protection comes from the open exclusive file handle, not from deleting the file on exit. Reopening the existing file after the previous handle closes is valid.

The persistent lock filename is not itself a stale-lock bug.

## 5.4 No hidden networking found

I found no application runtime use of `HttpClient`, WebView, sockets, telemetry/analytics, AI APIs, or process execution in the audited source.

The offline/privacy architecture is consistent with the stated MVP direction.

---

# 6. Recommended repair order

## Repair batch A — release blockers

1. **PLH-001** — protect all user mutations from recoverable persistence exceptions.
2. **PLH-002** — surface startup/initialization backup failures.
3. **PLH-003** — remove `Exists`-based authoritative startup classification.
4. **PLH-004** — classify empty/whitespace metadata as corrupt.
5. **PLH-005** — move all startup filesystem work under controlled error handling.
6. **PLH-006** — make production deletion failures observable.
7. **PLH-010** — include license/notices in publish output.

## Repair batch B — functional/UI correctness

8. **PLH-007** — guarantee globally unique final destination labels.
9. **PLH-008** — align UI Unicode name limit with validator.
10. **PLH-009** — make deep/long hierarchy navigation usable.
11. **PLH-011** — fix move destination sort calculation.
12. **PLH-012** — pre-check non-empty category before delete confirmation.
13. **PLH-013** — fix missing-file diagnostic.
14. **PLH-014** — stop exposing live mutable canonical state.

---

# 7. Mandatory regression set after repairs

The next verification pass should not stop at existing tests.

At minimum run:

```text
dotnet restore
dotnet build
dotnet test
dotnet build -c Release
dotnet test -c Release
dotnet publish src/PromptHelper/PromptHelper.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Then verify:

```text
[ ] clean first run
[ ] second run
[ ] valid primary + failed backup sync -> visible warning
[ ] first init + failed backup write -> visible warning
[ ] zero-byte primary + valid backup -> recovery
[ ] whitespace primary + valid backup -> recovery
[ ] metadata access denied -> fatal controlled error, never "missing"
[ ] prompt directory access denied -> controlled error, never "empty"
[ ] Add/Rename/Create/Edit/Delete/Move/Duplicate write failures do not crash app
[ ] failed Create/Edit preserves user's attempted text
[ ] physical delete access failure -> warning
[ ] Move to empty destination -> sortOrder 10
[ ] Move to populated destination -> true destination-end sort
[ ] all destination DisplayPath values globally unique
[ ] 80 Unicode text elements accepted through Name dialog
[ ] 81 text elements rejected
[ ] deep hierarchy + 80-element names remains navigable
[ ] 900x600
[ ] 125% scaling
[ ] 150% scaling
[ ] keyboard navigation/focus visibility
[ ] 50k prompt card/edit/copy
[ ] clipboard retry behavior
[ ] self-contained executable launches outside IDE
[ ] LICENSE present in publish folder
[ ] THIRD_PARTY_NOTICES.md present in publish folder
[ ] no networking
```

---

# 8. Final audit verdict

**Verdict: NOT CLEAN / NOT FINAL YET.**

The repository has a sound core design and unusually good persistence-oriented tests for an MVP, but the current HEAD still contains several real release-relevant defects.

The most important theme is that the lower layers correctly model failure, while the composition/UI/startup layers sometimes:

- suppress that failure;
- misclassify it as “missing”; or
- allow it to become an unhandled WPF exception.

After the 14 findings above are repaired, the project should receive one full Windows/.NET 10 regression run including the mandatory manual UI and self-contained publish checks. Only a pass with no newly discovered defects should be treated as the clean baseline for the next development step.
