# Prompt Helper — Tenth Paranoid Audit + Foolproof Final-Closure Instructions (`_plh10.md`)

**Repository:** `Ceegore/AI_prompt_helper`  
**Current `main`:** `b4f586b1eb4409d5db1570a50689217a5fc86c67`  
**Frozen release tag `v0.1.0`:** `0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd`  
**Release-source commit:** `0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd`  
**Audit date:** 2026-08-20  
**Purpose:** Verify the claimed resolution of `_plh9_with_weak_model_helpers.md` and provide zero-ambiguity closure instructions.

---

# 1. Executive verdict

The repository/product state is stable.

No new product-code defect was found.

The release tag is now correctly frozen at:

```text
v0.1.0
→
0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd
```

Current `main` is:

```text
b4f586b1eb4409d5db1570a50689217a5fc86c67
```

This is **not a release-provenance defect** because the only change from `0eef3eb` to `b4f586b` is:

```text
_plh9_with_weak_model_helpers.md
```

No product or test source changed.

Therefore:

```text
release source:
0eef3eb

main:
0eef3eb + documentation only
```

is healthy.

However, the supplied "Final Release Acceptance Report" is still not internally valid as a full closure report.

The central issue remains:

```text
it claims that published GUI/runtime scope was separated,
but it does not actually provide separate published-GUI evidence
for many required gates.
```

It also states:

```text
Automated & Mechanical Validation: PASS
```

while explicitly classifying:

```text
Network-Disabled Sandbox Workflow:
BLOCKED_ENVIRONMENT
```

A mandatory mechanical gate cannot be both blocked and part of an overall unconditional mechanical PASS.

---

# 2. Finding count

## Product defects

```text
Critical: 0
High:     0
Medium:   0
Low:      0
```

## Verification/reporting defects

```text
Critical: 0
High:     1
Medium:   2
Low:      0
```

Findings:

```text
PLH10-001 HIGH
Published GUI/runtime gates are still not separately evidenced.
Service-level PASS rows are being used as if they also close real WPF E2E.

PLH10-002 MEDIUM
"Automated & Mechanical Validation: PASS" contradicts a mandatory
BLOCKED_ENVIRONMENT mechanical gate and other non-evidenced runtime gates.

PLH10-003 MEDIUM
The high-level summary incorrectly groups the blocked network-disabled
sandbox test together with visual/DPI HUMAN_REQUIRED work.
Those are different statuses and different kinds of remaining work.
```

No source fix is currently justified.

---

# 3. Current repository verification

Fresh GitHub state:

```text
main:
b4f586b1eb4409d5db1570a50689217a5fc86c67
```

Commit message:

```text
docs: add ninth paranoid audit report _plh9_with_weak_model_helpers.md
```

Comparison:

```text
0eef3eb
→
b4f586b
```

shows one changed file:

```text
_plh9_with_weak_model_helpers.md
```

Therefore:

```text
PRODUCTION SOURCE CHANGE:
none

TEST SOURCE CHANGE:
none
```

The product/release code remains the already-audited `0eef3eb` tree.

---

# 4. Frozen tag verification

Fresh comparison:

```text
v0.1.0
==
0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd
```

Therefore:

```text
PLH9-003:
RESOLVED
```

Do not move `v0.1.0` again.

---

# 5. Important clarification: main no longer has to equal the release tag

The current documentation commit means:

```text
main
!=
v0.1.0
```

That is now correct.

The release tag identifies the exact released source.

Later documentation may exist on `main`.

Do **not** "repair" this by moving `v0.1.0` to `b4f586b`.

That would be wrong.

---

# 6. PLH9-002 classification correction — partially resolved

The supplied report correctly changed the network-disabled test to:

```text
BLOCKED_ENVIRONMENT
```

That is an improvement.

It no longer falsely calls a socket observation an offline-runtime PASS.

Therefore the classification itself is now correct.

But the high-level verdict is still wrong because it says:

```text
Automated & Mechanical Validation: PASS
```

while a required mechanical gate is blocked.

Correct:

```text
AUTOMATED TEST SUITE:
PASS

AVAILABLE MECHANICAL CHECKS:
PASS

COMPLETE MECHANICAL RELEASE VALIDATION:
INCOMPLETE — BLOCKED_ENVIRONMENT
```

until the offline test is actually run.

---

# 7. PLH10-001 — HIGH — published GUI E2E still not separately established

The acceptance matrix includes:

```text
Category Lifecycle & Tree Integrity
Prompt Lifecycle & Persistence
Reload Persistence
Unavailable Prompt State Handling
Orphan Prompt File Preservation
Corrupt Primary Auto-Recovery
Double-Corruption Safety Stop
Future-Schema Safety Stop
Dialog Keyboard Default Routing
```

