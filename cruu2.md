# CRUU2 — Full Regression Re-Audit and Weak-Model Fix Blueprint

**Project:** Prompt Helper  
**Repository:** `Ceegore/AI_prompt_helper`  
**Audited branch:** `main`  
**Audited commit:** `7fea75db46ef08efca242298bcdd904d48a2c4c4`  
**Primary requirement source for this repair round:** `cruu1.md` as implemented by the commit above  
**Purpose of this file:** identify every still-open issue found by a fresh regression audit and provide a complete, implementation-ready repair plan that a weak coding model can execute without making design decisions.

---

# 1. Executive result

The `cruu1.md` implementation is **not yet acceptable as complete**.

A second audit found additional defects beyond the first post-implementation review. Most requested features are structurally present, but several open issues affect:

- application startup;
- data integrity;
- custom headline semantics;
- configurable data-folder safety;
- icon packaging;
- automated-test reliability;
- regression-test truthfulness;
- documentation accuracy;
- CI evidence.

This file is both the defect report and the complete fix plan.

## 1.1 Final acceptance status before these fixes

```text
STATUS = FAIL
```

Do not mark the `cruu1.md` implementation complete until every blocker/high/medium item in this document is resolved and the final Windows build/test/manual gate passes.

---

# 2. Authority and non-negotiable decisions

The implementation model must preserve the following decisions from `cruu1.md` unless this file explicitly corrects a defect in their implementation.

## 2.1 Keep these product decisions

1. Prompt bodies remain separate `.md` files.
2. Optional custom prompt headline remains metadata in `PromptRecord.Title`.
3. `Title == null` means automatic headline mode.
4. The existing first-non-empty-line automatic headline fallback remains.
5. Do **not** make `Title` `[JsonRequired]`.
6. Keep `LibraryDocument.CurrentSchemaVersion == 1` for this repair round.
7. Do not introduce a schema-migration framework solely for the optional title.
8. Editor line wrapping remains **visual only** and must never insert/delete prompt newlines.
9. Prompt cards remain three per row using the existing lightweight non-virtualized approach. `cruu1.md` explicitly accepted the loss of recycling virtualization for this feature round.
10. Recent-copy history remains session-only, unique by prompt ID, newest-first, maximum three entries.
11. Recent-copy history must not be persisted.
12. Existing valid destination Prompt Helper libraries may be selected as a data root without overwriting them.
13. Changing the data root takes effect on the next application start; do not hot-swap the live repository graph.
14. Old source data remains untouched after a successful copy-to-new-folder migration.
15. Use the supplied Prompt Helper logo SVG as the icon source. Do not invent/redraw a replacement if the real SVG is absent.
16. The application remains local/offline; no telemetry, cloud account, network dependency, registry prompt history, database, or unrelated feature expansion.

## 2.2 Do not “fix” these accepted behaviors

The following are **not defects** in this round:

- no custom virtualizing wrap panel;
- schema version remains 1;
- recent history resets at every application launch;
- old source data is not automatically deleted after folder migration;
- the quick-bar tile itself does not navigate/edit; only its small Copy button is actionable;
- the main prompt tooltip shows the full prompt body, not a rendered Markdown document.

## 2.3 Downgrade compatibility note

Because `Title` is an additive field while schema version stays 1, an **older Prompt Helper binary** may ignore the unknown `title` property and later rewrite metadata without it.

This is an accepted risk from the `cruu1.md` schema-v1 decision. Do **not** add a schema bump in this repair round. Add a short documentation warning that once custom headlines are used, users should not edit that library with an older Prompt Helper build.

---

# 3. Audit limitations — do not misreport them

This audit inspected the current GitHub repository and the implementation/tests in commit `7fea75d`.

The audit environment does **not** contain the .NET SDK and cannot execute the WPF application locally. The audited commit also has no attached GitHub CI/check result.

Therefore:

```text
static/source verification = performed
actual Windows dotnet build = UNVERIFIED here
actual Windows dotnet test = UNVERIFIED here
real taskbar/Explorer icon = UNVERIFIED here
real 0.5 s tooltip timing = UNVERIFIED here
physical GUI interaction = UNVERIFIED here
```

This is why this repair plan includes a Windows CI workflow plus an explicit final Windows manual gate.

Never convert an unexecuted check into PASS.

---

# 4. Complete open-issue register

| ID | Severity | Area | Status | Summary |
|---|---|---|---|---|
| CRUU2-001 | BLOCKER | Icon/startup | OPEN | `MainWindow.xaml` unconditionally references `Assets/PromptHelper.ico`, but the committed repository contains no `Assets` directory, no SVG, and no ICO. |
| CRUU2-002 | HIGH | Data integrity | OPEN | `CreatePrompt` writes the prompt `.md` before validating title metadata. Invalid title metadata can throw before rollback begins and leave an orphan body file. |
| CRUU2-003 | HIGH | Data-folder safety | OPEN | A configured custom data root that disappears can be recreated as an empty folder and initialized with defaults, masking the fact that the user’s real library is unavailable. |
| CRUU2-004 | MEDIUM | Headline compatibility | OPEN | Legacy two-argument `EditPrompt(id, content)` overloads clear an existing custom title by forwarding `null`. |
| CRUU2-005 | MEDIUM | Headline semantics | OPEN | An automatic-title prompt is opened with its fallback headline prefilled; saving without touching that field converts automatic mode into a custom title and silently freezes the headline. |
| CRUU2-006 | MEDIUM | Input/error handling | OPEN | Invalid custom titles are rejected only by deep library validation; the thrown `InvalidDataException` is not part of the normal prompt-save error path and can become a fatal app-level exception. |
| CRUU2-007 | MEDIUM | Settings durability | OPEN | `settings.json` has no safety backup/recovery. Corrupt settings can prevent startup even when the prompt library itself is healthy. |
| CRUU2-008 | MEDIUM | Settings validation | OPEN | Persisted relative/invalid data-root paths are normalized too permissively; startup errors are generic and can be misleading. |
| CRUU2-009 | MEDIUM | Migration integrity | OPEN | Folder migration validates the target but not the current source library before copying. If source metadata disappears/changes externally during the session, migration can produce an incomplete target. |
| CRUU2-010 | MEDIUM | Migration topology | OPEN | A user can select a nested directory inside the current managed data tree, creating confusing self-nested data layouts. |
| CRUU2-011 | MEDIUM | WPF tests | OPEN | `RunOnStaThread` can leave a singleton WPF `Application` owned by a terminated STA thread and later access it from other threads. |
| CRUU2-012 | MEDIUM | Test truthfulness | OPEN | Several new tests silently do nothing when source paths are not found (`if (File.Exists(...))`), so missing files can produce a false PASS. |
| CRUU2-013 | MEDIUM | Test coverage | OPEN | Icon test only checks strings in the generator script; no test proves SVG/ICO existence, ICO frame validity, resource packaging, or `MainWindow` construction. |
| CRUU2-014 | MEDIUM | Regression coverage | OPEN | No automated integration check proves failed clipboard writes leave recent history unchanged through the real UI copy path; GUI structure/timing checks are mostly source-text tests. |
| CRUU2-015 | MEDIUM | CI/release gate | OPEN | The repository has no Windows CI status for the implementation. Build/test regressions are not automatically blocked. |
| CRUU2-016 | LOW | Settings dialog | OPEN | Path normalization is performed before `SaveButton_Click` enters its exception handling block. Move all path work into the guarded path. |
| CRUU2-017 | MEDIUM | Documentation | OPEN | README and the German usage guide still describe the old fixed data folder, `?` Help button, separate category rename/delete buttons, full-width card content area, and pre-headline/pre-recent-bar UI. |
| CRUU2-018 | LOW | Icon generation | OPEN | The generator script does not explicitly square-pad non-square SVG artwork as required by `cruu1.md`, and does not validate ICO frame dimensions. |

No additional blocker/high issue was confirmed after the register above. Items may share a single implementation phase, but do not close an ID until its explicit acceptance tests pass.

---

# 5. CRUU2-001 + CRUU2-018 — repair the Windows icon implementation completely

## 5.1 Current defect

Current main-window XAML contains an unconditional resource reference conceptually equivalent to:

```xml
Icon="/PromptHelper;component/Assets/PromptHelper.ico"
```

The project file conditionally uses the ICO only if it happens to exist.

The repository at the audited commit does **not** contain:

```text
src/PromptHelper/Assets/PromptHelperLogo.svg
src/PromptHelper/Assets/PromptHelper.ico
```

The current generator script expects those paths but only the script itself was committed.

Result:

- requested icon feature is incomplete;
- the project is allowed to build without the executable icon because `ApplicationIcon` is conditional;
- the XAML still references the missing WPF resource;
- the current icon test can pass even though neither real asset exists.

## 5.2 Required behavior

After repair, all must be true:

```text
real supplied SVG exists in repo
multi-resolution ICO exists in repo
ICO has 16/24/32/48/64/128/256 frames
ICO preserves transparency
non-square SVG is not stretched
EXE receives ICO
MainWindow receives ICO
missing ICO becomes a build/test failure, not a silent fallback
no runtime image-conversion dependency exists
```

## 5.3 Required asset paths

Use exactly these paths unless the real supplied SVG is already stored at another canonical path:

```text
src/PromptHelper/Assets/PromptHelperLogo.svg
src/PromptHelper/Assets/PromptHelper.ico
```

If a real supplied logo SVG is found elsewhere, either:

1. move it to the canonical path above; or
2. keep that existing canonical source and update every reference consistently.

Do not retain two diverging source SVGs.

## 5.4 Missing-source rule

If the implementation agent cannot find the actual supplied SVG:

```text
DO NOT draw a substitute
DO NOT generate a generic P logo
DO NOT download unrelated artwork
DO NOT mark CRUU2-001 complete
```

Report:

```text
MISSING_REQUIRED_ASSET: Prompt Helper logo SVG
```

Continue all non-icon fixes, but final overall status remains FAIL until the real logo source is supplied.

## 5.5 Replace the generator command with aspect-safe square padding

Update `tools/GenerateAppIcon.ps1` so it renders the SVG into a square 256x256 transparent canvas before generating icon frames.

Recommended conversion:

