# Working on Prompt Helper

Notes for anyone - human or agent - running builds and tests on this project.

## Smart App Control: never debug a block as a test failure

This repository is developed on a Windows 11 machine with **Smart App Control enabled**
(`HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState = 1`).
Prompt Helper is an unsigned WPF application, so `PromptHelper.dll` is a GUI-subsystem
managed image (`IMAGE_FILE_DLL=false`, subsystem 2) - the shape code-integrity heuristics
treat most conservatively.

When the policy refuses to load it, `dotnet test` does not report "blocked". It reports
hundreds of unrelated test failures, each with a `FileLoadException`, or it aborts the test
host. **That looks exactly like a huge code regression and it is not one.**

The full field guide is at `C:\Projects\_SACsolutions.md`. The short version:

### Before believing any pile of test failures

```powershell
Get-WinEvent -FilterHashtable @{
    LogName='Microsoft-Windows-CodeIntegrity/Operational'; Id=3033,3077;
    StartTime=(Get-Date).AddMinutes(-15)
} -ErrorAction SilentlyContinue |
  Where-Object { $_.Message -match 'PromptHelper' } |
  Select-Object TimeCreated, Id, Message | Format-List
```

Filter by `PromptHelper`. This machine's CodeIntegrity log also carries blocks from
unrelated products (Playwright's browser binaries, for example); counting those as ours
reports a block that never happened.

Or grep the run output for the locale-independent HRESULT `0x800711C7`. Do not match on the
message text - on a German Windows it reads
`Eine Anwendungssteuerungsrichtlinie hat diese Datei blockiert.`

If either detector fires: **the tests did not run.** The failure count is meaningless. Do
not debug it, do not "fix" it, and do not revert anything.

### Use the wrapper instead of bare `dotnet test`

```powershell
powershell -NoProfile -File tools\RunTestsSacSafe.ps1 -Build
```

`tools/RunTestsSacSafe.ps1` settles after a build, runs a cheap canary first, applies both
detectors, retries with backoff, and exits with a code that cannot be confused with a real
failure:

| Exit | Meaning |
|---|---|
| 0 | the run completed and all tests passed |
| 1 | the run completed and there were **real** test failures |
| 42 | **environment block** - the tests did not run; report as "skipped - environment" |
| 43 | the `--filter` matched no tests; fix the filter, it is not a regression |
| 44 | inconclusive - timed out, or the exit code was unreadable |

During iteration prefer a targeted run, which is cheaper and provokes blocks less often:

```powershell
powershell -NoProfile -File tools\RunTestsSacSafe.ps1 -Filter "FullyQualifiedName~LibraryRepository"
```

Two details that matter on this multi-project solution:

- The **canary** runs against `tests/PromptHelper.Tests` alone. It exists to probe whether the
  loader trusts the Windows product and test assemblies, which is what a code-integrity policy
  refuses; the Core and Linux test projects contain no matching test.
- **"No test matches the filter"** is only treated as decisive when no project matched. A filter
  that legitimately selects tests in one project makes every other project print that message,
  so the wrapper compares the number of such messages against the number of test assemblies
  vstest reported. Treating the first occurrence as decisive made a passing run exit 43.

The block detector matches our assemblies **by prefix** (`PromptHelper*.dll|exe`), not an
enumerated list. An exhaustive list silently stopped covering the solution when it grew
`PromptHelper.Core`, `.Desktop` and `.Platform.Linux`: a block of `PromptHelper.Core.dll`
would have been reported as real test failures.

### Keep the number of rebuild/test cycles down

Sustained `rebuild -> launch test host` churn is what provokes a block. So:

- Do not re-run the full suite once it is green.
- Prefer one `dotnet test` that builds over separate build and test invocations.
- Run coverage once, at the end.

Builds are deterministic, so rebuilding unchanged source produces a byte-identical assembly
that reuses the verdict the policy already made. That creates a trap: changing something and
rebuilding can be blocked, reverting and rebuilding then "works" because it reproduced an
already-approved binary. **Never conclude that a code change caused a block on that
evidence.** Check the log.

### Never weaken the machine

Do not disable Smart App Control, Defender, WDAC or execution policy, do not self-sign, do
not `Unblock-File`, and do not copy or rename binaries to dodge a verdict (the verdict
follows content, not path - copying does not clear it). On Windows 11 Smart App Control can
only be re-enabled by reinstalling Windows; it is the machine owner's decision.

If tests genuinely cannot run, verify statically - `dotnet build`, the `tools/Verify*.ps1`
gates, structural assertions - and say plainly that the tests were blocked rather than
implying they passed.

### Measured on this machine (2026-10-22)

Smart App Control enforcing, host policy unchanged:

| Action | Result |
|---|---|
| `dotnet build PromptHelper.slnx -c Release` | clean, no block |
| `dotnet test` full suite (1080 tests across all four test projects) via the wrapper | clean, no block, zero 3033/3077 naming PromptHelper |
| `src\PromptHelper\bin\Release\net10.0-windows\PromptHelper.exe` launched directly | starts, WPF window created, zero 3033/3077 naming PromptHelper |

So the host test path is currently usable. Treat that as a measurement, not a guarantee: a
pass can be transient, and a newly built assembly may be refused later. Re-check the log
rather than assuming the last result still holds.

## Measured volume capabilities on this machine (2026-10-22)

Probed with `GetVolumeInformationW` plus the actual native queries against a real temporary
file on each volume:

| Volume | Filesystem | `FileIdInfo` (provenance) | `FileAttributeTagInfo` | `OPEN_BY_FILE_ID` | `REPARSE_POINTS` |
|---|---|---|---|---|---|
| C:, D:, F:, G: | NTFS | OK | OK | yes | yes |
| E: | exFAT | **fails, Win32 87** | **fails, Win32 87** | no | no |

`WindowsFileIdentity` is, by its own doc comment, "the only thing in the codebase that
constitutes provenance for a transaction artifact", and it needs `FileIdInfo`. So an exFAT
or FAT32 volume **cannot** host the data folder: without file IDs the owned-artifact journal
cannot prove it is acting on the object it created, and the CRUU15/16/20/21 durability
guarantees are unenforceable. This is architectural, not a defect to work around.

Verified end to end on the exFAT volume. `DataRootTopologyValidator.AssertVolumeSupportsDataRoot`
now refuses such a folder at selection time with `UnsupportedDataRootFilesystemException`,
which names the filesystem and the missing capability and tells the user to pick an NTFS or
ReFS drive. Measured on `E:\`: refused, no Win32 code or staging filename in the message,
nothing written to the drive, and an NTFS folder still accepted. See CRUU22-002 in
`cruu22.md`.

The verdict is empirical - a real file ID read from a real directory handle - never a
filesystem-name allowlist, which would both reject capable filesystems and admit incapable
ones. A non-capability error such as `ERROR_ACCESS_DENIED` fails closed as an inspection
failure rather than blaming the drive.

Do not try to make exFAT pass by relaxing the identity model, and do not conclude from the
case-sensitivity fix that exFAT is supported - that fix removed a wrong rejection reason
(and unblocks volumes such as network shares that lack `FileCaseSensitiveInfo` but do
support file IDs), not the filesystem requirement.

## Filesystem assumptions in tests

Do not hardcode a drive letter for "a drive that is not there". Use
`TestDrives.UnusedPath(...)`, which scans what is actually mounted. A test that assumed
`E:\` was absent instead hit a real exFAT volume and reported a case-insensitivity error
rather than the safety error it was written to prove - which is how the exFAT defect in
`WindowsDirectoryCaseSensitivityInspector` stayed hidden.

When a test needs a specific case-sensitivity answer, inject
`FakeDirectoryCaseSensitivityInspector` rather than letting the real inspector run against
whatever the developer has plugged in. Real filesystem behaviour belongs in
`CaseSensitivityFileSystemSupportTests`.

## Asserting that a transition failed cleanly

Ten product exception types derive from `IOException`, and `MigrationRollbackException`
means the opposite of a clean failure: rollback could not finish and residue was left
behind. `Assert.Throws<IOException>` accepts it, so a test that then asserts the residue is
gone fails on the residue line with no explanation.

Use `TransitionFailureAssert.CleanRollback(action, targetRoot)` where a clean rollback is the
point. It reports the rollback failures and lists the leftover residue.

## Repository gates

These run in CI and are worth running locally before finishing:

```powershell
powershell -NoProfile -File tools\VerifyFindingCoverage.ps1
powershell -NoProfile -File tools\VerifyReleaseAssets.ps1
powershell -NoProfile -File tools\VerifyIconGeneration.ps1
```