and marks all as:

```text
PASS
```

But the evidence descriptions still correspond primarily to:

```text
service/integration tests
source configuration
file-state tests
```

not explicit WPF E2E.

The prior instructions required actual:

```text
PromptHelper.exe
```

runtime interactions.

Those must remain separate rows.

---

# 8. The matrix must distinguish two layers

For every capability with both service coverage and GUI/runtime behavior, use two rows.

Example:

```text
Service Category Lifecycle:
PASS

Published Category GUI E2E:
PASS / BLOCKED_ENVIRONMENT
```

Do not collapse them.

---

# 9. Required category split

Current report:

```text
Category Lifecycle & Tree Integrity:
PASS
```

Correct structure:

```text
Service Category Lifecycle:
PASS

Published Category GUI CRUD:
PASS / BLOCKED_ENVIRONMENT
```

The published row requires actual WPF interaction.

---

# 10. Required prompt split

Current report:

```text
Prompt Lifecycle & Persistence:
PASS
```

Correct:

```text
Service Prompt Lifecycle:
PASS

Published Prompt GUI CRUD:
PASS / BLOCKED_ENVIRONMENT
```

---

# 11. Required persistence split

Current:

```text
Reload Persistence:
PASS
```

Correct:

```text
Service Disk Reload Persistence:
PASS

Published Process Restart Persistence:
PASS / BLOCKED_ENVIRONMENT
```

A second `LoadOrInitialize()` inside one MSTest process is not a new desktop-process restart.

---

# 12. Required unavailable-prompt split

Current:

```text
Unavailable Prompt State Handling:
PASS
```

Correct:

```text
Service Unavailable-Prompt Behavior:
PASS

Published Unavailable-Prompt UI:
PASS / BLOCKED_ENVIRONMENT
```

The published row must verify actual enabled/disabled UI controls.

---

# 13. Required orphan split

Current:

```text
Orphan Prompt File Preservation:
PASS
```

Correct:

```text
Service Orphan Preservation:
PASS

Published Orphan Startup Smoke:
PASS / BLOCKED_ENVIRONMENT
```

---

# 14. Required recovery split

Current:

```text
Corrupt Primary Auto-Recovery:
PASS
```

Correct:

```text
Service Corrupt-Primary Recovery:
PASS

Published Corrupt-Primary Recovery:
PASS / BLOCKED_ENVIRONMENT
```

The published row includes the actual startup warning and usable MainWindow.

---

# 15. Required double-corruption split

Correct:

```text
Service Double-Corruption Safety:
PASS

Published Double-Corruption Fatal Startup:
PASS / BLOCKED_ENVIRONMENT
```

---

# 16. Required future-schema split

Correct:

```text
Service Future-Schema Safety:
PASS

Published Future-Schema Fatal Startup:
PASS / BLOCKED_ENVIRONMENT
```

---

# 17. Required keyboard split

Current:

```text
Dialog Keyboard Default Routing:
PASS
```

Evidence:

```text
IsDefault="True"
Save not default in editor
```

This establishes only static configuration.

Correct:

```text
Keyboard Static Configuration:
PASS

Keyboard Runtime Behavior:
PASS / BLOCKED_ENVIRONMENT
```

---

# 18. PLH10-002 — MEDIUM — overall verdict contradiction

The report says:

```text
Network-Disabled Sandbox Workflow:
BLOCKED_ENVIRONMENT
```

but:

```text
Automated & Mechanical Validation:
PASS
```

These cannot both be used as a full mechanical acceptance statement.

The network-disabled workflow is a required mechanical/runtime check.

Therefore the current maximum verdict is:

```text
AUTOMATED TEST SUITE PASS
AVAILABLE MECHANICAL CHECKS PASS
COMPLETE RELEASE VALIDATION INCOMPLETE
```

If actual WPF E2E is also unavailable:

```text
COMPLETE MECHANICAL VALIDATION:
BLOCKED_ENVIRONMENT
```

---

# 19. PLH10-003 — MEDIUM — network and visual work are incorrectly grouped

The summary says:

```text
Physical Sandbox / DPI Visual Inspection:
HUMAN_REQUIRED / PENDING
```

This merges two unrelated things.

Correct:

```text
Network-disabled sandbox:
BLOCKED_ENVIRONMENT

DPI / visual inspection:
HUMAN_REQUIRED
```

Network disconnection is not inherently a human visual task.

A suitable VM/Sandbox executor can perform it mechanically.

---

# 20. Current accurate status

