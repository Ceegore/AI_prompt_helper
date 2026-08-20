# CRUU5 — Post-CRUU4 Deep Regression Audit & Deterministic Weak-Model Repair Blueprint

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited commit:** `bdbaa9e02d28249274523cfd317a86f9624c685e`  
**Previous repair authority:** `cruu1.md`, `cruu2.md`, `cruu3.md`, `cruu4.md`  
**Purpose:** independently re-audit the implementation after CRUU4, find residual correctness/data-safety/verification defects, and give a weak implementation model an exact, low-choice repair path.

---

# 1. Executive result

CRUU4 **substantially landed**. The repository now contains the intended transition coordinator, target-root reservation, physical-path resolver, managed-root policy, coherent primary migration snapshot, target rollback transaction, write-capability checks, future-schema preservation during startup, and strict/manual release-gate infrastructure.

However, CRUU4 moved important safety responsibilities into new layers. A fresh audit of those layers found **new second-order defects and verification gaps**.

The correct status is:

```text
CRUU4 STRUCTURAL IMPLEMENTATION       = SUBSTANTIALLY LANDED
CURRENT AUDITED COMMIT                = bdbaa9e02d28249274523cfd317a86f9624c685e
SOURCE-LEVEL POST-CRUU4 AUDIT         = COMPLETED
NEW CRUU5 FINDINGS                    = OPEN
INDEPENDENT WINDOWS/.NET EXECUTION    = NOT AVAILABLE IN THIS AUDIT ENVIRONMENT
GITHUB COMBINED STATUS EVIDENCE       = NO STATUS ENTRIES RETURNED FOR AUDITED COMMIT
FINAL PRODUCTION-CLEAN ACCEPTANCE     = NOT YET GRANTED
STRICT RELEASE ACCEPTANCE             = BLOCKED BY AUTHORITATIVE LOGO ASSET
```

This audit environment is Linux and does not expose a usable .NET SDK/Windows WPF runtime. Therefore this document does **not** claim that it reran the Windows test suite. The findings below are direct source-level defects/gaps found in the actual pushed `main` commit.

Do not interpret “existing tests pass” as proof for cases the existing tests do not represent.

---

# 2. New CRUU5 finding table

| ID | Severity | Finding |
|---|---|---|
| CRUU5-001 | HIGH | `AppSettingsRepository.Save()` can still overwrite future-schema settings files at write time |
| CRUU5-002 | HIGH | Settings JSON does not strictly require exactly one integer `schemaVersion`; missing/duplicate case-variants can bypass authority |
| CRUU5-003 | HIGH | Normal library mutations still overwrite a future-schema `library.backup.json`; write-time library authority is not centralized |
| CRUU5-004 | HIGH | Data-folder transition derives the source root from mutable bootstrap settings instead of the active process root |
| CRUU5-005 | MEDIUM-HIGH | Transition settings commit has no compare-and-swap/precondition; an external same-session settings change can be silently overwritten |
| CRUU5-006 | HIGH | Physical-path safety is fail-open: resolver failures silently fall back to lexical checks |
| CRUU5-007 | HIGH | A lexical directory alias/junction that physically resolves to a drive/UNC share root bypasses the no-volume-root rule |
| CRUU5-008 | MEDIUM | A physical alias of the currently active root is “allowed” but not returned as a no-op; it can produce a false “target in use” error/restart path |
| CRUU5-009 | MEDIUM | `TargetRootReservation` can leave a newly created target directory behind after failed transitions |
| CRUU5-010 | MEDIUM | Existing-target confirmation revalidation compares only target *kind*, not the actual library snapshot/fingerprint |
| CRUU5-011 | MEDIUM | Public/legacy `DataFolderMigrationService.PrepareTarget()` bypasses the coordinator/reservation contract and is still used by an E2E test |
| CRUU5-012 | MEDIUM | Rollback cleanup failures are silently swallowed, so a failed migration can leave a dirty target without telling the user |
| CRUU5-013 | MEDIUM verification gap | Physical-path logic is tested mainly with a fake resolver; no real Windows junction/reparse integration gate proves the P/Invoke behavior |
| CRUU5-014 | RELEASE BLOCKER | The authoritative `PromptHelperLogo.svg` / generated ICO is still absent from the audited repository |

These are **not** reasons to reopen settled UI/product design. Fix only the defects described here.

---

# 3. Locked product and architecture decisions

The weak implementation model must preserve all of these:

1. WPF/.NET 10 remains the product stack.
2. Prompt bodies remain individual local Markdown files under `prompts/`.
3. `library.json` remains primary structural metadata.
4. `library.backup.json` remains structural safety backup.
5. Bootstrap settings remain in `%LOCALAPPDATA%\PromptHelper`.
6. Custom data roots remain supported.
7. Existing valid target libraries can be selected without merge/overwrite.
8. Empty targets receive a copy of the current active library.
9. The old source root is never deleted by migration.
10. Root changes still require a process boundary; no hot repository swap.
11. One running Prompt Helper instance holds the active library `.app.lock`.
12. `PromptRecord.Title == null` remains automatic headline mode.
13. Editor wrapping remains visual-only.
14. Recent-copy state remains session-only, unique, newest-first, max three.
15. Current schema versions stay `1` in this repair round.
16. A future-schema primary must never be downgraded by an older build.
17. A future-schema backup may be ignored for current operation when a compatible primary is valid, but must never be overwritten by the older build.
18. Resolver/path uncertainty must fail **closed** for a data-root transition.
19. The **active process root** is authoritative for what this process is editing; a mutable bootstrap pointer is not.
20. Never invent or synthesize the missing logo. Use only the real supplied design asset.

---

# 4. Exact implementation order

Do not repair findings in random order.

```text
PHASE A  CRUU5-002  strict settings schema inspection
PHASE B  CRUU5-001  write-time settings authority
PHASE C  CRUU5-003  write-time library authority
PHASE D  CRUU5-006/007/008 shared physical-root relationship API
PHASE E  CRUU5-004/005 active-root authority + settings precondition token
PHASE F  CRUU5-009/010 target reservation/fingerprint correctness
PHASE G  CRUU5-011/012 remove unsafe transition bypass + truthful rollback reporting
PHASE H  CRUU5-013 real Windows junction integration tests
PHASE I  full regression + five repeated test runs
PHASE J  CRUU5-014 strict icon/release gate only after real SVG is supplied
```

Why the order matters:

- The settings write token must use the strict parser from Phase A.
- Transition code must consume the path relationship result from Phase D.
- Fingerprint and rollback changes should be tested through the single coordinator path, so the legacy bypass must be removed/contained after coordinator changes are stable.
- The release blocker must not be “fixed” with a fake asset.

---

# 5. CRUU5-001 — Settings Save still violates future-schema authority

**Severity:** HIGH  
**Files:**  
`src/PromptHelper/Services/AppSettingsRepository.cs`  
`tests/PromptHelper.Tests/AppSettingsRepositoryTests.cs`

## 5.1 Current defect

CRUU4 correctly fixed **startup synchronization**:

```text
valid schema-1 settings.json
+ schema-2 settings.backup.json
→ start with primary
→ preserve future backup
```

But `Save()` still does this:

```csharp
_writer.Write(_settingsPath, json);

try
{
    _writer.Write(_backupPath, json);
}
catch (...)
{
    ...
}
```

Therefore:

```text
1. old build starts with valid schema-1 primary
2. newer schema backup is correctly preserved on startup
3. user changes data folder in old build
4. Save() overwrites settings.backup.json with schema 1
5. newer-version recovery evidence is destroyed
```

There is a second write-time authority issue: if `settings.json` itself is replaced with a future-schema file while the old process is still running, direct `Save()` currently overwrites it.

## 5.2 Required invariant

Before any settings write:

```text
existing future primary  => ABORT; write nothing
existing unreadable primary => ABORT; write nothing
existing current/missing primary => primary may be written

existing future backup => preserve exact bytes; return warning
existing unreadable backup => preserve; return warning
existing missing/current/corrupt backup => backup may be synchronized
```

Do not make a future backup block use of a valid current primary.

## 5.3 Exact implementation

Refactor `Save()` to inspect disk state **before** writing.

Use this copy-ready shape:

```csharp
public SettingsSaveResult Save(AppSettings settings)
{
    ArgumentNullException.ThrowIfNull(settings);

    if (settings.SchemaVersion != AppSettings.CurrentSchemaVersion)
    {
        throw new InvalidDataException(
            $"Cannot save unsupported settings schema version: {settings.SchemaVersion}.");
    }

    SettingsReadState primaryBefore = ReadState(_settingsPath);

    if (primaryBefore is SettingsReadState.FutureSchema futurePrimary)
    {
        throw new UnsupportedSettingsSchemaException(futurePrimary.Version);
    }

    if (primaryBefore is SettingsReadState.Unreadable unreadablePrimary)
    {
        throw new SettingsReadException(_settingsPath, unreadablePrimary.Error);
    }

    // Capture backup state before primary mutation.
    SettingsReadState backupBefore = ReadState(_backupPath);

    settings.DataRootPath = NormalizeAndValidateDataRoot(settings.DataRootPath);
    string json = JsonSerializer.Serialize(settings, JsonOptions);

    string? settingsDir = Path.GetDirectoryName(_settingsPath);
    if (!string.IsNullOrEmpty(settingsDir))
    {
        Directory.CreateDirectory(settingsDir);
    }

    _writer.Write(_settingsPath, json);

    if (backupBefore is SettingsReadState.FutureSchema futureBackup)
    {
        return new SettingsSaveResult(
            $"The setting was saved, but settings.backup.json uses newer schema " +
            $"{futureBackup.Version}. The newer backup was preserved and was not overwritten.");
    }

    if (backupBefore is SettingsReadState.Unreadable unreadableBackup)
    {
        return new SettingsSaveResult(
            "The setting was saved, but settings.backup.json could not be inspected " +
            $"or synchronized: {unreadableBackup.Error.Message}");
    }

    try
    {
        string? backupDir = Path.GetDirectoryName(_backupPath);
        if (!string.IsNullOrEmpty(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        _writer.Write(_backupPath, json);
        return new SettingsSaveResult(null);
    }
    catch (Exception ex)
    {
        return new SettingsSaveResult(
            "The data folder was saved, but the settings backup could not be " +
            $"synchronized: {ex.Message}");
    }
}
```

Do **not** implement the future-backup case by deleting or renaming the future file.

## 5.4 Required tests

Add all of these:

```csharp
[TestMethod]
public void CRUU5_001_Save_preserves_future_schema_backup_exactly()
{
    using var temp = new TestDirectory();

    string primary = Path.Combine(temp.Root, "settings.json");
    string backup = Path.Combine(temp.Root, "settings.backup.json");

    File.WriteAllText(
        primary,
        "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Old\"}");

    File.WriteAllText(
        backup,
        "{\"schemaVersion\":2,\"dataRootPath\":\"C:\\\\Newer\"}");

    byte[] backupBefore = File.ReadAllBytes(backup);

    var repo = new AppSettingsRepository(
        settingsPathOverride: primary,
        backupPathOverride: backup);

    SettingsSaveResult result = repo.Save(new AppSettings
    {
        SchemaVersion = AppSettings.CurrentSchemaVersion,
        DataRootPath = @"C:\ChangedByOldBuild"
    });

    CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(backup));
    Assert.IsNotNull(result.Warning);
    StringAssert.Contains(result.Warning, "newer");
}
```

```csharp
[TestMethod]
public void CRUU5_001_Save_refuses_to_overwrite_future_schema_primary()
{
    using var temp = new TestDirectory();

    string primary = Path.Combine(temp.Root, "settings.json");
    string backup = Path.Combine(temp.Root, "settings.backup.json");

    File.WriteAllText(
        primary,
        "{\"schemaVersion\":2,\"dataRootPath\":\"C:\\\\Future\"}");
    File.WriteAllText(
        backup,
        "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Old\"}");

    byte[] primaryBefore = File.ReadAllBytes(primary);
    byte[] backupBefore = File.ReadAllBytes(backup);

    var repo = new AppSettingsRepository(
        settingsPathOverride: primary,
        backupPathOverride: backup);

    Assert.Throws<UnsupportedSettingsSchemaException>(() =>
        repo.Save(new AppSettings
        {
            SchemaVersion = 1,
            DataRootPath = @"C:\AttemptedOverwrite"
        }));

    CollectionAssert.AreEqual(primaryBefore, File.ReadAllBytes(primary));
    CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(backup));
}
```

Also add:

```text
CRUU5_001_Save_with_unreadable_backup_saves_primary_and_preserves_backup
CRUU5_001_Save_with_unreadable_primary_writes_nothing
```

---

# 6. CRUU5-002 — Settings schemaVersion is not structurally authoritative

**Severity:** HIGH  
**Files:** `AppSettingsRepository.cs`, `AppSettingsRepositoryTests.cs`

## 6.1 Current defect

`AppSettings.SchemaVersion` defaults to `1`, and settings are currently deserialized before any strict raw JSON schema-property inspection.

This means malformed input such as:

```json
{
  "dataRootPath": "C:\\Data"
}
```

can deserialize with the default schema `1`.

More importantly, because settings deserialization is case-insensitive, JSON such as:

```json
{
  "schemaVersion": 2,
  "SchemaVersion": 1,
  "dataRootPath": "C:\\Data"
}
```

can be accepted according to last-property binding instead of being rejected as ambiguous. That can hide evidence that the file came from a newer schema.

`LibraryRepository.InspectAndDeserialize()` already has the correct pattern: inspect raw JSON first, require exactly one `schemaVersion`, then deserialize.

## 6.2 Required invariant

Settings JSON must contain:

```text
root JSON kind = object
exactly one property whose name equals "schemaVersion" OrdinalIgnoreCase
schemaVersion value kind = number
schemaVersion value = Int32
schemaVersion > current => UnsupportedSettingsSchemaException
schemaVersion < current => InvalidDataException
schemaVersion == current => only then deserialize AppSettings
```

## 6.3 Copy-ready helper

Add:

```csharp
private static void ValidateSchemaPropertyBeforeDeserialization(
    string json,
    string path)
{
    JsonDocument document;

    try
    {
        document = JsonDocument.Parse(json);
    }
    catch (JsonException ex)
    {
        throw new InvalidDataException(
            $"Failed to parse settings JSON from '{path}': {ex.Message}",
            ex);
    }

    using (document)
    {
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Root of settings JSON must be an object: '{path}'.");
        }

        int count = 0;
        int version = 0;

        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (!string.Equals(
                    property.Name,
                    "schemaVersion",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            count++;

            if (property.Value.ValueKind != JsonValueKind.Number ||
                !property.Value.TryGetInt32(out version))
            {
                throw new InvalidDataException(
                    $"Property 'schemaVersion' must be an integer in '{path}'.");
            }
        }

        if (count == 0)
        {
            throw new InvalidDataException(
                $"Missing required 'schemaVersion' property in '{path}'.");
        }

        if (count > 1)
        {
            throw new InvalidDataException(
                $"Multiple 'schemaVersion' properties found in '{path}'.");
        }

        if (version > AppSettings.CurrentSchemaVersion)
        {
            throw new UnsupportedSettingsSchemaException(version);
        }

        if (version != AppSettings.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported settings schema version: {version}.");
        }
    }
}
```

Then call it at the top of `ParseAndValidate()` **before** `JsonSerializer.Deserialize<AppSettings>()`.

After doing this, keep the post-deserialization schema assertion as defense-in-depth.

## 6.4 Required tests

Add:

```text
CRUU5_002_Settings_missing_schemaVersion_is_corrupt
CRUU5_002_Settings_duplicate_same_case_schemaVersion_is_corrupt
CRUU5_002_Settings_duplicate_case_variant_schemaVersion_is_corrupt
CRUU5_002_Future_schema_first_then_current_case_variant_does_not_bypass
CRUU5_002_Current_schema_first_then_future_case_variant_does_not_bypass
CRUU5_002_Noninteger_schemaVersion_is_corrupt
CRUU5_002_Array_root_is_corrupt
CRUU5_002_Null_root_is_corrupt
```

Critical copy-ready test:

```csharp
[TestMethod]
public void CRUU5_002_Future_schema_cannot_be_hidden_by_case_variant_duplicate()
{
    using var temp = new TestDirectory();
    string settings = Path.Combine(temp.Root, "settings.json");

    File.WriteAllText(
        settings,
        """
        {
          "schemaVersion": 2,
          "SchemaVersion": 1,
          "dataRootPath": "C:\\Data"
        }
        """);

    var repo = new AppSettingsRepository(settingsPathOverride: settings);

    Assert.Throws<InvalidDataException>(() => repo.LoadOrRecover());
}
```

Do not “fix” this test by changing JSON serializer duplicate-property behavior globally. The explicit schema authority check is required.

---

# 7. CRUU5-003 — Library write path can destroy a future backup

**Severity:** HIGH  
**Files:**  
`src/PromptHelper/Services/LibraryRepository.cs`  
`src/PromptHelper/Services/LibraryStartupService.cs`  
`tests/PromptHelper.Tests/LibraryRepositoryTests.cs`  
`tests/PromptHelper.Tests/PromptLibraryServiceTests.cs`

## 7.1 Current defect

Startup now correctly preserves a future-schema `library.backup.json`.

