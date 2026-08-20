# Prompt Helper — Final Verification Concept for a Weaker Model

**Repository:** `Ceegore/AI_prompt_helper`  
**Baseline audited commit:** `fb69b54973dbec7630f2cf47164bd9451fb0be19`  
**Product:** Prompt Helper  
**Target:** Windows 11 x64, WPF, .NET 10  
**Authoritative implementation plan:** `Prompt Helper – Implementation Plan v1.2.0 FINAL AUDITED.md`  
**Purpose:** Execute everything that remains unverified after the fifth static audit, using a comparatively weak implementation/testing model without allowing it to invent requirements, weaken tests, or falsely claim validation.

---

# 1. Current state and objective

The repository has completed repeated static/adversarial source audits.

The latest static audit found:

```text
Critical: 0
High:     0
Medium:   0
Low:      0
```

What remains unverified is primarily **execution evidence**:

```text
actual .NET 10 restore
actual Debug build
actual full MSTest execution
actual Release build
actual Release MSTest execution
actual self-contained win-x64 publish
actual published executable startup
actual Windows process-lock behaviour
actual Windows clipboard behaviour
actual WPF keyboard/dialog behaviour
actual DPI/layout rendering
actual published persistence/recovery smoke
actual offline runtime behaviour
actual license/notices in publish output
```

The goal of this document is to let a weaker model verify as much of that as possible **mechanically and deterministically**.

The weaker model must not perform a new design pass.

It must:

```text
TEST
→ record evidence
→ if a real defect appears, reproduce it
→ add/repair a regression test when practical
→ make the smallest correct fix
→ rerun the failed scope
→ rerun the complete required suite
→ repeat until no executable defect remains
```

---

# 2. Core rule: never confuse "not tested" with "passed"

Every test item must end in exactly one status:

```text
PASS
FAIL
BLOCKED_ENVIRONMENT
HUMAN_REQUIRED
NOT_APPLICABLE
```

Meanings:

## PASS

The required action was actually executed and objective evidence proves the expected result.

## FAIL

The action was executed and a requirement was violated.

## BLOCKED_ENVIRONMENT

The model cannot execute the test because a required machine capability is missing.

Examples:

```text
.NET 10 SDK missing
not running on Windows
Windows Sandbox unavailable for isolated destructive GUI test
no GUI session
insufficient permission for firewall rule
```

A blocked item is **not a product defect** unless the product itself causes the block.

## HUMAN_REQUIRED

The test depends on visual or subjective judgement that the current model cannot reliably perform.

Examples for a shell-only model:

```text
does the UI look visually polished?
is text visibly clipped at 150% DPI?
is focus outline visually obvious?
does the edit glyph render attractively?
```

## NOT_APPLICABLE

Only use when the authoritative plan explicitly makes a test irrelevant to the current configuration.

Do not use `SKIP`, `probably passes`, `looks fine from source`, or similar wording.

---

# 3. Model capability tiers

Before testing, classify the executor into one of these tiers.

## Tier A — shell/files only

The model can:

```text
run PowerShell
run git
run dotnet
read/write files
inspect process state
inspect logs
```

This tier can perform the majority of release verification.

It **cannot** claim visual GUI PASS.

## Tier B — shell + Windows UI Automation

The model can additionally run a UIAutomation-based script against WPF windows.

It can mechanically:

```text
find windows
find buttons by accessible name
invoke controls
set text
select combo-box entries
check enabled/disabled states
read accessible text
close dialogs
```

It still cannot reliably judge visual aesthetics or clipping unless screenshot inspection is available.

## Tier C — shell + GUI/computer use + screenshot reasoning

The model can additionally:

```text
see the desktop
interact with WPF visually
inspect screenshots
judge clipping/overlap
inspect 100/125/150% scaling
```

Only Tier C may mark the visual layout/DPI items PASS without a human.

---

# 4. Safety rules

These rules are mandatory.

## 4.1 Never test destructive published-app recovery against valuable real user data

The application stores real data under:

```text
%LOCALAPPDATA%\PromptHelper
```

For any GUI/published-EXE test that modifies that location, use one of these in order of preference:

```text
1. disposable Windows Sandbox
2. disposable Windows VM
3. dedicated disposable Windows user account
```

Do not destructively test corruption, missing metadata, unknown `.md`, or initialization recovery against the user's actual working Prompt Helper data.

If no isolated Windows environment exists:

```text
published destructive GUI recovery tests
→ BLOCKED_ENVIRONMENT
```

The corresponding repository/integration tests can still be executed because they use temporary directories.

## 4.2 Do not run `git reset --hard`, `git clean -fdx`, or delete uncommitted work

Capture repository state first.

If working tree is dirty:

```text
record changed files
do not overwrite them
do not delete them
```

Tests may proceed only if the dirty changes are understood and do not invalidate the baseline.

## 4.3 Do not weaken tests to make them pass

Forbidden:

```text
deleting a failing test
commenting out assertions
catching exceptions only to hide failure
changing expected output to current wrong output
reducing test scope
adding arbitrary delays until a race "goes away"
```

## 4.4 Do not add features

Fix only demonstrated defects.

## 4.5 Do not change persistence ordering casually

The current persistence ordering is safety-critical.

Any fix involving:

```text
library.json
library.backup.json
prompt .md lifecycle
first-run marker
delete cleanup
future-schema logic
```

requires a regression test and a full persistence/recovery rerun.

---

# 5. Evidence directory

Create:

```text
artifacts/
└── final-validation/
    └── <YYYYMMDD-HHmmss>/
```

Example:

```text
artifacts/final-validation/20260820-123500/
```

Inside create:

```text
00-baseline/
01-environment/
02-restore/
03-debug-build/
04-debug-tests/
05-targeted-tests/
06-release-build/
07-release-tests/
08-publish/
09-publish-inspection/
10-process-smoke/
11-ui-automation/
12-recovery-gui/
13-offline/
14-manual-visual/
15-final/
```

All command output must be preserved.

Use:

```powershell
... *>&1 | Tee-Object -FilePath <log>
```

where practical.

---

# 6. Phase 0 — baseline freeze

## Purpose

Prove what was actually tested.

## Commands

From repository root:

```powershell
git status --short
git rev-parse HEAD
git branch --show-current
git log -1 --oneline
git diff --stat
git diff --cached --stat
```

Save outputs.

## Required baseline

Expected audited commit:

```text
fb69b54973dbec7630f2cf47164bd9451fb0be19
```

If HEAD differs:

```text
do not assume the fifth audit still applies
record the new HEAD
inspect the changes before continuing
```

This does not automatically fail product validation, but the final report must say a different commit was tested.

## PASS criteria

```text
HEAD recorded
branch recorded
working-tree state recorded
no hidden assumption about commit tested
```

---

# 7. Phase 1 — environment verification

## Commands

```powershell
$PSVersionTable
[System.Environment]::OSVersion
Get-ComputerInfo | Select-Object WindowsProductName, WindowsVersion, OsBuildNumber, OsArchitecture
dotnet --info
dotnet --version
where.exe dotnet
```

Also record:

```powershell
Get-Culture
Get-UICulture
```

## Required

```text
Windows
x64
stable .NET 10 SDK
not preview
```

The plan does not require one exact servicing patch.

## Check `global.json`

```powershell
Get-Content .\global.json -Raw
```

Confirm resolution is compatible with:

```text
version: 10.0.100
rollForward: latestFeature
allowPrerelease: false
```

## PASS criteria

```text
Windows machine identified
x64 identified
stable .NET 10 SDK selected
dotnet executable path recorded
```

If no suitable SDK:

```text
BLOCKED_ENVIRONMENT
```

Do not modify `global.json` merely to make a different SDK work.

---

# 8. Phase 2 — package/dependency sanity

The shipping application is supposed to have no extra NuGet package dependency.

## Commands

```powershell
dotnet list .\src\PromptHelper\PromptHelper.csproj package
dotnet list .\tests\PromptHelper.Tests\PromptHelper.Tests.csproj package
```

Also inspect project files:

```powershell
Get-Content .\src\PromptHelper\PromptHelper.csproj -Raw
Get-Content .\tests\PromptHelper.Tests\PromptHelper.Tests.csproj -Raw
```

## Expected

Application:

```text
no PackageReference added
```

Tests:

```text
MSTest.Sdk 4.3.3 through project SDK
```

No product dependency expansion.

---

# 9. Phase 3 — clean restore

## Command

```powershell
dotnet restore .\PromptHelper.slnx --force-evaluate
```

Capture full output and `$LASTEXITCODE`.

## PASS criteria

```text
exit code 0
no unresolved package error
no SDK resolution failure
```

Warnings must be recorded.

Warnings that indicate unsupported runtime, package vulnerability, or target incompatibility must be investigated rather than ignored.

---

# 10. Phase 4 — Debug build

## Command

```powershell
dotnet build .\PromptHelper.slnx `
  -c Debug `
  --no-restore `
  /warnaserror
```

First try with `/warnaserror`.

If it fails **only because of a known external SDK/tooling warning that is not caused by project code**, record it and rerun without `/warnaserror`.

Never hide compiler warnings from project source.

## PASS criteria

```text
exit code 0
0 compiler errors
0 project-source warnings
WPF XAML compilation succeeds
```

Record output DLL/EXE paths.

---

# 11. Phase 5 — full Debug automated suite

## Primary command

```powershell
dotnet test .\PromptHelper.slnx `
  -c Debug `
  --no-build `
  --logger "trx;LogFileName=debug-tests.trx"
```

If solution-level TRX naming creates collisions, run the test project directly:

```powershell
dotnet test .\tests\PromptHelper.Tests\PromptHelper.Tests.csproj `
  -c Debug `
  --no-build `
  --logger "trx;LogFileName=debug-tests.trx"
```

## Capture

Record:

```text
total tests
passed
failed
skipped
duration
exit code
TRX path
```

## PASS criteria

```text
exit code 0
failed = 0
unexpected skipped = 0
```

A test framework discovery failure is a FAIL unless the environment is clearly incompatible.

---

# 12. Phase 6 — critical targeted regression run

Even if the full suite passes, rerun the high-value persistence/recovery cases separately.

This gives easy-to-read evidence that the safety tests truly executed.

Use `dotnet test --filter` with the real discovered test names.

At minimum run tests covering:

```text
Failed_write_does_not_modify_existing_target

PLH3001_Non_IOException_during_backup_is_warning_only_and_commits_primary

PLH3001_GUID_generation_retries_on_metadata_or_orphan_collision

PLH3001_GUID_generation_fails_after_ten_collisions

PLH4001_Destination_paths_unique_even_with_32_char_guid_exhaustion

PLH4002_Valid_primary_with_locked_unreadable_backup_loads_and_warns

PLH3004_CanDeleteCategory_returns_exact_locked_text

PLH004_Zero_byte_primary_recovers_from_valid_backup

PLH004_Whitespace_primary_recovers_from_valid_backup

Future_primary_never_falls_back_to_old_backup

Corrupt_primary_valid_backup_recovers

Corrupt_primary_corrupt_backup_fails

Interrupted_init_with_modified_default_file_stops

Unknown_prompt_files_without_marker_stop_initialization

Delete_backup_failure_keeps_file

Delete_file_failure_leaves_orphan

Create_primary_failure_no_metadata_commit

