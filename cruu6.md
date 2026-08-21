# CRUU6 — Post-CRUU5 Deep Regression Audit & Deterministic Repair Blueprint

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited commit:** `197196035b9ebf82c43c9a37ac4ed33b81bc8005`  
**Previous audit authority:** `cruu1.md`, `cruu2.md`, `cruu3.md`, `cruu4.md`, `cruu5.md`  
**Purpose:** independently re-audit the implementation after the CRUU5 repair commit, identify remaining correctness/data-safety/diagnostic/release defects, and give a weak implementation model a precise, low-choice repair path.

---

# 1. Executive result

CRUU5 **substantially landed**. The current source contains the requested strict settings-schema check, future-schema write preservation, active-root transition coordinator, settings write token, fail-closed physical path resolution, physical-root topology checks, target reservation cleanup, target fingerprints, explicit migration rollback reporting, and real NTFS junction integration tests.

A fresh source-level audit nevertheless found additional defects and second-order races created or exposed by those changes.

The correct status is:

```text
CRUU5 STRUCTURAL IMPLEMENTATION       = SUBSTANTIALLY LANDED
CURRENT AUDITED COMMIT                = 197196035b9ebf82c43c9a37ac4ed33b81bc8005
SOURCE-LEVEL POST-CRUU5 AUDIT         = COMPLETED
NEW CRUU6 FINDINGS                    = OPEN
INDEPENDENT WINDOWS/.NET EXECUTION    = NOT AVAILABLE IN THIS AUDIT ENVIRONMENT
GITHUB COMBINED STATUS ENTRIES        = NONE RETURNED BY CONNECTOR
FINAL PRODUCTION-CLEAN ACCEPTANCE     = NOT YET GRANTED
STRICT RELEASE ACCEPTANCE             = BLOCKED BY AUTHORITATIVE LOGO ASSET
```

The audit environment does not expose a usable Windows WPF/.NET 10 runtime, so this document does **not** claim to have rerun the application or the test suite. Findings below come from direct inspection of the actual pushed `main` source at the commit above.

Do not interpret an existing green test count as coverage for cases not represented by the tests.

---

# 2. CRUU6 finding table

| ID | Severity | Finding |
|---|---|---|
| CRUU6-001 | HIGH | A persisted junction/symlink data-root alias can make all later data-folder transitions fail because startup uses the physical root while the coordinator compares it to the lexical settings path |
| CRUU6-002 | HIGH | Physical topology is validated before target reservation but not bound to the target after reservation; a reparse-point/path swap can redirect the operation after validation |
| CRUU6-003 | HIGH | Settings transition compare-and-swap covers only `settings.json`, not `settings.backup.json`, and comparison is not atomic with the write |
| CRUU6-004 | MEDIUM-HIGH | The transition token is captured before a call that can itself recover/synchronize settings, allowing Prompt Helper to invalidate its own precondition |
| CRUU6-005 | HIGH | A partially created destination file can escape migration rollback because ownership is recorded only after `File.Copy` succeeds |
| CRUU6-006 | MEDIUM-HIGH | Capability-probe cleanup failures are swallowed and probe artifacts are outside the migration rollback journal |
| CRUU6-007 | MEDIUM | Target reservation cleanup failures are still swallowed, so failed transitions can leave `.app.lock` or an empty root without truthful reporting |
| CRUU6-008 | MEDIUM-HIGH | Existing-target fingerprints can be hybrid snapshots because metadata is parsed from one read and fingerprinted from a later read |
| CRUU6-009 | MEDIUM-HIGH | Future-schema transition exceptions are not handled by `SettingsDialog`, allowing a safe folder-selection error to reach the fatal WPF exception handler |
| CRUU6-010 | MEDIUM | “Writable target” validation proves a disposable probe is replaceable but does not prove existing managed files such as read-only `library.json` can be atomically replaced |
| CRUU6-011 | LOW-MEDIUM | An unavailable/disconnected configured root can fail during physical resolution before the dedicated “configured folder unavailable” path, degrading recovery guidance |
| CRUU6-012 | RELEASE BLOCKER | The authoritative `PromptHelperLogo.svg` and generated ICO are still absent |

These findings do **not** authorize new product features or redesigns.

---

# 3. Locked product/architecture decisions

Preserve all of the following:

1. WPF + .NET 10 remains the stack.
2. Prompt bodies remain local Markdown files under `prompts/`.
3. `library.json` remains the primary library metadata.
4. `library.backup.json` remains its safety backup.
5. Bootstrap settings remain in `%LOCALAPPDATA%\PromptHelper`.
6. `settings.json` remains settings primary and `settings.backup.json` remains its safety backup.
7. Existing valid target libraries may be selected without merge/overwrite.
8. Empty targets receive a copy of the active library.
9. The old source root is never deleted automatically.
10. A data-root change requires process shutdown/restart.
11. One Prompt Helper instance holds the active data-root `.app.lock`.
12. Physical alias/junction safety remains required.
13. Future-schema files must never be downgraded by an older build.
14. Missing/unavailable configured custom roots must never cause silent initialization of a new library.
15. `PromptRecord.Title == null` remains automatic-headline mode.
16. Recent-copy state remains session-only.
17. No cloud, telemetry, accounts, network service, database migration, updater, installer framework, MSIX, signing, trimming, or single-file publishing is introduced.
18. Do not fabricate the missing logo.
19. Current schema versions remain `1` in this repair.
20. Tests must prove observable behavior, not merely source-string presence.

---

# 4. Required implementation order

Implement exactly in this dependency order:

```text
PHASE A  CRUU6-003/004  bootstrap settings transaction/precondition
PHASE B  CRUU6-001      physical settings-root identity
PHASE C  CRUU6-002      bind validated physical target to reservation
PHASE D  CRUU6-005      owned temporary copy + rollback-safe promotion
PHASE E  CRUU6-006/007  truthful cleanup journal and reservation release
PHASE F  CRUU6-008      coherent existing-target snapshot/fingerprint
PHASE G  CRUU6-009      controlled future-schema UI error handling
PHASE H  CRUU6-010      existing managed-file capability validation
PHASE I  CRUU6-011      unavailable-root diagnostic classification
PHASE J  full regression + fault injection + real Windows junction tests
PHASE K  CRUU6-012      icon gate only after real SVG is supplied
```

Do not start with UI wording or documentation. The storage and transition invariants must be repaired first.

---

# 5. CRUU6-001 — Persisted physical alias breaks later transitions

**Severity:** HIGH  
**Area:** data-root identity / settings / junctions  
**Files:**  
`src/PromptHelper/App.xaml.cs`  
`src/PromptHelper/Services/DataFolderTransitionCoordinator.cs`  
`src/PromptHelper/Services/ManagedDataRootPolicy.cs`  
`tests/PromptHelper.Tests/DataFolderTransitionCoordinatorTests.cs`  
`tests/PromptHelper.Tests/WindowsPhysicalPathResolverIntegrationTests.cs`

