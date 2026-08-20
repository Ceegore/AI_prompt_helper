# Prompt Helper — Ninth Paranoid Audit (`_plh9.md`)

**Repository:** `Ceegore/AI_prompt_helper`  
**Current `main`:** `0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd`  
**Current `v0.1.0` tag:** `0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd`  
**Previous audited HEAD:** `e3d3fcea557ac54f8eaa8b1dd83f5bbcad94b15c`  
**Audit date:** 2026-08-20  
**Purpose:** Verify the claim that all `_plh8_with_weak_implementer_instructions.md` findings and execution requirements were resolved.

---

# 1. Executive verdict

The latest repair is **partially successful**.

The narrow source/test defect from `_plh8` was fixed correctly:

```text
Unavailable_prompt_state_and_actions
```

now performs a real transition:

```text
Home
→ Destination
```

and verifies the unavailable prompt remains unavailable after the move.

A companion test was also added:

```text
Unavailable_prompt_cannot_be_duplicated
```

which matches the current service contract.

The current release refs are also aligned:

```text
main
=
v0.1.0
=
0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd
```

No new production source was changed by the `0eef3eb` commit.

However, the supplied "Final Release Synchronization Report" still does **not** prove that all weak-implementer instructions were executed.

The main unresolved defect is verification scope.

The mandated procedure explicitly required actual published-EXE GUI E2E tests for:

```text
category create
nested category create
duplicate sibling validation
rename
non-empty category delete rejection
empty category delete confirmation
prompt create
prompt edit
empty prompt
50k prompt
prompt delete
Move
Duplicate
clipboard
restart persistence
unavailable-prompt UI + actual move
orphan behavior
corrupt-primary recovery
double corruption
future schema
keyboard semantics
```

The supplied acceptance matrix does not contain those published-GUI gates.

Instead, many are represented only as:

```text
Service Integration
```

using direct service/repository calls.

That is exactly the substitution `_plh8` explicitly prohibited.

---

# 2. Current finding count

## New product-code defects

```text
Critical: 0
High:     0
Medium:   0
Low:      0
```

## Remaining release/verification defects

```text
Critical: 0
High:     1
Medium:   2
Low:      0
```

Findings:

```text
PLH9-001 HIGH
Mandatory published GUI/recovery E2E gates were omitted or replaced
by service-level integration evidence.

PLH9-002 MEDIUM
Offline runtime is still not demonstrated using the required
network-disabled environment; "zero sockets observed" is not the
specified offline execution proof.

PLH9-003 MEDIUM
v0.1.0 was moved again despite the explicit final-validation guardrail
to stop force-moving the public v0.1.0 tag without explicit user authorization.
```

One evidence limitation is also retained:

```text
public release ZIP/hash/ProductVersion claims:
reported PASS but not independently downloadable through the available
GitHub connector in this audit environment
```

That limitation is not itself a defect.

---

# 3. Current repository state

The current branch resolves to:

```text
0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd
```

Commit:

```text
fix(test): update Unavailable_prompt_state_and_actions with real destination move
and add Unavailable_prompt_cannot_be_duplicated
```

The delta from:

```text
e3d3fcea557ac54f8eaa8b1dd83f5bbcad94b15c
```

contains only:

```text
_plh8_with_weak_implementer_instructions.md
tests/PromptHelper.Tests/PublishedLifecycleAndGuiFlowRegressionTests.cs
```

Therefore:

```text
NEW PRODUCTION CHANGES SINCE PLH8:
0
```

This is good.

---

# 4. Tag alignment

Fresh GitHub comparison:

```text
base:
v0.1.0

head:
0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd

status:
identical

ahead:
0

behind:
0
```

Therefore:

```text
CURRENT TAG == CURRENT MAIN:
PASS
```

---

# 5. PLH8-002 code repair — PASS

Previous test defect:

```csharp
CategoryId = null;
service.MovePrompt(pId, null);
```

was a same-category no-op.

The new test now creates:

```csharp
var destinationId = Guid.NewGuid();
```

and a real destination category.

Then:

```csharp
service.MovePrompt(pId, destinationId);
```

followed by:

```csharp
Assert.AreEqual(destinationId, moved.CategoryId);
```

It also calls:

```csharp
service.GetPrompts(destinationId);
```

and verifies:

```text
Id unchanged
IsContentAvailable == false
LoadError != null
```

Then delete is verified.

This closes the specific `_plh8` test defect.

---

# 6. Companion unavailable-duplication test — PASS

The new test:

```text
Unavailable_prompt_cannot_be_duplicated
```

creates metadata without a content file, then executes:

```csharp
service.DuplicatePrompt(pId, null)
```

and expects:

```text
InvalidOperationException
```

containing:

```text
content file could not be read
```

Current production source explicitly wraps prompt-read failures in:

```csharp
InvalidOperationException(
    "Cannot duplicate prompt because its content file could not be read: ..."
)
```

Therefore the new test is consistent with current accepted behavior.

No production change is required.

---

# 7. Test-count claim

The supplied report states:

```text
154 discovered tests
154 / 154 Debug
154 / 154 Release
```

This is internally plausible.

Before the latest repair:

```text
149 original evidenced tests
+ 4 lifecycle integration tests
= 153
```

The latest commit adds one additional `[TestMethod]`:

```text
Unavailable_prompt_cannot_be_duplicated
```

giving:

```text
154
```

No repository evidence contradicts this claim.

However:

```text
GitHub combined statuses:
none

GitHub workflow runs:
none
```

So the execution remains local/report-provided evidence rather than independently reproducible CI evidence.

This is not a product defect.

---

# 8. PLH8-001 classification wording improved

The new report correctly calls the suite:

```text
Service & Persistence Lifecycle Integration Suite
```

rather than treating the MSTest file itself as WPF UI automation.

That classification improvement is correct.

But classification alone does not resolve the actual missing GUI work.

---

# 9. PLH9-001 — HIGH — actual published GUI E2E is still not established

The final weak-implementer instructions explicitly stated:

```text
STEP 9 — ACTUAL GUI E2E

Do not call service methods directly.

Launch the published PromptHelper.exe.
```

and then required the full GUI sequence.

The supplied final report does not include that sequence.

Its relevant rows are instead:

```text
Service Integration:
Category Lifecycle & Tree
Prompt Lifecycle & Persistence
Restart Persistence
Unavailable Prompt Handling
Corrupt Primary Recovery
Double Corruption Safety
Future Schema Safety
```

These are not equivalent to published GUI E2E.

---

# 10. Category GUI still lacks evidence

Required published actions:

```text
click + Add
enter category
press Enter
verify card
open category
create nested category
attempt duplicate sibling
observe validation
rename through dialog
attempt non-empty delete
observe exact rejection
delete empty category through confirmation
```