Duplicate_primary_failure_no_metadata_commit

Move_same_category_noop
```

If exact names differ, discover them from test source or TRX.

## Example

```powershell
dotnet test .\tests\PromptHelper.Tests\PromptHelper.Tests.csproj `
  -c Debug `
  --no-build `
  --filter "FullyQualifiedName~PLH4002_Valid_primary_with_locked_unreadable_backup_loads_and_warns"
```

## PASS criteria

Every targeted case is discovered and passes.

If a filter returns zero tests:

```text
FAIL
```

until the reason is understood.

Do not count "0 tests matched" as a pass.

---

# 13. Phase 7 — test completeness sanity

A weak model should not assume `dotnet test` discovered every intended file.

## Inspect test files

Enumerate:

```powershell
Get-ChildItem .\tests\PromptHelper.Tests -Filter *.cs -Recurse
```

Extract `[TestMethod]` occurrences:

```powershell
Select-String `
  -Path .\tests\PromptHelper.Tests\*.cs `
  -Pattern '\[TestMethod\]' |
  Measure-Object
```

Compare approximate source count to executed test count.

This is not expected to match perfectly if data-driven tests exist, but a large mismatch requires investigation.

## PASS criteria

No obvious test-discovery omission.

---

# 14. Phase 8 — Release build

## Command

```powershell
dotnet build .\PromptHelper.slnx `
  -c Release `
  --no-restore
```

Prefer an additional warning-strict run:

```powershell
dotnet build .\PromptHelper.slnx `
  -c Release `
  --no-restore `
  /warnaserror
```

## PASS criteria

```text
exit code 0
no compiler/XAML errors
no project-source warnings
```

---

# 15. Phase 9 — full Release automated suite

## Command

```powershell
dotnet test .\tests\PromptHelper.Tests\PromptHelper.Tests.csproj `
  -c Release `
  --no-build `
  --logger "trx;LogFileName=release-tests.trx"
```

## PASS criteria

```text
all discovered tests pass
0 failures
0 unexpected skips
exit code 0
```

Debug PASS does not replace Release PASS.

---

# 16. Phase 10 — self-contained win-x64 publish

Clean only the dedicated publish destination, not the repository.

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

## Forbidden publish options

Do not add:

```text
PublishSingleFile
PublishTrimmed
NativeAOT
custom packing
```

## PASS criteria

```text
exit code 0
PromptHelper.exe exists
publish folder is non-empty
self-contained runtime files present
```

---

# 17. Phase 11 — publish artifact inspection

## Required files

Check:

```powershell
Test-Path "$publish\PromptHelper.exe"
Test-Path "$publish\LICENSE"
Test-Path "$publish\THIRD_PARTY_NOTICES.md"
```

All must be true.

## Version

```powershell
$vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo("$publish\PromptHelper.exe")
$vi | Format-List *
```

Verify application version corresponds to:

```text
0.1.0
```

Also inspect assembly version if necessary with PowerShell reflection in a copy-safe process.

## Architecture

Inspect PE architecture using a trusted available tool.

Preferred if Visual Studio tools exist:

```powershell
dumpbin /headers "$publish\PromptHelper.exe"
```

Otherwise record that RID was:

```text
win-x64
```

and inspect runtimeconfig/deps output.

Do not install random binary tools merely for this check.

## Notices

Read:

```powershell
Get-Content "$publish\THIRD_PARTY_NOTICES.md" -Raw
```

Confirm non-empty.

Record any Microsoft/runtime notice files produced by publishing.

Do not delete them.

---

# 18. Phase 12 — publish folder hygiene

Enumerate:

```powershell
Get-ChildItem $publish -Recurse |
  Select-Object FullName, Length
```

Search for unwanted project/test output:

```text
PromptHelper.Tests
TestResults
source .cs files
debug-only artifacts
```

PDB files are not automatically a defect; record them.

Ensure no test project was accidentally packaged as the application.

---

# 19. Phase 13 — static runtime privacy check

This does not replace the actual offline smoke but is easy for a weak model.

Search shipping source for obvious intentional networking/process execution:

```powershell
$patterns = @(
  'HttpClient',
  'WebRequest',
  'WebClient',
  'Socket',
  'TcpClient',
  'UdpClient',
  'Process\.Start',
  'powershell',
  'cmd\.exe',
  'WebView',
  'Telemetry',
  'Analytics'
)

foreach ($p in $patterns) {
    Select-String `
      -Path .\src\PromptHelper\**\*.cs `
      -Pattern $p
}
```

Investigate any hit.

Expected:

```text
no intentional runtime networking
no prompt execution
no shell/process execution
```

---

# 20. Phase 14 — basic published process smoke

This is non-destructive.

## Important

Do not run if the real `%LOCALAPPDATA%\PromptHelper` contains valuable data unless the launch is known to be safe.

A normal launch with valid existing data is non-destructive, but isolated testing is preferred.

## Procedure

Launch:

```powershell
$p = Start-Process `
  -FilePath "$publish\PromptHelper.exe" `
  -PassThru
```

Wait a few seconds:

```powershell
Start-Sleep -Seconds 3
$p.Refresh()
```

Check:

```powershell
$p.HasExited
$p.MainWindowHandle
$p.MainWindowTitle
```

Expected:

```text
process still running
non-zero MainWindowHandle
Prompt Helper window title
```

Then close normally if possible:

```powershell
$p.CloseMainWindow()
$p.WaitForExit(5000)
```

If it does not close, do not immediately force kill.

Record behavior first.

## PASS criteria

```text
published EXE launches
main WPF window created
normal close succeeds
```

---

# 21. Phase 15 — two-process single-instance smoke

Prefer isolated environment.

## Procedure

Start first instance:

```powershell
$p1 = Start-Process "$publish\PromptHelper.exe" -PassThru
Start-Sleep -Seconds 2
```

Start second:

```powershell
$p2 = Start-Process "$publish\PromptHelper.exe" -PassThru
Start-Sleep -Seconds 2
$p2.Refresh()
```

Expected behavior:

```text
first remains open
second reports that Prompt Helper is already running
second exits
```

A shell-only model may not be able to read the MessageBox text.

Tier B/C should inspect it through UI Automation.

At minimum Tier A can verify:

```text
second process exits
first remains alive
```

Then close first normally.

## Result status

Tier A:

```text
PASS_PARTIAL is forbidden.
```

Instead split into two checks:

```text
single-instance process enforcement → PASS/FAIL
visible explanatory dialog → HUMAN_REQUIRED or Tier-B PASS
```

---

# 22. Phase 16 — UI Automation feasibility check

Tier B only.

Before writing a large automation flow, probe WPF accessibility.

PowerShell can load:

```powershell
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
```

Get desktop:

```powershell
$root = [System.Windows.Automation.AutomationElement]::RootElement
```

Find Prompt Helper window by process ID or name.

Enumerate controls and record:

```text
ControlType
Name
AutomationId
IsEnabled
```

## PASS criteria

Enough stable accessible metadata exists to drive:

```text
+ Add
+ Prompt
Rename category
Delete category
Copy
Edit
Move
Help
dialog buttons
editor text box
destination combo
```

If controls cannot be reliably identified:

```text
GUI automation track → BLOCKED_ENVIRONMENT/TESTABILITY
```

Do not rewrite the UI merely because the automation driver is weak unless the user explicitly authorizes testability metadata changes.

---

# 23. Phase 17 — deterministic GUI flow in isolated environment

Tier B/C only.

Run this in:

```text
Windows Sandbox
disposable VM
or dedicated disposable user
```

Use unique test data such as:

```text
QA_Category_A
QA_Category_B
QA nested
```

and a unique prompt:

```text
# QA Prompt

Unicode:
ä ö ü ß 日本語 한국어 中文 Русский 🚀 ✅ ❌

```json
{
  "qa": true
}
```
```

## Required flow

### 17.1 First start

Verify:

```text
main window opens
default categories visible
default prompts appear in correct categories
```

### 17.2 Category create

At Home:

```text
click + Add
enter QA_Category_A
submit
```

Verify card appears.

### 17.3 Nested category create

Open `QA_Category_A`.

Create:

```text
QA nested
```

Verify breadcrumb and child card.

### 17.4 Duplicate sibling rejection

Attempt:

```text
QA nested
qa NESTED
```

in same parent.

Expected:

```text
inline rejection
dialog remains open
```

Cancel.

### 17.5 Rename

Rename `QA nested` to:

```text
QA nested renamed
```

Verify:

```text
card updates
category ID behavior is invisible but persistence later proves structure
```

### 17.6 Non-empty delete rejection

Create prompt or child content inside category, then attempt parent delete.

Expected exact message:

```text
This category is not empty.

Move or delete its prompts and subcategories first.
```

### 17.7 Empty category delete

Create temporary empty category:

```text
QA delete me
```

Delete.

Expected custom confirmation:

```text
Cancel
Delete
This action cannot be undone.
```

Confirm deletion.

Verify card disappears.

---

# 24. Phase 18 — prompt GUI flow

Tier B/C in isolated environment.

## Create

Create the QA prompt above.

Verify:

```text
prompt card appears
preview title derived from first non-empty line
```

## Edit

Open Edit.

Append:

```text
EDITED
```

Save.

Verify display updates.

## Empty prompt

Create a second prompt with empty content.

Expected preview:

```text
(Empty prompt)
```

## Large prompt

Create a generated prompt >= 50,000 characters.

The model can generate it in PowerShell and paste/set it through UI automation if reliable.

Verify:

```text
editor accepts content
save completes
card renders
application remains responsive enough to continue
```

Do not claim subjective performance measurements unless measured.

---

# 25. Phase 19 — clipboard end-to-end

Tier B/C preferred.

Use the QA prompt containing:

```text
Markdown
Unicode
blank lines
code fence
```

Click:

```text
Copy
```

Mechanically read Windows clipboard from the same STA-capable UI test helper or via a small PowerShell STA process.

Example:

```powershell
powershell.exe -STA -NoProfile -Command `
  "Add-Type -AssemblyName PresentationCore; [Windows.Clipboard]::GetText()"