## 5.1 Exact defect

Startup currently does this:

```text
settings.DataRootPath
    -> GetEffectiveDataRoot()
    -> ValidateConfiguredRootForStartup()
    -> returns PHYSICAL root
    -> AppPaths / MainViewModel use physical root
```

The transition coordinator later does this:

```csharp
string cleanCurrent = _activeCurrentRoot;      // physical
string currentSettingsRoot = _settingsRepo.GetEffectiveDataRoot(); // lexical
if (!PathIdentity.Equals(cleanCurrent, currentSettingsRoot))
{
    throw ...
}
```

`PathIdentity.Equals` is lexical normalization, not physical identity.

Therefore:

```text
settings.json: C:\Aliases\PromptData
junction:      C:\Aliases\PromptData -> D:\PromptData

startup active root: D:\PromptData

later transition:
active = D:\PromptData
setting = C:\Aliases\PromptData
lexical comparison = different
=> transition rejected
```

Both paths identify the same library, but the app treats them as disagreement.

The current junction test only tests the **candidate target** being an alias of the active root. It does not test the **persisted settings path** being an alias.

## 5.2 Required invariant

For transition authority:

```text
active running root identity          = physical identity
persisted settings data-root identity = physical identity
candidate target identity             = physical identity
```

Lexical paths may still be retained for display/persistence, but all safety equality/containment comparisons must use the same physical-identity layer.

## 5.3 Exact implementation

Do not delete the settings-vs-active consistency guard.

Replace the lexical guard with a physical guard.

First derive bootstrap root before the guard:

```csharp
string bootstrapRoot =
    Path.GetDirectoryName(_settingsRepo.SettingsPath)
    ?? Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "PromptHelper");
```

After obtaining the transition settings snapshot from Phase A:

```csharp
string settingsLexicalRoot =
    _settingsRepo.GetEffectiveDataRoot(settingsSnapshot.Settings);

string settingsPhysicalRoot =
    _rootPolicy.ValidateConfiguredRootForStartup(
        settingsLexicalRoot,
        bootstrapRoot);

string activePhysicalRoot =
    DataRootTopologyValidator.ResolvePhysicalOrThrow(
        _physicalPathResolver,
        _activeCurrentRoot,
        "active data folder");

if (!PathIdentity.Equals(
        activePhysicalRoot,
        settingsPhysicalRoot))
{
    throw new InvalidOperationException(
        "Prompt Helper settings no longer identify the active " +
        "running library. The data-folder transition was cancelled. " +
        "Reopen Tools & Settings and retry.");
}
```

Store the resolver as a coordinator field. Do not instantiate a second resolver with different behavior.

Recommended constructor state:

```csharp
private readonly IPhysicalPathResolver _physicalPathResolver;
private readonly ManagedDataRootPolicy _rootPolicy;
private readonly string _activeCurrentRoot;

public DataFolderTransitionCoordinator(
    string activeCurrentRoot,
    AppSettingsRepository settingsRepo,
    DataFolderMigrationService migrationService,
    IUserConfirmationService confirmationService,
    DataRootCapabilityValidator? capabilityValidator = null,
    IPhysicalPathResolver? pathResolver = null)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(activeCurrentRoot);

    _physicalPathResolver =
        pathResolver ?? new WindowsPhysicalPathResolver();

    _rootPolicy =
        new ManagedDataRootPolicy(_physicalPathResolver);

    _activeCurrentRoot =
        PathIdentity.NormalizeForComparison(activeCurrentRoot);

    // existing assignments...
}
```

Do not rewrite the stored setting merely because its physical target is canonicalized. Preserve the user-selected lexical path unless a real transition is committed.

## 5.4 Required deterministic fake-resolver test

Add a test where:

```text
persisted setting = C:\Aliases\Current
fake resolver maps C:\Aliases\Current -> C:\Data\Current
active process root = C:\Data\Current
candidate = C:\Data\Third
```

Expected:

```text
transition does NOT throw settings mismatch
transition proceeds normally
```

## 5.5 Required real Windows integration test

Create a real junction alias to a seeded active library.

Persist the junction path in `settings.json`.

Construct coordinator with the resolved real physical root, matching actual startup behavior.

Then transition to a third safe folder.

Acceptance:

```text
persisted alias accepted as same active root
third-folder transition succeeds
settings becomes third folder
alias source remains intact
no false "settings do not match active root" error
```

---

# 6. CRUU6-002 — Validated target can be redirected after topology validation

**Severity:** HIGH  
**Area:** reparse-point TOCTOU / migration safety  
**Files:**  
`DataFolderTransitionCoordinator.cs`  
`DataRootRelationship.cs`  
`ManagedDataRootPolicy.cs`  
`DataFolderTransitionCoordinatorTests.cs`  
`WindowsPhysicalPathResolverIntegrationTests.cs`

## 6.1 Exact defect

Current flow:

```text
1. Resolve/validate target physically
2. Inspect target
3. possibly show modal confirmation
4. acquire lexical target .app.lock
5. inspect/fingerprint content
6. probe/copy
7. commit settings
```

The **path object itself** is not re-bound to the physical target after reservation.

A directory/reparse point can be replaced between steps 1 and 4.

For an empty target, target creation itself also changes resolution from:

```text
nearest existing ancestor + lexical remainder
```

to:

```text
actual existing directory
```

Therefore the physical target must be re-resolved under the reservation.

Content fingerprinting does not solve this. Fingerprints answer “did library content change?”, not “is this lexical path still the same physical directory?”.

## 6.2 Required invariant

A transition may commit only if:

```text
initial validated physical target
    ==
physical target after target reservation
    ==
physical target immediately before settings commit
```

and every revalidation must still satisfy:

```text
not current root
not ancestor/descendant of current
not bootstrap root overlap
not volume/share root
```

## 6.3 Required helper

Add:

```csharp
private DataRootRelationship RevalidateTargetIdentity(
    string cleanCurrent,
    string cleanTarget,
    string bootstrapRoot,
    DataRootRelationship expected)
{
    DataRootRelationship actual =
        _rootPolicy.ValidateTransition(
            cleanCurrent,
            cleanTarget,
            bootstrapRoot);

    if (actual.SamePhysicalRoot)
    {
        throw new InvalidOperationException(
            "The selected target now resolves to the active library.");
    }

    if (!PathIdentity.Equals(
            actual.PhysicalTarget,
            expected.PhysicalTarget))
    {
        throw new InvalidOperationException(
            "The physical target folder changed while the " +
            "data-folder transition was in progress. " +
            "Nothing was committed. Retry with a stable target.");
    }

    return actual;
}
```

`DataRootRelationship` must expose `PhysicalTarget`.

## 6.4 Existing target flow

Use:

```text
initial relationship
initial content inspection
confirmation
reservation acquire
PHYSICAL REVALIDATION #1
locked inspection + fingerprint compare
capability validation
PHYSICAL REVALIDATION #2
content fingerprint compare #2
settings CAS commit
```

Do not perform target file mutations before revalidation #1.

## 6.5 Empty target flow

Use:

```text
initial relationship
source snapshot
reservation acquire (may create root)
PHYSICAL REVALIDATION #1
target must still be Empty
migration transaction
copy
capability validation
PHYSICAL REVALIDATION #2
settings CAS commit
commit migration transaction
```

The revalidation immediately after root creation is mandatory.

## 6.6 Deterministic changing-resolver test

Extend `FakePhysicalPathResolver` to support sequential mappings or a callback.

Test:

```text
first target resolve  -> C:\Safe\Target
second target resolve -> C:\Bootstrap\Hijacked
```

Expected:

```text
transition throws
settings unchanged
no target library copy
no user source mutation
```

Also test swap to:

```text
active current root
drive root
different valid existing library directory
```

---

# 7. CRUU6-003 — Settings CAS is incomplete and non-atomic

**Severity:** HIGH  
**Area:** settings concurrency / authority  
**Files:**  
`AppSettingsRepository.cs`  
new `SettingsMutationLease.cs` or equivalent  
`AppSettingsRepositoryTests.cs`  
`DataFolderTransitionCoordinatorTests.cs`

## 7.1 Exact defect A — token covers only primary

Current token:

```csharp
public sealed record SettingsPrimaryWriteToken(
    bool Exists,
    byte[]? Sha256);
```

`settings.backup.json` is ignored.

But backup can be authoritative when primary is missing/corrupt, and it is protected future-version evidence even when primary is valid.

Example:

```text
primary missing
backup = valid root A
app recovers in memory
primary restore failed
transition captures primary token = Missing

external writer changes backup to root B
transition compares only primary = still Missing
transition Save writes new root C
backup B can be overwritten
```

## 7.2 Exact defect B — compare and write are not atomic

Current:

```text
CapturePrimaryWriteToken()
compare
return Save(settings)
```

There is a race between comparison and `Save()`.

Another writer can change settings in that gap.

The correct fix is not “compare one more time.” A final compare and mutation need a shared filesystem lease.

## 7.3 Required model

Replace `SettingsPrimaryWriteToken` with:

```csharp
public sealed record SettingsFileToken(
    bool Exists,
    byte[]? Sha256);

public sealed record SettingsWritePrecondition(
    SettingsFileToken Primary,
    SettingsFileToken Backup);
```

Add a bootstrap settings mutation lock:

```text
%LOCALAPPDATA%\PromptHelper\.settings.lock
```

For test-overridden settings paths, put it beside the overridden settings primary.

## 7.4 Required `SettingsMutationLease`

Use a file handle with:

```csharp
FileMode.OpenOrCreate
FileAccess.ReadWrite
FileShare.None
```

Do not hold this lock across the modal confirmation or entire migration copy.

Use it only around:

```text
settings recovery/synchronization mutation
settings precondition capture
final compare + settings write
```

## 7.5 Required repository API

Add:

```csharp
public sealed record SettingsTransitionSnapshot(
    AppSettings Settings,
    SettingsWritePrecondition Precondition,
    string? Warning);
```

Public API:

```csharp
public SettingsTransitionSnapshot
    LoadForTransitionAndCapturePrecondition()
{
    using var lease = AcquireMutationLease();

    SettingsLoadResult load =
        LoadOrRecoverCore();

    SettingsWritePrecondition token =
        CaptureWritePreconditionCore();

    return new SettingsTransitionSnapshot(
        CloneSettings(load.Settings),
        token,
        load.Warning);
}
```

Final commit:

```csharp
public SettingsSaveResult SaveIfUnchanged(
    AppSettings settings,
    SettingsWritePrecondition expected)
{
    using var lease = AcquireMutationLease();

    SettingsWritePrecondition actual =
        CaptureWritePreconditionCore();

    if (!WritePreconditionsEqual(
            expected,
            actual))
    {
        throw new InvalidOperationException(
            "Prompt Helper settings changed while the " +
            "data-folder transition was in progress. " +
            "Nothing was committed.");
    }

    return SaveCore(settings);
}
```

`SaveCore` must not reacquire the same lock.

Likewise:

```csharp
public SettingsLoadResult LoadOrRecover()
{
    using var lease = AcquireMutationLease();
    return LoadOrRecoverCore();
}

public SettingsSaveResult Save(AppSettings settings)
{
    using var lease = AcquireMutationLease();
    return SaveCore(settings);
}
```

This centralizes mutation serialization.

## 7.6 Important lock rule

Do not use the data-root `.app.lock` as the settings lock.

They have different scope:

```text
.app.lock       = one running editor per data library root
.settings.lock  = serialize bootstrap settings mutations
```

## 7.7 Required tests

Add:

```text
CRUU6_003_Backup_change_invalidates_transition_precondition
CRUU6_003_Backup_appearing_invalidates_transition_precondition
CRUU6_003_Backup_disappearing_invalidates_transition_precondition
CRUU6_003_Future_backup_appearing_before_commit_is_not_overwritten
CRUU6_003_Final_compare_and_write_happen_under_settings_lease
CRUU6_003_Second_settings_mutator_cannot_enter_commit_while_first_holds_lease
```

For synchronization tests, use events/barriers rather than arbitrary sleeps.

---

# 8. CRUU6-004 — Prompt Helper can invalidate its own transition token

**Severity:** MEDIUM-HIGH  
**Area:** settings recovery sequencing  
**Files:** `AppSettingsRepository.cs`, `DataFolderTransitionCoordinator.cs`, tests

## 8.1 Exact defect

Current coordinator:

```csharp
SettingsPrimaryWriteToken settingsToken =
    _settingsRepo.CapturePrimaryWriteToken();

string currentSettingsRoot =
    _settingsRepo.GetEffectiveDataRoot();
```

`GetEffectiveDataRoot()` calls `Load()` when no settings object is supplied.

`Load()` calls `LoadOrRecover()`.

`LoadOrRecover()` may mutate disk:

```text
repair missing/corrupt settings.json from backup
synchronize settings.backup.json from primary
```

Therefore the sequence can be:

```text
capture P0
LoadOrRecover changes P0 -> P1
later SaveIfPrimaryUnchanged compares expected P0 to P1
=> Prompt Helper rejects its own transition
```

## 8.2 Required fix

Phase A's API fixes this by returning:

```text
post-recovery settings object
+
post-recovery dual-file precondition
```

in one short settings lease.

Coordinator must use:

```csharp
SettingsTransitionSnapshot settingsSnapshot =
    _settingsRepo.LoadForTransitionAndCapturePrecondition();
```

and never call parameterless `GetEffectiveDataRoot()` during the transition.

Use:

```csharp
string settingsLexicalRoot =
    _settingsRepo.GetEffectiveDataRoot(
        settingsSnapshot.Settings);
```

## 8.3 Tests

Create a fault-injecting writer scenario:

```text
settings primary corrupt
backup valid
an earlier primary-repair attempt failed
transition begins
repair now succeeds
post-repair precondition captured
transition succeeds if no external mutation occurs
```

Also assert no self-generated false CAS failure.

---

# 9. CRUU6-005 — Partial File.Copy can escape rollback

**Severity:** HIGH  
**Area:** migration transaction ownership  
**Files:**  
`DataFolderMigrationService.cs`  
`IMigrationFileOps.cs`  
`FaultInjectingMigrationFileOps.cs`  
migration tests

## 9.1 Exact defect

Current code:

```csharp
_fileOps.CopyFile(
    sourcePath,
    destPath,
    overwrite: false);

tx.TrackCreatedFile(destPath);
```

Ownership is recorded only after copy returns.

A filesystem copy is not a transaction. A failure can occur after destination creation.

Then:

```text
destination partially exists
CopyFile throws
TrackCreatedFile never runs
transaction rollback has no record
rollback reports Success
partial file remains
```

This violates the CRUU5 “truthful rollback” goal.

## 9.2 Do NOT apply this tempting wrong fix

Do not simply move this before copy:

```csharp
tx.TrackCreatedFile(destPath);
_fileOps.CopyFile(...);
```

A race can cause another actor to create `destPath` after the existence check.

Rollback could then delete a file Prompt Helper did not create.

Ownership must be unambiguous.

## 9.3 Required safe copy protocol

For every copied file:

```text
1. Generate unique temp name in destination directory.
2. Create temp with CreateNew (Prompt Helper now owns it).
3. Register temp in migration transaction.
4. Copy bytes source -> owned temp.
5. Flush temp to disk.
6. Atomically move temp -> final path with overwrite=false.
7. Update transaction ownership from temp path to final path.
```

Suggested temp:

```text
.<final-name>.migration-<guid>.tmp
```

## 9.4 Required transaction API

Add a promotion API:

```csharp
public void PromoteCreatedFile(
    string oldOwnedPath,
    string newOwnedPath)
{
    int index = _createdFiles.FindIndex(
        x => PathIdentity.Equals(
            x,
            oldOwnedPath));

    if (index < 0)
    {
        throw new InvalidOperationException(
            "Cannot promote an untracked migration file.");
    }

    _createdFiles[index] = newOwnedPath;
}
```

Do not expose this publicly outside migration internals.

## 9.5 Required file operations

Prefer injected lower-level operations:

```csharp
internal interface IMigrationFileOps
{
    byte[] ReadAllBytes(string path);

    Stream CreateNewFile(string path);

    Stream OpenRead(string path);

    void MoveNoOverwrite(
        string source,
        string destination);

    IEnumerable<string> EnumeratePromptFiles(
        string directory);
}
```

Production implementations use `FileMode.CreateNew` and `File.Move(..., overwrite: false)`.

## 9.6 Required failure tests

Fault injection points:

```text
after temp creation
after first bytes written
after all bytes written before flush
during flush
before move
during move because final collision appears
```

Assertions after every failed transition:

```text
source unchanged
settings unchanged
no partial final file owned by this attempt
no migration temp file
foreign collision file preserved exactly
rollback result truthful
```

---

# 10. CRUU6-006 — Capability-probe residue can be invisible

**Severity:** MEDIUM-HIGH  
**Area:** rollback truth / capability validation  
**Files:**  
`DataRootCapabilityValidator.cs`  
new `DataRootCapabilityProbeException.cs` or equivalent  
`DataFolderTransitionCoordinator.cs`  
tests

## 10.1 Exact defect

Current probe:

```text
create .prompthelper-write-probe-...
write
replace
delete file
delete directory
```

On error it performs best-effort cleanup, but cleanup errors are swallowed.

During an empty-target transition these probe objects are not in `MigrationTargetTransaction`.

Therefore:

```text
capability check fails
probe cleanup also fails
migration tx deletes its own copied files
tx reports rollback success
probe residue remains
user is not told target is dirty
```

## 10.2 Required cleanup contract

Add an internal ownership journal:

```csharp
internal interface ICreatedPathJournal
{
    void TrackCreatedFile(string path);
    void TrackCreatedDirectory(string path);
}
```

Have `MigrationTargetTransaction` implement it.

Allow:

```csharp
public void ValidateWritable(
    string root,
    ICreatedPathJournal? journal = null,
    ExistingLibraryCapabilityContext? existing = null)
```

When the probe creates a directory/file, record ownership immediately after successful creation.

For existing-target validation where no migration transaction exists, cleanup failure must be explicit.

## 10.3 Explicit exception

Add:

```csharp
public sealed class DataRootCapabilityProbeException
    : IOException
{
    public DataRootCapabilityProbeException(
        string root,
        Exception original,
        IReadOnlyList<
            MigrationRollbackFailure> cleanupFailures)
        : base(
            BuildMessage(
                root,
                cleanupFailures),
            original)
    {
        Root = root;
        CleanupFailures = cleanupFailures;
    }

    public string Root { get; }

    public IReadOnlyList<
        MigrationRollbackFailure>
        CleanupFailures { get; }
}
```

Do not hide cleanup failure behind the original probe exception.

## 10.4 Coordinator integration

Empty target:

```text
capability fails
-> rollback migration journal
-> combine capability cleanup failures + migration rollback failures
-> if any residue failure exists, throw MigrationRollbackException
```

Existing target:

```text
capability fails
-> if probe cleanup succeeded, show ordinary capability error
-> if cleanup failed, show explicit residue paths
```

## 10.5 Tests

Inject:

```text
probe file delete failure
probe directory delete failure
both
```

Assert warning/error includes exact residual path.

---

# 11. CRUU6-007 — Reservation cleanup is still silent

**Severity:** MEDIUM  
**Area:** transition cleanup / target lock residue  
**Files:** `TargetRootReservation.cs`, coordinator, tests

## 11.1 Exact defect

`TargetRootReservation.Dispose()` catches and suppresses failure deleting:

```text
reservation-created .app.lock
reservation-created empty root
```

This is safe for ordinary stale unlocked lock files but conflicts with CRUU5's stronger failed-transition cleanup truth.

## 11.2 Required API

Make release explicit and idempotent:

```csharp
public sealed record
    TargetReservationCleanupResult(
        IReadOnlyList<
            MigrationRollbackFailure> Failures)
{
    public bool Success =>
        Failures.Count == 0;
}
```

Add:

```csharp
public TargetReservationCleanupResult Release()
```

which closes the handle and records failures for lock-file/root deletion.

`Dispose()` may call `Release()` as fallback, but production coordinator must explicitly inspect the result.

## 11.3 Failure semantics

On failed transition:

```text
migration rollback failures
+
capability cleanup failures
+
reservation cleanup failures
```

must all appear in the final cleanup report.

On successful transition, failure to delete an unlocked stale `.app.lock` should not undo committed settings; surface a warning.

## 11.4 Tests

Inject lock-file and root-directory deletion faults. Do not depend on machine ACLs for unit tests.

---

# 12. CRUU6-008 — Existing-target fingerprint can be a hybrid snapshot

**Severity:** MEDIUM-HIGH  
**Area:** target consistency / confirmation integrity  
**Files:** `DataFolderMigrationService.cs`, migration tests

## 12.1 Exact defect

Current `InspectTarget()`:

```text
ReadAllText(metadata)
parse into document A
validate document A and body existence

later:
ComputeEffectiveLibraryFingerprint(...)
    ReadAllBytes(metadata) again
    read bodies using document A IDs
```

If metadata changes between the two reads:

```text
EffectiveDocument = A
Fingerprint metadata bytes = B
Prompt ID set = A
```

That is not a coherent target snapshot.

## 12.2 Required model

Read and fingerprint the same bytes.

Introduce:

```csharp
internal sealed record
    TargetContentSnapshot(
        byte[] MetadataBytes,
        LibraryDocument Document,
        IReadOnlyDictionary<Guid, byte[]>
            PromptHashes,
        byte[] CombinedFingerprint);
```

Capture metadata bytes once, parse those exact bytes, hash active prompt bodies, then re-read metadata and active bodies once to prove the snapshot stayed stable. If any changed, abort with a retry error.

The combined fingerprint must be calculated from the already captured metadata bytes and prompt hashes. It must not reopen metadata.

## 12.3 Required tests

Fault-inject:

```text
metadata read #1 = valid library A
metadata read #2 = valid library B
```

Inspection must reject the unstable target instead of returning `document A + fingerprint B`.

Also inject active prompt-body change between passes.

---

# 13. CRUU6-009 — Future-schema folder selection can escape to fatal handler

**Severity:** MEDIUM-HIGH  
**Area:** WPF error contract  
**Files:**  
`SettingsDialog.xaml.cs`  
`UnsupportedLibrarySchemaException.cs`  
`UnsupportedSettingsSchemaException.cs`  
dialog tests

## 13.1 Exact defect

Coordinator intentionally throws `UnsupportedLibrarySchemaException` when a selected target is from a newer Prompt Helper schema.

Both schema exceptions currently inherit directly from `Exception`.

`SettingsDialog.SaveButton_Click` handles a filtered set including:

```text
IOException
UnauthorizedAccessException
SecurityException
InvalidDataException
ArgumentException
NotSupportedException
InvalidOperationException
```

It does not include either unsupported-schema exception.

Therefore a normal safety rejection can escape to `DispatcherUnhandledException`, which displays a fatal error and shuts down the application.

## 13.2 Required fix

Do **not** change the dialog to `catch (Exception)`.

Use explicit catches first:

```csharp
catch (UnsupportedLibrarySchemaException ex)
{
    _confirmationService.ShowWarning(
        "The selected folder contains a Prompt Helper " +
        $"library created by a newer schema " +
        $"({ex.SchemaVersion}).\r\n\r\n" +
        "The folder was not selected and the current " +
        "data-folder setting was not changed.",
        "Newer Library Version");

    return;
}
catch (UnsupportedSettingsSchemaException ex)
{
    _confirmationService.ShowWarning(
        "Prompt Helper settings changed to a newer " +
        $"schema ({ex.SchemaVersion}) while this " +
        "dialog was open.\r\n\r\n" +
        "No data-folder change was committed. " +
        "Close Prompt Helper and use the newer version.",
        "Newer Settings Version");

    return;
}
catch (Exception ex) when (...)
{
    // existing operational filter
}
```

Do not reinterpret future schema as corruption.

## 13.3 Required dialog regression

Assertions:

```text
future target => warning shown
dialog remains usable
RestartRequired false
settings bytes unchanged
no application lifetime shutdown requested
no unhandled exception
```

---

# 14. CRUU6-010 — Scratch probe does not prove existing library files are replaceable

**Severity:** MEDIUM  
**Area:** existing target usability  
**Files:** `DataRootCapabilityValidator.cs`, coordinator, tests

## 14.1 Exact defect

Current validator proves:

```text
can create probe subdirectory
can create probe file
can File.Replace that probe file
can delete probe objects
```

For an existing library, actual operations later replace:

```text
library.json
library.backup.json
prompts\<id>.md
```

A folder can permit new-file creation while an existing managed file is read-only or has a restrictive file ACL.

`.NET File.Replace` documents `UnauthorizedAccessException` when the destination is read-only or the caller lacks permission.

Therefore an existing-library switch can pass the scratch probe but later fail on normal edits.

## 14.2 Required conservative managed-file validation

Do not mutate the existing library just to test it.

Add context:

```csharp
internal sealed record
    ExistingLibraryCapabilityContext(
        string MetadataPath,
        LibraryDocument Document);
```

After scratch probe succeeds, validate managed files conservatively.

For every existing managed file Prompt Helper may replace:

```text
effective metadata file
library.backup.json if present
every active prompt body
```

Check:

```csharp
FileAttributes attributes =
    File.GetAttributes(path);

if ((attributes & FileAttributes.ReadOnly) != 0)
{
    throw new UnauthorizedAccessException(
        $"Managed Prompt Helper file is read-only: '{path}'.");
}

using FileStream stream = new(
    path,
    FileMode.Open,
    FileAccess.ReadWrite,
    FileShare.Read);

if (!stream.CanWrite)
{
    throw new UnauthorizedAccessException(
        $"Managed Prompt Helper file is not writable: '{path}'.");
}
```

This is intentionally conservative.

Do not change file attributes automatically.

## 14.3 Required test

Set existing target `library.json` to read-only.

The switch must be rejected before settings change.

Restore attributes in `finally`.

---

# 15. CRUU6-011 — Unavailable custom root loses dedicated recovery message

**Severity:** LOW-MEDIUM  
**Area:** startup diagnostics / removable/network storage  
**Files:** `ManagedDataRootPolicy.cs`, `App.xaml.cs`, tests

## 15.1 Exact defect

Startup currently runs physical resolution before `DataRootBootstrapValidator.ValidateConfiguredRoot(...)`.

An unavailable network/removable/custom path may fail inside the physical resolver first.

That reaches the generic outer startup error instead of the dedicated configured-folder-unavailable message.

This is not a data-loss bug because fail-closed behavior prevents initialization. It is a recovery-guidance regression.

## 15.2 Required classification

Preserve fail-closed behavior.

Do not fall back to default root.

Add a dedicated physical-root-unavailable exception carrying the configured path. Map configured-target resolution failure to it while leaving unsafe topology errors distinct.

## 15.3 App handling