But every normal mutation eventually calls:

```csharp
LibraryRepository.Commit(candidate)
```

and `Commit()` still blindly writes:

```csharp
_writer.Write(_paths.LibraryPath, json);
_writer.Write(_paths.LibraryBackupPath, json);
```

Therefore:

```text
valid schema-1 library.json
future schema-2 library.backup.json
start old app => future backup preserved
create/rename/edit/move/delete anything
=> Commit() overwrites schema-2 backup with schema 1
```

This violates the same downgrade-safety invariant fixed for startup.

## 7.2 Required design

Centralize write-time backup authority **inside `LibraryRepository`**, so callers cannot bypass it.

Add an internal file-state abstraction:

```csharp
private abstract record MetadataFileState
{
    public sealed record Missing : MetadataFileState;
    public sealed record Current : MetadataFileState;
    public sealed record Future(int Version) : MetadataFileState;
    public sealed record Corrupt(Exception Error) : MetadataFileState;
    public sealed record Unreadable(Exception Error) : MetadataFileState;
}
```

Use raw file reads plus `InspectAndDeserialize()` to classify.

### Primary write rule

Before overwriting an existing primary:

```text
Future => throw UnsupportedLibrarySchemaException; write nothing
Unreadable => throw IOException; write nothing
Current/Missing/Corrupt => normal existing recovery semantics may proceed
```

Allowing `Corrupt` is intentional because startup recovery uses `Commit()` to restore a valid backup over a corrupt primary.

### Backup write rule

```text
Future => preserve exact bytes, return BackupSynchronized=false + warning
Unreadable => preserve, return false + warning
Missing/Current/Corrupt => synchronize current document
```

## 7.3 Copy-ready core

Use a shared method:

```csharp
private CommitResult SynchronizeBackupPreservingFuture(string json)
{
    MetadataFileState state = ReadMetadataFileState(_paths.LibraryBackupPath);

    if (state is MetadataFileState.Future future)
    {
        return new CommitResult(
            false,
            $"The library was saved, but library.backup.json uses newer schema " +
            $"{future.Version}. The newer backup was preserved and was not overwritten.");
    }

    if (state is MetadataFileState.Unreadable unreadable)
    {
        return new CommitResult(
            false,
            "The library was saved, but its safety backup could not be inspected " +
            $"or synchronized: {unreadable.Error.Message}");
    }

    try
    {
        _writer.Write(_paths.LibraryBackupPath, json);
        return new CommitResult(true, null);
    }
    catch (Exception ex)
    {
        return new CommitResult(
            false,
            "The library was saved, but its safety backup could not be updated. " +
            $"Current data remains stored in library.json. {ex.Message}");
    }
}
```

Then:

```csharp
public CommitResult Commit(LibraryDocument document)
{
    ArgumentNullException.ThrowIfNull(document);
    LibraryValidator.Validate(document);

    MetadataFileState primaryState =
        ReadMetadataFileState(_paths.LibraryPath);

    if (primaryState is MetadataFileState.Future futurePrimary)
    {
        throw new UnsupportedLibrarySchemaException(futurePrimary.Version);
    }

    if (primaryState is MetadataFileState.Unreadable unreadablePrimary)
    {
        throw new IOException(
            $"library.json cannot be safely replaced because it cannot be read: " +
            unreadablePrimary.Error.Message,
            unreadablePrimary.Error);
    }

    string json = JsonSerializer.Serialize(document, JsonOptions);

    _writer.Write(_paths.LibraryPath, json);

    return SynchronizeBackupPreservingFuture(json);
}
```

Change `SynchronizeBackup()` to return `CommitResult` and use the same shared guarded method:

```csharp
public CommitResult SynchronizeBackup(LibraryDocument document)
{
    ArgumentNullException.ThrowIfNull(document);
    LibraryValidator.Validate(document);

    string json = JsonSerializer.Serialize(document, JsonOptions);
    return SynchronizeBackupPreservingFuture(json);
}
```

Update `LibraryStartupService` to use the returned result instead of assuming a void write.

## 7.4 Important DeletePrompt behavior

Do not break this safety rule:

```text
BackupSynchronized == false
=> DeletePrompt must preserve the removed prompt body file
```

That is correct when a future backup is being preserved, because the future backup might still reference the body.

## 7.5 Required tests

Add:

```text
CRUU5_003_Commit_preserves_future_schema_backup_exact_bytes
CRUU5_003_Commit_returns_warning_when_future_backup_preserved
CRUU5_003_CreateCategory_does_not_destroy_future_backup
CRUU5_003_EditPrompt_does_not_destroy_future_backup
CRUU5_003_DeletePrompt_preserves_body_when_future_backup_is_preserved
CRUU5_003_Commit_refuses_future_schema_primary
CRUU5_003_SynchronizeBackup_preserves_future_backup
CRUU5_003_Corrupt_same_version_backup_may_be_replaced
```

Test both repository level and at least one complete `PromptLibraryService` mutation path.

---

# 8. CRUU5-004 — Transition source must be the active process root

**Severity:** HIGH  
**Files:**  
`DataFolderTransitionCoordinator.cs`  
`SettingsDialog.xaml.cs`  
`MainWindow.xaml.cs`  
`DataFolderTransitionCoordinatorTests.cs`

## 8.1 Current defect

`MainWindow` opens settings with:

```csharp
new SettingsDialog(_viewModel.DataFolderPath, ...)
```

That is the actual root being used by the running repositories.

But the coordinator ignores it and computes:

```csharp
string currentRoot = _settingsRepo.GetEffectiveDataRoot();
```

This re-reads mutable bootstrap settings.

Failure scenario:

```text
process started and locked ActiveRootA
repositories/viewmodel use ActiveRootA

settings.json changes externally to RootB
user opens Tools & Settings and selects NewRootC

coordinator re-reads settings
coordinator believes current source = RootB
migration snapshots/copies RootB
running UI was actually editing RootA
```

The bootstrap pointer is a *next-start locator*. It must never redefine what the current process is already editing.

## 8.2 Required invariant

At startup:

```text
active root is resolved once
repositories + viewmodel + app lock bind to that root
```

For the lifetime of the process:

```text
that immutable active root is the only allowed migration source
```

## 8.3 Exact constructor change

Change coordinator constructor:

```csharp
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

    _activeCurrentRoot =
        PathIdentity.NormalizeForComparison(activeCurrentRoot);

    _settingsRepo =
        settingsRepo ?? throw new ArgumentNullException(nameof(settingsRepo));

    _migrationService =
        migrationService ?? throw new ArgumentNullException(nameof(migrationService));

    _confirmationService =
        confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));

    _capabilityValidator =
        capabilityValidator ?? new DataRootCapabilityValidator();

    _pathResolver =
        pathResolver ?? new WindowsPhysicalPathResolver();
}
```

Then remove this from `RequestTransition()`:

```csharp
string currentRoot = _settingsRepo.GetEffectiveDataRoot();
```

and use:

```csharp
string cleanCurrent = _activeCurrentRoot;
```

## 8.4 SettingsDialog construction

Change:

```csharp
_coordinator = coordinator ?? new DataFolderTransitionCoordinator(
    _settingsRepo,
    _migrationService,
    _confirmationService);
```

to:

```csharp
_coordinator = coordinator ?? new DataFolderTransitionCoordinator(
    _currentDataFolder,
    _settingsRepo,
    _migrationService,
    _confirmationService);
```

The injected test coordinator must also be constructed with the active root.

## 8.5 Required test

Add a test where settings intentionally disagree with active root:

```csharp
[TestMethod]
public void CRUU5_004_Transition_uses_active_process_root_not_mutated_settings_pointer()
{
    using var active = new TestDirectory();
    using var wrong = new TestDirectory();
    using var targetParent = new TestDirectory();
    using var settingsDir = new TestDirectory();

    SeedValidLibrary(active.Root, out Guid activePrompt);
    SeedValidLibrary(wrong.Root, out Guid wrongPrompt);

    string settingsPath = Path.Combine(settingsDir.Root, "settings.json");

    // Simulate external mutation after the process was already running on active.Root.
    File.WriteAllText(
        settingsPath,
        $"{{\"schemaVersion\":1,\"dataRootPath\":\"{EscapeJson(wrong.Root)}\"}}");

    var settingsRepo =
        new AppSettingsRepository(settingsPathOverride: settingsPath);

    var coordinator = new DataFolderTransitionCoordinator(
        active.Root,
        settingsRepo,
        new DataFolderMigrationService(),
        new FakeUserConfirmationService());

    string target = Path.Combine(targetParent.Root, "Target");

    // CRUU5-005 may make this abort because settings changed.
    // What is forbidden is copying wrong.Root.
    Assert.Throws<InvalidOperationException>(() =>
        coordinator.RequestTransition(target));

    Assert.IsFalse(File.Exists(
        Path.Combine(target, "prompts", $"{wrongPrompt:N}.md")));
}
```