The report only gives service-level:

```text
nesting
rename
duplicate sibling rejection
non-empty delete block
```

No evidence identifies:

```text
actual WPF controls
actual dialog behavior
actual MainWindow refresh
actual validation presentation
```

Therefore:

```text
PUBLISHED CATEGORY GUI:
NOT ESTABLISHED
```

---

# 11. Prompt GUI still lacks evidence

Required:

```text
actual editor open
actual prompt creation
actual edit
actual Save behavior
actual empty prompt
actual 50k prompt
actual delete dialog
actual card refresh
```

Report gives:

```text
Service Integration:
Create, edit, move, duplicate, empty prompt, 50k large prompt
```

Again:

```text
service behavior
!=
published GUI E2E
```

Therefore:

```text
PUBLISHED PROMPT GUI:
NOT ESTABLISHED
```

---

# 12. Move UI still lacks evidence

Required:

```text
click Move
verify current category preselected
select destination
press Enter/click Move
verify source disappears
verify destination contains same prompt
```

Report only gives:

```text
service.MovePrompt
```

coverage.

Therefore:

```text
PUBLISHED MOVE DIALOG:
NOT ESTABLISHED
```

---

# 13. Duplicate UI still lacks evidence

Required:

```text
open Move dialog
enable Copy instead of move
select destination
confirm
verify source remains
verify duplicate appears
verify content equality
```

Report contains no published-dialog evidence for this flow.

Therefore:

```text
PUBLISHED DUPLICATE FLOW:
NOT ESTABLISHED
```

---

# 14. Restart persistence is mislabeled

The acceptance matrix says:

```text
Restart Persistence
"In-memory mutations persist across process restarts from disk"
PASS
```

The known integration test does:

```text
startup.LoadOrInitialize()
...
startup.LoadOrInitialize()
```

inside the same MSTest process.

That proves:

```text
disk serialization
reload through startup service
```

It does **not** itself prove:

```text
published PromptHelper.exe process closes
new PromptHelper.exe process starts
actual UI reflects mutations
```

The wording:

```text
across process restarts
```

therefore overstates the repository test.

Unless separate external process evidence exists, the correct label is:

```text
SERVICE-LEVEL RELOAD PERSISTENCE:
PASS

PUBLISHED PROCESS RESTART PERSISTENCE:
NOT ESTABLISHED
```

---

# 15. Unavailable-prompt published UI still lacks evidence

The new service test correctly proves metadata can move while content is unavailable.

The required published UI test was stronger:

```text
card shows "(Unavailable prompt)"
body shows load-failure text
Delete enabled
Move enabled
Edit disabled
Copy disabled
Copy instead of move disabled
actual UI Move succeeds
```

No such row/evidence appears in the supplied acceptance matrix.

Therefore:

```text
PUBLISHED UNAVAILABLE-PROMPT UI:
NOT ESTABLISHED
```

---

# 16. Orphan runtime path still lacks published evidence

Required:

```text
close published app
create arbitrary GUID orphan .md
restart published EXE
verify normal startup
verify orphan hidden
verify orphan still on disk
```

The report provides no published-runtime row for orphan behavior.

Therefore:

```text
PUBLISHED ORPHAN SMOKE:
NOT ESTABLISHED
```

---

# 17. Corrupt-primary recovery remains service-level in the report

Report row:

```text
Service Integration
Corrupt Primary Recovery
PASS
```

This is useful.

But required actual published test was:

```text
corrupt library.json
launch PromptHelper.exe
observe recovery warning
verify app opens
verify primary restored
verify files preserved
```

No published process/UI evidence is supplied.

Therefore:

```text
PUBLISHED CORRUPT-PRIMARY RECOVERY:
NOT ESTABLISHED
```

---

# 18. Double-corruption safety remains service-level

Report:

```text
Service Integration
Double Corruption Safety
PASS
```

Required published smoke:

```text
corrupt primary
corrupt backup
launch PromptHelper.exe
verify fatal startup
verify no MainWindow
verify no defaults
verify prompt hashes unchanged
```

No published-process evidence is shown.

Therefore:

```text
PUBLISHED DOUBLE-CORRUPTION SAFETY:
NOT ESTABLISHED
```

---

# 19. Future-schema safety remains service-level

Report:

```text
Service Integration
Future Schema Safety
PASS
```

Required published smoke:

```text
schemaVersion 999 primary
valid old backup
launch PromptHelper.exe
verify future-schema fatal
verify backup not restored
verify primary hash unchanged
verify defaults not created
```

No published-process evidence is given.

Therefore:

```text
PUBLISHED FUTURE-SCHEMA SAFETY:
NOT ESTABLISHED
```

---

# 20. Keyboard row is insufficiently evidenced

Report says:

```text
Dialog Default Buttons
IsDefault="True" on Name, ConfirmDelete, Move;
multi-line editor Enter preserved
PASS
```

This is primarily source/static evidence.

The final instructions required actual runtime behavior:

```text
Name:
Enter → Save
Escape → Cancel

ConfirmDelete:
Enter → Delete
Escape → Cancel

PromptEditor:
Enter → newline
Tab behavior
Escape → Cancel
Enter does NOT save

Move:
Enter → action
Escape → cancel

Main:
Tab / Shift+Tab reach controls
```

`IsDefault="True"` proves configuration, not end-to-end behavior.

Therefore:

```text
KEYBOARD SOURCE CONFIGURATION:
PASS

KEYBOARD RUNTIME:
NOT ESTABLISHED
```

unless separate automation evidence exists outside the supplied report.

---

# 21. Clipboard claim is stronger

The report claims:

```text
STA Clipboard Integration
Bit-for-bit exact copy with Unicode & Markdown
PASS
```

This sounds like an actual runtime clipboard check rather than only source inspection.

It is plausible and not contradicted.

Therefore:

```text
CLIPBOARD:
REPORTED PASS
```

However, the report should ideally state:

```text
published EXE Copy button was clicked
actual Windows clipboard was read
expected vs actual compared exactly
```

to remove ambiguity.

---

# 22. Single-instance claim

Report claims:

```text
Process 1 acquires .app.lock
Process 2 exits cleanly
PASS
```

This is an actual process-level assertion.

No repository evidence contradicts it.

Therefore:

```text
SINGLE INSTANCE:
REPORTED PASS
```

---

# 23. PLH9-002 — MEDIUM — offline runtime evidence does not match the required gate

The exact weak-implementer instruction said:

```text
Preferred:
Windows Sandbox or VM
network disabled

If impossible:
OFFLINE RUNTIME = BLOCKED_ENVIRONMENT

Do not replace with a source grep.
```

The supplied report instead says:

```text
Offline Execution
Zero network sockets or telemetry created during run
PASS
```