Add a specific catch before the generic startup catch that reuses the existing “configured folder unavailable / no new library created / reconnect or repair setting” guidance.

## 15.4 Tests

Fake resolver throws:

```text
DirectoryNotFoundException
DriveNotFoundException
Win32Exception path-not-found
```

Expected:

```text
dedicated unavailable classification
no root creation
no settings mutation
no fallback library
```

---

# 16. CRUU6-012 — Real icon asset remains a release blocker

**Severity:** RELEASE BLOCKER  
**Area:** release asset dependency

Current project inclusion remains conditional on `Assets\PromptHelper.ico`, and the audited repository does not expose `src/PromptHelper/Assets`.

README still says the release asset is pending.

Until the real SVG is supplied:

```text
development build/test may proceed
normal non-strict CI may proceed
strict release acceptance = BLOCKED
```

Once the real SVG is supplied:

```powershell
pwsh ./tools/GenerateAppIcon.ps1

pwsh ./tools/VerifyReleaseAssets.ps1 `
  -RequireIcon

dotnet build PromptHelper.slnx `
  -c Release

dotnet test PromptHelper.slnx `
  -c Release

dotnet publish `
  src/PromptHelper/PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/publish-check

pwsh ./tools/VerifyReleaseAssets.ps1 `
  -RequireIcon `
  -PublishedExe `
  artifacts/publish-check/PromptHelper.exe
```

Then manually verify:

```text
Explorer EXE icon
taskbar icon
Alt+Tab icon
window title icon
```

Do not synthesize or approximate the missing design.

---

# 17. Ordered implementation phases

## PHASE A — Settings transaction implementation

Goal:

```text
one coherent settings snapshot
dual-file precondition
short shared settings mutation lease
atomic compare-with-respect-to-other Prompt Helper writers
```

Required files:

```text
src/PromptHelper/Services/SettingsMutationLease.cs
src/PromptHelper/Services/AppSettingsRepository.cs
tests/PromptHelper.Tests/AppSettingsRepositoryTests.cs
tests/PromptHelper.Tests/DataFolderTransitionCoordinatorTests.cs
```

Acceptance:

```text
valid primary + changed backup => CAS rejects
missing primary + changed valid backup => CAS rejects
corrupt primary repaired => token captured AFTER repair
future backup never overwritten
future primary never overwritten
no compare/write race among repository writers
```

## PHASE B — Physical active/settings identity

Implement CRUU6-001 exactly.

Acceptance:

```text
persisted junction alias startup -> physical active root
later transition to third root succeeds
no settings mismatch false positive
```

## PHASE C — Bind target to physical identity

Implement CRUU6-002.

Acceptance:

```text
changing resolver target => abort
junction swap => abort
no copy/probe/settings mutation after unsafe swap
```

## PHASE D — Rollback-safe file ownership

Implement CRUU6-005.

Acceptance matrix:

| Failure point | Expected |
|---|---|
| temp create | no owned residue |
| mid-stream copy | temp removed/reported |
| flush | temp removed/reported |
| final path collision | foreign file preserved |
| final move | owned temp removed/reported |
| settings failure after copy | all promoted files removed/reported |

## PHASE E — Unified cleanup truth

Goal:

```text
migration copy cleanup
capability probe cleanup
reservation cleanup
```

must share one truthful cleanup model.

Do not throw away cleanup failure details.

## PHASE F — Coherent target snapshot

Implement CRUU6-008.

If a stable snapshot cannot be captured, abort and request retry. Do not guess authority.

## PHASE G — UI exception contract

Implement CRUU6-009.

Controlled operational/safety errors stay in Settings dialog.

Fatal dispatcher handling remains for genuinely unexpected exceptions.

## PHASE H — Existing managed-file capability

Implement CRUU6-010.

Do not mutate user metadata or clear read-only attributes to make validation pass.

## PHASE I — Startup unavailable-path diagnostics

Implement CRUU6-011.

No automatic fallback. No implicit creation.

## PHASE J — Full regression and Windows integration

Run all old and new tests, then five full-suite runs and publish verification.

## PHASE K — Icon

Only execute strict icon gate after real SVG exists.

---

# 18. File-by-file change map

## `AppSettingsRepository.cs`

Required:

```text
remove primary-only transition token
add dual-file SettingsWritePrecondition
add settings mutation lease
split public wrappers from Core methods
add LoadForTransitionAndCapturePrecondition
make final compare + SaveCore execute under one lease
```

Do not alter schema version.

## `SettingsMutationLease.cs`

New:

```text
FileShare.None lease beside settings files
idempotent dispose
controlled sharing/busy exception
```

Do not wait forever.

## `DataFolderTransitionCoordinator.cs`

Required:

```text
use SettingsTransitionSnapshot
compare settings root physically
retain initial DataRootRelationship
revalidate physical target under reservation
revalidate again before settings commit
use dual-file settings precondition
pass migration journal to capability validator
aggregate cleanup failures
```

## `DataRootCapabilityValidator.cs`

Required:

```text
journal owned probe paths
explicit cleanup-failure exception
optional existing-library capability context
read-only/writable check for actual managed files
```

## `TargetRootReservation.cs`

Required:

```text
explicit Release()
return cleanup result
Dispose only fallback
inject deletion filesystem seam for tests
```

## `DataFolderMigrationService.cs`

Required:

```text
owned temp copy protocol
transaction file promotion
coherent target metadata bytes + body hashes
no hybrid fingerprint reads
```

## `IMigrationFileOps.cs`

Required:

```text
stream/temp create
read stream
no-overwrite move
fault-injection seams
```

## `SettingsDialog.xaml.cs`

Required:

```text
explicit UnsupportedLibrarySchemaException catch
explicit UnsupportedSettingsSchemaException catch
no catch-all
```

## `ManagedDataRootPolicy.cs`

Required:

```text
configured-root resolution failure classification
preserve fail-closed topology behavior
```

## `App.xaml.cs`

Required:

```text
dedicated unavailable physical-root catch
no default-root fallback
```

## Tests

Add CRUU6-specific regression tests. Do not remove older CRUU1–CRUU5 tests.

---

# 19. Fault-injection matrix

| Area | Injected event | Expected |
|---|---|---|
| settings | backup changes after snapshot | transition aborts |
| settings | primary changes after snapshot | transition aborts |
| settings | both change | transition aborts |
| settings | future backup appears | no downgrade |
| settings | another repository holds `.settings.lock` | controlled busy failure |
| path | persisted alias -> active physical root | accepted as same authority |
| path | target physical identity changes after confirmation | abort |
| path | target becomes current root | abort |
| path | target becomes bootstrap descendant | abort |
| path | target becomes volume root | abort |
| copy | destination temp creation fails | no residue |
| copy | mid-copy failure | owned temp cleaned/reported |
| copy | foreign final path appears | foreign bytes unchanged |
| capability | probe replace fails | controlled failure |
| capability | probe cleanup fails | residue reported |
| reservation | lock-file cleanup fails | residue reported/warning |
| reservation | new root cleanup fails | residue reported |
| fingerprint | metadata changes between reads | unstable-target abort |
| fingerprint | body changes during inspection | unstable-target abort |
| UI | target future schema | dialog warning, no fatal exception |
| UI | settings future schema mid-dialog | dialog warning, no commit |
| existing target | `library.json` read-only | switch rejected before settings write |
| startup | configured drive unavailable | dedicated no-create unavailable message |

---

# 20. Real Windows integration matrix

Run on Windows 11 with NTFS.

## Junction cases

```text
A. candidate alias of active root -> no-op
B. persisted settings alias of active root -> later third-root transition succeeds
C. alias into bootstrap -> rejected
D. alias to drive root -> rejected
E. target junction changed after initial validation -> rejected
```

A deterministic fake-resolver test for E remains mandatory even if the real timing test is difficult.

## Read-only existing target

```text
1. Seed existing library target.
2. Set library.json ReadOnly.
3. Select target.
4. Verify settings do not change.
5. Verify clear permission/read-only error.
6. Restore attribute.
```

## Unavailable removable/network target

If accessible test hardware/path exists:

```text
1. Configure custom data root.
2. Make root unavailable.
3. Start application.
4. Verify no directory/library creation.
5. Verify dedicated unavailable-root guidance.
```

Do not make CI depend on a real network share.

---

# 21. Test design rules

1. No arbitrary `Thread.Sleep` for synchronization tests.
2. Use events, barriers, callbacks, or injected seams.
3. No source-string “test” when runtime behavior can be tested.
4. Never conditionally return PASS because a required source file is missing.
5. Windows-only junction tests may use OS guards, but on the Windows CI runner they must execute.
6. A skipped required junction test on `windows-latest` must be treated as missing evidence.
7. Do not delete old regression tests to make new changes pass.
8. Each CRUU6 finding needs at least one direct regression test.
9. High findings need happy-path and adversarial coverage.
10. Cleanup tests must inspect disk after exception.

---

# 22. Suggested test names

```text
CRUU6_001_Persisted_alias_of_active_root_allows_transition
CRUU6_001_Real_persisted_junction_alias_allows_third_root_transition