After CRUU5-005 is implemented, the expected result is an explicit settings-precondition failure **before target mutation**.

---

# 9. CRUU5-005 — Settings transition commit needs an optimistic precondition

**Severity:** MEDIUM-HIGH  
**Area:** lost update / transition atomicity

## 9.1 Current defect

Even after fixing the active source root, the coordinator currently loads settings again near the end and saves a new root.

If `settings.json` changes while confirmation/copying is in progress, the old process can silently overwrite that newer same-schema edit.

## 9.2 Required invariant

A root transition may commit bootstrap settings only if the settings primary is still the same file state the coordinator observed before the transition began.

If it changed:

```text
new/empty target => rollback copied target
existing target => leave target untouched
settings => do not overwrite
show controlled "settings changed; retry" error
```

## 9.3 Add write token

In `AppSettingsRepository.cs`:

```csharp
public sealed record SettingsPrimaryWriteToken(
    bool Exists,
    byte[]? Sha256);
```

Add:

```csharp
public SettingsPrimaryWriteToken CapturePrimaryWriteToken()
{
    try
    {
        byte[] bytes = File.ReadAllBytes(_settingsPath);
        return new SettingsPrimaryWriteToken(
            Exists: true,
            Sha256: System.Security.Cryptography.SHA256.HashData(bytes));
    }
    catch (FileNotFoundException)
    {
        return new SettingsPrimaryWriteToken(false, null);
    }
    catch (DirectoryNotFoundException)
    {
        return new SettingsPrimaryWriteToken(false, null);
    }
    catch (Exception ex) when (
        ex is IOException or UnauthorizedAccessException or SecurityException)
    {
        throw new SettingsReadException(_settingsPath, ex);
    }
}
```

Add comparison:

```csharp
private static bool WriteTokensEqual(
    SettingsPrimaryWriteToken expected,
    SettingsPrimaryWriteToken actual)
{
    if (expected.Exists != actual.Exists)
    {
        return false;
    }

    if (!expected.Exists)
    {
        return true;
    }

    return expected.Sha256 is not null &&
           actual.Sha256 is not null &&
           expected.Sha256.AsSpan().SequenceEqual(actual.Sha256);
}
```

Add:

```csharp
public SettingsSaveResult SaveIfPrimaryUnchanged(
    AppSettings settings,
    SettingsPrimaryWriteToken expected)
{
    ArgumentNullException.ThrowIfNull(expected);

    SettingsPrimaryWriteToken actual = CapturePrimaryWriteToken();

    if (!WriteTokensEqual(expected, actual))
    {
        throw new InvalidOperationException(
            "Prompt Helper settings changed while the data-folder transition " +
            "was in progress. Nothing was committed. Reopen Tools & Settings and retry.");
    }

    return Save(settings);
}
```

## 9.4 Coordinator usage

At the beginning of `RequestTransition()`:

```csharp
SettingsPrimaryWriteToken settingsToken =
    _settingsRepo.CapturePrimaryWriteToken();
```

Before any target mutation, also verify that compatible current settings, if present, still resolve to the active root. The check should accept the default/missing settings case when `_activeCurrentRoot` is the default root.

When committing:

```csharp
var newSettings = new AppSettings
{
    SchemaVersion = AppSettings.CurrentSchemaVersion,
    DataRootPath = cleanTarget
};

SettingsSaveResult saveResult =
    _settingsRepo.SaveIfPrimaryUnchanged(
        newSettings,
        settingsToken);
```

Do not mutate an object returned by a late `_settingsRepo.Load()`.

## 9.5 Required tests

```text
CRUU5_005_Empty_target_rolls_back_when_settings_change_during_copy
CRUU5_005_Existing_target_switch_aborts_when_settings_change_after_confirmation
CRUU5_005_Missing_settings_primary_token_allows_first_save_if_still_missing
CRUU5_005_Future_primary_appearing_mid_transition_is_never_overwritten
```

For the first test, mutate `settings.json` from a fault-injection callback during copy.

Acceptance requires:

```text
settings changed => no overwrite
new target copied files => rolled back
active source => unchanged
```

---

# 10. CRUU5-006 — Physical-path safety currently fails open

**Severity:** HIGH  
**Files:**  
`DataRootTopologyValidator.cs`  
`ManagedDataRootPolicy.cs`  
`FakePhysicalPathResolver.cs`  
new path-policy tests

## 10.1 Current defect

Current topology code does:

```csharp
try
{
    // physical path resolution
}
catch (InvalidOperationException)
{
    throw;
}
catch
{
    // lexical check stands
}
```

That directly defeats the physical-alias protection when resolution is unavailable, denied, malformed, or fails due to a Windows filesystem edge case.

If identity cannot be proven, a potentially unsafe transition must not proceed.

## 10.2 Required invariant

For a data-root transition:

```text
physical resolution succeeds => evaluate physical relationship
physical resolution fails => ABORT transition
```

No lexical fallback.

For startup of an already-configured custom root, also fail safely rather than silently changing semantics.

## 10.3 Exact fix

Delete the broad fail-open catch.

Prefer a shared wrapper:

```csharp
private static string ResolvePhysicalOrThrow(
    IPhysicalPathResolver resolver,
    string path,
    string role)
{
    try
    {
        return PathIdentity.NormalizeForComparison(
            resolver.ResolveWithNearestExistingAncestor(path));
    }
    catch (Exception ex) when (
        ex is IOException or
        UnauthorizedAccessException or
        System.ComponentModel.Win32Exception or
        ArgumentException or
        NotSupportedException)
    {
        throw new InvalidOperationException(
            $"Prompt Helper could not safely resolve the physical {role} path " +
            $"'{path}'. The data-folder operation was cancelled.",
            ex);
    }
}
```

Use it for current, target, and bootstrap physical paths.

Do not swallow `Win32Exception`.

## 10.4 Required tests

Enhance `FakePhysicalPathResolver` with:

```csharp
public Exception? Failure { get; set; }

public string ResolveWithNearestExistingAncestor(string path)
{
    if (Failure != null)
    {
        throw Failure;
    }

    ...
}
```

Then add:

```text
CRUU5_006_Resolver_IO_failure_aborts_transition
CRUU5_006_Resolver_Unauthorized_failure_aborts_transition
CRUU5_006_Resolver_Win32_failure_aborts_transition
CRUU5_006_Resolver_failure_does_not_create_target
CRUU5_006_Startup_policy_does_not_fail_open
```

---

# 11. CRUU5-007 — Physical alias can resolve to a volume/share root

**Severity:** HIGH

## 11.1 Current defect

The code rejects a **lexical** target like:

```text
C:\
```

But after physical resolution it never repeats the volume-root rule.

Thus this can pass:

```text
C:\SafeLookingAlias   -> junction/reparse target -> D:\
```

Lexically it is a subdirectory. Physically it is the entire volume root.

The same problem applies to a UNC share root.

## 11.2 Required invariant

Reject both:

```text
lexical target is volume/share root
physical resolved target is volume/share root
```

## 11.3 Exact code

Immediately after resolving the physical target:

```csharp
if (DataRootTopologyValidator.IsVolumeRootSafe(physicalTarget))
{
    throw new InvalidOperationException(
        "The selected data folder resolves to a drive or share root. " +
        "Choose a dedicated subdirectory instead.");
}
```

In startup policy use `InvalidDataException` instead of `InvalidOperationException` if that matches existing startup error taxonomy.

## 11.4 Required tests

Fake-resolver deterministic test:

```csharp
[TestMethod]
public void CRUU5_007_Alias_resolving_to_volume_root_is_rejected()
{
    var resolver = new FakePhysicalPathResolver();
    string alias = @"C:\Aliases\LooksSafe";
    string current = @"C:\Data\Active";
    string bootstrap = @"C:\Users\Test\AppData\Local\PromptHelper";

    resolver.AddMapping(alias, @"D:\");
    resolver.AddMapping(current, current);
    resolver.AddMapping(bootstrap, bootstrap);

    var policy = new ManagedDataRootPolicy(resolver);

    Assert.Throws<InvalidOperationException>(() =>
        policy.ValidateDisjointOrSame(
            current,
            alias,
            bootstrap));
}
```

Also add startup policy and UNC share-root equivalents.

---

# 12. CRUU5-008 — Physical same-root alias must become a no-op

**Severity:** MEDIUM

## 12.1 Current defect

Current topology validation notices:

```text
physical current == physical target
```

and simply `return`s from a void validator.

The coordinator does not learn that they are the same root. It continues as though the alias were a different target.