```text
PRODUCT SOURCE:
PASS

RELEASE TAG:
PASS

TEST SUITE:
REPORTED PASS 154/154 Debug + Release

BUILD:
REPORTED PASS

PUBLISH:
REPORTED PASS

BINARY PROVENANCE:
REPORTED PASS

SERVICE INTEGRATION:
PASS / reported execution

SINGLE INSTANCE:
REPORTED PASS

CLIPBOARD:
REPORTED PASS

PUBLISHED GUI E2E:
NOT ESTABLISHED FROM SUPPLIED EVIDENCE

PUBLISHED RECOVERY E2E:
NOT ESTABLISHED FROM SUPPLIED EVIDENCE

KEYBOARD RUNTIME:
NOT ESTABLISHED FROM SUPPLIED EVIDENCE

NETWORK-DISABLED OFFLINE:
BLOCKED_ENVIRONMENT

VISUAL/DPI:
HUMAN_REQUIRED

FULL RELEASE VALIDATION:
NOT YET COMPLETE
```

---

# 21. Do not change product code

This is the most important instruction for the next weak executor.

```text
DO NOT EDIT src/PromptHelper/**
```

There is currently no demonstrated product defect.

The next action is evidence execution/classification only.

---

# 22. Foolproof executor protocol

The weak model must follow these steps in exact order.

It is not allowed to skip ahead.

---

# 23. STEP 0 — answer three capability questions before testing

The executor must write:

```text
CAPABILITY A:
Can I launch PromptHelper.exe and interact with WPF controls?
YES / NO

CAPABILITY B:
Can I observe/query the resulting UI state after each action?
YES / NO

CAPABILITY C:
Can I run Windows with network access genuinely disabled?
YES / NO
```

Do not continue until these three answers are recorded.

---

# 24. Capability mapping — no improvisation

Use this exact mapping.

## If A = NO

Then all actual published GUI/runtime interaction gates become:

```text
BLOCKED_ENVIRONMENT
```

including:

```text
Published Category GUI CRUD
Published Prompt GUI CRUD
Published Move
Published Duplicate
Published Process Restart Persistence
Published Unavailable-Prompt UI
Published recovery warning/UI verification
Keyboard Runtime
```

Do not mark them PASS.

---

# 25. If A = YES but B = NO

You may perform process actions, but cannot reliably confirm UI outcomes.

Therefore:

```text
GUI outcome-dependent gate:
BLOCKED_ENVIRONMENT
```

Do not infer outcome from files alone unless the specific gate is defined as file-only.

---

# 26. If A = YES and B = YES

Run the exact GUI workflow in this report.

Do not call service APIs.

---

# 27. If C = NO

Set:

```text
Network-Disabled Offline Workflow:
BLOCKED_ENVIRONMENT
```

Do not run another socket scan.

Do not substitute static privacy.

---

# 28. If C = YES

Actually disable network first, then launch the release binary and run the offline workflow.

---

# 29. STEP 1 — verify exact release binary

Release source/tag:

```text
0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd
```

The executable being tested must contain:

```text
ProductVersion:
0.1.0+0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd
```

If not:

```text
STOP
RESULT = FAIL
```

Do not run GUI acceptance against another binary.

---

# 30. STEP 2 — create disposable test state

Never run destructive recovery scenarios on valuable user data.

Use one:

```text
Windows Sandbox
disposable VM
disposable Windows account
known disposable Prompt Helper data directory
```

If none are available:

```text
destructive recovery E2E:
BLOCKED_ENVIRONMENT
```

Do not delete arbitrary local data.

---

# 31. STEP 3 — published category GUI

Only if:

```text
A = YES
B = YES
```

Use actual UI.

Exact names:

```text
E2E_Category_A
E2E_Nested
E2E_Delete
```

Actions:

```text
1. Launch PromptHelper.exe.
2. Click category + Add.
3. Enter E2E_Category_A.
4. Press Enter.
5. Confirm E2E_Category_A is visible.
6. Open E2E_Category_A.
7. Click + Add.
8. Enter E2E_Nested.
9. Press Enter.
10. Confirm E2E_Nested visible.
11. Try to create e2e_nested.
12. Confirm duplicate rejection appears.
13. Rename E2E_Nested → E2E_Nested_Renamed.
14. Confirm renamed card visible.
15. Make E2E_Category_A non-empty.
16. Attempt delete.
17. Confirm exact message:
    This category is not empty.

    Move or delete its prompts and subcategories first.
18. Create empty E2E_Delete.
19. Delete it.
20. Confirm custom Cancel/Delete dialog appears.
21. Confirm Delete.
22. Confirm E2E_Delete disappears.
```

Pass condition:

```text
all 22 steps succeed
```

