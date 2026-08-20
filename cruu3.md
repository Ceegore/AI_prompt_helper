# CRUU3 — Post-CRUU2 Deep Regression Audit and Complete Weak-Model Repair Blueprint

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited commit:** `0f300de0f4191fc0e2338ec0d5dafdd64c3cae64`  
**Previous repair authority for this round:** `cruu2.md` plus the accepted `cruu1.md` product requirements  
**Purpose:** independently re-audit the implementation after the CRUU2 repair commit, identify residual defects and verification gaps that the previous 214-test report can miss, and give a weak implementation model enough exact material to repair every open item without making product or architecture decisions.

---

# 1. Executive result

The CRUU2 implementation is a **large improvement** and the previously identified CRUU2 defects are substantially repaired in source. However, a fresh post-CRUU2 audit found additional open issues that are not covered by the quoted “214/214 tests passed” conclusion.

The new findings are concentrated in five areas:

1. **data-root transition safety** after changing the configured folder;
2. **settings authority/recovery correctness**, especially future-schema and transient-I/O cases;
3. **migration integrity and topology**, including prompt-body concurrency, target capabilities, and existing-library switching;
4. **release verification**, where build/test is not equivalent to publishing and validating the actual self-contained Windows artifact;
5. **edge-case validation/test hygiene**, including Unicode single-line separators and WPF test-host cleanup.

The application should therefore **not yet be called fully production-clean** solely because the existing 214 tests pass.

## 1.1 Current acceptance state

```text
SOURCE-LEVEL CRUU2 REPAIRS = SUBSTANTIALLY LANDED
NEW POST-CRUU2 FINDINGS = OPEN
FINAL RELEASE ACCEPTANCE = NOT YET GRANTED
MISSING_REQUIRED_ASSET = PromptHelperLogo.svg
```

## 1.2 Important evidence distinction

The user-provided verification report states:

```text
214 tests passed
0 failed
0 skipped
0 compiler warnings/errors
5 consecutive full-suite runs passed
```

Treat that as **external execution evidence supplied with this audit**. This CRUU3 audit does not dispute those numbers.

However, this audit environment does not contain the .NET SDK or a Windows GUI runtime, so the runs could not be independently reproduced here. The GitHub repository does contain `.github/workflows/windows-ci.yml`, but the current commit’s connector-visible combined status did not provide an attached check result, and the available workflow-run lookup is not sufficient to prove the push workflow result.

Therefore use these labels correctly:

```text
user-supplied local/runtime verification = ACCEPTED AS REPORTED, NOT INDEPENDENTLY REPRODUCED
source/static regression audit = PERFORMED AGAINST COMMIT 0f300de
new runtime checks described in CRUU3 = MUST BE EXECUTED AFTER THE CRUU3 FIXES
```

Never convert “the previous tests passed” into “a scenario that was never represented by a test is therefore safe.”

---

# 2. Authority and locked product decisions

The weak implementation model must **not reopen settled product design** while fixing this file.

## 2.1 Preserve these decisions exactly

1. Prompt bodies remain separate local `.md` files.
2. `PromptRecord.Title` remains optional metadata.
3. `Title == null` means automatic headline mode.
4. Automatic headline remains the first non-empty body line, with display truncation only.
5. `LibraryDocument.CurrentSchemaVersion` remains `1` in this repair round.
6. Do not make `PromptRecord.Title` `[JsonRequired]`.
7. Editor “Wrap long lines” remains visual-only and must never mutate prompt text.
8. Prompt cards remain a three-column `UniformGrid`; do not invent a custom virtualizing wrap panel.
9. Recent-copy history remains session-only, newest-first, unique by prompt ID, maximum three.
10. Recent-copy history is never persisted.
11. Existing valid target Prompt Helper libraries remain selectable **without overwrite or merge**.
12. Selecting an empty/new target means copying the current library there while preserving the old source.
13. The live repository graph is not hot-swapped in place.
14. The old source data remains untouched after successful migration.
15. Bootstrap settings remain under `%LOCALAPPDATA%\PromptHelper`.
16. Prompt Helper remains offline/local with no telemetry/cloud/database expansion.
17. The real supplied logo SVG is the icon authority. Do not invent, redraw, approximate, or synthesize a replacement logo.
18. The missing logo source is a release dependency, not permission to fabricate an asset.

## 2.2 Clarification added by CRUU3

“Data-root changes take effect on next start” must now be interpreted as:

```text
save new root
→ current process stops accepting library mutations
→ close Prompt Helper
→ user reopens Prompt Helper
→ new root becomes active
```

It must **not** mean:

```text
save new root
→ continue editing old root for minutes/hours
→ eventually restart and silently see a stale copied snapshot
```

This is a safety clarification, not a new feature.

---

# 3. Fresh audit scope

The post-CRUU2 audit re-read the current implementations of at least:

```text
.github/workflows/windows-ci.yml
README.md
Prompt_Helper_Nutzungsguide_DE_v2_FINAL.md
src/PromptHelper/App.xaml.cs
src/PromptHelper/MainWindow.xaml
src/PromptHelper/MainWindow.xaml.cs
src/PromptHelper/Models/AppSettings.cs
src/PromptHelper/Models/OperationResults.cs
src/PromptHelper/PromptHelper.csproj
src/PromptHelper/Services/AppInstanceLock.cs
src/PromptHelper/Services/AppSettingsRepository.cs
src/PromptHelper/Services/AtomicTextWriter.cs
src/PromptHelper/Services/DataFolderMigrationService.cs
src/PromptHelper/Services/DataRootBootstrapValidator.cs
src/PromptHelper/Services/LibraryValidator.cs
src/PromptHelper/Services/PromptCopyCoordinator.cs
src/PromptHelper/Services/PromptLibraryService.cs
src/PromptHelper/ViewModels/MainViewModel.cs
src/PromptHelper/Views/PromptEditorDialog.xaml
src/PromptHelper/Views/PromptEditorDialog.xaml.cs
src/PromptHelper/Views/SettingsDialog.xaml
src/PromptHelper/Views/SettingsDialog.xaml.cs
tests/PromptHelper.Tests/AppSettingsRepositoryTests.cs
tests/PromptHelper.Tests/Cruu1ComprehensiveVerificationTests.cs
tests/PromptHelper.Tests/DataFolderMigrationServiceTests.cs
tests/PromptHelper.Tests/IconAssetTests.cs
tests/PromptHelper.Tests/PromptCopyCoordinatorTests.cs
tests/PromptHelper.Tests/RepositoryTestPaths.cs
tests/PromptHelper.Tests/TestAssemblyHooks.cs
tests/PromptHelper.Tests/WpfTestHost.cs
tools/GenerateAppIcon.ps1
```

The audit intentionally searched for **cases that can remain green even if every existing named test passes**.

---

# 4. New open-finding register

| ID | Severity | Area | Status | Summary |
|---|---|---|---|---|
| CRUU3-001 | HIGH | Data-root transition | OPEN | After successfully changing the data root, `SettingsDialog.RestartRequired` is ignored by `MainWindow`; the application can keep mutating the old root after the migration snapshot was created. |
| CRUU3-002 | HIGH | Settings downgrade safety | OPEN | A future/newer `settings.json` can be treated as generic corruption and bypassed by falling back to an older schema-1 backup. |
| CRUU3-003 | HIGH | Settings authority | OPEN | A temporarily unreadable/locked primary settings file can be treated as recoverable corruption and replaced logically by a stale backup. |
| CRUU3-004 | MEDIUM | Settings durability evidence | OPEN | Valid-primary backup-sync failures and backup-recovery primary-restore failures are swallowed; callers can receive no warning about degraded settings redundancy. |
| CRUU3-005 | LOW/MEDIUM | Settings API invariant | OPEN | `AppSettingsRepository.Save()` does not reject an unsupported `SchemaVersion`, allowing an internal caller to persist settings the same binary cannot read. |
| CRUU3-006 | MEDIUM/HIGH | Migration concurrency | OPEN | Migration hashes only `library.json`; prompt bodies can change externally during copy without being detected. |
| CRUU3-007 | MEDIUM | Migration topology | OPEN | Descendant targets are rejected, but an ancestor of the current data root is still allowed, creating nested managed-root relationships. |
| CRUU3-008 | MEDIUM | Bootstrap topology | OPEN | Custom targets can overlap the fixed `%LOCALAPPDATA%\PromptHelper` bootstrap directory, tangling bootstrap files and active library data. |
| CRUU3-009 | MEDIUM | Existing target detection | OPEN | A target containing only a valid `library.backup.json` is recoverable by normal startup but is not recognized as an existing library by migration. |
| CRUU3-010 | MEDIUM | Target write capability | OPEN | A target is validated for readability, not for the exact create/replace/delete writes Prompt Helper needs after restart. |
| CRUU3-011 | MEDIUM | Existing-library UX safety | OPEN | `ExistingLibraryFound` is ignored by the settings UI, so switching to a pre-existing library receives the same message as migrating the current library. |
| CRUU3-012 | MEDIUM | Target concurrency | OPEN | Switching to an existing target does not detect that another Prompt Helper instance currently owns that target’s `.app.lock`. |
| CRUU3-013 | MEDIUM/HIGH | Release gate | OPEN | Windows CI builds/tests source but does not execute the required `dotnet publish` gate for the intended self-contained `win-x64` package. |
| CRUU3-014 | MEDIUM | Verification evidence | OPEN | CI runs the suite once and only uploads TRX on failure; the previous 5-run flakiness claim is not encoded as a repeatable gate for future commits. |
| CRUU3-015 | RELEASE BLOCKER / dependency | Icon/release | OPEN | The real `PromptHelperLogo.svg` and generated `PromptHelper.ico` remain absent; icon tests intentionally allow this, so a green suite does not mean the icon feature is release-complete. |
| CRUU3-016 | LOW/MEDIUM | Single-line validation | OPEN | `char.IsControl` does not reject Unicode line/paragraph separators U+2028/U+2029, allowing visually multiline “single-line” metadata. |
| CRUU3-017 | LOW | Test infrastructure | OPEN | The shared WPF host does not assert successful shutdown/reset state, and some WPF tests do not deterministically close created windows. |
| CRUU3-018 | MEDIUM | Documentation/release truth | OPEN | Docs must be synchronized with forced restart, existing-library switching, target constraints, actual self-contained publish validation, and release icon dependency. |