Because the active process already owns the physical `.app.lock`, the later reservation can produce:

```text
"The selected target library is currently in use by another instance"
```

even though it is this process's own active library.

## 12.2 Required design

Make root validation return a relationship result instead of only throwing.

Add:

```csharp
public sealed record DataRootRelationship(
    string LexicalCurrent,
    string LexicalTarget,
    string PhysicalCurrent,
    string PhysicalTarget,
    bool SamePhysicalRoot);
```

Add to `ManagedDataRootPolicy`:

```csharp
public DataRootRelationship ValidateTransition(
    string currentRoot,
    string targetRoot,
    string bootstrapRoot)
{
    // lexical checks
    // fail-closed physical resolution
    // physical volume-root check
    // physical nesting/bootstrap checks

    bool same = PathIdentity.Equals(
        physicalCurrent,
        physicalTarget);

    return new DataRootRelationship(
        lexicalCurrent,
        lexicalTarget,
        physicalCurrent,
        physicalTarget,
        same);
}
```

Coordinator:

```csharp
DataRootRelationship roots = _rootPolicy.ValidateTransition(
    _activeCurrentRoot,
    cleanTarget,
    bootstrapRoot);

if (roots.SamePhysicalRoot)
{
    return new DataFolderTransitionResult(
        Changed: false,
        RestartRequired: false,
        ExistingLibrarySelected: false,
        NormalizedTargetRoot: roots.LexicalTarget,
        Warning: null);
}
```

This no-op must occur before target inspection, confirmation, reservation, probes, or settings writes.

## 12.3 Required tests

```text
CRUU5_008_Physical_alias_of_active_root_is_noop
CRUU5_008_Physical_alias_noop_does_not_prompt
CRUU5_008_Physical_alias_noop_does_not_create_probe
CRUU5_008_Physical_alias_noop_does_not_write_settings
CRUU5_008_Physical_alias_noop_does_not_request_restart
```

Replace the old test whose only assertion was “no exception” with behavior-level assertions.

---

# 13. CRUU5-009 — Reservation-created root can leak after failure

**Severity:** MEDIUM  
**File:** `TargetRootReservation.cs`

## 13.1 Current defect

`TargetRootReservation.TryAcquire()` calls:

```csharp
Directory.CreateDirectory(root);
```

before the migration transaction exists.

For a previously nonexistent target, the reservation creates the root. Later the transaction sees that the root already exists, so it does not track it as transaction-created.

If settings save or copying fails:

```text
target files/subdirectories can be rolled back
lock file can be deleted
empty target root itself remains
```

Current transition tests check `library.json` and `prompts/`, but do not require the formerly nonexistent root to be absent.

## 13.2 Exact fix

Track whether the reservation created the root.

Add field:

```csharp
private readonly bool _deleteRootIfStillEmptyOnDispose;
```

In `TryAcquire()`:

```csharp
bool rootExistedBefore = Directory.Exists(root);

if (!rootExistedBefore)
{
    Directory.CreateDirectory(root);
}
```

On successful reservation pass:

```csharp
deleteRootIfStillEmptyOnDispose: !rootExistedBefore
```

In `Dispose()`, after releasing/deleting the lock:

```csharp
if (_deleteRootIfStillEmptyOnDispose)
{
    try
    {
        if (Directory.Exists(_rootPath) &&
            !Directory.EnumerateFileSystemEntries(_rootPath).Any())
        {
            Directory.Delete(_rootPath);
        }
    }
    catch
    {
        // CRUU5-012 handles reporting cleanup degradation.
    }
}
```

Store `_rootPath`.

Also wrap acquisition so an exception after creating the root attempts empty-root cleanup.

Never delete a directory that existed before the transition.

## 13.3 Required tests

```text
CRUU5_009_Settings_failure_removes_reservation_created_empty_root
CRUU5_009_Copy_failure_removes_reservation_created_empty_root
CRUU5_009_Preexisting_empty_target_is_preserved_after_failure
CRUU5_009_Acquisition_exception_does_not_leave_new_empty_root
CRUU5_009_Successful_migration_keeps_target_root
```

---

# 14. CRUU5-010 — Existing target recheck compares only classification kind

**Severity:** MEDIUM

## 14.1 Current defect

Existing-target flow:

```text
inspect target A
show confirmation
acquire reservation
inspect target again
compare only:
    lockedInspection.Kind == initialInspection.Kind
```

If valid library A is replaced with different valid library B during the confirmation window:

```text
initial Kind = ValidPrimary
locked Kind = ValidPrimary
=> transition proceeds
```

The user confirmed one inspected library state but can be switched to a different one.

## 14.2 Required design

`TargetInspection` needs a content fingerprint.

Fingerprint must cover:

```text
effective metadata file exact bytes
all prompt bodies referenced by that effective document
prompt ID order must be deterministic
```

For `ValidPrimary`, fingerprint primary metadata + referenced bodies.

For `RecoverableBackupOnly`, fingerprint backup metadata + referenced bodies.

## 14.3 Copy-ready fingerprint helper

```csharp
private static byte[] ComputeEffectiveLibraryFingerprint(
    string root,
    string metadataPath,
    LibraryDocument document)
{
    using var hash =
        System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);

    byte[] metadata = File.ReadAllBytes(metadataPath);
    hash.AppendData(metadata);

    string promptsDir = Path.Combine(root, "prompts");

    foreach (PromptRecord prompt in document.Prompts.OrderBy(p => p.Id))
    {
        byte[] id = prompt.Id.ToByteArray();
        hash.AppendData(id);

        string promptPath =
            Path.Combine(promptsDir, $"{prompt.Id:N}.md");

        byte[] body = File.ReadAllBytes(promptPath);
        hash.AppendData(
            System.Security.Cryptography.SHA256.HashData(body));
    }

    return hash.GetHashAndReset();
}
```

Add `byte[]? Fingerprint` to `TargetInspection`.

## 14.4 Coordinator check

After reservation:

```csharp
if (lockedInspection.Kind != initialInspection.Kind ||
    !FingerprintsEqual(
        lockedInspection.Fingerprint,
        initialInspection.Fingerprint))
{
    throw new InvalidOperationException(
        "The selected target library changed while confirmation was open. " +
        "No settings were changed. Review the target and retry.");
}
```

After capability probes, re-inspect once more immediately before settings commit and compare against the locked fingerprint.

## 14.5 Required tests

```text
CRUU5_010_Valid_library_A_replaced_by_valid_library_B_aborts
CRUU5_010_Prompt_body_changed_during_confirmation_aborts
CRUU5_010_Backup_only_target_changed_to_different_backup_aborts
CRUU5_010_Unchanged_target_fingerprint_allows_transition
CRUU5_010_Target_change_never_writes_settings
```

The existing “kind changed” test must remain.

---

# 15. CRUU5-011 — Remove the unsafe legacy transition bypass

**Severity:** MEDIUM

## 15.1 Current defect

`DataFolderMigrationService.PrepareTarget()` remains public and performs a direct migration without the complete coordinator contract:

```text
no user confirmation semantics
no held target reservation across settings commit
no settings precondition token
no active-process-root authority
no process transition result
```

The real UI now uses the coordinator, but a public footgun remains.

Worse, `PublishedLifecycleAndGuiFlowRegressionTests.Full_E2E_Title_CRUD_Migration_and_Restart_Persistence()` still demonstrates the old unsafe path as the E2E migration example.

A future weak model is likely to copy the wrong API.

## 15.2 Required resolution

There must be **one production transition entry point**:

```text
DataFolderTransitionCoordinator.RequestTransition(...)
```

Migration service should expose only internal primitives.

Preferred fix:

```text
remove public PrepareTarget()
```

If unit tests need a convenience wrapper, make it `internal` and name it explicitly:

```csharp
internal DataFolderChangeResult PrepareTargetForMigrationUnitTest(
    string currentRoot,
    string selectedRoot)
```

Add a comment:

```csharp
// TEST/INTERNAL ONLY.
// Production data-root changes must go through DataFolderTransitionCoordinator.
```

Better still, migrate unit tests to the lower-level internal methods and delete the wrapper entirely.

## 15.3 E2E test rewrite

Replace:

```csharp
var migration = new DataFolderMigrationService();
migration.PrepareTarget(source, target);
settingsRepo.Save(...);
```

with the real coordinator:

```csharp
var confirmation = new FakeUserConfirmationService
{
    ConfirmationResult = true
};

var coordinator = new DataFolderTransitionCoordinator(
    sourceRoot,
    settingsRepo,
    new DataFolderMigrationService(),
    confirmation);

DataFolderTransitionResult transition =
    coordinator.RequestTransition(targetRoot);

Assert.IsTrue(transition.Changed);
Assert.IsTrue(transition.RestartRequired);
```