```powershell
& magick `
    -background none `
    $sourceSvg `
    -resize "256x256" `
    -gravity center `
    -extent "256x256" `
    -define icon:auto-resize=256,128,64,48,32,24,16 `
    $outputIco
```

Important ImageMagick behavior:

- `-resize 256x256` without `!` preserves aspect ratio;
- `-extent 256x256` centers the result on a transparent square canvas;
- never use `256x256!`, because `!` forces distortion.

## 5.6 Generator preconditions

The script must fail non-zero if:

- SVG missing;
- `magick` missing;
- output directory cannot be created;
- conversion exits non-zero;
- ICO absent;
- ICO length is zero;
- ICO header/frames are invalid.

Do not use `exit 0` after a failed conversion.

## 5.7 Add a small ICO validator helper to the script

At minimum parse the ICO header:

```text
ICONDIR:
reserved ushort == 0
type ushort == 1
count ushort >= 7
```

Each 16-byte directory entry contains width/height bytes. A byte value of `0` means 256.

Required dimensions:

```text
16, 24, 32, 48, 64, 128, 256
```

The script may implement a PowerShell binary check, or the C# tests below may be authoritative. Prefer both.

## 5.8 Make project configuration fail fast

After the real ICO is committed, replace conditional configuration with unconditional configuration.

Use:

```xml
<PropertyGroup>
  ...
  <ApplicationIcon>Assets\PromptHelper.ico</ApplicationIcon>
</PropertyGroup>

<ItemGroup>
  <Resource Include="Assets\PromptHelper.ico" />
</ItemGroup>
```

Remove:

```xml
Condition="Exists('Assets\PromptHelper.ico')"
```

Why:

A required release asset should break the build if it disappears. A conditional icon makes a regression invisible.

## 5.9 Keep explicit window icon

Keep the explicit `MainWindow.xaml` icon binding once the resource exists:

```xml
Icon="/PromptHelper;component/Assets/PromptHelper.ico"
```

If that exact pack URI fails in real WPF execution, fix the URI to the correct embedded-resource URI. Do not remove the icon requirement.

## 5.10 Add binary ICO unit test

Add `tests/PromptHelper.Tests/IconAssetTests.cs`.

Suggested helper:

```csharp
using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PromptHelper.Tests;

[TestClass]
public sealed class IconAssetTests
{
    [TestMethod]
    public void PromptHelperIco_exists_is_nonempty_and_contains_required_square_frames()
    {
        string root = RepositoryTestPaths.Root;
        string svg = Path.Combine(root, "src", "PromptHelper", "Assets", "PromptHelperLogo.svg");
        string ico = Path.Combine(root, "src", "PromptHelper", "Assets", "PromptHelper.ico");

        Assert.IsTrue(File.Exists(svg), $"Missing required SVG: {svg}");
        Assert.IsTrue(File.Exists(ico), $"Missing required ICO: {ico}");

        byte[] bytes = File.ReadAllBytes(ico);
        Assert.IsTrue(bytes.Length > 6, "ICO is empty or truncated.");

        ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2));
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2));
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2));

        Assert.AreEqual((ushort)0, reserved);
        Assert.AreEqual((ushort)1, type);
        Assert.IsTrue(count >= 7);
        Assert.IsTrue(bytes.Length >= 6 + (count * 16));

        var sizes = new HashSet<int>();

        for (int i = 0; i < count; i++)
        {
            int offset = 6 + (i * 16);
            int width = bytes[offset] == 0 ? 256 : bytes[offset];
            int height = bytes[offset + 1] == 0 ? 256 : bytes[offset + 1];

            Assert.AreEqual(width, height, $"ICO frame {i} is not square.");
            sizes.Add(width);

            uint imageSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 8, 4));
            uint imageOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 12, 4));

            Assert.IsTrue(imageSize > 0, $"ICO frame {i} has zero image size.");
            Assert.IsTrue(imageOffset < bytes.Length, $"ICO frame {i} offset is outside file.");
            Assert.IsTrue((ulong)imageOffset + imageSize <= (ulong)bytes.Length,
                $"ICO frame {i} extends past end of file.");
        }

        foreach (int required in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            Assert.IsTrue(sizes.Contains(required), $"ICO is missing {required}x{required} frame.");
        }
    }
}
```

## 5.11 Add project-file assertion

Test that the project contains an unconditional application icon declaration and resource entry.

Do not use `if (File.Exists(...))`.

```csharp
[TestMethod]
public void Project_requires_application_icon_resource()
{
    string csprojPath = Path.Combine(
        RepositoryTestPaths.Root,
        "src", "PromptHelper", "PromptHelper.csproj");

    string xml = File.ReadAllText(csprojPath);

    StringAssert.Contains(xml, "<ApplicationIcon>Assets\\PromptHelper.ico</ApplicationIcon>");
    StringAssert.Contains(xml, "<Resource Include=\"Assets\\PromptHelper.ico\"");
    Assert.IsFalse(xml.Contains("ApplicationIcon Condition=", StringComparison.Ordinal));
}
```

## 5.12 Add runtime window-construction test

After the single-STA host from section 12 is implemented, construct `MainWindow` under that host. If icon/resource lookup is broken, this must fail.

## 5.13 Manual icon acceptance

On Windows after Release build/publish:

1. Open output directory in Explorer.
2. Confirm `PromptHelper.exe` shows Prompt Helper logo.
3. Launch app.
4. Confirm title-bar/window icon.
5. Confirm taskbar icon.
6. Pin/unpin or refresh Explorer if shell icon cache delays display.
7. Inspect small icon display around 16/24/32 px.
8. Inspect large Explorer icon around 128/256 px.
9. Confirm logo is not stretched.
10. Confirm transparency has no opaque rectangle.

Do not close CRUU2-001 from unit tests alone; manual shell/taskbar inspection is required.

---

# 6. CRUU2-002 + CRUU2-006 — fix headline validation and CreatePrompt orphan risk

## 6.1 Current dangerous ordering

Current `CreatePrompt` conceptually performs:

```text
create prompt body file
add metadata record to candidate
validate candidate metadata
try metadata commit with rollback
```

This is wrong now that `Title` can be invalid.

Example:

```text
title contains a control character
-> prompt body .md is created
-> LibraryValidator.Validate(candidate) throws InvalidDataException
-> code has not entered commit try/catch yet
-> prompt .md remains orphaned
```

This is a real data-integrity regression introduced by adding title validation.

## 6.2 Required transaction ordering

Reorder `PromptLibraryService.CreatePrompt` to:

```text
clone metadata
validate category
calculate ID/order
automatically normalize + validate title
create PromptRecord in candidate
validate candidate metadata BEFORE writing body
write body file
try metadata commit
if commit fails: best-effort delete body
only then publish candidate as _document
```

## 6.3 Add explicit service-level title normalization/validation

Do not rely on deep `LibraryValidator.Validate` to produce a user-facing validation error.

Replace current `NormalizePromptTitle` with a helper that returns null for automatic mode and throws `InvalidOperationException` for invalid user input.

No new maximum headline length is introduced in this repair round. Preserve current product semantics and only enforce the already-existing invariant: a stored title must be trimmed, nonblank, and contain no control characters.

Use:

```csharp
private static string? NormalizeAndValidatePromptTitle(string? input)
{
    string trimmed = (input ?? string.Empty).Trim();

    if (trimmed.Length == 0)
    {
        return null;
    }

    if (trimmed.Any(char.IsControl))
    {
        throw new InvalidOperationException(
            "Headline cannot contain line breaks, tabs, or other control characters.");
    }

    return trimmed;
}
```

Use this helper in create and edit.

Keep `LibraryValidator` title validation as defense-in-depth for persisted/corrupt data.

## 6.4 Correct CreatePrompt code skeleton

Use this sequence:

```csharp
public OperationResult<PromptRecord> CreatePrompt(
    Guid? categoryId,
    string content,
    string? title)
{
    var candidate = LibraryDocumentCloner.Clone(_document);

    if (categoryId.HasValue &&
        !candidate.Categories.Any(c => c.Id == categoryId.Value))
    {
        throw new InvalidOperationException(
            $"Category does not exist: {categoryId.Value}");
    }

    Guid newPromptId = GenerateUniquePromptGuid(candidate);
    long nextSortOrder = CalculateNextPromptSortOrder(candidate, categoryId, null);
    string? normalizedTitle = NormalizeAndValidatePromptTitle(title);

    var newPrompt = new PromptRecord
    {
        Id = newPromptId,
        CategoryId = categoryId,
        SortOrder = nextSortOrder,
        Title = normalizedTitle
    };

    candidate.Prompts.Add(newPrompt);

    // IMPORTANT: no prompt file exists yet.
    LibraryValidator.Validate(candidate);

    // Create body only after metadata candidate is known-valid.
    _promptRepo.Create(newPromptId, content);

    CommitResult commitResult;
    try
    {
        commitResult = _libraryRepo.Commit(candidate);
    }
    catch
    {
        try
        {
            _promptRepo.DeleteIfExists(newPromptId);
        }
        catch
        {
            // Best effort cleanup. Preserve original exception.
        }

        throw;
    }

    _document = candidate;

    return new OperationResult<PromptRecord>(
        new PromptRecord
        {
            Id = newPrompt.Id,
            CategoryId = newPrompt.CategoryId,
            SortOrder = newPrompt.SortOrder,
            Title = newPrompt.Title
        },
        commitResult.Warning);
}
```

## 6.5 EditPrompt validation ordering

Edit currently validates candidate metadata before updating the body, which is good.

Keep:

```text
read old body
clone metadata
normalize+validate title
LibraryValidator.Validate(candidate)
update body
commit metadata
rollback body if commit throws
```

Only replace the title helper with the user-facing `NormalizeAndValidatePromptTitle`.

## 6.6 UI error handling

`MainWindow` already catches `InvalidOperationException` for prompt save flows. The new explicit helper intentionally uses that exception type so bad headline input:

- does not reach the global fatal exception handler;
- shows the existing Save Prompt Error dialog;
- reopens the prompt editor with user text preserved;
- does not create an orphan prompt file.

Do not add `catch (Exception)` to normal prompt-save handlers.

## 6.7 Make headline editor explicitly single-line

In `PromptEditorDialog.xaml` add explicit properties:

```xml
<TextBox x:Name="HeadlineTextBox"
         Grid.Row="1"
         Style="{StaticResource ModernTextBoxStyle}"
         AcceptsReturn="False"
         AcceptsTab="False"
         MaxLines="1"/>
