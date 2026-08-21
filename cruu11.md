# CRUU11 — Post-CRUU10 Re-Audit, Remaining-Defect Register & Final Weak-AI Repair Blueprint

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited commit:** `cca54bc1ed79cc69e60342e865573c16c77f9950`  
**Previous implementation baseline:** `be1da4fa49916a102616f82a6c74f5601ab5d2d6`  
**Audit date:** 2026-08-21  

This document independently validates the CRUU9/CRUU10 implementation. It preserves fixes that actually landed, identifies all currently substantiated remaining/new defects, and gives a weak implementing AI exact repair architecture, copy-ready code, tests, fault injection, sequencing, CI gates, and acceptance evidence.

---

# 1. Executive verdict

The CRUU10 implementation commit is substantial and fixes many earlier defects. It must **not** be reverted wholesale.

Current pushed `main`:

```text
cca54bc1ed79cc69e60342e865573c16c77f9950
```

The commit message claims **453 passing tests**. This audit did not independently reproduce that Windows/.NET test result. The available GitHub combined-status query returned no status entries, and the connector's commit-workflow lookup returned no PR-triggered runs. Those observations do not prove whether push CI ran.

```text
CRUU9 / CRUU10 IMPLEMENTATION            = SUBSTANTIAL BUT INCOMPLETE
AUDITED HEAD                             = cca54bc1ed79cc69e60342e865573c16c77f9950
SOURCE-LEVEL CRUU11 AUDIT                = COMPLETE
CRUU11 REMAINING / NEW FINDINGS          = 27
CRUU11 CRITICAL FINDINGS                 = 1
CRUU11 HIGH FINDINGS                     = 5
INDEPENDENT WINDOWS/.NET EXECUTION       = NOT AVAILABLE HERE
ZERO-DEFECT ACCEPTANCE                   = NOT GRANTED
STRICT RELEASE                           = BLOCKED
```

The remaining problems cluster around:

```text
1. native handle/path containment and lifetime;
2. source/target terminal authority at ReadyToCommit;
3. crash consistency of ordinary Create/Edit/Duplicate/Delete operations;
4. crash-temp/control ownership and cleanup;
5. test/CI/release evidence still weaker than the claims.
```

---

# 2. Preserve these fixes

The weak implementation AI must preserve these improvements:

```text
- migration manifest schema v3;
- AttemptId-bound payload temp grammar;
- one final/temp/control ownership namespace;
- deterministic manifest phase staging path;
- deterministic migration capability-probe plan;
- strict migration JSON member validation and strict migration UTF-8 decode;
- exact root-level migration/.app.lock recognition in recovery;
- prompts/recovery target baseline existence flags;
- handle-bound hash verification before final-artifact deletion;
- target junction/reparse validation;
- physical target revalidation in transition coordinator;
- write-through settings primary save;
- primary+backup settings CAS under one lease;
- settings transition precondition captured after recovery writes;
- native reservation directory ownership and cleanup reporting;
- CommitRootOwnership for committed new target directories;
- package-health inspection before startup backup synchronization;
- package-health inspection before backup recovery;
- strict settings/library unknown/duplicate member validation;
- 160 text-element prompt-title cap;
- narrowed GetPrompts filesystem exception handling;
- enum-only case-sensitivity inspector;
- SettingsDialog postcommit RestartRequired monotonicity;
- MainWindow shutdown independent of DialogResult;
- real junction tests for migration paths;
- TRX verifier now fails when a required sentinel is absent;
- CI explicitly runs CrashRecovery, WpfIntegration,
  WindowsFilesystemIntegration, and full-suite jobs.
```

---

# 3. Finding register

| ID | Severity | Finding |
|---|---|---|
| CRUU11-001 | **CRITICAL** | `WindowsVerifiedArtifactDeleter` uses boundary-unsafe `StartsWith(root)` containment after following the opened file; a prefix-collision path can be treated as inside the root, enabling deletion outside the bound root after a reparse/path substitution |
| CRUU11-002 | **HIGH** | Ordinary prompt mutation journal/recovery was not implemented; Create/Edit/Duplicate remain dependent on in-process catch rollback and are inconsistent after process/power loss |
| CRUU11-003 | MED-HIGH | Safe orphan reconciliation was not implemented; conservative delete leftovers can persist indefinitely and are intentionally copied by later migrations |
| CRUU11-004 | **HIGH** | `ManagedDataRootSessionLease` exists but is never retained by `App`; ordinary runtime prompt reads/writes are not protected from later `prompts`/`recovery` node replacement |
| CRUU11-005 | **HIGH** | `DataRootTopologyValidator.FindNearestExistingDirectory` catches access failures and walks to a parent, recreating Missing-vs-Unreadable fail-open behavior |
| CRUU11-006 | MED-HIGH | `WindowsPhysicalPathResolver` probes a directory and then reopens it by pathname, leaving a check/open TOCTOU; the new strict directory opener is not used |
| CRUU11-007 | MED | `ManagedTreeTopologyValidator` silently accepts `prompts`/`recovery` when the path is a file by treating wrong type like missing |
| CRUU11-008 | MED-HIGH | Managed child directories are not checked for Windows per-directory case sensitivity |
| CRUU11-009 | **HIGH** | `MigrationReadyGate` ignores its source snapshot and the manifest lacks a complete source-package fingerprint; source stability is not re-proven immediately before Ready/settings commit |
| CRUU11-010 | MED-HIGH | Ready/startup authority exempts `ManifestPhaseStaging`; declared stage residue can survive marker retirement, and Ready does not reject all newly introduced foreign target entries |
| CRUU11-011 | MED | Retry recovery retires the ownership marker before attempt-created `prompts`/`recovery` directory cleanup; cleanup then occurs outside the lease and exceptions are swallowed |
| CRUU11-012 | MED | New-attempt target baseline is captured before an older interrupted migration is recovered, so new manifest baseline flags can describe old-attempt residue |
| CRUU11-013 | MED-HIGH | Planned capability-probe directories use `Directory.CreateDirectory` and are claimed as owned even if another actor created the exact declared directory first |
| CRUU11-014 | MED | The random/unplanned capability-probe fallback remains available for non-manifest paths and can leave unjournaled residue on process death |
| CRUU11-015 | LOW-MED | `PrepareTargetForMigrationUnitTest` remains a real copy/capability path that bypasses durable manifest publication and coordinator safety sequencing |
| CRUU11-016 | MED-HIGH | Settings Save uses the durable settings writer, but settings backup synchronization/recovery still use generic `AtomicTextWriter`; crash temp grammars diverge and dedicated cleanup cannot recognize all settings temps |
| CRUU11-017 | MED-HIGH | `SettingsTempName.TryParse` accepts arbitrary target basenames, so cleanup can delete foreign `.prompthelper-settings-<anything>-<GUID>.tmp` files and recovery can over-recognize them |
| CRUU11-018 | MED-HIGH | Settings, library metadata, and prompt bodies still use permissive .NET UTF-8 decoding; malformed UTF-8 can be replacement-decoded then rewritten instead of rejected |
| CRUU11-019 | MED | Generic `AtomicTextWriter` crash temps for library metadata, prompt bodies, recovery copies, and initialization have no centralized reconciler / exact owned-temp namespace |
| CRUU11-020 | MED | Migration reserved-name policy remains basename-wide rather than exact root-relative; legitimate nested recovery/orphan data such as `recovery\settings.json` can be rejected |
| CRUU11-021 | MED-HIGH | `PromptRepository.Create` performs check-then-write but final promotion uses `MOVEFILE_REPLACE_EXISTING`; a file created after the check can be overwritten |
| CRUU11-022 | LOW-MED | `DuplicatePrompt` catches all exceptions around body read and converts them to `InvalidOperationException`, masking programming/system failures |
| CRUU11-023 | LOW-MED | Backup synchronization still accepts a bare `LibraryDocument`; the new package-health safety ordering is enforced by convention rather than API/type authority |
| CRUU11-024 | MED verification gap | CRUU10-004/005 crash/orphan tests are absent; the CRUU10 session-lease test never attempts node replacement; access-denied, strict-UTF8 and programmer-exception tests are incomplete |
| CRUU11-025 | MED verification gap | TRX verifier still accepts substring matches, and CI lacks dedicated PackageIntegrity, MutationRecovery, and FilesystemAuthority gates |
| CRUU11-026 | MED release gap | `CompareIconIdentity.ps1` compares raw ICO frame bytes only and does not extract/compare the published EXE icon; release verification still proves only icon presence |
| CRUU11-027 | RELEASE BLOCKER | Approved `PromptHelperLogo.svg` and real generated product ICO remain absent |

---

# 4. Mandatory repair order

```text
PHASE 00  baseline + source map
PHASE 01  handle/path primitives
PHASE 02  verified deletion containment
PHASE 03  strict ancestor/physical resolver
PHASE 04  managed tree + application-lifetime lease
PHASE 05  strict UTF-8 + shared durable writer/temp namespace
PHASE 06  settings writer unification
PHASE 07  migration manifest schema v4 compatibility layer
PHASE 08  Ready/source authority
PHASE 09  recovery terminal cleanup
PHASE 10  capability-probe ownership
PHASE 11  remove safety bypasses
PHASE 12  library mutation journal + recovery
PHASE 13  safe orphan reconciler
PHASE 14  repository/API hardening
PHASE 15  verification/CI
PHASE 16  release identity
PHASE 17  five-run regression + publish + final source audit
```

Do not implement the mutation journal before the durable writer exists.

Do not bump migration schema without a safe schema-v3 recovery path.

Do not auto-delete orphan data until primary, backup and active journal authority are known.



---

# 5. CRUU11-001 — Critical verified-deletion containment escape

**Severity:** CRITICAL

## 5.1 Failure mechanism

The current verified deleter does the right thing in one respect: it opens one handle, hashes the object represented by that handle, and marks that same handle for deletion.

The containment test is still wrong:

```csharp
if (!normalizedHandlePath.StartsWith(
        normalizedPhysicalRoot,
        StringComparison.OrdinalIgnoreCase))
{
    throw ...
}
```

This is not a path-component-aware containment test.

Example:

```text
bound root     C:\Data
opened file    C:\DataOutside\important.bin
```

The second string begins with `C:\Data`.

Because the current `CreateFileW` call follows reparse points, an interrupted
migration path can be replaced with a link to such a prefix-collision sibling.
If the outside object happens to match the manifest length/hash, recovery can
verify and delete foreign data.

This is a deletion-boundary defect, not merely a diagnostic defect.

## 5.2 Adjacent defects in the same helper

Fix these in the same phase:

```text
- GetFinalPathNameByHandleW buffer is fixed at 1024 chars;
- required-size retry is absent;
- "\\?\UNC\server\share" is not normalized correctly;
- opened reparse objects are not rejected;
- containment logic is duplicated instead of using PathIdentity.
```

## 5.3 Add `WindowsFinalPathHelper.cs`

```csharp
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

internal static class WindowsFinalPathHelper
{
    private const uint FILE_NAME_NORMALIZED = 0x0;
    private const uint VOLUME_NAME_DOS = 0x0;

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    public static string GetNormalizedDosPath(
        SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (handle.IsInvalid)
        {
            throw new ArgumentException(
                "Handle is invalid.",
                nameof(handle));
        }

        int capacity = 512;

        while (true)
        {
            var buffer =
                new StringBuilder(capacity);

            uint result =
                GetFinalPathNameByHandleW(
                    handle,
                    buffer,
                    (uint)capacity,
                    FILE_NAME_NORMALIZED |
                    VOLUME_NAME_DOS);

            if (result == 0)
            {
                throw new IOException(
                    "GetFinalPathNameByHandleW failed.",
                    new Win32Exception(
                        Marshal.GetLastWin32Error()));
            }

            // API returns required buffer size when current
            // buffer is too small.
            if (result >= capacity)
            {
                capacity =
                    checked((int)result + 1);
                continue;
            }

            string raw =
                buffer.ToString();

            string dosPath;

            if (raw.StartsWith(
                    @"\\?\UNC\",
                    StringComparison.OrdinalIgnoreCase))
            {
                dosPath =
                    @"\\" + raw.Substring(8);
            }
            else if (raw.StartsWith(
                         @"\\?\",
                         StringComparison.Ordinal))
            {
                dosPath = raw.Substring(4);
            }
            else
            {
                dosPath = raw;
            }

            return PathIdentity
                .NormalizeForComparison(dosPath);
        }
    }

    public static void AssertStrictDescendantFile(
        string physicalRoot,
        string finalFilePath)
    {
        string root =
            PathIdentity.NormalizeForComparison(
                physicalRoot);

        string file =
            PathIdentity.NormalizeForComparison(
                finalFilePath);

        // A file artifact can never equal the data-root directory.
        if (!PathIdentity.IsStrictDescendant(
                file,
                root))
        {
            throw new InvalidDataException(
                $"Opened artifact resolved outside the " +
                $"bound data root. Root='{root}', " +
                $"File='{file}'.");
        }
    }
}
```