If one fails:

```text
FAIL
```

---

# 32. STEP 4 — published prompt GUI

Only if A/B = YES.

Exact normal prompt text:

```text
# E2E Prompt

Unicode: ä ö ü ß 日本語 한국어 中文 Русский 🚀

- one
- two

```json
{
  "test": true
}
```
```

Actions:

```text
1. Create prompt through actual editor.
2. Save.
3. Confirm card visible.
4. Edit through actual Edit action.
5. Confirm previous content loaded.
6. Append "Edited."
7. Save.
8. Reopen.
9. Confirm edited content.
10. Create empty prompt.
11. Reopen empty prompt.
12. Confirm still empty.
13. Create exactly 50,000-character prompt.
14. Save.
15. Reopen.
16. Confirm full content/length.
17. Delete a prompt.
18. Confirm delete dialog.
19. Confirm it disappears.
```

---

# 33. STEP 5 — published Move

Create:

```text
E2E_Move_A
E2E_Move_B
```

Create source prompt in A:

```text
MOVE_TEST_CONTENT
```

Then actual UI:

```text
1. Click Move.
2. Confirm current category preselected.
3. Select E2E_Move_B.
4. Execute Move.
5. Confirm source prompt no longer appears in A.
6. Open B.
7. Confirm same prompt appears.
8. Confirm exact content.
```

---

# 34. STEP 6 — published Duplicate

Source:

```text
DUPLICATE_TEST_CONTENT
```

Actual UI:

```text
1. Open Move dialog.
2. Enable Copy instead of move.
3. Choose destination.
4. Execute.
5. Confirm source still exists.
6. Confirm destination contains duplicate.
7. Confirm content exactly equals source.
```

---

# 35. STEP 7 — published process restart persistence

After category/prompt/move/duplicate mutations:

```text
1. Close PromptHelper normally.
2. Confirm process has exited.
3. Launch PromptHelper.exe again as a NEW process.
4. Through UI confirm:
   - created categories remain
   - renamed category remains renamed
   - deleted category absent
   - edited prompt remains edited
   - moved prompt remains in destination
   - duplicate remains
   - empty prompt remains
```

Do not use:

```text
startup.LoadOrInitialize()
```

as evidence for this row.

---

# 36. STEP 8 — unavailable prompt UI

Use disposable data.

```text
1. Create a prompt through UI.
2. Close app.
3. Identify exactly the newly created .md file.
4. Delete only that file.
5. Relaunch app.
6. Confirm actual unavailable UI state.
```

Required UI:

```text
(Unavailable prompt)
load-failure body/state
Delete enabled
Move enabled
Edit disabled
Copy disabled
Duplicate/Copy instead of move disabled
```

Then:

```text
7. Move unavailable prompt through actual Move dialog.
8. Confirm it appears in destination still unavailable.
```

---

# 37. STEP 9 — orphan published startup

Disposable state only.

```text
1. Close app.
2. Create arbitrary GUID-named .md in prompts folder.
3. Record hash.
4. Launch published EXE.
5. Confirm normal startup.
6. Confirm orphan is not shown.
7. Confirm orphan still exists.
8. Confirm hash unchanged.
```

---

# 38. STEP 10 — published corrupt-primary recovery

Disposable state.

```text
1. Start from valid primary + valid backup.
2. Close app.
3. Record prompt file hashes.
4. Corrupt primary.
5. Launch published EXE.
6. Confirm recovery warning appears.
7. Confirm MainWindow opens normally.
8. Confirm primary restored.
9. Confirm prompt hashes unchanged.
10. Confirm no unexpected reset/default recreation.
```

---

# 39. STEP 11 — published double-corruption

Fresh disposable state.

```text
1. Record prompt hashes.
2. Corrupt primary.
3. Corrupt backup.
4. Launch published EXE.
5. Confirm fatal startup.
6. Confirm normal MainWindow does NOT open.
7. Confirm defaults are NOT initialized.
8. Confirm prompt hashes unchanged.
```

---

# 40. STEP 12 — published future schema

Fresh disposable state.

Primary:

```json
{
  "schemaVersion": 999,
  "categories": [],
  "prompts": []
}
```

Keep valid old backup.

Then:

```text
1. Hash primary.
2. Launch published EXE.
3. Confirm future-schema fatal startup.
4. Confirm normal MainWindow does not open.
5. Confirm backup was not restored over primary.
6. Confirm primary hash unchanged.
7. Confirm defaults not created.
```

---

# 41. STEP 13 — runtime keyboard

Only actual key interaction counts.

## Name dialog

```text
Enter:
submits valid name

Escape:
cancels
```

## Delete confirmation