No CRUU2 finding is reopened merely for being already known. CRUU3 contains only residual/new work plus the still-unresolved external logo dependency.

---

# 5. CRUU3-001 — force a clean process boundary after a data-root change

## 5.1 Current behavior

`SettingsDialog.SaveButton_Click` correctly sets:

```csharp
RestartRequired = true;
DialogResult = true;
Close();
```

when a new data root is selected.

But `MainWindow.SettingsButton_Click` currently performs only:

```csharp
var dialog = new SettingsDialog(...)
{
    Owner = this
};
dialog.ShowDialog();
```

It ignores `dialog.RestartRequired`.

## 5.2 Reproduction scenario

```text
A = current active library root
B = empty new target

1. Start Prompt Helper on A.
2. Tools & Settings → choose B → Save.
3. Migration copies snapshot A0 into B.
4. settings.json now points to B.
5. Settings closes, but MainWindow remains live on A.
6. User edits Prompt P on A, creating state A1.
7. User exits later.
8. Reopen Prompt Helper.
9. App opens B, which still contains A0.
10. The edit made at step 6 appears “lost” even though it remains stranded in A.
```

This is a real transition-safety defect.

## 5.3 Additional split-brain risk

Because `AppInstanceLock` is per data root, once settings points to B the first process can still hold A’s lock while a newly launched second process can acquire B’s lock.

That permits two live Prompt Helper processes operating on the two roots created by one migration operation.

Do not solve this by introducing a global application mutex; a forced process boundary is simpler and preserves the existing per-root locking model.

## 5.4 Required behavior

After a successful settings save with `RestartRequired == true`:

1. Show one clear informational message if not already shown by the dialog.
2. Return from the dialog.
3. Close/shutdown the current application immediately.
4. Do not auto-relaunch unless that behavior is separately authorized.
5. A same-path Save with `RestartRequired == false` must leave the application open.

Recommended user text:

```text
Data folder changed

Prompt Helper must close now so the previous data folder cannot be modified after the migration snapshot.

Open Prompt Helper again to use the selected data folder.
```

## 5.5 Minimal MainWindow implementation

Change `SettingsButton_Click` to:

```csharp
private void SettingsButton_Click(object sender, RoutedEventArgs e)
{
    var dialog = new SettingsDialog(
        _viewModel.DataFolderPath,
        _settingsRepo,
        _migrationService)
    {
        Owner = this
    };

    bool? result = dialog.ShowDialog();

    if (result == true && dialog.RestartRequired)
    {
        Application.Current.Shutdown();
    }
}
```

If direct `Application.Current.Shutdown()` makes unit testing awkward, inject a tiny lifecycle abstraction instead of adding test-only branches:

```csharp
public interface IApplicationLifetime
{
    void RequestShutdown();
}

public sealed class WpfApplicationLifetime : IApplicationLifetime
{
    public void RequestShutdown()
        => Application.Current.Shutdown();
}
```

A weak model should choose the abstraction only if it is needed to test the handler cleanly. Do not build a large navigation/lifetime framework.

## 5.6 Required tests

Add a narrow coordinator or handler test rather than reflection-driving private UI methods if practical.

Required cases:

```text
DataRootChange_success_requests_shutdown
DataRootChange_same_path_does_not_request_shutdown
DataRootChange_cancel_does_not_request_shutdown
DataRootChange_failed_migration_does_not_request_shutdown
```

Manual Windows test:

```text
1. Start on A.
2. Create marker prompt “Before migration”.
3. Select empty B and Save.
4. Verify application closes.
5. Reopen.
6. Verify B is active and contains marker.
7. Verify there was no opportunity to create “After migration but before restart” in old A.
```

---

# 6. CRUU3-002 + CRUU3-003 — replace generic settings fallback with an authority-preserving state machine

These two findings must be repaired together. Do not patch them with more `catch` clauses around the existing algorithm.

## 6.1 Current defect: future schema can be bypassed

`ReadAndValidate` currently throws `InvalidDataException` for every schema other than 1.

`LoadOrRecover` catches that exception like any other primary failure and then attempts the backup.

Dangerous state:

```text
settings.json        = schemaVersion 2, path B   // created by newer Prompt Helper
settings.backup.json = schemaVersion 1, path A   // older backup
old Prompt Helper starts
→ treats schema 2 primary as generic failure
→ loads schema 1 backup
→ opens A
```

A newer authoritative primary must never be silently bypassed by an older backup.

## 6.2 Current defect: temporary I/O failure can use stale backup

`LoadOrRecover` also catches arbitrary exceptions while reading the primary.

Dangerous state:

```text
settings.json        = valid schema 1, path B, temporarily locked/unreadable
settings.backup.json = valid schema 1, path A, stale
→ primary read throws IOException / UnauthorizedAccessException
→ backup loads successfully
→ application silently opens A
```

A backup is for **missing or corrupt content**, not for overriding an authoritative primary that is temporarily inaccessible.

## 6.3 Add an explicit exception for future settings schema

Create:

`src/PromptHelper/Services/UnsupportedSettingsSchemaException.cs`

```csharp
namespace PromptHelper.Services;

public sealed class UnsupportedSettingsSchemaException : Exception
{
    public UnsupportedSettingsSchemaException(int schemaVersion)
        : base($"Unsupported settings schema version: {schemaVersion}.")
    {
        SchemaVersion = schemaVersion;
    }

    public int SchemaVersion { get; }
}
```

## 6.4 Add an internal read-state model

Inside `AppSettingsRepository` use a discriminated state shape similar to `LibraryStartupService`:

```csharp
private abstract record SettingsReadState
{
    public sealed record Missing : SettingsReadState;
    public sealed record Valid(AppSettings Settings) : SettingsReadState;
    public sealed record Corrupt(Exception Error) : SettingsReadState;
    public sealed record FutureSchema(int Version) : SettingsReadState;
    public sealed record Unreadable(Exception Error) : SettingsReadState;
}
```

## 6.5 Strict parser

Split parsing from filesystem read.

Suggested implementation:

```csharp
private static AppSettings ParseAndValidate(string json, string path)
{
    if (string.IsNullOrWhiteSpace(json))
    {
        throw new InvalidDataException(
            $"Settings file is empty or whitespace: '{path}'");
    }

    AppSettings? settings;
    try
    {
        settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
    }
    catch (JsonException ex)
    {
        throw new InvalidDataException(
            $"Failed to deserialize settings from '{path}': {ex.Message}", ex);
    }

    if (settings is null)
    {
        throw new InvalidDataException(
            $"Settings deserialized to null from '{path}'.");
    }

    if (settings.SchemaVersion > 1)
    {
        throw new UnsupportedSettingsSchemaException(settings.SchemaVersion);
    }

    if (settings.SchemaVersion < 1)
    {
        throw new InvalidDataException(
            $"Invalid settings schema version: {settings.SchemaVersion}.");
    }

    settings.DataRootPath = NormalizeAndValidateDataRoot(settings.DataRootPath);
    return settings;
}
```

Do not classify an unknown **higher** version as corruption.

## 6.6 Filesystem-state reader

```csharp
private static SettingsReadState ReadState(string path)
{
    string json;

    try
    {
        json = File.ReadAllText(path);
    }
    catch (FileNotFoundException)
    {
        return new SettingsReadState.Missing();
    }
    catch (DirectoryNotFoundException)
    {
        return new SettingsReadState.Missing();
    }
    catch (Exception ex) when (
        ex is IOException or UnauthorizedAccessException or SecurityException)
    {
        return new SettingsReadState.Unreadable(ex);
    }

    try
    {
        return new SettingsReadState.Valid(ParseAndValidate(json, path));
    }
    catch (UnsupportedSettingsSchemaException ex)
    {
        return new SettingsReadState.FutureSchema(ex.SchemaVersion);
    }
    catch (Exception ex) when (ex is JsonException or InvalidDataException)
    {
        return new SettingsReadState.Corrupt(ex);
    }
}
```

If `JsonException` is already wrapped by `ParseAndValidate`, the outer catch can be `InvalidDataException` only. Keep the implementation internally consistent.

## 6.7 Exact authority matrix

Implement this matrix, not an approximation:

| Primary | Backup | Required result |
|---|---|---|
| Missing | Missing | default settings |
| Valid | any | primary wins |
| FutureSchema | any | STOP with `UnsupportedSettingsSchemaException` |
| Unreadable | any | STOP with controlled read/access error; do not use backup |
| Corrupt | Valid | recover backup with warning |
| Corrupt | Missing | STOP corrupt settings |
| Corrupt | Corrupt | STOP corrupt settings |
| Corrupt | FutureSchema | STOP future-schema exception |
| Corrupt | Unreadable | STOP with controlled error; do not claim recovery |
| Missing | Valid | recover backup with warning |
| Missing | FutureSchema | STOP future-schema exception |
| Missing | Corrupt | STOP corrupt backup |
| Missing | Unreadable | STOP unreadable backup |