Then simulate restart using `settingsRepo.GetEffectiveDataRoot()`.

## 15.4 Required source guard test

Add a repository-source assertion ensuring no production file except the coordinator uses migration-copy internals.

At minimum:

```text
SettingsDialog must reference DataFolderTransitionCoordinator
MainWindow must not call PrepareTarget
no public PrepareTarget method remains
PublishedLifecycle... must use coordinator
```

---

# 16. CRUU5-012 — Rollback failure is silent

**Severity:** MEDIUM  
**Area:** failure truthfulness / recoverability

## 16.1 Current defect

`MigrationTargetTransaction.Dispose()` catches and ignores cleanup failures.

That avoids masking the original failure, but it creates another problem:

```text
migration fails
rollback delete also fails
user only sees original migration error
target may contain partial files
next retry can hit collisions
```

The source/settings are still protected, so this is not source data loss. It is a target cleanliness and error-reporting defect.

## 16.2 Required design

Rollback must be:

```text
idempotent
best effort
non-destructive to preexisting entries
able to report every cleanup failure
```

Do not throw directly from `Dispose()` during stack unwinding.

## 16.3 Add result types

```csharp
public sealed record MigrationRollbackFailure(
    string Path,
    string Operation,
    string Message);

public sealed record MigrationRollbackResult(
    IReadOnlyList<MigrationRollbackFailure> Failures)
{
    public bool Success => Failures.Count == 0;
}
```

Add explicit:

```csharp
public MigrationRollbackResult Rollback()
```

to the transaction and make it idempotent.

`Dispose()` can still call `Rollback()` if neither committed nor already rolled back, but discard the result only as a last safety net.

## 16.4 Coordinator catch

Use an explicit catch around the empty-target transaction:

```csharp
var tx = new DataFolderMigrationService.MigrationTargetTransaction();

try
{
    _migrationService.CopySnapshotToTarget(
        cleanCurrent,
        cleanTarget,
        snapshot,
        tx);

    ...

    tx.Commit();
}
catch (Exception original)
{
    MigrationRollbackResult rollback = tx.Rollback();

    if (!rollback.Success)
    {
        throw new MigrationRollbackException(
            original,
            cleanTarget,
            rollback.Failures);
    }

    throw;
}
finally
{
    tx.Dispose();
}
```

Add:

```csharp
public sealed class MigrationRollbackException : IOException
{
    public MigrationRollbackException(
        Exception original,
        string targetRoot,
        IReadOnlyList<MigrationRollbackFailure> failures)
        : base(
            BuildMessage(targetRoot, failures),
            original)
    {
        TargetRoot = targetRoot;
        Failures = failures;
    }

    public string TargetRoot { get; }
    public IReadOnlyList<MigrationRollbackFailure> Failures { get; }

    private static string BuildMessage(
        string targetRoot,
        IReadOnlyList<MigrationRollbackFailure> failures)
    {
        string details = string.Join(
            Environment.NewLine,
            failures.Select(x =>
                $"- {x.Operation}: {x.Path}: {x.Message}"));

        return
            "The data-folder transition failed and Prompt Helper could not " +
            "fully clean the target folder. The active source library and " +
            "settings were not switched. Review this target before retrying:" +
            Environment.NewLine +
            targetRoot +
            Environment.NewLine +
            details;
    }
}
```

## 16.5 Required tests

Fault-inject deletion failures:

```text
CRUU5_012_Rollback_delete_failure_is_reported
CRUU5_012_Rollback_failure_preserves_original_exception_as_inner
CRUU5_012_Rollback_never_deletes_preexisting_target_file
CRUU5_012_Rollback_success_rethrows_original_exception
CRUU5_012_Rollback_is_idempotent
```

---

# 17. CRUU5-013 — Real Windows junction behavior is not proven

**Severity:** MEDIUM verification gap

## 17.1 Current gap

CRUU4 added a real Win32 physical resolver, but the important path-policy tests primarily use `FakePhysicalPathResolver`.

A fake can prove policy branching. It cannot prove:

```text
CreateFileW directory flags are correct
GetFinalPathNameByHandleW works for actual junctions
extended prefix stripping is correct
junction alias equality is correct
nearest-existing-ancestor logic works through a reparse point
volume-root alias rejection works on Windows
```

This matters because CRUU5-006/007/008 are safety decisions based on the resolver.

## 17.2 Add Windows integration tests

Create:

```text
tests/PromptHelper.Tests/WindowsPhysicalPathResolverIntegrationTests.cs
```

Mark:

```csharp
[TestClass]
[DoNotParallelize]
public sealed class WindowsPhysicalPathResolverIntegrationTests
```

Helper:

```csharp
private static void CreateJunction(string junction, string target)
{
    var psi = new System.Diagnostics.ProcessStartInfo
    {
        FileName = "cmd.exe",
        Arguments = $"/c mklink /J \"{junction}\" \"{target}\"",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    using var process = System.Diagnostics.Process.Start(psi)
        ?? throw new InvalidOperationException("Failed to start cmd.exe.");

    string stdout = process.StandardOutput.ReadToEnd();
    string stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    Assert.AreEqual(
        0,
        process.ExitCode,
        $"mklink /J failed. stdout={stdout} stderr={stderr}");
}
```

Required tests:

```csharp
[TestMethod]
public void CRUU5_013_Real_junction_resolves_to_target()
```

```csharp
[TestMethod]
public void CRUU5_013_Real_junction_alias_of_active_root_is_noop()
```

```csharp
[TestMethod]
public void CRUU5_013_Real_junction_into_bootstrap_is_rejected()
```

```csharp
[TestMethod]
public void CRUU5_013_Real_junction_to_volume_root_is_rejected()
```

For the volume-root test:

```csharp
string volumeRoot =
    Path.GetPathRoot(temp.Root)
    ?? throw new InvalidOperationException("No volume root.");
```

Create a junction under the temp directory pointing to that root. Do **not** enumerate or delete through the target. Remove the junction itself explicitly at test cleanup.

If junction creation truly fails on a runner due to an environment limitation, the CI configuration should make that explicit rather than silently counting the safety scenario as passed. On `windows-latest`, the intended gate is to run it.

## 17.3 CI requirement

Keep these tests in the ordinary `dotnet test` job. Do not hide them behind a manual release gate.

---

# 18. CRUU5-014 — Real app logo remains an open release dependency

**Severity:** RELEASE BLOCKER  
**Current repository state:** audited tree still has no:

```text
src/PromptHelper/Assets/PromptHelperLogo.svg
src/PromptHelper/Assets/PromptHelper.ico
```

This is the one finding that must **not** be repaired by generating fake design content.

## 18.1 Required procedure when the real SVG is available

Use exactly:

```powershell
New-Item -ItemType Directory -Force src\PromptHelper\Assets | Out-Null
```

Place the authoritative supplied file at:

```text
src/PromptHelper/Assets/PromptHelperLogo.svg
```

Then:

```powershell
pwsh -NoProfile -File .\tools\GenerateAppIcon.ps1
```

Then:

```powershell
pwsh -NoProfile -File .\tools\VerifyReleaseAssets.ps1 -RequireIcon
```

Build/test:

```powershell
dotnet restore PromptHelper.slnx
dotnet build PromptHelper.slnx -c Release --no-restore
dotnet test PromptHelper.slnx -c Release --no-build
```

Publish:

```powershell
Remove-Item -Recurse -Force artifacts\publish-check -ErrorAction SilentlyContinue

dotnet publish src\PromptHelper\PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts\publish-check
```

Validate published EXE:

```powershell
pwsh -NoProfile -File .\tools\VerifyReleaseAssets.ps1 `
  -RequireIcon `
  -PublishedExe .\artifacts\publish-check\PromptHelper.exe
```

Finally run the GitHub workflow manually with:

```text
release_gate = true
stress = true
```

## 18.2 Manual Windows icon acceptance

Check all of:

```text
Explorer large icon
Explorer small/details icon
taskbar icon
Alt+Tab icon
window title-bar icon
pinned shortcut if applicable
```

Do not close CRUU5-014 until the actual supplied SVG is in the repository and the generated ICO/release EXE pass.

---

# 19. Cross-cutting recommended code layout

After CRUU5, the relevant files should have these responsibilities:

```text
AppSettingsRepository
  strict raw schema authority
  startup recovery
  future-file preservation
  primary write token
  conditional settings save

LibraryRepository
  strict library parsing
  primary commit
  guarded/future-preserving backup synchronization

ManagedDataRootPolicy
  lexical validation
  fail-closed physical resolution
  physical volume-root rejection
  physical current/target/bootstrap relationship result

WindowsPhysicalPathResolver
  Windows path canonicalization only
  no policy decisions

DataFolderMigrationService
  inspect target
  coherent source snapshot
  copy/verify snapshot
  migration transaction primitive
  NO public product transition workflow

TargetRootReservation
  held target .app.lock
  tracks whether it created root/lock
  cleans only its own empty artifacts

DataFolderTransitionCoordinator
  single production transition entry point
  immutable active source root
  settings precondition token
  root policy
  confirmation
  held reservation
  target fingerprint recheck
  migration
  settings commit
  transition result

SettingsDialog
  user input and messages only
  delegates transition to coordinator
```

Do not duplicate these policies across UI code.

---

# 20. Exact full CRUU5 regression matrix

The implementation model must add at least the following new tests.

## Settings

```text
CRUU5_001_Save_preserves_future_schema_backup_exactly
CRUU5_001_Save_refuses_to_overwrite_future_schema_primary
CRUU5_001_Save_with_unreadable_backup_saves_primary_and_warns
CRUU5_001_Save_with_unreadable_primary_writes_nothing

CRUU5_002_Settings_missing_schemaVersion_is_corrupt
CRUU5_002_Settings_duplicate_schemaVersion_is_corrupt
CRUU5_002_Settings_duplicate_case_variant_schemaVersion_is_corrupt
CRUU5_002_Future_schema_cannot_be_hidden_by_duplicate
CRUU5_002_Noninteger_schemaVersion_is_corrupt
CRUU5_002_Nonobject_settings_root_is_corrupt

CRUU5_005_SaveIfPrimaryUnchanged_accepts_unchanged_primary
CRUU5_005_SaveIfPrimaryUnchanged_rejects_changed_primary
CRUU5_005_SaveIfPrimaryUnchanged_rejects_appearing_primary
CRUU5_005_SaveIfPrimaryUnchanged_rejects_disappearing_primary
```

## Library

```text
CRUU5_003_Commit_preserves_future_schema_backup_exactly
CRUU5_003_Commit_warns_when_future_backup_is_preserved
CRUU5_003_Commit_refuses_future_schema_primary
CRUU5_003_CreateCategory_preserves_future_backup
CRUU5_003_EditPrompt_preserves_future_backup
CRUU5_003_DeletePrompt_preserves_body_if_backup_not_synchronized
CRUU5_003_SynchronizeBackup_preserves_future_backup
```

## Transition/root identity

```text
CRUU5_004_Uses_immutable_active_root
CRUU5_004_Does_not_copy_mutated_settings_root

CRUU5_005_Settings_change_during_empty_target_transition_rolls_back
CRUU5_005_Settings_change_during_existing_target_confirmation_aborts

CRUU5_006_Physical_resolution_failure_is_fail_closed
CRUU5_006_Failure_does_not_touch_target

CRUU5_007_Physical_volume_root_alias_rejected
CRUU5_007_Physical_UNC_share_root_alias_rejected
CRUU5_007_Startup_rejects_physical_volume_alias

CRUU5_008_Physical_same_root_alias_is_noop
CRUU5_008_Same_alias_no_prompt_no_probe_no_write_no_restart
```

## Reservation/fingerprint/rollback

```text
CRUU5_009_New_target_root_removed_after_failure
CRUU5_009_Preexisting_target_root_preserved
CRUU5_009_Acquisition_exception_cleanup

CRUU5_010_Target_valid_A_to_valid_B_change_aborts
CRUU5_010_Target_body_change_aborts
CRUU5_010_Backup_only_change_aborts
CRUU5_010_Unchanged_fingerprint_passes

CRUU5_011_Production_source_has_no_PrepareTarget_bypass
CRUU5_011_E2E_transition_uses_coordinator

CRUU5_012_Rollback_failure_reported
CRUU5_012_Original_exception_preserved
CRUU5_012_Preexisting_entries_never_deleted
CRUU5_012_Rollback_idempotent
```

## Real Windows resolver

```text
CRUU5_013_Real_junction_resolves_to_target
CRUU5_013_Real_junction_alias_current_is_noop
CRUU5_013_Real_junction_into_bootstrap_rejected
CRUU5_013_Real_junction_to_volume_root_rejected
```

---

# 21. Existing regressions that MUST remain green

Do not focus only on new tests. Re-run all old coverage, including:

```text
first-run initialization
interrupted initialization
primary/backup library recovery
future primary settings halt
future primary library halt
unreadable primary settings halt
unreadable primary library halt
valid primary + future backup startup preservation
category CRUD
prompt create/edit/delete/move/duplicate
prompt transaction rollback
automatic/custom headline semantics
Unicode headline separators
missing prompt-body behavior
recent-copy max-three uniqueness
clipboard failure does not mutate recents
settings same-folder no-op
existing-library confirmation cancel
empty-target migration
backup-only target recognition
target prompt-body completeness
migration source hash mutation abort
target metadata hash mismatch abort
target prompt hash mismatch abort
target collision rollback
process shutdown after successful root change
WPF host construction/lifecycle
three-column prompt layout
tooltip behavior
wrap visual-only behavior
documentation assertions
publish payload LICENSE
publish payload THIRD_PARTY_NOTICES.md
```

No previously passing requirement may be weakened to make CRUU5 pass.

---

# 22. Fault-injection campaign

The weak model must explicitly inject failures at these boundaries:

```text
settings primary read
settings backup read
settings primary write
settings backup write
settings primary changed between transition start and commit

library primary read
library backup read
library primary write
library backup write

physical current resolution
physical target resolution
physical bootstrap resolution

target root creation
target reservation lock creation/acquisition
target initial inspection
target post-confirmation inspection
target capability probe
source snapshot metadata read
source snapshot prompt read
target library copy
target prompt copy
source final hash read
target final hash read
settings commit after successful copy
rollback file delete
rollback directory delete
reservation lock delete
reservation-created-root delete
```

For every injected failure answer these five questions in the test:

```text
1. Did settings remain correct?
2. Did the active source remain untouched?
3. Did preexisting target data remain untouched?
4. Were only files created by this transition eligible for rollback?
5. If cleanup could not finish, was that truthfully reported?
```

---

# 23. Forbidden weak-model shortcuts

The implementation agent must NOT:

1. Delete a future-schema settings backup to make tests pass.
2. Downgrade a future-schema file.
3. Catch physical resolver failures and continue lexically.
4. Treat a physical drive root as safe because the selected string is a subdirectory alias.
5. Read the migration source root from settings after the process is already running.
6. Keep `_settingsRepo.GetEffectiveDataRoot()` as coordinator source authority.
7. Save settings without a precondition after a long-running transition.
8. Compare only target classification kind after confirmation.
9. Use timestamps/file length instead of cryptographic fingerprints for target identity.
10. Delete preexisting target files during rollback.
11. Delete a preexisting target directory merely because it is empty.
12. Throw from `Dispose()` and hide the original migration exception.
13. Swallow rollback cleanup failures without surfacing them.
14. Leave `PrepareTarget()` as the recommended/public production migration API.
15. Keep the E2E test on the old migration bypass.
16. Replace real Windows junction tests with only a fake resolver.
17. Mark a real path-safety test inconclusive just to keep CI green.
18. Synthesize a fake SVG/logo.
19. Disable the strict release gate.
20. Change schema versions to avoid implementing the authority rules.
21. Add cloud/network dependencies.
22. Change prompt file format.
23. Hot-swap repositories after a root change.
24. Remove the forced restart/process shutdown.
25. Weaken any CRUU1–CRUU4 behavior to satisfy new tests.

---

# 24. File-by-file implementation map

## MUST MODIFY

```text
src/PromptHelper/Services/AppSettingsRepository.cs
src/PromptHelper/Services/LibraryRepository.cs
src/PromptHelper/Services/LibraryStartupService.cs
src/PromptHelper/Services/ManagedDataRootPolicy.cs
src/PromptHelper/Services/DataRootTopologyValidator.cs
src/PromptHelper/Services/DataFolderTransitionCoordinator.cs
src/PromptHelper/Services/DataFolderMigrationService.cs
src/PromptHelper/Services/TargetRootReservation.cs
src/PromptHelper/Views/SettingsDialog.xaml.cs

tests/PromptHelper.Tests/AppSettingsRepositoryTests.cs
tests/PromptHelper.Tests/LibraryRepositoryTests.cs
tests/PromptHelper.Tests/LibraryStartupServiceTests.cs
tests/PromptHelper.Tests/DataFolderTransitionCoordinatorTests.cs
tests/PromptHelper.Tests/ManagedDataRootPolicyTests.cs
tests/PromptHelper.Tests/PublishedLifecycleAndGuiFlowRegressionTests.cs
```

## SHOULD ADD

