# CRUU4 — Post-CRUU3 Paranoid Regression Audit & Weak-Model Repair Blueprint

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited commit:** `742a1d030569cb20bfbc74e477bc74c845eb5cab`  
**Previous repair authority:** `cruu1.md`, `cruu2.md`, `cruu3.md`  
**Purpose:** independently re-audit the implementation after the CRUU3 repair commit, identify residual defects and verification gaps that are not covered by the reported 240-test pass, and provide a deterministic implementation blueprint that a weak coding model can execute without making architecture or product decisions.

---

# 1. Executive result

The CRUU3 implementation **substantially landed correctly**. The source now contains the major safeguards requested by CRUU3: future-settings-schema handling, unreadable-settings handling, stronger migration hashing, disjoint-root validation, write-capability probes, existing-library confirmation, target lock probing, process shutdown after a root change, a self-contained `win-x64` publish step, a release-asset verifier, Unicode single-line validation, and the repaired WPF test host.

The supplied verification evidence is also internally plausible:

```text
Build:                    PASS as reported by user
Release tests:            240 / 240 as reported by user
Repeated local runs:      5 / 5 as reported by user
Self-contained publish:   PASS as reported by user
Git commit:               742a1d0... confirmed on main
```

However, a fresh source-level audit found **new residual issues** that were not represented by the CRUU3 finding matrix. Several are data-protection or migration-consistency defects rather than cosmetic gaps.

## 1.1 Audit verdict

```text
CRUU3 IMPLEMENTATION                    = SUBSTANTIALLY LANDED
USER-SUPPLIED 240-TEST EVIDENCE         = ACCEPTED AS REPORTED
INDEPENDENT DOTNET EXECUTION HERE       = NOT AVAILABLE
POST-CRUU3 SOURCE AUDIT                 = COMPLETED
NEW CRUU4 FINDINGS                      = OPEN
FINAL PRODUCTION-CLEAN ACCEPTANCE       = NOT YET GRANTED
STRICT RELEASE ACCEPTANCE               = BLOCKED BY REAL LOGO ASSET
MISSING_REQUIRED_ASSET                  = src/PromptHelper/Assets/PromptHelperLogo.svg
```

The test environment used for this audit does not provide a .NET SDK or a Windows desktop runtime, so the user's 240-test / five-run result could not be independently reproduced here. Do **not** reinterpret that limitation as a product defect. It only means the execution figures are external evidence while the findings below are based on direct inspection of commit `742a1d0`.

## 1.2 New finding count

This document defines **13 CRUU4 items**:

| ID | Severity | Summary |
|---|---|---|
| CRUU4-001 | HIGH | Valid current settings can overwrite a future-schema settings backup |
| CRUU4-002 | HIGH | Valid current library can overwrite a future-schema library backup; corrupt-primary/future-backup classification is wrong |
| CRUU4-003 | HIGH | Migration document and migration hash can come from different source versions |
| CRUU4-004 | HIGH | Target `library.json` bytes are not checked against the captured source snapshot |
| CRUU4-005 | MEDIUM-HIGH | Migration touches the target before the stable snapshot and before rollback protection begins |
| CRUU4-006 | MEDIUM-HIGH | Switching to an existing good library is blocked by unrelated damage in the current library |
| CRUU4-007 | HIGH | Backup-only targets are called recoverable without validating their referenced prompt bodies |
| CRUU4-008 | MEDIUM-HIGH | Persisted custom roots bypass the topology/volume/bootstrap policy during startup |
| CRUU4-009 | HIGH | Lexical path comparison can be bypassed by junctions/symlinks/physical aliases; root normalization is fragile |
| CRUU4-010 | MEDIUM-HIGH | Target-lock preflight remains TOCTOU and the migration/settings transition is not one transaction |
| CRUU4-011 | MEDIUM | Release asset verification exists but is not wired into the CI/release gate |
| CRUU4-012 | MEDIUM | Release ICO verification is weaker than the unit parser and does not prove an embedded EXE icon |
| CRUU4-013 | RELEASE BLOCKER | The authoritative SVG/ICO asset is still absent |

The release-asset item is intentionally carried forward. **Do not fabricate a logo to close CRUU4-013.**

---

# 2. Authority and locked product decisions

The implementation model must not reopen settled design decisions.

Preserve all of the following:

1. Prompt bodies stay as local Markdown files under `prompts/`.
2. `library.json` remains the structural primary metadata file.
3. `library.backup.json` remains the structural safety backup.
4. Settings remain at `%LOCALAPPDATA%\PromptHelper\settings.json` and `settings.backup.json`.
5. `PromptRecord.Title == null` continues to mean automatic headline mode.
6. Automatic headline continues to derive from the first non-empty prompt body line.
7. Editor wrapping remains visual-only.
8. Recent-copy state remains session-only, unique by prompt ID, newest-first, maximum three.
9. The three-column prompt grid remains.
10. Existing valid target libraries may be selected, but are never overwritten or merged.
11. Empty/new targets receive a copy of the current library while the old source remains untouched.
12. Data-root switching still requires a process boundary; do not hot-swap repositories.
13. A missing real logo is never permission to synthesize an approximation.
14. `LibraryDocument.CurrentSchemaVersion` remains `1`.
15. `AppSettings.CurrentSchemaVersion` remains `1`.
16. No cloud sync, telemetry, database, account system, or network service is introduced by CRUU4.
17. Valid primary metadata remains authoritative over an ordinary same-version backup.
18. A **future-schema backup is special**: an older build may ignore it for current operation, but must never overwrite/downgrade it.
19. A locked/unreadable backup must not prevent use of a valid current primary.
20. Unavailable prompt bodies may continue to appear as unavailable prompts; do not delete or silently regenerate them.

---

# 3. Files inspected in the fresh audit

The new findings are grounded in the current implementation at commit `742a1d0`, especially:

```text
src/PromptHelper/App.xaml.cs
src/PromptHelper/MainWindow.xaml.cs
src/PromptHelper/Models/AppSettings.cs
src/PromptHelper/Services/AppSettingsRepository.cs
src/PromptHelper/Services/AppInstanceLock.cs
src/PromptHelper/Services/DataFolderMigrationService.cs
src/PromptHelper/Services/DataRootBootstrapValidator.cs
src/PromptHelper/Services/DataRootCapabilityValidator.cs
src/PromptHelper/Services/DataRootTopologyValidator.cs
src/PromptHelper/Services/IApplicationLifetime.cs
src/PromptHelper/Services/IMigrationFileOps.cs
src/PromptHelper/Services/LibraryRepository.cs
src/PromptHelper/Services/LibraryStartupService.cs
src/PromptHelper/Services/LibraryValidator.cs
src/PromptHelper/Infrastructure/TextUtilities.cs
src/PromptHelper/Views/SettingsDialog.xaml.cs
src/PromptHelper/PromptHelper.csproj
tools/VerifyReleaseAssets.ps1
.github/workflows/windows-ci.yml

tests/PromptHelper.Tests/AppSettingsRepositoryTests.cs
tests/PromptHelper.Tests/DataFolderMigrationServiceTests.cs
tests/PromptHelper.Tests/DataRootTopologyValidatorTests.cs
tests/PromptHelper.Tests/FaultInjectingMigrationFileOps.cs
tests/PromptHelper.Tests/WpfTestHost.cs
```

The current repository tree still does **not** contain:

```text
src/PromptHelper/Assets/PromptHelperLogo.svg
src/PromptHelper/Assets/PromptHelper.ico
```

---

# 4. Required implementation order

Do **not** fix these findings in arbitrary order.

Use exactly this sequence:

```text
PHASE A  = shared physical-path identity + managed-root policy
PHASE B  = settings primary/backup authority matrix
PHASE C  = library primary/backup authority matrix
PHASE D  = migration source snapshot + target byte verification
PHASE E  = migration inspection order + backup-only validation
PHASE F  = target transition coordinator / reservation / rollback
PHASE G  = startup policy enforcement
PHASE H  = release-gate wiring and release binary verification
PHASE I  = documentation + full regression suite
PHASE J  = strict icon gate only after the real SVG arrives
```

Why this order matters:

- CRUU4-009 provides path identity used by CRUU4-008 and CRUU4-010.
- CRUU4-003/004/005 must be repaired before building an atomic transition coordinator.
- CRUU4-006/007 require a read-only target inspection stage before confirmation/mutation.
- Release workflow changes should be made only after product-side fixes are stable.

---

# 5. CRUU4-001 — Valid current settings can destroy a future-schema settings backup

**Severity:** HIGH  
**Area:** settings authority / downgrade safety

## 5.1 Current defect

`AppSettingsRepository.LoadOrRecover()` reads both primary and backup states, then handles a valid primary like this conceptually:

```text
primary = valid schema 1
→ serialize primary
→ write settings.backup.json
```

The current branch does not first preserve a backup whose state is `FutureSchema`.

Therefore this state is unsafe:

```text
settings.json         = schema 1, valid
settings.backup.json  = schema 2, valid for a newer Prompt Helper
old Prompt Helper starts
→ schema-1 primary wins, which is correct
→ but old Prompt Helper overwrites schema-2 backup, which is NOT correct
```

The future backup may be the only remaining recovery evidence from a newer application version.

This is a separate matrix entry from CRUU3-002. CRUU3 tested:

```text
future primary + old backup
```

but not:

```text
old valid primary + future backup
```

## 5.2 Required behavior

Use this exact matrix when primary is valid:

| Backup state | Start with valid primary? | Overwrite backup? | Warning? |
|---|---:|---:|---:|
| Missing | yes | yes, create current backup | no unless write fails |
| Current-schema valid | yes | yes, synchronize | no unless write fails |
| Corrupt | yes | yes, replace corrupt backup | optional/no |
| Future schema | yes | **NO** | **YES: newer backup preserved** |
| Unreadable/locked | yes | **NO attempt required** | **YES** |

A future or unreadable backup must not block a valid primary.

## 5.3 Exact implementation

Rewrite `LoadOrRecover()` so primary authority is decided before destructive backup synchronization.

Use this shape:

```csharp
public SettingsLoadResult LoadOrRecover()
{
    SettingsReadState primaryState = ReadState(_settingsPath);

    // Future primary: authoritative incompatibility. Do not even inspect backup.
    if (primaryState is SettingsReadState.FutureSchema futurePrimary)
    {
        throw new UnsupportedSettingsSchemaException(futurePrimary.Version);
    }

    // Temporarily unreadable primary: do not substitute stale backup.
    if (primaryState is SettingsReadState.Unreadable unreadablePrimary)
    {
        throw new SettingsReadException(_settingsPath, unreadablePrimary.Error);
    }

    if (primaryState is SettingsReadState.Valid validPrimary)
    {
        SettingsReadState backupState = ReadState(_backupPath);

        if (backupState is SettingsReadState.FutureSchema futureBackup)
        {
            return new SettingsLoadResult(
                validPrimary.Settings,
                RecoveredFromBackup: false,
                Warning:
                    $"Prompt Helper loaded settings.json, but settings.backup.json " +
                    $"was created by a newer settings schema ({futureBackup.Version}). " +
                    "The newer backup was preserved and was not overwritten.");
        }

        if (backupState is SettingsReadState.Unreadable unreadableBackup)
        {
            return new SettingsLoadResult(
                validPrimary.Settings,
                RecoveredFromBackup: false,
                Warning:
                    $"Prompt Helper loaded settings.json, but settings.backup.json " +
                    $"could not be inspected or synchronized: {unreadableBackup.Error.Message}");
        }

        string? warning = null;
        try
        {
            string? dir = Path.GetDirectoryName(_backupPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(validPrimary.Settings, JsonOptions);
            _writer.Write(_backupPath, json);
        }
        catch (Exception ex)
        {
            warning =
                "Settings loaded from settings.json, but settings.backup.json " +
                $"could not be synchronized: {ex.Message}";
        }

        return new SettingsLoadResult(validPrimary.Settings, false, warning);
    }

    // Backup is only needed after a missing/corrupt primary.
    SettingsReadState backupStateForRecovery = ReadState(_backupPath);

    if (primaryState is SettingsReadState.Missing &&
        backupStateForRecovery is SettingsReadState.Missing)
    {
        return new SettingsLoadResult(new AppSettings(), false, null);
    }

    if (backupStateForRecovery is SettingsReadState.FutureSchema futureBackupForRecovery)
    {
        throw new UnsupportedSettingsSchemaException(futureBackupForRecovery.Version);
    }

    if (backupStateForRecovery is SettingsReadState.Unreadable unreadableBackupForRecovery)
    {
        throw new SettingsReadException(_backupPath, unreadableBackupForRecovery.Error);
    }

    if (backupStateForRecovery is SettingsReadState.Valid validBackup)
    {
        string warning =
            "Prompt Helper recovered its data-folder setting from settings.backup.json.\r\n\r\n" +
            "The configured prompt library itself was not modified by this recovery.";

        try
        {
            string? dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(validBackup.Settings, JsonOptions);
            _writer.Write(_settingsPath, json);
        }
        catch (Exception ex)
        {
            warning =
                "Settings were recovered from settings.backup.json, but settings.json " +
                $"could not be restored: {ex.Message}";
        }

        return new SettingsLoadResult(validBackup.Settings, true, warning);
    }

    if (backupStateForRecovery is SettingsReadState.Corrupt corruptBackup)
    {
        throw new InvalidDataException(
            $"Settings file '{_backupPath}' is corrupt: {corruptBackup.Error.Message}",
            corruptBackup.Error);
    }

    if (primaryState is SettingsReadState.Corrupt corruptPrimary)
    {
        throw new InvalidDataException(
            $"Settings file '{_settingsPath}' is corrupt and no valid backup exists: " +
            corruptPrimary.Error.Message,
            corruptPrimary.Error);
    }

    throw new InvalidDataException(
        $"Failed to load settings from '{_settingsPath}'.");
}
```

## 5.4 Required tests

Add:

```csharp
[TestMethod]
public void CRUU4_001_Valid_primary_preserves_future_schema_backup()
{
    using var temp = new TestDirectory();

    string primary = Path.Combine(temp.Root, "settings.json");
    string backup = Path.Combine(temp.Root, "settings.backup.json");

    File.WriteAllText(
        primary,
        "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Current\"}");

    File.WriteAllText(
        backup,
        "{\"schemaVersion\":2,\"dataRootPath\":\"C:\\\\Newer\"}");

    byte[] backupBefore = File.ReadAllBytes(backup);

    var repo = new AppSettingsRepository(
        settingsPathOverride: primary,
        backupPathOverride: backup);

    SettingsLoadResult result = repo.LoadOrRecover();

    Assert.IsFalse(result.RecoveredFromBackup);
    Assert.AreEqual(Path.GetFullPath(@"C:\Current"), result.Settings.DataRootPath);
    Assert.IsNotNull(result.Warning);
    StringAssert.Contains(result.Warning, "newer");
    CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(backup));
}
```

Also add:

```csharp
[TestMethod]
public void CRUU4_001_Valid_primary_does_not_attempt_to_replace_unreadable_backup()
```

Acceptance:

```text
valid schema-1 primary stays usable
future-schema backup bytes remain exactly unchanged
future backup does not block startup
warning is surfaced
```

---

# 6. CRUU4-002 — Valid library primary can destroy a future-schema library backup

**Severity:** HIGH  
**Area:** library startup / future-version recovery safety

## 6.1 Current defect

`LibraryStartupService` correctly gives valid `library.json` primary authority, but then blindly calls:

```csharp
_libraryRepo.SynchronizeBackup(primaryValid.Document);
```

That call serializes the current schema-1 document and writes `library.backup.json`.

This destroys a newer-schema backup if the user downgraded the executable while an older schema-1 primary still exists.

There is a second related classification bug:

```text
primary = corrupt
backup  = future schema
```

currently falls through the corrupt-primary branch and produces a generic “no valid backup” error rather than the explicit unsupported-newer-schema result.

## 6.2 Locked behavior to preserve

Do **not** regress the older rule:

```text
valid current primary + locked/unreadable backup
→ primary MUST still start
→ backup failure becomes a warning
```

Future-backup preservation must be added without making valid-primary startup depend on backup availability.

## 6.3 Required metadata state

Extend `MetadataReadResult`:

```csharp
private abstract record MetadataReadResult
{
    public sealed record Valid(LibraryDocument Document) : MetadataReadResult;
    public sealed record Corrupt(string RawContent) : MetadataReadResult;
    public sealed record Missing : MetadataReadResult;
    public sealed record FutureSchema(int Version) : MetadataReadResult;
    public sealed record Unreadable(Exception Error) : MetadataReadResult;
}
```

Update the reader:

```csharp
private static MetadataReadResult ReadMetadataState(string path)
{
    string raw;

    try
    {
        raw = File.ReadAllText(path);
    }
    catch (FileNotFoundException)
    {
        return new MetadataReadResult.Missing();
    }
    catch (DirectoryNotFoundException)
    {
        return new MetadataReadResult.Missing();
    }
    catch (Exception ex) when (
        ex is IOException or
        UnauthorizedAccessException or
        System.Security.SecurityException)
    {
        return new MetadataReadResult.Unreadable(ex);
    }

    try
    {
        LibraryDocument doc = LibraryRepository.InspectAndDeserialize(raw);
        return new MetadataReadResult.Valid(doc);
    }
    catch (UnsupportedLibrarySchemaException ex)
    {
        return new MetadataReadResult.FutureSchema(ex.SchemaVersion);
    }
    catch (Exception ex) when (ex is JsonException or InvalidDataException)
    {
        return new MetadataReadResult.Corrupt(raw);
    }
}
```

## 6.4 Valid-primary branch

Replace the blind synchronize call with:

```csharp
if (primaryResult is MetadataReadResult.Valid primaryValid)
{
    string? backupWarning = null;
    MetadataReadResult backupState = ReadMetadataState(_paths.LibraryBackupPath);

    if (backupState is MetadataReadResult.FutureSchema futureBackup)
    {
        backupWarning =
            $"The current library.json was loaded, but library.backup.json uses " +
            $"newer schema version {futureBackup.Version}. " +
            "The newer backup was preserved and was not overwritten.";
    }
    else if (backupState is MetadataReadResult.Unreadable unreadableBackup)
    {
        backupWarning =
            "The current library.json was loaded, but its safety backup could not " +
            $"be inspected or synchronized: {unreadableBackup.Error.Message}";
    }
    else
    {
        try
        {
            _libraryRepo.SynchronizeBackup(primaryValid.Document);
        }
        catch (Exception)
        {
            backupWarning =
                "The library was loaded from library.json, but its safety backup " +
                "could not be synchronized.";
        }
    }

    TryRemoveStaleMarker();
    return new StartupResult(primaryValid.Document, false, backupWarning);
}
```

## 6.5 Recovery branch rule

Immediately after backup inspection for a non-valid primary:

```csharp
if (backupResult is MetadataReadResult.FutureSchema backupFuture)
{
    throw new UnsupportedLibrarySchemaException(backupFuture.Version);
}

if (backupResult is MetadataReadResult.Unreadable unreadableBackup)
{
    throw new IOException(
        $"The library backup could not be read: {unreadableBackup.Error.Message}",
        unreadableBackup.Error);
}
```

Do this **before** deciding whether the primary was corrupt or missing.

## 6.6 Tests

Add:

```csharp
[TestMethod]
public void CRUU4_002_Valid_primary_preserves_future_schema_library_backup()
```

Arrange:

```text
library.json        valid schema 1
library.backup.json schema 99
```

Assert:

```text
startup succeeds from primary
backup bytes unchanged
warning mentions newer schema and preservation
```

Add:

```csharp
[TestMethod]
public void CRUU4_002_Corrupt_primary_future_backup_throws_unsupported_schema()
```

Assert `UnsupportedLibrarySchemaException.SchemaVersion == 99`.

Add:

```csharp
[TestMethod]
public void CRUU4_002_Valid_primary_locked_backup_still_starts()
```

This protects the older valid-primary authority rule.

---

# 7. CRUU4-003 — Migration metadata document and hash are not one coherent snapshot

**Severity:** HIGH  
**Area:** migration consistency

## 7.1 Current defect

Current flow is effectively:

```text
ValidateLibraryRoot(current)
  → reads and parses library.json into sourceDoc
  → checks referenced prompt bodies

later...

CopyAndValidateLibrary(current, target, sourceDoc)
  → reads library.json bytes again
  → hashes those later bytes
  → hashes prompt IDs from earlier sourceDoc
```

Therefore this race is possible:

```text
T0  library.json = A
T1  ValidateLibraryRoot parses A into sourceDoc
T2  library.json changes to B
T3  initial library hash captures B
T4  prompt hash list is still based on A
T5  final library is still B
T6  library hash comparison passes
```

The document deciding which prompt bodies belong to the snapshot and the bytes defining the metadata hash are not guaranteed to be the same version.

## 7.2 Required rule

A migration snapshot must be constructed from exactly one `library.json` byte sequence:

```text
read exact library bytes once
→ hash those exact bytes
→ parse those exact bytes
→ derive referenced prompt IDs from that parsed document
→ read/hash those prompt bodies
```

Do not parse the source metadata in one method and hash a later read in another method.

## 7.3 New snapshot record

Use:

```csharp
internal sealed record MigrationSnapshot(
    byte[] LibraryBytes,
    byte[] LibraryHash,
    LibraryDocument Document,
    IReadOnlyDictionary<Guid, byte[]> PromptHashes);
```

## 7.4 Copy-ready snapshot capture

Add:

```csharp
private MigrationSnapshot CaptureSourceSnapshot(string currentRoot)
{
    if (!Directory.Exists(currentRoot))
    {
        throw new DirectoryNotFoundException(
            $"Library directory does not exist: '{currentRoot}'");
    }

    string libraryPath = Path.Combine(currentRoot, "library.json");
    if (!File.Exists(libraryPath))
    {
        throw new InvalidDataException(
            $"Library directory does not contain library.json: '{currentRoot}'");
    }

    byte[] libraryBytes = _fileOps.ReadAllBytes(libraryPath);
    byte[] libraryHash = SHA256.HashData(libraryBytes);

    string libraryJson = DecodeUtf8Text(libraryBytes);

    LibraryDocument document;
    try
    {
        document = LibraryRepository.InspectAndDeserialize(libraryJson);
        LibraryValidator.Validate(document);
    }
    catch (Exception ex) when (
        ex is System.Text.Json.JsonException or
        InvalidDataException or
        ArgumentException)
    {
        throw new InvalidDataException(
            $"Source library metadata at '{libraryPath}' is invalid: {ex.Message}",
            ex);
    }

    string promptsDir = Path.Combine(currentRoot, "prompts");
    var promptHashes = new Dictionary<Guid, byte[]>();

    foreach (PromptRecord prompt in document.Prompts)
    {
        string promptPath = Path.Combine(
            promptsDir,
            $"{prompt.Id:N}.md");

        if (!File.Exists(promptPath))
        {
            throw new InvalidDataException(
                $"Library references prompt file '{prompt.Id:N}.md' " +
                $"which does not exist in '{promptsDir}'.");
        }

        byte[] promptBytes;
        try
        {
            promptBytes = _fileOps.ReadAllBytes(promptPath);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"Prompt file '{promptPath}' cannot be read: {ex.Message}",
                ex);
        }

        promptHashes.Add(
            prompt.Id,
            SHA256.HashData(promptBytes));
    }

    return new MigrationSnapshot(
        LibraryBytes: libraryBytes,
        LibraryHash: libraryHash,
        Document: document,
        PromptHashes: promptHashes);
}

private static string DecodeUtf8Text(byte[] bytes)
{
    using var stream = new MemoryStream(bytes, writable: false);
    using var reader = new StreamReader(
        stream,
        new System.Text.UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true),
        detectEncodingFromByteOrderMarks: true);

    return reader.ReadToEnd();
}
```

## 7.5 Remove the old split authority

For the empty-target copy path:

```text
DELETE:
LibraryDocument sourceDoc = ValidateLibraryRoot(...)

REPLACE WITH:
MigrationSnapshot snapshot = CaptureSourceSnapshot(...)
```

Then pass `snapshot` to the copy method.

The copy method must use:

```csharp
snapshot.Document
snapshot.LibraryHash
snapshot.PromptHashes
```

from the same snapshot object.

## 7.6 Required tests

Add a focused snapshot-builder test:

```csharp
[TestMethod]
public void CRUU4_003_Snapshot_document_is_parsed_from_same_bytes_that_are_hashed()
```

Make `IMigrationFileOps.ReadAllBytes(libraryPath)` return a known metadata payload and assert:

```text
snapshot.Document matches payload
snapshot.LibraryHash == SHA256(payload)
prompt IDs hashed == prompt IDs in payload
```

Also retain the CRUU3 prompt-body mutation test.

---

# 8. CRUU4-004 — Target `library.json` bytes are not checked against snapshot

**Severity:** HIGH  
**Area:** migration byte integrity

## 8.1 Current defect

Current migration verifies:

```text
final source library hash == initial source library hash
source prompt hashes unchanged
target prompt hashes == source snapshot prompt hashes
target library structurally parses
```

It does **not** verify:

```text
SHA256(target/library.json) == snapshot.LibraryHash
```

Structural validation is weaker than byte-snapshot validation.

A transient source mutation during `File.Copy`, a faulty file operation, or another external writer can produce a different but still-valid target metadata file.

## 8.2 Required fix

After copy and before accepting the target:

```csharp
string targetLibraryPath = Path.Combine(targetRoot, "library.json");

byte[] targetLibraryHash = SHA256.HashData(
    _fileOps.ReadAllBytes(targetLibraryPath));

if (!snapshot.LibraryHash.AsSpan().SequenceEqual(targetLibraryHash))
{
    throw new IOException(
        "Target library.json does not match the captured source snapshot.");
}
```

Also verify final source metadata:

```csharp
byte[] finalSourceLibraryHash = SHA256.HashData(
    _fileOps.ReadAllBytes(
        Path.Combine(currentRoot, "library.json")));

if (!snapshot.LibraryHash.AsSpan().SequenceEqual(finalSourceLibraryHash))
{
    throw new IOException(
        "Source library metadata changed during migration. Retry after it is stable.");
}
```

## 8.3 Deterministic regression test that must fail on old code

The current `FaultInjectingMigrationFileOps` already provides an `OnCopyFile` hook.

Add:

```csharp
[TestMethod]
public void CRUU4_004_Altered_but_valid_target_library_bytes_abort_and_rollback()
{
    using var source = new TestDirectory();
    using var targetParent = new TestDirectory();

    SeedValidLibrary(source.Root, out Guid promptId);

    string target = Path.Combine(targetParent.Root, "Target");

    var ops = new FaultInjectingMigrationFileOps();

    ops.OnCopyFile = (src, dst, overwrite) =>
    {
        if (Path.GetFileName(src)
            .Equals("library.json", StringComparison.OrdinalIgnoreCase))
        {
            string json = File.ReadAllText(src);

            // Keep metadata valid but change bytes.
            json = json.Replace(
                "\"schemaVersion\": 1",
                "\"schemaVersion\": 1   ");

            File.WriteAllText(dst, json);
            return;
        }

        File.Copy(src, dst, overwrite);
    };

    var service = new DataFolderMigrationService(fileOps: ops);

    Assert.Throws<IOException>(() =>
        service.PrepareTarget(source.Root, target));

    Assert.IsFalse(File.Exists(
        Path.Combine(target, "library.json")));
}
```

If whitespace replacement is not guaranteed to match serializer output, deserialize/re-serialize with a harmless indentation variation instead.

Acceptance:

```text
target metadata bytes must match captured metadata snapshot bytes by SHA-256
```

---

# 9. CRUU4-005 — Target mutation begins before snapshot and before rollback protection

**Severity:** MEDIUM-HIGH  
**Area:** migration rollback hygiene

## 9.1 Current defect

For an empty/nonexistent target, current code can:

```text
create target root
create target/prompts
create target/recovery
THEN start reading the initial source snapshot
THEN enter the protected copy try/catch
```

If initial snapshot reading fails, the target can be left with directories even though the migration failed.

CRUU3's intent was stronger:

```text
capture source snapshot before touching target
```

## 9.2 Required ordering

For an empty target use exactly:

```text
1. normalize and validate path identity
2. read-only inspect target
3. capture complete source snapshot
4. only now create/modify target
5. all target mutation occurs inside one rollback owner
6. verify
7. commit target transaction
```

