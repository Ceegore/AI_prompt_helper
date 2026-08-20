# Prompt Helper — Eighth Paranoid Audit (`_plh8.md`)

**Repository:** `Ceegore/AI_prompt_helper`  
**Current `main`:** `e3d3fcea557ac54f8eaa8b1dd83f5bbcad94b15c`  
**Current `v0.1.0` tag:** `e3d3fcea557ac54f8eaa8b1dd83f5bbcad94b15c`  
**Previous audited HEAD:** `27aee1fc9b4395fd8475e401b441c428d69253a3`  
**Last fully static-clean product baseline before runtime-test work:** `fb69b54973dbec7630f2cf47164bd9451fb0be19`  
**Audit date:** 2026-08-20  
**Purpose:** Paranoid re-audit after the attempt to close `_plh7.md` by adding `PublishedLifecycleAndGuiFlowRegressionTests` and dialog keyboard changes.

---

# 1. Executive verdict

The latest commit is **not a clean documentation-only follow-up**.

It changes:

```text
_plh7.md
src/PromptHelper/Views/ConfirmDeleteDialog.xaml
src/PromptHelper/Views/NameDialog.xaml
tests/PromptHelper.Tests/PublishedLifecycleAndGuiFlowRegressionTests.cs
```

The two production changes are:

```text
NameDialog ActionButton:
IsDefault="True"

ConfirmDeleteDialog ActionButton:
IsDefault="True"
```

The test project adds four new tests.

The current:

```text
main
```

and:

```text
v0.1.0
```

both resolve exactly to:

```text
e3d3fcea557ac54f8eaa8b1dd83f5bbcad94b15c
```

So source/tag alignment itself is currently good.

However, the attempt to close the remaining runtime verification gap is **not sufficient**.

The new file is named:

```text
PublishedLifecycleAndGuiFlowRegressionTests.cs
```

but it does not launch:

```text
PromptHelper.exe
```

and does not drive:

```text
WPF
UI Automation
Windows clipboard
real dialogs
keyboard input
offline runtime
DPI/layout
```

It is a service/repository integration-test file using:

```text
TestDirectory
AppPaths
LibraryRepository
PromptRepository
LibraryStartupService
PromptLibraryService
```

Therefore the remaining published-runtime acceptance gates from `_plh7.md` are still open.

---

# 2. Finding summary

```text
Critical: 0
High:     2
Medium:   1
Low:      0
```

Findings:

```text
PLH8-001 HIGH
"PublishedLifecycleAndGuiFlowRegressionTests" does not test the published
application or GUI and therefore cannot close PLH7-001.

PLH8-002 MEDIUM
Unavailable_prompt_state_and_actions claims to verify moving an unavailable
prompt but performs a null → null same-category no-op.

PLH8-003 HIGH
Previous build/test/binary provenance evidence is stale for current v0.1.0:
the tag now points to e3d3fcea, which contains production XAML changes and
four new tests, while the last evidenced binary/test run referred to 27aee1fc.
```

No new functional product defect was confirmed from the two `IsDefault` changes themselves.

---

# 3. Current change set

Comparison:

```text
27aee1fc...
→
e3d3fcea...
```

shows exactly:

```text
_plh7.md                                           +1076
ConfirmDeleteDialog.xaml                             +1
NameDialog.xaml                                      +1
PublishedLifecycleAndGuiFlowRegressionTests.cs     +179
```

No persistence/business-service production code changed.

---

# 4. Current tag state

A fresh comparison shows:

```text
v0.1.0
==
e3d3fcea557ac54f8eaa8b1dd83f5bbcad94b15c
```

and `main` also resolves to that exact SHA.

Therefore:

```text
TAG SOURCE ALIGNMENT:
PASS
```

at the source-ref level.

---

# 5. Production XAML changes

## NameDialog

Current action button:

```xml
<Button x:Name="ActionButton"
        Content="Save"
        Style="{StaticResource PrimaryButtonStyle}"
        IsDefault="True"
        Click="ActionButton_Click"/>
```

This is compatible with the locked plan's Name-dialog keyboard requirement:

```text
Enter → Create/Save
Escape → Cancel
```

The existing `NameInputTextBox_KeyDown` also handles Enter and marks the event handled.

I do not identify this as a product defect.

---

# 6. ConfirmDeleteDialog change

Current delete action:

```xml
<Button x:Name="ActionButton"
        Content="Delete"
        Style="{StaticResource DangerButtonStyle}"
        IsDefault="True"
        Click="ActionButton_Click"/>
```

The locked plan requires the custom:

```text
Cancel
Delete
```

confirmation dialog and danger styling.

It does not establish a contradictory requirement forbidding the Delete button from being default.

Therefore this audit does **not** promote the change as a defect.

It should still be exercised in real keyboard/manual QA.

---

# 7. PLH8-001 — HIGH — the new "GUI" tests do not test GUI or the published binary

The new class is:

```csharp
PublishedLifecycleAndGuiFlowRegressionTests
```

but its implementation is not a GUI automation suite.

The first test constructs:

```csharp
var paths = new AppPaths(testDir.Root);
var writer = new AtomicTextWriter();
var deleter = new FileDeleter();
var libRepo = new LibraryRepository(paths, writer);
var promptRepo = new PromptRepository(paths, writer, deleter);
var startup = new LibraryStartupService(...);
```

and then drives:

```csharp
PromptLibraryService
```

directly.

No published EXE is launched.

No `MainWindow` is created.

No `MainViewModel` is driven through WPF controls.

No dialog is shown.

No Windows UI Automation API is used.

No keyboard input is generated.

No clipboard is read.

No network-disabled runtime is executed.

No screenshot/DPI rendering occurs.

---

# 8. The first new test is a useful integration test — but not published GUI QA

Test:

```text
Category_and_Prompt_Full_CRUD_Lifecycle_and_Restart_Persistence
```

does useful service-level work:

```text
fresh initialization
category create
nested create
duplicate sibling rejection
rename
prompt create
edit
move
duplicate
50k prompt
empty prompt
non-empty delete rejection
prompt delete
category delete
restart persistence
```

This is valuable.

It increases confidence in the persistence/business-service composition.

But all operations happen through:

```text
PromptLibraryService
```

not through the published application's actual UI.

Therefore it cannot prove:

```text
button wiring
dialog wiring
binding correctness
enabled/disabled states
modal behavior
keyboard behavior
clipboard wiring
MainViewModel refresh behavior through actual WPF
published executable composition
```

---

# 9. Why this distinction matters

A service test can pass while the WPF application still has a defect such as:

```text
wrong Click handler
wrong DataContext
wrong binding
disabled button wired incorrectly
dialog result not propagated
wrong dialog default/cancel behavior
Copy button not invoking clipboard correctly
Move dialog returning wrong category
UI not refreshing after operation
published composition wiring error
```

That is why `_plh_final_verification_concept.md` explicitly separated:

```text
unit/integration tests
```

from:

```text
published executable GUI smoke
```

---

# 10. Mandatory GUI category flow is still unverified

The new test directly calls:

```csharp
service.CreateCategory(...)
service.RenameCategory(...)
service.DeleteCategory(...)
```

It does not prove that a user can:

```text
click + Add
type a category
press Enter
see the category card

open it
create nested category

attempt duplicate sibling
see inline validation

rename through the dialog

attempt non-empty delete
see exact rejection message

delete empty category
see custom confirmation
```

So the published GUI category gate remains open.

---

# 11. Mandatory prompt GUI flow is still unverified

The new test calls:

```csharp
service.CreatePrompt(...)
service.EditPrompt(...)
service.DeletePrompt(...)
```

It does not prove:

```text
+ Prompt opens editor
editor displays correct text
Save closes only when appropriate
failed save preserves text
empty prompt UI works
50k editor path works
Delete confirmation works
card refreshes correctly
```

Published prompt GUI QA remains open.

---

# 12. Move dialog remains unverified

The test directly calls:

```csharp
service.MovePrompt(...)
```

It does not exercise:

```text
Move button
MovePromptDialog
destination ComboBox
current category preselection
display-path labels
Enter default action
Escape cancel
Copy-instead-of-move toggle
unavailable-prompt duplicate disabling
```

Therefore the actual Move-dialog gate remains open.

---

# 13. Duplicate GUI path remains unverified

The test directly calls:

```csharp
service.DuplicatePrompt(...)
```

It does not prove the UI path:

```text
Move
→ Copy instead of move
→ choose destination
→ Copy
```

works end-to-end.

---

# 14. Clipboard remains completely unverified by the new test file

The new test class does not reference:

```text
ClipboardService
System.Windows.Clipboard
PresentationCore
STA clipboard helper
Copy button
```

Therefore it does not test:

```text
click Copy
exact clipboard text
Unicode
Markdown
blank lines
code fence
no truncation
Copied ✓
reset to Copy
```

This gate remains open.

---

# 15. Keyboard behavior remains unverified

The commit changes keyboard-related XAML:

```text
NameDialog IsDefault=True
ConfirmDeleteDialog IsDefault=True
```

but adds no WPF/keyboard test.

The new test file does not instantiate either dialog.

Required runtime semantics still need real verification:

```text
Name:
Enter submit
Escape cancel

Prompt editor:
Enter newline
Tab input
Escape cancel
Enter does NOT save

Move:
Enter action
Escape cancel

main:
Tab / Shift+Tab navigation
```

---

# 16. Offline runtime remains unverified

The new tests use local files only, which is consistent with an offline design.

They do not execute the published application with networking disabled.

Therefore current states remain:

```text
STATIC PRIVACY:
PASS

OFFLINE RUNTIME:
NOT ESTABLISHED
```

---

# 17. DPI / layout remain unverified

Nothing in the new commit verifies:

```text
900×600
125%
150%
visible focus
clipping
overlap
dialog fit
breadcrumb usability
category-card usability
prompt-list usability
```

These remain:

```text
HUMAN_REQUIRED
```

or Tier-C GUI/screenshot tests.

---

# 18. Recovery tests are still service-level, not published recovery smoke

The new file adds:

```text
Double_corruption_and_future_schema_safety_stop
```

This calls:

```csharp
startup.LoadOrInitialize()
```

directly.

That is useful.

It does not launch the actual published EXE and verify:

```text
fatal startup dialog
no MainWindow
no default recreation
files preserved
process lifecycle
```

Likewise, the new file does not add a published corrupt-primary recovery run.

---

# 19. PLH8-001 impact

The problem is not that the tests are bad.

The problem is that their name and use can create a false conclusion:

```text
"PublishedLifecycleAndGuiFlowRegressionTests passed"
→ therefore published GUI QA passed
```

That conclusion is invalid.

The class should be understood as:

```text
service/persistence lifecycle regression coverage
```

not:

```text
published GUI E2E coverage
```

---

# 20. PLH8-001 required resolution

Do one of these:

## Preferred

Keep the tests, but classify them accurately:

```text
SERVICE / PERSISTENCE INTEGRATION:
PASS
```

Then still execute the real published runtime/UI gates.

## Optional naming cleanup

Rename the test class to something like:

```text
LifecycleAndPersistenceIntegrationTests
```

or:

```text
ServiceLifecycleRegressionTests
```

This naming change is optional; the actual missing runtime testing is the important part.

---

# 21. PLH8-002 — MEDIUM — unavailable prompt "move" test is a no-op

New test:

```text
Unavailable_prompt_state_and_actions
```

creates:

```csharp
new PromptRecord
{
    Id = pId,
    CategoryId = null,
    SortOrder = 10
}
```

The prompt is therefore on:

```text
Home
```

with:

```csharp
CategoryId == null
```

It then executes:

```csharp
service.MovePrompt(pId, null);
```

The destination is also:

```text
Home
```

So this is:

```text
null
→
null
```

---

# 22. Why PLH8-002 is a real test defect

`MovePrompt` intentionally treats same-category movement as a no-op.

Therefore:

```csharp
service.MovePrompt(pId, null);
```

does not demonstrate that an unavailable prompt can actually be moved to another category.

Yet the test comment says:

```text
Unavailable prompt can be moved and deleted
```

The delete portion is exercised.

The move portion is not.

---

# 23. Correct PLH8-002 test

Create a real category:

```csharp
Guid destinationId = ...
```

or use:

```csharp
service.CreateCategory(null, "Destination")
```

Then perform:

```csharp
service.MovePrompt(pId, destinationId);
```

Assert:

```text
prompt metadata CategoryId == destinationId
```

Then optionally move back to Home.

This verifies an actual metadata transition despite missing prompt content.

---

# 24. Also verify unavailable duplication rejection

The locked behavior says:

```text
Delete → enabled
Move → enabled
Edit → disabled
Copy → disabled
Duplicate → disabled
```

The service-level equivalent should include:

```text
DuplicatePrompt on unavailable content
→ fails safely / is not used
```

and UI-level verification should confirm:

```text
Copy instead of move
```

is disabled for unavailable prompts.

The current new test does not cover this.

---

# 25. PLH8-003 — HIGH — previous execution/provenance evidence is stale for current HEAD

Before this commit, the most recent reported execution evidence referred to:

```text
27aee1fc9b4395fd8475e401b441c428d69253a3
```

including the previously reported binary:

```text
ProductVersion:
0.1.0+27aee1fc9b4395fd8475e401b441c428d69253a3
```

Current:

```text
main
```

and:

```text
v0.1.0
```

now point to:

```text
e3d3fcea557ac54f8eaa8b1dd83f5bbcad94b15c
```

---

# 26. Why this invalidates the old final execution evidence

The new commit changes production XAML:

```text
NameDialog.xaml
ConfirmDeleteDialog.xaml
```

So it is not merely:

```text
test-only
documentation-only
```

The release binary produced from `27aee1fc` does not contain those new XAML changes.

Therefore the previous binary verification cannot be used as final evidence for:

```text
e3d3fcea
```

---

# 27. Test-count evidence is also stale

The previous reported suite was:

```text
149 tests
```

The current commit adds four new:

```text
[TestMethod]
```

methods.

So a correct current run should no longer simply report the same prior:

```text
149 / 149
```

result.

The current suite must be rediscovered and rerun.

If all four new methods are independently discovered as normal tests, the expected test count should increase accordingly.

Do not hardcode the expected number; use the runner's discovered count.

---

# 28. Current GitHub execution evidence

For:

```text
e3d3fcea557ac54f8eaa8b1dd83f5bbcad94b15c
```

GitHub currently exposes:

```text
combined statuses:
none

workflow runs:
none
```

This does not prove local tests were not run.

It does mean there is no independent CI evidence attached to the current release commit.

---

# 29. Current release-asset provenance status

This audit can independently verify:

```text
v0.1.0 source tag
==
e3d3fcea
```

It cannot independently download the public GitHub release ZIP with the available connector.

No new evidence was supplied in this turn showing:

```text
PromptHelper.exe ProductVersion
==
0.1.0+e3d3fcea...
```

Therefore the current correct state is:

```text
TAG / SOURCE:
PASS

BINARY / TAG CORRESPONDENCE:
NOT ESTABLISHED FOR CURRENT HEAD
```

---

# 30. Important consequence

If the public release ZIP is still the previously rebuilt:

```text
27aee1fc
```

binary, then the source/binary mismatch has reappeared.

If the ZIP was rebuilt and re-uploaded from:

```text
e3d3fcea
```

then it may be correct.

This audit cannot honestly choose between those two without new artifact evidence.

Therefore do not claim provenance PASS until the current public binary is rechecked.

---

# 31. Required PLH8-003 resolution

From a clean checkout of:

```text
e3d3fcea557ac54f8eaa8b1dd83f5bbcad94b15c
```

run:

```powershell
dotnet restore .\PromptHelper.slnx --force-evaluate

dotnet build .\PromptHelper.slnx -c Debug --no-restore /warnaserror

dotnet test .\tests\PromptHelper.Tests\PromptHelper.Tests.csproj `
  -c Debug `
  --no-build

dotnet build .\PromptHelper.slnx -c Release --no-restore /warnaserror

dotnet test .\tests\PromptHelper.Tests\PromptHelper.Tests.csproj `
  -c Release `
  --no-build
```

Then publish:

```powershell
dotnet publish `
  .\src\PromptHelper\PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  --no-restore `
  -o artifacts\publish\win-x64
```

---

# 32. Verify current binary revision

Inspect:

```powershell
[System.Diagnostics.FileVersionInfo]::GetVersionInfo(
  ".\artifacts\publish\win-x64\PromptHelper.exe"
).ProductVersion
```

Expected source revision should correspond to:

```text
e3d3fcea557ac54f8eaa8b1dd83f5bbcad94b15c
```

if SourceRevisionId is embedded as before.

---

# 33. Verify public asset again

Create the ZIP from that publish output.

Record:

```text
ZIP SHA-256
EXE SHA-256
ProductVersion
```

Upload the asset.

Then download the public GitHub release asset again and verify:

```text
downloaded ZIP hash == uploaded/local ZIP hash
EXE ProductVersion == current tag source
LICENSE present
THIRD_PARTY_NOTICES present
```

Only then restore:

```text
BINARY / TAG PROVENANCE:
PASS
```

---

# 34. Repeated retagging concern

The public tag:

```text
v0.1.0
```

has now been observed at multiple different commits during this audit sequence:

```text
c464190...
27aee1fc...
e3d3fcea...
```

Repeatedly force-moving a published version tag weakens reproducibility.

If this release has any external consumers, stop moving:

```text
v0.1.0
```

and publish a new immutable corrective version such as:

```text
v0.1.1
```

for further production-code changes.

This is a release-process warning.

It is not promoted here as an additional numbered defect because external consumption is unknown.

---

# 35. New `IsDefault` changes — no confirmed source bug

This audit specifically checked whether the two XAML changes themselves are problematic.

## NameDialog

Plan explicitly requires:

```text
Enter → Create/Save
Escape → Cancel
```

`IsDefault=True` is compatible.

## ConfirmDeleteDialog

The plan requires a custom:

```text
Cancel
Delete
```

dialog.

No contradictory authority forbidding default Delete was found.

So:

```text
NO CONFIRMED PRODUCT BUG
```

is raised for these two lines.

They still require runtime keyboard QA because they change real interaction behavior.

---

# 36. What the new test file does improve

The added integration file is not worthless.

It materially improves coverage for:

```text
full service-level category lifecycle
service-level prompt lifecycle
large prompt persistence
empty prompt persistence
restart persistence through repositories
orphan retention
double-corruption stop
future-schema stop
unavailable-prompt state
```

That should be retained.

The error is only treating it as a replacement for actual published GUI/runtime verification.

---

# 37. Current acceptance matrix

| Area | Current status |
|---|---|
| Static business/persistence source | PASS |
| New XAML source review | PASS |
| Current `main` == `v0.1.0` | PASS |
| New lifecycle integration tests exist | PASS |
| New integration tests actually executed | NOT ESTABLISHED |
| Debug build current HEAD | NOT ESTABLISHED |
| Release build current HEAD | NOT ESTABLISHED |
| Current full test suite | NOT ESTABLISHED |
| Current published EXE provenance | NOT ESTABLISHED |
| Actual GUI category CRUD | NOT ESTABLISHED |
| Actual GUI prompt CRUD | NOT ESTABLISHED |
| Actual Move dialog | NOT ESTABLISHED |
| Actual Duplicate dialog path | NOT ESTABLISHED |
| Actual clipboard | NOT ESTABLISHED |
| Published restart persistence | NOT ESTABLISHED |
| Published unavailable-prompt UI | NOT ESTABLISHED |
| Published orphan behavior | NOT ESTABLISHED |
| Published recovery smoke | NOT ESTABLISHED |
| Published future-schema smoke | NOT ESTABLISHED |
| Keyboard semantics | NOT ESTABLISHED |
| Offline runtime | NOT ESTABLISHED |
| 900×600 layout | HUMAN_REQUIRED |
| 125% DPI | HUMAN_REQUIRED |
| 150% DPI | HUMAN_REQUIRED |