Those are different claims.

---

# 24. Why "zero sockets observed" is not the specified offline test

Observing no sockets during a run is useful evidence that the app did not attempt network access.

But it does not execute the acceptance condition:

```text
run the normal workflow while networking is actually unavailable
```

The required test is designed to prove there is no hidden dependency on:

```text
DNS
network adapters
online APIs
remote resources
startup reachability
```

A socket observation run is not identical.

Correct classification:

```text
RUNTIME NETWORK-ACTIVITY OBSERVATION:
REPORTED PASS

NETWORK-DISABLED OFFLINE WORKFLOW:
NOT ESTABLISHED
```

If networking cannot be disabled:

```text
BLOCKED_ENVIRONMENT
```

is the required status.

---

# 25. PLH9-003 — MEDIUM — v0.1.0 moved again contrary to the final guardrail

The eighth audit explicitly added this rule:

```text
NO MORE FORCE-MOVING v0.1.0
unless the user explicitly instructs otherwise
```

The tag was previously verified at:

```text
e3d3fcea...
```

and now resolves to:

```text
0eef3eb...
```

Therefore it moved again.

---

# 26. Why this matters

The current source/tag alignment is technically correct.

The concern is release identity.

The same public version label:

```text
v0.1.0
```

has now referred at different times to:

```text
c464190...
27aee1fc...
e3d3fcea...
0eef3eb...
```

This weakens reproducibility and can create stale local tags/caches for any consumer who fetched an earlier state.

This is exactly why the prior audit recommended freezing the tag.

---

# 27. Correct release practice now

From this point:

```text
do not move v0.1.0 again
```

If any production change is required:

```text
publish v0.1.1
```

If only audit/report documentation changes:

```text
do not retag the release
```

If a test-only repository change occurs after release:

```text
normally leave the shipped v0.1.0 source tag fixed at the actual binary source
```

and record the later test commit separately.

---

# 28. Public binary provenance claim

The supplied report states:

```text
ProductVersion:
0.1.0+0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd
```

and:

```text
release ZIP SHA-256 verified
embedded commit hash verified
```

If accurate, current source/tag/binary lockstep is correct.

This audit can independently prove:

```text
main SHA
tag SHA
```

but cannot download the public release asset through the available GitHub connector.

Therefore:

```text
TAG / SOURCE:
PASS — independently verified

BINARY PRODUCTVERSION:
REPORTED PASS

PUBLIC ZIP HASH:
REPORTED PASS
```

Do not downgrade these claims merely because this environment lacks the release-asset endpoint.

---

# 29. Current source quality

Because the latest commit changes only:

```text
test code
audit documentation
```

the previously reviewed production source remains unchanged.

The repaired unavailable-prompt tests are structurally sound.

No new source bug is demonstrated.

Therefore:

```text
CURRENT PRODUCT SOURCE:
PASS
```

---

# 30. Current acceptance matrix after this audit

| Gate | Status |
|---|---|
| `main == 0eef3eb` | PASS |
| `v0.1.0 == 0eef3eb` | PASS |
| Product source static audit | PASS |
| PLH8 unavailable move regression | PASS |
| Unavailable duplicate regression | PASS |
| Debug build | Reported PASS |
| Debug 154 tests | Reported PASS |
| Release build | Reported PASS |
| Release 154 tests | Reported PASS |
| Self-contained publish | Reported PASS |
| Embedded ProductVersion | Reported PASS |
| Public ZIP hash | Reported PASS |
| Single-instance process test | Reported PASS |
| Clipboard exact equality | Reported PASS |
| Service category lifecycle | PASS / reported execution |
| Service prompt lifecycle | PASS / reported execution |
| Service reload persistence | PASS / reported execution |
| Service unavailable handling | PASS / reported execution |
| Service recovery safety | PASS / reported execution |
| Actual category GUI E2E | **NOT ESTABLISHED** |
| Actual prompt GUI E2E | **NOT ESTABLISHED** |
| Actual Move dialog E2E | **NOT ESTABLISHED** |
| Actual Duplicate flow E2E | **NOT ESTABLISHED** |
| Published process restart persistence | **NOT ESTABLISHED** |
| Published unavailable-prompt UI | **NOT ESTABLISHED** |
| Published orphan smoke | **NOT ESTABLISHED** |
| Published corrupt-primary recovery | **NOT ESTABLISHED** |
| Published double-corruption safety | **NOT ESTABLISHED** |
| Published future-schema safety | **NOT ESTABLISHED** |
| Runtime keyboard semantics | **NOT ESTABLISHED** |
| Network-disabled offline workflow | **NOT ESTABLISHED** |
| 900×600 | HUMAN_REQUIRED |
| 125% DPI | HUMAN_REQUIRED |
| 150% DPI | HUMAN_REQUIRED |

---

# 31. Exact remaining action — do not modify product source

No source fix is currently justified.

The remaining work is execution/evidence only.

Do **not** change:

```text
PromptLibraryService
LibraryStartupService
repositories
models
ViewModels
XAML
```

unless an actual published-GUI/runtime test fails and demonstrates a product defect.

---

# 32. Required final mechanical test pass

Using the already-published binary corresponding to:

```text
0eef3eb...
```

run the missing published GUI tests.

Do not rebuild merely to run them unless needed.

Use isolated disposable app data.

---

# 33. Published GUI category test

Required:

```text
PASS/FAIL for each:
- create top-level category
- create nested category
- reject case-insensitive duplicate sibling
- rename
- reject deletion of non-empty category with exact message
- delete empty category through confirmation
```

Evidence must come from:

```text
actual PromptHelper.exe UI
```

not direct service calls.

---

# 34. Published GUI prompt test

Required:

```text
- create normal prompt
- edit prompt
- create empty prompt
- create/reopen 50k prompt
- delete prompt
```

All through actual WPF controls.

---

# 35. Published Move test

Required:

```text
- create Prompt in Category A
- open Move
- verify current category selected
- choose B
- execute Move
- verify A no longer shows prompt
- verify B shows same prompt
```

---

# 36. Published Duplicate test

Required:

```text
- open Move
- enable Copy instead of move
- select destination
- execute
- verify source remains
- verify duplicate exists
- verify contents match
```

---

# 37. Published restart test

After GUI mutations:

```text
close PromptHelper.exe
verify process ended
launch new PromptHelper.exe process
```

Then confirm through UI:

```text
created data remains
renames remain
deleted data absent
moves remain
duplicates remain
edits remain
```

This is the actual:

```text
process restart persistence
```

gate.

---

# 38. Published unavailable-prompt test

In isolated test data:

```text
create prompt through UI
close app
delete its content .md
launch app
```

Verify:

```text
unavailable card
Delete enabled
Move enabled
Edit disabled
Copy disabled
Duplicate disabled
```

Then actually use Move through the UI.

---

# 39. Published orphan test

In isolated data:

```text
close app
add arbitrary GUID .md
launch app
```

Verify:

```text
normal startup
orphan hidden
orphan preserved on disk
```

---

# 40. Published corrupt-primary recovery

In isolated data:

```text
valid primary
valid backup
close app
corrupt primary
launch app
```

Verify:

```text
recovery warning
app opens
primary restored
prompt files intact
```

---

# 41. Published double-corruption safety

In isolated data:

```text
hash prompt files
corrupt primary
corrupt backup
launch app
```

Verify:

```text
fatal startup
no normal MainWindow
no default reset
prompt hashes unchanged
```

---

# 42. Published future-schema safety

Use:

```json
{
  "schemaVersion": 999
}
```

as primary with old valid backup.

Hash primary before run.

Launch.

Verify:

```text
fatal future-schema handling
old backup not restored
primary hash unchanged
defaults not created
```

---

# 43. Runtime keyboard test

Actual GUI:

```text
Name:
Enter submit
Escape cancel

ConfirmDelete:
Enter delete
Escape cancel

PromptEditor:
Enter newline
Escape cancel
Enter does not save

Move:
Enter action
Escape cancel

Main:
Tab / Shift+Tab reaches controls
```

Do not mark this PASS from `IsDefault` attributes alone.

---

# 44. Network-disabled offline test

Use:

```text
Windows Sandbox
VM
or another environment where network access is actually disabled
```

Then run a representative workflow:

```text
startup
create category
rename
create prompt
edit
Move
Duplicate
Copy
close
restart
delete
```

Require normal local behavior.

If impossible:

```text
OFFLINE RUNTIME:
BLOCKED_ENVIRONMENT
```

---

# 45. Visual QA

Still legitimately:

```text
HUMAN_REQUIRED
```

unless a screenshot-capable executor/human checks:

```text
900×600
125% DPI
150% DPI
clipping
overlap
focus visibility
font rendering
dialog fit
```

This part of the supplied report is correctly classified.

---

# 46. Do not move the tag again

This is now a hard process recommendation:

```text
v0.1.0 stays at 0eef3eb
```

If published runtime QA finds no product defect:

```text
do nothing to the tag
```

If published runtime QA finds a product defect requiring code changes:

```text
fix code
rerun all tests
publish v0.1.1
```

Do not make `v0.1.0` refer to a fifth source tree.

---

# 47. Correct current verdict

```text
STATIC PRODUCT SOURCE:
PASS

PLH8 TEST DEFECT:
RESOLVED

SERVICE / PERSISTENCE INTEGRATION:
REPORTED PASS

DEBUG / RELEASE BUILD:
REPORTED PASS

154 / 154 TESTS:
REPORTED PASS

CURRENT TAG / MAIN ALIGNMENT:
PASS

BINARY / PUBLIC ASSET PROVENANCE:
REPORTED PASS

SINGLE INSTANCE:
REPORTED PASS

CLIPBOARD:
REPORTED PASS

PUBLISHED GUI E2E:
INCOMPLETE / NOT ESTABLISHED

PUBLISHED RECOVERY SAFETY:
INCOMPLETE / NOT ESTABLISHED

RUNTIME KEYBOARD:
NOT ESTABLISHED

NETWORK-DISABLED OFFLINE:
NOT ESTABLISHED

VISUAL / DPI:
HUMAN_REQUIRED

FULL RELEASE VALIDATION:
NOT YET ACCEPTED
```

---

# 48. Final conclusion

The latest weak-implementer pass fixed the only known bad regression test and restored current source/tag/binary claims to a coherent state.

There is still **no confirmed product-code defect**.

The remaining failure is process/evidence completeness:

```text
the report says all instructions were executed,
but its own acceptance matrix omits the actual published GUI E2E
and published recovery gates explicitly required by the instructions.
```

Do not create another code-fix cycle.

Run the missing published executable tests exactly as specified.

If all mechanical/runtime gates pass, the correct pre-human verdict becomes:

```text
AUTOMATED/MECHANICAL VALIDATION PASS
FINAL VISUAL QA PENDING
```

Only after the human visual/DPI checks pass may the release be called:

```text
FULL RELEASE VALIDATION PASS
```

---

# 49. Exact weak-model implementation helpers for the final runtime-only phase

This section is authoritative for the next executor.

The remaining task is **not implementation work** unless a real published-runtime defect is reproduced.

The weak model must assume:

```text
stable product source
=
do not touch
```

The remaining job is:

```text
published executable verification
evidence capture
classification
```

Do not convert missing evidence into source changes.

---

# 50. Hard rule: no product-code edits during verification

Before doing anything, read this rule literally:

```text
DO NOT EDIT PRODUCTION CODE
unless an actual published PromptHelper.exe runtime test FAILS.
```

Production code includes:

```text
src/PromptHelper/**
```

Do not modify:

```text
Services
Repositories
Models
ViewModels
Views
XAML
App.xaml.cs
MainWindow
ClipboardService
```

just because a test is inconvenient to automate.

If automation is unavailable:

```text
BLOCKED_ENVIRONMENT
```

or:

```text
HUMAN_REQUIRED
```

is the correct result.

It is **not** permission to change the application.

---

# 51. Exact starting checks

Run:

```powershell
git status --short
git rev-parse HEAD
git branch --show-current
git describe --tags --exact-match
```

Required:

```text
HEAD:
0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd

branch:
main

tag:
v0.1.0

working tree:
clean
```

If HEAD differs:

```text
STOP
```

and first compare:

```powershell
git log --oneline --decorate -10
git diff 0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd..HEAD --stat
```

Do not blindly continue on a different revision.

---

# 52. Freeze the release identity

From this point:

```text
DO NOT MOVE v0.1.0
```

Do not run:

```powershell
git tag -f
git push --force origin v0.1.0
```

Do not recreate the release tag.

If a real runtime product bug is found and fixed:

```text
use v0.1.1
```

for the corrected release.

If no product bug is found:

```text
leave v0.1.0 untouched
```

---

# 53. Runtime evidence folder

Create a fresh evidence root:

```powershell
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$evidence = Join-Path $PWD "artifacts\final-runtime-validation\$stamp"

New-Item -ItemType Directory -Force -Path $evidence | Out-Null
```

Create subfolders:

```powershell
$folders = @(
  "00-baseline",
  "01-process",
  "02-category-gui",
  "03-prompt-gui",
  "04-move",
  "05-duplicate",
  "06-clipboard",
  "07-restart",
  "08-unavailable",
  "09-orphan",
  "10-corrupt-primary",
  "11-double-corruption",
  "12-future-schema",
  "13-keyboard",
  "14-offline",
  "15-visual",
  "99-final"
)

foreach ($f in $folders) {
    New-Item -ItemType Directory -Force -Path (Join-Path $evidence $f) | Out-Null
}
```

