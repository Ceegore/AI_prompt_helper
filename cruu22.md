# CRUU22 — Filesystem Capability Audit of the Data-Folder Admission Path

**Project:** Prompt Helper
**Repository:** `Ceegore/AI_prompt_helper`
**Audited branch:** `main`
**Parent / CRUU21 audit snapshot:** `b72e82b` — `fix(release): correct v0.1.2 version contract`
**Previous audit:** `cruu21.md`
**Audit date:** 2026-10-22
**Mode:** measured on-hardware audit of the data-folder admission path across real volumes of
differing filesystems (NTFS x4, exFAT x1), plus the native metadata queries that path depends
on. Smart App Control enforcing; host policy unchanged throughout.

> Findings below are measured on live hardware, not inferred from source reading. Every
> claim names the volume, the native call and the Win32 code it was observed with.

---

# 1. Executive verdict

Every CRUU1–CRUU21 finding concerned behaviour *within* a data folder the product had already
admitted: crash consistency, ownership provenance, marker authority, rollback completeness.
None of them asked whether the admission decision itself is correct.

It is not, on two counts, and both reduce to the same root error: **an NTFS-only native query
is treated as a universal capability, and its absence is reported as a failure of the folder
rather than as a property of the filesystem.**

The practical consequence is that Prompt Helper was unusable on exFAT and FAT32 volumes — USB
sticks, SD cards and most large external drives — and said so in language that identified
neither the cause nor the remedy.

CRUU22 found **2 actionable findings**:

```text
HIGH      = 1
MED-HIGH  = 1
TOTAL     = 2
```

Measured volume capabilities on the audit machine:

| Volume | Filesystem | `FileCaseSensitiveInfo` | `FileIdInfo` | `FileAttributeTagInfo` | `OPEN_BY_FILE_ID` | `REPARSE_POINTS` |
|---|---|---|---|---|---|---|
| C:, D:, F:, G: | NTFS | OK | OK | OK | yes | yes |
| E: | exFAT | **87** | **87** | **87** | no | no |

`87` is `ERROR_INVALID_PARAMETER`, returned for every directory on the volume including its
root. It is the filesystem reporting that the feature does not exist, not an error about the
directory.

---

# 2. Findings

## CRUU22-001 — HIGH — A filesystem without per-directory case sensitivity was rejected as uninspectable

**Component:** `WindowsDirectoryCaseSensitivityInspector.Inspect`

**Observed.** `GetFileInformationByHandleEx(FileCaseSensitiveInfo)` fails with Win32 87 for
every directory on the exFAT volume. The inspector treated any query failure as
`DirectoryCaseSensitivityInspectionException`. Both admission paths converted that into a
rejection:

- `DataRootTopologyValidator.ValidateTransition` →
  `InvalidOperationException: Failed to verify case-insensitivity for target directory 'E:\'`
- `ManagedDataRootPolicy.ValidateConfiguredRootForStartup` →
  `InvalidDataException: Failed to verify case-insensitivity for configured data folder …`

**Why it is wrong.** Per-directory case sensitivity is an NTFS/ReFS feature. A filesystem that
does not implement the query cannot hold a case-sensitive directory, so it already satisfies
the requirement the check exists to enforce. The check rejected precisely the volumes that are
guaranteed to comply, and the stated reason — a case-insensitivity problem — cannot occur
there.

**Severity rationale.** HIGH: a user selecting a folder on any exFAT/FAT32 volume is refused
outright with a false explanation. It also pre-empted
`ConfiguredDataFolderUnavailableException` at startup, so a folder on a present-but-empty
removable drive reported a case-sensitivity error instead of the dedicated safety error that
the UI is built to handle.

**Masking.** `ManagedDataRootPolicyTests.CRUU6_011` hardcoded `E:\Removable\CustomData` and ran
the real inspector, so on this machine it failed for an unrelated reason and the defect was
visible only as a confusing test failure. A test that encodes a drive-layout assumption cannot
be trusted to report the defect it was written to catch.

**Resolution.** The inspector now distinguishes "the feature is absent" from "the inspection
failed". On a feature-absent code (1, 50, 87) it confirms via `GetVolumeInformationByHandleW`
that the filesystem is not NTFS/ReFS and returns `CaseInsensitive`; on NTFS/ReFS, or when the
filesystem cannot be identified, it still fails closed. Startup then reaches the dedicated
unavailable-folder error as designed.