---

# 38. Exact remaining weak-model runtime flow

After repairing the ineffective unavailable-move test and rerunning the suite, use a Tier-B capable executor where possible.

Run:

```text
1. published EXE first start
2. category create
3. nested category create
4. duplicate sibling validation
5. rename
6. non-empty delete rejection
7. empty delete confirmation
8. prompt create
9. prompt edit
10. empty prompt
11. 50k prompt
12. move
13. duplicate
14. clipboard exact equality
15. close
16. restart persistence
17. missing prompt
18. orphan
19. corrupt primary
20. double corruption
21. future schema
22. keyboard semantics
23. network-disabled offline workflow
24. 900×600 / 125% / 150% human/Tier-C visual QA
```

---

# 39. Do not substitute service tests for UI tests

The final report must explicitly distinguish:

```text
SERVICE INTEGRATION
```

from:

```text
PUBLISHED GUI E2E
```

Both are useful.

They are not interchangeable.

---

# 40. Final finding counts

## Confirmed new product implementation defects

```text
Critical: 0
High:     0
Medium:   0
Low:      0
```

## Release/test-process findings

```text
Critical: 0
High:     2
Medium:   1
Low:      0
```

---

# 41. Final verdict

```text
CURRENT SOURCE STATIC AUDIT:
PASS

NEW XAML CHANGES:
NO CONFIRMED DEFECT

CURRENT TAG == CURRENT MAIN:
PASS

NEW SERVICE/PERSISTENCE TEST COVERAGE:
IMPROVED

PLH7 RUNTIME GUI GAP:
NOT RESOLVED

CURRENT BUILD/TEST EXECUTION:
NOT ESTABLISHED

CURRENT RELEASE BINARY PROVENANCE:
NOT ESTABLISHED

FULL RELEASE VALIDATION:
NOT ACCEPTED
```

---

# 42. Bottom line

The latest attempt moves in the right direction, but it still confuses:

```text
service integration testing
```

with:

```text
published GUI/runtime testing
```

The new test file should stay, but it does **not** close the remaining runtime acceptance requirements.

Before another full-release PASS is accepted:

1. fix the ineffective unavailable-prompt move test;
2. rerun the complete current test suite at `e3d3fcea`;
3. rebuild/re-publish from that exact tag;
4. verify binary/tag provenance;
5. execute or explicitly classify every remaining actual GUI/runtime/offline/visual gate.

Do not make speculative changes to the stable persistence/business code.

---

# 43. Exact weak-implementer instructions

This section is deliberately procedural.

The implementer should **not design**, **not refactor broadly**, and **not reinterpret requirements**.

The remaining work is narrow:

```text
A. repair one ineffective regression test
B. rerun the complete test/build chain
C. rebuild and re-publish from the exact current release commit
D. verify source/tag/binary provenance
E. execute the remaining real published-runtime tests
F. leave visual-only checks explicitly HUMAN_REQUIRED unless screenshot-capable
```

Do not change stable persistence/business logic unless a newly executed test proves a real defect.

---

# 44. Required starting state

Before editing anything, run:

```powershell
git status --short
git rev-parse HEAD
git branch --show-current
git log -1 --oneline
git describe --tags --exact-match
```

Expected current release commit:

```text
e3d3fcea557ac54f8eaa8b1dd83f5bbcad94b15c
```

Expected:

```text
branch:
main

tag:
v0.1.0
```

If HEAD differs:

```text
STOP
```

Do not apply these instructions blindly to another revision.

Record the actual SHA and compare it against this document first.

If the working tree is dirty:

```text
record all changed files
do not delete them
do not run git clean
do not run git reset --hard
```

---

# 45. Task A — fix the ineffective unavailable-prompt move test

Open:

```text
tests/PromptHelper.Tests/PublishedLifecycleAndGuiFlowRegressionTests.cs
```

Find:

```csharp
[TestMethod]
public void Unavailable_prompt_state_and_actions()
```

The current defect is:

```csharp
CategoryId = null;
...
service.MovePrompt(pId, null);
```

This is:

```text
Home
→ Home
```

and therefore exercises only the intentional same-category no-op path.

---

# 46. Required replacement behavior for `Unavailable_prompt_state_and_actions`

Keep the test focused.

Do **not** rewrite the entire test file.

The corrected test must do all of the following:

```text
1. create an unavailable prompt on Home
2. create a real destination category
3. move the unavailable prompt from Home to that category
4. verify metadata CategoryId changed
5. verify missing prompt content remains unavailable
6. verify delete still succeeds
```

Recommended exact structure:

```csharp
[TestMethod]
public void Unavailable_prompt_state_and_actions()
{
    using var testDir = new TestDirectory();
    var paths = new AppPaths(testDir.Root);
    var writer = new AtomicTextWriter();
    var deleter = new FileDeleter();
    var libRepo = new LibraryRepository(paths, writer);
    var promptRepo = new PromptRepository(paths, writer, deleter);

    var pId = Guid.NewGuid();

    var destinationId = Guid.NewGuid();

    var doc = new LibraryDocument
    {
        Categories =
        [
            new CategoryRecord
            {
                Id = destinationId,
                ParentId = null,
                Name = "Destination",
                SortOrder = 10
            }
        ],
        Prompts =
        [
            new PromptRecord
            {
                Id = pId,
                CategoryId = null,
                SortOrder = 10
            }
        ]
    };

    libRepo.Commit(doc);

    var service = new PromptLibraryService(doc, libRepo, promptRepo);

    var prompts = service.GetPrompts(null);

    Assert.AreEqual(1, prompts.Count);
    Assert.IsFalse(prompts[0].IsContentAvailable);
    Assert.IsNotNull(prompts[0].LoadError);

    service.MovePrompt(pId, destinationId);

    var moved = service.CurrentDocument.Prompts.Single(p => p.Id == pId);
    Assert.AreEqual(destinationId, moved.CategoryId);

    var destinationPrompts = service.GetPrompts(destinationId);

    Assert.AreEqual(1, destinationPrompts.Count);
    Assert.AreEqual(pId, destinationPrompts[0].Id);
    Assert.IsFalse(destinationPrompts[0].IsContentAvailable);
    Assert.IsNotNull(destinationPrompts[0].LoadError);

    service.DeletePrompt(pId);

    Assert.IsFalse(service.CurrentDocument.Prompts.Any(p => p.Id == pId));
}
```

