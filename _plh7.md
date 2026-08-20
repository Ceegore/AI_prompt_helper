# Prompt Helper — Seventh Paranoid Audit (`_plh7.md`)

**Repository:** `Ceegore/AI_prompt_helper`  
**Current `main`:** `27aee1fc9b4395fd8475e401b441c428d69253a3`  
**Current `v0.1.0` tag:** `27aee1fc9b4395fd8475e401b441c428d69253a3`  
**Previously clean product-source baseline:** `fb69b54973dbec7630f2cf47164bd9451fb0be19`  
**Audit date:** 2026-08-20  
**Purpose:** Paranoid verification of the claimed resolution of `_plh6.md`.

---

# 1. Executive verdict

The situation is materially improved.

The most serious `_plh6` release-provenance defect is now genuinely fixed:

```text
main
=
27aee1fc9b4395fd8475e401b441c428d69253a3

v0.1.0
=
27aee1fc9b4395fd8475e401b441c428d69253a3
```

GitHub comparison resolves the tag and branch to the same exact commit.

The current `v0.1.0` source also contains the repaired startup/recovery logic, not the original pre-fix implementation.

No production or test source changed between the previous audited source and this tag update cycle.

However, the statement:

```text
"Resolution of Findings"
```

is still too broad.

`PLH6-002` is **only partially resolved**.

The revised status correctly marks subjective visual/DPI QA as `HUMAN_REQUIRED`, but it still does not account for several other mandatory runtime acceptance gates required by `_plh_final_verification_concept.md`, including:

```text
published GUI CRUD flow
clipboard end-to-end
restart persistence
published missing-prompt behavior
published orphan behavior
published corrupt-primary recovery
published double-corruption safety
published future-schema safety
keyboard behavior
offline runtime
```

These are not merely subjective visual checks.

Therefore:

```text
NEW PRODUCT CODE DEFECTS:
0

RELEASE PROVENANCE DEFECT:
RESOLVED

FULL RELEASE ACCEPTANCE:
STILL NOT ESTABLISHED
```

---

# 2. Finding status summary

| Finding | Previous severity | Current status |
|---|---:|---|
| PLH6-001 — stale release tag / source-binary mismatch | High | **RESOLVED** |
| PLH6-002 — full release acceptance overclaimed | High | **PARTIALLY RESOLVED** |
| PLH6-003 — static privacy scan mislabeled as offline runtime PASS | Medium | **RESOLVED AS CLASSIFICATION**, offline runtime itself still pending |

No new product-code defect was found.

---

# 3. Current repository state

GitHub `main` resolves to:

```text
27aee1fc9b4395fd8475e401b441c428d69253a3
```

Commit message:

```text
docs: add sixth paranoid audit report _plh6.md
```

The only change from:

```text
eff97e7e1636bee773406ee2a814ef314b652347
```

to:

```text
27aee1fc9b4395fd8475e401b441c428d69253a3
```

is:

```text
_plh6.md
```

No application or test source changed.

Therefore the prior clean-source result still applies.

---

# 4. PLH6-001 — RESOLVED

## Previous problem

`v0.1.0` originally resolved to:

```text
c46419079eab56d0b66acf33e6e15d126b53d391
```

which was the original implementation before four repair rounds.

That created a real mismatch:

```text
tag source
!=
fixed binary source
```

and GitHub's generated release source archives exposed known-buggy source.

---

# 5. Current tag verification

A fresh GitHub comparison now shows:

```text
base:
v0.1.0

head:
27aee1fc9b4395fd8475e401b441c428d69253a3

status:
identical

ahead:
0

behind:
0
```

This independently proves:

```text
v0.1.0
=
27aee1fc9b4395fd8475e401b441c428d69253a3
```

The main branch also independently resolves to the same SHA.

So:

```text
main
=
tag
=
27aee1fc...
```

---

# 6. Tagged source is now the repaired source

I also directly inspected:

```text
v0.1.0/src/PromptHelper/Services/LibraryStartupService.cs
```

The tagged source now contains the final fixed logic:

```text
read primary

future primary
→ fatal

valid primary
→ resolve immediately
→ attempt backup sync
→ backup sync failure becomes warning
→ return valid primary

only inspect backup if primary is corrupt or missing
```

This is the repaired `PLH4-002` behavior.

The old tagged version previously did:

```text
read primary
read backup unconditionally
then resolve valid primary
silently swallow backup sync failure
```

That obsolete code is no longer present at the tag.

Therefore the source-side portion of `PLH6-001` is definitely fixed.

---

# 7. Binary provenance claim

The supplied repair report states the rebuilt executable contains:

```text
ProductVersion:
0.1.0+27aee1fc9b4395fd8475e401b441c428d69253a3
```

If correct, that gives ideal correspondence:

```text
tag SHA
=
main SHA
=
binary SourceRevisionId
=
27aee1fc...
```

This is exactly what `_plh6.md` required.

---

# 8. Public release asset limitation in this audit environment

The available GitHub connector allows source/tag/branch/commit inspection but does not expose release-asset download or release-asset metadata for this repository.

I attempted to retrieve the public:

```text
PromptHelper-v0.1.0-win-x64.zip
```

directly, but the current execution environment could not resolve/download the asset.

Therefore I cannot independently authenticate:

```text
the ZIP currently uploaded to GitHub
its SHA-256
its actual contained PromptHelper.exe
its embedded ProductVersion
its LICENSE
its THIRD_PARTY_NOTICES.md
```

The supplied report is the evidence for those binary-asset claims.

This does not invalidate the repaired tag/source state.

It means the uploaded-asset portion remains externally asserted rather than independently re-downloaded in this audit.

---

# 9. PLH6-001 final status

## Source/tag half

```text
PASS — independently verified
```

## Binary upload half

```text
REPORTED PASS — not independently downloadable here
```

Overall:

```text
PLH6-001:
RESOLVED
```

because the original definite defect was the stale tag, and that tag is now demonstrably corrected.

---

# 10. PLH6-002 — only partially resolved

The new report says:

```text
Automated Repository & Persistence Suite:
149/149 PASS

Published Binary & Single-Instance Smoke:
PASS

Subjective/Visual Layout QA:
HUMAN_REQUIRED
```

This is much better than the previous false:

```text
"all phases executed"
"ready for release"
```

However, this still does not fully implement the acceptance taxonomy in `_plh_final_verification_concept.md`.

---

# 11. Why visual QA is not the only remaining category

The verification concept requires full product release acceptance to include:

```text
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

The new repair summary explicitly marks only the subjective/visual portion:

```text
125%
150%
visual font rendering
```

as `HUMAN_REQUIRED`.

That leaves multiple mandatory **non-visual** runtime checks unaccounted for.

---

# 12. Missing non-visual runtime gate: GUI category flow

There is still no supplied evidence for the published executable performing:

```text
create category
create nested category
reject duplicate sibling case-insensitively
rename category
reject deletion of non-empty category
confirm and delete empty category
```

These are functional tests.

They do not require subjective visual judgment.

A Tier-B UIAutomation executor can test them.

---

# 13. Missing non-visual runtime gate: prompt GUI flow

There is still no supplied evidence for published-app execution of:

```text
create prompt
edit prompt
delete prompt
empty prompt
large 50k prompt
Home prompt
nested prompt
```

The 149 unit/integration tests provide strong domain coverage.

They do not prove published WPF wiring end-to-end.

---

# 14. Missing non-visual runtime gate: Move

No published-GUI evidence is shown for:

```text
open Move
current category preselected
select destination
confirm
source disappears
same prompt appears at destination
```

This is a deterministic UI flow.

---

# 15. Missing non-visual runtime gate: Duplicate

No published-GUI evidence is shown for:

```text
Copy instead of move
source remains
duplicate appears at destination
content equals source
```

Again, deterministic and automatable.

---

# 16. Missing non-visual runtime gate: Clipboard

No evidence is shown for the actual Windows clipboard path:

```text
click Copy
read clipboard from STA Windows process
compare exact prompt text
```

Required content coverage:

```text
Unicode
Markdown
blank lines
code fence
no truncation
```

Required feedback:

```text
Copied ✓
→
Copy
```

This is not a visual-only test.

The exact text comparison can be mechanically verified.

---

# 17. Missing non-visual runtime gate: Restart persistence

The phrase:

```text
Automated Repository & Persistence Suite
```

does not replace the required published persistence smoke.

The verification concept specifically requires:

```text
perform real GUI mutations
close the published app
restart the published app
verify all mutations persisted
```

No such evidence is supplied.

---

# 18. Missing non-visual runtime gate: unavailable prompt behavior

No published-app evidence is supplied for:

```text
metadata references prompt
.md removed

restart

card displays unavailable state
Delete enabled
Move enabled
Edit disabled
Copy disabled
Duplicate unavailable
```

This validates real UI wiring for the missing-file state.

---

# 19. Missing non-visual runtime gate: orphan behavior

No published-app evidence is supplied for:

```text
valid metadata
arbitrary GUID-named orphan .md

restart