When primary is valid, backup content is not authority. A future-schema backup can be overwritten by synchronization from a valid current primary because the valid primary is authoritative in this state.

## 6.8 App-level future-schema message

In `App.OnStartup`, catch before generic `Exception`:

```csharp
catch (UnsupportedSettingsSchemaException ex)
{
    MessageBox.Show(
        $"Prompt Helper settings were created by a newer version " +
        $"(schema {ex.SchemaVersion}) and cannot be safely opened by this build.\n\n" +
        "Install the newer Prompt Helper version or restore compatible settings.",
        "Unsupported Settings Schema",
        MessageBoxButton.OK,
        MessageBoxImage.Error);

    Shutdown();
    return;
}
```

Do not delete or rewrite the future-schema file.

## 6.9 Required tests

Add these exact cases:

```text
Primary_future_schema_with_valid_old_backup_stops_without_recovery
Primary_future_schema_does_not_modify_primary_or_backup
Missing_primary_with_future_schema_backup_stops
Corrupt_primary_with_future_schema_backup_stops
Locked_valid_primary_does_not_fall_back_to_stale_backup
Unreadable_primary_does_not_rewrite_primary
Valid_primary_with_future_backup_uses_primary
```

### Locked-primary test pattern

Windows-only deterministic example:

```csharp
using var lockStream = new FileStream(
    settingsPath,
    FileMode.Open,
    FileAccess.ReadWrite,
    FileShare.None);

Assert.Throws<SettingsReadException>(() => repo.LoadOrRecover());
```

If introducing `SettingsReadException`, preserve the original inner exception.

---

# 7. CRUU3-004 — surface settings redundancy degradation instead of swallowing it

## 7.1 Current primary-valid behavior

When the primary settings file is valid, the repository tries to synchronize the backup:

```csharp
try
{
    _writer.Write(_backupPath, json);
}
catch
{
    // Best effort backup synchronization
}

return new SettingsLoadResult(primarySettings, false, null);
```

A failed backup sync is invisible.

## 7.2 Current backup-recovery behavior

When backup is used, restoration of the primary is also best-effort and failure is swallowed. The result still only says the setting was recovered.

This can leave the app operating successfully while settings redundancy remains broken indefinitely.

## 7.3 Required result semantics

Keep startup operational when the authoritative settings are known, but return a warning.

### Valid primary, backup sync failed

```text
RecoveredFromBackup = false
Warning = "Settings loaded from settings.json, but settings.backup.json could not be synchronized: ..."
```

### Backup recovered, primary restore failed

```text
RecoveredFromBackup = true
Warning = "Settings were recovered from settings.backup.json, but settings.json could not be restored: ..."
```

## 7.4 App warning display bug

Current `App.xaml.cs` displays a settings warning only when:

```csharp
settingsResult.RecoveredFromBackup &&
!string.IsNullOrEmpty(settingsResult.Warning)
```

Change this to display **any** non-empty settings warning.

Recommended:

```csharp
if (!string.IsNullOrEmpty(settingsResult.Warning))
{
    MessageBox.Show(
        settingsResult.Warning,
        settingsResult.RecoveredFromBackup
            ? "Settings Recovery Notice"
            : "Settings Backup Warning",
        MessageBoxButton.OK,
        MessageBoxImage.Warning);
}
```

## 7.5 Tests

```text
Valid_primary_backup_sync_failure_returns_warning_and_primary_settings
Backup_recovery_primary_restore_failure_returns_warning_and_backup_settings
App_settings_warning_is_not_conditioned_only_on_recovery
```

Use `FaultInjectingAtomicTextWriter`. Do not rely on OS permissions for these unit tests.

---

# 8. CRUU3-005 — Save must enforce the settings schema invariant

## 8.1 Current gap

`AppSettingsRepository.Save(AppSettings settings)` serializes whatever `SchemaVersion` the caller provides.

An accidental internal call can therefore write:

```json
{
  "schemaVersion": 99,
  "dataRootPath": "C:\\Data"
}
```

which the same binary refuses to load.

## 8.2 Required guard

At the start of `Save`:

```csharp
if (settings.SchemaVersion != 1)
{
    throw new InvalidDataException(
        $"Cannot save unsupported settings schema version: {settings.SchemaVersion}.");
}
```

Prefer a shared constant:

```csharp
public const int CurrentSchemaVersion = 1;
```

in `AppSettings`, then use it everywhere instead of duplicated literals.

Suggested model:

```csharp
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string? DataRootPath { get; set; }
}
```

## 8.3 Tests

```text
Save_future_schema_throws_before_primary_write
Save_future_schema_does_not_create_backup
Save_current_schema_roundtrips
```

---

# 9. CRUU3-006 — migration snapshot must include every active prompt body

## 9.1 Current protection is incomplete

`DataFolderMigrationService` currently hashes `library.json` before and after copying.

That detects concurrent metadata mutation, but prompt bodies are separate `.md` files. An external editor, sync tool, script, antivirus repair tool, or other process can change a prompt body without changing `library.json`.

The current code validates prompt existence/readability, then uses `File.Copy`, but does not prove that the copied bodies all belong to one stable source snapshot.

## 9.2 Required snapshot model

Use the already validated source `LibraryDocument` instead of discarding it.

Add an internal record:

```csharp
internal sealed record MigrationSnapshot(
    byte[] LibraryHash,
    IReadOnlyDictionary<Guid, byte[]> PromptHashes);
```

## 9.3 Capture snapshot before touching target

```csharp
private static MigrationSnapshot CaptureSnapshot(
    string root,
    LibraryDocument document)
{
    string libraryPath = Path.Combine(root, "library.json");

    var promptHashes = new Dictionary<Guid, byte[]>();
    string promptsDir = Path.Combine(root, "prompts");

    foreach (var prompt in document.Prompts)
    {
        string path = Path.Combine(promptsDir, $"{prompt.Id:N}.md");
        promptHashes.Add(prompt.Id, SHA256.HashData(File.ReadAllBytes(path)));
    }

    return new MigrationSnapshot(
        SHA256.HashData(File.ReadAllBytes(libraryPath)),
        promptHashes);
}
```

For very large future files, a streaming hash helper is better, but current prompt files are text prompts and this implementation is acceptable.

## 9.4 Verify both source and target

After copy and normal target structural validation:

1. rehash current source `library.json` and compare to initial library hash;
2. rehash every source prompt referenced by the initial document;
3. hash every target prompt for the same ID;
4. require source-before == source-after == target-copy for each prompt;
5. on mismatch, throw and roll back files created during this migration.

Helper:

```csharp
private static bool HashEquals(byte[] a, byte[] b)
    => CryptographicOperations.FixedTimeEquals(a, b);
```

Fixed-time comparison is not security-critical here; `SequenceEqual` is also acceptable. Do not waste effort adding cryptographic security architecture.

## 9.5 Do not silently exclude extra `.md` safety files

CRUU2 allowed copying additional top-level `.md` files as safety artifacts. Keep that behavior unless another authority explicitly changes it.

The **integrity snapshot requirement applies at minimum to every metadata-referenced active prompt**.

## 9.6 Deterministic tests

Do not write a flaky “start thread, sleep 5 ms, mutate file” test.

Preferred shape: extract filesystem operations behind a tiny internal abstraction or internal helper that can inject a mutation between copy and final verification.

Example interface if needed:

```csharp
internal interface IMigrationFileOps
{
    byte[] ReadAllBytes(string path);
    void CopyFile(string source, string destination);
    IEnumerable<string> EnumeratePromptFiles(string directory);
}
```

Do not over-generalize all of `System.IO`.

Required tests:

```text
Migration_prompt_body_source_change_after_copy_aborts_and_rolls_back
Migration_target_prompt_hash_mismatch_aborts
Migration_stable_prompt_hashes_succeed
Migration_metadata_change_still_aborts
```

---

# 10. CRUU3-007 + CRUU3-008 — enforce disjoint managed-root topology

## 10.1 Current check is one-directional

Current code rejects:

```text
target is inside current
```

but allows:

```text
current is inside target
```

Example:

```text
current = C:\Data\PromptHelper
new target = C:\Data
```

After switching, the old root is physically nested inside the new active root.

This creates confusing backup, migration, and future-selection behavior.

## 10.2 Add a topology validator

Create:

`src/PromptHelper/Services/DataRootTopologyValidator.cs`

Suggested implementation:

```csharp
namespace PromptHelper.Services;

public static class DataRootTopologyValidator
{
    public static bool IsStrictDescendant(string candidate, string parent)
    {
        string candidateFull = Normalize(candidate);
        string parentFull = Normalize(parent);

        string parentPrefix = parentFull + Path.DirectorySeparatorChar;
        return candidateFull.StartsWith(
            parentPrefix,
            StringComparison.OrdinalIgnoreCase);
    }

    public static void ValidateDisjointOrSame(
        string currentRoot,
        string targetRoot)
    {
        string current = Normalize(currentRoot);
        string target = Normalize(targetRoot);

        if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (IsStrictDescendant(target, current) ||
            IsStrictDescendant(current, target))
        {
            throw new InvalidOperationException(
                "The current and target data folders cannot contain one another.");
        }
    }

    private static string Normalize(string path)
        => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
```