Use the repository's exact model constructors/properties if compilation requires a small syntax adjustment.

Do not alter service behavior merely to fit the test.

---

# 47. Optional but recommended companion regression

The locked behavior for unavailable prompts is:

```text
Delete      enabled
Move        enabled
Edit        disabled
Copy        disabled
Duplicate   disabled
```

At service level, duplication of an unavailable prompt should not silently create junk.

If there is already an existing test for unavailable duplication failure, do not duplicate it.

Otherwise add one focused test:

```csharp
[TestMethod]
public void Unavailable_prompt_cannot_be_duplicated()
{
    ...
    Assert.Throws<...>(() => service.DuplicatePrompt(...));
}
```

Use the **actual exception type currently specified by the service contract**.

Do not invent a new exception type.

If `DuplicatePrompt` currently fails because content read throws `FileNotFoundException`, and that is already the accepted contract, test exactly that.

Do not modify production code unless the current behavior contradicts the locked plan.

---

# 48. Do not rename the new test class as the first priority

The file name:

```text
PublishedLifecycleAndGuiFlowRegressionTests.cs
```

is misleading.

Renaming it would improve clarity, but it is **not required to fix the product**.

For a weak implementer:

```text
DO NOT spend time on broad naming cleanup
```

unless the user explicitly asks.

More important:

```text
do not use this test class as proof of published GUI E2E validation
```

In reports, call these:

```text
service/persistence integration tests
```

---

# 49. Task B — inspect the two XAML changes and do not expand them

Current intentional changes:

```text
NameDialog.xaml
ConfirmDeleteDialog.xaml
```

Current additions:

```xml
IsDefault="True"
```

Do not add more keyboard logic preemptively.

Do not add:

```text
PreviewKeyDown hacks
global keyboard hooks
custom command routing
extra event handlers
manual Enter dispatch logic
```

The existing locked semantics are sufficient.

For NameDialog:

```text
Enter → Create/Save
Escape → Cancel
```

For ConfirmDeleteDialog:

```text
Enter → Delete
Escape → Cancel
```

For PromptEditorDialog:

```text
Enter → newline
Escape → Cancel
Save must NOT be IsDefault
```

For MovePromptDialog:

```text
Enter → Move/Copy
Escape → Cancel
```

Before doing any further XAML change, inspect:

```text
PromptEditorDialog.xaml
MovePromptDialog.xaml
```

and confirm those semantics remain intact.

Do not change them unless the current source contradicts the locked plan.

---

# 50. Task C — run current test discovery before assuming test count

Do not hardcode:

```text
149
153
or any other total
```

Run:

```powershell
dotnet test .\tests\PromptHelper.Tests\PromptHelper.Tests.csproj `
  -c Debug `
  --list-tests
```

Save the output.

Then count discovered tests from the actual runner output.

Important:

```text
the correct number is whatever the current test runner discovers
```

If the four new methods are normal MSTest methods, the count should increase compared with the old 149.

If it does not:

```text
investigate discovery
```

Do not accept silently.

---

# 51. Task D — exact build/test sequence

Run from repository root.

## 51.1 Environment

```powershell
dotnet --info
dotnet --version
where.exe dotnet
```

Require stable .NET 10.

---

## 51.2 Restore

```powershell
dotnet restore .\PromptHelper.slnx --force-evaluate
```

Require:

```text
exit code 0
```

---

## 51.3 Debug build

```powershell
dotnet build .\PromptHelper.slnx `
  -c Debug `
  --no-restore `
  /warnaserror
```

Require:

```text
0 errors
0 project-source warnings
```

---

## 51.4 Debug tests

```powershell
dotnet test .\tests\PromptHelper.Tests\PromptHelper.Tests.csproj `
  -c Debug `
  --no-build `
  --logger "trx;LogFileName=current-debug.trx"
```

Require:

```text
0 failed
0 skipped unless explicitly expected by plan
```

---

## 51.5 Target the repaired unavailable test

Run separately:

```powershell
dotnet test .\tests\PromptHelper.Tests\PromptHelper.Tests.csproj `
  -c Debug `
  --no-build `
  --filter "FullyQualifiedName~Unavailable_prompt_state_and_actions"
```

Require:

```text
exactly one matching test
PASS
```

If zero tests match:

```text
FAIL
```

Do not count zero matches as success.

---

## 51.6 Release build

```powershell
dotnet build .\PromptHelper.slnx `
  -c Release `
  --no-restore `
  /warnaserror
```

Require:

```text
0 errors
0 project-source warnings
```

---

## 51.7 Release tests

```powershell
dotnet test .\tests\PromptHelper.Tests\PromptHelper.Tests.csproj `
  -c Release `
  --no-build `
  --logger "trx;LogFileName=current-release.trx"
```

Require:

```text
all discovered tests pass
0 failed
0 unexpected skipped
```

---

# 52. If any test fails

Follow this exact decision tree.

## 52.1 First classify

Ask:

```text
Is this:

A. a real product defect?
B. a bad new test?
C. an environment problem?
D. a stale test expectation?
```

Do not edit production code before classification.

---

## 52.2 If the test is wrong

Fix the test only.

Examples:

```text
same-category no-op mislabeled as move
wrong expected exception type
wrong setup order
wrong test fixture
```

Then rerun:

```text
targeted test
full Debug
full Release
```

---

## 52.3 If product code is wrong

Only then:

```text
make the smallest fix
add/repair regression test
rerun targeted test
rerun Debug full suite
rerun Release full suite
republish
redo affected runtime smoke
```

Do not refactor unrelated areas.

---

# 53. Explicit anti-regression rules

A weak implementer must not do any of the following while fixing the final issues:

```text
DO NOT:
- change persistence transaction ordering
- change primary commit-point semantics
- change backup warning semantics
- change orphan retention behavior
- change future-schema behavior
- change prompt ID generation algorithm
- change category sorting
- change prompt sorting
- change destination disambiguation
- change startup classification
- change default content
- add dependencies
- add networking
- add telemetry
- add logging frameworks
- add DI containers
- change target framework
- change application version casually
- change file layout
- change JSON schema
- rename public product terminology
```

These areas are already audited and stable.

---

# 54. Task E — publish from exact current release commit

After all tests pass, ensure the working tree contains only intended changes.

Run:

```powershell
git status --short
git rev-parse HEAD
git describe --tags --exact-match
```

The exact commit used for release must be recorded.

If you fixed the unavailable test and committed it, the release tag must point to **that new commit**, not the pre-fix commit.

Do not publish from an uncommitted dirty tree.

---

# 55. Stop force-moving `v0.1.0` after further production changes

The tag has already moved multiple times.

Best practice from this point:

```text
if only test/report files change:
decide whether v0.1.0 should remain immutable