## 5.4 Harden `WindowsVerifiedArtifactDeleter`

Add:

```csharp
private const uint FILE_FLAG_OPEN_REPARSE_POINT =
    0x00200000;

private const uint FILE_ATTRIBUTE_REPARSE_POINT =
    0x00000400;

private const int FileAttributeTagInfo = 9;

[StructLayout(LayoutKind.Sequential)]
private struct FILE_ATTRIBUTE_TAG_INFO
{
    public uint FileAttributes;
    public uint ReparseTag;
}

[DllImport(
    "kernel32.dll",
    SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
private static extern bool
    GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        int fileInformationClass,
        out FILE_ATTRIBUTE_TAG_INFO
            fileInformation,
        uint bufferSize);
```

Open the object itself rather than blindly following a reparse target:

```csharp
using SafeFileHandle handle =
    CreateFileW(
        path,
        GENERIC_READ | DELETE,
        FILE_SHARE_NONE,
        IntPtr.Zero,
        OPEN_EXISTING,
        FILE_FLAG_OPEN_REPARSE_POINT,
        IntPtr.Zero);
```

After missing/error handling:

```csharp
if (!GetFileInformationByHandleEx(
        handle,
        FileAttributeTagInfo,
        out FILE_ATTRIBUTE_TAG_INFO tagInfo,
        (uint)Marshal.SizeOf<
            FILE_ATTRIBUTE_TAG_INFO>()))
{
    throw new IOException(
        $"Unable to inspect opened artifact " +
        $"attributes for '{path}'.",
        new Win32Exception(
            Marshal.GetLastWin32Error()));
}

if ((tagInfo.FileAttributes &
     FILE_ATTRIBUTE_REPARSE_POINT) != 0)
{
    throw new InvalidDataException(
        $"Recovery refuses to delete reparse " +
        $"artifact '{path}'.");
}

string finalPath =
    WindowsFinalPathHelper
        .GetNormalizedDosPath(handle);

WindowsFinalPathHelper
    .AssertStrictDescendantFile(
        physicalRoot,
        finalPath);
```

Then perform:

```text
length check
hash check
SetFileInformationByHandle(FileDispositionInfo)
```

on the same handle.

## 5.5 Do not use these "fixes"

Forbidden:

```csharp
finalPath.StartsWith(root + "\\")
```

and:

```csharp
Path.GetFullPath(finalPath)
    .StartsWith(Path.GetFullPath(root))
```

Use `PathIdentity.IsStrictDescendant`.

Do not simply remove `GetFinalPathNameByHandleW`; same-handle physical
identity is valuable.

## 5.6 Required tests

Mandatory unit tests:

```text
CRUU11_001_Prefix_collision_C_Data_vs_C_DataOutside_is_rejected
CRUU11_001_Strict_descendant_file_is_accepted
CRUU11_001_UNC_final_path_prefix_normalizes_correctly
CRUU11_001_Buffer_resize_retries_when_API_returns_required_size
```

Mandatory Windows test:

```text
CRUU11_001_Reparse_artifact_is_rejected_before_deletion
```

Preservation:

```text
external object bytes identical after failure
migration marker remains
```

---

# 6. CRUU11-005 / 006 — One strict directory-handle authority

Fix these together.

## 6.1 The wrong pattern still present

Topology currently does:

```text
Probe(path)
catch access failure
walk to parent
```

Physical resolver does:

```text
Probe(path)
then CreateFileW(path)
```

The first is fail-open.

The second has a check/open race.

## 6.2 Replace with one strict opener

Create/keep:

```csharp
internal enum DirectoryOpenState
{
    Missing,
    Opened
}

internal sealed record DirectoryOpenResult(
    DirectoryOpenState State,
    SafeFileHandle? Handle);

internal interface IStrictDirectoryOpener
{
    DirectoryOpenResult OpenDirectoryStrict(
        string path);

    SafeFileHandle OpenManagedNodeLease(
        string path);
}
```

Production rules:

```text
ERROR_FILE_NOT_FOUND / ERROR_PATH_NOT_FOUND => Missing
sharing/access/security/path-device errors    => throw
Opened                                        => return actual handle
```

No boolean existence probe first.

## 6.3 Replace nearest existing ancestor search

```csharp
public static string
    FindNearestExistingDirectoryStrict(
        string path,
        IStrictDirectoryOpener opener)
{
    string current =
        Path.GetFullPath(path);

    while (true)
    {
        DirectoryOpenResult result =
            opener.OpenDirectoryStrict(current);

        if (result.State ==
            DirectoryOpenState.Opened)
        {
            result.Handle!.Dispose();
            return current;
        }

        string trimmed =
            current.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        string? parent =
            Path.GetDirectoryName(trimmed);

        if (string.IsNullOrEmpty(parent) ||
            PathIdentity.Equals(
                parent,
                current))
        {
            throw new DirectoryNotFoundException(
                $"No accessible existing directory " +
                $"ancestor exists for '{path}'.");
        }

        current = parent;
    }
}
```

No catch block is allowed to translate access failure into parent traversal.

## 6.4 Refactor physical resolver to use the returned handle

```csharp
public string ResolveWithNearestExistingAncestor(
    string path)
{
    string full =
        Path.GetFullPath(path);

    string current = full;

    var suffix =
        new Stack<string>();

    while (true)
    {
        DirectoryOpenResult result =
            _directoryOpener
                .OpenDirectoryStrict(current);

        if (result.State ==
            DirectoryOpenState.Opened)
        {
            using SafeFileHandle handle =
                result.Handle!;

            string resolved =
                WindowsFinalPathHelper
                    .GetNormalizedDosPath(handle);

            while (suffix.Count > 0)
            {
                resolved =
                    Path.Combine(
                        resolved,
                        suffix.Pop());
            }

            return Path.GetFullPath(resolved);
        }

        string trimmed =
            current.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        string name =
            Path.GetFileName(trimmed);

        if (string.IsNullOrEmpty(name))
        {
            throw new DirectoryNotFoundException(
                $"No accessible ancestor for '{full}'.");
        }

        suffix.Push(name);

        string? parent =
            Path.GetDirectoryName(trimmed);

        if (string.IsNullOrEmpty(parent) ||
            PathIdentity.Equals(parent, current))
        {
            throw new DirectoryNotFoundException(
                $"No accessible ancestor for '{full}'.");
        }

        current = parent;
    }
}
```

Delete the resolver's independent second `CreateFileW` path after this lands.

## 6.5 Tests

```text
CRUU11_005_Access_denied_ancestor_is_not_skipped
CRUU11_005_Missing_child_walks_to_parent
CRUU11_006_Resolver_uses_the_handle_returned_by_strict_opener
CRUU11_006_Resolver_does_not_reopen_authority_path
CRUU11_006_Missing_suffix_is_appended_after_physical_resolution
```

Use fake opener callbacks to prove no second name lookup.

---

# 7. CRUU11-007 / 008 — Managed tree required type + child case policy

## 7.1 Required validator semantics

Use two modes.

```csharp
internal enum ManagedTreeValidationMode
{
    PreCreation,
    RuntimeRequired
}
```

For each child:

```text
Missing + PreCreation     => allowed
Missing + RuntimeRequired => error
File                      => error
Directory                 => continue checks
```

Then require:

```text
no ReparsePoint attribute
physical path equals exact expected child
case-sensitivity state == CaseInsensitive
```

Copy-ready core:

```csharp
private void ValidateChild(
    string physicalRoot,
    string childName,
    ManagedTreeValidationMode mode)
{
    string child =
        Path.Combine(
            physicalRoot,
            childName);

    StrictPathProbe probe =
        _paths.Probe(child);

    switch (probe.Kind)
    {
        case StrictPathKind.Missing:
            if (mode ==
                ManagedTreeValidationMode
                    .PreCreation)
            {
                return;
            }

            throw new DirectoryNotFoundException(
                $"Required managed directory is " +
                $"missing: '{child}'.");

        case StrictPathKind.File:
            throw new InvalidDataException(
                $"Managed path must be a directory: " +
                $"'{child}'.");

        case StrictPathKind.Directory:
            break;

        default:
            throw new InvalidOperationException(
                $"Unexpected path state: " +
                $"{probe.Kind}.");
    }

    if ((probe.Attributes!.Value &
         FileAttributes.ReparsePoint) != 0)
    {
        throw new InvalidDataException(
            $"Managed directory must not be a " +
            $"reparse point: '{child}'.");
    }

    string physicalChild =
        _resolver.ResolveWithNearestExistingAncestor(
            child);

    if (!PathIdentity.Equals(
            physicalChild,
            child))
    {
        throw new InvalidDataException(
            $"Managed directory resolves to " +
            $"unexpected physical path. " +
            $"Expected='{child}', " +
            $"Actual='{physicalChild}'.");
    }

    if (_caseInspector.Inspect(child) ==
        DirectoryCaseSensitivityState
            .CaseSensitive)
    {
        throw new InvalidDataException(
            $"Managed directory is case-sensitive: " +
            $"'{child}'.");
    }
}
```

## 7.2 Required tests

```text
CRUU11_007_Prompts_file_is_rejected
CRUU11_007_Recovery_file_is_rejected
CRUU11_007_Runtime_missing_prompts_is_rejected
CRUU11_008_Case_sensitive_prompts_is_rejected
CRUU11_008_Case_sensitive_recovery_is_rejected
CRUU11_008_Case_query_failure_is_not_assumed_insensitive
```

At least one real Windows case-sensitive child test belongs in
`WindowsFilesystemIntegration`.

---

# 8. CRUU11-004 — Make the managed tree lease truly process-lifetime

## 8.1 Current lease is useful but scoped too narrowly

Do not delete `ManagedDataRootSessionLease`.

Change how it is wired.

## 8.2 `App.xaml.cs`

Add:

```csharp
private AppInstanceLock? _appLock;
private ManagedDataRootSessionLease?
    _managedTreeLease;
```

After migration recovery and after ordinary managed directories have been
created and runtime-validated:

```csharp
_managedTreeLease =
    ManagedDataRootSessionLease.Acquire(
        paths.RootDirectory);
```

Keep it alive while:

```text
library mutation recovery runs
startup library package is loaded
orphan reconciliation runs
MainWindow exists
all normal prompt CRUD happens
```

## 8.3 Exit

```csharp
protected override void OnExit(
    ExitEventArgs e)
{
    _managedTreeLease?.Dispose();
    _managedTreeLease = null;

    _appLock?.Dispose();
    _appLock = null;

    base.OnExit(e);
}
```

## 8.4 Lease acquisition must require the children

At application-lifetime acquisition:

```csharp
foreach (string path in new[]
{
    physicalRoot,
    Path.Combine(physicalRoot, "prompts"),
    Path.Combine(physicalRoot, "recovery")
})
{
    StrictPathProbe probe =
        authority.Probe(path);

    if (probe.Kind ==
        StrictPathKind.Missing)
    {
        throw new DirectoryNotFoundException(
            $"Managed session directory missing: " +
            $"'{path}'.");
    }

    if (probe.Kind !=
        StrictPathKind.Directory)
    {
        throw new InvalidDataException(
            $"Managed session path is not a " +
            $"directory: '{path}'.");
    }

    handles.Add(
        opener.OpenManagedNodeLease(path));
}
```

## 8.5 Real test must attempt mutation

The current double-acquisition test is not sufficient.

```csharp
[TestMethod]
[TestCategory("WindowsFilesystemIntegration")]
[TestCategory("FilesystemAuthority")]
public void
CRUU11_004_Prompts_directory_rename_fails_while_session_lease_held()
{
    using var temp =
        new TestDirectory();

    string root =
        temp.Root;

    string prompts =
        Path.Combine(root, "prompts");

    string recovery =
        Path.Combine(root, "recovery");

    Directory.CreateDirectory(prompts);
    Directory.CreateDirectory(recovery);

    using var lease =
        ManagedDataRootSessionLease
            .Acquire(root);

    Assert.ThrowsException<IOException>(
        () => Directory.Move(
            prompts,
            prompts + "-moved"));

    Assert.IsTrue(
        Directory.Exists(prompts));
}
```