## 9.3 Transaction helper

Add:

```csharp
internal sealed class MigrationTargetTransaction : IDisposable
{
    private readonly List<string> _createdFiles = [];
    private readonly List<string> _createdDirectories = [];
    private bool _committed;

    public void TrackCreatedFile(string path)
        => _createdFiles.Add(path);

    public void TrackCreatedDirectory(string path)
        => _createdDirectories.Add(path);

    public void Commit()
        => _committed = true;

    public void Dispose()
    {
        if (_committed)
        {
            return;
        }

        foreach (string file in _createdFiles.AsEnumerable().Reverse())
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // best effort; original failure remains authoritative
            }
        }

        foreach (string dir in _createdDirectories
            .OrderByDescending(x => x.Length))
        {
            try
            {
                if (Directory.Exists(dir) &&
                    !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
            catch
            {
                // best effort
            }
        }
    }
}
```

## 9.4 Mutation helper

Use:

```csharp
private static void EnsureDirectoryTracked(
    string path,
    MigrationTargetTransaction tx)
{
    if (!Directory.Exists(path))
    {
        Directory.CreateDirectory(path);
        tx.TrackCreatedDirectory(path);
    }
}
```

And:

```csharp
private void CopyFileNoOverwrite(
    string sourcePath,
    string destPath,
    MigrationTargetTransaction tx)
{
    if (File.Exists(destPath))
    {
        throw new IOException(
            $"Target file collision: '{destPath}' already exists.");
    }

    _fileOps.CopyFile(sourcePath, destPath, overwrite: false);
    tx.TrackCreatedFile(destPath);
}
```

## 9.5 Regression test

```csharp
[TestMethod]
public void CRUU4_005_Snapshot_read_failure_leaves_nonexistent_target_nonexistent()
{
    using var source = new TestDirectory();
    using var parent = new TestDirectory();

    SeedValidLibrary(source.Root, out _);

    string target = Path.Combine(parent.Root, "NewTarget");

    var ops = new FaultInjectingMigrationFileOps
    {
        OnReadAllBytes = path =>
        {
            if (Path.GetFileName(path)
                .Equals("library.json", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Injected source snapshot failure");
            }

            return File.ReadAllBytes(path);
        }
    };

    var migration = new DataFolderMigrationService(fileOps: ops);

    Assert.Throws<IOException>(() =>
        migration.PrepareTarget(source.Root, target));

    Assert.IsFalse(
        Directory.Exists(target),
        "A source snapshot failure must not leave a target directory behind.");
}
```

---

# 10. CRUU4-006 — Existing-library switch unnecessarily depends on current-library prompt health

**Severity:** MEDIUM-HIGH  
**Area:** recovery usability / target switching

## 10.1 Current defect

`PrepareTarget()` currently validates the current source library **before** it classifies the target.

Source validation requires every referenced prompt body to exist/read successfully.

That makes sense for:

```text
empty target → copy current library
```

because an incomplete current library cannot be copied as a complete snapshot.

It does **not** make sense for:

```text
existing target → no copy / no merge
```

In the existing-target case, the current source is not transferred at all.

Prompt Helper intentionally tolerates unavailable prompt bodies in its normal UI. A user with one missing current prompt should still be allowed to switch to a separate healthy existing library.

## 10.2 Required flow

Reorder:

```text
normalize
→ path policy
→ read-only target inspection

IF target is existing valid library:
    validate target
    return ExistingLibraryFound
    DO NOT require complete current source prompt bodies

IF target is empty:
    capture and validate complete source snapshot
    copy snapshot
```

## 10.3 Required test

```csharp
[TestMethod]
public void CRUU4_006_Damaged_current_prompt_does_not_block_switch_to_existing_good_library()
{
    using var current = new TestDirectory();
    using var target = new TestDirectory();

    SeedValidLibrary(current.Root, out Guid currentPrompt);
    SeedValidLibrary(target.Root, out _);

    File.Delete(Path.Combine(
        current.Root,
        "prompts",
        $"{currentPrompt:N}.md"));

    var migration = new DataFolderMigrationService();

    DataFolderChangeResult result =
        migration.PrepareTarget(current.Root, target.Root);

    Assert.IsTrue(result.ExistingLibraryFound);
    Assert.IsFalse(result.Copied);
}
```

Also add the inverse:

```csharp
[TestMethod]
public void CRUU4_006_Damaged_current_prompt_still_blocks_copy_to_empty_target()
```

That test must fail migration.

---

# 11. CRUU4-007 — Backup-only target is not proven recoverable

**Severity:** HIGH  
**Area:** existing-library target validation

## 11.1 Current defect

For an existing normal primary target, the service validates that every prompt referenced by its metadata exists and can be opened.

For a backup-only target:

```text
library.json          absent
library.backup.json   valid metadata
```

the current classifier validates only the backup metadata structure, then returns:

```text
ExistingLibraryFound = true
"recoverable safety backup"
```

It does not verify referenced prompt body files.

A backup-only target can therefore be called recoverable even though its backup points at missing/unreadable prompt files.

## 11.2 Required helper

Create one shared prompt-body validator:

```csharp
private static void ValidateDocumentPromptBodies(
    string root,
    LibraryDocument document,
    string metadataDescription)
{
    string promptsDir = Path.Combine(root, "prompts");

    foreach (PromptRecord prompt in document.Prompts)
    {
        string promptPath = Path.Combine(
            promptsDir,
            $"{prompt.Id:N}.md");

        if (!File.Exists(promptPath))
        {
            throw new InvalidDataException(
                $"{metadataDescription} references prompt file " +
                $"'{prompt.Id:N}.md', but it is missing from '{promptsDir}'.");
        }

        try
        {
            using FileStream stream = new(
                promptPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (!stream.CanRead)
            {
                throw new IOException("File stream is not readable.");
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            throw new InvalidDataException(
                $"{metadataDescription} references unreadable prompt file " +
                $"'{promptPath}': {ex.Message}",
                ex);
        }
    }
}
```

## 11.3 Target inspection must retain document

Refactor classifier output from only an enum to:

```csharp
internal sealed record TargetInspection(
    string NormalizedRoot,
    TargetLibraryKind Kind,
    LibraryDocument? EffectiveDocument,
    string? EffectiveMetadataPath,
    string? Warning,
    Exception? Error);
```

For `RecoverableBackupOnly`, store the parsed backup document in `EffectiveDocument`.

Before returning it as selectable:

```csharp
ValidateDocumentPromptBodies(
    inspection.NormalizedRoot,
    inspection.EffectiveDocument!,
    "library.backup.json");
```

## 11.4 Regression test

```csharp
[TestMethod]
public void CRUU4_007_Backup_only_target_with_missing_prompt_is_not_recoverable()
{
    using var source = new TestDirectory();
    using var target = new TestDirectory();

    SeedValidLibrary(source.Root, out _);

    Guid missingPrompt = Guid.NewGuid();

    File.WriteAllText(
        Path.Combine(target.Root, "library.backup.json"),
        $$"""
        {
          "schemaVersion": 1,
          "categories": [],
          "prompts": [
            {
              "id": "{{missingPrompt}}",
              "categoryId": null,
              "sortOrder": 10,
              "title": "Missing"
            }
          ]
        }
        """);

    var migration = new DataFolderMigrationService();

    Assert.Throws<InvalidDataException>(() =>
        migration.PrepareTarget(source.Root, target.Root));
}
```

Keep the existing backup-only empty-prompt-list test too.

---

# 12. CRUU4-008 — Persisted custom roots bypass the managed-root policy on startup

**Severity:** MEDIUM-HIGH  
**Area:** startup safety invariant

## 12.1 Current defect

The selection flow uses `DataRootTopologyValidator`.

But startup consumes a persisted custom root using approximately:

```text
settings path is absolute
→ DataRootBootstrapValidator:
     directory exists
     library.json or backup exists
→ AppPaths
→ EnsureRootDirectory
→ acquire lock
```

Startup does **not** reapply:

```text
not a volume root
not bootstrap ancestor
not bootstrap descendant
physical identity safety
```

Therefore manually edited settings, settings restored from an old version, or settings recovered from disk can bypass rules the UI enforces.

Safety invariants must be enforced at the point where persisted data is consumed, not only at the UI that writes it.

## 12.2 Required design

Introduce:

```text
IDataRootIdentityResolver
ManagedDataRootPolicy
```

`ManagedDataRootPolicy` must be called:

- from Settings/transition validation;
- from `App.OnStartup()` before any root directory creation or lock acquisition.

## 12.3 Copy-ready policy

After CRUU4-009 introduces physical resolution:

```csharp
public sealed class ManagedDataRootPolicy
{
    private readonly IPhysicalPathResolver _resolver;

    public ManagedDataRootPolicy(IPhysicalPathResolver resolver)
    {
        _resolver = resolver;
    }

    public string ValidateConfiguredRootForStartup(
        string configuredRoot,
        string bootstrapRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapRoot);

        string lexical = Path.GetFullPath(configuredRoot);

        if (DataRootTopologyValidator.IsVolumeRootSafe(lexical))
        {
            throw new InvalidDataException(
                "A drive or volume root cannot be used as the Prompt Helper data folder.");
        }

        string physicalTarget =
            _resolver.ResolveWithNearestExistingAncestor(lexical);

        string physicalBootstrap =
            _resolver.ResolveWithNearestExistingAncestor(
                Path.GetFullPath(bootstrapRoot));

        if (!PathIdentity.Equals(physicalTarget, physicalBootstrap) &&
            (PathIdentity.IsStrictDescendant(
                 physicalTarget,
                 physicalBootstrap) ||
             PathIdentity.IsStrictDescendant(
                 physicalBootstrap,
                 physicalTarget)))
        {
            throw new InvalidDataException(
                "The configured data folder overlaps the Prompt Helper bootstrap settings folder.");
        }

        return physicalTarget;
    }
}
```