Use the helper in `DataFolderMigrationService` instead of a private one-directional method.

## 10.3 Protect the fixed bootstrap root

The bootstrap settings live under:

```text
%LOCALAPPDATA%\PromptHelper
```

That directory is also the normal default data root.

For **new custom target selections**:

- target equal to bootstrap/default root = allowed;
- target disjoint from bootstrap root = allowed;
- target strict descendant of bootstrap root = reject;
- target strict ancestor of bootstrap root = reject.

Do not retroactively strand a user who somehow already has an older accepted configuration. Apply this to new settings selection/migration validation.

## 10.4 Optional conservative volume-root guard

Rejecting `C:\`, `D:\`, or the root of a UNC share is recommended because Prompt Helper should own a dedicated data directory, not treat an entire volume/share as its managed root.

Helper:

```csharp
private static bool IsVolumeRoot(string path)
{
    string full = Path.GetFullPath(path);
    string? root = Path.GetPathRoot(full);

    return root is not null &&
        string.Equals(
            full.TrimEnd('\\', '/'),
            root.TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
}
```

If this is added, document it. Do not add arbitrary path restrictions beyond these safety cases.

## 10.5 Tests

```text
Topology_same_root_allowed
Topology_sibling_roots_allowed
Topology_target_descendant_rejected
Topology_target_ancestor_rejected
Topology_custom_target_inside_bootstrap_rejected
Topology_custom_target_ancestor_of_bootstrap_rejected
Topology_default_bootstrap_root_exactly_allowed
Topology_volume_root_rejected_if_guard_enabled
```

---

# 11. CRUU3-009 — recognize backup-only existing target libraries

## 11.1 Inconsistency

Normal startup can recover when:

```text
library.json = missing
library.backup.json = valid
```

But `DataFolderMigrationService` currently only decides “existing target library” when `target/library.json` exists.

So a backup-only target is treated like an empty migration target until copying reaches the already-existing backup and collides.

## 11.2 Add an explicit target-state classifier

Use states:

```csharp
internal enum TargetLibraryKind
{
    Empty,
    ValidPrimary,
    RecoverableBackupOnly,
    CorruptPrimaryWithValidBackup,
    Invalid
}
```

A richer record is preferable if warnings/errors must be carried.

## 11.3 Required target matrix

| Target state | Action |
|---|---|
| neither primary nor backup | migrate current source |
| valid primary | existing library; do not overwrite |
| primary missing + valid backup | existing recoverable library; do not overwrite |
| corrupt primary + valid backup | conservative: reject switch until target is recovered/started normally; do not overwrite |
| future-schema primary | reject as incompatible/newer |
| corrupt/no valid backup | reject |
| unreadable metadata | reject |

For backup-only target, return:

```text
ExistingLibraryFound = true
Copied = false
Warning = "The selected folder contains a recoverable Prompt Helper safety backup but no primary library.json. Prompt Helper will recover it on startup; the current library will not be copied there."
```

Do not create the target primary during the settings dialog just to classify it. Let normal startup own recovery.

## 11.4 Tests

```text
Target_backup_only_valid_is_recognized_as_existing_library
Target_backup_only_valid_is_not_overwritten
Target_corrupt_primary_valid_backup_is_not_overwritten
Target_future_schema_is_rejected
Target_invalid_primary_and_backup_is_rejected
```

---

# 12. CRUU3-010 — preflight the exact write capability required after restart

## 12.1 Current blind spot

A selected target can be structurally valid/readable yet unusable for later edits.

Prompt Helper’s actual persistence primitive uses:

```text
create temporary file
flush to disk
File.Move for first write
File.Replace for overwrite
cleanup/delete
```

A folder that allows reading but not those writes should be rejected **before settings is committed**.

## 12.2 Add `DataRootCapabilityValidator`

Create:

`src/PromptHelper/Services/DataRootCapabilityValidator.cs`

Suggested API:

```csharp
public sealed class DataRootCapabilityValidator
{
    private readonly IAtomicTextWriter _writer;

    public DataRootCapabilityValidator(IAtomicTextWriter writer)
    {
        _writer = writer;
    }

    public void ValidateWritable(string root)
    {
        // implementation below
    }
}
```

## 12.3 Exact probe

Probe using files that are clearly application-owned temporary test artifacts and always clean them.

```csharp
public void ValidateWritable(string root)
{
    string probeDir = Path.Combine(
        root,
        $".prompthelper-write-probe-{Guid.NewGuid():N}");

    string probeFile = Path.Combine(probeDir, "probe.txt");

    try
    {
        Directory.CreateDirectory(probeDir);

        _writer.Write(probeFile, "create");
        _writer.Write(probeFile, "replace"); // exercises File.Replace path

        File.Delete(probeFile);
        Directory.Delete(probeDir);
    }
    catch
    {
        try
        {
            if (File.Exists(probeFile))
                File.Delete(probeFile);

            if (Directory.Exists(probeDir) &&
                !Directory.EnumerateFileSystemEntries(probeDir).Any())
            {
                Directory.Delete(probeDir);
            }
        }
        catch
        {
            // cleanup is best effort; preserve original error
        }

        throw;
    }
}
```

Also probe the existing `prompts` directory if it exists, because root permissions and child-directory permissions can differ.

Do not write into `library.json` or a real prompt file.

## 12.4 Where to run it

### Existing target library

```text
validate target metadata/readability
→ capability probe
→ if probe succeeds, offer explicit switch confirmation
→ save settings
```

### New/empty migration target

```text
validate source
→ create/copy under rollback scope
→ validate copied target
→ capability probe
→ if probe fails, rollback all files/dirs created by this migration
→ only then save settings
```

Do not leave a copied-but-unusable migration target looking successful.

## 12.5 Deterministic tests

Prefer injecting `IAtomicTextWriter` or the capability validator itself.

```text
Capability_create_failure_rejects_target
Capability_replace_failure_rejects_target
Capability_probe_cleans_temporary_files_on_success
Capability_probe_best_effort_cleans_on_failure
New_target_capability_failure_rolls_back_migration
Existing_target_capability_failure_does_not_change_library_files
Settings_are_not_saved_when_capability_validation_fails
```

---

# 13. CRUU3-011 — existing-library switch requires explicit semantic confirmation

## 13.1 Current ambiguity

`DataFolderChangeResult` contains:

```csharp
bool ExistingLibraryFound
bool Copied
```

but `SettingsDialog` ignores the distinction and always shows essentially the same “data folder saved / previous folder left as safety copy” success message.

This is technically safe from overwrite, but unsafe from user expectation.

## 13.2 Common surprising scenario

```text
1. User migrated from default root A to custom root B months ago.
2. A still contains the old safety-copy library.
3. User chooses A because they think “move my current library back to default.”
4. Migration sees valid A and intentionally does NOT overwrite it.
5. Settings changes to A.
6. On restart, user sees months-old A and thinks recent prompts vanished.
```

No data was deleted, but this is exactly the kind of avoidable incident a settings UI should prevent.

## 13.3 Required confirmation

If `ExistingLibraryFound == true`, show a confirmation **before saving settings**:

```text
Existing Prompt Helper library found

The selected folder already contains a Prompt Helper library.

Your CURRENT library will NOT be copied, merged, or overwritten.
After restart, Prompt Helper will open the library that already exists at:

<target path>

If you intended to move the current library, cancel and choose an empty folder instead.

Switch to the existing library anyway?

[Cancel] [Switch Library]
```

If `DataFolderChangeResult.Warning` is non-empty, display it in this dialog too.

## 13.4 Do not invent merge behavior

Forbidden fixes:

```text
automatically overwrite existing target
merge categories/prompts by title
merge by GUID
rename target library and replace it
copy current over existing because it “looks newer”
```

The accepted behavior remains **switch without overwrite**; CRUU3 only makes it explicit.

## 13.5 Testable helper

Avoid burying all policy in a private MessageBox handler. Extract a small decision result or dialog service if needed:

```csharp
public interface IUserConfirmationService
{
    bool ConfirmExistingLibrarySwitch(string targetPath, string? warning);
}
```

This makes unit testing deterministic.

Required tests:

```text
Existing_target_cancel_does_not_save_settings
Existing_target_confirm_saves_target_settings
Empty_target_does_not_show_existing_library_confirmation
Existing_target_warning_is_exposed_to_user
```

---

# 14. CRUU3-012 — detect a target library already in use

## 14.1 Current race/UX problem

Prompt Helper locks each active data root via `.app.lock` with `FileShare.None`.

The settings flow does not test whether the selected existing library is already owned by another running Prompt Helper process.

A switch can therefore be saved successfully and only fail on next startup.

## 14.2 Add a non-creating lock probe

Do **not** call `AppInstanceLock.TryAcquire` directly for the target preflight because it can create `.app.lock` when none exists.

Add:

```csharp
public static bool IsExistingLockHeld(string root)
{
    string path = Path.Combine(root, ".app.lock");

    if (!File.Exists(path))
    {
        return false;
    }

    try
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        return false;
    }
    catch (IOException ex) when (IsSharingOrLockViolation(ex))
    {
        return true;
    }
}
```

Move the Win32 sharing/lock-violation helper to a reusable internal/public-safe place if necessary.

Do not delete an existing lock file merely because it is not held. The owner process may have crashed; the file itself is harmless because locking is stream-based.

## 14.3 Behavior

If held:

```text
The selected Prompt Helper library is currently in use by another running instance.
Close that instance and try again.
```

Do not save settings.

Startup lock acquisition remains the final race-proof authority; this preflight is for early user feedback, not a replacement for startup locking.

## 14.4 Tests

```text
Target_lock_absent_is_allowed
Target_lock_file_present_but_unlocked_is_allowed
Target_lock_held_by_other_stream_is_rejected
Target_lock_rejection_does_not_save_settings
```

---

# 15. CRUU3-013 — CI must prove the actual intended Windows publish artifact

## 15.1 Current workflow scope

Current Windows CI does:

```text
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