## CRUU22-002 — MED-HIGH — A volume without file identifiers was refused by a leaked native diagnostic

**Component:** admission path; surfaced from `WindowsOwnedDurableStage.AssertNonReparseAndUnderRoot`

**Observed.** With CRUU22-001 fixed, an exFAT folder passes topology and case-sensitivity
validation and then fails inside the write-capability probe:

```text
IOException: Unable to inspect staging file attributes for
'E:\…\.prompthelper-capability-3f03…-current.tmp'
  <-- Win32Exception(native=87): Falscher Parameter.
```

`FileIdInfo` also fails with 87 on that volume, and the volume reports no
`FILE_SUPPORTS_OPEN_BY_FILE_ID`. `WindowsFileIdentity` — by its own documentation "the only
thing in the codebase that constitutes *provenance* for a transaction artifact" — requires it.

**Why it is wrong.** The refusal is correct and the rollback is clean: the failed probe left
**0 residual entries** on the volume, verified independently. The defect is not the refusal but
its expression. The user sees an internal staging filename and a native error number, from
which nothing actionable follows. The check also runs late, inside a staging write, rather than
at the moment the folder is chosen.

A volume without file identifiers genuinely **cannot** host the data folder: without identity,
the owned-artifact journal cannot prove it is acting on the object it created, and the
CRUU15/16/20/21 durability guarantees are unenforceable. This is an architectural limit, not a
defect to engineer around. It must therefore be stated plainly rather than relaxed.

**Severity rationale.** MED-HIGH: no data is at risk and nothing is left behind, but the
product is unusable on a whole class of ordinary consumer drives and gives the user no path
forward. Reported as MED-HIGH rather than HIGH because the safety outcome is already correct.

**Resolution.** A dedicated admission gate,
`DataRootTopologyValidator.AssertVolumeSupportsDataRoot`, runs alongside the case-sensitivity
check in both admission paths and raises `UnsupportedDataRootFilesystemException`, which names
the filesystem, the missing capability, why it is required, and what to do instead. The
capability verdict is empirical — a real file ID read from a real directory handle — never a
filesystem-name allowlist, which would both reject capable filesystems and admit incapable
ones. A non-capability error such as `ERROR_ACCESS_DENIED` fails closed as an inspection
failure instead of blaming the drive. `App` and `SettingsDialog` handle the new exception
explicitly.

The contract (`IDataRootVolumeCapabilityInspector`, `DataRootVolumeCapabilities` and both
exceptions) lives in `PromptHelper.Core`, following `IDirectoryCaseSensitivityInspector`; the
Windows implementation stays in the WPF project. The platform-specific remedy - which
filesystems are affected and what to choose instead - is supplied by the caller rather than
baked into the core exception, because that is a platform fact, not a core one.

---

# 3. Non-findings

Recorded so later audits do not re-raise them:

- **`CA2000` handle warnings (28).** Reviewed individually; every path disposes or transfers
  ownership. False positives for the ownership-transfer pattern.
- **A failed capability probe on exFAT leaving residue.** Measured: 0 residual entries. The
  rollback path is correct.
- **Smart App Control blocking the suite.** Measured clean across the audit, with one logged
  3033/3077 against `PromptHelper.Tests.dll` during a run that nevertheless completed and
  passed. A logged refusal is not proof of a block; see `AGENTS.md`.

- **`LinuxDirectoryCaseSensitivityInspector` sharing the CRUU22-001 defect class.** Checked:
  it does not. It decides case sensitivity by *behaviour* - creating `…-A` and testing whether
  `…-a` resolves - rather than by a filesystem-specific metadata query, so there is no
  "feature absent" code to misclassify. The defect class is specific to querying an
  NTFS/ReFS-only interface and inferring a folder fault from its absence.

- **`LinuxFileIdentity` sharing the CRUU22-002 defect class.** Not reproduced here, and not
  expected: `st_dev`/`st_ino` are POSIX and available on every Linux filesystem Prompt Helper
  would run on, so there is no capability gap for an admission gate to report. The gate added
  for CRUU22-002 is consequently Windows-only, which is also where both admission paths
  (`DataRootTopologyValidator`, `ManagedDataRootPolicy`) live. If either path later moves into
  `PromptHelper.Core`, a Linux inspector must be supplied with it; the contract was placed in
  Core precisely so that remains possible without moving it again.