```

Compare exact textual content to expected.

Verify button temporarily becomes:

```text
Copied ✓
```

and returns to:

```text
Copy
```

after roughly one second.

## PASS criteria

```text
clipboard text equals prompt text
Unicode preserved
Markdown preserved
no truncation
copy feedback appears and resets
```

---

# 26. Phase 20 — move GUI flow

With:

```text
source category
destination category
QA prompt
```

open Move.

Verify:

```text
current category preselected
Home exists
destination list is readable
Move is default action
Escape cancels
```

Move to destination.

Expected:

```text
source card disappears
same prompt appears at destination
```

No file content change.

---

# 27. Phase 21 — duplicate GUI flow

Use Move dialog:

```text
check Copy instead of move
action becomes Copy
select destination
confirm
```

Expected:

```text
source remains
duplicate appears in destination
duplicate content equals source
```

The automated service tests already prove a new ID; GUI smoke proves wiring.

---

# 28. Phase 22 — restart persistence

After GUI mutations:

1. close application normally;
2. start published EXE again.

Verify:

```text
created categories remain
renames remain
deleted category remains absent
edited prompt remains edited
move remains moved
duplicate remains
empty prompt remains
```

This is the published executable persistence smoke.

---

# 29. Phase 23 — unavailable prompt GUI behavior

Tier B/C, isolated environment only.

Procedure:

1. create a prompt;
2. close app;
3. identify its `.md` by comparing metadata and prompt directory;
4. move the `.md` outside `prompts`;
5. restart.

Expected card:

```text
(Unavailable prompt)
[Prompt file could not be loaded.]
```

Expected actions:

```text
Delete  enabled
Move    enabled
Edit    disabled
Copy    disabled
Duplicate disabled
```

Move dialog must say:

```text
Unavailable prompts can be moved but cannot be duplicated.
```

Do not delete the moved test file until evidence is captured.

---

# 30. Phase 24 — orphan GUI behavior

Isolated environment.

1. close app;
2. add an arbitrary GUID-named `.md` to `prompts`;
3. keep valid primary metadata;
4. restart.

Expected:

```text
normal library opens
orphan is not displayed
orphan remains on disk
```

---

# 31. Phase 25 — corrupt-primary published recovery

Isolated environment.

Precondition:

```text
valid library.json
matching library.backup.json
```

1. close app;
2. copy both metadata files to test evidence;
3. corrupt only `library.json`;
4. start app.

Expected:

```text
application starts
backup is restored
recovery notice appears
new primary becomes valid
prompt files remain
recovery copy is attempted under recovery\
```

Verify the recovery directory mechanically.

---

# 32. Phase 26 — double corruption

Isolated environment.

Corrupt:

```text
library.json
library.backup.json
```

Start.

Expected:

```text
fatal startup
no normal MainWindow
no default reinitialization
existing prompt files untouched
```

Capture hashes of prompt files before and after.

---

# 33. Phase 27 — future-schema safety

Isolated environment.

Set primary to:

```json
{
  "schemaVersion": 999
}
```

Keep an old valid schema-1 backup.

Start.

Expected:

```text
fatal unsupported schema
old backup is NOT restored
future primary remains untouched
no default initialization
```

Hash primary before and after.

This is a high-value destructive safety test.

---

# 34. Phase 28 — interrupted initialization smoke

This is already strongly covered by repository tests.

Published GUI validation is optional but possible in isolation.

Construct:

```text
metadata missing
initializing.marker present
one exact default prompt file present
```

Start.

Expected:

```text
missing default file created
metadata initialized
no duplicate defaults
marker removed best-effort
```

Then repeat with modified default content.

Expected:

```text
fatal safety stop
modified file preserved
```

---

# 35. Phase 29 — locked backup with valid primary

This is already covered by the new Windows regression test and does not need a dangerous GUI reproduction to accept the product.

Optional isolated published smoke:

```text
valid primary
backup held FileShare.None by helper process
launch application
```

Expected:

```text
MainWindow still opens
warning shown
primary data loaded
```

After releasing lock and restarting:

```text
backup synchronizes normally
```

---

# 36. Phase 30 — keyboard behavior

Tier B can automate keystrokes but cannot always judge visible focus.

Mechanically verify:

## Name dialog

```text
Enter → submit valid value
Escape → cancel
```

## Prompt editor

```text
Enter → inserts newline
Tab → inserts editor tab/input
Escape → cancel
```

Critically:

```text
Enter must NOT save the prompt editor
```

## Move dialog

```text
Enter → Move/Copy
Escape → cancel
```

## Main window

Test:

```text
Tab
Shift+Tab
```

ensures focus reaches interactive controls.

Visual focus-outline quality:

```text
Tier A/B → HUMAN_REQUIRED
Tier C → inspect visually
```

---

# 37. Phase 31 — window size and DPI

This is the major human/visual remainder for a shell-only executor.

Required combinations:

```text
100%
125%
150%
```

at:

```text
900×600
1100×760
large desktop resolution
```

Check:

```text
no clipped headers
no inaccessible buttons
category cards usable
prompt list usable
breadcrumb usable
dialogs fit
action controls visible
text readable
no overlapping controls
```

## Tier A/B

Status:

```text
HUMAN_REQUIRED
```

unless reliable screenshot analysis is available.

## Tier C

Capture screenshots for every scale/size combination and inspect them.

Store screenshots under:

```text
14-manual-visual/
```

---

# 38. Phase 32 — offline runtime check

Best isolated method:

```text
Windows Sandbox or VM with network disabled
```

Then run:

```text
startup
category create/rename/delete
prompt create/edit/delete
move
duplicate
clipboard
restart
```

Expected:

```text
all local functionality works
```

If network cannot be disabled, perform a weaker process check:

```powershell
Get-NetTCPConnection -OwningProcess <PID>
```

and:

```powershell
Get-NetUDPEndpoint -OwningProcess <PID>
```

Expected:

```text
no intentional runtime sockets
```

The source scan and no-package architecture support this result, but only actual network-disabled operation fully proves QA-037.

---

# 39. Phase 33 — data integrity hash checks for destructive scenarios

For every destructive recovery test, capture file hashes before and after:

```powershell
Get-FileHash <path> -Algorithm SHA256
```

Use for:

```text
future-schema primary
prompt files during double corruption
unknown initialization data
orphan file
modified interrupted-init default file
```

This prevents a weak model from claiming "preserved" without evidence.

---

# 40. Phase 34 — warning/error text evidence

When a test depends on exact or meaningful text, capture it rather than paraphrasing.

Important cases:

```text
non-empty category rejection

backup synchronization warning

recovery warning

unsupported future schema

unknown initialization data

second instance warning