Add same for `recovery`.

---

# 9. CRUU11-009 — Migration source package fingerprint and final Ready recheck

## 9.1 Why the current source check is not enough

`CopySnapshotToTarget` rechecks source content after copy.

Then capability validation runs.

Then Ready is written.

The Ready gate receives the original snapshot but does not use it.

There is therefore another source-mutation window.

## 9.2 Schema v4

Change:

```csharp
public const int CurrentSchemaVersion = 4;
```

Add:

```csharp
public string
    SourcePayloadFingerprintSha256Hex
        { get; set; } = string.Empty;
```

Do not delete schema-v3 recovery support. Section 18 defines compatibility.

## 9.3 Compute one full-payload fingerprint

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

internal static class
    MigrationPayloadFingerprint
{
    public static string Compute(
        IEnumerable<MigrationPayloadFile> files)
    {
        using IncrementalHash hash =
            IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);

        foreach (MigrationPayloadFile file in
                 files
                    .OrderBy(
                        x => x.RelativePath,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        x => x.RelativePath,
                        StringComparer.Ordinal))
        {
            Append(hash, file.RelativePath);
            hash.AppendData([0]);

            Append(
                hash,
                ((int)file.Role).ToString(
                    CultureInfo.InvariantCulture));
            hash.AppendData([0]);

            Append(
                hash,
                file.Length.ToString(
                    CultureInfo.InvariantCulture));
            hash.AppendData([0]);

            hash.AppendData(file.Sha256);
            hash.AppendData([0]);
        }

        return Convert.ToHexStringLower(
            hash.GetHashAndReset());
    }

    private static void Append(
        IncrementalHash hash,
        string text)
    {
        hash.AppendData(
            Encoding.UTF8.GetBytes(text));
    }
}
```

## 9.4 Builder

```csharp
manifest.SourcePayloadFingerprintSha256Hex =
    MigrationPayloadFingerprint.Compute(
        snapshot.Files);
```

Strict validator:

```text
64 hexadecimal characters
```

## 9.5 Ready gate final source recheck

The Ready gate must be able to ask the migration service for a fresh source
snapshot/fingerprint.

Better API:

```csharp
_readyGate.AssertReady(
    sourcePhysicalRoot:
        runtime.ActivePhysicalRoot,
    targetPhysicalRoot:
        bound.PhysicalRoot,
    manifest,
    originalSnapshot);
```

Inside:

```csharp
MigrationPayloadSnapshot fresh =
    _migrationService
        .CaptureSourcePayloadSnapshot(
            sourcePhysicalRoot);

string freshFingerprint =
    MigrationPayloadFingerprint
        .Compute(fresh.Files);

if (!string.Equals(
        freshFingerprint,
        manifest
            .SourcePayloadFingerprintSha256Hex,
        StringComparison.OrdinalIgnoreCase))
{
    throw new IOException(
        "Source library changed before " +
        "ReadyToCommit.");
}
```

This re-enumerates:

```text
primary
backup if payload-eligible
active prompt bodies
orphans
recovery artifacts
```

exactly according to migration snapshot policy.

## 9.6 Retry context

Add:

```csharp
string?
    ExpectedSourcePayloadFingerprint
```

For a retry initiated by the active old root:

```text
root match AND payload fingerprint match
```

are required before deleting old-attempt target artifacts.

## 9.7 Tests

```text
CRUU11_009_Source_active_body_changes_after_copy_aborts_Ready
CRUU11_009_Source_orphan_appears_after_copy_aborts_Ready
CRUU11_009_Source_recovery_file_changes_after_copy_aborts_Ready
CRUU11_009_Retry_same_root_changed_payload_fails_closed
CRUU11_009_Stable_payload_fingerprint_allows_Ready
```

---

# 10. CRUU11-010 — Ready terminal inventory and staging control

## 10.1 All declared controls must be absent before stage creation

Ready gate:

```text
final artifacts exact
declared payload temps absent
capability files absent
capability directories (until removed by CRUU11-013) absent
manifest stage absent
no unknown target entries
source fingerprint still exact
managed tree still valid
```

Do not exempt `ManifestPhaseStaging`.

## 10.2 Then Ready writer owns the stage lifecycle

```text
ReadyGate confirms stage Missing
WriteReadyManifestDurable creates exact stage
Flush(true)
MoveFileEx(REPLACE_EXISTING | WRITE_THROUGH)
stage is consumed
```

## 10.3 Committed startup

If Ready marker exists and stage also exists:

```text
stage is exact AttemptId-owned control
delete it strictly
re-inventory
require all ephemeral controls absent
then retire marker
```

Do not simply ignore it.

## 10.4 Unknown inventory

Create shared:

```text
MigrationTargetInventoryInspector.cs
```

Use it in:

```text
ReadyGate
Retry recovery before cleanup
Retry recovery after cleanup
Committed startup
```

Unknown entries before Ready:

```text
abort before settings commit
```

## 10.5 Tests

```text
CRUU11_010_Ready_gate_rejects_existing_stage
CRUU11_010_Ready_gate_rejects_new_root_foreign_file
CRUU11_010_Ready_gate_rejects_new_nested_foreign_file
CRUU11_010_Committed_startup_removes_exact_owned_stage_before_marker
CRUU11_010_Committed_startup_does_not_ignore_stage_residue
```

---

# 11. CRUU11-011 / 012 — Recovery marker retirement and fresh baseline

## 11.1 Marker must be last operation in retry cleanup

Current recovery sequence effectively verifies files, retires marker, then
attempts to remove attempt-created directories outside the lease.

Required retry order:

```text
1 strict-read marker
2 validate target/source authority
3 acquire recovery managed-tree lease
4 inventory
5 delete exact controls
6 delete exact payload temps
7 verified-delete exact finals
8 remove attempt-created prompts/recovery directories
9 rebuild complete inventory
10 verify pre-existing dirs preserved
11 verify all attempt-created dirs absent
12 verify no unknowns
13 delete marker LAST
14 verify marker Missing
15 release lease
```

No cleanup after marker retirement.

## 11.2 Directory cleanup failures are not best effort

Do not:

```csharp
try
{
    Directory.Delete(...)
}
catch
{
}
```

If an attempt-created directory cannot be removed:

```text
recovery fails
marker remains
exact cleanup error reported
```

## 11.3 Build the new attempt manifest after old recovery

Current empty transition builds `manifest` before target reservation/old
attempt recovery.

Required sequence:

```text
capture source snapshot
acquire target reservation
revalidate target locator
if old manifest exists:
    recover old attempt fully
reinspect target as strict Empty
capture NEW target baseline now
allocate new AttemptId
build new manifest from this clean baseline
verify all planned final/temp/control names available
publish Copying marker
```

The new manifest must not inherit baseline flags from old-attempt directories.

## 11.4 Tests

```text
CRUU11_011_Directory_cleanup_failure_preserves_marker
CRUU11_011_No_attempt_cleanup_occurs_after_marker_retirement
CRUU11_011_Attempt_created_prompts_removed_before_marker
CRUU11_012_New_manifest_baseline_captured_after_old_attempt_cleanup
CRUU11_012_Old_attempt_created_prompts_is_not_marked_preexisting_in_new_manifest
```



---

# 12. CRUU11-013 / 014 — Capability probe ownership

## 12.1 Current planned-probe defect

The new planned probe is a major improvement because its paths are declared
by the migration manifest.

However `ProbeLocationWithPlan` still does:

```csharp
Directory.CreateDirectory(probeDir);
dirCreated = true;
journal?.TrackCreatedDirectory(probeDir);
```

`Directory.CreateDirectory` succeeds when the directory already exists.

Therefore:

```text
manifest declares probe dir
external actor creates exact dir before ProbeLocationWithPlan
Prompt Helper calls CreateDirectory
Prompt Helper marks dirCreated=true
Prompt Helper later deletes what it did not create
```

A manifest declaration proves **reserved path intent**.

It does not prove that Prompt Helper won the object-creation race.

## 12.2 Best fix: eliminate probe directories

A directory is not required to prove create/write/replace ability.

For the empty-target migration, use exact manifest-declared files.

Root:

```text
.prompthelper-probe-<attemptIdN>-root-current.tmp
.prompthelper-probe-<attemptIdN>-root-replacement.tmp
```

Prompts:

```text
prompts\.prompthelper-probe-<attemptIdN>-prompts-current.tmp
prompts\.prompthelper-probe-<attemptIdN>-prompts-replacement.tmp
```

Each is created with `FileMode.CreateNew`.

CreateNew provides ownership proof:

```text
success          => Prompt Helper owns this exact file
already exists   => collision; abort; DO NOT delete collision file
```

## 12.3 New plan model

```csharp
internal sealed record
    CapabilityFileProbePlan(
        string CurrentRelativePath,
        string ReplacementRelativePath);

internal sealed record
    MigrationCapabilityProbePlan(
        CapabilityFileProbePlan RootProbe,
        CapabilityFileProbePlan
            PromptsProbe)
{
    public static
        MigrationCapabilityProbePlan Create(
            Guid attemptId)
    {
        return new(
            new CapabilityFileProbePlan(
                $".prompthelper-probe-" +
                $"{attemptId:N}-root-current.tmp",
                $".prompthelper-probe-" +
                $"{attemptId:N}-root-replacement.tmp"),
            new CapabilityFileProbePlan(
                Path.Combine(
                    "prompts",
                    $".prompthelper-probe-" +
                    $"{attemptId:N}-prompts-current.tmp"),
                Path.Combine(
                    "prompts",
                    $".prompthelper-probe-" +
                    $"{attemptId:N}-prompts-replacement.tmp")));
    }
}
```

Manifest controls:

```text
four CapabilityProbeFile entries
no CapabilityProbeDirectory entries in schema v4 writes
```

The v3 reader must still understand legacy probe directories for recovery.

## 12.4 Probe sequence

```text
CreateNew current
write "create"
Flush(true)

CreateNew replacement
write "replace"
Flush(true)

replace replacement -> current
delete current

both exact control paths Missing
```

Because both paths were declared before creation, a crash after successful
CreateNew is recoverable.

If CreateNew collides:

```text
abort
do not mark that path owned
do not auto-delete it
```

### Ownership subtlety

The v4 manifest already *reserves* the path, but a collision after marker
publication may be foreign.

Recovery must not delete a declared probe path merely because it appears.

For capability probe controls, persist an ownership phase.

Preferred schema-v4 addition:

```csharp
public List<MigrationControlArtifact>
    AcquiredControls { get; set; } = [];
```

That creates more manifest rewrites.

A simpler robust option is:

```text
do not publish the Copying manifest until:
    - target reservation acquired;
    - all planned probe paths confirmed Missing.