if production files change again:
prefer a new tag/version such as v0.1.1
```

Do not repeatedly force-update the same public release tag after meaningful production changes.

For a weak implementer, the safest rule is:

```text
NO MORE FORCE-MOVING v0.1.0
```

unless the user explicitly instructs otherwise.

---

# 56. Task F — publish command

From the exact clean release commit:

```powershell
$publish = Join-Path $PWD "artifacts\publish\win-x64"

if (Test-Path $publish) {
    Remove-Item $publish -Recurse -Force
}

dotnet publish `
  .\src\PromptHelper\PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  --no-restore `
  -o $publish
```

Do not add:

```text
PublishSingleFile
PublishTrimmed
NativeAOT
```

---

# 57. Verify publish contents

Require:

```powershell
Test-Path "$publish\PromptHelper.exe"
Test-Path "$publish\LICENSE"
Test-Path "$publish\THIRD_PARTY_NOTICES.md"
```

All must return:

```text
True
```

Enumerate:

```powershell
Get-ChildItem $publish -Recurse |
  Select-Object FullName, Length
```

Confirm no test binaries are included.

---

# 58. Verify binary provenance

Run:

```powershell
$exe = "$publish\PromptHelper.exe"
$vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)

$vi.FileVersion
$vi.ProductVersion
```

Record both.

If `ProductVersion` includes a source SHA:

```text
that SHA must equal the exact release commit
```

If it does not:

```text
STOP
```

Do not upload the asset.

---

# 59. Hash before upload

Run:

```powershell
Get-FileHash "$publish\PromptHelper.exe" -Algorithm SHA256
```

Create release ZIP only from the publish directory.

Example:

```powershell
$zip = ".\artifacts\PromptHelper-v0.1.0-win-x64.zip"

if (Test-Path $zip) {
    Remove-Item $zip -Force
}

Compress-Archive `
  -Path "$publish\*" `
  -DestinationPath $zip
```

Then:

```powershell
Get-FileHash $zip -Algorithm SHA256
```

Record:

```text
EXE SHA-256
ZIP SHA-256
```

---

# 60. Public asset verification rule

After upload:

```text
do not verify only the local ZIP
```

Download the exact public GitHub release asset again.

Then verify:

```text
downloaded ZIP SHA-256 == locally created ZIP SHA-256
```

Extract the downloaded ZIP and verify again:

```text
PromptHelper.exe
LICENSE
THIRD_PARTY_NOTICES.md
ProductVersion
```

This is mandatory for provenance closure.

If the weak implementer cannot download the public asset:

```text
PUBLIC ASSET PROVENANCE:
BLOCKED_ENVIRONMENT
```

Do not claim PASS.

---

# 61. Task G — actual published GUI E2E test

The service tests do not satisfy this.

The published executable must actually be launched.

Use an isolated environment for destructive scenarios:

```text
Windows Sandbox
disposable VM
or dedicated disposable Windows user
```

Do not use valuable real `%LOCALAPPDATA%\PromptHelper` data.

---

# 62. GUI test capability gate

Before attempting full GUI automation, determine capability.

If the executor can use Windows UI Automation:

```text
Tier B
```

If it can also inspect screenshots:

```text
Tier C
```

If it only has shell access:

```text
Tier A
```

Do not fake Tier B/C results from source inspection.

---

# 63. Exact category GUI E2E flow

Launch:

```text
PromptHelper.exe
```

Then execute through the actual GUI:

```text
1. click + Add
2. enter E2E_Category_A
3. press Enter
4. verify category appears

5. open E2E_Category_A
6. click + Add
7. enter E2E_Nested
8. press Enter
9. verify nested category appears

10. try to create e2e_nested
11. verify duplicate sibling rejection

12. rename E2E_Nested
13. verify displayed name changes

14. create content inside parent
15. try deleting non-empty parent
16. verify exact locked message

17. create empty category E2E_Delete
18. delete it
19. verify custom Cancel/Delete confirmation
20. confirm
21. verify category disappears
```

Every step must be:

```text
PASS
FAIL
BLOCKED_ENVIRONMENT
HUMAN_REQUIRED
```

---

# 64. Exact prompt GUI E2E flow

Create through the actual editor:

```text
# E2E Prompt

Unicode:
ä ö ü ß 日本語 한국어 中文 Русский 🚀

```json
{
  "test": true
}
```
```

Then verify:

```text
prompt card appears
Edit opens correct text
edit and Save persists
empty prompt can be created
50k prompt can be created and reopened
Delete removes prompt after confirmation
```

Do not use direct service calls for this phase.

---

# 65. Exact Move GUI E2E flow

Create:

```text
Category A
Category B
Prompt in Category A
```

Through UI:

```text
click Move
verify current category preselected
select Category B
press Enter or click Move
verify prompt disappears from A
open B
verify same prompt appears
```

This is the test the service integration suite does not replace.

---

# 66. Exact Duplicate GUI E2E flow

Through UI:

```text
click Move
enable Copy instead of move
select destination
confirm
```

Verify:

```text
source remains
new prompt appears at destination
contents match source
```

---

# 67. Exact unavailable-prompt GUI E2E flow

In isolated test data:

```text
1. create prompt
2. close app
3. remove its .md file
4. restart app
```

Verify actual UI:

```text
card title:
(Unavailable prompt)

body:
[Prompt file could not be loaded.]

Delete:
enabled

Move:
enabled

Edit:
disabled

Copy:
disabled

Duplicate / Copy instead of move:
disabled
```

Then actually move the unavailable prompt to another category.

Verify it appears there still as unavailable.

This closes the exact gap that the ineffective service test missed.

---

# 68. Exact clipboard E2E test

Use a prompt containing:

```text
Unicode
Markdown
blank lines
code fence
```

Click actual:

```text
Copy
```

Read Windows clipboard mechanically.

Example helper:

```powershell
powershell.exe -STA -NoProfile -Command `
  "Add-Type -AssemblyName PresentationCore; [Windows.Clipboard]::GetText()"
```

Compare exact text byte-for-byte/string-for-string to expected.

Verify:

```text
Copied ✓
```

appears and later resets to:

```text
Copy
```

If clipboard cannot be inspected:

```text
BLOCKED_ENVIRONMENT
```

not PASS.

---