unavailable prompt duplicate notice
```

Tier B/C should read text through UI Automation.

Tier A should rely on direct service tests where possible and mark GUI wording as HUMAN_REQUIRED/BLOCKED if it cannot inspect dialogs.

---

# 41. Phase 35 — failure handling policy

When any test fails:

## Step 1 — classify

Determine whether failure is:

```text
product defect
test defect
environment defect
tooling defect
```

Do not edit source until classification is supported by evidence.

## Step 2 — minimize

Create the smallest deterministic reproduction.

## Step 3 — inspect authoritative requirements

Use the implementation plan to determine expected behavior.

Do not invent a new requirement.

## Step 4 — add a regression test where practical

Required for:

```text
persistence
recovery
data integrity
sorting
ID generation
service behavior
repeatable UI wiring defect if unit/integration-testable
```

## Step 5 — fix minimally

No feature expansion.

## Step 6 — rerun narrow test

The new/failing test must pass.

## Step 7 — rerun complete Debug suite

```text
all pass
```

## Step 8 — rerun complete Release suite

```text
all pass
```

## Step 9 — republish if production code changed

Then repeat publish inspection and affected smoke tests.

## Step 10 — continue remaining validation

Do not stop at the first fixed bug.

---

# 42. Phase 36 — no-false-green rules

A weaker model must never declare success because:

```text
the code looks correct
the test was not run
a command was interrupted
a filter matched zero tests
the application launched but no workflow was exercised
the UI could not be inspected
the environment lacked the required capability
```

A command timeout is:

```text
not PASS
```

Investigate whether it is:

```text
FAIL
or
BLOCKED_ENVIRONMENT
```

---

# 43. Phase 37 — complete final QA mapping

Use this matrix.

| QA | Requirement | Weak-model method | Result type |
|---|---|---|---|
| QA-001 | clean restore | `dotnet restore` | deterministic |
| QA-002 | Debug build | `dotnet build -c Debug` | deterministic |
| QA-003 | automated tests | full Debug MSTest | deterministic |
| QA-004 | first start defaults | startup tests + isolated GUI | mostly automatable |
| QA-005 | second start no duplicates | startup test + restart GUI | automatable |
| QA-006 | category add | service tests + GUI automation | automatable |
| QA-007 | category rename | service tests + GUI automation | automatable |
| QA-008 | duplicate sibling rejection | validator/service + GUI | automatable |
| QA-009 | non-empty delete rejection | regression/service + GUI | automatable |
| QA-010 | empty delete confirmation | UI automation | Tier B/C |
| QA-011 | deep hierarchy | automated service test + GUI | automatable |
| QA-012 | Home prompt | service + GUI | automatable |
| QA-013 | prompt create | service + GUI | automatable |
| QA-014 | prompt edit | service + GUI | automatable |
| QA-015 | prompt delete | service + GUI | automatable |
| QA-016 | prompt move | service + GUI | automatable |
| QA-017 | duplicate | service + GUI | automatable |
| QA-018 | clipboard | Windows STA/UI automation | Tier B/C |
| QA-019 | Unicode | automated + clipboard smoke | automatable |
| QA-020 | Markdown | automated + clipboard smoke | automatable |
| QA-021 | empty prompt | automated + GUI | automatable |
| QA-022 | 50k prompt | automated + GUI | mostly automatable |
| QA-023 | missing prompt file | automated + isolated GUI | automatable |
| QA-024 | orphan preservation | automated + isolated GUI | automatable |
| QA-025 | corrupt primary recovery | automated + isolated GUI | automatable |
| QA-026 | double corruption | automated + isolated GUI | automatable |
| QA-027 | future schema safety | automated + isolated GUI | automatable |
| QA-028 | interrupted initialization | automated; optional GUI | automatable |
| QA-029 | unknown-data protection | automated; optional GUI | automatable |
| QA-030 | backup-write failure | failure-injection tests | deterministic |
| QA-031 | delete-file failure | failure-injection tests | deterministic |
| QA-032 | second instance rejected | unit + process/UI smoke | automatable |
| QA-033 | 900×600 | screenshot/manual | Tier C/human |
| QA-034 | 125% scaling | screenshot/manual | Tier C/human |
| QA-035 | 150% scaling | screenshot/manual | Tier C/human |
| QA-036 | keyboard navigation | UI automation + visual focus | partial model/human |
| QA-037 | offline functionality | isolated network-off smoke | automatable if env supports |
| QA-038 | Release build | CLI | deterministic |
| QA-039 | Release tests | CLI | deterministic |
| QA-040 | self-contained publish | CLI | deterministic |
| QA-041 | publish smoke | process + UI automation | automatable |
| QA-042 | license/notices retained | filesystem inspection | deterministic |

---

# 44. Phase 38 — acceptance levels

Use three separate acceptance statements.

## A. Automated repository acceptance

May be PASS when:

```text
restore PASS
Debug build PASS
Debug tests PASS
critical targeted tests PASS
Release build PASS
Release tests PASS
```

## B. Publish acceptance

May be PASS when:

```text
publish PASS
EXE exists
0.1.0 confirmed
LICENSE exists
THIRD_PARTY_NOTICES exists
published EXE launches
normal close works
second-instance enforcement works
```

## C. Full product release acceptance

May be PASS only when:

```text
A PASS
B PASS
required GUI flows PASS
clipboard PASS
offline PASS
900×600 PASS
125% PASS
150% PASS
keyboard PASS
published persistence smoke PASS
recovery safety smoke PASS
```

If visual tests remain HUMAN_REQUIRED:

```text
do not call C PASS
```

Say instead:

```text
AUTOMATED/MECHANICAL VALIDATION PASS
FINAL VISUAL QA PENDING
```

---

# 45. Phase 39 — final evidence report

Create:

```text
artifacts/final-validation/<timestamp>/15-final/FINAL_VALIDATION_REPORT.md
```

Required structure:

```text
# Final Validation Report

TESTED COMMIT:
<sha>

WINDOWS:
<edition/build>