That is valuable, but it does not prove the release packaging path.

The existing user guide states that the intended `win-x64` release is self-contained and should not require users to install .NET separately.

## 15.2 Add a publish gate

Add after tests:

```yaml
      - name: Publish self-contained win-x64
        shell: pwsh
        run: |
          dotnet publish src/PromptHelper/PromptHelper.csproj `
            -c Release `
            -r win-x64 `
            --self-contained true `
            -o artifacts/publish-check

      - name: Verify publish payload
        shell: pwsh
        run: |
          $required = @(
            "artifacts/publish-check/PromptHelper.exe",
            "artifacts/publish-check/LICENSE",
            "artifacts/publish-check/THIRD_PARTY_NOTICES.md"
          )

          foreach ($path in $required) {
            if (-not (Test-Path $path)) {
              throw "Missing required publish artifact: $path"
            }
          }
```

Do not add `--single-file`, trimming, ReadyToRun, signing, installer generation, or MSIX unless another authority explicitly requests them.

## 15.3 Upload the publish check

```yaml
      - name: Upload publish-check
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: PromptHelper-publish-check
          path: artifacts/publish-check
          if-no-files-found: error
```

If artifact size becomes undesirable on every PR, upload on main/manual/release while still executing publish validation on PR.

## 15.4 Runtime acceptance remains manual

CI publish success does not prove:

```text
Explorer icon cache behavior
taskbar icon
Alt+Tab icon
real OpenFolderDialog interaction
visual clipping at 125%/150% DPI
0.5-second tooltip feel
```

Those remain in the final Windows manual gate.

---

# 16. CRUU3-014 — make flakiness evidence reproducible, not historical

## 16.1 Current mismatch

The supplied report says five full-suite runs passed.

The committed CI workflow itself executes the suite once and uploads TRX only on failure.

Therefore future code cannot inherit the claim “five consecutive passes” unless the repeated run is executed again.

## 16.2 Recommended CI shape

Keep normal PR CI efficient, but add a repeatable stress mode.

### Option A — always run three times

Good if total suite duration is small:

```yaml
      - name: Test Release x3
        shell: pwsh
        run: |
          1..3 | ForEach-Object {
            dotnet test PromptHelper.slnx `
              -c Release `
              --no-build `
              --logger "trx;LogFileName=test-results-$_.trx"

            if ($LASTEXITCODE -ne 0) {
              exit $LASTEXITCODE
            }
          }
```

### Option B — standard one-run CI + manual five-run stress job

Preferred if CI time matters:

```yaml
on:
  push:
    branches: [ main ]
  pull_request:
  workflow_dispatch:
    inputs:
      stress:
        description: 'Run full suite five times'
        required: false
        default: 'false'
```

Then conditionally loop five times when `stress == 'true'`.

## 16.3 Always retain test evidence

Change test-results upload from:

```yaml
if: failure()
```

to:

```yaml
if: always()
```

with unique TRX names for repeated runs.

## 16.4 Add workflow timeout

Use a reasonable job timeout so a WPF deadlock cannot consume an entire runner indefinitely:

```yaml
jobs:
  build-test:
    timeout-minutes: 20