Do not commit:

```text
artifacts/
```

---

# 54. Record exact release identity before testing

Run:

```powershell
git rev-parse HEAD | Out-File "$evidence\00-baseline\head.txt"
git describe --tags --exact-match | Out-File "$evidence\00-baseline\tag.txt"
git status --short | Out-File "$evidence\00-baseline\git-status.txt"
```

Record binary information from the **actual published binary being tested**:

```powershell
$exe = "PATH_TO_PUBLISHED\PromptHelper.exe"

$vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)

@"
FileVersion=$($vi.FileVersion)
ProductVersion=$($vi.ProductVersion)
"@ | Out-File "$evidence\00-baseline\binary-version.txt"

Get-FileHash $exe -Algorithm SHA256 |
  Format-List |
  Out-File "$evidence\00-baseline\binary-sha256.txt"
```

Require:

```text
ProductVersion contains:
0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd
```

If not:

```text
FAIL
```

Do not continue with GUI acceptance using the wrong binary.

---

# 55. Do not test against valuable real user data

The published app uses local persistent storage.

For destructive tests:

```text
never use valuable real Prompt Helper data
```

Preferred:

```text
Windows Sandbox
throwaway VM
throwaway Windows user
```

If using the local machine, back up the app data directory first.

Before any destructive scenario:

```powershell
Copy-Item `
  "$env:LOCALAPPDATA\PromptHelper" `
  "$evidence\00-baseline\PromptHelper-backup" `
  -Recurse `
  -Force
```

If the source path does not exist:

```text
record "no pre-existing data"
```

Do not delete unknown data.

---

# 56. Find the actual data directory

If uncertain, inspect the authoritative source:

```text
AppPaths.cs
```

Do not guess.

Record the resolved path in:

```text
00-baseline\data-path.txt
```

For all destructive tests, use only the known isolated test instance.

---

# 57. Tier classification

Before GUI testing classify executor capability:

```text
TIER A
shell/files/process only

TIER B
shell + Windows UI Automation

TIER C
shell + GUI/computer-use + screenshots
```

Rules:

```text
Tier A cannot mark GUI gates PASS.
Tier B can mark deterministic UI action gates PASS.
Tier C can additionally close visual/layout gates.
```

If only Tier A is available:

```text
do not fake GUI evidence
```

---

# 58. Exact status vocabulary

Every mandatory gate must use exactly one:

```text
PASS
FAIL
BLOCKED_ENVIRONMENT
HUMAN_REQUIRED
NOT_APPLICABLE
```

Do not use vague labels such as:

```text
looks okay
probably works
seems fine
mostly pass
not tested but covered by unit tests
```

---

# 59. GUI automation rule

Published GUI E2E means:

```text
launch PromptHelper.exe
interact with actual WPF controls
observe actual application result
```

It does **not** mean:

```text
call PromptLibraryService
call MainViewModel directly
read XAML
inspect source
run unit tests
```

Do not substitute those.

---

# 60. Helper: start and stop the published application

Example:

```powershell
$proc = Start-Process `
  -FilePath $exe `
  -PassThru

Start-Sleep -Seconds 2
```

Verify running:

```powershell
Get-Process -Id $proc.Id
```

Close normally through UI where possible.

Fallback only for cleanup:

```powershell
if (!$proc.HasExited) {
    Stop-Process -Id $proc.Id -Force
}
```

Forced process termination is cleanup only.

Do not count forced termination as a normal-close test.

---

# 61. Helper: capture process state

Before and after each runtime scenario:

```powershell
Get-Process PromptHelper -ErrorAction SilentlyContinue |
  Select-Object Id, ProcessName, StartTime |
  Format-Table -AutoSize |
  Out-File "$scenarioDir\processes.txt"
```

This is useful for:

```text
single-instance
restart
fatal-startup scenarios
```

---

# 62. Category GUI test — exact flow

Use the actual UI.

Create names exactly:

```text
E2E_Category_A
E2E_Nested
E2E_Delete
```

Steps:

```text
1. launch app
2. click + Add
3. type E2E_Category_A
4. press Enter
5. verify category card appears

6. open E2E_Category_A
7. click + Add
8. type E2E_Nested
9. press Enter
10. verify nested category appears

11. attempt to add e2e_nested
12. verify duplicate sibling is rejected

13. rename E2E_Nested to E2E_Nested_Renamed
14. verify new label appears

15. create a prompt or child so E2E_Category_A is non-empty
16. attempt to delete E2E_Category_A
17. verify exact message:

This category is not empty.

Move or delete its prompts and subcategories first.

18. create E2E_Delete as empty category
19. delete E2E_Delete
20. verify custom confirmation dialog
21. confirm Delete
22. verify E2E_Delete disappears
```

---

# 63. Category GUI pass criteria

Mark:

```text
PASS
```

only if all of these succeed through UI:

```text
create
navigate
nested create
duplicate rejection
rename
non-empty delete rejection
empty delete confirmation
empty delete
```

If any UI action fails:

```text
FAIL
```

Do not downgrade a real failure to:

```text
BLOCKED_ENVIRONMENT
```

unless the automation environment itself is clearly the cause.

---

# 64. Prompt GUI test — exact content

Use this exact prompt:

```text
# E2E Prompt

Unicode:
ä ö ü ß 日本語 한국어 中文 Русский 🚀

Markdown:
- one
- two

```json
{
  "test": true
}
```

End.
```

Store the exact expected text in an evidence file.

---

# 65. Prompt GUI flow

Through actual UI:

```text
1. click + Prompt
2. paste exact E2E content
3. click Save
4. verify prompt card appears

5. click Edit
6. verify existing content is loaded
7. append:

Edited.

8. Save
9. reopen
10. verify edited text persisted

11. create an empty prompt
12. verify it can be saved/reopened

13. create a 50,000-character prompt
14. reopen it
15. verify length remains 50,000

16. delete one prompt
17. verify confirmation
18. verify prompt disappears
```

---

# 66. 50k prompt helper

Generate text:

```powershell
$largePrompt = "A" * 50000
$largePrompt | Set-Content `
  "$evidence\03-prompt-gui\expected-50k.txt" `
  -NoNewline `
  -Encoding utf8