```

This is UI defense only. Service validation remains authoritative because clipboard paste/programmatic calls can still produce unexpected data.

## 6.8 Tests for invalid title behavior

Add:

```csharp
[TestMethod]
public void Create_prompt_invalid_control_character_title_does_not_create_orphan_file()
{
    using var testDir = new TestDirectory();
    var (service, paths, _, _, _, _) = CreateTestContext(testDir.Root);

    Assert.Throws<InvalidOperationException>(() =>
        service.CreatePrompt(null, "body", "Bad\nHeadline"));

    Assert.AreEqual(0, service.CurrentDocument.Prompts.Count);
    Assert.AreEqual(0, Directory.GetFiles(paths.PromptsDirectory, "*.md").Length);
}
```

Add:

```csharp
[TestMethod]
public void Edit_prompt_invalid_title_does_not_change_body_or_metadata()
{
    using var testDir = new TestDirectory();
    var (service, _, _, promptRepo, _, _) = CreateTestContext(testDir.Root);

    var p = service.CreatePrompt(null, "Old body", "Old title").Value;

    Assert.Throws<InvalidOperationException>(() =>
        service.EditPrompt(p.Id, "New body", "Bad\tTitle"));

    Assert.AreEqual("Old body", promptRepo.Read(p.Id));
    Assert.AreEqual(
        "Old title",
        service.CurrentDocument.Prompts.Single(x => x.Id == p.Id).Title);
}
```

Add a successful trim test:

```csharp
[TestMethod]
public void Create_prompt_title_is_trimmed_before_persistence()
{
    ...
    var p = service.CreatePrompt(null, "Body", "  Valid title  ").Value;
    Assert.AreEqual("Valid title", p.Title);
}
```

---

# 7. CRUU2-004 — legacy EditPrompt overload must preserve custom title

## 7.1 Current bug

The compatibility overload currently behaves conceptually as:

```csharp
EditPrompt(id, content) => EditPrompt(id, content, null);
```

For a prompt with a custom title, that means any old caller that edits only the body clears the title.

The same problem exists in `MainViewModel`.

## 7.2 Required service fix

Change the service overload to preserve current metadata:

```csharp
public OperationResult EditPrompt(Guid promptId, string content)
{
    var current = _document.Prompts.FirstOrDefault(p => p.Id == promptId)
        ?? throw new InvalidOperationException(
            $"Prompt does not exist in library: {promptId}");

    return EditPrompt(promptId, content, current.Title);
}
```

Do not pass `null` unless the caller explicitly intends automatic-title mode.

## 7.3 Required MainViewModel fix

Do not implement the ViewModel compatibility overload as `title: null` either.

Preferred:

```csharp
public OperationResult EditPrompt(Guid promptId, string content)
{
    var currentCard = Prompts.FirstOrDefault(p => p.Id == promptId);

    if (currentCard != null)
    {
        return EditPrompt(promptId, content, currentCard.CustomTitle);
    }

    // If prompt is not in currently displayed cards, let the service
    // compatibility overload preserve metadata.
    var result = _service.EditPrompt(promptId, content);
    Refresh();
    RefreshRecentPromptDisplay(promptId);
    return result;
}
```

An even cleaner option is to remove the public two-argument ViewModel overload if no caller needs it, but only do so after repository-wide search proves there are zero callers/tests requiring it.

For a weak model, safest action is: **keep it and preserve title**.

## 7.4 Regression tests

```csharp
[TestMethod]
public void Legacy_EditPrompt_overload_preserves_custom_title()
{
    using var testDir = new TestDirectory();
    var (service, _, _, promptRepo, _, _) = CreateTestContext(testDir.Root);

    var p = service.CreatePrompt(null, "Old", "Keep me").Value;

    service.EditPrompt(p.Id, "New");

    Assert.AreEqual("New", promptRepo.Read(p.Id));
    Assert.AreEqual(
        "Keep me",
        service.CurrentDocument.Prompts.Single(x => x.Id == p.Id).Title);
}
```

ViewModel equivalent:

```csharp
[TestMethod]
public void MainViewModel_legacy_EditPrompt_preserves_custom_title()
{
    ...
    var p = vm.CreatePrompt("Old", "Keep me").Value;
    vm.EditPrompt(p.Id, "New");
    var card = vm.Prompts.Single(x => x.Id == p.Id);
    Assert.AreEqual("Keep me", card.CustomTitle);
}
```

---

# 8. CRUU2-005 — preserve automatic-title mode when headline prefill is untouched

## 8.1 Current semantic bug

`cruu1.md` intentionally asked for automatic fallback text to be prefilled when editing an old/automatic prompt.

Current flow:

```text
PromptRecord.Title == null
body first line = "Run full regression"
editor opens headline field = "Run full regression"
user only edits body
user presses Save
ResultHeadline = "Run full regression"
metadata Title becomes custom string
```

The user never chose to switch out of automatic mode, but automatic mode was silently destroyed.

Future first-line body changes will no longer update the displayed headline.

## 8.2 Required semantics

For an existing prompt:

```text
custom title exists + untouched -> keep custom title
automatic title + untouched prefilled fallback -> keep Title == null
automatic title + user edits headline -> save custom title
any mode + user clears headline -> Title == null
```

The UI still shows the automatic fallback in the edit box. We are **not** changing the visible prefill requirement.

## 8.3 Do not solve this by leaving the field blank

Do not replace the prefilled automatic headline with an empty field. That would contradict `cruu1.md`.

Track whether the user actually changed the headline field.

## 8.4 PromptEditorDialog state

Add fields:

```csharp
private readonly bool _initialHeadlineWasAutomatic;
private bool _isInitializingHeadline;
private bool _headlineWasUserEdited;
```

Extend constructor:

```csharp
public PromptEditorDialog(
    string title,
    string initialText,
    string initialHeadline = "",
    bool initialHeadlineWasAutomatic = false)
```

Initialize safely:

```csharp
_initialHeadlineWasAutomatic = initialHeadlineWasAutomatic;

_isInitializingHeadline = true;
HeadlineTextBox.Text = initialHeadline;
_isInitializingHeadline = false;
```

Wire:

```xml
TextChanged="HeadlineTextBox_TextChanged"
```

Handler:

```csharp
private void HeadlineTextBox_TextChanged(object sender, TextChangedEventArgs e)
{
    if (!_isInitializingHeadline)
    {
        _headlineWasUserEdited = true;
    }
}
```

## 8.5 Expose both raw editor text and semantic title

Add:

```csharp
public string ResultHeadlineEditorText { get; private set; } = string.Empty;
public bool ResultUsesAutomaticHeadline { get; private set; }
public string? ResultHeadline { get; private set; }
```

On Save:

```csharp
ResultText = EditorTextBox.Text;
ResultHeadlineEditorText = HeadlineTextBox.Text;

string trimmed = HeadlineTextBox.Text.Trim();

if (trimmed.Length == 0)
{
    ResultUsesAutomaticHeadline = true;
    ResultHeadline = null;
}
else if (_initialHeadlineWasAutomatic && !_headlineWasUserEdited)
{
    // Visible fallback was only a prefill. User did not choose to pin it.
    ResultUsesAutomaticHeadline = true;
    ResultHeadline = null;
}
else
{
    ResultUsesAutomaticHeadline = false;
    ResultHeadline = trimmed;
}
```

## 8.6 Edit flow initialization

Use:

```csharp
string headlineEditorText = card.EditableHeadline;
bool headlineAutomatic = card.CustomTitle is null;
```

Construct dialog:

```csharp
var dialog = new PromptEditorDialog(
    "Edit Prompt",
    promptText,
    headlineEditorText,
    headlineAutomatic)
{
    Owner = this
};
```

## 8.7 Preserve state across failed saves

This matters.

Current retry logic must not replace the automatic prefill with an empty string after a service error.

After the dialog returns true but persistence fails:

```csharp
promptText = dialog.ResultText;
headlineEditorText = dialog.ResultHeadlineEditorText;
headlineAutomatic = dialog.ResultUsesAutomaticHeadline;
```

Then reopen with those exact values.

If `headlineAutomatic == true`, the editor still displays `headlineEditorText`, but an untouched retry must still submit semantic `null`.

## 8.8 Create flow

New prompt creation starts with:

```text
initialHeadline = ""
initialHeadlineWasAutomatic = true
```

But there is no automatic prefill at creation time because body content is being authored simultaneously.

Behavior:

- blank headline -> null / automatic mode;
- typed headline -> custom;
- save failure -> preserve raw editor headline and semantic mode.

## 8.9 Tests

### Automatic untouched remains automatic

```csharp
[TestMethod]
public void Automatic_headline_prefill_untouched_remains_null_after_edit()
{
    // Test dialog semantics on WPF test host.
    // initialHeadlineWasAutomatic = true
    // do not modify headline textbox
    // invoke Save
    // assert ResultHeadline == null
    // assert ResultUsesAutomaticHeadline == true
}
```

### Automatic prompt body edit updates fallback

Service/UI integration test concept:

```text
create prompt with Title = null, body first line "Old automatic"
edit body to first line "New automatic"
leave prefilled headline untouched
save
reload
assert metadata Title == null
assert PreviewTitle == "New automatic"
```

### User deliberately changes auto title

```text
Title null
editor fallback "Old automatic"
user replaces headline with "Pinned title"
save
assert Title == "Pinned title"
```

### Custom untouched remains custom

```text
Title "Pinned"
user edits body only
save
assert Title == "Pinned"
```

### Clearing either mode produces automatic

```text
custom title "Pinned"
clear headline
save
assert Title == null
```

---

# 9. CRUU2-003 — never silently initialize a fresh library in a missing configured custom data root

## 9.1 Current data-loss-masking scenario

Startup currently resolves `DataRootPath`, constructs `AppPaths`, and creates directories before loading the library.

`LibraryStartupService.LoadOrInitialize()` also calls `EnsureDataDirectories()` and treats missing primary + missing backup + no prompt files as a clean first run.

That behavior is correct for the **default** root on a genuine first launch.

It is wrong for an explicitly configured custom root because that root was only saved after a valid library existed there.

Example:

```text
user migrates library to D:\PromptHelperData
settings.json points there
later D:\PromptHelperData is deleted/renamed or drive contents disappear
app starts
folder can be recreated empty
startup sees no metadata / no prompts
app creates default library
user sees defaults and may think their library was erased
```

## 9.2 Required rule

```text
DEFAULT root with no setting:
    missing metadata may mean first run -> initialization allowed