```

Adjust only if measured normal execution requires more.

---

# 17. CRUU3-015 — keep development tolerant of missing icon, but make release validation strict

## 17.1 Current state

The repository still has no:

```text
src/PromptHelper/Assets/PromptHelperLogo.svg
src/PromptHelper/Assets/PromptHelper.ico
```

The hardened generator exists and is good infrastructure, but it cannot generate the icon without the real source SVG.

`IconAssetTests.PromptHelperIco_when_present...` intentionally returns successfully when the ICO is absent.

Therefore:

```text
214/214 tests passing
≠
application icon feature release-complete
```

The supplied report correctly disclosed this dependency.

## 17.2 Do not change the source-asset rule

```text
MISSING_REQUIRED_ASSET: Prompt Helper logo SVG
```

Do not generate an artificial substitute.

## 17.3 Add a release-asset verification script

Create:

`tools/VerifyReleaseAssets.ps1`

Example:

```powershell
[CmdletBinding()]
param(
    [switch]$RequireIcon
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$svg = Join-Path $repoRoot "src\PromptHelper\Assets\PromptHelperLogo.svg"
$ico = Join-Path $repoRoot "src\PromptHelper\Assets\PromptHelper.ico"

if ($RequireIcon) {
    if (-not (Test-Path $svg)) {
        throw "MISSING_REQUIRED_ASSET: PromptHelperLogo.svg"
    }

    if (-not (Test-Path $ico)) {
        throw "MISSING_REQUIRED_ASSET: PromptHelper.ico. Run tools/GenerateAppIcon.ps1."
    }
}
```

Reuse/extract the existing binary ICO frame validation rather than maintaining two diverging validators.

## 17.4 CI policy

### Development PR/build gate

May remain tolerant while the real asset has not been supplied, provided output clearly reports:

```text
ICON ASSET: NOT PRESENT — release icon validation deferred
```

### Release/manual release gate

Must run:

```powershell
./tools/VerifyReleaseAssets.ps1 -RequireIcon
```

and fail if asset is missing.

## 17.5 Improve MainWindow icon exception handling after asset exists

The constructor currently catches **all** icon-load exceptions and silently ignores them.

Missing optional resource is acceptable during development. A **present but corrupt** icon should not be silently treated as equivalent.

Preferred shape:

```csharp
private void TryApplyApplicationIcon()
{
    var uri = new Uri(
        "pack://application:,,,/PromptHelper;component/Assets/PromptHelper.ico",
        UriKind.Absolute);

    StreamResourceInfo? resource = Application.GetResourceStream(uri);
    if (resource is null)
    {
        return; // allowed dev state before real asset is supplied
    }

    Icon = BitmapFrame.Create(uri);
}
```

If the resource exists but decoding fails, let the construction smoke/release test expose it. Do not use `catch {}` around a present corrupt resource.

## 17.6 Once the SVG is supplied

Execute exactly:

```powershell
./tools/GenerateAppIcon.ps1
./tools/VerifyReleaseAssets.ps1 -RequireIcon
dotnet build PromptHelper.slnx -c Release
dotnet test PromptHelper.slnx -c Release
dotnet publish src/PromptHelper/PromptHelper.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish-check
```

Commit both authoritative SVG and generated ICO unless repository policy explicitly says generated release assets are not committed.

Then manually verify:

```text
Explorer executable icon
taskbar icon
Alt+Tab/window icon
small 16x16/24x24 clarity
transparency/no unintended square background
no distorted aspect ratio
```

---

# 18. CRUU3-016 — close the Unicode single-line validation hole

## 18.1 Current validator

Prompt titles and category names reject:

```csharp
char.IsControl(c)
```

This catches CR/LF/tab and normal control characters.

But Unicode `U+2028 LINE SEPARATOR` and `U+2029 PARAGRAPH SEPARATOR` are separator categories, not classic control characters.

They can therefore enter metadata that is intended to be single-line.

## 18.2 Add one shared helper

In `TextUtilities`:

```csharp
public static bool ContainsForbiddenSingleLineCharacter(string value)
{
    ArgumentNullException.ThrowIfNull(value);

    foreach (char c in value)
    {
        if (char.IsControl(c) || c is '\u2028' or '\u2029')
        {
            return true;
        }
    }

    return false;
}
```

Alternatively inspect `CharUnicodeInfo.GetUnicodeCategory` and reject `LineSeparator`/`ParagraphSeparator`, but the explicit helper above is simple and sufficient.

## 18.3 Use it consistently

Replace duplicated `Any(char.IsControl)` checks for:

```text
PromptRecord.Title validation
NormalizeAndValidatePromptTitle
category persisted-name validation
ValidateCategoryNameInput
```

Use one semantic rule to prevent drift.

## 18.4 Tests

```text
Prompt_title_U2028_rejected_without_file_creation
Prompt_title_U2029_rejected_without_file_creation
Prompt_edit_U2028_rejected_without_body_or_metadata_change
Category_name_U2028_rejected
Category_name_U2029_rejected
Normal_non_ASCII_headline_is_allowed
Emoji_headline_is_allowed
```

Do not impose an arbitrary ASCII-only restriction.

---

# 19. CRUU3-017 — finish WPF test-host lifecycle hygiene

This is low priority compared with product data safety, but it is worth fixing because the verification report relies heavily on WPF tests.

## 19.1 Current host improvements are real

The new shared `WpfTestHost` correctly uses:

```text
one background STA thread
one WPF Application
ShutdownMode.OnExplicitShutdown
Dispatcher.Run()
```

This fixes the previous multi-threaded singleton-Application problem.

## 19.2 Remaining cleanup weaknesses

`Stop()` currently:

1. calls `InvokeShutdown`;
2. calls `thread?.Join(TimeSpan.FromSeconds(10))`;
3. ignores whether the thread actually joined;
4. does not reset `_thread`, `_dispatcher`, `_startupException`, or the readiness event.

Also some WPF tests instantiate dialogs and never explicitly close them.

These are not current user-data bugs, but they weaken repeated/in-process test reliability.

## 19.3 Required hardening

At assembly cleanup:

```csharp
public static void Stop()
{
    Dispatcher? dispatcher;
    Thread? thread;

    lock (Sync)
    {
        dispatcher = _dispatcher;
        thread = _thread;
    }

    if (dispatcher != null && !dispatcher.HasShutdownStarted)
    {
        dispatcher.InvokeShutdown();
    }

    if (thread != null && !thread.Join(TimeSpan.FromSeconds(10)))
    {
        throw new TimeoutException(
            "WPF test host did not stop within 10 seconds.");
    }

    lock (Sync)
    {
        _thread = null;
        _dispatcher = null;
        _startupException = null;
        Ready.Reset();
    }
}
```

If MSTest assembly cleanup should never throw due framework constraints, assert/fail through a controlled test-host mechanism instead. Do not silently ignore a deadlocked WPF thread.

## 19.4 Close test windows deterministically

Pattern:

```csharp
WpfTestHost.Invoke(() =>
{
    var dialog = new PromptEditorDialog(...);
    try
    {
        // assertions
    }
    finally
    {
        dialog.Close();
    }
});
```

Apply to all tests that instantiate `Window`/dialog objects.

## 19.5 Wording correction

Do not claim:

```text
all 214 tests execute on the single STA thread
```

unless the entire suite truly does.

Correct claim:

```text
all WPF operations that require STA are marshalled through the shared WpfTestHost STA dispatcher
```

---

# 20. CRUU3-018 — synchronize documentation with the corrected transition and release model

After the code fixes, update both:

```text
README.md
Prompt_Helper_Nutzungsguide_DE_v2_FINAL.md
```

## 20.1 Required user-facing data-folder semantics

Document:

```text
Selecting an EMPTY folder:
→ current library is copied there
→ old folder is preserved
→ Prompt Helper closes
→ reopen to use new folder

Selecting a folder with an EXISTING Prompt Helper library:
→ current library is NOT copied or merged
→ explicit confirmation is required
→ Prompt Helper closes
→ reopen to use that existing library
```

## 20.2 Document target-folder requirements

Target must:

```text
be an absolute path
not be inside the current data root
not contain the current data root
not overlap the fixed bootstrap root unless it is exactly the default PromptHelper root
support Prompt Helper create/replace/delete writes
not be actively locked by another Prompt Helper instance when switching to an existing library
```

Keep wording understandable for nontechnical users.

## 20.3 Document settings recovery truthfully

Explain:

```text
settings.json = primary bootstrap setting
settings.backup.json = safety backup
backup is used only when primary settings are missing/corrupt
newer-version settings are not downgraded through the backup
read/permission failures are reported instead of silently selecting stale settings
```

## 20.4 Release/build section

README should include:

```powershell
dotnet restore PromptHelper.slnx
dotnet build PromptHelper.slnx -c Release
dotnet test PromptHelper.slnx -c Release
dotnet publish src/PromptHelper/PromptHelper.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish-check
```

The German guide’s claim that users do not need a separate .NET install must only refer to the **self-contained win-x64 release artifact**, not arbitrary framework-dependent `dotnet build` output.

## 20.5 Icon dependency

Until the real SVG lands, docs/release notes must not imply the branded EXE/taskbar icon has been validated.

Use:

```text
Release asset pending: PromptHelperLogo.svg
```

Once supplied and verified, remove the pending notice.

---

# 21. Complete implementation order for a weak coding model

Do not fix findings in random file order. Use these phases because they minimize overlapping edits and make regressions easier to isolate.

## Phase A — establish a clean baseline

1. Confirm current HEAD is the intended CRUU2 implementation commit or a descendant.
2. Run the current tests once before modification.
3. Record test count and failures.
4. Do not edit `cruu1.md`, `cruu2.md`, prior `_plh*.md`, or historical reports as implementation state.
5. Create/update tests only for current product code.

Commands:

```powershell
git status --short
git rev-parse HEAD
dotnet --info
dotnet restore PromptHelper.slnx
dotnet build PromptHelper.slnx -c Release
dotnet test PromptHelper.slnx -c Release
```

Gate:

```text
If baseline fails for a newly introduced source defect, investigate before applying CRUU3.
If only the externally missing logo is reported by a release-only gate, keep it as the explicit asset dependency.
```

## Phase B — settings authority state machine

Implement in this order:

```text
AppSettings.CurrentSchemaVersion
UnsupportedSettingsSchemaException
SettingsReadState
strict settings parser
primary/backup decision matrix
unreadable-primary stop behavior
Save schema guard
warning propagation
App startup future-schema handling
App warning display
```

Then run only settings tests plus full suite.

## Phase C — data-root process boundary and existing-target user decision

Implement:

```text
forced shutdown after successful new-root save
same-path no shutdown
existing-library confirmation
existing-target warning display
held target lock preflight
```

Do not yet modify migration hash/topology in this phase unless required for compilation.

## Phase D — migration topology, target state, integrity and capability

Implement:

```text
DataRootTopologyValidator
bootstrap overlap guard
target library classifier
backup-only target handling
MigrationSnapshot library + referenced prompt hashes
source/target prompt hash verification
DataRootCapabilityValidator
rollback integration
```

Run all migration tests after each logical subsection.

## Phase E — shared single-line validation

Implement the shared Unicode-aware single-line helper and update both prompt-title and category-name validators.

Run:

```text
TextUtilitiesTests
LibraryValidatorTests
PromptLibraryServiceTests
```

then full suite.

## Phase F — release/CI proof

Implement:

```text
self-contained win-x64 publish-check in CI
publish payload assertions
always-upload TRX
repeat/stress test mode
timeout
release asset verifier
```

Do not make normal development impossible solely because the user has not supplied the real SVG; enforce it in release validation.

## Phase G — WPF test-host cleanup

Only after product logic is stable:

```text
close all test windows
assert WpfTestHost shutdown
reset host state if restartable
```

Re-run full suite 5 times locally on Windows.

## Phase H — docs and manual certification

Update README/guide to match actual behavior, then execute final Windows manual flows.

---

# 22. Suggested file-by-file change map

| File | Required CRUU3 work |
|---|---|
| `src/PromptHelper/Models/AppSettings.cs` | Add `CurrentSchemaVersion` constant. |
| `src/PromptHelper/Models/OperationResults.cs` | Extend result/warning types only if needed for target classification; do not overload booleans beyond clarity. |
| `src/PromptHelper/Services/UnsupportedSettingsSchemaException.cs` | New exact future-settings-schema exception. |
| `src/PromptHelper/Services/AppSettingsRepository.cs` | State-machine reader, future-schema authority, unreadable state, warnings, Save schema guard. |
| `src/PromptHelper/App.xaml.cs` | Specific future-settings catch; show all settings warnings. |
| `src/PromptHelper/Services/DataRootTopologyValidator.cs` | New disjoint-root/topology helper. |
| `src/PromptHelper/Services/DataRootCapabilityValidator.cs` | New create/replace/delete probe. |
| `src/PromptHelper/Services/DataFolderMigrationService.cs` | Target classifier, two-way topology, backup-only handling, prompt-body snapshot hashes, capability validation/rollback integration. |
| `src/PromptHelper/Services/AppInstanceLock.cs` or small companion | Reusable held-lock detection without creating a lock file. |
| `src/PromptHelper/Views/SettingsDialog.xaml.cs` | Existing-library confirmation, warnings, no save on rejected target, force process-boundary result. |
| `src/PromptHelper/MainWindow.xaml.cs` | Act on `RestartRequired`; improve icon loading missing-vs-corrupt distinction. |
| `src/PromptHelper/Infrastructure/TextUtilities.cs` | Unicode-aware single-line validation helper. |
| `src/PromptHelper/Services/LibraryValidator.cs` | Use shared single-line helper. |
| `src/PromptHelper/Services/PromptLibraryService.cs` | Use shared single-line helper for title validation. |
| `tests/PromptHelper.Tests/AppSettingsRepositoryTests.cs` | Future-schema and unreadable-primary matrix, warnings, Save schema guard. |
| `tests/PromptHelper.Tests/DataFolderMigrationServiceTests.cs` | Ancestor/bootstrap/backup-only/hash/capability/lock scenarios. |
| `tests/PromptHelper.Tests/Cruu1ComprehensiveVerificationTests.cs` | Keep feature checks; close windows; add transition semantics only if appropriate. |
| `tests/PromptHelper.Tests/WpfTestHost.cs` | Assert shutdown/reset. |
| `.github/workflows/windows-ci.yml` | Publish, repeated/stress testing, always-upload evidence. |
| `tools/VerifyReleaseAssets.ps1` | New strict release-only asset gate. |
| `tools/GenerateAppIcon.ps1` | Prefer reuse of shared ICO validation if refactored; otherwise no behavioral change required until SVG supplied. |
| `README.md` | Updated commands and root-switch semantics. |
| `Prompt_Helper_Nutzungsguide_DE_v2_FINAL.md` | End-user transition/existing-library/release semantics. |

---

# 23. Automated test blueprint

The weak implementation model must add **behavioral tests**, not source-string tests, wherever product logic can be isolated.

## 23.1 Settings authority tests

Create/extend `AppSettingsRepositoryTests.cs` with:

```text
CRUU3_002_Primary_future_schema_with_valid_old_backup_stops
CRUU3_002_Primary_future_schema_does_not_rewrite_files
CRUU3_002_Missing_primary_future_backup_stops
CRUU3_002_Corrupt_primary_future_backup_stops
CRUU3_003_Locked_valid_primary_does_not_use_stale_backup
CRUU3_003_Unreadable_primary_preserves_primary_and_backup
CRUU3_004_Valid_primary_backup_sync_failure_returns_warning
CRUU3_004_Backup_recovery_primary_restore_failure_returns_warning
CRUU3_005_Save_invalid_schema_writes_nothing
```

For every failure-path test assert **file bytes remain unchanged** where applicable.

Example:

```csharp
byte[] primaryBefore = File.ReadAllBytes(settingsPath);
byte[] backupBefore = File.ReadAllBytes(backupPath);

Assert.Throws<UnsupportedSettingsSchemaException>(
    () => repo.LoadOrRecover());

CollectionAssert.AreEqual(primaryBefore, File.ReadAllBytes(settingsPath));
CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(backupPath));
```

## 23.2 Data-root transition tests

If a lifetime abstraction is introduced:

```text
CRUU3_001_Changed_root_requests_shutdown_once
CRUU3_001_Same_root_save_does_not_shutdown
CRUU3_001_Cancel_does_not_shutdown
```

Also verify no settings save occurs after user cancels an existing-library switch.

## 23.3 Topology tests

```text
CRUU3_007_Target_descendant_rejected
CRUU3_007_Target_ancestor_rejected
CRUU3_007_Sibling_target_allowed
CRUU3_008_Custom_target_under_bootstrap_rejected
CRUU3_008_Custom_target_above_bootstrap_rejected
CRUU3_008_Exact_default_bootstrap_root_allowed
```

Use temporary directories and a parameterized/fake bootstrap path to avoid writing real LocalAppData during tests.

## 23.4 Existing-target matrix tests

```text
CRUU3_009_Valid_primary_target_detected_existing
CRUU3_009_Valid_backup_only_target_detected_existing
CRUU3_009_Backup_only_target_not_overwritten
CRUU3_009_Corrupt_primary_valid_backup_target_rejected_conservatively
CRUU3_009_Future_schema_target_rejected
CRUU3_011_Existing_target_requires_user_confirmation
CRUU3_011_Cancel_existing_target_preserves_settings
CRUU3_012_Held_target_lock_blocks_switch
```

## 23.5 Migration snapshot tests

Test initial and final hashes.

```text
CRUU3_006_Stable_snapshot_copies_exact_prompt_bytes
CRUU3_006_Prompt_changed_during_copy_aborts
CRUU3_006_Target_prompt_mismatch_aborts
CRUU3_006_Metadata_changed_during_copy_aborts
CRUU3_006_Failed_snapshot_removes_only_files_created_by_this_attempt
```

Critical rollback assertion:

```text
pre-existing unrelated files in target MUST remain untouched
```

## 23.6 Capability tests

```text
CRUU3_010_Probe_exercises_create_and_replace
CRUU3_010_Probe_cleanup_success
CRUU3_010_Probe_replace_failure_surfaces
CRUU3_010_New_target_probe_failure_rolls_back_migrated_files
CRUU3_010_Existing_target_probe_failure_does_not_modify_library
```

## 23.7 Unicode single-line tests

```text
CRUU3_016_U2028_prompt_title_rejected
CRUU3_016_U2029_prompt_title_rejected
CRUU3_016_U2028_category_rejected
CRUU3_016_Emoji_and_accented_text_allowed
```

## 23.8 Release/static configuration tests

Use source-string checks only for configuration artifacts where runtime object testing is not practical.

```text
WindowsCI_contains_self_contained_win_x64_publish_gate
WindowsCI_preserves_TRX_with_if_always
VerifyReleaseAssets_script_requires_icon_in_release_mode
```

Do not count those as proof that GitHub Actions actually executed successfully.

---

# 24. Fault-injection matrix

A repair is incomplete if it only works on normal filesystem paths.

| Fault | Expected result |
|---|---|
| primary settings JSON malformed, backup valid | recover backup + warning |
| primary settings future schema, backup old valid | stop; do not recover old backup |
| primary settings locked, backup valid | stop; do not use backup |
| valid primary settings, backup sync write fails | run with primary + warning |
| backup used, primary restoration write fails | run with backup + warning |
| Save settings primary write fails | no success message; no root transition |
| Save settings backup write fails | primary authoritative; warning shown |
| migration source metadata changes | abort + rollback new target artifacts |
| migration source prompt changes | abort + rollback new target artifacts |
| target prompt write collision | abort; preserve collision file |
| target capability create fails | reject target; no settings change |
| target capability replace fails | reject target; no settings change |
| target currently locked by another app | reject target; no settings change |
| existing target switch user cancels | no settings change |
| WPF host shutdown stalls | test run fails rather than silently succeeding |

---

# 25. Manual Windows regression matrix

These tests must be done after automated tests pass. They cannot be replaced by source inspection.

## 25.1 Basic existing behavior

```text
[ ] Create category
[ ] Rename category through wrench menu
[ ] Delete empty category
[ ] Block deletion of non-empty category
[ ] Create prompt with automatic headline
[ ] Create prompt with custom headline
[ ] Edit automatic prompt without touching headline; remains automatic
[ ] Edit automatic prompt and deliberately change headline; becomes custom
[ ] Clear custom headline; returns to automatic
[ ] Wrap long lines on/off without prompt-byte mutation
[ ] Move prompt
[ ] Duplicate prompt
[ ] Delete prompt
[ ] Copy main card prompt
[ ] Copy recent-bar prompt
[ ] Recent order max 3 and session reset
[ ] Full tooltip opens after approximately 0.5 s and shows full text
```

## 25.2 Empty-folder migration

```text
[ ] Start on A
[ ] Create unique marker prompt
[ ] Select empty B
[ ] Save
[ ] App closes immediately
[ ] Reopen
[ ] B is active
[ ] marker exists
[ ] A remains intact
[ ] settings.json points to B
[ ] settings.backup.json synchronized or warning shown
```

## 25.3 Existing-library switch

```text
[ ] Prepare A with marker “A”
[ ] Prepare B with marker “B”
[ ] Start on A
[ ] Select B
[ ] Explicit message says A will NOT be copied/merged/overwritten
[ ] Cancel → remain on A, settings unchanged
[ ] Repeat and confirm
[ ] App closes
[ ] Reopen → B marker visible
[ ] A marker remains untouched in A
```

## 25.4 Switch back to stale default safety copy

This is the scenario CRUU3-011 is designed to make understandable:

```text
[ ] Default A contains old library
[ ] Current B contains newer marker
[ ] Select A
[ ] Confirmation explicitly warns current B will not be copied
[ ] Cancel if migration was intended
[ ] Verify user can instead select a new empty folder for migration
```

## 25.5 Locked target

```text
[ ] Open B in another Prompt Helper instance
[ ] From A, try selecting B
[ ] Target-in-use message appears before settings save
[ ] Close B instance
[ ] Retry and confirm works
```

## 25.6 Settings future-schema safety

Use a disposable test profile/data backup only:

```text
[ ] primary settings schema=999, backup schema=1
[ ] start app
[ ] app refuses downgrade with specific newer-settings message
[ ] primary and backup bytes unchanged
```

## 25.7 Publish artifact

From clean checkout:

```powershell
dotnet publish src/PromptHelper/PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/publish-check
```

Then:

```text
[ ] PromptHelper.exe launches on Windows without separately installing .NET runtime
[ ] LICENSE present
[ ] THIRD_PARTY_NOTICES.md present
[ ] first run initializes correctly
[ ] close/reopen preserves library
[ ] Tools & Settings opens
[ ] folder picker opens
```

## 25.8 Icon acceptance — only after real SVG is supplied

```text
[ ] PromptHelperLogo.svg present
[ ] PromptHelper.ico generated
[ ] release asset validator passes
[ ] Explorer shows branded icon
[ ] taskbar shows branded icon
[ ] Alt+Tab/window chrome shows branded icon
[ ] icon is not stretched
[ ] 16x16 and 24x24 remain legible
```

---

# 26. CI workflow reference implementation

A weak model may adapt the existing workflow, but the functional requirements should be equivalent to this:

```yaml
name: Windows CI