DOTNET:
<sdk>

EXECUTOR CAPABILITY:
Tier A / B / C

SUMMARY:
PASS / FAIL / BLOCKED

COUNTS:
PASS:
FAIL:
BLOCKED_ENVIRONMENT:
HUMAN_REQUIRED:
NOT_APPLICABLE:

AUTOMATED REPOSITORY:
...

PUBLISH:
...

GUI:
...

RECOVERY:
...

OFFLINE:
...

VISUAL/DPI:
...

DEFECTS FOUND:
...

DEFECTS FIXED:
...

REMAINING DEFECTS:
...

REMAINING HUMAN CHECKS:
...

EVIDENCE DIRECTORY:
...
```

Every FAIL must reference:

```text
command/log
test name
reproduction
expected
actual
fix commit/diff if repaired
rerun evidence
```

---

# 46. Phase 40 — clean-run requirement after repairs

If any product defect is fixed during validation, the final state is accepted only after a complete clean rerun.

Minimum final clean rerun:

```text
dotnet restore
Debug build
Debug full tests
Release build
Release full tests
publish
publish inspection
affected published smoke
all previously failing targeted tests
```

Do not say:

```text
"the fix is obvious, so we can proceed"
```

The entire required chain must be clean.

---

# 47. Recommended weak-model workflow

For a weak model, use this exact working pattern:

```text
ONE PHASE AT A TIME

1. read the phase
2. execute commands exactly
3. save evidence
4. evaluate explicit PASS criteria
5. report one compact phase result
6. continue automatically if PASS
7. if FAIL:
   diagnose
   fix only if confirmed
   rerun
8. never ask the user to manually choose a technical design decision
9. never skip ahead after a failure
10. do not stop until all model-executable phases are complete
```

---

# 48. Copy-ready master prompt for the weaker model

Use the prompt below with the repository on the Windows test machine.

---

## MASTER PROMPT

```text
ROLE

You are the final verification and defect-repair executor for the Prompt Helper repository.

You are comparatively weak, so you must follow this procedure literally and avoid making design decisions that are not required.

REPOSITORY

Ceegore/AI_prompt_helper

EXPECTED BASELINE

fb69b54973dbec7630f2cf47164bd9451fb0be19

AUTHORITATIVE DOCUMENT

Prompt Helper – Implementation Plan v1.2.0 FINAL AUDITED.md

TEST CONCEPT

Use the repository copy of:
_plh_final_verification_concept.md

if present.

If the filename differs, use the supplied final verification concept verbatim.

OBJECTIVE

Execute every verification that can actually be performed in your current Windows environment.

Do not merely inspect source and claim it should work.

The repository has already completed a clean static audit. Your job is execution verification:

- environment
- restore
- Debug build
- Debug tests
- targeted persistence/recovery regression tests
- Release build
- Release tests
- self-contained win-x64 publish
- publish inspection
- published EXE smoke
- single-instance behavior
- GUI automation where your environment permits it
- clipboard where your environment permits it
- isolated recovery tests where your environment permits them
- offline test where your environment permits it
- final evidence report

STATUS VOCABULARY

Every test must be exactly one of:

PASS
FAIL
BLOCKED_ENVIRONMENT
HUMAN_REQUIRED
NOT_APPLICABLE

Never use "probably pass", "looks correct", "should pass", "not tested but fine", or equivalent wording.

SAFETY

Never use destructive published-app recovery tests against valuable real user data in %LOCALAPPDATA%\PromptHelper.

Use, in order of preference:

1. Windows Sandbox
2. disposable VM
3. disposable Windows account

If none exists, mark destructive published-GUI tests BLOCKED_ENVIRONMENT.

Repository unit/integration tests using temporary directories may still run.

Never:

- git reset --hard
- git clean -fdx
- delete unrelated work
- weaken a failing test
- remove assertions
- skip a failing required test
- change requirements to fit current behavior
- add product features
- alter persistence ordering without a regression test

BASELINE

First record:

git status --short
git rev-parse HEAD
git branch --show-current
git log -1 --oneline
git diff --stat
git diff --cached --stat

If HEAD differs from the expected baseline, record the tested SHA and inspect the delta before proceeding.

EVIDENCE

Create:

artifacts/final-validation/<timestamp>/

Store command logs, TRX files, publish listings, screenshots if available, hashes, and the final report there.

COMMAND EXECUTION

Run all commands for real.

Capture exit codes.

A timeout, interrupted command, zero-tests-matched filter, or missing log is not a PASS.

ENVIRONMENT GATE

Record:

PowerShell version
Windows edition/build/architecture
dotnet --info
dotnet --version
where dotnet

Require a stable .NET 10 SDK compatible with global.json.

If unavailable:

BLOCKED_ENVIRONMENT

Do not edit global.json to bypass the requirement.

RESTORE

Run:

dotnet restore .\PromptHelper.slnx --force-evaluate

Require exit code 0.

DEBUG BUILD

Run:

dotnet build .\PromptHelper.slnx -c Debug --no-restore

Also attempt a warning-strict build with /warnaserror.

Do not ignore project-source compiler warnings.

DEBUG TESTS

Run the entire test project and write a TRX file.

Require:

exit code 0
0 failed
0 unexpected skipped

Record total/passed/failed/skipped.

TARGETED TESTS

Separately execute the critical regressions, including at minimum tests for:

- failed atomic replacement preserving existing target
- non-IOException backup failure remaining warning-only
- prompt GUID metadata/orphan collisions
- ten GUID collision exhaustion
- full destination GUID suffix exhaustion with #2 fallback
- valid primary with locked/unreadable backup
- zero-byte primary recovery
- whitespace primary recovery
- future primary never falling back
- corrupt primary recovery
- double corruption
- interrupted initialization safety
- unknown prompt-file initialization safety
- delete backup failure retaining prompt file
- delete file cleanup failure retaining orphan
- create primary failure
- duplicate primary failure
- move same-category no-op
- exact non-empty category rejection text