Then, because target reservation locks Prompt Helper instances but cannot
lock arbitrary external writers, CreateNew remains final ownership proof.
If CreateNew collides, abort and leave manifest. On retry, the collision
must be treated as unknown/foreign unless an acquisition marker proves
ownership.
```

The cleanest solution is therefore to **avoid leaving the marker on a probe
creation collision** if no attempt-owned payload/control has yet been created:

```text
strictly verify no attempt-owned objects exist
delete manifest
preserve collision object
abort
```

Once any attempt object exists, marker stays.

Implement this explicitly; do not make recovery infer.

## 12.5 Existing-target / non-manifest probe

The old `ProbeLocation` random-directory fallback remains.

Replace it with one reserved file grammar:

```text
.prompthelper-capability-<guidN>-current.tmp
.prompthelper-capability-<guidN>-replacement.tmp
```

at the probed directory.

At beginning of capability validation:

```text
clean only exact stale Prompt Helper capability files
with this grammar
after strict managed-tree containment
```

No random probe directory.

On next selection of the same target, residue is reconcilable.

## 12.6 Tests

```text
CRUU11_013_Preexisting_planned_probe_collision_is_never_deleted
CRUU11_013_CreateNew_success_establishes_probe_ownership
CRUU11_013_Crash_after_probe_current_is_recoverable
CRUU11_013_Crash_after_probe_replacement_is_recoverable
CRUU11_014_Existing_target_probe_uses_reserved_reconcilable_file_grammar
CRUU11_014_Existing_target_stale_probe_is_cleaned_on_next_validation
CRUU11_014_Similar_foreign_probe_name_is_preserved
```

---

# 13. CRUU11-015 — Remove the manifest-bypass unit helper

Current internal helper:

```text
PrepareTargetForMigrationUnitTest
```

does real copy/capability work without the durable manifest/coordinator
sequence.

This means the test suite has two architectures:

```text
production safe path
unit-test convenience path
```

That is dangerous because future tests can pass against a helper that no
longer represents production.

## 13.1 Required change

Preferred:

```text
delete PrepareTargetForMigrationUnitTest
```

Move narrow pure operations to explicit helpers:

```text
InspectTarget
CaptureSourcePayloadSnapshot
CopySnapshotToTarget
```

For full transition tests:

```text
instantiate DataFolderTransitionCoordinator
```

Do not preserve a "unit test" method that performs a manifestless migration.

## 13.2 Test

Reflection/source-level:

```text
CRUU11_015_No_manifestless_PrepareTargetForMigrationUnitTest_API_remains
```

More importantly, migrate every old test using it to production coordinator
or pure helpers.

---

# 14. CRUU11-016 / 017 — One settings durability/temp authority

## 14.1 Current split

`SaveCore` correctly uses:

```text
WindowsDurableSettingsFileWriter
```

for primary and backup.

But `LoadOrRecoverCore` still uses generic:

```text
IAtomicTextWriter _writer
```

when:

```text
- synchronizing settings.backup.json from valid primary;
- restoring settings.json from valid backup.
```

The two writers create different temp grammars.

Dedicated cleanup only recognizes:

```text
.prompthelper-settings-*.tmp
```

Generic AtomicTextWriter can leave:

```text
.settings.json.<guidN>.tmp
.settings.backup.json.<guidN>.tmp
```

after a crash.

## 14.2 Rule

Every settings mutation must use one writer:

```text
IDurableSettingsFileWriter
```

Remove generic `_writer` from `AppSettingsRepository`.

Constructor:

```csharp
public AppSettingsRepository(
    string? settingsPathOverride = null,
    string? backupPathOverride = null,
    SettingsLeasePolicy? leasePolicy = null,
    IDurableSettingsFileWriter?
        durableWriter = null)
{
    _durableWriter =
        durableWriter ??
        new WindowsDurableSettingsFileWriter();

    ...
}
```

Update:

```text
valid-primary backup sync
backup-to-primary recovery
SaveCore primary
SaveCore backup
```

to the same durable writer.

## 14.3 Temporary compatibility constructor

If too many tests inject `IAtomicTextWriter`, update the tests.

Do not keep a production constructor that silently ignores or routes an old
writer differently.

## 14.4 Fix `SettingsTempName.TryParse`

Current parser accepts any target name.

It must accept exactly:

```text
settings.json
settings.backup.json
```

Copy-ready:

```csharp
public static bool TryParse(
    string fileName,
    out string targetFileName)
{
    targetFileName = string.Empty;

    if (string.IsNullOrWhiteSpace(fileName) ||
        !fileName.StartsWith(
            Prefix,
            StringComparison.OrdinalIgnoreCase) ||
        !fileName.EndsWith(
            Suffix,
            StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    string middle =
        fileName.Substring(
            Prefix.Length,
            fileName.Length -
            Prefix.Length -
            Suffix.Length);

    int separator =
        middle.LastIndexOf('-');

    if (separator <= 0)
    {
        return false;
    }

    string target =
        middle.Substring(
            0,
            separator);

    string nonce =
        middle.Substring(
            separator + 1);

    if (nonce.Length != 32 ||
        !Guid.TryParseExact(
            nonce,
            "N",
            out _))
    {
        return false;
    }

    if (!string.Equals(
            target,
            "settings.json",
            StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(
            target,
            "settings.backup.json",
            StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    targetFileName = target;
    return true;
}
```

## 14.5 Settings temps are bootstrap-only controls

Migration recovery must classify them as persistent/owned controls **only**
when:

```csharp
context.IsExactBootstrapRoot
```

A file named:

```text
.prompthelper-settings-settings.json-<guid>.tmp
```

inside a normal custom data root is foreign unless another exact authority
claims it.

## 14.6 Legacy temp cleanup

Because the current shipped code may have created generic old settings temps,
support exact legacy patterns at bootstrap:

```text
.settings.json.<guidN>.tmp
.settings.backup.json.<guidN>.tmp
```

Create:

```csharp
internal static bool
    TryParseLegacySettingsTemp(
        string fileName)
```

with exact filenames.

Do not use wildcard deletion.

## 14.7 Tests

```text
CRUU11_016_Settings_primary_recovery_uses_durable_settings_writer
CRUU11_016_Settings_backup_sync_uses_durable_settings_writer
CRUU11_016_No_generic_AtomicTextWriter_remains_in_AppSettingsRepository
CRUU11_017_Parser_rejects_foreign_target_basename
CRUU11_017_Parser_accepts_only_primary_and_backup_settings
CRUU11_017_Foreign_prompthelper_settings_lookalike_is_preserved
CRUU11_017_Settings_temp_is_control_only_at_physical_bootstrap_root
CRUU11_017_Legacy_exact_settings_temp_is_reconciled_at_bootstrap
```

---

# 15. CRUU11-018 — Strict UTF-8 authority for all user-data text

## 15.1 Why this remains open

Migration manifest decoding is strict.

Settings/library/prompt reads still use APIs such as:

```csharp
File.ReadAllText(path)
```

with default UTF-8 behavior.

Malformed bytes can be replacement-decoded.

That creates an unacceptable authority flow:

```text
malformed bytes
    ↓
replacement character string
    ↓
valid JSON / prompt text
    ↓
rewritten as different canonical UTF-8 bytes
```

The application should reject malformed UTF-8 rather than silently mutate it.

## 15.2 Create `StrictUtf8Text.cs`

Support optional UTF-8 BOM for legacy compatibility, but no UTF-16/32.

```csharp
using System;
using System.IO;
using System.Text;

namespace PromptHelper.Services;

internal static class StrictUtf8Text
{
    private static readonly UTF8Encoding Encoding =
        new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    public static string Decode(
        ReadOnlySpan<byte> bytes,
        string description)
    {
        try
        {
            ReadOnlySpan<byte> payload = bytes;

            if (payload.Length >= 3 &&
                payload[0] == 0xEF &&
                payload[1] == 0xBB &&
                payload[2] == 0xBF)
            {
                payload = payload[3..];
            }

            return Encoding.GetString(payload);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException(
                $"Invalid UTF-8 in {description}.",
                ex);
        }
    }

    public static string ReadAllText(
        string path,
        string description)
    {
        byte[] bytes =
            File.ReadAllBytes(path);

        return Decode(bytes, description);
    }

    public static byte[] Encode(
        string text) =>
        Encoding.GetBytes(text);
}
```

## 15.3 Settings ordering

Important future-schema rule:

```text
strict bytes decode
parse JsonDocument enough to inspect schemaVersion
if schemaVersion > current:
    throw UnsupportedSettingsSchemaException BEFORE rejecting future unknown members
if current:
    enforce strict exact member set
deserialize
```

Do not make a future file fail merely as "unknown property" before future
schema precedence is known.

Same rule for library metadata.

## 15.4 Apply to

```text
AppSettingsRepository.ReadState
LibraryRepository.ReadMetadataFileState
LibraryRepository recovery-copy parsing
PromptRepository.Read
LibraryPackageInspector body verification
target metadata inspection
```

## 15.5 Prompt package health

A prompt body with malformed UTF-8 is:

```text
BodyUnreadable / BodyInvalidEncoding
```

not Healthy.

Add explicit state if useful:

```csharp
public sealed record BodyInvalidEncoding(...)
```

## 15.6 Tests use raw invalid bytes

Example:

```csharp
byte[] invalid =
[
    (byte)'{',
    (byte)'"',
    (byte)'x',
    (byte)'"',
    (byte)':',
    (byte)'"',
    0xC3,
    0x28,
    (byte)'"',
    (byte)'}'
];
```

Required:

```text
CRUU11_018_Invalid_UTF8_settings_is_rejected
CRUU11_018_Invalid_UTF8_library_is_rejected
CRUU11_018_Invalid_UTF8_prompt_body_is_not_Healthy
CRUU11_018_UTF8_BOM_current_files_remain_readable
CRUU11_018_Future_schema_precedence_survives_strict_UTF8_validation
```

---

# 16. CRUU11-019 — Shared durable writer + crash-temp reconciliation

## 16.1 Current AtomicTextWriter is only half the solution

It now:

```text
CreateNew temp
write
Flush(true)
MoveFileEx(REPLACE_EXISTING | WRITE_THROUGH)
```

That fixed final-promotion durability.

But a process death before promotion can leave:

```text
.library.json.<guid>.tmp
.<promptGuid>.md.<guid>.tmp
.initializing.marker.<guid>.tmp
recovery staging temp
```

There is no central startup reconciler.

## 16.2 Introduce one writer contract

```csharp
internal interface IDurableAtomicFileWriter
{
    void ReplaceDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass);

    void CreateNewDurable(
        string targetPath,
        ReadOnlySpan<byte> bytes,
        DurableFileClass fileClass);
}

internal enum DurableFileClass
{
    Settings,
    LibraryMetadata,
    PromptBody,
    RecoveryArtifact,
    InitializationControl,
    MigrationControl,
    MutationControl
}
```

The `CreateNewDurable` operation is required by CRUU11-021.

## 16.3 Exact new temp grammar

Use product-reserved names:

```text
.prompthelper-tmp-settings-<guidN>.tmp
.prompthelper-tmp-library-<guidN>.tmp
.prompthelper-tmp-prompt-<guidN>.tmp
.prompthelper-tmp-recovery-<guidN>.tmp
.prompthelper-tmp-init-<guidN>.tmp
.prompthelper-tmp-migration-<guidN>.tmp
.prompthelper-tmp-mutation-<guidN>.tmp
```

The directory context plus class defines expected ownership.

New code should not emit old `.<target>.<guid>.tmp`.

## 16.4 `ReplaceDurable`

```csharp
public void ReplaceDurable(
    string targetPath,
    ReadOnlySpan<byte> bytes,
    DurableFileClass fileClass)
{
    string temp =
        CreateOwnedTempPath(
            targetPath,
            fileClass);

    bool promoted = false;

    try
    {
        using (var stream =
               new FileStream(
                   temp,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        MoveReplaceWriteThrough(
            temp,
            targetPath);

        promoted = true;
    }
    finally
    {
        if (!promoted)
        {
            BestEffortDeleteExactOwnedTemp(
                temp);
        }
    }
}
```

## 16.5 `CreateNewDurable`

Same temp write, but final move uses:

```text
MOVEFILE_WRITE_THROUGH
WITHOUT MOVEFILE_REPLACE_EXISTING
```

If target exists at final promotion:

```text
throw
preserve target
cleanup temp
```

This is the required atomic create/no-overwrite primitive.

## 16.6 Startup temp reconciler

Create:

```text
DurableTempReconciler.cs
```

Run under:

```text
settings lease for bootstrap settings temps
app lock + managed-tree lease for active data-root temps
```

Only delete exact parser-approved Prompt Helper temp names.

Never:

```text
*.tmp => delete
```

## 16.7 Legacy current-version temp grammar

Support exact legacy cleanup for:

```text
.library.json.<guidN>.tmp
.library.backup.json.<guidN>.tmp
.initializing.marker.<guidN>.tmp
prompts\.<promptGuidN>.md.<guidN>.tmp
```

For recovery artifacts, only clean exact known Prompt Helper recovery
destination grammars.

Unknown legacy `.tmp` is preserved.

## 16.8 Tests

```text
CRUU11_019_ReplaceDurable_write_through_promotes
CRUU11_019_CreateNewDurable_never_replaces_existing_target
CRUU11_019_Exact_new_temp_grammar_reconciles
CRUU11_019_Foreign_tmp_is_preserved
CRUU11_019_Legacy_exact_library_temp_reconciles
CRUU11_019_Legacy_exact_prompt_temp_reconciles
```

---

# 17. CRUU11-020 — Root-relative control policy, not basename policy

## 17.1 Current issue

Manifest path validation still rejects paths based on basename:

```text
settings.json
settings.backup.json
.app.lock...
.settings.lock...
```

at arbitrary nesting depth.

But migration intentionally carries:

```text
recovery files
orphan prompt files
```

A nested recovery file called:

```text
recovery\settings.json
```

is not the bootstrap root settings authority.

## 17.2 One policy helper

```csharp
internal static class ManagedControlPathPolicy
{
    public static bool IsReservedRootControl(
        string relativePath,
        bool targetIsBootstrapRoot)
    {
        string p =
            NormalizeRelative(relativePath);

        if (p.Contains(
            Path.DirectorySeparatorChar))
        {
            return false;
        }

        if (EqualsName(p, ".app.lock") ||
            EqualsName(
                p,
                ".prompthelper-migration.json") ||
            EqualsName(
                p,
                ".prompthelper-library-mutation.json") ||
            EqualsName(
                p,
                "initializing.marker"))
        {
            return true;
        }

        if (targetIsBootstrapRoot &&
            (EqualsName(p, ".settings.lock") ||
             EqualsName(p, "settings.json") ||
             EqualsName(
                 p,
                 "settings.backup.json")))
        {
            return true;
        }

        return false;
    }

    private static bool EqualsName(
        string a,
        string b) =>
        string.Equals(
            a,
            b,
            StringComparison.OrdinalIgnoreCase);
}
```

Manifest artifacts are validated with the manifest's physical target +
bootstrap context.

## 17.3 Tests

```text
CRUU11_020_Root_settings_json_is_reserved_only_at_bootstrap
CRUU11_020_Recovery_settings_json_is_not_root_control
CRUU11_020_Nested_app_lock_name_is_not_root_app_lock
CRUU11_020_Source_snapshot_and_manifest_validator_share_same_control_policy
```



---

# 18. Migration schema-v3 compatibility while writing schema v4

Do not strand interrupted migrations created by the current audited build.

The current build writes schema 3.

CRUU11 should write schema 4.

Reader policy:

```text
schema > 4:
    fail future/unsupported
schema == 4:
    strict v4 parser
schema == 3:
    strict legacy-v3 parser for RECOVERY ONLY
schema < 3:
    fail unsupported legacy state
```

## 18.1 Do not rewrite a v3 marker before recovery

A crash marker is itself evidence.

Read it.

Validate it.

Recover it under the strongest compatible rules.

Do not first replace it with a v4 marker.

## 18.2 Derive the missing v4 source fingerprint for v3

Schema 3 already contains artifact hashes/lengths/roles.

Compute:

```csharp
string legacyFingerprint =
    MigrationPayloadFingerprint
        .ComputeFromManifestArtifacts(
            v3.Artifacts);
```

For retry:

```text
expected active source root must match v3 SourcePhysicalRoot
fresh active source payload fingerprint must match the fingerprint derived
from v3 artifacts
```

This gives schema-v3 retry the same practical source-content protection
without changing the on-disk marker.

For committed startup:

```text
source root need not exist
validate target finals + controls + baseline only
```

## 18.3 v3 staging residue

The current v3 manifest declares `ManifestPhaseStaging`.

Under CRUU11:

```text
Copying:
    exact declared stage may be cleaned as attempt-owned

ReadyToCommit:
    exact declared stage must be deleted/reconciled before marker retirement
```

Do not retain the old v3 exemption.

## 18.4 Tests

```text
CRUU11_V3COMPAT_Schema3_Copying_retry_recovers
CRUU11_V3COMPAT_Schema3_same_root_changed_source_refuses_cleanup
CRUU11_V3COMPAT_Schema3_Ready_with_stage_reconciles_stage_before_marker
CRUU11_V3COMPAT_Schema3_future_unknown_schema_is_not_modified
```

---

# 19. CRUU11-021 — Prompt create must be true create-no-overwrite

**Severity:** MED-HIGH

## 19.1 Current race

Current conceptual flow:

```text
PromptRepository.Exists(id)
    ↓
AtomicTextWriter.Write(path, body)
    ↓
MoveFileEx(... REPLACE_EXISTING ...)
```

If the body appears between the check and final promotion:

```text
Prompt Helper overwrites it.
```

The GUID collision probability is tiny.

That is not the only threat.

External repair/sync/user action can also create a file in the window.

## 19.2 Use `CreateNewDurable`

`PromptRepository.Create` becomes:

```csharp
public void Create(
    Guid id,
    string content)
{
    if (id == Guid.Empty)
    {
        throw new ArgumentException(
            "Prompt ID cannot be empty.",
            nameof(id));
    }

    ArgumentNullException.ThrowIfNull(content);

    byte[] bytes =
        StrictUtf8Text.Encode(content);

    _durableWriter.CreateNewDurable(
        _paths.GetPromptPath(id),
        bytes,
        DurableFileClass.PromptBody);
}
```

No pre-existence check is needed for correctness.

A caller may probe earlier for a friendly error, but final authority is the
no-overwrite promotion.

## 19.3 GUID generation

When choosing a new GUID:

```text
metadata set does not contain GUID
strict prompt path is Missing
```

If prompt path is unreadable:

```text
do not assume free
```

Then final create still uses no-overwrite.

## 19.4 Tests

Deterministic writer seam:

```text
after GUID check but before final promotion create a foreign destination
```

Assert:

```text
operation fails
foreign destination exact bytes preserved
metadata unchanged
mutation journal remains/recovery safely resolves
```

Named:

```text
CRUU11_021_Foreign_body_created_after_GUID_check_is_not_overwritten
CRUU11_021_CreateNewDurable_collision_preserves_existing_bytes
```

---

# 20. CRUU11-022 — Narrow `DuplicatePrompt` read exception handling

**Severity:** LOW-MED

Current Duplicate catches broadly around prompt read.

Use the same expected-filesystem filter as `GetPrompts`:

```csharp
try
{
    body =
        _promptRepo.Read(promptId);
}
catch (Exception ex) when (
    ex is IOException or
    UnauthorizedAccessException or
    SecurityException)
{
    throw new InvalidOperationException(
        "The prompt body could not be read " +
        "for duplication.",
        ex);
}
```

Do not catch arbitrary `Exception`.

Tests:

```text
CRUU11_022_Duplicate_IO_error_becomes_user_operation_error
CRUU11_022_Duplicate_programmer_exception_propagates
```

Use an injected repository seam for the second.

---

# 21. CRUU11-023 — Compile-time package-health guard for backup sync

**Severity:** LOW-MED

Startup currently calls backup sync in the right order after package
inspection.

Make it difficult for future code to call it incorrectly.

## 21.1 Add strong type

```csharp
internal sealed record HealthyLibraryPackage(
    LibraryDocument Document,
    IReadOnlyDictionary<Guid,
        PromptBodySnapshot> Bodies);
```

`LibraryPackageInspector.Healthy` should expose this.

## 21.2 Change backup API

Preferred:

```csharp
internal CommitResult SynchronizeBackup(
    HealthyLibraryPackage package)
{
    return SynchronizeBackupCore(
        package.Document);
}
```

Make bare-document sync:

```text
private
```

or at most:

```text
internal Core helper used only by transaction code that has another proof
```

No public/internal general bare `LibraryDocument` entry point.

## 21.3 Mutation journal interaction

After Create/Edit/Delete metadata/body reach a proven committed state:

```text
build/inspect HealthyLibraryPackage
sync backup from that package
```

For Delete, body may be intentionally preserved as orphan; package health
only requires bodies still referenced by the new primary.

## 21.4 Tests

```text
CRUU11_023_Startup_backup_sync_requires_HealthyLibraryPackage
CRUU11_023_Incomplete_package_cannot_be_passed_to_backup_sync_API
```

The second may be a compile/reflection API-shape test plus runtime package
test.

---

# 22. Full library mutation journal implementation

This section is mandatory for CRUU11-002.

## 22.1 Files

Create:

```text
src/PromptHelper/Services/
    LibraryMutationJournal.cs
    LibraryMutationJournalRepository.cs
    LibraryMutationRecoveryService.cs
    PromptMutationCoordinator.cs
    MutationContentClassifier.cs
```

Modify:

```text
AppPaths.cs
App.xaml.cs
PromptLibraryService.cs
PromptRepository.cs
LibraryRepository.cs
LibraryStartupService.cs
```

## 22.2 AppPaths

```csharp
public string LibraryMutationJournalPath =>
    Path.Combine(
        RootDirectory,
        ".prompthelper-library-mutation.json");
```

Recovery body helper:

```csharp
public string GetMutationRecoveryBodyPath(
    Guid operationId,
    Guid promptId) =>
    Path.Combine(
        RecoveryDirectory,
        $"mutation-{operationId:N}-" +
        $"old-{promptId:N}.md");
```

## 22.3 Journal exact members

Allowed/required JSON root:

```text
schemaVersion
operationId
kind
phase
promptId
bodyRelativePath
oldLibrarySha256Hex
newLibrarySha256Hex
oldBodyLength
oldBodySha256Hex
newBodyLength
newBodySha256Hex
recoveryBodyRelativePath
```

Strict duplicate/unknown member checks.

Strict UTF-8.

Undefined enum => invalid.

Relative body path must equal:

```text
prompts\<promptIdN>.md
```

Recovery path, when present, must equal:

```text
recovery\mutation-<operationIdN>-old-<promptIdN>.md
```

No arbitrary journal paths.

## 22.4 Journal repository

```csharp
internal sealed class
    LibraryMutationJournalRepository
{
    private readonly AppPaths _paths;
    private readonly IDurableAtomicFileWriter
        _writer;
    private readonly StrictPathAuthority
        _strictPaths;

    public LibraryMutationJournalRepository(
        AppPaths paths,
        IDurableAtomicFileWriter writer,
        StrictPathAuthority? strictPaths = null)
    {
        _paths = paths;
        _writer = writer;
        _strictPaths =
            strictPaths ??
            new StrictPathAuthority();
    }

    public LibraryMutationJournal?
        TryReadStrict()
    {
        StrictPathProbe state =
            _strictPaths.Probe(
                _paths.LibraryMutationJournalPath);

        if (state.Kind ==
            StrictPathKind.Missing)
        {
            return null;
        }

        if (state.Kind !=
            StrictPathKind.File)
        {
            throw new InvalidDataException(
                "Library mutation journal path " +
                "is not a file.");
        }

        string json =
            StrictUtf8Text.ReadAllText(
                _paths.LibraryMutationJournalPath,
                "library mutation journal");

        LibraryMutationJournal journal =
            ParseValidate(json);

        return journal;
    }

    public void CreatePreparedDurable(
        LibraryMutationJournal journal)
    {
        if (journal.Phase !=
            LibraryMutationPhase.Prepared)
        {
            throw new InvalidOperationException(
                "New journal must begin in Prepared.");
        }

        byte[] bytes =
            SerializeValidate(journal);

        _writer.CreateNewDurable(
            _paths.LibraryMutationJournalPath,
            bytes,
            DurableFileClass.MutationControl);
    }

    public void AdvanceDurable(
        LibraryMutationJournal journal,
        LibraryMutationPhase next)
    {
        if (!IsAllowedTransition(
                journal.Kind,
                journal.Phase,
                next))
        {
            throw new InvalidOperationException(
                $"Invalid mutation phase transition: " +
                $"{journal.Phase} -> {next}.");
        }

        journal.Phase = next;

        _writer.ReplaceDurable(
            _paths.LibraryMutationJournalPath,
            SerializeValidate(journal),
            DurableFileClass.MutationControl);
    }

    public void DeleteStrict()
    {
        StrictPathProbe state =
            _strictPaths.Probe(
                _paths.LibraryMutationJournalPath);

        if (state.Kind ==
            StrictPathKind.Missing)
        {
            return;
        }

        if (state.Kind !=
            StrictPathKind.File)
        {
            throw new InvalidDataException(
                "Mutation journal path changed type.");
        }

        File.Delete(
            _paths.LibraryMutationJournalPath);
    }
}
```

## 22.5 Phase transitions

Create/Duplicate:

```text
Prepared -> BodyDurable -> MetadataDurable
```

Edit:

```text
Prepared -> RecoveryBodyDurable ->
BodyDurable -> MetadataDurable
```

Delete:

```text
Prepared -> MetadataDurable -> BodyDeleted
```

Reject everything else.

## 22.6 Hash exact bytes that will be written

Library:

```csharp
byte[] oldLibraryBytes =
    _libraryRepo
        .SerializeCanonicalBytes(
            current);

byte[] newLibraryBytes =
    _libraryRepo
        .SerializeCanonicalBytes(
            candidate);
```

Body:

```csharp
byte[] bodyBytes =
    StrictUtf8Text.Encode(body);
```

Hash:

```csharp
string oldLibrarySha =
    Convert.ToHexStringLower(
        SHA256.HashData(oldLibraryBytes));
```

Journal hash and actual durable write must use the same byte representation.

---

# 23. `PromptMutationCoordinator` copy-ready structure

```csharp
internal sealed class PromptMutationCoordinator
{
    private readonly AppPaths _paths;
    private readonly PromptRepository _promptRepo;
    private readonly LibraryRepository _libraryRepo;
    private readonly LibraryPackageInspector
        _packageInspector;
    private readonly
        LibraryMutationJournalRepository
            _journalRepo;
    private readonly
        LibraryMutationRecoveryService
            _recovery;
    private readonly IDurableAtomicFileWriter
        _writer;
    private readonly IVerifiedArtifactDeleter
        _verifiedDeleter;

    public PromptMutationCoordinator(
        AppPaths paths,
        PromptRepository promptRepo,
        LibraryRepository libraryRepo,
        LibraryPackageInspector packageInspector,
        LibraryMutationJournalRepository
            journalRepo,
        LibraryMutationRecoveryService recovery,
        IDurableAtomicFileWriter writer,
        IVerifiedArtifactDeleter
            verifiedDeleter)
    {
        _paths = paths;
        _promptRepo = promptRepo;
        _libraryRepo = libraryRepo;
        _packageInspector = packageInspector;
        _journalRepo = journalRepo;
        _recovery = recovery;
        _writer = writer;
        _verifiedDeleter = verifiedDeleter;
    }

    ...
}
```

`PromptLibraryService` can still own in-memory document/domain rules.

It delegates multi-file persistence to this coordinator.

---

# 24. Create/Duplicate implementation

```csharp
internal CommitResult CommitCreatePrompt(
    LibraryDocument current,
    LibraryDocument candidate,
    PromptRecord newPrompt,
    string body,
    LibraryMutationKind kind)
{
    if (kind is not
        LibraryMutationKind.CreatePrompt and not
        LibraryMutationKind.DuplicatePrompt)
    {
        throw new ArgumentOutOfRangeException(
            nameof(kind));
    }

    Guid operationId =
        Guid.NewGuid();

    byte[] oldLibrary =
        _libraryRepo.SerializeCanonicalBytes(
            current);

    byte[] newLibrary =
        _libraryRepo.SerializeCanonicalBytes(
            candidate);

    byte[] newBody =
        StrictUtf8Text.Encode(body);

    var journal =
        new LibraryMutationJournal
        {
            OperationId = operationId,
            Kind = kind,
            Phase =
                LibraryMutationPhase.Prepared,
            PromptId = newPrompt.Id,
            BodyRelativePath =
                Path.Combine(
                    "prompts",
                    $"{newPrompt.Id:N}.md"),
            OldLibrarySha256Hex =
                Hash(oldLibrary),
            NewLibrarySha256Hex =
                Hash(newLibrary),
            NewBodyLength =
                newBody.LongLength,
            NewBodySha256Hex =
                Hash(newBody)
        };

    _journalRepo
        .CreatePreparedDurable(journal);

    try
    {
        _writer.CreateNewDurable(
            _paths.GetPromptPath(
                newPrompt.Id),
            newBody,
            DurableFileClass.PromptBody);

        _journalRepo.AdvanceDurable(
            journal,
            LibraryMutationPhase.BodyDurable);

        CommitResult result =
            _libraryRepo
                .CommitCanonicalBytes(
                    candidate,
                    newLibrary);

        _journalRepo.AdvanceDurable(
            journal,
            LibraryMutationPhase.MetadataDurable);

        // Metadata + body are now committed.
        // Backup is a safety copy, not commit authority.
        _journalRepo.DeleteStrict();

        return result;
    }
    catch
    {
        // Optional immediate recovery attempt.
        // Startup recovery remains authoritative.
        throw;
    }
}
```

Do not publish the in-memory candidate until this returns committed.

---

# 25. Edit implementation

## 25.1 Sequence

```text
journal Prepared
old recovery copy
journal RecoveryBodyDurable
new active body
journal BodyDurable
new metadata
journal MetadataDurable
backup sync
verified recovery-copy delete
journal retire
```

## 25.2 Copy-ready core

```csharp
internal CommitResult CommitEditPrompt(
    LibraryDocument current,
    LibraryDocument candidate,
    Guid promptId,
    string newBody)
{
    Guid operationId =
        Guid.NewGuid();

    string bodyPath =
        _paths.GetPromptPath(promptId);

    byte[] oldBody =
        _promptRepo.ReadBytesStrict(
            promptId);

    byte[] newBodyBytes =
        StrictUtf8Text.Encode(newBody);

    byte[] oldLibrary =
        _libraryRepo
            .SerializeCanonicalBytes(current);

    byte[] newLibrary =
        _libraryRepo
            .SerializeCanonicalBytes(candidate);

    string recoveryRelative =
        Path.Combine(
            "recovery",
            $"mutation-{operationId:N}-" +
            $"old-{promptId:N}.md");

    string recoveryFull =
        Path.Combine(
            _paths.RootDirectory,
            recoveryRelative);

    var journal =
        new LibraryMutationJournal
        {
            OperationId = operationId,
            Kind =
                LibraryMutationKind.EditPrompt,
            Phase =
                LibraryMutationPhase.Prepared,
            PromptId = promptId,
            BodyRelativePath =
                Path.Combine(
                    "prompts",
                    $"{promptId:N}.md"),
            OldLibrarySha256Hex =
                Hash(oldLibrary),
            NewLibrarySha256Hex =
                Hash(newLibrary),
            OldBodyLength =
                oldBody.LongLength,
            OldBodySha256Hex =
                Hash(oldBody),
            NewBodyLength =
                newBodyBytes.LongLength,
            NewBodySha256Hex =
                Hash(newBodyBytes),
            RecoveryBodyRelativePath =
                recoveryRelative
        };

    _journalRepo
        .CreatePreparedDurable(journal);

    _writer.CreateNewDurable(
        recoveryFull,
        oldBody,
        DurableFileClass.RecoveryArtifact);

    _journalRepo.AdvanceDurable(
        journal,
        LibraryMutationPhase
            .RecoveryBodyDurable);

    _writer.ReplaceDurable(
        bodyPath,
        newBodyBytes,
        DurableFileClass.PromptBody);

    _journalRepo.AdvanceDurable(
        journal,
        LibraryMutationPhase.BodyDurable);

    CommitResult result =
        _libraryRepo.CommitCanonicalBytes(
            candidate,
            newLibrary);

    _journalRepo.AdvanceDurable(
        journal,
        LibraryMutationPhase.MetadataDurable);

    VerifyRecoveryCopyAndDelete(
        journal,
        recoveryFull);

    _journalRepo.DeleteStrict();

    return result;
}
```

If backup synchronization warning is returned after primary commit, do not roll
the edit back.

---

# 26. Delete implementation

```csharp
internal CommitResult CommitDeletePrompt(
    LibraryDocument current,
    LibraryDocument candidate,
    Guid promptId)
{
    byte[] oldLibrary =
        _libraryRepo
            .SerializeCanonicalBytes(current);

    byte[] newLibrary =
        _libraryRepo
            .SerializeCanonicalBytes(candidate);

    byte[] body =
        _promptRepo.ReadBytesStrict(promptId);

    var journal =
        new LibraryMutationJournal
        {
            OperationId = Guid.NewGuid(),
            Kind =
                LibraryMutationKind.DeletePrompt,
            Phase =
                LibraryMutationPhase.Prepared,
            PromptId = promptId,
            BodyRelativePath =
                Path.Combine(
                    "prompts",
                    $"{promptId:N}.md"),
            OldLibrarySha256Hex =
                Hash(oldLibrary),
            NewLibrarySha256Hex =
                Hash(newLibrary),
            OldBodyLength =
                body.LongLength,
            OldBodySha256Hex =
                Hash(body)
        };

    _journalRepo.CreatePreparedDurable(
        journal);

    CommitResult result =
        _libraryRepo.CommitCanonicalBytes(
            candidate,
            newLibrary);

    _journalRepo.AdvanceDurable(
        journal,
        LibraryMutationPhase.MetadataDurable);

    if (result.BackupSynchronized)
    {
        _verifiedDeleter.VerifyAndDelete(
            _paths.RootDirectory,
            _paths.GetPromptPath(promptId),
            body.LongLength,
            Hash(body));

        _journalRepo.AdvanceDurable(
            journal,
            LibraryMutationPhase.BodyDeleted);
    }

    _journalRepo.DeleteStrict();

    return result;
}
```

If backup was not synchronized:

```text
body remains as conservative orphan
return warning
reconciler handles it later when authority permits
```

---

# 27. Mutation recovery classification

Create:

```csharp
internal enum MutationContentState
{
    Missing,
    Old,
    New,
    Other
}
```

Helper:

```csharp
private static MutationContentState
    ClassifyBytes(
        byte[]? bytes,
        long? oldLength,
        string? oldSha,
        long? newLength,
        string? newSha)
{
    if (bytes is null)
    {
        return MutationContentState.Missing;
    }

    if (oldLength.HasValue &&
        oldSha is not null &&
        bytes.LongLength ==
            oldLength.Value &&
        string.Equals(
            Hash(bytes),
            oldSha,
            StringComparison.OrdinalIgnoreCase))
    {
        return MutationContentState.Old;
    }

    if (newLength.HasValue &&
        newSha is not null &&
        bytes.LongLength ==
            newLength.Value &&
        string.Equals(
            Hash(bytes),
            newSha,
            StringComparison.OrdinalIgnoreCase))
    {
        return MutationContentState.New;
    }

    return MutationContentState.Other;
}
```

Library primary classification uses:

```text
Old hash
New hash
Other
Missing
Unreadable
```

Any `Other` or `Unreadable` required state:

```text
FAIL CLOSED
keep journal
keep recovery copy
do not guess
```

---

# 28. Create/Duplicate recovery matrix

| library.json | body | Action |
|---|---|---|
| OLD | Missing | retire Prepared journal |
| OLD | NEW exact | verified delete body; retire |
| NEW | NEW exact | committed; retire |
| NEW | Missing | stop |
| OLD/NEW | Other | stop |
| Other | any | stop |
| Unreadable | any | stop |

The recovery service should not care whether phase is one step stale when the
durable files provide stronger evidence.

Phase narrows what states are expected; hashes decide authority.

---

# 29. Edit recovery matrix

| library | active body | old recovery copy | Action |
|---|---|---|---|
| OLD | OLD | missing/OLD | retire/cleanup recovery |
| OLD | NEW | OLD | restore old body durably; remove recovery; retire |
| NEW | NEW | OLD | committed; remove recovery; retire |
| OLD | Missing | OLD | restore old body; retire |
| NEW | OLD | OLD | inconsistent; stop |
| NEW | Missing | OLD | inconsistent; stop |
| any | Other | any | stop |
| any | any | Other | stop |

Restoration uses `ReplaceDurable`.

Do not `File.Copy` over the active body.

---

# 30. Delete recovery matrix

| library | body | backup authority | Action |
|---|---|---|---|
| OLD | OLD | any | deletion not committed; retire journal |
| NEW | OLD | current/synced new metadata | verified delete body; retire |
| NEW | OLD | future/unreadable/unsynced | preserve orphan; retire with warning |
| NEW | Missing | any | committed; retire |
| Other | any | any | stop |
| unreadable | any | any | stop |

---

# 31. Startup journal recovery wiring

Recommended order in `App.xaml.cs`:

```text
01 settings lease + settings temp reconciliation
02 load/recover settings
03 resolve physical active root/bootstrap
04 acquire active .app.lock
05 detect migration/mutation/init journal conflict
06 if migration marker exists:
       recover/finalize migration with recovery lease
07 EnsureDataDirectories
08 strict managed tree runtime validation
09 acquire application-lifetime ManagedDataRootSessionLease
10 if mutation journal exists:
       LibraryMutationRecoveryService.RecoverIfPresent()
11 if initialization marker exists:
       initialization recovery
12 load startup primary/backup package authority
13 synchronize backup only from Healthy package
14 PromptOrphanReconciler
15 build UI
```

The long-lived session lease must already be held during mutation recovery.

---

# 32. CRUU11-003 — PromptOrphanReconciler copy-ready design

```csharp
internal sealed record
    OrphanReconciliationAuthority(
        LibraryDocument Primary,
        LibraryDocument Backup);

internal sealed record
    OrphanReconciliationResult(
        IReadOnlyList<string> Deleted,
        IReadOnlyList<string> Preserved,
        string? Warning);

internal sealed class PromptOrphanReconciler
{
    private readonly AppPaths _paths;
    private readonly PromptRepository _prompts;
    private readonly
        LibraryMutationJournalRepository
            _journalRepo;

    public OrphanReconciliationResult Reconcile(
        OrphanReconciliationAuthority authority)
    {
        if (_journalRepo.TryReadStrict()
            is not null)
        {
            return new(
                [],
                [],
                "Orphan cleanup deferred while a " +
                "library mutation journal exists.");
        }

        var protectedIds =
            new HashSet<Guid>(
                authority.Primary.Prompts
                    .Select(p => p.Id));

        protectedIds.UnionWith(
            authority.Backup.Prompts
                .Select(p => p.Id));

        var deleted =
            new List<string>();

        var preserved =
            new List<string>();

        foreach (string path in
                 _prompts
                     .EnumeratePromptFilesStrict())
        {
            string stem =
                Path.GetFileNameWithoutExtension(
                    path);

            if (!Guid.TryParseExact(
                    stem,
                    "N",
                    out Guid id))
            {
                preserved.Add(path);
                continue;
            }

            if (protectedIds.Contains(id))
            {
                preserved.Add(path);
                continue;
            }

            // App-lifetime tree lease is already held.
            File.Delete(path);
            deleted.Add(path);
        }

        return new(
            deleted,
            preserved,
            null);
    }
}
```

Call this only when backup authority is known Current.

If backup Future or Unreadable:

```text
do not instantiate authority
preserve all candidates
```

---

# 33. Orphan tests

```text
CRUU11_003_Primary_reference_protects_body
CRUU11_003_Backup_reference_protects_body
CRUU11_003_Future_backup_defers_cleanup
CRUU11_003_Unreadable_backup_defers_cleanup
CRUU11_003_Mutation_journal_defers_cleanup
CRUU11_003_Unreferenced_GUID_body_is_removed
CRUU11_003_NonGUID_markdown_is_preserved
CRUU11_003_After_reconciliation_migration_snapshot_does_not_include_removed_body
```

Every preservation case checks exact bytes.



---

# 34. CRUU11-024 — Replace overclaiming / missing verification

**Severity:** MED verification gap

The current comprehensive tests are useful, but several names/sections are
stronger than the behavior actually executed.

## 34.1 Missing mutation/orphan test sections

Current CRUU10 comprehensive test source has no:

```text
CRUU10_004
CRUU10_005
```

sections.

This matches the missing production mutation journal/orphan subsystem.

CRUU11 must add dedicated test files, not another giant region to the same
comprehensive file.

Recommended:

```text
Cruu11VerifiedDeletionTests.cs
Cruu11PathAuthorityTests.cs
Cruu11ManagedTreeTests.cs
Cruu11MigrationReadyTests.cs
Cruu11MigrationRecoveryTests.cs
Cruu11CapabilityOwnershipTests.cs
Cruu11SettingsDurabilityTests.cs
Cruu11StrictUtf8Tests.cs
Cruu11MutationRecoveryTests.cs
Cruu11OrphanReconcilerTests.cs
Cruu11RepositoryRaceTests.cs
Cruu11EvidenceTests.cs
WindowsCruu11FilesystemIntegrationTests.cs
```

## 34.2 Replace the current session-lease test

Current test only proves:

```text
lease1 can open
lease2 can open
```

It does not prove node replacement is blocked.

Required real Windows behavior:

```csharp
using var lease =
    ManagedDataRootSessionLease.Acquire(root);

Assert.ThrowsException<IOException>(
    () => Directory.Move(
        prompts,
        prompts + ".moved"));
```

Then:

```text
original directory still exists
no outside directory used
```

## 34.3 Add strict access-denied path test

The current CRUU10 strict-path test checks:

```text
file
directory
missing
```

It does not exercise inaccessible state.

Use a deterministic fake:

```csharp
internal sealed class
    ThrowingDirectoryOpener
    : IStrictDirectoryOpener
{
    public DirectoryOpenResult
        OpenDirectoryStrict(string path) =>
        throw new UnauthorizedAccessException(
            "Injected");

    public SafeFileHandle
        OpenManagedNodeLease(string path) =>
        throw new UnauthorizedAccessException(
            "Injected");
}
```

Then:

```text
FindNearestExistingDirectoryStrict must throw
not walk to parent
```

## 34.4 Add invalid-UTF8 tests

Use raw bytes rather than a .NET string.

Invalid sequence:

```csharp
byte[] invalidUtf8 =
[
    0x7B,                   // {
    0x22, 0x78, 0x22,      // "x"
    0x3A, 0x22,            // :"
    0xC3, 0x28,            // invalid UTF-8
    0x22, 0x7D             // "}
];
```

Test:

```text
settings
library
prompt body
mutation journal
migration v4 manifest
```

## 34.5 Add programmer-exception test for Duplicate

A fake read seam throws:

```csharp
new InvalidOperationException(
    "programmer-test-sentinel")
```

or a dedicated non-filesystem exception.

Assert it is **not** converted to "prompt body unavailable".

## 34.6 Add prefix-collision verified-deleter test

Do not consider this test sufficient:

```text
root=temp1
file=temp2
```

Required exact semantic case:

```text
root = C:\...\Data
file = C:\...\DataOutside\file.bin
```

or a path-normalization unit seam with those exact names.

The test must fail with the old `StartsWith` implementation.

## 34.7 Required negative-test preservation template

For every destructive recovery test:

```csharp
byte[] settingsBefore =
    File.Exists(settings)
        ? File.ReadAllBytes(settings)
        : [];

byte[] primaryBefore = ...;
byte[] backupBefore = ...;
byte[] foreignBefore = ...;

Assert.ThrowsException<SpecificException>(
    Act);

CollectionAssert.AreEqual(
    foreignBefore,
    File.ReadAllBytes(foreignPath));

// Assert journal/manifest state exactly.
```

Do not test only exception type.

---

# 35. Test categories required after CRUU11

Use:

```text
FilesystemAuthority
MigrationRecovery
MigrationReady
PackageIntegrity
MutationRecovery
OrphanReconciliation
SettingsDurability
StrictUtf8
WpfIntegration
WindowsFilesystemIntegration
ReleaseVerification
CrashRecovery
```

A test can have multiple categories.

Minimum critical/high mapping:

```text
CRUU11-001 -> FilesystemAuthority + WindowsFilesystemIntegration
CRUU11-002 -> MutationRecovery + CrashRecovery
CRUU11-004 -> WindowsFilesystemIntegration
CRUU11-005 -> FilesystemAuthority
CRUU11-009 -> MigrationReady + CrashRecovery
```

---

# 36. CRUU11-025 — Exact TRX evidence, no substring matching

**Severity:** MED verification gap

Current script allows:

```powershell
$testName -like "*$required*"
```

That means required:

```text
CRUU11_001_Prefix_collision
```

could be satisfied by:

```text
NOT_REALLY_CRUU11_001_Prefix_collision_placeholder
```

Required sentinels must use exact equality.

## 36.1 Replace matching block

```powershell
$resultsByName = @{}

foreach ($result in $unitTestResults) {
    $name = [string]$result.testName

    if (-not [string]::IsNullOrWhiteSpace($name)) {
        if (-not $resultsByName.ContainsKey($name)) {
            $resultsByName[$name] = @()
        }

        $resultsByName[$name] += $result
    }
}

$missingOrFailed = @()

foreach ($required in $RequiredTests) {
    if (-not $resultsByName.ContainsKey($required)) {
        $missingOrFailed +=
            "$required (Not Executed)"
        continue
    }

    $runs = @($resultsByName[$required])

    if ($runs.Count -ne 1) {
        $missingOrFailed +=
            "$required (Expected exactly one result, " +
            "found $($runs.Count))"
        continue
    }

    $outcome =
        [string]$runs[0].outcome

    if ($outcome -ne "Passed") {
        $missingOrFailed +=
            "$required (Outcome: $outcome)"
    }
}

if ($missingOrFailed.Count -gt 0) {
    throw (
        "Required test evidence failed: " +
        ($missingOrFailed -join ", ")
    )
}
```

Exact names only.

## 36.2 Support multiple TRX paths

Preferred script contract:

```powershell
param(
    [Parameter(Mandatory=$true)]
    [string[]]$TrxPath,

    [Parameter(Mandatory=$false)]
    [string[]]$RequiredTests = @()
)
```

Merge all result entries before exact sentinel verification.

This allows separate category jobs to be verified as one evidence set.

## 36.3 Required sentinel file

Create:

```text
tools/RequiredRegressionTests.psd1
```

At minimum:

```powershell
@{
    Required = @(
        'CRUU11_001_Prefix_collision_C_Data_vs_C_DataOutside_is_rejected',
        'CRUU11_002_Create_crash_after_body_before_metadata_removes_exact_orphan',
        'CRUU11_002_Edit_crash_after_new_body_before_metadata_restores_old_body',
        'CRUU11_003_Backup_reference_protects_body',
        'CRUU11_004_Prompts_directory_rename_fails_while_session_lease_held',
        'CRUU11_005_Access_denied_ancestor_is_not_skipped',
        'CRUU11_008_Case_sensitive_prompts_is_rejected',
        'CRUU11_009_Source_active_body_changes_after_copy_aborts_Ready',
        'CRUU11_010_Ready_gate_rejects_existing_stage',
        'CRUU11_011_Directory_cleanup_failure_preserves_marker',
        'CRUU11_013_Preexisting_planned_probe_collision_is_never_deleted',
        'CRUU11_016_Settings_primary_recovery_uses_durable_settings_writer',
        'CRUU11_017_Parser_rejects_foreign_target_basename',
        'CRUU11_018_Invalid_UTF8_library_is_rejected',
        'CRUU11_019_CreateNewDurable_never_replaces_existing_target',
        'CRUU11_021_Foreign_body_created_after_GUID_check_is_not_overwritten'
    )
}
```

Add previous high-risk CRUU9 sentinels as well.

---

# 37. Windows CI exact gate expansion

Current workflow has:

```text
CrashRecovery
WpfIntegration
WindowsFilesystemIntegration
Full Suite
```

Add:

```yaml
      - name: Test Filesystem Authority
        shell: pwsh
        run: |
          dotnet test PromptHelper.slnx `
            -c Release `
            --no-build `
            --filter "TestCategory=FilesystemAuthority" `
            --logger "trx;LogFileName=filesystem-authority.trx"

      - name: Test Package Integrity
        shell: pwsh
        run: |
          dotnet test PromptHelper.slnx `
            -c Release `
            --no-build `
            --filter "TestCategory=PackageIntegrity" `
            --logger "trx;LogFileName=package-integrity.trx"

      - name: Test Mutation Recovery
        shell: pwsh
        run: |
          dotnet test PromptHelper.slnx `
            -c Release `
            --no-build `
            --filter "TestCategory=MutationRecovery" `
            --logger "trx;LogFileName=mutation-recovery.trx"

      - name: Test Migration Ready Gate
        shell: pwsh
        run: |
          dotnet test PromptHelper.slnx `
            -c Release `
            --no-build `
            --filter "TestCategory=MigrationReady" `
            --logger "trx;LogFileName=migration-ready.trx"

      - name: Test Strict UTF-8
        shell: pwsh
        run: |
          dotnet test PromptHelper.slnx `
            -c Release `
            --no-build `
            --filter "TestCategory=StrictUtf8" `
            --logger "trx;LogFileName=strict-utf8.trx"
```

Then existing:

```text
CrashRecovery
WpfIntegration
WindowsFilesystemIntegration
Full Suite
```

## 37.1 Verify exact sentinels after category runs

```powershell
$required =
    Import-PowerShellDataFile `
        ./tools/RequiredRegressionTests.psd1

$trx =
    Get-ChildItem `
        tests/PromptHelper.Tests/TestResults `
        -Recurse `
        -Filter '*.trx' |
    Select-Object -ExpandProperty FullName

./tools/VerifyTestEvidence.ps1 `
    -TrxPath $trx `
    -RequiredTests $required.Required
```

## 37.2 Do not allow empty category pass

`VerifyTestEvidence` already rejects total 0.

Keep that.

Each category command must also have at least one exact category sentinel.

## 37.3 Final stress

The workflow may keep the manual `stress` input for regular development.

Final acceptance of CRUU11 requires one actual run with:

```text
stress=true
```

and five consecutive full suites.

The implementation AI must report the run evidence.

---

# 38. CRUU11-026 — Exact icon identity, not icon presence

**Severity:** MED release gap

Current `CompareIconIdentity.ps1`:

```text
reads ICO frame payload bytes
SHA256s compressed frame bytes
compares ReferenceIco vs TargetIco
PublishedExePath branch only checks the EXE exists
```

Current `VerifyReleaseAssets.ps1`:

```text
verifies ICO structure
ExtractIconEx icon count >= 1
```

This does not prove:

```text
current approved SVG
    ==
committed ICO visual identity
    ==
published EXE embedded icon identity
```

## 38.1 Required normalization

Compare decoded pixel identity, not compressed PNG/DIB payload bytes.

For each mandatory size:

```text
16
24
32
48
64
128
256
```

normalize to:

```text
width x height
32-bit RGBA
fixed row order
no metadata
```

Hash raw RGBA bytes.

## 38.2 Strict release sequence

When real SVG exists:

```text
1. generate temporary expected ICO from current SVG
2. decode expected ICO frames -> normalized RGBA hashes
3. decode committed PromptHelper.ico -> normalized RGBA hashes
4. require every size hash equal
5. extract published EXE RT_GROUP_ICON + RT_ICON resources
6. reconstruct/decode EXE icon frames
7. require every size hash equals committed ICO
```

## 38.3 Recommended implementation form

Rather than increasingly complex PowerShell byte parsing, create a tiny
Windows-only verification console tool:

```text
tools/IconIdentityVerifier/
    IconIdentityVerifier.csproj
    Program.cs
    IcoReader.cs
    PeIconResourceReader.cs
    PixelNormalizer.cs
```

It is a build/test tool, not shipped runtime code.

CLI:

```text
IconIdentityVerifier.exe compare-ico expected.ico actual.ico
IconIdentityVerifier.exe compare-exe actual.ico PromptHelper.exe
```

Exit nonzero on mismatch.

PowerShell orchestrates SVG generation and calls the verifier.

## 38.4 Fixture tests

Generate fixture A and B.

```text
A ICO vs A ICO => pass
A ICO vs B ICO => fail
A ICO vs EXE(A) => pass
A ICO vs EXE(B) => fail
missing 24x24 => fail
single wrong pixel at 256 => fail
```

Do not use production logo fixture until supplied.

## 38.5 `VerifyReleaseAssets.ps1`

Strict gate must call identity verifier.

Do not retain only:

```text
ExtractIconEx count
```

as final proof.

---

# 39. CRUU11-027 — Real logo remains external release blocker

**Severity:** RELEASE BLOCKER

Audited repository still has no:

```text
src/PromptHelper/Assets/PromptHelperLogo.svg
```

and no real approved production icon set.

Rules:

```text
DO NOT generate a placeholder to make CI green
DO NOT substitute a test fixture
DO NOT claim strict release
```

Until real approved SVG arrives:

```text
PRODUCT/CODE ACCEPTANCE    = potentially achievable
STRICT RELEASE ACCEPTANCE  = BLOCKED
```

The icon verification tooling may be fully implemented and tested with
fixtures meanwhile.

---

# 40. Full fault-injection matrix

The weak model must create deterministic seams for these.

## 40.1 Native/path

| Cut/fault | Expected result |
|---|---|
| final path is `C:\DataOutside\x` while root `C:\Data` | reject before deletion |
| opened final is reparse object | reject |
| GetFinalPath requires > initial buffer | resize/retry |
| UNC final path | normalize to `\\server\share\...` correctly |
| strict directory opener returns access denied | fail closed |
| nearest child Missing | walk only to parent |
| managed prompts path is File | reject |
| managed prompts case-sensitive | reject |
| rename prompts while session lease held | Windows sharing failure |

## 40.2 Migration

| Cut/fault | Expected result |
|---|---|
| old interrupted migration exists | recover old first, then capture new baseline |
| source changes after copy verification | Ready rejects |
| source orphan appears after copy | Ready rejects |
| stage exists before Ready | Ready rejects |
| stage remains with Ready marker on startup | exact cleanup or fail before marker retirement |
| foreign root file appears before Ready | reject |
| foreign nested file appears before Ready | reject |
| directory cleanup fails during retry | marker remains |
| planned probe CreateNew collides | foreign collision preserved |
| capability replacement fails | cleanup exact owned files, marker state truthful |

## 40.3 Settings

| Cut/fault | Expected result |
|---|---|
| valid-primary backup sync crashes in temp | exact settings temp reconciles under settings lease |
| backup-to-primary recovery crashes in temp | same |
| foreign `.prompthelper-settings-notes-<guid>.tmp` | preserved |
| legacy `.settings.json.<guid>.tmp` | exact bootstrap reconciliation |
| invalid UTF-8 settings | reject, do not normalize |

## 40.4 Library package

| Cut/fault | Expected result |
|---|---|
| primary body missing | do not overwrite better backup |
| backup body missing | do not promote backup |
| invalid UTF-8 body | package not Healthy |
| future backup | preserve |

## 40.5 Create

```text
journal Prepared crash
body temp crash
body durable / metadata old
metadata durable / journal not retired
foreign final appears before create promotion
```

Each has exact recovery state.

## 40.6 Edit

```text
Prepared crash
old recovery copy durable crash
new active body durable / metadata old
metadata new / recovery copy remains
unexpected recovery copy bytes
unexpected active body bytes
```

## 40.7 Delete

```text
Prepared
metadata durable / backup unsynced
metadata durable / body delete access denied
body missing / metadata new
future backup
```

## 40.8 Evidence

```text
required test missing
required test renamed with required string as substring
required test skipped
category total zero
one failed result in second TRX
duplicate exact test-name result
```

All fail evidence gate except legitimate multiple TRX execution if the
script explicitly allows and resolves duplicates by run identity.

---

# 41. Required exact tests

The following are the minimum CRUU11 set.

## 41.1 Verified deletion

```text
CRUU11_001_Prefix_collision_C_Data_vs_C_DataOutside_is_rejected
CRUU11_001_Strict_descendant_file_is_accepted
CRUU11_001_UNC_final_path_prefix_normalizes_correctly
CRUU11_001_Buffer_resize_retries_when_API_returns_required_size
CRUU11_001_Reparse_artifact_is_rejected_before_deletion
```

## 41.2 Mutation recovery

```text
CRUU11_002_Create_crash_after_Prepared_recovers_old_state
CRUU11_002_Create_crash_after_body_before_metadata_removes_exact_orphan
CRUU11_002_Create_crash_after_metadata_before_journal_retire_finalizes
CRUU11_002_Edit_crash_after_recovery_copy_before_body_is_safe
CRUU11_002_Edit_crash_after_new_body_before_metadata_restores_old_body
CRUU11_002_Edit_crash_after_metadata_keeps_new_body
CRUU11_002_Delete_crash_after_metadata_before_body_delete_is_recoverable
CRUU11_002_Unexpected_body_hash_preserves_journal_and_stops
CRUU11_002_Unexpected_library_hash_preserves_journal_and_stops
CRUU11_002_Duplicate_uses_Create_transaction_state_machine
```

## 41.3 Orphans

```text
CRUU11_003_Backup_reference_protects_body
CRUU11_003_Future_backup_preserves_orphans
CRUU11_003_Unreadable_backup_preserves_orphans
CRUU11_003_Active_mutation_journal_preserves_body
CRUU11_003_Unreferenced_GUID_body_is_removed
```

## 41.4 Managed tree/path

```text
CRUU11_004_Prompts_directory_rename_fails_while_session_lease_held
CRUU11_004_Recovery_directory_rename_fails_while_session_lease_held
CRUU11_005_Access_denied_ancestor_is_not_skipped
CRUU11_006_Resolver_uses_the_handle_returned_by_strict_opener
CRUU11_007_Prompts_file_is_rejected
CRUU11_008_Case_sensitive_prompts_is_rejected
```

## 41.5 Migration terminal authority

```text
CRUU11_009_Source_active_body_changes_after_copy_aborts_Ready
CRUU11_009_Retry_same_root_changed_payload_fails_closed
CRUU11_010_Ready_gate_rejects_existing_stage
CRUU11_010_Ready_gate_rejects_new_nested_foreign_file
CRUU11_011_Directory_cleanup_failure_preserves_marker
CRUU11_012_New_manifest_baseline_captured_after_old_attempt_cleanup
CRUU11_013_Preexisting_planned_probe_collision_is_never_deleted
```

## 41.6 Settings / UTF-8 / durable create

```text
CRUU11_016_Settings_primary_recovery_uses_durable_settings_writer
CRUU11_017_Parser_rejects_foreign_target_basename
CRUU11_018_Invalid_UTF8_settings_is_rejected
CRUU11_018_Invalid_UTF8_library_is_rejected
CRUU11_018_Invalid_UTF8_prompt_body_is_not_Healthy
CRUU11_019_CreateNewDurable_never_replaces_existing_target
CRUU11_021_Foreign_body_created_after_GUID_check_is_not_overwritten
```

## 41.7 API / evidence

```text
CRUU11_022_Duplicate_programmer_exception_propagates
CRUU11_023_Backup_sync_API_requires_HealthyLibraryPackage
CRUU11_025_Substring_test_name_does_not_satisfy_required_exact_name
CRUU11_025_Missing_required_test_fails_evidence_script
```

---

# 42. Crash fixture helper

Create:

```text
tests/PromptHelper.Tests/LibraryMutationCrashFixtureBuilder.cs
```

Recommended API:

```csharp
internal sealed class
    LibraryMutationCrashFixtureBuilder
{
    public LibraryMutationCrashFixtureBuilder(
        string root);

    public LibraryMutationCrashFixtureBuilder
        WithPrimary(
            LibraryDocument document);

    public LibraryMutationCrashFixtureBuilder
        WithBackup(
            LibraryDocument document);

    public LibraryMutationCrashFixtureBuilder
        WithBody(
            Guid id,
            byte[] body);

    public LibraryMutationCrashFixtureBuilder
        WithRecoveryBody(
            string relativePath,
            byte[] body);

    public LibraryMutationCrashFixtureBuilder
        WithJournal(
            LibraryMutationJournal journal);

    public void Build();
}
```

Tests construct a cut-point state, then create **new** service instances.

Do not call the original mutation method's catch to simulate a crash.

---

# 43. Recording/fault-injection helpers

## 43.1 Durable writer recorder

```csharp
internal sealed class
    RecordingDurableWriter
    : IDurableAtomicFileWriter
{
    public List<string> Operations { get; } = [];

    public Action<string>? BeforeReplace { get; set; }
    public Action<string>? BeforeCreate { get; set; }

    ...
}
```

Record:

```text
TempCreate
TempWrite
TempFlush
FinalMoveReplaceWriteThrough
FinalMoveCreateNoOverwriteWriteThrough
TempCleanup
```

Fault callbacks at each.

## 43.2 Migration source mutation callback

Add test-only seam:

```csharp
internal Action? BeforeReadyGateForTest
    { get; set; }
```

or inject a source snapshot provider.

Do not put sleeps between copy and Ready.

Test callback mutates source deterministically.

## 43.3 Recovery inventory callback

```csharp
Action? BeforeFinalRecoveryInventoryForTest
```

Inject foreign file after cleanup but before terminal verification.

## 43.4 Directory opener fake

Must support:

```text
Opened(handle identity A)
Missing
AccessDenied
different handle identity on forbidden second open
```

This proves resolver uses one handle.

---

# 44. Byte-preservation standard

For every negative path, assert exact bytes for relevant:

```text
settings.json
settings.backup.json
source library.json
source library.backup.json
source active prompt
source orphan
source recovery artifact
target foreign file
target collision file
mutation recovery body
migration marker
mutation journal
```

Do not assert only `File.Exists`.

Helper:

```csharp
internal static void AssertBytes(
    byte[] expected,
    string path)
{
    CollectionAssert.AreEqual(
        expected,
        File.ReadAllBytes(path));
}
```

---

# 45. Manual Windows destructive-path validation

After automated tests pass, use disposable temporary roots only.

Validate:

```text
1. target\prompts junction -> outside
2. target\recovery junction -> outside
3. prefix collision root Data vs DataOutside
4. held managed-tree session lease blocks rename
5. case-sensitive prompts directory rejected
6. stale migration marker v3 recovery
7. stale migration marker v4 recovery
8. foreign target file introduced before Ready
9. mutation-journal Create crash fixture
10. mutation-journal Edit crash fixture
11. delete with read-only body
12. future backup orphan preservation
```

Never test destructive recovery against a real user's production prompt
directory.