EXPLICIT CUSTOM root from settings:
    missing root or missing both library.json and library.backup.json
    is NOT a first run -> abort safely with actionable message
```

Do not create a fresh library in a configured custom root automatically.

## 9.3 Add custom-root state to startup bootstrap

After settings are loaded, determine:

```csharp
bool hasConfiguredCustomRoot =
    !string.IsNullOrWhiteSpace(settings.DataRootPath);
```

Before `paths.EnsureRootDirectory()` for a custom root:

```csharp
if (hasConfiguredCustomRoot)
{
    if (!Directory.Exists(effectiveDataRoot))
    {
        throw new ConfiguredDataFolderUnavailableException(
            effectiveDataRoot,
            "The configured data folder does not exist.");
    }

    string primary = Path.Combine(effectiveDataRoot, "library.json");
    string backup = Path.Combine(effectiveDataRoot, "library.backup.json");

    if (!File.Exists(primary) && !File.Exists(backup))
    {
        throw new ConfiguredDataFolderUnavailableException(
            effectiveDataRoot,
            "The configured data folder does not contain library.json or library.backup.json.");
    }
}
```

Do this **before** creating missing directories.

## 9.4 Add exception type

```csharp
public sealed class ConfiguredDataFolderUnavailableException : Exception
{
    public ConfiguredDataFolderUnavailableException(string path, string reason)
        : base($"{reason} Configured data folder: {path}")
    {
        DataFolderPath = path;
    }

    public string DataFolderPath { get; }
}
```

## 9.5 App-level handling

Catch this exception separately before the generic outer catch.

Message must state:

```text
The configured Prompt Helper data folder is unavailable:
<path>

Prompt Helper did not create a new library there, so your existing data was not overwritten.
Reconnect/restore the folder or repair the configured data-folder setting before continuing.
```

If a future UI recovery picker is desired, implement it in another feature. Do not silently reset the setting in this repair round.

## 9.6 Do not weaken normal first-run logic

When settings contain no custom data root, the normal `%LOCALAPPDATA%\PromptHelper` first-run behavior remains unchanged.

## 9.7 Tests

```csharp
[TestMethod]
public void Configured_custom_root_missing_does_not_create_directory_or_defaults()
{
    using var temp = new TestDirectory();
    string missing = Path.Combine(temp.Root, "DoesNotExist");

    // Exercise new bootstrap validator.
    Assert.Throws<ConfiguredDataFolderUnavailableException>(() =>
        DataRootBootstrapValidator.ValidateConfiguredRoot(missing));

    Assert.IsFalse(Directory.Exists(missing));
}
```

And:

```csharp
[TestMethod]
public void Configured_existing_empty_root_is_not_treated_as_first_run()
{
    using var temp = new TestDirectory();

    Assert.Throws<ConfiguredDataFolderUnavailableException>(() =>
        DataRootBootstrapValidator.ValidateConfiguredRoot(temp.Root));

    Assert.IsFalse(File.Exists(Path.Combine(temp.Root, "library.json")));
}
```

And:

```csharp
[TestMethod]
public void Configured_root_with_backup_only_is_allowed_for_normal_recovery()
{
    ... seed valid backup, remove primary ...
    DataRootBootstrapValidator.ValidateConfiguredRoot(root);
    // then LibraryStartupService must recover from backup
}
```

## 9.8 Preferred implementation shape

For testability, do not bury all validation in `App.xaml.cs`.

Add:

```text
src/PromptHelper/Services/DataRootBootstrapValidator.cs
```

with a pure/static method.

This gives deterministic unit tests without booting WPF.

---

# 10. CRUU2-007 + CRUU2-008 + CRUU2-016 — harden bootstrap settings

## 10.1 Current weakness

The application has a single fixed bootstrap file:

```text
%LOCALAPPDATA%\PromptHelper\settings.json
```

If it is corrupt, startup fails before the healthy library can be resolved.

No settings safety backup exists.

Persisted relative paths are converted using `Path.GetFullPath`, which can reinterpret a manually damaged settings file relative to process working directory instead of rejecting it.

## 10.2 Keep settings outside the movable custom root

Do not move bootstrap settings into the user-selected data root.

Canonical bootstrap location remains:

```text
%LOCALAPPDATA%\PromptHelper\settings.json
%LOCALAPPDATA%\PromptHelper\settings.backup.json
```

When the actual prompt library lives at a custom path, these two settings files remain in fixed LocalAppData.

## 10.3 Add backup path

`AppSettingsRepository`:

```csharp
private readonly string _settingsPath;
private readonly string _backupPath;
```

Default:

```csharp
string root = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "PromptHelper");

_settingsPath = Path.Combine(root, "settings.json");
_backupPath = Path.Combine(root, "settings.backup.json");
```

Test constructor may accept both path overrides or derive backup from primary directory.

## 10.4 Add explicit read result

Add model:

```csharp
public sealed record SettingsLoadResult(
    AppSettings Settings,
    bool RecoveredFromBackup,
    string? Warning);
```

Prefer:

```csharp
public SettingsLoadResult LoadOrRecover()
```

over a plain `Load()` in startup.

Keep `Load()` only if tests/other callers need strict parsing.

## 10.5 Validation rules for DataRootPath

Normalized user-selected paths are always absolute because `OpenFolderDialog` returns a concrete filesystem path.

Therefore persisted settings must reject a non-empty relative path.

Use:

```csharp
private static string? NormalizeAndValidateDataRoot(string? path)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return null;
    }

    string trimmed = path.Trim();

    if (!Path.IsPathFullyQualified(trimmed))
    {
        throw new InvalidDataException(
            "Configured dataRootPath must be an absolute filesystem path.");
    }

    try
    {
        return Path.GetFullPath(trimmed);
    }
    catch (Exception ex) when (
        ex is ArgumentException or
        NotSupportedException or
        PathTooLongException)
    {
        throw new InvalidDataException(
            $"Configured dataRootPath is invalid: {ex.Message}", ex);
    }
}
```

Do not silently reinterpret relative settings paths.

## 10.6 Strict settings parser

Create helper:

```csharp
private AppSettings ReadAndValidate(string path)
```

It must:

1. read non-empty UTF-8 JSON;
2. deserialize;
3. require schema version 1;
4. normalize/validate data root;
5. return valid settings;
6. wrap `JsonException` and path-format errors as `InvalidDataException`.

## 10.7 Recovery algorithm

`LoadOrRecover()`:

```text
if neither settings file exists:
    return default settings, no warning

try primary:
    if valid:
        best-effort refresh backup from primary
        return primary

if primary invalid/missing:
    try backup
    if valid:
        restore primary from backup atomically
        return backup + recovery warning

if primary invalid AND backup invalid/missing:
    throw InvalidDataException with both paths
```

Do **not** silently use default settings when a corrupt primary exists and there is no known-good backup. That could point the user at the wrong library.

## 10.8 Save algorithm

Use atomic writes.

Recommended:

```text
normalize+validate candidate settings
write primary atomically
write backup atomically
```

If primary succeeds but backup fails:

- primary setting is authoritative;
- return a warning to the Settings dialog;
- do not pretend backup is synchronized.

You may introduce:

```csharp
public sealed record SettingsSaveResult(string? Warning);
```

## 10.9 Startup warning

If settings were recovered from backup, show a nonfatal warning after the main window is initialized:

```text
Prompt Helper recovered its data-folder setting from settings.backup.json.
The configured prompt library itself was not modified by this recovery.
```

## 10.10 SettingsDialog path handling

Move **all** normalization and migration work inside the existing `try` block.

Bad:

```csharp
string normalizedCurrent = Path.GetFullPath(...); // outside try
```

Required:

```csharp
private void SaveButton_Click(object sender, RoutedEventArgs e)
{
    try
    {
        string normalizedCurrent = NormalizeForComparison(_currentDataFolder);
        string normalizedSelected = NormalizeForComparison(_selectedDataFolder);
        ...
    }
    catch (...)
    {
        ...
    }
}
```

## 10.11 Same-path Save result

When selected folder is identical to current folder:

```text
no migration
no settings rewrite required
RestartRequired = false
DialogResult = true
close
```

Do not treat pressing Save as Cancel (`DialogResult = false`). This is small but makes semantics/test expectations clearer.

## 10.12 Settings tests

Add all:

```text
missing primary+backup -> default settings
valid primary -> primary returned
valid primary refreshes backup
corrupt primary + valid backup -> recovered from backup
missing primary + valid backup -> recovered from backup
corrupt primary + corrupt backup -> controlled InvalidDataException
relative dataRootPath -> InvalidDataException
absolute custom root roundtrip -> exact absolute path
blank dataRootPath -> null/default mode
primary save succeeds + backup save fails -> warning, primary remains readable
```

Use fault-injecting atomic writer for backup failure.

---

# 11. CRUU2-009 + CRUU2-010 — harden data-folder migration source and topology

## 11.1 Validate source before touching target

Current migration validates existing/copied targets, but it assumes the current source root is still valid.

The source can change externally after startup.

Before creating/copying target files, require:

```text
current source root exists
source library.json exists and is valid current schema
all metadata-referenced prompt files exist and are readable
source path is not a file
```

If source library metadata is missing/corrupt, abort. Do not synthesize target from loose files.

## 11.2 Source validator

Extract a reusable helper:

```csharp
private static LibraryDocument ValidateLibraryRoot(
    string root,
    bool requirePrimaryLibrary)