```text
Enter:
confirms Delete

Escape:
cancels
```

## Prompt editor

```text
Enter:
creates newline

Escape:
cancels

Enter:
does NOT save the editor
```

## Move dialog

```text
Enter:
executes current Move/Copy action

Escape:
cancels
```

## Main

```text
Tab and Shift+Tab:
move focus through interactive controls
```

If executor cannot send keys to actual WPF UI:

```text
Keyboard Runtime:
BLOCKED_ENVIRONMENT
```

---

# 42. STEP 14 — network-disabled offline

Only run if C = YES.

Before app launch:

```text
network must actually be unavailable
```

Then perform:

```text
launch
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

If all work:

```text
PASS
```

If C = NO:

```text
BLOCKED_ENVIRONMENT
```

Never substitute:

```text
zero sockets
grep
static source
```

---

# 43. STEP 15 — visual/DPI

Requires actual visual capability.

Check:

```text
900×600 @ 100%
900×600 @ 125%
900×600 @ 150%
```

Inspect:

```text
clipping
overlap
dialog fit
readability
focus visibility
breadcrumb usability
category cards
prompt list
buttons
```

If no visual capability:

```text
HUMAN_REQUIRED
```

This is the only correct non-failure classification for visual-only work.

---

# 44. Foolproof result rules

The weak model may not invent combined statuses.

Use these exact rules.

## Rule A

If a gate was executed and all assertions succeeded:

```text
PASS
```

## Rule B

If executed and application behavior was wrong:

```text
FAIL
```

## Rule C

If the model lacks the capability/environment to execute it:

```text
BLOCKED_ENVIRONMENT
```

## Rule D

If it inherently requires visual human/screenshot judgment that the model cannot provide:

```text
HUMAN_REQUIRED
```

## Rule E

Never convert:

```text
BLOCKED_ENVIRONMENT
```

into:

```text
PASS
```

because unit tests exist.

---

# 45. Mandatory corrected acceptance matrix

The next report MUST use at least these rows:

| Gate | Status |
|---|---|
| Release tag provenance | PASS/FAIL |
| Binary ProductVersion provenance | PASS/FAIL/BLOCKED_ENVIRONMENT |
| Debug build | PASS/FAIL |
| Debug tests | PASS/FAIL |
| Release build | PASS/FAIL |
| Release tests | PASS/FAIL |
| Publish package | PASS/FAIL |
| Static privacy | PASS/FAIL |
| Single instance | PASS/FAIL/BLOCKED_ENVIRONMENT |
| Clipboard exact equality | PASS/FAIL/BLOCKED_ENVIRONMENT |
| Service category lifecycle | PASS/FAIL |
| **Published category GUI CRUD** | PASS/FAIL/BLOCKED_ENVIRONMENT |
| Service prompt lifecycle | PASS/FAIL |
| **Published prompt GUI CRUD** | PASS/FAIL/BLOCKED_ENVIRONMENT |
| **Published Move dialog** | PASS/FAIL/BLOCKED_ENVIRONMENT |
| **Published Duplicate flow** | PASS/FAIL/BLOCKED_ENVIRONMENT |
| Service disk reload | PASS/FAIL |
| **Published process restart persistence** | PASS/FAIL/BLOCKED_ENVIRONMENT |
| Service unavailable prompt | PASS/FAIL |
| **Published unavailable-prompt UI** | PASS/FAIL/BLOCKED_ENVIRONMENT |
| Service orphan preservation | PASS/FAIL |
| **Published orphan startup** | PASS/FAIL/BLOCKED_ENVIRONMENT |
| Service corrupt-primary recovery | PASS/FAIL |
| **Published corrupt-primary recovery** | PASS/FAIL/BLOCKED_ENVIRONMENT |
| Service double-corruption safety | PASS/FAIL |
| **Published double-corruption fatal startup** | PASS/FAIL/BLOCKED_ENVIRONMENT |
| Service future-schema safety | PASS/FAIL |
| **Published future-schema fatal startup** | PASS/FAIL/BLOCKED_ENVIRONMENT |
| Keyboard static configuration | PASS/FAIL |
| **Keyboard runtime behavior** | PASS/FAIL/BLOCKED_ENVIRONMENT |
| Network-disabled offline workflow | PASS/FAIL/BLOCKED_ENVIRONMENT |
| 900×600 @100% | PASS/FAIL/HUMAN_REQUIRED |
| 900×600 @125% | PASS/FAIL/HUMAN_REQUIRED |
| 900×600 @150% | PASS/FAIL/HUMAN_REQUIRED |
| Focus visibility | PASS/FAIL/HUMAN_REQUIRED |

No bold published-runtime row may be omitted.

---

# 46. Overall verdict algorithm — literal

Use this exact decision tree.

## Case 1 — any FAIL

Final:

```text
VALIDATION FAIL
```

Do not claim readiness.

---

## Case 2 — no FAIL, but any required mechanical/runtime row is BLOCKED_ENVIRONMENT

Final:

```text
VALIDATION INCOMPLETE — ENVIRONMENT BLOCKED
```

Then list blocked rows.

Do **not** say:

```text
Automated & Mechanical Validation PASS
```

for the complete release.

You may separately say:

```text
Available automated tests passed.
```

---

## Case 3 — all mechanical/runtime rows PASS, visual rows HUMAN_REQUIRED

Final:

```text
AUTOMATED/MECHANICAL VALIDATION PASS
FINAL VISUAL QA PENDING
```

---

## Case 4 — every required row PASS

Final:

```text
FULL RELEASE VALIDATION PASS
```

---

# 47. Current report should therefore NOT say mechanical PASS

Because the supplied report explicitly has:

```text
Network-Disabled Sandbox Workflow:
BLOCKED_ENVIRONMENT
```

the current maximum overall status is:

```text
VALIDATION INCOMPLETE — ENVIRONMENT BLOCKED
```

unless that blocked gate is later executed successfully.

If published GUI E2E is also not executable in the current environment, those rows are additional:

```text
BLOCKED_ENVIRONMENT
```

---

# 48. Zero-code-change policy

The current next pass should not produce a code commit.

Allowed:

```text
runtime evidence
screenshots
local logs
audit/report documentation
```

Not allowed without a reproduced FAIL:

```text
src changes
XAML changes
service changes
repository changes
new production dependencies
version changes
tag movement
```

---

# 49. If a real product FAIL appears

Only then:

```text
1. Record exact reproduction.
2. Record expected vs actual.
3. Re-run once to confirm.
4. Rule out test setup.
5. Make smallest fix.
6. Add regression test if practical.
7. Run full Debug 154+ suite.
8. Run full Release suite.
9. Publish new version 0.1.1.
10. Tag v0.1.1.
11. Re-run affected runtime gates.
```

Do not modify `v0.1.0`.

---

# 50. Copy-ready ultra-constrained prompt for the weak AI

```text
ROLE