# 69. Exact restart persistence E2E test

After GUI mutations:

```text
close app normally
restart published EXE
```

Verify through UI:

```text
created categories remain
renamed categories remain
deleted categories remain absent
edited prompt remains edited
moved prompt remains moved
duplicate remains
empty prompt remains
```

This must be actual published-app restart behavior.

---

# 70. Exact orphan runtime test

In isolated data:

```text
1. close app
2. create arbitrary GUID-named .md in prompts
3. keep valid metadata
4. restart
```

Verify:

```text
app starts normally
orphan is not shown
orphan remains on disk
```

---

# 71. Exact corrupt-primary runtime test

In isolated data:

```text
valid library.json
valid library.backup.json
```

Then:

```text
close app
corrupt library.json
launch published EXE
```

Verify:

```text
app recovers
recovery warning appears
primary restored
prompt files preserved
recovery copy attempted
```

---

# 72. Exact double-corruption runtime test

In isolated data:

```text
corrupt library.json
corrupt library.backup.json
```

Launch.

Verify:

```text
fatal startup
no normal MainWindow
no default reinitialization
prompt files unchanged
```

Capture SHA-256 of prompt files before and after.

---

# 73. Exact future-schema runtime test

Set primary to:

```json
{
  "schemaVersion": 999
}
```

Keep old valid backup.

Launch.

Verify:

```text
future-schema fatal error
backup NOT restored
future primary unchanged
defaults NOT created
```

Hash primary before and after.

---

# 74. Exact keyboard runtime test

## NameDialog

Verify:

```text
Enter → Create/Save
Escape → Cancel
```

## ConfirmDeleteDialog

Verify:

```text
Enter → Delete
Escape → Cancel
```

## PromptEditorDialog

Verify:

```text
Enter → newline
Tab → editor input/tab behavior
Escape → Cancel
Enter does NOT save
```

## MovePromptDialog

Verify:

```text
Enter → Move/Copy
Escape → Cancel
```

## Main window

Verify:

```text
Tab
Shift+Tab
```

reach interactive controls.

Visible focus quality may remain:

```text
HUMAN_REQUIRED
```

if the executor cannot inspect visuals.

---

# 75. Exact offline runtime test

Preferred:

```text
Windows Sandbox or VM
network disabled
```

Run:

```text
startup
category create
category rename
category delete
prompt create
prompt edit
prompt delete
move
duplicate
clipboard
restart
```

Require all local functionality still works.

If full network disconnection cannot be done:

```text
OFFLINE RUNTIME:
BLOCKED_ENVIRONMENT
```

Do not substitute source grep.

Static privacy scan remains a separate PASS.

---

# 76. Exact visual/manual tests

These remain visual:

```text
900×600
125% DPI
150% DPI
visible focus quality
clipping/overlap
font rendering
dialog fit
```

If no screenshot-capable executor:

```text
HUMAN_REQUIRED
```

Do not infer PASS from XAML.

---

# 77. Required final evidence table

The final report must contain one row for every gate:

| Gate | Status | Evidence |
|---|---|---|
| Current commit recorded | PASS/FAIL | SHA |
| Tag == release commit | PASS/FAIL | SHA |
| Debug build | PASS/FAIL | log |
| Debug tests | PASS/FAIL | TRX/count |
| Release build | PASS/FAIL | log |
| Release tests | PASS/FAIL | TRX/count |
| Published EXE | PASS/FAIL | path/hash |
| Binary provenance | PASS/FAIL | ProductVersion |
| Public ZIP provenance | PASS/BLOCKED | hash |
| Category GUI CRUD | PASS/... | UI evidence |
| Prompt GUI CRUD | PASS/... | UI evidence |
| Move GUI | PASS/... | UI evidence |
| Duplicate GUI | PASS/... | UI evidence |
| Clipboard | PASS/... | exact comparison |
| Restart persistence | PASS/... | UI evidence |
| Unavailable prompt | PASS/... | UI evidence |
| Orphan | PASS/... | disk/UI evidence |
| Corrupt primary | PASS/... | recovery evidence |
| Double corruption | PASS/... | hashes |
| Future schema | PASS/... | hashes |
| Keyboard | PASS/... | automation evidence |
| Offline runtime | PASS/... | network-off evidence |
| 900×600 | PASS/HUMAN_REQUIRED | screenshot |
| 125% DPI | PASS/HUMAN_REQUIRED | screenshot |
| 150% DPI | PASS/HUMAN_REQUIRED | screenshot |

No required row may be omitted.

---

# 78. Final verdict rules

Only use:

```text
FULL RELEASE VALIDATION PASS
```

if every mandatory non-visual gate is actually PASS and every mandatory visual gate is also PASS.

If all automatable gates pass but visual checks remain:

```text
AUTOMATED/MECHANICAL VALIDATION PASS
FINAL VISUAL QA PENDING
```

If a required runtime gate cannot be executed:

```text
VALIDATION INCOMPLETE — ENVIRONMENT BLOCKED
```

If a real defect remains:

```text
VALIDATION FAIL
```

---

# 79. Minimal commit policy

For the weak implementer:

```text
Commit 1:
fix ineffective unavailable-prompt regression test
```

If no product code changes are needed, do not mix unrelated cleanup into that commit.

If real product defects are discovered later:

```text
one focused commit per logically related defect group
```

Do not create broad "cleanup" commits during final validation.

---

# 80. Final anti-defect checklist

Before claiming completion, answer every line:

```text
[ ] Did I avoid changing stable persistence logic without a failing test?
[ ] Did I fix the unavailable move test so it actually changes category?
[ ] Did I run current test discovery?
[ ] Did I run Debug build with /warnaserror?
[ ] Did I run all Debug tests?
[ ] Did I run Release build with /warnaserror?
[ ] Did I run all Release tests?
[ ] Did I build from the exact release commit?
[ ] Does binary ProductVersion match the release commit?
[ ] Did I hash the EXE?
[ ] Did I hash the ZIP?
[ ] Did I verify the public downloaded ZIP, not only the local one?
[ ] Did I distinguish service integration from published GUI E2E?
[ ] Did I run actual category GUI flow?
[ ] Did I run actual prompt GUI flow?
[ ] Did I run Move through the UI?
[ ] Did I run Duplicate through the UI?
[ ] Did I test actual Windows clipboard?
[ ] Did I restart the published app and verify persistence?
[ ] Did I test unavailable-prompt UI?
[ ] Did I test orphan preservation?
[ ] Did I test corrupt-primary recovery?
[ ] Did I test double-corruption safety?
[ ] Did I test future-schema safety?
[ ] Did I test keyboard semantics?
[ ] Did I run network-disabled offline smoke?
[ ] Did I mark visual-only checks HUMAN_REQUIRED if I could not see them?
[ ] Did I avoid calling unexecuted checks PASS?
```