CRUU6_002_Target_physical_identity_change_after_validation_aborts
CRUU6_002_Target_becomes_bootstrap_alias_after_reservation_aborts
CRUU6_002_Empty_target_revalidated_after_reservation_creation

CRUU6_003_Backup_change_invalidates_settings_precondition
CRUU6_003_Settings_compare_and_save_share_mutation_lease
CRUU6_003_Future_backup_appearing_during_transition_is_not_overwritten

CRUU6_004_Post_recovery_precondition_does_not_self_invalidate

CRUU6_005_Mid_copy_failure_leaves_no_untracked_partial_file
CRUU6_005_Foreign_collision_file_is_never_deleted_by_rollback

CRUU6_006_Probe_cleanup_failure_is_reported
CRUU6_006_Probe_residue_is_in_transition_cleanup_report

CRUU6_007_Reservation_lock_cleanup_failure_is_reported
CRUU6_007_Reservation_root_cleanup_failure_is_reported

CRUU6_008_Metadata_change_during_fingerprint_capture_aborts
CRUU6_008_Prompt_body_change_during_fingerprint_capture_aborts

CRUU6_009_Future_target_schema_is_controlled_dialog_error
CRUU6_009_Future_settings_schema_mid_transition_is_controlled_dialog_error

CRUU6_010_Readonly_existing_library_primary_rejects_switch
CRUU6_010_Readonly_active_prompt_file_rejects_existing_target

CRUU6_011_Unavailable_configured_root_uses_dedicated_safety_error
```

---

# 23. Verification commands

From repository root on Windows:

```powershell
dotnet --info
```

```powershell
dotnet restore PromptHelper.slnx
```

```powershell
dotnet build PromptHelper.slnx `
  -c Release `
  --no-restore
```

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --logger "trx;LogFileName=cruu6-full.trx"
```

Five consecutive runs:

```powershell
1..5 | ForEach-Object {
    Write-Host "CRUU6 stress run $_"

    dotnet test PromptHelper.slnx `
      -c Release `
      --no-build `
      --logger "trx;LogFileName=cruu6-stress-$_.trx"

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
```

Publish:

```powershell
Remove-Item `
  -Recurse `
  -Force `
  artifacts\publish-check `
  -ErrorAction SilentlyContinue

dotnet publish `
  src/PromptHelper/PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/publish-check
```

Payload check:

```powershell
$required = @(
  "artifacts\publish-check\PromptHelper.exe",
  "artifacts\publish-check\LICENSE",
  "artifacts\publish-check\THIRD_PARTY_NOTICES.md"
)

foreach ($item in $required) {
    if (-not (Test-Path $item)) {
        throw "Missing publish artifact: $item"
    }
}
```

Non-strict assets:

```powershell
pwsh ./tools/VerifyReleaseAssets.ps1
```

Strict assets only after real SVG exists:

```powershell
pwsh ./tools/VerifyReleaseAssets.ps1 `
  -RequireIcon `
  -PublishedExe `
  artifacts/publish-check/PromptHelper.exe
```

---

# 24. Manual GUI regression

After automated tests:

```text
1. Launch published PromptHelper.exe.
2. Verify default library loads.
3. Create category.
4. Rename category.
5. Create prompt.
6. Edit prompt headline/content.
7. Move prompt.
8. Duplicate prompt.
9. Copy prompt.
10. Verify recent-copy bar.
11. Select empty new data folder.
12. Verify migration success.
13. Verify process closes.
14. Reopen and verify migrated data.
15. Select existing valid library.
16. Verify explicit no-copy/no-merge confirmation.
17. Cancel once; verify no settings change.
18. Confirm once; reopen and verify selected library.
19. Select future-schema library; verify controlled warning, app stays alive.
20. Exercise persisted junction alias scenario.
21. Exercise read-only target rejection.
22. Verify unavailable custom-root safety message.
```

---

# 25. Weak-model “do not” traps

The implementer must not:

```text
- remove physical path checks because they are inconvenient;
- compare junction/symlink paths lexically for safety identity;
- hold .settings.lock across a modal user confirmation;
- use only settings.json in the transition precondition;
- add another compare without locking and call it atomic;
- track a final migration path before Prompt Helper owns it;
- delete a foreign file after a collision race;
- swallow capability cleanup failure;
- swallow reservation cleanup failure in the normal explicit coordinator path;
- use catch(Exception) in SettingsDialog;
- treat future schema as corrupt JSON;
- mutate read-only flags to make a target pass;
- rewrite the persisted alias just to make equality tests pass;
- initialize a default library when a custom configured root is unavailable;
- invent a logo;
- disable the real junction tests;
- remove old tests;
- weaken existing backup/future-schema protections.
```

---

# 26. Required implementation evidence report

After implementation, return:

```text
CRUU6 IMPLEMENTATION EVIDENCE