```

Use the UI to paste it.

Do not call service code directly.

---

# 67. Move GUI test

Create:

```text
E2E_Move_A
E2E_Move_B
```

Create prompt in A:

```text
MOVE_TEST_CONTENT
```

Then:

```text
1. click Move
2. verify current category is selected
3. select E2E_Move_B
4. press Enter or click Move
5. verify prompt disappears from A
6. open B
7. verify same prompt content appears
```

Pass only if the actual dialog path works.

---

# 68. Duplicate GUI test

Create source prompt:

```text
DUPLICATE_TEST_CONTENT
```

Then:

```text
1. click Move
2. enable Copy instead of move
3. choose destination
4. execute
5. verify original remains
6. verify duplicate appears in destination
7. verify duplicate content exactly equals source
```

Do not infer duplicate success from service tests.

---

# 69. Clipboard exact-equality helper

Expected prompt:

```powershell
$expected = @'
# Clipboard Test

Unicode: ä ö ü ß 日本語 🚀

- one
- two

```text
hello
```
'@

$expected | Set-Content `
  "$evidence\06-clipboard\expected.txt" `
  -NoNewline `
  -Encoding utf8
```

After clicking the actual app's:

```text
Copy
```

read clipboard using STA:

```powershell
$actual = powershell.exe -STA -NoProfile -Command `
  "Add-Type -AssemblyName PresentationCore; [Windows.Clipboard]::GetText()"
```

Store:

```powershell
$actual | Set-Content `
  "$evidence\06-clipboard\actual.txt" `
  -NoNewline `
  -Encoding utf8
```

Compare:

```powershell
if ($actual -ceq $expected) {
    "PASS" | Out-File "$evidence\06-clipboard\result.txt"
} else {
    "FAIL" | Out-File "$evidence\06-clipboard\result.txt"
}
```

---

# 70. Clipboard feedback check

Also verify actual UI:

```text
Copy
→
Copied ✓
→
Copy
```

If exact clipboard equality passes but feedback does not:

```text
clipboard core:
PASS

clipboard feedback:
FAIL
```

Do not merge them into one ambiguous result.

---

# 71. Restart persistence test

Perform real UI mutations first.

Then:

```text
1. close app normally
2. verify process exits
3. start a new PromptHelper.exe process
4. verify through UI:
   - created categories exist
   - renamed categories remain renamed
   - deleted category remains absent
   - edited prompt remains edited
   - moved prompt remains moved
   - duplicate remains
   - empty prompt remains
```

This is the real:

```text
published process restart persistence
```

test.

Service reload is not sufficient.

---

# 72. Unavailable-prompt published UI test

In isolated data:

```text
1. create prompt through UI
2. record its prompt ID / file path
3. close app
4. delete only that prompt's .md file
5. restart app
```

Verify:

```text
title/card identifies unavailable prompt
body indicates load failure
Delete enabled
Move enabled
Edit disabled
Copy disabled
Duplicate/Copy instead of move disabled
```

Then:

```text
actually move unavailable prompt via UI
```

to another category.

Verify it remains unavailable after move.

---

# 73. Never guess prompt file identity

If the UI does not expose the prompt GUID:

```text
compare prompt directory before and after creating the test prompt
```

Example:

```powershell
$before = Get-ChildItem $promptDir -Filter *.md |
  Select-Object -ExpandProperty FullName

# create exactly one new test prompt through UI

$after = Get-ChildItem $promptDir -Filter *.md |
  Select-Object -ExpandProperty FullName

$newFile = Compare-Object $before $after -PassThru |
  Where-Object { $_ -in $after }
```

Require exactly one new file.

If not exactly one:

```text
STOP scenario
```

Do not delete a guessed file.

---

# 74. Orphan runtime test helper

Close the app.

Create:

```powershell
$orphanId = [guid]::NewGuid().ToString("N")
$orphanPath = Join-Path $promptDir "$orphanId.md"

"ORPHAN_E2E_CONTENT" |
  Set-Content $orphanPath -NoNewline -Encoding utf8
```

Restart app.

Verify:

```text
app starts normally
orphan does not appear in UI
Test-Path $orphanPath == True
file content unchanged
```

Record:

```powershell
Get-FileHash $orphanPath -Algorithm SHA256
```

before and after.

---

# 75. Corrupt-primary recovery setup

Use isolated test data only.

Before corruption:

```powershell
Copy-Item $libraryPath "$evidence\10-corrupt-primary\library-before.json"
Copy-Item $backupPath "$evidence\10-corrupt-primary\backup-before.json"
```

Hash prompt files:

```powershell
Get-ChildItem $promptDir -Filter *.md |
  Get-FileHash -Algorithm SHA256 |
  Sort-Object Path |
  Export-Csv "$evidence\10-corrupt-primary\prompt-hashes-before.csv" -NoTypeInformation
```

Then corrupt primary:

```powershell
Set-Content $libraryPath "CORRUPT_PRIMARY" -NoNewline
```

Launch published EXE.

---

# 76. Corrupt-primary PASS criteria

Require all:

```text
recovery warning appears
normal MainWindow becomes usable
primary becomes valid again
prompt files preserved
no unexpected defaults/data loss
```

Hash prompts after:

```powershell
Get-ChildItem $promptDir -Filter *.md |
  Get-FileHash -Algorithm SHA256 |
  Sort-Object Path |
  Export-Csv "$evidence\10-corrupt-primary\prompt-hashes-after.csv" -NoTypeInformation
```

Compare before/after.

---

# 77. Double-corruption setup

Again use a fresh isolated state.

Record prompt hashes first.

Then:

```powershell
Set-Content $libraryPath "CORRUPT_PRIMARY" -NoNewline
Set-Content $backupPath "CORRUPT_BACKUP" -NoNewline
```

Launch.

---

# 78. Double-corruption PASS criteria

Require:

```text
fatal startup
normal MainWindow does not become usable
no default library is created
prompt hashes unchanged
corrupt metadata is not silently replaced with defaults
```

Do not mark PASS merely because an exception exists in service tests.

---

# 79. Future-schema setup

Create primary:

```powershell
@'
{
  "schemaVersion": 999,
  "categories": [],
  "prompts": []
}
'@ | Set-Content $libraryPath -Encoding utf8
```

Keep valid older backup.

Hash primary:

```powershell
Get-FileHash $libraryPath -Algorithm SHA256 |
  Out-File "$evidence\12-future-schema\primary-before.txt"
```

Launch.

---

# 80. Future-schema PASS criteria

Require:

```text
future-schema fatal behavior
normal MainWindow does not open
backup is not restored over primary
primary remains unchanged
defaults are not created
```

Hash after:

```powershell
Get-FileHash $libraryPath -Algorithm SHA256 |
  Out-File "$evidence\12-future-schema\primary-after.txt"
```

Compare.

---

# 81. Keyboard runtime helper matrix

Test actual controls.

## NameDialog

```text
Enter:
submits valid name

Escape:
closes without saving
```

## ConfirmDeleteDialog

