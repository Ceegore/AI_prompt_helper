# Prompt Helper — Sixth Paranoid Audit (`_plh6.md`)

**Repository:** `Ceegore/AI_prompt_helper`  
**Current `main`:** `eff97e7e1636bee773406ee2a814ef314b652347`  
**Last clean product-source baseline:** `fb69b54973dbec7630f2cf47164bd9451fb0be19`  
**Release tag inspected:** `v0.1.0`  
**Release tag target resolved by GitHub comparison:** `c46419079eab56d0b66acf33e6e15d126b53d391`  
**Audit date:** 2026-08-20  
**Purpose:** Paranoid verification of the claimed final release-validation run and release integrity after `_plh_final_verification_concept.md`.

---

# 1. Executive verdict

## Product source

The production and test source on current `main` is unchanged from the fifth clean audit.

The only commit after the clean `fb69b549...` baseline is:

```text
eff97e7e1636bee773406ee2a814ef314b652347
docs: add final verification concept and execute validation suite
```

GitHub comparison shows the only changed file from `fb69b549...` to `eff97e7e...` is:

```text
_plh_final_verification_concept.md
```

Therefore:

```text
NEW PRODUCT-SOURCE REGRESSIONS:
0 confirmed
```

The previously clean static source conclusion still stands.

---

## Final-release verification

The claimed conclusion:

```text
"All verification phases ... have been executed."
"The codebase is verified, stable, and ready for release."
```

is **not supported by the available evidence and is not compliant with the verification concept**.

More importantly, the release itself has a source/tag integrity problem:

```text
v0.1.0 tag
→ c46419079eab56d0b66acf33e6e15d126b53d391
→ original implementation before four rounds of fixes
```

while the reported published executable identifies:

```text
ProductVersion:
0.1.0+fb69b54973dbec7630f2cf47164bd9451fb0be19
```

So the GitHub release tag/source archives and the uploaded binary do not represent the same source revision.

---

# 2. Severity summary

```text
Critical: 0
High:     2
Medium:   1
Low:      0
```

Findings:

```text
PLH6-001 HIGH
v0.1.0 release tag points to the original known-buggy commit, not the fixed binary source.

PLH6-002 HIGH
Full release PASS was claimed without evidence for mandatory GUI, clipboard,
DPI, keyboard, published persistence/recovery, and other release gates.

PLH6-003 MEDIUM
"Privacy & Offline PASS" was derived from a source-pattern scan rather than
the required offline runtime smoke.
```

---

# 3. PLH6-001 — HIGH — `v0.1.0` tag points to the original known-buggy implementation

## Confirmed tag target

A GitHub compare operation resolves:

```text
v0.1.0
```

as **identical** to:

```text
c46419079eab56d0b66acf33e6e15d126b53d391
```

That is the initial implementation commit:

```text
feat: implement Prompt Helper v0.1.0 (C# / WPF / .NET 10)
```

It is **not**:

```text
fb69b54973dbec7630f2cf47164bd9451fb0be19
```

and is **not**:

```text
eff97e7e1636bee773406ee2a814ef314b652347
```

---

## Why this is definitely not cosmetic

`_plh1.md` audited exactly:

```text
c46419079eab56d0b66acf33e6e15d126b53d391
```

and found:

```text
Critical: 0
High:     3
Medium:   7
Low:      4
Total:    14
```

The three High findings included:

```text
PLH-001
normal expected save/write failures could terminate the WPF application

PLH-002
backup failures during startup/initialization could be silently swallowed

PLH-003
File.Exists / Directory.Exists destroyed the required distinction between
missing files and access/I/O failure
```

Further confirmed issues included:

```text
zero-byte primary recovery defect
startup exception-boundary gaps
physical delete warning defect
destination-label collision defect
Unicode-name UI mismatch
overflow/UI issues
publish license/notices omission
sort-order defect
delete-confirmation order issue
wrong missing-file diagnostic
mutable-state leakage
```

Those defects were fixed only in later commits.

---

# 4. Direct source confirmation that the release tag is stale

The `v0.1.0` tag currently contains the old startup implementation.

For example, tagged `LibraryStartupService.cs` still does this:

```csharp
MetadataReadResult primaryResult = ReadMetadataState(...);

// backup is read unconditionally BEFORE the valid-primary branch
MetadataReadResult backupResult = ReadMetadataState(...);

if (primaryResult is MetadataReadResult.Valid primaryValid)
{
    try
    {
        _libraryRepo.SynchronizeBackup(primaryValid.Document);
    }
    catch
    {
        // Best effort backup sync
    }

    return new StartupResult(primaryValid.Document, false, null);
}
```

This is the pre-fix behavior.

Current `main` instead:

```text
reads primary

if primary is valid:
    resolves it immediately
    attempts backup synchronization
    converts backup failure to a warning
    returns primary

only reads backup if primary is corrupt/missing
```

That later behavior is the audited/fixed implementation.

So the Git tag is not merely missing documentation; it contains older production code.

---

# 5. Release-source consequence

GitHub automatically presents source archives for a release tag.

For the current:

```text
v0.1.0
```

those source archives correspond to:

```text
c464190...
```

not the fixed source that reportedly produced the uploaded executable.

Therefore a user who:

```text
downloads the GitHub "Source code (zip)"
downloads the GitHub "Source code (tar.gz)"
checks out v0.1.0
builds v0.1.0 from source
```

gets the old implementation with known defects.

This breaks:

```text
release provenance
source/binary correspondence
reproducibility
trust in the v0.1.0 tag
```

---

# 6. Binary/tag mismatch

The pasted verification report states:

```text
Release:
v0.1.0

Branch/main:
eff97e7...

Published PromptHelper.exe ProductVersion:
0.1.0+fb69b54973dbec7630f2cf47164bd9451fb0be19
```

The binary therefore reportedly identifies the final fixed product-source baseline:

```text
fb69b549...
```

That is reassuring for the **uploaded executable**.

But it confirms the mismatch:

```text
release tag source:
c464190...

binary source revision:
fb69b549...

current main:
eff97e7...
```

The three revisions are not the same.

The difference from `fb69b549` to `eff97e7` is documentation only, so that part is not a product-code defect.

The difference from `c464190` to `fb69b549` is substantial and contains all four audit repair rounds.

---

# 7. Required repair for PLH6-001

Do not leave the public release in this state.

## Preferred if `v0.1.0` is brand new and no external consumer has relied on the tag

Recreate the release/tag so that:

```text
v0.1.0
→ fb69b54973dbec7630f2cf47164bd9451fb0be19
```

because the uploaded binary reportedly identifies that exact product-source commit.

Then:

1. verify tag target;
2. rebuild from a clean checkout of that exact tag;
3. rerun Debug + Release tests;
4. publish from that tag;
5. verify `ProductVersion` source revision;
6. recreate ZIP from that publish folder;
7. replace release asset;
8. independently download release ZIP;
9. compute SHA-256;
10. verify EXE/version/notices inside the downloaded asset.

If the verification document itself is intended to be part of the release source, then tag a later exact commit and rebuild from that exact revision instead.

---

## Preferred if anyone may already have consumed `v0.1.0`

Do **not** silently mutate a tag people may have cached.

Safer:

```text
publish a new corrected release/tag
```

for example:

```text
v0.1.1
```

built from a clean fixed source revision.

Then make sure:

```text
tag commit
binary SourceRevisionId/ProductVersion commit
tested commit
release asset provenance
```

all agree.

Version/project metadata should be updated consistently if a new application version is used.

---

# 8. PLH6-002 — HIGH — Full release acceptance was falsely closed

The final verification concept intentionally distinguishes:

```text
A. Automated repository acceptance
B. Publish acceptance
C. Full product release acceptance
```

Full product release acceptance is allowed only when all of these are actually passed:

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

If visual/manual tests remain unresolved, the concept explicitly forbids a full release PASS and requires:

```text
AUTOMATED/MECHANICAL VALIDATION PASS
FINAL VISUAL QA PENDING
```

---

# 9. What the pasted release report actually demonstrates

The supplied report gives explicit evidence/claims for:

```text
Windows/.NET environment
dependency sanity
restore
Debug build
Release build
149 Debug tests
149 Release tests
publish
LICENSE
THIRD_PARTY_NOTICES
version inspection
basic EXE startup/close
single-instance enforcement
source-pattern privacy scan
release asset presence
```

Those are valuable and materially improve confidence.

I found no contradiction in the claim:

```text
149 passed
0 failed
0 skipped
```

and the named regression tests exist in the repository.

However, that is not the complete final QA matrix.

---

# 10. Mandatory gates not established by the supplied report