normal startup
orphan hidden
orphan preserved on disk
```

The service tests are valuable but do not replace the published-smoke requirement.

---

# 20. Missing non-visual runtime gate: corrupt-primary recovery

No isolated published-app evidence is supplied for:

```text
valid primary
valid backup

corrupt primary

launch published EXE

recovery occurs
warning shown
primary restored
prompt files preserved
recovery copy attempted
```

This remains an explicit final verification requirement.

---

# 21. Missing non-visual runtime gate: double-corruption safety

No isolated published-app evidence is supplied for:

```text
corrupt primary
corrupt backup

launch

fatal startup
no default initialization
prompt files unchanged
```

This is a data-safety runtime gate.

---

# 22. Missing non-visual runtime gate: future-schema safety

No isolated published-app evidence is supplied for:

```text
primary schemaVersion = 999
old valid backup present

launch

future schema fatal
backup NOT restored
future primary unchanged
defaults NOT created
```

This is one of the highest-value release smoke tests.

---

# 23. Missing non-visual runtime gate: keyboard semantics

The new repair summary does not classify:

```text
Name dialog:
Enter submit
Escape cancel

Prompt editor:
Enter newline
Tab input
Escape cancel
Enter does NOT save

Move dialog:
Enter action
Escape cancel

main:
Tab / Shift+Tab navigation
```

Visible focus quality may require human/Tier-C judgment.

The key semantics themselves can be tested mechanically.

---

# 24. Correct status for PLH6-002

The repair is not wrong.

It is incomplete.

Correct status:

```text
PLH6-002:
PARTIALLY RESOLVED
```

What was fixed:

```text
the report no longer treats visual/DPI QA as automatically passed
```

What remains:

```text
several mandatory runtime gates are still neither PASS
nor BLOCKED_ENVIRONMENT
nor HUMAN_REQUIRED
nor NOT_APPLICABLE
```

That still violates the verification concept's rule that every required item receive an explicit status.

---

# 25. Severity of remaining PLH6-002 gap

This remains:

```text
HIGH
```

as a release-verification/process finding.

Not because there is evidence the product is broken.

There is not.

It remains High because the process can still produce an incorrect:

```text
release accepted
```

state without testing required data-safety/runtime paths.

---

# 26. PLH6-003 — classification defect resolved

The previous problem was:

```text
0 networking patterns
→ labeled "Privacy & Offline PASS"
```

The revised report now correctly says:

```text
Static Source Privacy Audit PASS

end-to-end sandbox offline network disconnection
=
runtime environment test
```

That classification is correct.

Therefore:

```text
PLH6-003:
RESOLVED
```

---

# 27. Important distinction for PLH6-003

The **finding** is resolved because the report no longer falsely equates:

```text
static scan
```

with:

```text
runtime offline test
```

But the actual mandatory runtime offline gate is still pending.

Current accurate state:

```text
STATIC PRIVACY SCAN:
PASS

OFFLINE RUNTIME:
NOT YET ESTABLISHED
```

---

# 28. Current automated test claims

The supplied reports state:

```text
Debug:
149 / 149 pass
0 failed
0 skipped

Release:
149 / 149 pass
0 failed
0 skipped
```

No production or test source changed after those claimed runs except documentation.

The named regression tests still exist in the repository.

I found no contradiction that would invalidate the 149/149 claim.

Therefore:

```text
AUTOMATED TEST CLAIM:
PLAUSIBLE / CONSISTENT WITH CURRENT SOURCE
```

---

# 29. CI evidence

Current commit:

```text
27aee1fc...
```

has:

```text
GitHub combined statuses:
none

GitHub workflow runs:
none
```

This is unchanged from previous audits.

This is not itself a product defect.

It means the local test run remains local evidence rather than independently reproduced CI evidence.

---

# 30. Current product source result

Because:

```text
fb69b549
→ eff97e7
→ 27aee1f
```

added only documentation after the fifth clean source pass, the product code remains the same repaired source.

No reason exists to reopen speculative code repair.

Current:

```text
PRODUCT SOURCE:
STATIC PASS