## 12.4 Startup patch

Before:

```csharp
DataRootBootstrapValidator.ValidateConfiguredRoot(effectiveDataRoot);
```

do:

```csharp
string bootstrapRoot = Path.Combine(
    Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData),
    "PromptHelper");

var physicalResolver = new WindowsPhysicalPathResolver();
var rootPolicy = new ManagedDataRootPolicy(physicalResolver);

effectiveDataRoot =
    rootPolicy.ValidateConfiguredRootForStartup(
        effectiveDataRoot,
        bootstrapRoot);

DataRootBootstrapValidator.ValidateConfiguredRoot(effectiveDataRoot);
```

Do this **before**:

```csharp
new AppPaths(effectiveDataRoot)
paths.EnsureRootDirectory()
AppInstanceLock.TryAcquire(...)
```

## 12.5 Tests

Do not try to instantiate the full WPF app just to test this rule.

Test the policy directly:

```csharp
[TestMethod]
public void CRUU4_008_Persisted_volume_root_is_rejected_before_bootstrap()
```

```csharp
[TestMethod]
public void CRUU4_008_Persisted_bootstrap_parent_is_rejected()
```

```csharp
[TestMethod]
public void CRUU4_008_Exact_bootstrap_root_is_allowed()
```

Use an injectable fake physical resolver for deterministic unit tests.

---

# 13. CRUU4-009 — Lexical path comparison is not physical path identity

**Severity:** HIGH  
**Area:** Windows filesystem safety

## 13.1 Current defect

Current topology validation is based on:

```csharp
Path.GetFullPath(...)
StringComparison.OrdinalIgnoreCase
StartsWith(parent + separator)
```

This normalizes `.` and `..`, but it does not resolve NTFS reparse-point identity.

These can refer to the same physical location with different lexical strings:

```text
junction
directory symbolic link
mapped/alias path
8.3 short-name alias where enabled
```

Example:

```text
C:\Data\Current
C:\AliasToCurrent
```

If `AliasToCurrent` is a junction to `C:\Data\Current`, lexical comparison can say “different sibling” while both roots are physically identical.

That can bypass:

- same-root no-op detection;
- current/target containment protection;
- bootstrap overlap protection;
- target lock identity.

There is also a fragile root-normalization pattern:

```csharp
Path.GetFullPath(path)
    .TrimEnd('\\', '/')
```

For a drive root this can produce the drive-qualified form `C:` rather than preserving `C:\`. Never use a generic `TrimEnd` routine as the canonical representation of a filesystem root.

## 13.2 Required architecture

Add:

```text
IPhysicalPathResolver
WindowsPhysicalPathResolver
PathIdentity
```

The application is Windows-only, so using Win32 final-path resolution is appropriate.

## 13.3 Copy-ready interface

```csharp
public interface IPhysicalPathResolver
{
    string ResolveWithNearestExistingAncestor(string path);
}
```

## 13.4 Copy-ready Windows resolver

Create `Services/WindowsPhysicalPathResolver.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PromptHelper.Services;

public sealed class WindowsPhysicalPathResolver : IPhysicalPathResolver
{
    private const uint FileFlagBackupSemantics = 0x02000000;

    public string ResolveWithNearestExistingAncestor(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string full = Path.GetFullPath(path);

        if (Directory.Exists(full))
        {
            return ResolveExistingDirectory(full);
        }

        var remainder = new Stack<string>();
        DirectoryInfo? current = new(full);

        while (current is not null && !current.Exists)
        {
            remainder.Push(current.Name);
            current = current.Parent;
        }

        if (current is null)
        {
            throw new DirectoryNotFoundException(
                $"Could not find an existing ancestor for '{full}'.");
        }

        string resolved = ResolveExistingDirectory(current.FullName);

        while (remainder.Count > 0)
        {
            resolved = Path.Combine(resolved, remainder.Pop());
        }

        return Path.GetFullPath(resolved);
    }

    private static string ResolveExistingDirectory(string directory)
    {
        using SafeFileHandle handle = CreateFileW(
            directory,
            desiredAccess: 0,
            shareMode:
                FileShare.Read |
                FileShare.Write |
                FileShare.Delete,
            securityAttributes: IntPtr.Zero,
            creationDisposition: FileMode.Open,
            flagsAndAttributes: FileFlagBackupSemantics,
            templateFile: IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Could not resolve physical directory path '{directory}'.");
        }

        var buffer = new StringBuilder(1024);

        uint length = GetFinalPathNameByHandleW(
            handle,
            buffer,
            (uint)buffer.Capacity,
            0);

        if (length == 0)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Could not resolve final path for '{directory}'.");
        }

        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder(checked((int)length + 1));

            length = GetFinalPathNameByHandleW(
                handle,
                buffer,
                (uint)buffer.Capacity,
                0);

            if (length == 0)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"Could not resolve final path for '{directory}'.");
            }
        }

        return StripExtendedPrefix(buffer.ToString());
    }

    private static string StripExtendedPrefix(string path)
    {
        const string uncPrefix = @"\\?\UNC\";
        const string normalPrefix = @"\\?\";

        if (path.StartsWith(
            uncPrefix,
            StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[uncPrefix.Length..];
        }

        if (path.StartsWith(
            normalPrefix,
            StringComparison.OrdinalIgnoreCase))
        {
            return path[normalPrefix.Length..];
        }

        return path;
    }

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        FileMode creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle file,
        StringBuilder filePath,
        uint filePathLength,
        uint flags);
}
```

## 13.5 Root-safe identity helper

Create:

```csharp
public static class PathIdentity
{
    public static bool Equals(string left, string right)
        => string.Equals(
            NormalizeForComparison(left),
            NormalizeForComparison(right),
            StringComparison.OrdinalIgnoreCase);