If any answer is:

```text
No
```

the final release verification is not complete.

---

# 81. Copy-ready prompt for the weak implementer

```text
ROLE

You are the final defect-closure and release-verification executor for Prompt Helper.

You are a weaker implementation model. Follow these instructions literally.

CURRENT TARGET

Repository:
Ceegore/AI_prompt_helper

Current audited target before your fix:
e3d3fcea557ac54f8eaa8b1dd83f5bbcad94b15c

AUTHORITIES

1. Prompt Helper – Implementation Plan v1.2.0 FINAL AUDITED.md
2. _plh8.md
3. _plh_final_verification_concept.md

Do not invent requirements.

PRIMARY OBJECTIVE

Close only the remaining verified gaps:

1. repair the ineffective unavailable-prompt move regression test
2. rerun complete current build/test chain
3. rebuild release from the exact final commit
4. prove binary/tag/source provenance
5. execute real published GUI/runtime tests
6. classify visual-only checks honestly

DO NOT

Do not refactor stable persistence/business code.
Do not change transaction ordering.
Do not change schema.
Do not add dependencies.
Do not add networking.
Do not add telemetry.
Do not redesign UI.
Do not change category/prompt sorting.
Do not change GUID behavior.
Do not change backup semantics.
Do not weaken tests.
Do not mark unexecuted tests PASS.
Do not use service integration tests as proof of published GUI E2E.
Do not force-move v0.1.0 again unless the user explicitly instructs you.

STEP 1 — BASELINE

Run:

git status --short
git rev-parse HEAD
git branch --show-current
git log -1 --oneline
git describe --tags --exact-match

If the checked-out commit differs from the expected target, inspect the delta before doing anything.

STEP 2 — FIX ONLY THE BAD TEST

Open:

tests/PromptHelper.Tests/PublishedLifecycleAndGuiFlowRegressionTests.cs

Fix:

Unavailable_prompt_state_and_actions

The current test incorrectly performs:

Home → Home

by calling:

service.MovePrompt(pId, null)

Create a real destination category and move the unavailable prompt from Home to that category.

Assert:

- metadata CategoryId changed
- prompt remains unavailable
- destination GetPrompts returns it
- Delete still succeeds

Do not modify PromptLibraryService unless the corrected test proves a real product defect.

STEP 3 — DISCOVERY

Run:

dotnet test .\tests\PromptHelper.Tests\PromptHelper.Tests.csproj -c Debug --list-tests

Record the current discovered test count.

Never assume 149 or 153.

STEP 4 — FULL BUILD/TEST

Run in order:

dotnet restore .\PromptHelper.slnx --force-evaluate

dotnet build .\PromptHelper.slnx -c Debug --no-restore /warnaserror

dotnet test .\tests\PromptHelper.Tests\PromptHelper.Tests.csproj -c Debug --no-build

dotnet test .\tests\PromptHelper.Tests\PromptHelper.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~Unavailable_prompt_state_and_actions"

dotnet build .\PromptHelper.slnx -c Release --no-restore /warnaserror

dotnet test .\tests\PromptHelper.Tests\PromptHelper.Tests.csproj -c Release --no-build

Every required test must pass.

A zero-test filter result is FAIL.

STEP 5 — IF FAILURE

Classify first:

product defect
test defect
environment defect
stale expectation

Only change production code for a proven product defect.

If production changes:

- add regression test
- rerun targeted test
- rerun full Debug
- rerun full Release
- republish
- rerun affected runtime smoke

STEP 6 — RELEASE COMMIT

Commit only intended changes.

Record final:

git rev-parse HEAD

Do not publish from a dirty tree.

STEP 7 — PUBLISH

Run:

dotnet publish .\src\PromptHelper\PromptHelper.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts\publish\win-x64

Verify:

PromptHelper.exe
LICENSE
THIRD_PARTY_NOTICES.md

STEP 8 — PROVENANCE

Inspect PromptHelper.exe FileVersion and ProductVersion.

If ProductVersion contains a source SHA, require it to equal the final release commit.

Compute SHA-256 for:

PromptHelper.exe
release ZIP

Upload release asset.

Download the public asset again.

Require downloaded ZIP hash to equal the locally created ZIP hash.

Re-check executable ProductVersion inside the downloaded ZIP.

If you cannot download the public asset:

PUBLIC ASSET PROVENANCE = BLOCKED_ENVIRONMENT

not PASS.

STEP 9 — ACTUAL GUI E2E

Do not call service methods directly.

Launch the published PromptHelper.exe.

Use Windows UI Automation or equivalent if available.

Test actual GUI:

- category create
- nested category create
- duplicate sibling rejection
- rename
- non-empty delete rejection
- empty delete confirmation
- prompt create
- prompt edit
- empty prompt
- 50k prompt
- prompt delete
- Move
- Duplicate
- clipboard exact equality
- restart persistence
- unavailable prompt UI and actual move
- orphan behavior
- corrupt primary recovery
- double corruption
- future schema
- keyboard semantics

Use isolated disposable app data for destructive tests.

STEP 10 — OFFLINE

Prefer Windows Sandbox/VM with networking disabled.

Run normal workflows.

If impossible:

OFFLINE RUNTIME = BLOCKED_ENVIRONMENT

Do not replace with a source grep.

STEP 11 — VISUAL

900×600
125% DPI
150% DPI
clipping/overlap
focus visibility

If you cannot visually inspect:

HUMAN_REQUIRED

STEP 12 — FINAL REPORT

Every gate must be one of:

PASS
FAIL
BLOCKED_ENVIRONMENT
HUMAN_REQUIRED
NOT_APPLICABLE

Never omit a required gate.

Only say:

FULL RELEASE VALIDATION PASS

if every mandatory gate actually passed.

If automatable checks pass but visual checks remain:

AUTOMATED/MECHANICAL VALIDATION PASS
FINAL VISUAL QA PENDING

If execution is blocked:

VALIDATION INCOMPLETE — ENVIRONMENT BLOCKED

If a defect remains:

VALIDATION FAIL

Do not stop until every model-executable gate is complete.
```

---

# 82. Recommended final strategy

The safest path now is:

```text
do not touch stable product logic
↓
repair one bad test
↓
run entire suite
↓
publish from exact final commit
↓
prove binary/source/tag match
↓
perform real published GUI/runtime checks
↓
leave only truly visual checks to a human
```

This minimizes the chance that the final validation process itself introduces new bugs.