```

For source:

- require `library.json` primary;
- inspect with `LibraryRepository.InspectAndDeserialize`;
- `LibraryValidator.Validate`;
- verify every referenced prompt file exists;
- open/read each referenced prompt file to verify readability.

For an existing target:

- current primary-only validation remains acceptable;
- also verify referenced files are readable, not only `File.Exists`.

## 11.3 Source changed during copy

The app UI is modal during Settings dialog, so its own mutation cannot race migration. External mutation is still possible.

A full cross-file transactional snapshot is outside scope, but reduce risk:

1. validate source before copy;
2. copy primary metadata;
3. copy referenced prompt files;
4. validate target;
5. optionally re-read source library metadata and compare exact contents/hash to the first read;
6. if source metadata changed during migration, abort and clean created target files.

Recommended simple hash:

```csharp
byte[] sourceLibraryBytes = File.ReadAllBytes(sourceLibraryPath);
byte[] sourceHash = SHA256.HashData(sourceLibraryBytes);
...
byte[] finalSourceHash = SHA256.HashData(File.ReadAllBytes(sourceLibraryPath));
if (!sourceHash.AsSpan().SequenceEqual(finalSourceHash))
{
    throw new IOException("Source library metadata changed during migration. Retry after it is stable.");
}
```

Do not hold a long exclusive lock on the whole source tree.

## 11.4 Copy only metadata-referenced prompts plus safety artifacts

Prefer copying exactly prompt IDs referenced by the source document rather than every `*.md` found in source `prompts`.

Why:

- unknown/orphan files may be deliberate recovery evidence;
- copying all loose files can introduce collisions unrelated to active library metadata.

However existing Prompt Helper intentionally preserves orphan prompt files for recovery safety in some failure cases. Therefore do **not delete** them from source.

For target migration choose one of these exact policies:

```text
ACTIVE prompt files referenced by library.json -> copy to target prompts/
unreferenced orphan .md files -> copy to target recovery/orphan-prompts/ with original filename
```

This preserves evidence without making orphan files appear active.

If implementing this policy would be too invasive for the current recovery conventions, the minimum acceptable fix is:

- retain current copy-all behavior;
- validate active referenced files explicitly;
- never overwrite collisions;
- document that orphan prompt files may also be carried for recovery safety.

For weak-model safety, use the **minimum acceptable fix** unless existing recovery code already has an orphan relocation helper.

## 11.5 Reject self-nested target roots

Do not allow a new data root inside the current managed data root.

Examples to reject:

```text
current = C:\Data\PromptHelper
target  = C:\Data\PromptHelper\New

target  = C:\Data\PromptHelper\prompts

target  = C:\Data\PromptHelper\recovery
```

Implement path comparison after `Path.GetFullPath` and separator normalization.

Helper:

```csharp
private static bool IsStrictDescendant(string candidate, string parent)
{
    string parentWithSep = parent.TrimEnd(
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    string candidateFull = candidate.TrimEnd(
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar);

    return candidateFull.StartsWith(
        parentWithSep,
        StringComparison.OrdinalIgnoreCase);
}
```

Order:

1. same path -> no-op;
2. strict descendant of current -> reject with user-readable error;
3. otherwise proceed.

Do not reject an unrelated existing valid library.

## 11.6 Cleanup directories created by failed migration

Track directories created by the operation.

After removing created files on failure, remove created directories **only if empty**, deepest first.

Never delete a pre-existing directory.

## 11.7 Migration tests

Add:

```text
source library missing -> throws before target mutation
source library corrupt -> throws before target mutation
source referenced prompt missing -> throws before target mutation
source referenced prompt unreadable where testable -> throws
target inside current root -> rejected
same root -> no-op
target existing valid unrelated library -> remains untouched
copy to empty target -> exact active library still loads
collision -> rollback created files
failure cleanup does not delete pre-existing files/directories
source library hash changes during injected copy -> abort + rollback (if injectable)
```

---

# 12. CRUU2-011 — replace broken multi-STA WPF test helper with one persistent STA host

## 12.1 Current defect

The current `Cruu1ComprehensiveVerificationTests.RunOnStaThread` starts a fresh thread per call.

On the first call it may create the process-wide singleton:

```csharp
new Application()
```

Then that thread terminates.

Later test calls run on different STA threads but can still see `Application.Current`, which is dispatcher-affine to the old thread.

This can cause:

- cross-thread access exceptions;
- resources owned by a dead Dispatcher;
- order-dependent tests;
- intermittent failures;
- false confidence if only one UI test happens to execute.

## 12.2 Required design

Create exactly one long-lived STA thread for all WPF test actions in the test process.

The thread:

1. creates `Application` once;
2. sets `ShutdownMode.OnExplicitShutdown`;
3. loads Theme resources once;
4. starts `Dispatcher.Run()`;
5. stays alive until assembly cleanup;
6. executes test delegates via `Dispatcher.Invoke`;
7. shuts down explicitly at the end.

## 12.3 Add `WpfTestHost.cs`

Suggested implementation:

```csharp
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace PromptHelper.Tests;

internal static class WpfTestHost
{
    private static readonly object Sync = new();
    private static readonly ManualResetEventSlim Ready = new(false);

    private static Thread? _thread;
    private static Dispatcher? _dispatcher;
    private static Exception? _startupException;

    public static void Start()
    {
        lock (Sync)
        {
            if (_thread != null)
            {
                return;
            }

            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "PromptHelper.Tests.WPF"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        Ready.Wait();

        if (_startupException != null)
        {
            throw new InvalidOperationException(
                "Failed to initialize WPF test host.",
                _startupException);
        }
    }

    private static void ThreadMain()
    {
        try
        {
            var app = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };

            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/PromptHelper;component/Styles/Theme.xaml",
                    UriKind.Absolute)
            });

            _dispatcher = Dispatcher.CurrentDispatcher;
            Ready.Set();
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            _startupException = ex;
            Ready.Set();
        }
    }

    public static void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Start();
        _dispatcher!.Invoke(action);
    }

    public static T Invoke<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        Start();
        return _dispatcher!.Invoke(func);
    }

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

        thread?.Join(TimeSpan.FromSeconds(10));
    }
}
```

If `Dispatcher.InvokeShutdown()` from another thread is not accepted by the actual runtime, use:

```csharp
dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
```

then join.

## 12.4 Assembly hooks

Add:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PromptHelper.Tests;

[TestClass]
public sealed class TestAssemblyHooks
{
    [AssemblyInitialize]
    public static void Initialize(TestContext _)
    {
        WpfTestHost.Start();
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        WpfTestHost.Stop();
    }
}
```

If MSTest requires a different signature under the installed SDK, use the signature documented by the compiler/test runner. Do not create a second `Application` as a workaround.

## 12.5 Replace every `RunOnStaThread`

Use:

```csharp
WpfTestHost.Invoke(() =>
{
    var dialog = new PromptEditorDialog(...);
    ...
    dialog.Close();
});
```

Close every created Window in `finally` where practical.

## 12.6 UI tests should not run in parallel against the same Application

The dispatcher serializes invokes, but explicitly mark UI-heavy test classes `[DoNotParallelize]` if MSTest parallelization is enabled.

Do not disable parallelization globally unless actual races require it.

---

# 13. CRUU2-012 + CRUU2-013 + CRUU2-014 — make regression tests fail truthfully

## 13.1 Eliminate silent source-path skips

Current pattern:

```csharp
if (File.Exists(xamlPath))
{
    Assert...;
}
```

This is invalid verification. If `xamlPath` is wrong or the file disappears, the test passes without checking anything.

Replace every such pattern.

## 13.2 Add repository-root resolver

Create:

```text
tests/PromptHelper.Tests/RepositoryTestPaths.cs
```

Suggested code:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PromptHelper.Tests;

internal static class RepositoryTestPaths
{
    private static readonly Lazy<string> RootLazy = new(FindRoot);

    public static string Root => RootLazy.Value;

    private static string FindRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);

        while (dir != null)
        {
            bool solution = File.Exists(Path.Combine(dir.FullName, "PromptHelper.slnx"));
            bool project = File.Exists(Path.Combine(
                dir.FullName,
                "src", "PromptHelper", "PromptHelper.csproj"));

            if (solution && project)
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        Assert.Fail(
            $"Could not locate repository root from test base directory: {AppContext.BaseDirectory}");
        throw new InvalidOperationException();
    }

    public static string File(params string[] parts)
    {
        string path = parts.Aggregate(Root, Path.Combine);
        Assert.IsTrue(File.Exists(path), $"Required repository file missing: {path}");
        return path;
    }
}
```

If the method name `File` conflicts with `System.IO.File` readability, call it `RequireFile`.

## 13.3 Replace source-text feature tests with real runtime checks where possible

### Prompt editor wrapping

Keep runtime property checks on actual `PromptEditorDialog`.

Also invoke Save and verify the exact body text before/after wrap toggles.

### Three-column grid

Do not only check resource names.

At minimum parse/inspect `MainWindow.xaml` with required file assertion and verify:

```text
UniformGrid Columns="3"
ToolTipService.InitialShowDelay="500"
ToolTip uses DisplayText
```

Better: construct MainWindow on WPF host and inspect the loaded ItemsPanel template where practical.

### Category wrench

Current source check must become mandatory, not conditional.

Runtime smoke test should construct the button/context menu if feasible.

### Settings footer

Exact `Made by CeeGore` source assertion is fine if required source file lookup cannot silently skip.

### Icon

Use the binary tests from section 5 and MainWindow construction.

## 13.4 Add `MainWindow` construction smoke test

Use real temp repositories and explicit dependencies.

Pseudo-code:

```csharp
[TestMethod]
public void MainWindow_constructs_with_all_required_resources()
{
    WpfTestHost.Invoke(() =>
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        ... initialize service/vm ...

        string settingsPath = Path.Combine(temp.Root, "bootstrap", "settings.json");
        var settingsRepo = new AppSettingsRepository(
            new AtomicTextWriter(),
            settingsPathOverride: settingsPath);

        var window = new MainWindow(
            vm,
            new ClipboardService(),
            settingsRepo,
            new DataFolderMigrationService());

        try
        {
            Assert.IsNotNull(window.Icon);
            Assert.AreSame(vm, window.DataContext);
        }
        finally
        {
            window.Close();
        }
    });
}
```

If `ClipboardService` touches the real clipboard only when Copy is clicked, construction is safe.

## 13.5 Clipboard/recent-history integration test

The ViewModel recency logic is already reasonably covered, but the critical order is:

```text
read prompt
clipboard copy succeeds
THEN record recency
```

For deterministic testing, introduce a tiny interface:

```csharp
public interface IClipboardService
{
    void CopyText(string text);
}
```

Change:

```csharp
public sealed class ClipboardService : IClipboardService
```

Change `MainWindow` field/constructor type to `IClipboardService`.

Production `App` still supplies `new ClipboardService()`.

Test fake:

```csharp
internal sealed class FakeClipboardService : IClipboardService
{
    public string? LastCopiedText { get; private set; }
    public Exception? Failure { get; set; }