You are the FINAL VERIFICATION EXECUTOR for Prompt Helper.

You are a weak AI. You are forbidden to improvise.

REPOSITORY

Ceegore/AI_prompt_helper

RELEASE SOURCE

0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd

RELEASE TAG

v0.1.0

CURRENT MAIN MAY BE AHEAD BY DOCUMENTATION ONLY.
DO NOT MOVE THE TAG.

PRIMARY RULE

DO NOT EDIT src/PromptHelper/** unless an actual published PromptHelper.exe test FAILS and the failure is reproduced twice.

BEFORE TESTING, PRINT:

CAPABILITY A:
Can I launch PromptHelper.exe and interact with WPF controls?
YES or NO

CAPABILITY B:
Can I observe/query WPF results after each action?
YES or NO

CAPABILITY C:
Can I run Windows with network genuinely disabled?
YES or NO

STATUS RULES

Executed and correct = PASS
Executed and wrong = FAIL
Cannot execute because environment lacks capability = BLOCKED_ENVIRONMENT
Visual-only inspection unavailable = HUMAN_REQUIRED

NEVER infer PASS from unit tests for a published GUI gate.

NEVER infer keyboard runtime PASS from IsDefault/IsCancel XAML.

NEVER infer offline PASS from zero sockets.

NEVER call same-process service reload a process restart.

NEVER move v0.1.0.

STEP 1 — VERIFY BINARY

The exact binary under test must report ProductVersion containing:

0eef3eb245b4ab75fb3a5f1ebdfb6f6e44c4b9cd

If it does not:
FAIL and STOP.

STEP 2 — SAFE TEST DATA

Use disposable data only.

If destructive recovery tests cannot be isolated safely:
mark those published recovery gates BLOCKED_ENVIRONMENT.

STEP 3 — SERVICE RESULTS

Keep existing 154/154 service/integration results as SERVICE-level evidence only.

Do not reuse them for GUI rows.

STEP 4 — PUBLISHED CATEGORY GUI

If A=YES and B=YES:
launch actual PromptHelper.exe and test:
create top-level category
create nested category
case-insensitive duplicate rejection
rename
non-empty delete rejection with exact message
empty delete confirmation
empty delete

If A=NO or B=NO:
Published Category GUI CRUD = BLOCKED_ENVIRONMENT

STEP 5 — PUBLISHED PROMPT GUI

If A/B=YES:
test actual editor:
create
edit
empty prompt
50,000-character prompt
delete

Else:
Published Prompt GUI CRUD = BLOCKED_ENVIRONMENT

STEP 6 — MOVE

If A/B=YES:
actual Move dialog:
A → B
verify source disappears
verify destination contains same prompt

Else:
Published Move = BLOCKED_ENVIRONMENT

STEP 7 — DUPLICATE

If A/B=YES:
actual Move dialog
enable Copy instead of move
verify source remains
verify duplicate exists
verify exact content

Else:
Published Duplicate = BLOCKED_ENVIRONMENT

STEP 8 — PROCESS RESTART

If A/B=YES:
close app normally
confirm process ends
launch NEW PromptHelper.exe process
verify all GUI mutations persisted

Else:
Published Process Restart Persistence = BLOCKED_ENVIRONMENT

STEP 9 — UNAVAILABLE PROMPT

If A/B=YES and disposable file access exists:
create prompt through UI
close app
delete exactly its .md
relaunch
verify:
Unavailable state shown
Delete enabled
Move enabled
Edit disabled
Copy disabled
Duplicate disabled
then actually Move it via UI

Else:
Published Unavailable-Prompt UI = BLOCKED_ENVIRONMENT

STEP 10 — ORPHAN

If A/B=YES and disposable file access exists:
create orphan GUID .md while app closed
launch actual EXE
verify normal startup
orphan hidden
file preserved unchanged

Else:
Published Orphan Startup = BLOCKED_ENVIRONMENT

STEP 11 — CORRUPT PRIMARY

If A/B=YES and disposable state exists:
valid primary + backup
corrupt primary
launch EXE
verify recovery warning
verify usable app
verify primary restored
verify prompt files unchanged

Else:
Published Corrupt-Primary Recovery = BLOCKED_ENVIRONMENT

STEP 12 — DOUBLE CORRUPTION

If A/B=YES and disposable state exists:
hash prompts
corrupt primary + backup
launch EXE
verify fatal startup
verify no MainWindow
verify no defaults
verify hashes unchanged

Else:
Published Double-Corruption Fatal Startup = BLOCKED_ENVIRONMENT

STEP 13 — FUTURE SCHEMA

If A/B=YES and disposable state exists:
primary schemaVersion=999
valid old backup
hash primary
launch EXE
verify fatal startup
verify no backup restore
verify primary hash unchanged
verify no defaults

Else:
Published Future-Schema Fatal Startup = BLOCKED_ENVIRONMENT

STEP 14 — KEYBOARD RUNTIME

If A/B=YES and keys can be sent:
Name: Enter submit, Escape cancel
Delete: Enter delete, Escape cancel
Prompt editor: Enter newline, Escape cancel, Enter does not save
Move: Enter action, Escape cancel
Main: Tab and Shift+Tab navigation

Else:
Keyboard Runtime = BLOCKED_ENVIRONMENT

STEP 15 — OFFLINE

If C=YES:
disable network BEFORE launch
run startup/create/edit/move/duplicate/copy/restart/delete
if all work: PASS

If C=NO:
Network-Disabled Offline Workflow = BLOCKED_ENVIRONMENT

STEP 16 — VISUAL

If you can visually inspect screenshots:
test 900x600 at 100%, 125%, 150%, focus visibility.

If not:
HUMAN_REQUIRED

STEP 17 — FINAL MATRIX

YOU MUST INCLUDE SEPARATE ROWS FOR:

Service Category Lifecycle
Published Category GUI CRUD

Service Prompt Lifecycle
Published Prompt GUI CRUD

Published Move
Published Duplicate

Service Disk Reload
Published Process Restart Persistence

Service Unavailable Prompt
Published Unavailable-Prompt UI

Service Orphan Preservation
Published Orphan Startup

Service Corrupt-Primary Recovery
Published Corrupt-Primary Recovery

Service Double Corruption
Published Double-Corruption Fatal Startup

Service Future Schema
Published Future-Schema Fatal Startup

Keyboard Static Configuration
Keyboard Runtime

Network-Disabled Offline Workflow

900x600 100%
900x600 125%
900x600 150%
Focus Visibility

DO NOT OMIT ANY OF THESE.

STEP 18 — FINAL VERDICT

If any FAIL:
VALIDATION FAIL

Else if any required mechanical/runtime gate is BLOCKED_ENVIRONMENT:
VALIDATION INCOMPLETE — ENVIRONMENT BLOCKED

Else if all mechanical/runtime PASS and visuals HUMAN_REQUIRED:
AUTOMATED/MECHANICAL VALIDATION PASS
FINAL VISUAL QA PENDING

Else if every required gate PASS:
FULL RELEASE VALIDATION PASS

IMPORTANT

Do not say "Automated & Mechanical Validation PASS" while any mandatory mechanical gate is BLOCKED_ENVIRONMENT.

Do not modify source just because a gate is blocked.

Do not retag.

Do not stop until every mandatory row has an explicit status.
```

---

# 51. Human fallback checklist if the weak AI has no GUI capability

If:

```text
CAPABILITY A = NO
```

the weak AI must stop trying to manufacture GUI evidence.

It should hand this checklist to a human.

The human only needs to run:

```text
A. Category:
create
nested create
duplicate rejection
rename
non-empty delete rejection
empty delete

B. Prompt:
create
edit
empty
50k
delete

C. Move:
A → B

D. Duplicate:
Copy instead of move

E. Restart:
close/reopen and verify persistence

F. Unavailable:
remove prompt file, relaunch, verify disabled/enabled states, Move

G. Recovery:
corrupt primary
double corruption
future schema

H. Keyboard:
Enter/Escape/Tab behavior

I. Visual:
900×600
125%
150%
focus
```

If network-disabled Sandbox/VM is available, also run the offline workflow.

---

# 52. What the next report should look like if the current environment truly lacks GUI

A fully honest report may say:

```text
Service Category Lifecycle:
PASS

Published Category GUI CRUD:
BLOCKED_ENVIRONMENT

Service Prompt Lifecycle:
PASS

Published Prompt GUI CRUD:
BLOCKED_ENVIRONMENT

Published Move:
BLOCKED_ENVIRONMENT

Published Duplicate:
BLOCKED_ENVIRONMENT

Service Disk Reload:
PASS

Published Process Restart Persistence:
BLOCKED_ENVIRONMENT

Service Unavailable Prompt:
PASS

Published Unavailable-Prompt UI:
BLOCKED_ENVIRONMENT

Service Orphan Preservation:
PASS

Published Orphan Startup:
BLOCKED_ENVIRONMENT

Service Corrupt-Primary Recovery:
PASS

Published Corrupt-Primary Recovery:
BLOCKED_ENVIRONMENT

Service Double-Corruption Safety:
PASS

Published Double-Corruption Fatal Startup:
BLOCKED_ENVIRONMENT

Service Future-Schema Safety:
PASS

Published Future-Schema Fatal Startup:
BLOCKED_ENVIRONMENT

Keyboard Static Configuration:
PASS

Keyboard Runtime:
BLOCKED_ENVIRONMENT

Network-Disabled Offline Workflow:
BLOCKED_ENVIRONMENT

Visual/DPI:
HUMAN_REQUIRED

FINAL:
VALIDATION INCOMPLETE — ENVIRONMENT BLOCKED
```

That is **better** than an inaccurate PASS.

---

# 53. What the next report should look like if GUI automation is available and succeeds

```text
Service Category Lifecycle:
PASS
Published Category GUI CRUD:
PASS

Service Prompt Lifecycle:
PASS
Published Prompt GUI CRUD:
PASS

Published Move:
PASS
Published Duplicate:
PASS

Service Disk Reload:
PASS
Published Process Restart Persistence:
PASS

Service Unavailable Prompt:
PASS
Published Unavailable-Prompt UI:
PASS

Service Orphan Preservation:
PASS
Published Orphan Startup:
PASS

Service Corrupt-Primary Recovery:
PASS
Published Corrupt-Primary Recovery:
PASS

Service Double-Corruption:
PASS
Published Double-Corruption Fatal Startup:
PASS

Service Future-Schema:
PASS
Published Future-Schema Fatal Startup:
PASS

Keyboard Static:
PASS
Keyboard Runtime:
PASS

Network-Disabled Offline:
BLOCKED_ENVIRONMENT

Visual:
HUMAN_REQUIRED

FINAL:
VALIDATION INCOMPLETE — ENVIRONMENT BLOCKED
```

Once the offline test also passes:

```text
AUTOMATED/MECHANICAL VALIDATION PASS
FINAL VISUAL QA PENDING
```

---

# 54. Final audit verdict

The latest report is more honest than the previous one, but it still overstates closure.

Resolved:

```text
tag freeze
offline classification
service/test source
product source
```

Still unresolved:

```text
published GUI E2E evidence
published recovery E2E evidence
keyboard runtime evidence
overall verdict consistency
network-disabled execution
visual/DPI
```

The next weak-model pass should produce **zero code changes** unless a real executable FAIL is discovered.

The correct immediate objective is not "fix the app."

It is:

```text
finish the evidence matrix without substituting one test layer for another.
```