```text
src/PromptHelper/Services/DataRootRelationship.cs
src/PromptHelper/Services/MigrationRollbackException.cs
tests/PromptHelper.Tests/WindowsPhysicalPathResolverIntegrationTests.cs
```

## MAY MODIFY IF NEEDED

```text
tests/PromptHelper.Tests/FakePhysicalPathResolver.cs
tests/PromptHelper.Tests/FaultInjectingMigrationFileOps.cs
tests/PromptHelper.Tests/FaultInjectingAtomicTextWriter.cs
README.md
Prompt_Helper_Nutzungsguide_DE_v2_FINAL.md
```

## DO NOT FABRICATE

```text
src/PromptHelper/Assets/PromptHelperLogo.svg
```

---

# 25. Build/test procedure after implementation

On Windows from repository root:

```powershell
git status --short
```

Ensure only intended CRUU5 changes are present.

Clean:

```powershell
dotnet clean PromptHelper.slnx -c Release
```

Restore:

```powershell
dotnet restore PromptHelper.slnx
```

Build:

```powershell
dotnet build PromptHelper.slnx -c Release --no-restore
```

Required:

```text
exit code 0
0 errors
0 warnings
```

Run tests once:

```powershell
dotnet test PromptHelper.slnx `
  -c Release `
  --no-build `
  --logger "trx;LogFileName=cruu5-run-1.trx"
```

Then five consecutive full runs:

```powershell
for ($i = 1; $i -le 5; $i++) {
    Write-Host "=== CRUU5 FULL RUN $i / 5 ==="

    dotnet test PromptHelper.slnx `
      -c Release `
      --no-build `
      --logger "trx;LogFileName=cruu5-run-$i.trx"

    if ($LASTEXITCODE -ne 0) {
        throw "CRUU5 test run $i failed."
    }
}
```

No skipped tests are acceptable unless an explicitly documented platform condition is unavoidable. The Windows junction tests are expected to execute on Windows CI.

---

# 26. Publish verification after code fixes

Development publish can still run while the real logo is pending:

```powershell
Remove-Item -Recurse -Force artifacts\publish-check -ErrorAction SilentlyContinue

dotnet publish src\PromptHelper\PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts\publish-check
```

Verify:

```powershell
Test-Path artifacts\publish-check\PromptHelper.exe
Test-Path artifacts\publish-check\LICENSE
Test-Path artifacts\publish-check\THIRD_PARTY_NOTICES.md
```

Do not call this a final release until CRUU5-014 is closed.

Once the real SVG exists, run the strict sequence in §18.

---

# 27. Manual Windows regression

After automated tests, manually execute these scenarios.

## A. Normal app use

```text
fresh start
open category
create nested category
rename category
create prompt
edit body
set custom headline
clear headline back to automatic
move prompt
duplicate prompt
copy prompt
copy from recent bar
delete prompt
delete empty category
restart
verify persistence
```

## B. Data-root normal transition

```text
active root A
select new empty root B
save
verify app closes
reopen
verify B active
verify A unchanged
```

## C. Existing target

```text
active A
valid existing B
select B
cancel => no settings change
repeat and confirm => app closes
reopen => B loaded
A unchanged
```

## D. Physical alias

Create a junction alias of active A:

```text
select alias
expected: immediate no-op
no confirmation
no "other instance" error
no settings write
no restart
```

## E. Resolver/path failure

Use an inaccessible/unresolvable path:

```text
expected: controlled failure
target not created/mutated
settings unchanged
```

## F. Volume-root alias

Create a junction pointing to drive root:

```text
select alias
expected: rejected before target inspection/mutation
```

## G. Settings external-change safety

While app uses A:

```text
externally modify settings.json to point elsewhere
attempt transition
expected: explicit retry error
no migration from the wrong root
no target mutation
no settings overwrite
```

## H. Future backups

With valid current primary + future backup:

```text
start app
perform one settings save
future settings backup exact bytes unchanged

start app
create/edit/delete library content
future library backup exact bytes unchanged
warnings visible where appropriate
```

---

# 28. Definition of done

CRUU5 is complete only when every statement below is true.

```text
[ ] CRUU5-001 future settings files survive Save()
[ ] CRUU5-002 settings require exactly one explicit integer schemaVersion
[ ] CRUU5-003 library mutations preserve future library backup
[ ] CRUU5-004 transition source is immutable active process root
[ ] CRUU5-005 settings commit uses unchanged-primary precondition
[ ] CRUU5-006 physical resolution failures abort
[ ] CRUU5-007 physical volume/share root aliases are rejected
[ ] CRUU5-008 physical alias of active root is a real no-op
[ ] CRUU5-009 failed transition does not leak a newly created empty root
[ ] CRUU5-010 existing target uses content fingerprint revalidation
[ ] CRUU5-011 no public/recommended legacy transition bypass remains
[ ] CRUU5-012 cleanup degradation is truthfully reported
[ ] CRUU5-013 real Windows junction integration tests execute and pass
[ ] all old CRUU1–CRUU4 tests pass
[ ] all new CRUU5 tests pass
[ ] full suite passes 5 consecutive times
[ ] Release build has 0 warnings / 0 errors
[ ] self-contained win-x64 publish succeeds
[ ] old source library is never deleted by transition
[ ] preexisting target data is never rollback-deleted
```

Strict final release additionally requires:

```text
[ ] authoritative PromptHelperLogo.svg present
[ ] generated PromptHelper.ico passes strict parser
[ ] published EXE exposes embedded icon
[ ] manual Explorer/taskbar/window icon verification passes
[ ] manual GitHub workflow release_gate=true passes
```

Until those asset checks pass, use:

```text
CODE/DATA-SAFETY ACCEPTANCE = possible after CRUU5 tests
STRICT RELEASE ASSET ACCEPTANCE = pending real logo
```

---

# 29. Copy-ready implementation prompt for the weak model

Use the following prompt verbatim with the implementation agent:

```text
ROLE
You are the implementation agent for CRUU5 in the Prompt Helper repository.

AUTHORITY
1. cruu1.md through cruu4.md remain accepted product/history authority.
2. cruu5.md is the repair authority for this round.
3. Do not redesign the product.
4. Do not invent a logo or other missing external asset.

TARGET
Implement every CRUU5 finding exactly and add every required regression test.

NON-NEGOTIABLE SAFETY RULES
- Never overwrite a future-schema settings or library file with schema 1.
- Settings JSON must contain exactly one explicit integer schemaVersion.
- The active process data root, not mutable settings.json, is the migration source.
- Data-root physical resolution must fail closed.
- Reject targets that physically resolve to a drive or UNC share root.
- A physical alias of the active root is a no-op.
- A settings file changed during transition must abort via a write precondition.
- Existing targets must be fingerprinted and revalidated after reservation.
- Rollback deletes only files/directories created by that transition.
- Rollback cleanup failures must be reported without hiding the original failure.
- Production transitions must go through DataFolderTransitionCoordinator.
- Do not synthesize PromptHelperLogo.svg.

IMPLEMENTATION ORDER
A. strict settings schema parser
B. settings write-time authority
C. library write-time authority
D. physical path relationship/fail-closed rules
E. immutable active root + settings write token
F. reservation/fingerprint
G. legacy API removal + rollback reporting
H. real Windows junction tests
I. full regressions
J. icon gate only if real SVG is actually supplied

TESTING
- Add all tests named in cruu5.md.
- Run Release build with zero warnings/errors.
- Run the complete test suite.
- Run the complete suite five consecutive times.
- Do not skip or weaken old tests.
- Run self-contained win-x64 publish.
- If the real SVG is present, run strict release asset verification.
- If it is absent, report MISSING_REQUIRED_ASSET and do not fabricate one.

OUTPUT
Provide:
1. exact files changed/added,
2. mapping CRUU5-001..014 -> implementation + tests,
3. build result,
4. exact total test count/pass/fail/skip,
5. five-run flakiness result,
6. publish result,
7. strict icon result or explicit pending external asset,
8. remaining issues, if any.

Do not claim PASS for a command you did not execute.
Do not commit or push unless separately authorized.
```

---

# 30. Final CRUU5 audit conclusion

CRUU4 fixed the first layer of the migration/recovery architecture, but the new architecture still has several places where **authority is checked only at startup or only lexically, while writes/transitions happen later**.

The central CRUU5 principle is:

```text
AUTHORITY MUST SURVIVE UNTIL THE WRITE BOUNDARY
```

Concretely:

```text
future schema must be protected at Save/Commit, not only Load
active process root must remain source authority, not mutable bootstrap state
physical root identity must fail closed
target identity must be revalidated by content, not only classification
rollback state must be truthful and complete
```

Once CRUU5 is implemented and its real Windows tests pass, another audit should concentrate on whether the fixes themselves introduced regressions rather than reopening already-settled product design.