    public void CopyText(string text)
    {
        if (Failure != null)
        {
            throw Failure;
        }

        LastCopiedText = text;
    }
}
```

## 13.6 Extract copy operation from event-handler timing

To avoid reflection-testing private async handlers, add an internal helper on `MainWindow` or a small coordinator class.

Preferred small coordinator:

```text
src/PromptHelper/Services/PromptCopyCoordinator.cs
```

```csharp
public sealed class PromptCopyCoordinator
{
    private readonly MainViewModel _viewModel;
    private readonly IClipboardService _clipboard;

    public PromptCopyCoordinator(
        MainViewModel viewModel,
        IClipboardService clipboard)
    {
        _viewModel = viewModel;
        _clipboard = clipboard;
    }

    public string Copy(Guid promptId, string effectiveHeadline)
    {
        string text = _viewModel.GetPromptContent(promptId);
        _clipboard.CopyText(text);
        _viewModel.RecordSuccessfulPromptCopy(
            promptId,
            effectiveHeadline,
            text);
        return text;
    }
}
```

MainWindow event handlers call coordinator then handle `Copied ✓` timing.

Do not move UI `MessageBox` or `Task.Delay` into the coordinator.

## 13.7 Coordinator tests

### Success records recency

```csharp
[TestMethod]
public void CopyCoordinator_success_copies_full_text_then_records_recent()
{
    ... create prompt with multiline content ...
    var fake = new FakeClipboardService();
    var coordinator = new PromptCopyCoordinator(vm, fake);

    coordinator.Copy(prompt.Id, card.PreviewTitle);

    Assert.AreEqual(fullBody, fake.LastCopiedText);
    Assert.AreEqual(prompt.Id, vm.RecentPrompts[0].Id);
}
```

### Clipboard failure does not record recency

```csharp
[TestMethod]
public void CopyCoordinator_clipboard_failure_does_not_change_recent_history()
{
    ...
    fake.Failure = new InvalidOperationException("clipboard busy");

    Assert.Throws<InvalidOperationException>(() =>
        coordinator.Copy(prompt.Id, card.PreviewTitle));

    Assert.AreEqual(0, vm.RecentPrompts.Count);
}
```

### Quick copy reads current full body

```text
copy prompt once -> recents entry exists
edit body through service/vm
copy same prompt through coordinator using recent ID
assert fake clipboard contains NEW full body, not excerpt and not stale body
```

## 13.8 Tooltip timing still requires manual UI validation

Automated XAML assertion can prove `InitialShowDelay=500` was configured, but it does not prove Windows rendered timing accurately.

Keep manual test:

```text
hover < 0.5 s -> no tooltip
hover > 0.5 s -> tooltip appears
line breaks preserved
long prompt scrolls
move pointer away -> tooltip closes normally
```

---

# 14. CRUU2-015 — add Windows CI as the objective build/test gate

## 14.1 Why

The audited implementation commit has no attached status checks.

A WPF `.NET 10` project needs a Windows runner to validate:

- XAML compilation;
- pack resources;
- WPF test assembly;
- current test suite;
- icon project configuration.

## 14.2 Add workflow

Create:

```text
.github/workflows/windows-ci.yml
```

Use:

```yaml
name: Windows CI

on:
  push:
    branches: [ main ]
  pull_request:

jobs:
  build-test:
    runs-on: windows-latest

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
        run: dotnet test PromptHelper.slnx -c Release --no-build --logger "trx;LogFileName=test-results.trx"

      - name: Upload test results on failure
        if: failure()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: '**/TestResults/**/*.trx'
          if-no-files-found: ignore
```

## 14.3 Respect `global.json`

The repository currently requests SDK `10.0.100` with `latestFeature` roll-forward.

`setup-dotnet` `10.0.x` is compatible with that intent.

Do not pin an unrelated preview SDK.

## 14.4 CI must fail on missing icon

Because section 5 makes the ICO project references unconditional and tests require actual SVG/ICO files, deleting the icon must fail CI.

## 14.5 Do not add flaky real clipboard/UI timing to CI

CI may use fake clipboard tests and deterministic WPF construction.

Do not depend on:

- actual user clipboard ownership;
- visible taskbar;
- mouse hover timing;
- Explorer shell cache.

Those remain manual Windows acceptance checks.

---

# 15. CRUU2-017 — update stale README and German usage guide

## 15.1 README currently wrong/incomplete

Current README says user data is simply:

```text
%LOCALAPPDATA%\PromptHelper
```

After `cruu1`, that is only the **default** library location. The library can now live elsewhere while fixed bootstrap settings remain in LocalAppData.

## 15.2 README replacement for User data section

Use text equivalent to:

```markdown
## User data

By default, Prompt Helper stores its library in:

`%LOCALAPPDATA%\PromptHelper`

The data folder can be changed from the wrench **Tools and settings** dialog. A custom data folder becomes active after restarting Prompt Helper.

The small bootstrap settings file remains at:

`%LOCALAPPDATA%\PromptHelper\settings.json`