Commit / branch:
- branch:
- commit:

Build:
- exact command:
- exit code:
- warnings:
- errors:

Tests:
- exact command:
- total:
- passed:
- failed:
- skipped:

Five-run stress:
- run 1:
- run 2:
- run 3:
- run 4:
- run 5:

CRUU6 regression tests:
- 001:
- 002:
- 003:
- 004:
- 005:
- 006:
- 007:
- 008:
- 009:
- 010:
- 011:

Windows junction integration:
- executed:
- passed:
- skipped:
- reason if skipped:

Publish:
- command:
- exit code:
- PromptHelper.exe:
- LICENSE:
- THIRD_PARTY_NOTICES.md:

Release icon:
- real SVG present: YES/NO
- generated ICO present: YES/NO
- strict release gate run: YES/NO
- result:
```

Do not report “all tests pass” without the exact total.

---

# 27. Definition of done

CRUU6 product/code work is complete only when all are true:

```text
[ ] Persisted lexical alias and active physical root are treated as same authority.
[ ] Target physical identity is revalidated after reservation.
[ ] Target physical identity is unchanged immediately before commit.
[ ] Settings precondition covers primary AND backup.
[ ] Final settings compare + write are serialized under a settings mutation lease.
[ ] Precondition is captured after any recovery/sync writes.
[ ] Mid-copy failures cannot leave an untracked partial owned file.
[ ] Foreign collision files are never deleted.
[ ] Capability probe residue is journaled or explicitly reported.
[ ] Reservation cleanup failure is explicitly reported.
[ ] Existing-target fingerprint is a coherent snapshot.
[ ] Future-schema target selection remains a controlled dialog error.
[ ] Existing read-only managed files cause pre-switch rejection.
[ ] Unavailable configured roots retain no-create safety and dedicated guidance.
[ ] Release build passes.
[ ] Full test suite passes.
[ ] Five consecutive runs pass.
[ ] Real Windows junction tests execute on Windows.
[ ] Self-contained win-x64 publish succeeds.
```

Release completion additionally requires:

```text
[ ] real PromptHelperLogo.svg supplied
[ ] ICO generated from that SVG
[ ] strict release asset gate passes
[ ] published EXE exposes icon
[ ] Explorer/taskbar/Alt+Tab/window icon manually verified
```

If all code findings are closed but the logo is still absent, use exactly:

```text
CRUU6 PRODUCT/CODE FIXES CLEAN /
RELEASE ICON ASSET DEPENDENCY STILL OPEN
```

Do not call release complete.

---

# 28. Copy-ready implementation prompt

```text
ROLE
You are the implementation model for Prompt Helper CRUU6.

AUTHORITY
Implement the supplied cruu6.md exactly.
The audited baseline is main commit
197196035b9ebf82c43c9a37ac4ed33b81bc8005.
If main has advanced, compare first and preserve any already-landed
CRUU6-equivalent fixes.

PURPOSE
Close CRUU6-001 through CRUU6-011 without changing product behavior
outside the defect fixes. CRUU6-012 is an external release-asset
dependency and MUST NOT be faked.

MANDATORY ORDER
A settings transaction/precondition
B physical settings-root identity
C physical target rebinding/revalidation
D rollback-safe owned-temp migration copy
E unified cleanup truth
F coherent target fingerprint
G controlled schema UI errors
H existing managed-file capability
I unavailable-root diagnostics
J full regression and Windows integration
K icon strict gate only if real SVG exists

NON-NEGOTIABLES
- Never fabricate PromptHelperLogo.svg.
- Never delete user source data.
- Never merge existing target libraries.
- Never fall back to default root when a configured custom root is unavailable.
- Never remove physical junction/symlink safety.
- Never implement settings CAS with primary-only state.
- Never call compare-then-write atomic without a shared settings mutation lease.
- Never delete a file unless the transition can prove it created/owns it.
- Never swallow cleanup failures in the explicit transition path.
- Never use catch(Exception) to hide future-schema errors.
- Never remove or weaken existing CRUU1-CRUU5 tests.

TESTING
Add direct regression tests for every CRUU6 item.
Add deterministic fault injection for copy, cleanup, settings and
physical-target race cases.
Real NTFS junction tests must execute on Windows.
Run Release build, full suite, then five consecutive full-suite runs.
Publish self-contained win-x64 and verify required payload.

OUTPUT
Return the implementation evidence report from cruu6.md.
Do not claim tests or Windows integration passed unless they actually ran.
```

---

# 29. Audit-source notes

The following current-source behaviors directly support the findings:

```text
App.xaml.cs
- configured root is physically resolved and the returned physical path becomes AppPaths/MainViewModel root.

DataFolderTransitionCoordinator.cs
- active root is compared lexically against GetEffectiveDataRoot().
- primary-only settings token is captured before GetEffectiveDataRoot().
- target physical relationship is validated before reservation but not rebound after reservation.

AppSettingsRepository.cs
- SettingsPrimaryWriteToken contains only settings.json state.
- SaveIfPrimaryUnchanged compares, then calls Save separately.
- GetEffectiveDataRoot() calls Load() when no settings object is supplied.
- LoadOrRecover() can rewrite primary/backup.

DataFolderMigrationService.cs
- target metadata is parsed before fingerprint function re-reads metadata.
- CopyFileNoOverwrite tracks destination only after injected File.Copy returns.
- migration transaction cleanup is explicit, but only for paths it knows it owns.

DataRootCapabilityValidator.cs
- probe cleanup failures are best-effort/silent.
- capability probe tests only scratch paths, not actual existing managed files.

TargetRootReservation.cs
- reservation cleanup still suppresses lock/root deletion failures.

SettingsDialog.xaml.cs
- filtered catch does not include the two Unsupported*SchemaException types.

UnsupportedLibrarySchemaException.cs
UnsupportedSettingsSchemaException.cs
- both inherit directly from Exception.

PromptHelper.csproj / README.md
- icon remains conditional and real release logo is still pending.
```

---

# 30. Final audit verdict

At commit:

```text
197196035b9ebf82c43c9a37ac4ed33b81bc8005
```

CRUU5 is a significant improvement and most of its intended fixes are visible in source.

However, the source is **not yet zero-defect accepted** because the CRUU6 findings above remain materially open.

Highest-priority closure sequence:

```text
dual-file settings transaction
-> physical alias authority
-> target physical identity binding
-> rollback-safe copy ownership
-> truthful cleanup
-> coherent target snapshot
-> controlled future-schema UI
-> existing managed-file capability
-> unavailable-root diagnostics
```

Only after those are closed and actually exercised on Windows should product-clean acceptance be granted.

Strict release acceptance additionally remains blocked until the real authoritative logo asset is supplied.