The report does **not** provide execution evidence for the following required full-product gates.

## Functional GUI workflows

No evidence is shown for published-EXE execution of:

```text
category create
nested category create
duplicate sibling rejection
category rename
non-empty category delete rejection
empty category confirmation + delete

prompt create
prompt edit
prompt delete
empty prompt
50k prompt

prompt move
prompt duplicate
Home prompt
nested prompt
```

A window successfully appearing is not equivalent to these flows passing.

---

## Restart persistence

No evidence is shown for:

```text
perform GUI mutations
close published app
restart published app
verify all mutations persisted
```

This is specifically required by the verification concept.

---

## Clipboard end-to-end

No evidence is shown for:

```text
click Copy in published app
read Windows clipboard
compare exact text

Markdown preserved
Unicode preserved
blank lines preserved
code fence preserved
no truncation

Copied ✓ appears
feedback resets to Copy
```

Unit/service tests do not replace the actual Windows clipboard path.

---

## Missing prompt GUI behavior

No published-GUI evidence is shown for:

```text
missing .md
→ unavailable card
→ Delete enabled
→ Move enabled
→ Edit disabled
→ Copy disabled
→ Duplicate disabled
```

---

## Orphan published behavior

No published-GUI evidence is shown for:

```text
valid metadata + arbitrary orphan .md
→ normal startup
→ orphan hidden
→ orphan retained
```

---

## Published corrupt-primary recovery

No isolated published-app evidence is shown for:

```text
valid primary + backup
corrupt primary
launch published EXE

→ recovery
→ warning
→ valid primary restored
→ prompt files preserved
→ recovery copy attempted
```

---

## Published double-corruption safety

No isolated published-app evidence is shown for:

```text
corrupt primary + corrupt backup
→ fatal startup
→ no defaults
→ prompt files unchanged
```

---

## Published future-schema safety

No isolated published-app evidence is shown for:

```text
schemaVersion 999 primary
valid old backup

→ fatal future schema
→ old backup NOT restored
→ future primary hash unchanged
```

This is an especially important data-safety smoke.

---

## Keyboard behavior

No evidence is shown for:

```text
Name dialog:
Enter submit
Escape cancel

Prompt editor:
Enter newline
Tab editor input
Escape cancel
Enter does NOT Save

Move:
Enter action
Escape cancel

main:
Tab / Shift+Tab reach controls
```

---

## DPI / layout

No evidence is shown for:

```text
900×600
100%
125%
150%

no clipped buttons
no overlaps
dialogs fit
breadcrumb usable
category cards usable
prompt list usable
```

This cannot be replaced by a successful build.

---

# 11. Correct status after the supplied evidence

Assuming the 149/149 local runs and basic executable checks are truthful, the strongest justified status from the supplied report is approximately:

```text
AUTOMATED REPOSITORY ACCEPTANCE:
PASS

PUBLISH BUILD ACCEPTANCE:
PASS

BASIC PROCESS SMOKE:
PASS

SINGLE INSTANCE:
PASS

FULL GUI FUNCTIONAL QA:
UNPROVEN

CLIPBOARD:
UNPROVEN

PUBLISHED PERSISTENCE:
UNPROVEN

PUBLISHED RECOVERY SAFETY:
UNPROVEN

KEYBOARD:
UNPROVEN

DPI / VISUAL:
UNPROVEN

OFFLINE RUNTIME:
UNPROVEN

RELEASE TAG/SOURCE INTEGRITY:
FAIL
```

Therefore:

```text
FULL RELEASE VALIDATION:
FAIL / NOT YET ACCEPTED
```

---

# 12. PLH6-003 — MEDIUM — "Privacy & Offline PASS" uses the wrong proof

The report says:

```text
Privacy & Offline
→ 0 intentional networking / analytics / telemetry / child processes
→ 0 pattern matches
→ PASS
```

This establishes a useful **static privacy scan**.

It does not establish the required:

```text
offline runtime functionality
```

The verification concept explicitly prefers:

```text
Windows Sandbox or VM
network disabled
```

followed by real workflows:

```text
startup
category create/rename/delete
prompt create/edit/delete
move
duplicate
clipboard
restart
```

A fallback socket inspection can provide weaker evidence if networking cannot be disabled.

A source grep is not the same test.

---

# 13. Why PLH6-003 matters

The source architecture strongly suggests the product is offline:

```text
no intentional networking
no application NuGet dependencies
no AI API
no WebView
no telemetry
```

So I do **not** currently suspect a hidden network feature.

The defect is in the verification conclusion:

```text
STATIC PRIVACY SCAN PASS
```

was incorrectly promoted to:

```text
OFFLINE RUNTIME PASS
```

Those are separate gates.

---

# 14. Required repair for PLH6-002 and PLH6-003

Run the missing tests from `_plh_final_verification_concept.md`.

Use:

```text
Windows Sandbox
disposable VM
or dedicated disposable Windows account
```

for destructive/recovery tests.

Do not use valuable `%LOCALAPPDATA%\PromptHelper` data.

At minimum complete:

```text
GUI create/rename/delete
GUI prompt CRUD
move
duplicate
restart persistence
clipboard exact equality
missing prompt
orphan
corrupt primary
double corruption
future schema
keyboard
offline runtime
900×600
125% DPI
150% DPI
```

Record each as exactly:

```text
PASS
FAIL
BLOCKED_ENVIRONMENT
HUMAN_REQUIRED
NOT_APPLICABLE
```

If the executor cannot visually inspect DPI/layout:

```text
HUMAN_REQUIRED
```

and final verdict must remain:

```text
AUTOMATED/MECHANICAL VALIDATION PASS
FINAL VISUAL QA PENDING
```

not:

```text
ready for release
```

---

# 15. Evidence provenance limitation

For current HEAD:

```text
GitHub combined commit statuses:
none

GitHub workflow runs:
none
```

The repository also does not expose a committed:

```text
FINAL_VALIDATION_REPORT.md
TRX logs
build logs
publish inspection logs
```

under `main`.

This does **not** prove the local commands were not run, because the verification concept deliberately stores evidence under:

```text
artifacts/
```

and `.gitignore` excludes:

```text
artifacts/
TestResults/
```

Therefore I do not promote the absence of GitHub logs to a product defect.

But it means this audit cannot independently authenticate:

```text
149/149 Debug
149/149 Release
command exit codes
local publish contents
local ZIP contents
```

from repository/CI evidence alone.

The supplied report remains the evidence for those claims.

---

# 16. Test-count claim

The reported:

```text
149 Debug tests
149 Release tests
0 failed
0 skipped
```

is plausible and consistent with the expanded test suite now present on `main`.

The named high-value regression tests are present, including:

```text
PLH004 zero-byte recovery
PLH004 whitespace recovery
PLH3001 non-IOException backup failure
PLH3001 GUID collision retries
PLH4001 destination full-suffix exhaustion
PLH4002 locked backup with valid primary
PLH2001 defensive clone behavior
failed atomic replacement target preservation
```

I found no source-level reason to reject the 149/149 claim.

The problem is that those tests do not replace the required published GUI/manual gates.

---

# 17. Current source re-audit result

Because production/test source is unchanged from the fifth clean baseline, I rechecked the most sensitive paths for latent regressions:

```text
startup classification
valid-primary precedence
backup warning semantics
future-schema safety
first-run/interrupted-init safety
prompt repository missing-vs-I/O classification
file deletion warning behavior
candidate-clone transactions
prompt create rollback
delete commit/cleanup ordering
move destination sorting
duplicate flow
GUID collision handling
destination disambiguation
UI mutation exception boundaries
dispatcher fatal shutdown
dialog semantics
MainViewModel refresh
accessibility basics
project/release metadata
```

No new product-source bug was confirmed.

So this audit should **not** trigger speculative production-code changes.

The required work is release/provenance/QA correction.

---

# 18. Paranoid release repair sequence

Use this order.

## Step 1 — fix release tag/source provenance

Choose one safe strategy:

```text
A. if release is effectively unused:
   recreate v0.1.0 at the exact fixed source commit

B. if release may have consumers:
   create a corrected new version/tag
```

Do not leave `v0.1.0` pinned to `c464190...`.

---

## Step 2 — build from the exact release tag

Use a clean checkout.

Record:

```text
git rev-parse HEAD
git describe --tags --exact-match
git status --short
```

Require:

```text
exact intended tag
clean tree
```

---

## Step 3 — rerun deterministic gates

```text
dotnet restore
Debug /warnaserror build
Debug tests
Release /warnaserror build
Release tests
publish win-x64 self-contained
```

Require all pass.