on:
  push:
    branches: [ main ]
  pull_request:
  workflow_dispatch:
    inputs:
      stress:
        description: Run the full test suite five times
        required: false
        default: 'false'

jobs:
  build-test-publish:
    runs-on: windows-latest
    timeout-minutes: 20

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore PromptHelper.slnx

      - name: Build Release
        run: dotnet build PromptHelper.slnx -c Release --no-restore

      - name: Test Release
        shell: pwsh
        run: |
          $runs = if ('${{ github.event_name }}' -eq 'workflow_dispatch' -and '${{ inputs.stress }}' -eq 'true') { 5 } else { 1 }

          for ($i = 1; $i -le $runs; $i++) {
            dotnet test PromptHelper.slnx `
              -c Release `
              --no-build `
              --logger "trx;LogFileName=test-results-$i.trx"

            if ($LASTEXITCODE -ne 0) {
              exit $LASTEXITCODE
            }
          }

      - name: Publish self-contained win-x64
        shell: pwsh
        run: |
          dotnet publish src/PromptHelper/PromptHelper.csproj `
            -c Release `
            -r win-x64 `
            --self-contained true `
            -o artifacts/publish-check

      - name: Verify publish payload
        shell: pwsh
        run: |
          $required = @(
            'artifacts/publish-check/PromptHelper.exe',
            'artifacts/publish-check/LICENSE',
            'artifacts/publish-check/THIRD_PARTY_NOTICES.md'
          )

          foreach ($path in $required) {
            if (-not (Test-Path $path)) {
              throw "Missing publish artifact: $path"
            }
          }

      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: '**/TestResults/**/*.trx'
          if-no-files-found: error

      - name: Upload publish-check
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: PromptHelper-publish-check
          path: artifacts/publish-check
          if-no-files-found: error
```

Release asset validation can be a separate manual/release workflow until the real SVG exists.

---

# 27. Weak-model “do not” traps

The implementing AI must follow these rules literally.

1. **Do not** claim the icon is complete while the real SVG is absent.
2. **Do not** invent a logo.
3. **Do not** make normal source builds fail solely because the external logo has not yet been supplied; make the release gate strict instead.
4. **Do not** recover from a future settings schema by using an older backup.
5. **Do not** recover from a locked/unreadable primary settings file using a stale backup.
6. **Do not** delete a future-schema settings file.
7. **Do not** rewrite a future-schema settings file.
8. **Do not** hot-swap the active repository graph after a folder change.
9. **Do not** let the user continue editing the old root after a successful migration/switch.
10. **Do not** auto-merge an existing target library.
11. **Do not** overwrite an existing target library.
12. **Do not** infer that an existing target is “older” or “newer” and choose for the user.
13. **Do not** silently switch to an existing target without confirmation.
14. **Do not** treat read-only success as proof that a target can support `File.Replace` writes.
15. **Do not** use timing sleeps to test concurrent file mutation if deterministic injection is possible.
16. **Do not** hash only `library.json` and call the migration snapshot complete.
17. **Do not** delete pre-existing target files during rollback.
18. **Do not** reject normal Unicode/emoji text simply to solve U+2028/U+2029.
19. **Do not** add a title-length limit unless an authority document requires one.
20. **Do not** add schema version 2 for these fixes.
21. **Do not** persist recent-copy history.
22. **Do not** replace the three-column grid with a new UI framework.
23. **Do not** introduce cloud sync, telemetry, SQLite, installer technology, or unrelated architecture.
24. **Do not** weaken tests so the suite becomes green.
25. **Do not** use `if (File.Exists(...)) return;` in a test that claims a release-required artifact is present; separate optional development tests from strict release tests.
26. **Do not** claim CI passed merely because the YAML file exists.
27. **Do not** claim a self-contained release works without executing the actual self-contained publish command.
28. **Do not** claim taskbar/Explorer icon validation from an ICO parser test alone.

---

# 28. Final verification commands

Run on Windows from repository root after implementing CRUU3:

```powershell
dotnet --info

dotnet clean PromptHelper.slnx -c Release

dotnet restore PromptHelper.slnx

dotnet build PromptHelper.slnx -c Release --no-restore

dotnet test PromptHelper.slnx -c Release --no-build --logger "trx;LogFileName=cruu3-run-1.trx"
dotnet test PromptHelper.slnx -c Release --no-build --logger "trx;LogFileName=cruu3-run-2.trx"
dotnet test PromptHelper.slnx -c Release --no-build --logger "trx;LogFileName=cruu3-run-3.trx"
dotnet test PromptHelper.slnx -c Release --no-build --logger "trx;LogFileName=cruu3-run-4.trx"
dotnet test PromptHelper.slnx -c Release --no-build --logger "trx;LogFileName=cruu3-run-5.trx"

dotnet publish src/PromptHelper/PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/publish-check
```

If the real SVG has been supplied:

```powershell
./tools/GenerateAppIcon.ps1
./tools/VerifyReleaseAssets.ps1 -RequireIcon

dotnet build PromptHelper.slnx -c Release
dotnet test PromptHelper.slnx -c Release

dotnet publish src/PromptHelper/PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/publish-check
```

Then launch:

```powershell
./artifacts/publish-check/PromptHelper.exe
```

Do not launch the `bin/Debug` app as a substitute for published-artifact validation.

---

# 29. Required final implementation report format

The weak implementation model must finish by producing a concise evidence report with this exact information:

```text
HEAD commit:

CRUU3 findings fixed:
- CRUU3-001: PASS/FAIL + evidence
- CRUU3-002: PASS/FAIL + evidence
...
- CRUU3-018: PASS/FAIL + evidence

Build:
- command
- exit code
- warnings
- errors

Tests:
- total
- passed
- failed
- skipped
- repeated run count

Publish:
- command
- output path
- PromptHelper.exe present: yes/no
- LICENSE present: yes/no
- THIRD_PARTY_NOTICES.md present: yes/no

Manual Windows checks:
- data-root migration forced close
- existing-library confirmation
- locked-target rejection
- automatic/custom headline behavior
- copy/recent bar
- tooltip
- self-contained launch

Icon:
- source SVG present: yes/no
- ICO generated: yes/no
- release asset validator: pass/deferred
- Explorer/taskbar/Alt+Tab: pass/deferred

Known limitations/dependencies:
```

Never report a deferred manual check as PASS.

---

# 30. Definition of done

CRUU3 is complete only when all of the following are true.

## Settings authority

```text
[ ] Future primary settings schema cannot be bypassed through old backup
[ ] Future backup schema is handled explicitly
[ ] Unreadable primary never silently falls back to stale backup
[ ] Valid-primary backup-sync failure returns visible warning
[ ] Backup recovery primary-restore failure returns visible warning
[ ] Save rejects unsupported schema before writing
```

## Data-root transition

```text
[ ] Successful changed-root Save forces process shutdown
[ ] Same-root Save does not force shutdown
[ ] No live old-root mutation window remains after migration
[ ] Existing target switch requires explicit confirmation
[ ] User cancellation leaves settings untouched
[ ] Held target library lock is reported before switch
```

## Migration integrity

```text
[ ] current/target roots cannot contain one another
[ ] custom target cannot overlap bootstrap root except exact default root
[ ] backup-only existing target is classified safely
[ ] source library hash is stable
[ ] every referenced prompt-body hash is stable
[ ] target prompt hashes match source snapshot
[ ] target supports create/replace/delete semantics
[ ] failed validation rolls back only files created by current operation
[ ] old source remains untouched
```

## Validation

```text
[ ] U+2028/U+2029 rejected in single-line metadata
[ ] normal Unicode/emoji remains allowed
[ ] prompt invalid-input transactions do not create or mutate files
```

## Verification/release

```text
[ ] clean Release build succeeds
[ ] full suite passes
[ ] full suite passes repeated Windows stress run
[ ] TRX evidence retained
[ ] self-contained win-x64 publish succeeds
[ ] published payload contains PromptHelper.exe + legal files
[ ] published EXE launches on Windows
```

## Icon

Until the real asset is supplied:

```text
[ ] normal development remains safe without icon
[ ] release gate explicitly reports MISSING_REQUIRED_ASSET
```

After the real asset is supplied:

```text
[ ] authoritative SVG committed/present
[ ] ICO generated and binary validated
[ ] project embeds ICO
[ ] published EXE icon verified in Explorer
[ ] taskbar icon verified
[ ] Alt+Tab/window icon verified
```

## Documentation

```text
[ ] README matches actual build/test/publish commands
[ ] German guide explains forced close/reopen after root change
[ ] German guide distinguishes migrate-to-empty vs switch-to-existing
[ ] custom-root constraints documented
[ ] settings recovery authority documented accurately
[ ] self-contained release claim backed by publish gate
[ ] icon dependency status truthful
```

Only after all applicable boxes are satisfied may the implementation be described as:

```text
CRUU3 CLEAN
```

If the logo SVG is still missing, the correct final state is:

```text
CRUU3 PRODUCT/CODE FIXES CLEAN
RELEASE ICON ASSET DEPENDENCY STILL OPEN
```

---

# 31. Compact implementer checklist

For a weak model that needs a final short sequence after reading the full document:

```text
1. Run baseline build/tests.
2. Add AppSettings.CurrentSchemaVersion.
3. Add UnsupportedSettingsSchemaException.
4. Replace settings LoadOrRecover with explicit Missing/Valid/Corrupt/Future/Unreadable state logic.
5. Propagate backup-sync/restore warnings and display any settings warning.
6. Guard Save against unsupported schema.
7. Force app shutdown after an actual data-root change.
8. Add explicit confirmation before switching to an existing target library.
9. Add held-target-lock preflight.
10. Add two-way current/target topology validation.
11. Add bootstrap-root overlap validation for new custom targets.
12. Classify backup-only target as an existing recoverable library.
13. Hash every referenced prompt before/after migration and compare target bytes.
14. Probe target create + replace + delete capability before saving settings.
15. Add Unicode U+2028/U+2029 single-line rejection through shared helper.
16. Add/repair all CRUU3 regression tests.
17. Harden WpfTestHost cleanup and close every test Window.
18. Extend Windows CI with publish-check, persistent TRX, and repeatable stress mode.
19. Add strict release-asset verifier without inventing the missing logo.
20. Update README + German guide.
21. Run full suite 5 times.
22. Publish self-contained win-x64.
23. Run the published EXE and manual data-root flows.
24. If SVG exists, generate/verify ICO and manually verify Explorer/taskbar/Alt+Tab.
25. Produce evidence report; do not overclaim deferred checks.
```