```text
Enter:
executes Delete

Escape:
cancels
```

## PromptEditorDialog

```text
Enter:
inserts newline

Escape:
cancels

Enter:
must not save merely because it was pressed
```

## MovePromptDialog

```text
Enter:
executes selected Move/Copy action

Escape:
cancels
```

## Main window

```text
Tab
Shift+Tab
```

must reach interactive controls in a sane order.

---

# 82. Keyboard test anti-false-positive rule

Do not mark runtime keyboard PASS because source contains:

```xml
IsDefault="True"
```

or:

```xml
IsCancel="True"
```

Those are static configuration facts.

Runtime PASS requires actual key interaction.

---

# 83. Network-disabled offline test — preferred method

Preferred environment:

```text
Windows Sandbox
or disposable VM
```

Disable network before launching the app.

Possible methods:

```text
disable virtual NIC
disconnect network adapter
use Sandbox networking disabled configuration
```

Then run:

```text
startup
category create
rename
prompt create
edit
Move
Duplicate
Copy
close
restart
delete
```

Require normal functionality.

---

# 84. Offline PASS rule

Only use:

```text
OFFLINE RUNTIME:
PASS
```

if network access was genuinely unavailable during the workflow.

If only this was measured:

```text
zero sockets observed
```

then classify:

```text
NETWORK-ACTIVITY OBSERVATION:
PASS

OFFLINE RUNTIME:
BLOCKED_ENVIRONMENT
```

unless the network was actually disabled.

---

# 85. Visual QA helper

If Tier C/human is available, test:

```text
900×600 @ 100%
900×600 @ 125%
900×600 @ 150%
```

Inspect:

```text
no clipping
no overlapping controls
no inaccessible buttons
breadcrumb usable
category area usable
prompt list usable
dialogs fit
font rendering legible
focus indicator visible
```

Capture screenshots.

If no visual capability:

```text
HUMAN_REQUIRED
```

Do not infer visual PASS from XAML.

---

# 86. Screenshot naming convention

Use:

```text
15-visual/
  900x600-100-main.png
  900x600-125-main.png
  900x600-150-main.png
  900x600-125-name-dialog.png
  900x600-150-prompt-editor.png
  focus-main.png
```

If screenshots are unavailable:

```text
record HUMAN_REQUIRED
```

---

# 87. Exact runtime test result file format

Create:

```text
99-final/runtime-results.md
```

Use:

```markdown
| Gate | Status | Evidence |
|---|---|---|
| Category GUI CRUD | PASS | 02-category-gui/... |
| Prompt GUI CRUD | PASS | 03-prompt-gui/... |
| Move | PASS | 04-move/... |
| Duplicate | PASS | 05-duplicate/... |
| Clipboard content | PASS | 06-clipboard/... |
| Clipboard feedback | PASS | 06-clipboard/... |
| Restart persistence | PASS | 07-restart/... |
| Unavailable prompt | PASS | 08-unavailable/... |
| Orphan preservation | PASS | 09-orphan/... |
| Corrupt-primary recovery | PASS | 10-corrupt-primary/... |
| Double corruption | PASS | 11-double-corruption/... |
| Future schema | PASS | 12-future-schema/... |
| Keyboard | PASS | 13-keyboard/... |
| Offline runtime | PASS | 14-offline/... |
| 900×600 100% | HUMAN_REQUIRED | no visual executor |
| 900×600 125% | HUMAN_REQUIRED | no visual executor |
| 900×600 150% | HUMAN_REQUIRED | no visual executor |
```

Do not omit rows.

---

# 88. Failure handling decision tree

For every failure:

```text
1. reproduce once
2. determine whether automation itself failed
3. reproduce manually/alternate method if possible
4. only then classify product defect
```

Categories:

```text
AUTOMATION FAILURE
ENVIRONMENT FAILURE
TEST SETUP FAILURE
REAL PRODUCT DEFECT
```

Only:

```text
REAL PRODUCT DEFECT
```

permits source modification.

---

# 89. If a real product defect is found

Do not fix immediately by intuition.

Create a mini defect record first:

```text
ID
steps to reproduce
expected
actual
affected build SHA
data state
screenshots/logs
severity
```

Then identify the smallest code area responsible.

Make the smallest repair possible.

Add a regression test where practical.

Then rerun:

```text
targeted test
full Debug suite
full Release suite
publish
binary provenance
affected runtime scenario
all safety-critical recovery scenarios if relevant
```

---

# 90. Versioning rule after a real source fix

If source changes after:

```text
0eef3eb...
```

do **not** move:

```text
v0.1.0
```

Instead:

```text
Version:
0.1.1

tag:
v0.1.1
```

Then rebuild and publish that new version.

Do not reuse the old binary asset filename under the old tag.

---

# 91. No defect found path

If all automatable runtime gates PASS and only visual checks remain:

```text
DO NOT edit anything
DO NOT retag
DO NOT rebuild unnecessarily
```

Final verdict:

```text
AUTOMATED/MECHANICAL VALIDATION PASS
FINAL VISUAL QA PENDING
```

---

# 92. Full completion rule

Only use:

```text
FULL RELEASE VALIDATION PASS
```

if:

```text
all mechanical/runtime gates = PASS
all visual/DPI gates = PASS
```

If visual remains:

```text
not full release validation
```

even if every automated gate passes.

---

# 93. Weak-model anti-regression checklist

Before any edit:

```text
[ ] Did an actual published-runtime test fail?
[ ] Can I reproduce the failure?
[ ] Is it definitely not an automation/setup issue?
[ ] Is source modification actually necessary?
```

If any answer is:

```text
No
```

do not edit source.

---

# 94. Weak-model forbidden shortcuts

Never do any of these:

```text
- call service tests GUI E2E
- call source inspection keyboard runtime QA
- call zero sockets offline mode
- call same-process reload process restart
- infer unavailable UI states from ViewModel/source only
- infer recovery UI behavior from service exceptions
- infer DPI correctness from XAML dimensions
- infer release provenance from local ZIP only
- omit failed or blocked gates from final report
- move v0.1.0 again
- change code just to make testing easier
```

---

# 95. Exact final summary template

Use this exact structure:

```text
FINAL TARGET
Commit:
Tag:
Binary ProductVersion:
EXE SHA-256:
ZIP SHA-256:

SOURCE
Static product source: PASS

BUILD / TEST
Debug build: PASS
Debug tests: PASS (.../... discovered)
Release build: PASS
Release tests: PASS (.../... discovered)

PROVENANCE
Tag == release source: PASS
Binary source revision == tag: PASS
Public ZIP hash verified: PASS / BLOCKED_ENVIRONMENT

PUBLISHED RUNTIME
Single instance: PASS
Category GUI CRUD: PASS / ...
Prompt GUI CRUD: PASS / ...
Move: PASS / ...
Duplicate: PASS / ...
Clipboard exact equality: PASS / ...
Restart persistence: PASS / ...
Unavailable prompt UI: PASS / ...
Orphan preservation: PASS / ...
Corrupt-primary recovery: PASS / ...
Double corruption: PASS / ...
Future schema: PASS / ...
Keyboard runtime: PASS / ...
Offline runtime: PASS / BLOCKED_ENVIRONMENT

VISUAL
900×600 @100%: PASS / HUMAN_REQUIRED
900×600 @125%: PASS / HUMAN_REQUIRED
900×600 @150%: PASS / HUMAN_REQUIRED
Focus visibility: PASS / HUMAN_REQUIRED

FINAL VERDICT:
...
```

---

# 96. Copy-ready executor prompt

```text
ROLE

You are the final published-runtime verification executor for Prompt Helper.

You are a weak model. Do not design. Do not refactor. Follow the instructions literally.

TARGET

Repository:
Ceegore/AI_prompt_helper

Release commit:
0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd

Release tag:
v0.1.0

AUTHORITIES

1. Prompt Helper – Implementation Plan v1.2.0 FINAL AUDITED.md
2. _plh9.md
3. _plh8_with_weak_implementer_instructions.md

GOAL

Complete the remaining published executable runtime verification.

IMPORTANT

The product source is currently considered clean.

Do not edit production code unless a real published PromptHelper.exe runtime failure is reproduced.

Do not move v0.1.0.

Do not replace GUI E2E with service calls.

Do not replace keyboard runtime tests with XAML inspection.

Do not replace offline runtime with a source scan or socket observation.

Do not mark unexecuted tests PASS.

STEP 1 — BASELINE

Run:

git status --short
git rev-parse HEAD
git branch --show-current
git describe --tags --exact-match

Require exact HEAD:
0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd

Require:
v0.1.0

Require:
clean tree

STEP 2 — VERIFY BINARY

Use the published PromptHelper.exe intended for v0.1.0.

Record:

FileVersion
ProductVersion
SHA-256

Require ProductVersion source revision:
0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd

If mismatch:
FAIL
Do not continue.

STEP 3 — ISOLATED DATA

Use Windows Sandbox, disposable VM, disposable Windows account, or a safe isolated Prompt Helper data directory.

Never destroy valuable real user data.

STEP 4 — CATEGORY GUI E2E

Launch actual PromptHelper.exe.

Through the actual UI test:

- create top-level category
- create nested category
- case-insensitive duplicate sibling rejection
- rename
- exact non-empty deletion rejection
- empty category confirmation and deletion

Do not call PromptLibraryService.

STEP 5 — PROMPT GUI E2E

Through actual UI:

- create prompt
- edit prompt
- empty prompt
- 50k prompt
- delete prompt

Use Unicode and Markdown content.

STEP 6 — MOVE

Through actual Move dialog:

- current category preselected
- choose destination
- move
- source disappears
- destination shows same prompt

STEP 7 — DUPLICATE

Through actual Move dialog:

- enable Copy instead of move
- choose destination
- execute
- source remains
- duplicate appears
- contents match

STEP 8 — CLIPBOARD

Click actual Copy button.

Read Windows clipboard from an STA PowerShell process.

Compare exact expected and actual strings.

Also verify:
Copy → Copied ✓ → Copy

STEP 9 — PROCESS RESTART PERSISTENCE

Close app normally.

Verify process ended.

Launch a brand-new PromptHelper.exe process.

Verify all prior mutations persist through actual UI.

STEP 10 — UNAVAILABLE PROMPT

Create prompt through UI.

Close app.

Delete exactly its .md file.

Restart.

Verify UI:

- unavailable state visible
- Delete enabled
- Move enabled
- Edit disabled
- Copy disabled
- Duplicate disabled

Then actually move it via UI.

STEP 11 — ORPHAN

Close app.

Create arbitrary GUID-named orphan .md.

Restart.

Verify:

- normal startup
- orphan hidden
- orphan preserved on disk

STEP 12 — CORRUPT PRIMARY

Use isolated state.

Keep valid backup.

Corrupt primary.

Launch actual EXE.

Verify:

- recovery warning
- app opens
- primary restored
- prompt files preserved

STEP 13 — DOUBLE CORRUPTION

Use fresh isolated state.

Hash prompt files.

Corrupt primary and backup.

Launch actual EXE.

Verify:

- fatal startup
- no normal MainWindow
- no default reset
- prompt hashes unchanged

STEP 14 — FUTURE SCHEMA

Use primary schemaVersion 999 with valid older backup.

Hash primary.

Launch actual EXE.

Verify:

- fatal future-schema handling
- backup not restored
- primary unchanged
- defaults not created

STEP 15 — KEYBOARD

Actual runtime keys:

Name:
Enter submit
Escape cancel

ConfirmDelete:
Enter delete
Escape cancel

PromptEditor:
Enter newline
Escape cancel
Enter does NOT save

Move:
Enter action
Escape cancel

Main:
Tab / Shift+Tab navigation

STEP 16 — OFFLINE

Run representative workflow while network is genuinely disabled.

If impossible:
OFFLINE RUNTIME = BLOCKED_ENVIRONMENT

Do not mark PASS from zero observed sockets alone.

STEP 17 — VISUAL

Check:

900×600 @100%
900×600 @125%
900×600 @150%
focus visibility
clipping
overlap
dialog fit

If you cannot visually inspect:
HUMAN_REQUIRED

STEP 18 — FAILURE POLICY

If a step fails:

1. reproduce
2. rule out automation/setup
3. classify
4. only if real product defect, make smallest source fix
5. if source changes, do NOT move v0.1.0
6. create v0.1.1 after complete retest

STEP 19 — FINAL STATUS

Every gate must be:

PASS
FAIL
BLOCKED_ENVIRONMENT
HUMAN_REQUIRED
NOT_APPLICABLE

If all automatable gates pass and only visual remains:

AUTOMATED/MECHANICAL VALIDATION PASS
FINAL VISUAL QA PENDING

Only say FULL RELEASE VALIDATION PASS when visual/DPI checks also pass.

Do not stop early.
Do not omit required rows.
Do not reinterpret service tests as GUI E2E.
```

---

# 97. Final recommended path

The weak model should now do exactly this:

```text
freeze source
freeze v0.1.0
verify exact binary
run real published GUI E2E
run real published recovery tests
run real keyboard test
run real network-disabled offline test
classify visuals honestly
```

If all mechanical gates pass:

```text
AUTOMATED/MECHANICAL VALIDATION PASS
FINAL VISUAL QA PENDING
```

No further code change should occur unless a real executable defect is reproduced.