NEW PRODUCT DEFECTS:
0
```

---

# 31. Retagging caveat

The repair report explicitly says:

```text
forced-updated the remote tag
```

Moving an already-public version tag is normally undesirable because tags are expected to be immutable release identifiers.

Potential consequences:

```text
a user who fetched the old v0.1.0 tag may still have c464190 locally
another user fetching now gets 27aee1f
the same version label has referred to two different source trees
cached source archives may temporarily differ
```

If the release was created only minutes earlier and had no consumers, the practical risk may be negligible.

I therefore do **not** promote this to a confirmed defect without evidence of external consumption.

But future releases should use immutable tags.

If a published release must be materially corrected after external availability, prefer:

```text
v0.1.1
```

over silently moving:

```text
v0.1.0
```

---

# 32. What remains before full release validation can pass

At minimum assign explicit outcomes to:

```text
GUI category CRUD
GUI prompt CRUD
Move
Duplicate
Clipboard
Restart persistence
Unavailable prompt
Orphan preservation
Corrupt-primary published recovery
Double-corruption published safety
Future-schema published safety
Keyboard semantics
Offline runtime
900×600
125% DPI
150% DPI
focus visibility
```

Allowed statuses:

```text
PASS
FAIL
BLOCKED_ENVIRONMENT
HUMAN_REQUIRED
NOT_APPLICABLE
```

No required item should simply disappear from the report.

---

# 33. Weak-model-friendly next action

Use the existing:

```text
_plh_final_verification_concept.md
```

and resume at the runtime/UI phases rather than redoing static source analysis.

Recommended execution order:

```text
1. GUI Automation feasibility
2. first-start/defaults
3. category flow
4. prompt CRUD
5. clipboard
6. move
7. duplicate
8. restart persistence
9. unavailable prompt
10. orphan
11. corrupt-primary recovery
12. double corruption
13. future schema
14. keyboard semantics
15. network-disabled offline run
16. visual 900×600 / 125% / 150%
```

Use an isolated Windows environment for destructive scenarios.

---

# 34. Final status table

| Area | Status |
|---|---|
| Current `main` | PASS |
| `v0.1.0` tag matches `main` | **PASS — independently verified** |
| Tagged source contains final fixes | **PASS — independently verified** |
| Product source regression check | PASS |
| 149 Debug tests | Reported PASS |
| 149 Release tests | Reported PASS |
| Debug `/warnaserror` build | Reported PASS |
| Release `/warnaserror` build | Reported PASS |
| Self-contained publish | Reported PASS |
| Binary ProductVersion = `27aee1f...` | Reported PASS |
| Public ZIP contents | Reported PASS, not independently retrievable here |
| Single-instance smoke | Reported PASS |
| Static privacy scan | PASS / correctly classified |
| GUI CRUD runtime | **NOT ESTABLISHED** |
| Clipboard runtime | **NOT ESTABLISHED** |
| Restart persistence | **NOT ESTABLISHED** |
| Published recovery smoke | **NOT ESTABLISHED** |
| Future-schema published smoke | **NOT ESTABLISHED** |
| Keyboard behavior | **NOT ESTABLISHED** |
| Offline runtime | **NOT ESTABLISHED** |
| 900×600 visual | HUMAN_REQUIRED / not evidenced |
| 125% DPI | HUMAN_REQUIRED / not evidenced |
| 150% DPI | HUMAN_REQUIRED / not evidenced |

---

# 35. Finding counts for this pass

## New product-code defects

```text
Critical: 0
High:     0
Medium:   0
Low:      0
```

## Remaining release-verification findings

```text
Critical: 0
High:     1
Medium:   0
Low:      0
```

Remaining:

```text
PLH7-001 HIGH
PLH6-002 is only partially resolved because several mandatory non-visual
runtime release gates remain unclassified/unverified.
```

---

# 36. Final verdict

## PLH6-001

```text
RESOLVED
```

The tag/source mismatch is genuinely fixed.

## PLH6-002

```text
PARTIALLY RESOLVED
```

Visual QA is now correctly marked pending, but multiple mandatory functional runtime gates remain absent.

## PLH6-003

```text
RESOLVED AS A CLASSIFICATION DEFECT
```

Static privacy is correctly separated from offline runtime.

---

# 37. Overall conclusion

The latest repair is **substantially correct but not fully complete**.

The dangerous release-tag problem is fixed.

The product code remains clean.

The automated build/test claims remain plausible.

The final acceptance report, however, still cannot legitimately say:

```text
FULL RELEASE VALIDATION PASS
```

until the remaining functional runtime and visual/manual gates are explicitly classified and, where possible, executed.

The correct current high-level state is:

```text
STATIC SOURCE:
PASS

AUTOMATED REPOSITORY TESTING:
REPORTED PASS

RELEASE TAG / SOURCE PROVENANCE:
PASS

BASIC PUBLISHED BINARY SMOKE:
REPORTED PASS

STATIC PRIVACY:
PASS

FULL PUBLISHED FUNCTIONAL QA:
INCOMPLETE

FULL RELEASE VALIDATION:
NOT YET ACCEPTED
```

Do not change production code.

Complete the remaining runtime/UI/offline/visual gates and then perform one final acceptance-only audit.