    public static bool IsStrictDescendant(
        string candidate,
        string parent)
    {
        string child = NormalizeForComparison(candidate);
        string ancestor = NormalizeForComparison(parent);

        if (Equals(child, ancestor))
        {
            return false;
        }

        string prefix = EnsureTrailingSeparator(ancestor);

        return child.StartsWith(
            prefix,
            StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeForComparison(string path)
    {
        string full = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(full);

        if (root is not null &&
            string.Equals(
                full,
                root,
                StringComparison.OrdinalIgnoreCase))
        {
            // Preserve C:\ and UNC share-root syntax.
            return root;
        }

        return full.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) ||
            path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}
```

## 13.6 Update topology validator

It must compare both:

```text
lexical safety
physical identity safety
```

Physical identity is authoritative where paths/ancestors exist.

## 13.7 Testing strategy

Do not make normal CI depend only on Developer Mode symbolic-link privileges.

Use two layers.

### Deterministic unit test

Inject a fake resolver:

```text
C:\Alias    -> C:\Real
C:\Real     -> C:\Real
```

Assert target/current are treated as same physical root.

### Windows integration/manual junction test

On a Windows NTFS machine:

```powershell
$root = Join-Path $env:TEMP "PromptHelper-junction-test"
$real = Join-Path $root "Real"
$alias = Join-Path $root "Alias"

New-Item -ItemType Directory -Force $real | Out-Null
cmd /c "mklink /J `"$alias`" `"$real`""

# Resolver must report Alias and Real as the same physical directory.
```

Do not silently skip the deterministic fake-resolver unit tests.

---

# 14. CRUU4-010 — Root transition is still vulnerable to TOCTOU and cross-service partial completion

**Severity:** MEDIUM-HIGH  
**Area:** settings/migration transaction

## 14.1 Current defect A — lock preflight is point-in-time only

Current UI does:

```text
IsExistingLockHeld(target)
→ migration work
→ optional confirmation
→ settings save
```

Another Prompt Helper process can acquire the target after the preflight but before the transition is committed.

The preflight is useful, but it is not a reservation.

## 14.2 Current defect B — target copy can succeed and settings save can fail

For an empty target:

```text
copy current library to target succeeds
then
settings.json write fails
```

The UI reports configuration failure, but the copied target may remain.

This does not lose source data, but it leaves a partial transition artifact. A retry now sees an existing library rather than the original empty target, changing the user flow.

## 14.3 Required solution

Introduce one coordinator that owns:

```text
inspect
confirmation
source snapshot
target reservation
revalidation
migration
settings commit
rollback
```

Do not let `SettingsDialog` manually compose the transaction from independent services.

Recommended name:

```text
DataFolderTransitionCoordinator
```

## 14.4 Required two-path state machine

### Existing target

```text
1. inspect target read-only
2. validate target prompt bodies
3. ask user confirmation
4. acquire target reservation lock
5. re-inspect/revalidate target under reservation
6. capability probe
7. save settings
8. release reservation
9. request process shutdown
```

No current-source full-body validation is required.

### Empty target

```text
1. inspect target read-only
2. capture full current-source snapshot
3. acquire target reservation
4. verify target is still empty under reservation
5. create/copy target under rollback owner
6. verify target snapshot
7. save settings
8. COMMIT target rollback owner
9. release reservation
10. request process shutdown
```

If settings save fails at step 7, rollback the new target files created by this transition.

## 14.5 Reservation wrapper

Create:

```csharp
public sealed class TargetRootReservation : IDisposable
{
    private readonly AppInstanceLock _lock;
    private readonly string _lockPath;
    private readonly bool _deleteLockFileOnDispose;
    private bool _disposed;

    private TargetRootReservation(
        AppInstanceLock @lock,
        string lockPath,
        bool deleteLockFileOnDispose)
    {
        _lock = @lock;
        _lockPath = lockPath;
        _deleteLockFileOnDispose = deleteLockFileOnDispose;
    }

    public static TargetRootReservation? TryAcquire(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        Directory.CreateDirectory(root);

        string lockPath = Path.Combine(root, ".app.lock");
        bool existedBefore = File.Exists(lockPath);

        AppInstanceLock? @lock =
            AppInstanceLock.TryAcquire(lockPath);

        if (@lock is null)
        {
            return null;
        }

        return new TargetRootReservation(
            @lock,
            lockPath,
            deleteLockFileOnDispose: !existedBefore);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lock.Dispose();

        if (_deleteLockFileOnDispose)
        {
            try
            {
                File.Delete(_lockPath);
            }
            catch
            {
                // Stale unlocked lock files are safe.
            }
        }
    }
}
```

## 14.6 Coordinator result

Use a non-WPF record:

```csharp
public sealed record DataFolderTransitionResult(
    bool Changed,
    bool RestartRequired,
    bool ExistingLibrarySelected,
    string NormalizedTargetRoot,
    string? Warning);
```

## 14.7 Important ownership rule

The coordinator must not display message boxes.

It may accept:

```csharp
Func<TargetInspection, bool> confirmExistingLibrary
```

or an injected `IUserConfirmationService`.

The UI is responsible only for:

```text
read selected text
call coordinator
show result/warning
if RestartRequired: close dialog
```

`MainWindow` keeps the existing shutdown behavior.

## 14.8 Regression tests

Add:

```csharp
[TestMethod]
public void CRUU4_010_New_target_is_rolled_back_if_settings_primary_write_fails()
```

Inject a writer that fails on `settings.json`.

Assert:

```text
old setting unchanged
source untouched
new target library.json absent
new target prompt files absent
```

Add:

```csharp
[TestMethod]
public void CRUU4_010_Target_state_change_after_inspection_is_detected_under_reservation()
```

Add:

```csharp
[TestMethod]
public void CRUU4_010_Existing_target_confirmation_cancel_writes_no_target_probe_files()
```

This last test also improves UX: confirmation should occur before any write-capability probe on an existing library.

---

# 15. CRUU4-011 — Release asset verifier is not actually part of the workflow gate

**Severity:** MEDIUM  
**Area:** CI/release process

## 15.1 Current state

The repository now has:

```text
tools/VerifyReleaseAssets.ps1
```

with:

```text
-RequireIcon
```

but `.github/workflows/windows-ci.yml` does not invoke that script.

The workflow currently proves:

```text
build
tests
self-contained publish command completed
PromptHelper.exe exists
LICENSE exists
THIRD_PARTY_NOTICES.md exists
```

It does not prove the release asset policy.

## 15.2 Required CI behavior

Standard development CI:

```text
VerifyReleaseAssets.ps1
```

must run in non-strict mode. That means:

- missing SVG remains an acknowledged development dependency;
- but if an ICO is present and malformed, CI fails.

Strict release validation:

```text
VerifyReleaseAssets.ps1 -RequireIcon
```

must run before a build is called release-ready.

## 15.3 Copy-ready workflow patch

Extend `workflow_dispatch`:

```yaml
workflow_dispatch:
  inputs:
    stress:
      description: 'Run full test suite five times'
      required: false
      default: false
      type: boolean
    release_gate:
      description: 'Require all final release assets, including the real app icon'
      required: false
      default: false
      type: boolean
```

After checkout/setup:

```yaml
- name: Verify repository release assets
  shell: pwsh
  run: ./tools/VerifyReleaseAssets.ps1
```

Before publish:

```yaml
- name: Verify strict release assets
  if: ${{ github.event_name == 'workflow_dispatch' && inputs.release_gate }}
  shell: pwsh
  run: ./tools/VerifyReleaseAssets.ps1 -RequireIcon
```

After publish, for strict mode:

```yaml
- name: Verify published executable release assets
  if: ${{ github.event_name == 'workflow_dispatch' && inputs.release_gate }}
  shell: pwsh
  run: >
    ./tools/VerifyReleaseAssets.ps1
    -RequireIcon
    -PublishedExe artifacts/publish-check/PromptHelper.exe
```

Until the real SVG exists:

```text
normal CI = can pass
strict release_gate = MUST fail with MISSING_REQUIRED_ASSET
```

That is the desired behavior.

---

# 16. CRUU4-012 — Release ICO verification is not yet standalone-complete

**Severity:** MEDIUM  
**Area:** binary release verification

## 16.1 Current defect

`VerifyReleaseAssets.ps1` currently verifies:

```text
ICO header
type
minimum frame count
square frame dimensions
required nominal sizes
```

The unit-side icon parser previously checks more, including payload bounds.

A release script should not rely on a unit test to prove its own binary parser.

The script also does not verify that `PromptHelper.exe` actually contains an icon resource after publish.

## 16.2 Add directory-entry bounds checks

After reading `$count`:

```powershell
$directoryLength = 6 + ($count * 16)

if ($bytes.Length -lt $directoryLength) {
    throw "ICO directory table is truncated."
}
```

Inside each entry:

```powershell
$imageSize = [System.BitConverter]::ToUInt32($bytes, $offset + 8)
$imageOffset = [System.BitConverter]::ToUInt32($bytes, $offset + 12)

if ($imageSize -eq 0) {
    throw "ICO frame $i has zero image size."
}

if ($imageOffset -lt $directoryLength) {
    throw "ICO frame $i points inside the directory table."
}

$end = [UInt64]$imageOffset + [UInt64]$imageSize

if ($end -gt [UInt64]$bytes.Length) {
    throw "ICO frame $i extends beyond end of file."
}
```

## 16.3 Add published EXE parameter

At top:

```powershell
param(
    [switch]$RequireIcon,
    [string]$PublishedExe
)
```

## 16.4 Check embedded icon groups on Windows

Add:

```powershell
if ($RequireIcon -and $PublishedExe) {
    $resolvedExe = (Resolve-Path $PublishedExe).Path

    Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class PromptHelperNativeIconCheck
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint ExtractIconEx(
        string szFileName,
        int nIconIndex,
        IntPtr phiconLarge,
        IntPtr phiconSmall,
        uint nIcons);
}
"@

    $iconCount =
        [PromptHelperNativeIconCheck]::ExtractIconEx(
            $resolvedExe,
            -1,
            [IntPtr]::Zero,
            [IntPtr]::Zero,
            0)

    if ($iconCount -lt 1) {
        throw "Published PromptHelper.exe contains no embedded icon resources."
    }

    Write-Host "Published EXE exposes $iconCount embedded icon group(s)."
}
```

## 16.5 Add tests for the script text and binary parser

If the script remains PowerShell-only, at minimum require repository tests to assert it contains:

```text
imageSize
imageOffset
directoryLength
PublishedExe
ExtractIconEx
```

The strict Windows workflow is the real integration proof.

---

# 17. CRUU4-013 — Real logo source and ICO are still absent

**Severity:** RELEASE BLOCKER / external dependency  
**Area:** branding/release packaging

## 17.1 Current state

At audited commit `742a1d0`, the repository still does not contain:

```text
src/PromptHelper/Assets/PromptHelperLogo.svg
src/PromptHelper/Assets/PromptHelper.ico
```

The project intentionally makes icon packaging conditional:

```xml
<ApplicationIcon Condition="Exists('Assets\PromptHelper.ico')">
  Assets\PromptHelper.ico
</ApplicationIcon>

<ItemGroup Condition="Exists('Assets\PromptHelper.ico')">
  <Resource Include="Assets\PromptHelper.ico" />
</ItemGroup>
```

That is acceptable for development.

It is not final strict release completion.

## 17.2 Do not solve this by invention

The implementation AI must **not**:

- draw a substitute logo;
- generate one from text;
- use the current `P` placeholder as the release asset;
- download an unrelated icon;
- create a blank SVG just to satisfy tests;
- weaken `-RequireIcon`;
- change strict release validation so it passes without the asset.

## 17.3 Exact completion sequence when the real SVG arrives

Only after the real source artwork is supplied:

```powershell
# 1. Put exact approved source here:
src\PromptHelper\Assets\PromptHelperLogo.svg

# 2. Generate icon:
pwsh -File .\tools\GenerateAppIcon.ps1

# 3. Strictly verify source/ICO:
pwsh -File .\tools\VerifyReleaseAssets.ps1 -RequireIcon

# 4. Build/test:
dotnet clean PromptHelper.slnx
dotnet build PromptHelper.slnx -c Release
dotnet test PromptHelper.slnx -c Release

# 5. Publish:
dotnet publish src\PromptHelper\PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts\publish-final

# 6. Verify published EXE:
pwsh -File .\tools\VerifyReleaseAssets.ps1 `
  -RequireIcon `
  -PublishedExe .\artifacts\publish-final\PromptHelper.exe
```

Then manually inspect:

```text
Explorer large icon
Explorer small icon
PromptHelper window title-bar icon
Alt+Tab icon
Windows taskbar icon
published PromptHelper.exe Properties
```

---

# 18. Consolidated target architecture after CRUU4

After these fixes, data-folder transition logic should have these responsibilities.

## 18.1 `AppSettingsRepository`

Responsible for:

```text
settings schema parsing
primary/backup settings authority
atomic primary write
best-effort same-version backup sync
preservation of future-version backup
```

Not responsible for migration.

## 18.2 `ManagedDataRootPolicy`

Responsible for:

```text
absolute path semantics
volume-root exclusion
bootstrap overlap exclusion
physical identity / alias handling
current-target disjointness
```

Used by both UI transition and startup.

## 18.3 `DataFolderMigrationService`

Responsible for:

```text
read-only target inspection
stable source snapshot capture
target copy
target byte verification
target body validation
rollback bookkeeping
```

It must not show WPF UI.

## 18.4 `DataFolderTransitionCoordinator`

Responsible for:

```text
confirmation orchestration
reservation lock lifetime
migration + settings commit transaction boundary
rollback when settings commit fails
```

## 18.5 `SettingsDialog`

Responsible only for:

```text
folder picker
showing errors/warnings
requesting transition
closing with RestartRequired=true
```

## 18.6 `MainWindow`

Keeps:

```text
if SettingsDialog succeeds and RestartRequired:
    request application shutdown
```

Do not move migration logic into `MainWindow`.

---

# 19. Copy-ready target-inspection model

A weak model should not invent its own target states.

Use:

```csharp
internal enum TargetLibraryKind
{
    Empty,
    ValidPrimary,
    RecoverableBackupOnly,
    CorruptPrimaryWithValidBackup,
    FutureSchema,
    Invalid
}

internal sealed record TargetInspection(
    string NormalizedRoot,
    TargetLibraryKind Kind,
    LibraryDocument? EffectiveDocument,
    string? EffectiveMetadataPath,
    string? Warning,
    Exception? Error);
```

Read-only inspection must:

1. not create directories;
2. not create probe files;
3. parse primary if present;
4. preserve future schema as `FutureSchema`;
5. if primary is valid, keep its parsed `LibraryDocument`;
6. if backup-only and valid, keep backup document;
7. validate prompt bodies before calling a target selectable;
8. return no mutation side effects.

The existing-library confirmation should happen only after this read-only inspection.

---

# 20. Full required CRUU4 regression test list

At minimum add these named tests.

## Settings

```text
CRUU4_001_Valid_primary_preserves_future_schema_backup
CRUU4_001_Valid_primary_unreadable_backup_starts_with_warning
```

## Library startup

```text
CRUU4_002_Valid_primary_preserves_future_schema_library_backup
CRUU4_002_Corrupt_primary_future_backup_throws_unsupported_schema
CRUU4_002_Valid_primary_locked_backup_still_starts
```

## Migration snapshot

```text
CRUU4_003_Snapshot_document_is_parsed_from_same_bytes_that_are_hashed
CRUU4_003_Snapshot_referenced_prompt_set_matches_snapshot_document
CRUU4_004_Altered_but_valid_target_library_bytes_abort_and_rollback
CRUU4_005_Snapshot_read_failure_leaves_nonexistent_target_nonexistent
```

## Switching semantics

```text
CRUU4_006_Damaged_current_prompt_does_not_block_switch_to_existing_good_library
CRUU4_006_Damaged_current_prompt_still_blocks_copy_to_empty_target
CRUU4_007_Backup_only_target_with_missing_prompt_is_not_recoverable
CRUU4_007_Backup_only_target_with_all_prompt_bodies_is_selectable
```

## Path policy

```text
CRUU4_008_Persisted_volume_root_is_rejected_before_bootstrap
CRUU4_008_Persisted_bootstrap_parent_is_rejected
CRUU4_008_Exact_bootstrap_root_is_allowed
CRUU4_009_Physical_alias_of_current_is_treated_as_same_root
CRUU4_009_Physical_alias_into_bootstrap_is_rejected
CRUU4_009_Normalization_preserves_drive_root_semantics
```

## Transition transaction

```text
CRUU4_010_New_target_is_rolled_back_if_settings_primary_write_fails
CRUU4_010_Existing_target_confirmation_cancel_writes_no_target_probe_files
CRUU4_010_Target_state_change_after_inspection_is_detected_under_reservation
CRUU4_010_Reservation_blocks_second_transition_writer
```

## Release

```text
CRUU4_011_Workflow_invokes_non_strict_release_asset_verification
CRUU4_011_Strict_workflow_path_invokes_RequireIcon
CRUU4_012_Release_asset_script_checks_ico_payload_bounds
CRUU4_012_Release_asset_script_supports_published_exe_icon_check
```

Do not delete existing CRUU1/2/3 regression tests.

---

# 21. Fault-injection scenarios the weak model must run

The following are mandatory because normal happy-path tests are insufficient.

## 21.1 Settings matrix

Test all:

```text
primary valid / backup missing
primary valid / backup valid same schema
primary valid / backup corrupt
primary valid / backup future
primary valid / backup unreadable

primary missing / backup valid
primary missing / backup future
primary missing / backup corrupt
primary missing / backup unreadable

primary corrupt / backup valid
primary corrupt / backup future
primary corrupt / backup corrupt
primary corrupt / backup unreadable

primary future / backup anything
primary unreadable / backup anything
```

Authority rules:

```text
future PRIMARY          = halt; never downgrade
unreadable PRIMARY      = halt; never substitute stale backup
valid PRIMARY           = run from primary
future BACKUP + valid P = preserve backup; warn; do not block
unreadable BACKUP + P   = warn; do not block valid primary
```

## 21.2 Library matrix

Equivalent matrix for:

```text
library.json
library.backup.json
```

Preserve valid-primary authority while protecting future backup.

## 21.3 Migration failure points

Inject failure at:

```text
source library snapshot read
source prompt snapshot read
target root creation
target prompts directory creation
target library copy
target backup copy
target prompt N copy
target recovery file copy
source final library hash read
source final prompt hash read
target library hash read
target prompt hash read
target structural validation
capability probe
settings primary save
settings backup save
```

For each failure decide explicitly:

- source must remain untouched;
- new target mutations created by this transition must roll back when transition is not committed;
- pre-existing target content must never be deleted.

---

# 22. Manual Windows regression matrix

Automated tests are necessary but not sufficient for these filesystem behaviors.

Use a disposable test profile/data tree.

## 22.1 Normal default-root use

1. start with no settings files;
2. launch;
3. verify default initialization;
4. create category/prompt;
5. restart;
6. confirm content.

## 22.2 Empty target migration

1. create at least three prompts;
2. select a new empty target;
3. save;
4. verify app forces shutdown;
5. reopen;
6. verify all prompts and titles;
7. verify old root unchanged.

## 22.3 Existing target switch

1. create library A;
2. create separate library B;
3. reopen A;
4. select B;
5. verify explicit confirmation;
6. confirm;
7. verify forced shutdown;
8. reopen;
9. verify B loaded;
10. verify A unchanged.

## 22.4 Existing target cancel

1. choose B while running A;
2. cancel confirmation;
3. verify settings still point to A;
4. verify no content changed;
5. verify no probe directory is left in B.

## 22.5 Broken current prompt + healthy existing target

1. run current library;
2. externally remove one prompt body;
3. refresh/restart so it is unavailable;
4. choose healthy existing target B;
5. verify switch remains possible.

## 22.6 Broken current prompt + empty target

Same setup, choose empty target.

Expected:

```text
migration rejected
no target artifacts
source untouched
```

## 22.7 Junction alias

Create junction alias to current root.

Attempt to select alias.

Expected:

```text
treated as same physical root
no migration
no "another instance" false diagnosis
```

Create junction into bootstrap tree.

Expected:

```text
rejected by physical topology policy
```

## 22.8 Future settings backup

Place:

```text
settings.json         schema 1
settings.backup.json  schema 99
```

Launch.

Expected:

```text
schema-1 primary loads
schema-99 backup bytes unchanged
warning displayed
```

## 22.9 Future library backup

Place:

```text
library.json         schema 1 valid
library.backup.json  schema 99
```

Launch.

Expected:

```text
schema-1 primary loads
schema-99 backup bytes unchanged
warning displayed
```

## 22.10 Strict release gate

Before logo arrives:

```powershell
pwsh .\tools\VerifyReleaseAssets.ps1 -RequireIcon
```

Expected: fail with `MISSING_REQUIRED_ASSET`.

After logo arrives: pass and published EXE contains icon resource.

---

# 23. CI commands after implementation

Run from repository root on Windows:

```powershell
dotnet --info
dotnet clean PromptHelper.slnx
dotnet restore PromptHelper.slnx
dotnet build PromptHelper.slnx -c Release --no-restore
dotnet test PromptHelper.slnx -c Release --no-build
```

Then five clean repeated runs:

```powershell
1..5 | ForEach-Object {
    Write-Host "=== CRUU4 regression run $_ ==="
    dotnet test PromptHelper.slnx `
      -c Release `
      --no-build `
      --logger "trx;LogFileName=cruu4-run-$_.trx"

    if ($LASTEXITCODE -ne 0) {
        throw "CRUU4 test run $_ failed."
    }
}
```

Development publish:

```powershell
dotnet publish src\PromptHelper\PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts\publish-cruu4
```

Non-strict asset check:

```powershell
pwsh .\tools\VerifyReleaseAssets.ps1
```

Strict release asset check remains expected to fail until the real SVG exists:

```powershell
pwsh .\tools\VerifyReleaseAssets.ps1 -RequireIcon
```

---

# 24. Verification output the implementation AI must produce

After fixing CRUU4, the implementing model should report a matrix like:

| CRUU4 | Fix | Automated proof | Manual/runtime proof | Status |
|---|---|---|---|---|
| 001 | future settings backup preserved | named tests | startup scenario | PASS |
| 002 | future library backup preserved | named tests | startup scenario | PASS |
| 003 | coherent snapshot | named tests | migration | PASS |
| 004 | target metadata hash | named test | migration | PASS |
| 005 | rollback starts before target mutation | named fault test | failure scenario | PASS |
| 006 | existing switch independent from source body health | named tests | UI switch | PASS |
| 007 | backup-only body validation | named tests | recovery target | PASS |
| 008 | startup root policy | named tests | edited-settings scenario | PASS |
| 009 | physical path identity | fake resolver + Windows test | junction scenario | PASS |
| 010 | reservation/transition transaction | named fault tests | two-instance scenario | PASS |
| 011 | release verifier wired | workflow assertion | workflow run | PASS |
| 012 | stronger binary verification | tests/script | strict workflow | PASS |
| 013 | real SVG/ICO | strict verifier | Explorer/taskbar | BLOCKED until real asset |

Do not mark CRUU4-013 PASS before the actual approved asset exists.

---

# 25. Weak-model implementation checklist by file

## `src/PromptHelper/Services/AppSettingsRepository.cs`

- [ ] restructure `LoadOrRecover` primary-first;
- [ ] preserve future backup when primary valid;
- [ ] do not let unreadable backup block valid primary;
- [ ] retain CRUU3 future-primary halt;
- [ ] retain CRUU3 unreadable-primary halt;
- [ ] keep save schema guard.

## `src/PromptHelper/Services/LibraryStartupService.cs`

- [ ] add `Unreadable` metadata state;
- [ ] inspect backup safely with valid primary;
- [ ] preserve future backup;
- [ ] classify corrupt-primary/future-backup as unsupported schema;
- [ ] preserve valid-primary behavior with locked backup.

## `src/PromptHelper/Services/DataFolderMigrationService.cs`

- [ ] expose/read-only target inspection;
- [ ] classifier retains parsed effective document;
- [ ] backup-only target validates prompt bodies;
- [ ] existing target path no longer validates complete current source;
- [ ] empty target captures one coherent source snapshot before target touch;
- [ ] target `library.json` hash checked;
- [ ] all target mutation under transaction owner.

## New `src/PromptHelper/Services/IPhysicalPathResolver.cs`

- [ ] add interface.

## New `src/PromptHelper/Services/WindowsPhysicalPathResolver.cs`

- [ ] resolve final physical path via directory handle;
- [ ] resolve nearest existing ancestor for new targets;
- [ ] normalize extended path prefix.

## New/updated `PathIdentity`

- [ ] preserve root syntax;
- [ ] physical same-root comparison;
- [ ] physical containment comparison.

## New `ManagedDataRootPolicy`

- [ ] volume root rule;
- [ ] bootstrap overlap rule;
- [ ] physical alias rule;
- [ ] reusable by UI and startup.

## `src/PromptHelper/App.xaml.cs`

- [ ] apply managed-root policy before `AppPaths`/directory creation/lock;
- [ ] keep custom-root bootstrap existence check;
- [ ] keep future settings and future library dialogs.

## Transition orchestration

- [ ] add `DataFolderTransitionCoordinator`;
- [ ] add target reservation;
- [ ] move confirmation before existing-target write probe;
- [ ] hold reservation through settings primary commit;
- [ ] rollback new target when settings primary commit fails.

## `src/PromptHelper/Views/SettingsDialog.xaml.cs`

- [ ] become thin UI wrapper around coordinator;
- [ ] no duplicated transition state machine;
- [ ] preserve `RestartRequired`.

## `.github/workflows/windows-ci.yml`

- [ ] run non-strict release asset verification always;
- [ ] add strict `release_gate` input;
- [ ] strict gate runs before release publish;
- [ ] strict post-publish EXE icon verification.

## `tools/VerifyReleaseAssets.ps1`

- [ ] add ICO directory bounds;
- [ ] add per-entry payload offset/length checks;
- [ ] add `PublishedExe`;
- [ ] verify embedded icon resources when strict.

## Tests

- [ ] add every test from section 20;
- [ ] retain all older regression tests;
- [ ] five repeated Release runs.

---

# 26. Things the weak model must NOT do

1. Do not delete future-schema backups.
2. Do not overwrite future-schema backups “because primary wins.”
3. Do not make a future backup active when a valid current primary exists.
4. Do not let a locked backup block a valid primary.
5. Do not parse metadata from one read and hash another read as if it were one snapshot.
6. Do not accept structural target validity as a substitute for snapshot byte equality.
7. Do not create target directories before source snapshot capture for empty-target migration.
8. Do not require a complete current source when selecting a separate existing target.
9. Do not call a backup-only target recoverable without checking referenced prompt files.
10. Do not trust only `Path.GetFullPath` for security/safety topology.
11. Do not use simple `TrimEnd('\\')` as root canonicalization.
12. Do not rely on one target lock probe when the transition spans more operations.
13. Do not merge libraries.
14. Do not delete the old source after migration.
15. Do not create a fake icon.
16. Do not weaken strict release checks to make a missing asset pass.
17. Do not claim the executable icon is validated merely because an ICO file exists.
18. Do not remove old regression tests to make the suite green.
19. Do not mark a fault-injection test skipped because the code is inconvenient to test.
20. Do not change schema version during this repair.

---

# 27. Final definition of done

CRUU4 is complete only when all of the following are true:

```text
[ ] CRUU4-001 future settings backup preservation implemented
[ ] CRUU4-002 future library backup preservation implemented
[ ] CRUU4-003 coherent migration snapshot implemented
[ ] CRUU4-004 target library byte/hash verification implemented
[ ] CRUU4-005 pre-mutation snapshot + rollback boundary implemented
[ ] CRUU4-006 existing-target switch decoupled from current prompt-body health
[ ] CRUU4-007 backup-only referenced bodies validated
[ ] CRUU4-008 startup revalidates persisted managed-root policy
[ ] CRUU4-009 physical alias/reparse path identity implemented
[ ] CRUU4-010 reservation + transition/settings rollback implemented
[ ] CRUU4-011 release verifier integrated into workflow
[ ] CRUU4-012 standalone release binary verification strengthened
[ ] all existing tests still pass
[ ] every new named CRUU4 test passes
[ ] Release build has 0 errors
[ ] Release build has 0 warnings attributable to project code
[ ] five consecutive test runs pass
[ ] self-contained win-x64 publish succeeds
[ ] non-strict asset verifier passes
```

Strict final release additionally requires:

```text
[ ] approved PromptHelperLogo.svg is present
[ ] generated PromptHelper.ico is present
[ ] strict asset verifier passes
[ ] published EXE contains embedded icon resource
[ ] Explorer/titlebar/taskbar icon manually checked
```

Until those icon requirements are met:

```text
DEVELOPMENT / FUNCTIONAL ACCEPTANCE = possible after CRUU4 fixes
STRICT BRANDED RELEASE ACCEPTANCE   = BLOCKED
```

---

# 28. Final implementation prompt for the weak model

Use the following text verbatim when handing `cruu4.md` to the implementation model:

```text
ROLE
You are the implementation agent for Prompt Helper. Implement every open requirement in cruu4.md against the current repository. cruu4.md is a repair blueprint, not a suggestion list.

AUTHORITY
Preserve all locked product decisions in cruu1.md, cruu2.md, cruu3.md, and cruu4.md. Later CRUU documents override earlier implementation guidance only where they explicitly repair a defect. Do not invent new product behavior.

MANDATORY PROCESS
1. Confirm HEAD and record it.
2. Read cruu4.md completely before editing.
3. Implement CRUU4 in the exact phase order in section 4.
4. Do not fabricate the missing logo asset.
5. Add all named regression tests.
6. Do not delete or weaken existing tests.
7. Run Release build and full Release tests.
8. Run the full suite five consecutive times.
9. Run self-contained win-x64 publish.
10. Run non-strict release asset verification.
11. If the real SVG/ICO are present, also run the strict release gate and published-EXE icon validation. If they are absent, report MISSING_REQUIRED_ASSET rather than fabricating them.
12. Produce a finding-by-finding verification matrix with exact test names and commands.

ZERO-DESIGN-CHOICE RULE
Where cruu4.md gives code, state machines, names, ordering, or expected behavior, use them. Do not replace them with a weaker shortcut. If a tiny compile adjustment is needed, preserve the exact semantics.

DATA-SAFETY RULES
Never overwrite a future-schema backup with current-schema data.
Never use a stale backup when the primary is merely unreadable.
Never merge libraries.
Never delete the old source during a root transition.
Never call an incomplete backup-only target recoverable.
Never treat lexical path inequality as proof of physical directory inequality.

RELEASE RULE
The real supplied SVG is authoritative. The strict release gate must fail while it is absent. Do not generate or approximate a substitute.

FINAL ACCEPTANCE
Do not claim PASS until all non-asset CRUU4 findings are implemented and all mandatory tests pass. Keep CRUU4-013 explicitly BLOCKED until the real asset is supplied.
```

---

# 29. Audit conclusion

Commit `742a1d0` is clearly stronger than the pre-CRUU3 build and the reported 240-test success should be retained as useful evidence. The fresh audit nevertheless found several edge conditions with real data-protection consequences, especially:

```text
current primary overwriting future backup
source migration snapshot being assembled from different metadata moments
target metadata not being byte-verified against the snapshot
backup-only targets being accepted without body completeness
persisted paths bypassing selection-time safety policy
junction/symlink aliases bypassing lexical topology
transition lock/settings operations not sharing one reservation/transaction
```

These are appropriate CRUU4 targets because they close concrete failure paths rather than add new product scope.

After CRUU4-001 through CRUU4-012 are implemented and verified, the only intended outstanding blocker should be the externally supplied, authoritative Prompt Helper logo asset described in CRUU4-013.