---

## Step 4 — prove binary provenance

Inspect:

```text
FileVersion
ProductVersion
```

Require the embedded source revision, when present, to match the intended release source commit.

Record:

```text
release tag
tag commit
tested commit
binary source commit
```

in one table.

---

## Step 5 — package

Create the ZIP only from the newly generated publish folder.

Verify:

```text
PromptHelper.exe
LICENSE
THIRD_PARTY_NOTICES.md
no test assemblies
```

Compute:

```text
SHA-256 ZIP
SHA-256 EXE
```

---

## Step 6 — upload

Upload/replace release asset as appropriate.

---

## Step 7 — download from GitHub again

Do not verify only the local pre-upload ZIP.

Download the exact public release asset.

Verify again:

```text
SHA-256
ZIP contents
EXE FileVersion
EXE ProductVersion
LICENSE
THIRD_PARTY_NOTICES
launch
```

This catches upload/asset mistakes.

---

## Step 8 — complete missing GUI/recovery/offline/visual gates

Follow `_plh_final_verification_concept.md`.

---

## Step 9 — only then issue final verdict

Allowed:

```text
FULL RELEASE VALIDATION PASS
```

only if every mandatory gate passed.

Otherwise use the exact reduced verdict required by the concept.

---

# 19. Recommended provenance table for the next report

Add:

| Item | Required value |
|---|---|
| Release tag | `v0.1.0` or corrected version |
| Tag commit | exact SHA |
| Tested commit | same exact SHA |
| Binary ProductVersion source SHA | same exact SHA when embedded |
| GitHub asset SHA-256 | recorded |
| Locally produced ZIP SHA-256 | equal to downloaded asset |
| Working tree | clean |
| Debug tests | PASS |
| Release tests | PASS |
| GUI QA | PASS / explicit pending |
| Offline | PASS / explicit blocked |
| DPI | PASS / explicit human pending |

This closes the provenance gap that the first final-verification concept did not explicitly test.

---

# 20. Improvement to the verification concept

This paranoid pass exposed one gap in `_plh_final_verification_concept.md` itself:

it did not explicitly require:

```text
release tag commit
==
binary source revision
==
tested source revision
```

That check should be added to future release procedures.

The process should always verify:

```text
git tag target
binary informational/product version SourceRevisionId
release asset built from exact tag
GitHub-downloaded asset hash
```

before release acceptance.

This is a verification-process improvement, not a product feature.

---

# 21. Final counts

```text
NEW PRODUCT CODE DEFECTS:
0

RELEASE / VERIFICATION DEFECTS:
3

Critical:
0

High:
2

Medium:
1

Low:
0
```

---

# 22. Final verdict

```text
CURRENT MAIN STATIC SOURCE:
PASS

AUTOMATED TEST CLAIMS:
PLAUSIBLE / NOT CONTRADICTED

DEBUG BUILD CLAIM:
PLAUSIBLE / NOT CONTRADICTED

RELEASE BUILD CLAIM:
PLAUSIBLE / NOT CONTRADICTED

PUBLISH CLAIM:
PLAUSIBLE / NOT CONTRADICTED

BASIC PROCESS SMOKE:
REPORTED PASS

SINGLE INSTANCE:
REPORTED PASS

FULL GUI QA:
NOT ESTABLISHED

CLIPBOARD:
NOT ESTABLISHED

PUBLISHED PERSISTENCE:
NOT ESTABLISHED

PUBLISHED RECOVERY:
NOT ESTABLISHED

KEYBOARD:
NOT ESTABLISHED

DPI / VISUAL:
NOT ESTABLISHED

OFFLINE RUNTIME:
NOT ESTABLISHED

RELEASE TAG / SOURCE INTEGRITY:
FAIL
```

## Overall

**The last task was not completed correctly as a final release verification.**

The executable may very well be the fixed `fb69b549...` build and the 149/149 test runs may be genuine.

But the public:

```text
v0.1.0
```

tag is definitely still the original:

```text
c464190...
```

known-buggy implementation, and the supplied report does not establish the mandatory full-product QA gates required by the verification concept.

Therefore:

```text
FULL RELEASE VALIDATION:
NOT ACCEPTED
```

Do not change stable product code speculatively.

Fix release provenance, execute/evidence the remaining mandatory GUI/recovery/offline/visual gates, and then rerun the final release acceptance check.