Prompt bodies remain local `.md` files; Prompt Helper does not upload prompts or usage data.
```

After settings-backup work, also mention:

```text
settings.backup.json
```

## 15.3 German usage guide is materially stale

`Prompt_Helper_Nutzungsguide_DE_v2_FINAL.md` still describes:

- top-right `?` button;
- Help dialog rather than Tools & Settings;
- fixed LocalAppData library path;
- category `✎` and `×` buttons directly on each category;
- old prompt-card presentation with a large read-only scrolling body field;
- no headline field;
- no wrap checkbox;
- no three-card grid;
- no delayed full-body tooltip;
- no recent-copy row;
- no data-folder migration/restart behavior.

These are user-facing regressions in documentation.

## 15.4 Required guide changes

Update at least these sections:

### “Das Wichtigste in 60 Sekunden”

Add:

```text
optional Headline
Wrap long lines checkbox
recent copies row
wrench settings
custom data folder + restart
```

### Data-folder sections

Explain:

```text
default library = %LOCALAPPDATA%\PromptHelper
custom library = user-selected path
bootstrap settings remain in %LOCALAPPDATA%\PromptHelper
changing folder copies current data only when target has no existing valid library
existing valid Prompt Helper target is switched to without overwrite
old source remains as safety copy
restart required
```

After CRUU2-007, describe `settings.backup.json` recovery.

### Main window

Replace `?` with wrench `🔧` and title “Tools and settings”.

Describe the recent-copy row:

```text
starts empty every launch
max 3
newest first
copying fourth evicts oldest
copying same prompt moves it to first
not persisted
```

### Categories

Replace direct:

```text
✎
×
```

with:

```text
🔧 -> Rename / Delete
```

### Prompts

Describe:

```text
3 cards per row
short clipped preview
hover about 0.5 s for full prompt tooltip
full prompt is still copied regardless of short visual preview
```

### Prompt editor

Describe:

```text
Headline <optional>
blank => automatic headline from body
Wrap long lines => visual only
wrap checkbox never changes saved text
```

Clarify automatic-title semantics after CRUU2-005:

```text
an untouched automatically prefilled headline remains automatic
editing the headline explicitly pins it as custom
clearing it returns to automatic
```

### Backup

Do not tell custom-root users to back up only LocalAppData.

Guide must say:

```text
Back up the CURRENT DATA FOLDER shown in Tools & Settings.
If a custom data root is used, that folder is the prompt-library backup target.
The LocalAppData settings files only remember where that library is located.
```

### Downgrade warning

Add:

```text
After using custom prompt headlines, do not modify the same library with an older Prompt Helper binary that predates headline support.
```

## 15.5 Documentation consistency test

A source-text test can at least reject known obsolete UI claims.

Example:

```csharp
[TestMethod]
public void UsageGuide_does_not_document_removed_help_and_category_controls()
{
    string guidePath = RepositoryTestPaths.RequireFile(
        "Prompt_Helper_Nutzungsguide_DE_v2_FINAL.md");

    string guide = File.ReadAllText(guidePath);

    Assert.IsFalse(guide.Contains("Der `?`-Button öffnet den Hilfe-Dialog."));
    StringAssert.Contains(guide, "Tools");
    StringAssert.Contains(guide, "Headline");
    StringAssert.Contains(guide, "Wrap long lines");
}
```

Do not overfit the entire guide to fragile exact wording. The main validation is editorial/manual review.

---

# 16. File-by-file implementation map

A weak implementation model should use this map.

## 16.1 Must modify

```text
src/PromptHelper/PromptHelper.csproj
src/PromptHelper/App.xaml.cs
src/PromptHelper/MainWindow.xaml.cs
src/PromptHelper/Views/PromptEditorDialog.xaml
src/PromptHelper/Views/PromptEditorDialog.xaml.cs
src/PromptHelper/Views/SettingsDialog.xaml.cs
src/PromptHelper/Services/PromptLibraryService.cs
src/PromptHelper/Services/AppSettingsRepository.cs
src/PromptHelper/Services/DataFolderMigrationService.cs
README.md
Prompt_Helper_Nutzungsguide_DE_v2_FINAL.md
tests/PromptHelper.Tests/Cruu1ComprehensiveVerificationTests.cs
tests/PromptHelper.Tests/PromptLibraryServiceTests.cs
tests/PromptHelper.Tests/MainViewModelTests.cs
tests/PromptHelper.Tests/AppSettingsRepositoryTests.cs
tests/PromptHelper.Tests/DataFolderMigrationServiceTests.cs
tools/GenerateAppIcon.ps1
```

## 16.2 Must add

```text
src/PromptHelper/Assets/PromptHelperLogo.svg      # real supplied asset
src/PromptHelper/Assets/PromptHelper.ico          # generated asset
src/PromptHelper/Services/DataRootBootstrapValidator.cs
src/PromptHelper/Services/ConfiguredDataFolderUnavailableException.cs
src/PromptHelper/Services/IClipboardService.cs
src/PromptHelper/Services/PromptCopyCoordinator.cs
tests/PromptHelper.Tests/WpfTestHost.cs
tests/PromptHelper.Tests/TestAssemblyHooks.cs
tests/PromptHelper.Tests/RepositoryTestPaths.cs
tests/PromptHelper.Tests/IconAssetTests.cs
tests/PromptHelper.Tests/FakeClipboardService.cs
.github/workflows/windows-ci.yml
```

If project conventions prefer exception types under another folder, keep namespace consistent, but do not bury the exception as a private nested type if tests need to assert it.

## 16.3 May modify

```text
src/PromptHelper/Models/OperationResults.cs
```

if adding:

```csharp
SettingsLoadResult
SettingsSaveResult
```

## 16.4 Do not modify without a newly discovered compiler requirement

```text
LibraryDocument.CurrentSchemaVersion
prompt body file format
category model
move/duplicate semantics
recent-history persistence policy
```

---

# 17. Ordered implementation phases

Follow this order. Do not randomly patch files.

## Phase A — baseline and evidence capture

1. Confirm HEAD.
2. Record `git status`.
3. Do not delete existing tests.
4. Run baseline Windows build/test if environment supports it.
5. If baseline cannot run, record `UNVERIFIED_BASELINE`, not PASS.
6. Confirm the real logo SVG is available before claiming icon completion.

Expected commands:

```powershell
git status --short
git rev-parse HEAD
dotnet --info
dotnet restore PromptHelper.slnx
dotnet build PromptHelper.slnx -c Release --no-restore
dotnet test PromptHelper.slnx -c Release --no-build
```

## Phase B — headline data integrity first

Implement in order:

1. `NormalizeAndValidatePromptTitle`.
2. reorder CreatePrompt validation before body creation;
3. legacy service overload preserves title;
4. legacy ViewModel overload preserves title;
5. automatic-title editor touched-state semantics;
6. failed-save state preservation;
7. tests for all headline cases.

Run focused tests before continuing.

## Phase C — settings/bootstrap safety

1. settings strict path validation;
2. settings backup + recovery;
3. `SettingsLoadResult`/warning;
4. custom configured-root validator;
5. App startup integration;
6. Settings dialog exception-scope cleanup;
7. settings tests.

## Phase D — migration hardening

1. source validation;
2. reject nested target;
3. target readability validation;
4. rollback/empty-directory cleanup;
5. migration tests.

## Phase E — icon completion

Only if real SVG exists:

1. add source SVG;
2. improve generator;
3. generate ICO;
4. inspect output;
5. remove project `Exists` conditions;
6. add binary icon tests;
7. runtime MainWindow construction test.

If real SVG absent, mark only this phase blocked and proceed with phases F-H.

## Phase F — rebuild test infrastructure

1. add persistent WPF STA host;
2. add repository-root resolver;
3. remove every silent file-existence skip;
4. add fake clipboard interface/coordinator;
5. add MainWindow smoke test;
6. add copy failure/success integration tests;
7. run entire suite repeatedly.

Recommended repeated test loop:

```powershell
1..5 | ForEach-Object {
    dotnet test PromptHelper.slnx -c Release --no-build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

This is specifically useful for catching WPF test-order/threading instability.

## Phase G — docs

Update README + German guide after behavior is final.

## Phase H — CI + final regression

1. add Windows workflow;
2. run local Release build/test;
3. push only when authorized;
4. confirm CI green;
5. manually run GUI acceptance matrix;
6. only then declare clean.

---

# 18. Focused test matrix

The final suite must cover all of these.

## 18.1 Headlines

```text
[ ] old schema-v1 library without title loads
[ ] explicit custom title persists
[ ] title trims
[ ] blank title becomes null
[ ] invalid control title rejected as normal validation error
[ ] invalid create leaves no body file
[ ] invalid edit changes neither body nor metadata
[ ] duplicate preserves custom title
[ ] move preserves custom title
[ ] clone preserves custom title
[ ] metadata commit failure restores old body
[ ] legacy EditPrompt overload preserves title
[ ] automatic fallback prefill untouched remains Title null
[ ] auto body first-line edit changes fallback after save
[ ] auto headline explicitly edited becomes custom
[ ] custom headline untouched remains custom
[ ] clearing custom title returns auto
[ ] failed save retry preserves editor body and headline state
```

## 18.2 Wrapping

```text
[ ] default unchecked
[ ] NoWrap + horizontal Auto
[ ] checked -> Wrap + horizontal Disabled
[ ] unchecked again restores NoWrap/Auto
[ ] body bytes/text unchanged after toggles
[ ] CRLF/LF/tabs preserved
[ ] Save result exactly equals original text when only wrap changed
```

## 18.3 Recent copies

```text
[ ] new session starts empty
[ ] first copy -> [A]
[ ] A,B,C -> [C,B,A]
[ ] fourth -> oldest evicted
[ ] recopy existing -> move to first, no duplicate
[ ] failed clipboard -> no recent mutation
[ ] quick-copy failure -> no recency mutation
[ ] quick copy copies full prompt body
[ ] edit recent prompt updates headline/excerpt without reordering
[ ] delete recent prompt removes it
[ ] move recent prompt keeps it
[ ] duplicate does not auto-add
[ ] navigation does not clear recents
[ ] second MainViewModel/new app session starts empty
```

## 18.4 Data-folder settings

```text
[ ] missing settings -> default root
[ ] settings primary valid -> use it
[ ] primary corrupt + backup valid -> recover
[ ] primary missing + backup valid -> recover
[ ] both corrupt -> controlled failure
[ ] relative data root rejected
[ ] blank data root -> default mode
[ ] custom root missing -> NO directory creation / NO default library creation
[ ] custom root empty -> controlled unavailable error
[ ] custom root backup-only -> normal library recovery allowed
[ ] same-folder Settings Save -> success/no restart/no migration
[ ] custom Save -> restart required
[ ] Cancel -> settings unchanged
```

## 18.5 Migration

```text
[ ] same path no-op
[ ] empty target gets library + prompts + backup/recovery as designed
[ ] source remains unchanged
[ ] .app.lock not copied
[ ] initialization marker not copied
[ ] settings files not copied into custom root
[ ] existing valid target not overwritten
[ ] corrupt target rejected
[ ] target missing active prompt rejected
[ ] source missing primary rejected before target mutation
[ ] source corrupt rejected before target mutation
[ ] source missing active prompt rejected before target mutation
[ ] nested target rejected
[ ] collisions never overwrite
[ ] failure cleans only files/directories created by operation
[ ] copied target passes normal LibraryStartupService load
```

## 18.6 Icon

```text
[ ] real SVG exists
[ ] real ICO exists
[ ] ICO non-empty
[ ] ICO header valid
[ ] 16 frame
[ ] 24 frame
[ ] 32 frame
[ ] 48 frame
[ ] 64 frame
[ ] 128 frame
[ ] 256 frame
[ ] all frames square
[ ] csproj requires icon unconditionally
[ ] MainWindow constructs and Icon != null
[ ] Release EXE shows icon in Explorer
[ ] running app shows icon in taskbar
[ ] transparency correct
[ ] non-square source not distorted
```

## 18.7 GUI regression

```text
[ ] min window 900x600 usable
[ ] 3 cards per row
[ ] 7 cards -> 3/3/1 arrangement
[ ] all Delete/Edit/Move/Copy buttons clickable
[ ] tooltip does not show before ~0.5 s
[ ] tooltip shows after ~0.5 s
[ ] tooltip contains full prompt body
[ ] hard line breaks visible in tooltip
[ ] long tooltip scrolls
[ ] category wrench opens Rename/Delete menu
[ ] rename still validates duplicate names
[ ] non-empty category deletion remains blocked
[ ] top wrench opens Tools & Settings
[ ] Made by CeeGore visible
[ ] folder picker is native OpenFolderDialog
[ ] quick bar height remains compact
[ ] 0/1/2/3 recent tiles layout correctly
[ ] quick Copy keyboard focus visible
```

---

# 19. Full regression matrix for pre-existing behavior

Do not test only new features.

## 19.1 Categories

```text
[ ] create Home category
[ ] create nested category
[ ] duplicate sibling rejected case-insensitively
[ ] same name under different parent allowed
[ ] rename
[ ] rename duplicate rejected
[ ] delete empty
[ ] block delete with prompt
[ ] block delete with child
[ ] category save failure leaves in-memory state unchanged
[ ] backup failure returns warning but primary commits
[ ] deep hierarchy breadcrumb still correct
[ ] destination labels remain globally unique
```

## 19.2 Prompt CRUD

```text
[ ] create Home prompt
[ ] create category prompt
[ ] primary metadata failure cleans new body file
[ ] cleanup failure preserves only orphan evidence
[ ] backup failure commits with warning
[ ] edit body
[ ] missing body edit fails safely
[ ] delete success
[ ] delete metadata failure preserves prompt
[ ] delete backup failure preserves file for recovery
[ ] delete file failure leaves recoverable orphan + warning
[ ] move category
[ ] move Home
[ ] move same category no-op
[ ] duplicate prompt
[ ] duplicate unavailable prompt rejected
[ ] unavailable prompt remains movable where intended
```

## 19.3 Startup/recovery

```text
[ ] clean default first run
[ ] interrupted first initialization recovery
[ ] zero-byte primary + valid backup recovery
[ ] whitespace primary + valid backup recovery
[ ] corrupt primary + valid backup recovery
[ ] corrupt primary + corrupt backup hard stop
[ ] future library schema hard stop
[ ] valid primary + backup sync failure warning
[ ] unknown loose prompt files prevent destructive reinitialization
[ ] single-instance lock still works in active root
```

## 19.4 Sort-order edge cases

```text
[ ] category long overflow resequence
[ ] prompt long overflow resequence
[ ] move to destination does not inflate sort order from source
```

---

# 20. Manual fault-injection scenarios

Use temp test data, never a real user library.

## 20.1 Corrupt settings

```text
1. Configure custom root.
2. Confirm settings backup exists.
3. Corrupt settings.json.
4. Launch.
5. Expect automatic recovery from settings.backup.json + warning.
6. Confirm custom library loads unchanged.
```

## 20.2 Both settings corrupt

```text
1. Corrupt both settings files.
2. Launch.
3. Expect controlled settings-specific startup error.
4. No new default library should be silently created because configuration is ambiguous.
```

## 20.3 Custom root removed

```text
1. Configure custom root.
2. Close app.
3. Rename that root externally.
4. Launch.
5. Expect configured-folder-unavailable error.
6. Confirm original configured path was NOT recreated.
7. Restore folder name.
8. Relaunch and confirm library returns.
```

## 20.4 Invalid pasted headline

```text
1. Create Prompt.
2. Paste headline containing newline/tab/control character.
3. Save.
4. Expect normal Save Prompt Error.
5. Editor text preserved for correction.
6. Confirm no orphan .md appeared.
7. App remains open.
```

## 20.5 Migration source disappears

With app open on disposable test root:

```text
1. Open Settings dialog.
2. Externally rename/remove source library.json.
3. Select a new empty target.
4. Save.
5. Expect migration refusal.
6. Target must not become active.
7. settings.json must remain unchanged.
```

---

# 21. Weak-model traps — explicitly forbidden shortcuts

The implementation model must not do any of these.

1. Do not generate a fake logo because SVG is missing.
2. Do not remove `MainWindow.Icon` merely to make startup work while leaving icon feature incomplete.
3. Do not keep conditional `ApplicationIcon` after the required ICO exists.
4. Do not change schema version to 2 in this repair round.
5. Do not serialize automatic fallback text into every old prompt.
6. Do not “fix” automatic-title issue by making edit headline blank instead of prefilled.
7. Do not let untouched automatic prefill silently become custom.
8. Do not make two-argument EditPrompt pass null.
9. Do not write prompt body before validating candidate metadata.
10. Do not catch all exceptions and hide faults.
11. Do not silently replace corrupt settings with defaults when custom library location is unknown.
12. Do not silently create a new library inside a missing configured custom root.
13. Do not delete the old source folder after migration.
14. Do not overwrite an existing target library.
15. Do not copy `.app.lock` or initialization marker.
16. Do not accept a strict descendant of current root as migration target.
17. Do not leave `if (File.Exists(...)) { Assert... }` verification tests.
18. Do not create a WPF `Application` on multiple short-lived STA threads.
19. Do not use real Windows clipboard in deterministic CI tests when a fake can prove logic.
20. Do not claim tooltip timing passed from static XAML alone.
21. Do not claim taskbar icon passed from an ICO header unit test.
22. Do not skip existing regression tests to make new tests green.
23. Do not delete recovery/fault-injection tests.
24. Do not update documentation before behavior is stable, then forget to re-review it.
25. Do not commit generated test-output directories.
26. Do not weaken library validator to accept invalid title data.
27. Do not use Markdown rendering or HTML in the tooltip; plain full text is sufficient.
28. Do not add a runtime dependency on ImageMagick.
29. Do not add cloud/network functionality.
30. Do not report PASS when build/test was not actually executed.

---

# 22. Exact completion checklist per finding

## CRUU2-001 / 018 icon

```text
[ ] real SVG committed
[ ] ICO generated and committed
[ ] generator square-pads correctly
[ ] required frames present
[ ] csproj unconditional
[ ] runtime MainWindow loads
[ ] Explorer/taskbar manual pass
```

## CRUU2-002 / 006 prompt validation

```text
[ ] explicit title validation helper
[ ] candidate validated before body create
[ ] invalid create leaves zero orphan files
[ ] invalid edit leaves body/title unchanged
[ ] bad title handled by normal save error path
```

## CRUU2-003 custom-root startup

```text
[ ] explicit custom root never auto-initializes if missing
[ ] missing root is not created
[ ] empty custom root rejected
[ ] backup-only custom root allowed
```

## CRUU2-004 legacy edit

```text
[ ] service legacy overload preserves title
[ ] ViewModel legacy overload preserves title
[ ] tests prove both
```

## CRUU2-005 auto headline

```text
[ ] visible prefill retained
[ ] untouched auto remains null
[ ] explicit edit becomes custom
[ ] clearing returns auto
[ ] retry preserves state
```

## CRUU2-007 / 008 / 016 settings

```text
[ ] settings backup
[ ] recovery algorithm
[ ] relative paths rejected
[ ] invalid settings controlled
[ ] normalization in try
[ ] same-path Save returns success/no restart
```

## CRUU2-009 / 010 migration

```text
[ ] source validated first
[ ] active source prompts readable
[ ] target descendant rejected
[ ] target validation retained
[ ] rollback safe
```

## CRUU2-011 / 012 / 013 / 014 tests

```text
[ ] one persistent STA WPF host
[ ] source path resolver fails loudly
[ ] no silent conditional assertions
[ ] real icon tests
[ ] MainWindow construction test
[ ] fake clipboard integration tests
[ ] full suite stable repeatedly
```

## CRUU2-015 CI

```text
[ ] windows-ci.yml committed
[ ] Release build green
[ ] full tests green
```

## CRUU2-017 docs

```text
[ ] README current
[ ] DE guide current
[ ] no old ? Help instructions
[ ] custom data root documented
[ ] quick bar/headlines/wrap/grid/tooltip documented
[ ] backup instructions use current data folder
```

---

# 23. Final command gate

Run on Windows from repository root.

## 23.1 Clean

```powershell
dotnet clean PromptHelper.slnx -c Release
```

## 23.2 Restore

```powershell
dotnet restore PromptHelper.slnx
```

## 23.3 Build

```powershell
dotnet build PromptHelper.slnx -c Release --no-restore
```

Required:

```text
exit code 0
0 build errors
```

Warnings must be reviewed. Do not automatically ignore new warnings introduced by repairs.

## 23.4 Test

```powershell
dotnet test PromptHelper.slnx -c Release --no-build --logger "trx;LogFileName=full-regression.trx"
```

Required:

```text
exit code 0
0 failed tests
0 aborted tests
```

Skipped tests must be individually justified. Do not use skip as a replacement for fixing WPF test infrastructure.

## 23.5 Repeat to expose order/thread flakiness

```powershell
1..5 | ForEach-Object {
    Write-Host "Regression pass $_"
    dotnet test PromptHelper.slnx -c Release --no-build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

## 23.6 Run app

```powershell
dotnet run --project src/PromptHelper/PromptHelper.csproj -c Release
```

Execute manual matrix from sections 18 and 20.

---

# 24. Final acceptance criteria

The implementation is accepted only if **all** are true:

```text
1. no BLOCKER/HIGH/MEDIUM issue in section 4 remains open
2. required real logo SVG and ICO exist
3. application starts with all WPF resources
4. Release build passes on Windows
5. complete automated suite passes
6. suite passes repeatedly without STA/order flakiness
7. GitHub Windows CI is green
8. invalid headline cannot orphan a prompt body
9. legacy edits cannot erase custom titles
10. untouched automatic headline remains automatic
11. corrupt settings recover from backup when possible
12. missing custom root never silently creates defaults
13. migration validates source and rejects nested targets
14. quick-copy success/failure ordering is proven
15. Explorer/taskbar icon manually verified
16. 3-column layout and 0.5s tooltip manually verified
17. Settings/category wrench flows manually verified
18. README and DE usage guide describe current application
19. no existing recovery/CRUD regression fails
20. all unexecuted checks are explicitly marked UNVERIFIED rather than PASS
```

Final status vocabulary:

```text
PASS
PASS WITH EXPLICIT NON-BLOCKING LIMITATIONS
FAIL
```

Do not use PASS WITH LIMITATIONS for a missing required icon asset, failing build, failing test, data-integrity defect, or missing custom-root safety. Those are FAIL conditions.

---

# 25. Suggested final implementation-agent prompt

The following can be given directly to a weak coding model together with this file:

```text
You are implementing the complete repair plan in cruu2.md for the current Prompt Helper repository.

Treat cruu2.md as authoritative for this repair round. Read it fully before editing. Do not redesign product behavior. Preserve the explicit decisions in sections 2 and 21.

Work in the exact phase order from section 17. Resolve every open issue in section 4. Implement all required tests and helper infrastructure, not only production code.

Critical rules:
- never invent the Prompt Helper logo SVG; if the real supplied SVG is absent, report MISSING_REQUIRED_ASSET for only the icon subtask and continue all other fixes;
- never claim an unrun build/test/manual check passed;
- never silence a test by skipping it or by wrapping assertions in File.Exists;
- never let a configured missing custom data root initialize a fresh library silently;
- never write a new prompt body before its metadata candidate is valid;
- preserve automatic-title mode when the automatic prefill was not edited;
- preserve existing custom titles through legacy two-argument edit overloads;
- keep schema version 1;
- keep recent copies session-only/max-three/newest-first;
- keep the 3-column non-virtualized card layout.

After implementation:
1. run clean/restore/Release build/full test;
2. run the full suite five consecutive times;
3. fix every failure and repeat until clean;
4. run the manual Windows acceptance matrix where the environment permits it;
5. update README and the German usage guide to match the final behavior;
6. report each CRUU2 issue ID as FIXED, BLOCKED with exact reason, or still OPEN;
7. provide exact command outputs/counts for executed build/tests and mark non-executed manual checks UNVERIFIED.

Do not stop after making code compile. Completion means every applicable acceptance criterion in cruu2.md is satisfied.
```

---

# 26. Audit conclusion

The second regression audit found meaningful open issues that the first implementation and its new test suite did not fully cover.

The most important newly exposed problems are:

```text
- missing required icon assets despite active XAML wiring;
- prompt-file orphan risk from validation-after-write ordering;
- silent fresh-library initialization if a configured custom root disappears;
- automatic-headline mode being silently frozen into a custom title;
- legacy body-only edit overload erasing custom titles;
- weak settings durability/recovery;
- migration trusting the source without revalidation;
- invalid WPF Application/STA test lifecycle;
- tests that can silently pass when source files are missing;
- no objective Windows CI gate;
- stale end-user documentation.
```

Once all fixes and gates in this file are completed, run one more independent final audit against the resulting repository before release acceptance.