A targeted filter that matches zero tests is FAIL until understood.

RELEASE

Run:

dotnet build .\PromptHelper.slnx -c Release --no-restore

Then:

dotnet test .\tests\PromptHelper.Tests\PromptHelper.Tests.csproj -c Release --no-build

Require all pass.

PUBLISH

Publish exactly:

dotnet publish .\src\PromptHelper\PromptHelper.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts\publish\win-x64

Do not add:

PublishSingleFile
trimming
NativeAOT

VERIFY PUBLISH

Require:

PromptHelper.exe
LICENSE
THIRD_PARTY_NOTICES.md

Verify application version 0.1.0.

List the complete publish folder.

Ensure test project output was not packaged as the application.

PUBLISHED PROCESS SMOKE

Launch the published PromptHelper.exe.

Verify:

- process remains alive long enough to create main window
- non-zero MainWindowHandle
- expected main window title
- normal close succeeds

SINGLE INSTANCE

Start one published instance.

Start a second.

Verify:

- first remains running
- second is rejected/exits

If you can inspect the warning through UI Automation, verify the visible warning as well.

GUI AUTOMATION

If Windows UI Automation or direct GUI control is available, execute the full isolated GUI flow from the test concept.

Do not claim GUI PASS merely because service tests pass.

If GUI control is unavailable:

GUI-specific tests = BLOCKED_ENVIRONMENT or HUMAN_REQUIRED as defined by the concept.

CLIPBOARD

If GUI automation is available, create/copy a prompt containing Markdown, Unicode, blank lines, and a code fence.

Read the Windows clipboard mechanically from an STA-capable helper.

Require exact textual equality.

RECOVERY GUI

Only in disposable/isolate data environment, execute:

- unavailable prompt
- orphan
- corrupt primary
- double corruption
- future schema
- optional interrupted initialization
- optional locked backup

Use SHA256 hashes to prove files expected to be preserved remain unchanged.

OFFLINE

Prefer an isolated VM/Sandbox with network disabled.

Run normal local workflows.

If network cannot be disabled, do not falsely mark full offline QA PASS.

VISUAL QA

If you cannot visually inspect screenshots, mark:

900x600
125% DPI
150% DPI
visible focus quality
visual clipping/overlap

as HUMAN_REQUIRED.

Do not infer these from XAML.

DEFECT LOOP

Whenever a test fails:

1. classify product vs test vs environment defect
2. create smallest reproduction
3. read authoritative requirement
4. add regression test when practical
5. make the smallest correct fix
6. run the narrow failing test
7. run the full Debug suite
8. run the full Release suite
9. republish if production changed
10. rerun affected published smoke
11. continue all remaining tests

Do not stop after the first repaired defect.

Do not ask the user to approve routine defect repair.

Only stop for a true authority contradiction, destructive safety risk, missing required external environment, or requirement that cannot be determined from the plan.

FINAL CLEAN RUN

If you changed any production code, finish with a full clean verification run:

restore
Debug build
Debug full tests
Release build
Release full tests
publish
publish inspection
affected GUI/recovery smoke

FINAL REPORT

Create:

artifacts/final-validation/<timestamp>/15-final/FINAL_VALIDATION_REPORT.md

Report:

TESTED COMMIT
WINDOWS
DOTNET
EXECUTOR TIER
PASS count
FAIL count
BLOCKED_ENVIRONMENT count
HUMAN_REQUIRED count
NOT_APPLICABLE count

AUTOMATED REPOSITORY RESULT
PUBLISH RESULT
GUI RESULT
RECOVERY RESULT
OFFLINE RESULT
VISUAL/DPI RESULT

DEFECTS FOUND
DEFECTS FIXED
REMAINING DEFECTS
REMAINING HUMAN CHECKS

For every failed/fixed item include:
- reproduction
- expected
- actual
- root cause
- changed files
- regression test
- rerun evidence

FINAL VERDICT RULE

Only say:

FULL RELEASE VALIDATION PASS

if every mandatory QA item was actually verified and passed.

If all model-executable checks pass but visual/manual items remain, say exactly:

AUTOMATED/MECHANICAL VALIDATION PASS
FINAL VISUAL QA PENDING

If anything executable fails and remains unresolved, say:

VALIDATION FAIL

If execution is impossible because the environment lacks required capability, say:

VALIDATION INCOMPLETE — ENVIRONMENT BLOCKED

Begin now and continue until every model-executable phase is complete.
```

---

# 49. Recommended artifact filename

Save this concept in the repository as:

```text
_plh_final_verification_concept.md
```

This keeps it visually separate from the historical bug reports:

```text
_plh1.md
_plh2.md
_plh3.md
_plh4.md
_plh5.md
```

Do not treat it as a new product authority.

It is a verification procedure subordinate to the implementation-locked plan.

---

# 50. Final recommendation

At the current project state, another speculative source audit has diminishing value.

The strongest next action is:

```text
run this concept on a real Windows 11 x64 machine
with stable .NET 10
```

The weaker model should be able to fully verify:

```text
restore
build
unit/integration/failure tests
Release
publish
artifact contents
basic executable startup
process locking
most data-integrity/recovery behavior
```

With UI Automation or computer-use capability it can additionally verify most functional GUI flows.

The only items that should normally remain for a human if the model is shell-only are:

```text
visual polish
clipping/overlap
DPI appearance
focus visibility quality
glyph appearance
other subjective visual quality
```

Those must remain explicitly pending rather than being guessed.
