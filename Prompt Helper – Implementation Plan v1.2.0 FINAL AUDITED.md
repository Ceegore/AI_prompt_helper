# Prompt Helper — Implementation Plan v1.2.0

**Status:** FINAL AUDITED / IMPLEMENTATION-LOCKED  
**Product:** Prompt Helper  
**Target:** local Windows desktop utility  
**Implementation:** C# + WPF + .NET 10  
**Primary implementation executor:** comparatively weak AI coding agent  
**Document role:** sole authoritative MVP implementation plan

---

# 0. Purpose

Prompt Helper is a small local Windows application for storing, organizing, editing, moving, duplicating and quickly copying reusable AI prompts.

The intended workflow is:

```text
Start Prompt Helper
→ select category
→ optionally navigate through subcategories
→ locate prompt
→ click Copy
→ paste into the target AI tool
```

The application deliberately does **not** contain AI functionality itself.

Its job is prompt organization and clipboard access.

The implementation must be:

- simple,
- reliable,
- pleasant to use,
- visually modern,
- completely local,
- dependency-minimal,
- easy to inspect,
- resistant to avoidable data loss,
- suitable for implementation by a weak AI coding model.

This document resolves all meaningful product, architecture, persistence, failure-handling and UI decisions that should reasonably be made before coding.

---

# 1. Final audit changes from v1.1.0

The following remaining defects were found and are fixed by this version.

## 1.1 Missing JSON properties could previously masquerade as valid defaults

C# property initializers such as:

```csharp
public List<CategoryRecord> Categories { get; set; } = [];
```

would normally mean JSON such as:

```json
{}
```

could deserialize with apparently valid default values.

That is unsafe for persistent metadata.

The final model therefore marks **every persisted JSON field as required** using:

```csharp
[JsonRequired]
```

and additionally validates semantic values.

A truncated library must never silently become an empty library.

---

## 1.2 Empty GUIDs are now invalid

Missing GUID fields deserialize to:

```text
00000000-0000-0000-0000-000000000000
```

for value-type properties unless the property itself is required.

Even with required JSON fields, empty GUIDs are invalid application IDs.

Therefore:

```text
Category.Id == Guid.Empty → invalid

Prompt.Id == Guid.Empty → invalid
```

---

## 1.3 Future-schema detection happens before v1 deserialization

A future schema might no longer contain the v1 properties:

```text
categories
prompts
```

If future metadata were deserialized into the v1 model first, strict required-property validation could incorrectly classify it as corruption and then restore an older backup.

That could destroy newer data.

The final repository therefore:

```text
read raw JSON
↓
inspect schemaVersion first
↓
if schemaVersion > 1:
    stop with UnsupportedLibrarySchemaException
↓
only then deserialize as schema v1
```

This remains true even if the remainder of the future JSON is incompatible with v1.

---

## 1.4 schemaVersion must be unambiguous

The raw JSON reader requires exactly one case-insensitive top-level property named:

```text
schemaVersion
```

It must contain an integer.

Multiple conflicting `schemaVersion` properties are rejected as invalid.

---

## 1.5 Backup failure can no longer escape after primary commit

`library.json` is the metadata commit point.

Once the primary write succeeded, a later backup write failure must never propagate as though the complete operation failed.

Otherwise a caller could perform rollback cleanup even though primary metadata had already committed.

Therefore:

```text
primary write
→ success = logical commit

backup write
→ best effort only
→ any backup exception becomes warning
```

This rule is mandatory.

---

## 1.6 Physical delete cleanup can no longer invalidate a logical delete

After metadata and backup both commit, physical `.md` deletion is cleanup.

Any physical deletion failure therefore becomes:

```text
successful logical delete
+
warning
+
orphan .md retained
```

It must never make the UI claim that the delete failed.

---

## 1.7 First-run resumption is more conservative

If:

```text
initializing.marker
```

exists and default-ID `.md` files already exist, Prompt Helper now verifies that their contents **exactly match** the expected built-in defaults.

If the content differs:

```text
stop
preserve data
do not overwrite
```

---

## 1.8 Application lock no longer hides arbitrary I/O failures

A second process normally receives Windows:

```text
ERROR_SHARING_VIOLATION
or
ERROR_LOCK_VIOLATION
```

Only those errors are interpreted as:

```text
another instance holds the library
```

Unrelated `IOException`s propagate as real startup failures.

---

## 1.9 UI implementation phases are now executable in sequence

The previous sequence removed `StartupUri` before final application composition existed.

That would have made intermediate GUI testing impossible.

The final sequence introduces application composition as soon as:

- repositories,
- startup service,
- business service,
- ViewModel,
- styling

exist.

Every subsequent GUI phase therefore ends in an actually runnable application.

---

## 1.10 Missing prompts may be moved but not duplicated

When metadata exists but `.md` content is unavailable:

```text
Delete → enabled
Move   → enabled
Edit   → disabled
Copy to Clipboard → disabled
Duplicate via "Copy instead of move" → disabled
```

This prevents an obviously doomed duplicate operation.

---

## 1.11 Delete dialogs now match specified button semantics

Prompt/category deletion uses a small custom confirmation dialog with:

```text
Cancel
Delete
```

rather than a standard `MessageBox` containing:

```text
OK
Cancel
```

---

## 1.12 Category cards no longer imply nested Button controls

A category card is:

```text
Border
└ Grid
  ├ Open-category Button
  └ action Buttons
```

The Rename/Delete buttons are not children of another Button.

---

## 1.13 Destination display collisions are handled

Destination selection internally uses category GUIDs, so routing was already safe.

However paths such as:

```text
Home
```

or:

```text
A > B
```

could theoretically be visually ambiguous if category names themselves produced identical rendered paths.

When two destination display paths collide case-insensitively, category options receive a short GUID suffix.

Example:

```text
Home
Home  [4a23c8f1]
```

The root `Home` remains unsuffixed.

---

# 2. Acceptance standard

This plan is accepted when no remaining concrete:

- implementation blocker,
- internal contradiction,
- unhandled major failure mode,
- unresolved meaningful architecture decision,
- data-integrity defect,
- required UX decision,
- important testing omission

can be demonstrated from the available information.

This does not claim mathematical impossibility of all future bugs.

Actual compiled implementation remains subject to the mandatory phase tests in this document.

---

# 3. Technology lock

Use:

```text
Language
C#

Desktop framework
WPF

Runtime family
.NET 10

Target framework
net10.0-windows

JSON
System.Text.Json

Prompt content
plain .md files

Metadata
local JSON

Testing
MSTest

Architecture
small pragmatic MVVM/service architecture
```

Do not substitute another stack.

---

# 4. SDK selection

Repository root contains:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

Meaning:

```text
minimum SDK family anchor:
10.0.100

allowed:
later stable .NET 10 feature bands and patches

not allowed:
.NET 11
prerelease SDKs
.NET 9 fallback
```

Before Phase 001:

```powershell
dotnet --info
```

must show a stable .NET 10 SDK.

Do not block merely because one exact patch version such as `10.0.302` is absent.

---

# 5. Framework lifecycle rule

Use a currently supported stable .NET 10 servicing release for final publishing.

Do not deliberately publish the self-contained executable using an outdated known-vulnerable servicing runtime if a newer supported .NET 10 SDK is installed.

---

# 6. Windows platform target

Primary supported QA target:

```text
Windows 11 x64
```

Final release QA must include Windows 11.

Windows 10:

```text
officially supported only where the chosen .NET 10 runtime and the
specific Windows edition remain within Microsoft's support matrix
```

For ordinary out-of-support Windows 10 Home/Pro installations:

```text
best-effort compatibility only
```

The application may be smoke-tested there if useful, but the plan must not claim official Microsoft support.

---

# 7. Publishing target

Final distribution:

```text
win-x64
self-contained
```

The end user does not need to install .NET separately.

Development still requires a .NET 10 SDK.

---

# 8. Application dependency policy

The shipping application has:

```text
0 additional NuGet packages
```

Allowed framework functionality:

- WPF,
- System.Text.Json,
- standard .NET BCL.

The test project uses:

```text
MSTest.Sdk 4.3.3
```

as a **test-only** dependency.

Test packages are not part of the published Prompt Helper application.

---

# 9. Do not use experimental WPF ThemeMode

Do not use:

```text
Application.ThemeMode
Window.ThemeMode
ThemeMode.Light
ThemeMode.Dark
```

The UI uses locally defined stable WPF styles instead.

---

# 10. Explicitly forbidden architecture changes

Do not introduce:

- WinUI,
- Avalonia,
- Electron,
- Tauri,
- MAUI,
- Windows Forms,
- React,
- SQLite,
- Entity Framework,
- LiteDB,
- CommunityToolkit.Mvvm,
- ReactiveUI,
- dependency-injection containers,
- MediatR,
- MahApps,
- MaterialDesignInXaml,
- AvalonEdit,
- Markdig,
- Serilog,
- telemetry frameworks,
- analytics libraries.

These would add complexity without solving a current requirement.

---

# 11. MVP functionality

The application contains:

- hierarchical categories,
- effectively arbitrary category nesting depth,
- breadcrumb navigation,
- category creation,
- category rename,
- safe category deletion,
- prompt creation,
- prompt editing,
- prompt deletion,
- prompt moving,
- prompt duplication,
- copy-to-Windows-clipboard,
- local persistence,
- supplied default categories/prompts,
- help,
- safety metadata mirror,
- startup recovery,
- single-instance/data-lock protection,
- basic keyboard/accessibility support,
- lightweight modern styling.

---

# 12. Explicit non-goals

Do not implement:

- AI APIs,
- OpenAI API,
- Claude API,
- OpenRouter API,
- accounts,
- cloud storage,
- sync,
- search,
- tags,
- favorites,
- drag-and-drop,
- manual ordering UI,
- prompt variables,
- templates,
- prompt history,
- Markdown rendering,
- syntax highlighting,
- import,
- export,
- ZIP backup,
- installer,
- automatic update,
- system tray,
- startup-with-Windows,
- global hotkeys,
- dark mode,
- theme selector,
- localization,
- telemetry,
- analytics,
- networking.

---

# 13. Application language

MVP UI language:

```text
English
```

Do not add localization infrastructure.

---

# 14. Terminology lock

Use these terms:

```text
Category
Prompt
Home
Breadcrumb
Move
Duplicate
Copy
```

Visible prompt-card:

```text
Copy
```

always means:

```text
Copy to Windows clipboard
```

Internal:

```text
DuplicatePrompt
```

means:

```text
create a second stored prompt
```

Do not name the stored duplication method:

```text
CopyPrompt
```

because that conflicts with clipboard semantics.

---

# 15. Root model

`Home` is logical only.

It is not a `CategoryRecord`.

Internal root value:

```csharp
Guid? categoryId = null;
```

Therefore:

```text
Category.ParentId == null
→ top-level category

PromptRecord.CategoryId == null
→ prompt on Home
```

---

# 16. Typical hierarchy

```text
Home
├── Games
│   ├── Planning
│   ├── Implementation
│   │   ├── Android
│   │   └── Windows
│   └── Testing
│
└── Tools
    ├── Planning
    ├── Implementation
    └── Testing
```

No fixed depth field exists.

---

# 17. Main window

Initial size:

```text
1100 × 760
```

Minimum:

```text
900 × 600
```

Startup:

```text
CenterScreen
```

Use the standard Windows title bar.

Do not implement custom window chrome.

---

# 18. Visual direction

The UI must be:

- light,
- clean,
- restrained,
- modern,
- spacious,
- recognizable as a current application.

Do not use:

- gradients,
- acrylic,
- glass,
- decorative animations,
- large shadows,
- external fonts,
- icon packages.

The visual polish comes from:

- colour,
- spacing,
- hierarchy,
- typography,
- rounded cards,
- coherent buttons.

---

# 19. Colours

```text
Application background
#F4F6FA

Surface
#FFFFFF

Primary text
#111827

Secondary text
#6B7280

Subtle text
#9CA3AF

Border
#E5E7EB

Border hover
#D1D5DB

Accent
#4F46E5

Accent hover
#4338CA

Accent pressed
#3730A3

Accent light
#EEF2FF

Secondary hover
#F9FAFB

Danger
#DC2626

Danger light
#FEF2F2

Danger border
#FECACA
```

---

# 20. Typography

Default:

```text
Segoe UI
```

Prompt text/editor:

```text
Consolas
```

Sizes:

```text
Application title    20
Section title        18
Body                 14
Category             14
Secondary            13
Prompt content       13
Buttons              13
```

---

# 21. Spacing

Use:

```text
4
8
12
16
24
32
```

Avoid arbitrary spacing values unless technically necessary.

---

# 22. Corner radii

```text
Cards       12
Buttons     10
Logo        10
Inputs       8
```

---

# 23. Header

Height:

```text
64
```

Left:

```text
[P] Prompt Helper
```

Right:

```text
[?]
```

Logo:

```text
36 × 36
indigo background
white P
18px bold
CornerRadius 10
```

No logo file is required.

---

# 24. Main layout

Final layout:

```text
Window
├ Header                         64px
└ Main Grid
  ├ Breadcrumb                  Auto
  ├ Categories header           Auto
  ├ Categories area             Auto, max 190
  ├ Divider                     25
  ├ Prompts header              Auto
  └ Prompt ListBox              *
```

Do not wrap the complete main window content in one outer `ScrollViewer`.

---

# 25. Prompt list scrolling

The prompt list owns main vertical scrolling.

Use virtualization:

```xml
VirtualizingPanel.IsVirtualizing="True"
VirtualizingPanel.VirtualizationMode="Recycling"
ScrollViewer.CanContentScroll="True"
```

Do not place that `ListBox` inside another vertical `ScrollViewer`.

---

# 26. Category area

Header:

```text
Categories                         [+ Add]
```

Categories render through:

```text
ScrollViewer
└ ItemsControl
  └ WrapPanel
```

Maximum category-area height:

```text
190
```

If categories exceed this area, only the category area scrolls.

---

# 27. Category card

Approximate size:

```text
230 × 58
```

Structure:

```text
Border
└ Grid
  ├ [category name/open button]
  └ [✎] [×]
```

Never nest Rename/Delete buttons inside the Open-category button.

---

# 28. Category actions

Main area:

```text
opens category
```

Glyph:

```text
✎
```

means Rename.

Glyph:

```text
×
```

means Delete.

Tooltips:

```text
Rename category
Delete category
```

Automation names must contain the same descriptions.

---

# 29. Category-name normalization

Normalize with:

```text
Trim()
```

Do not:

- title-case,
- remove punctuation,
- replace spaces,
- rewrite symbols.

---

# 30. Category-name validation

Category name must:

- contain at least one non-trimmed character,
- contain no control characters,
- contain at most 80 Unicode text elements,
- be unique among siblings using `OrdinalIgnoreCase`.

Allowed:

```text
Games > Testing
Tools > Testing
```

Not allowed as siblings:

```text
Testing
testing
```

---

# 31. Category rename

Rename changes only:

```text
Name
```

It preserves:

- ID,
- ParentId,
- SortOrder,
- descendants,
- prompts.

---

# 32. Category delete

A category is deletable only if:

```text
direct child categories == 0
AND
direct prompts == 0
```

Non-empty message:

```text
This category is not empty.

Move or delete its prompts and subcategories first.
```

No recursive deletion exists.

---

# 33. Empty-category delete confirmation

Use custom dialog:

```text
Delete Category

Delete category "Planning"?

This action cannot be undone.

[Cancel] [Delete]
```

---

# 34. Breadcrumb

Example:

```text
Home › Games › Implementation › Android
```

All ancestors are clickable.

Current item is plain text.

Root on Home:

```text
Home
```

with no trailing separator.

---

# 35. Prompt section

Header:

```text
Prompts                         [+ Prompt]
```

Empty state:

```text
No prompts in this category.
```

---

# 36. Prompt card

Each card contains:

- derived preview title,
- raw Markdown text,
- Delete,
- Edit,
- Move,
- Copy.

Prompt display area:

```text
Height = 190
Font = Consolas 13
ReadOnly = true
TextWrapping = Wrap
Vertical scrollbar = Auto
```

---

# 37. Prompt preview title

There is no stored title.

Algorithm:

```text
read first non-empty line
Trim
↓
none:
    "(Empty prompt)"
↓
unavailable content:
    "(Unavailable prompt)"
↓
otherwise:
    at most 80 Unicode text elements
    reserve final element for …
```

Use `TextUtilities.TruncateWithEllipsis`.

---

# 38. Prompt create/edit window

Size:

```text
780 × 580
```

Minimum:

```text
640 × 450
```

Editor:

```text
Consolas
13px
AcceptsReturn = true
AcceptsTab = true
NoWrap
vertical scrollbar Auto
horizontal scrollbar Auto
```

Create title:

```text
Create Prompt
```

Edit title:

```text
Edit Prompt
```

Buttons:

```text
Cancel
Save
```

Empty prompts are valid.

---

# 39. Enter/Escape semantics

Name dialog:

```text
Enter → Create/Save
Escape → Cancel
```

Move dialog:

```text
Enter → Move/Copy
Escape → Cancel
```

Prompt editor:

```text
Enter → newline
Escape → Cancel
```

Do not make the editor Save button an `IsDefault` button.

---

# 40. Prompt content semantics

Prompts are plain text.

Do not:

- parse Markdown,
- render Markdown,
- alter code fences,
- trim text,
- normalize Markdown,
- remove blank lines.

Repository write/read without editor modification must round-trip the exact .NET string.

An actual user edit through WPF may normalize line-ending representation.

The application therefore guarantees **textual content preservation**, not byte-for-byte storage identity after an edit.

---

# 41. Copy to clipboard

Prompt-card button:

```text
Copy
```

Sequence:

```text
re-read current .md
↓
Windows clipboard
↓
button becomes "Copied ✓"
↓
wait ~900 ms
↓
button returns to "Copy"
```

No success popup.

---

# 42. Clipboard retry

Clipboard operations execute on the WPF UI/STA thread.

Retry:

```text
attempts: 5
delay:    25ms between failures
maximum additional wait: ~100ms
```

Do not add long blocking retries.

---

# 43. Move dialog

```text
Move Prompt

<prompt preview>

Destination
[combo box]

☐ Copy instead of move

[Cancel] [Move]
```

Checking the box changes action text:

```text
Move
→
Copy
```

Internally this calls:

```text
DuplicatePrompt
```

---

# 44. Unavailable prompt and Move dialog

If prompt content is unavailable:

```text
Move
enabled

Copy instead of move
disabled
```

Display:

```text
Unavailable prompts can be moved but cannot be duplicated.
```

---

# 45. Move semantics

Normal Move:

```text
same prompt ID
same .md file
different CategoryId
new destination-end SortOrder
```

Moving to same category:

```text
no-op
no save required
```

---

# 46. Duplicate semantics

When `Copy instead of move` is checked:

```text
source remains
new GUID
new .md file
new PromptRecord
destination-end SortOrder
same prompt text
```

Duplicating into the same category is allowed.

---

# 47. Missing prompt file behaviour

If metadata references a prompt whose `.md` cannot be read:

Card:

```text
(Unavailable prompt)

[Prompt file could not be loaded.]
```

Actions:

```text
Delete              enabled
Move                enabled
Edit                disabled
Copy                 disabled
Duplicate via Move  disabled
```

No application crash.

---

# 48. Orphan `.md` policy

An orphan is:

```text
.md file exists
but no PromptRecord references its GUID
```

MVP policy:

```text
preserve
ignore
do not display
do not automatically delete
```

There is no cleanup feature.

---

# 49. External file modification policy

Direct manual modification of app data is not an official workflow.

No file watcher exists.

If `.md` files are modified externally while the app runs:

- later category refresh may read the new content,
- an already open editor may contain older content,
- saving that editor may overwrite the external edit.

This is acceptable for the MVP.

---

# 50. Data location

```text
%LOCALAPPDATA%\PromptHelper\
```

Example:

```text
C:\Users\User\AppData\Local\PromptHelper\
```

---

# 51. Final data tree

```text
PromptHelper\
│
├── .app.lock
├── initializing.marker          # normally absent
├── library.json
├── library.backup.json
│
├── prompts\
│   ├── <guid-n>.md
│   └── ...
│
└── recovery\
    └── library.corrupt-<timestamp>-<guid>.json
```

---

# 52. Source-of-truth semantics

Current structural metadata:

```text
library.json
```

Current prompt content:

```text
prompts\<guid>.md
```

Safety metadata mirror:

```text
library.backup.json
```

The mirror is **not** a version history.

---

# 53. Metadata commit point

The logical commit point is:

```text
successful atomic replacement of library.json
```

After this succeeds:

```text
the metadata operation is committed
```

A backup failure after this point produces warning only.

---

# 54. Backup meaning

`library.backup.json` is:

```text
the latest metadata state that was successfully mirrored
```

Normally it equals primary exactly.

If a backup write fails, it may temporarily be older.

The application warns the user.

On the next valid startup:

```text
valid primary
→ backup synchronization retried
```

---

# 55. Data model — CategoryRecord.cs

```csharp
using System.Text.Json.Serialization;

namespace PromptHelper.Models;

public sealed class CategoryRecord
{
    [JsonRequired]
    public Guid Id { get; set; }

    [JsonRequired]
    public Guid? ParentId { get; set; }

    [JsonRequired]
    public string Name { get; set; } = string.Empty;

    [JsonRequired]
    public long SortOrder { get; set; }
}
```

---

# 56. PromptRecord.cs

```csharp
using System.Text.Json.Serialization;

namespace PromptHelper.Models;

public sealed class PromptRecord
{
    [JsonRequired]
    public Guid Id { get; set; }

    [JsonRequired]
    public Guid? CategoryId { get; set; }

    [JsonRequired]
    public long SortOrder { get; set; }
}
```

---

# 57. LibraryDocument.cs

```csharp
using System.Text.Json.Serialization;

namespace PromptHelper.Models;

public sealed class LibraryDocument
{
    public const int CurrentSchemaVersion = 1;

    [JsonRequired]
    public int SchemaVersion { get; set; } =
        CurrentSchemaVersion;

    [JsonRequired]
    public List<CategoryRecord> Categories { get; set; } = [];

    [JsonRequired]
    public List<PromptRecord> Prompts { get; set; } = [];
}
```

---

# 58. Supporting records

`Models/OperationResults.cs`

```csharp
namespace PromptHelper.Models;

public sealed record DefaultLibraryPackage(
    LibraryDocument Document,
    IReadOnlyDictionary<Guid, string> PromptContents);

public sealed record CommitResult(
    bool BackupSynchronized,
    string? Warning);

public sealed record StartupResult(
    LibraryDocument Document,
    bool RecoveredFromBackup,
    string? Warning);

public sealed record OperationResult(
    string? Warning = null);

public sealed record OperationResult<T>(
    T Value,
    string? Warning = null);

public sealed record PromptDisplayRecord(
    Guid Id,
    string Content,
    bool IsContentAvailable,
    string? LoadError);

public sealed record DestinationRecord(
    Guid? CategoryId,
    string DisplayPath);
```

---

# 59. TextUtilities.cs

```csharp
using System.Globalization;

namespace PromptHelper.Infrastructure;

public static class TextUtilities
{
    public static int GetTextElementCount(
        string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return StringInfo
            .ParseCombiningCharacters(value)
            .Length;
    }

    public static string TruncateWithEllipsis(
        string value,
        int maximumTextElements)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (maximumTextElements < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumTextElements));
        }

        int[] starts =
            StringInfo.ParseCombiningCharacters(value);

        if (starts.Length <= maximumTextElements)
        {
            return value;
        }

        int kept =
            maximumTextElements - 1;

        int endIndex =
            starts[kept];

        return value[..endIndex] + "…";
    }
}
```

---

# 60. Metadata example

```json
{
  "schemaVersion": 1,
  "categories": [
    {
      "id": "10000000-0000-0000-0000-000000000001",
      "parentId": null,
      "name": "Games",
      "sortOrder": 10
    }
  ],
  "prompts": [
    {
      "id": "20000000-0000-0000-0000-000000000001",
      "categoryId": "10000000-0000-0000-0000-000000000001",
      "sortOrder": 10
    }
  ]
}
```

Every listed property is required.

---

# 61. Prompt filename mapping

ID:

```text
06b933a3-d70c-4ad1-8a81-6df6d5483393
```

file:

```text
06b933a3d70c4ad18a816df6d5483393.md
```

Use:

```csharp
id.ToString("N")
```

No user text appears in a filename.

---

# 62. Validator invariants

A valid document requires:

```text
SchemaVersion == 1

Category IDs:
- non-empty
- unique

Prompt IDs:
- non-empty
- unique

Category Name:
- non-null
- already trimmed
- not empty
- <= 80 Unicode text elements
- no control characters

ParentId:
- null
OR valid category

No self-parent

No category cycles

Sibling names:
unique OrdinalIgnoreCase

Prompt CategoryId:
null OR valid category
```

`SortOrder` may legally have duplicate values.

Deterministic secondary sorting handles ties.

---

# 63. UnsupportedLibrarySchemaException.cs

```csharp
namespace PromptHelper.Services;

public sealed class UnsupportedLibrarySchemaException :
    InvalidDataException
{
    public UnsupportedLibrarySchemaException(
        int schemaVersion)
        : base(
            $"Unsupported library schema version: " +
            $"{schemaVersion}.")
    {
        SchemaVersion = schemaVersion;
    }

    public int SchemaVersion { get; }
}
```

---

# 64. LibraryDocumentCloner.cs

```csharp
using PromptHelper.Models;

namespace PromptHelper.Services;

public static class LibraryDocumentCloner
{
    public static LibraryDocument Clone(
        LibraryDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new LibraryDocument
        {
            SchemaVersion = source.SchemaVersion,

            Categories = source.Categories
                .Select(x => new CategoryRecord
                {
                    Id = x.Id,
                    ParentId = x.ParentId,
                    Name = x.Name,
                    SortOrder = x.SortOrder
                })
                .ToList(),

            Prompts = source.Prompts
                .Select(x => new PromptRecord
                {
                    Id = x.Id,
                    CategoryId = x.CategoryId,
                    SortOrder = x.SortOrder
                })
                .ToList()
        };
    }
}
```

---

# 65. Business mutation rule

Do **not** mutate the live document then attempt rollback.

Use:

```text
current
↓
deep clone
↓
modify candidate
↓
validate candidate
↓
commit candidate
↓
replace current in-memory document
```

If primary commit fails:

```text
current in-memory state never changed
```

---

# 66. AppPaths.cs

```csharp
namespace PromptHelper.Services;

public sealed class AppPaths
{
    public AppPaths(
        string? rootOverride = null)
    {
        RootDirectory =
            rootOverride ??
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .LocalApplicationData),
                "PromptHelper");
    }

    public string RootDirectory { get; }

    public string LockPath =>
        Path.Combine(
            RootDirectory,
            ".app.lock");

    public string InitializationMarkerPath =>
        Path.Combine(
            RootDirectory,
            "initializing.marker");

    public string LibraryPath =>
        Path.Combine(
            RootDirectory,
            "library.json");

    public string LibraryBackupPath =>
        Path.Combine(
            RootDirectory,
            "library.backup.json");

    public string PromptsDirectory =>
        Path.Combine(
            RootDirectory,
            "prompts");

    public string RecoveryDirectory =>
        Path.Combine(
            RootDirectory,
            "recovery");

    public string GetPromptPath(
        Guid id) =>
        Path.Combine(
            PromptsDirectory,
            $"{id:N}.md");

    public void EnsureRootDirectory()
    {
        Directory.CreateDirectory(
            RootDirectory);
    }

    public void EnsureDataDirectories()
    {
        Directory.CreateDirectory(
            RootDirectory);

        Directory.CreateDirectory(
            PromptsDirectory);

        Directory.CreateDirectory(
            RecoveryDirectory);
    }
}
```

Tests pass a temporary `rootOverride`.

---

# 67. Application lock

Use an exclusive file handle.

Do not use a global named mutex.

`AppInstanceLock.cs`:

```csharp
namespace PromptHelper.Services;

public sealed class AppInstanceLock :
    IDisposable
{
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    private readonly FileStream _stream;

    private AppInstanceLock(
        FileStream stream)
    {
        _stream = stream;
    }

    public static AppInstanceLock? TryAcquire(
        string lockPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            lockPath);

        try
        {
            FileStream stream =
                new(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);

            return new AppInstanceLock(stream);
        }
        catch (IOException ex)
            when (IsSharingOrLockViolation(ex))
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }

    private static bool IsSharingOrLockViolation(
        IOException ex)
    {
        int win32Code =
            ex.HResult & 0xFFFF;

        return win32Code
            is ErrorSharingViolation
            or ErrorLockViolation;
    }
}
```

The `.app.lock` file may remain after closing.

The open handle is the actual lock.

---

# 68. Atomic writer abstraction

`IAtomicTextWriter.cs`:

```csharp
namespace PromptHelper.Services;

public interface IAtomicTextWriter
{
    void Write(
        string targetPath,
        string content);
}
```

This is intentionally small.

Its main purpose is deterministic failure injection in tests.

---

# 69. AtomicTextWriter.cs

```csharp
using System.Text;

namespace PromptHelper.Services;

public sealed class AtomicTextWriter :
    IAtomicTextWriter
{
    public void Write(
        string targetPath,
        string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            targetPath);

        ArgumentNullException.ThrowIfNull(content);

        string directory =
            Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException(
                "Target path has no directory.");

        Directory.CreateDirectory(directory);

        string tempPath =
            Path.Combine(
                directory,
                $".{Path.GetFileName(targetPath)}." +
                $"{Guid.NewGuid():N}.tmp");

        try
        {
            using (
                var stream =
                    new FileStream(
                        tempPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
            using (
                var writer =
                    new StreamWriter(
                        stream,
                        new UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();

                stream.Flush(
                    flushToDisk: true);
            }

            if (File.Exists(targetPath))
            {
                File.Replace(
                    tempPath,
                    targetPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(
                    tempPath,
                    targetPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Explicit best-effort temp cleanup only.
                }
            }
        }
    }
}
```

Target platform is Windows/NTFS-style local storage.

The temp file is always created in the destination directory.

---

# 70. Delete abstraction

```csharp
namespace PromptHelper.Services;

public interface IFileDeleter
{
    void DeleteIfExists(
        string path);
}

public sealed class FileDeleter :
    IFileDeleter
{
    public void DeleteIfExists(
        string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
```

---

# 71. PromptRepository public API

Required:

```text
Exists
Read
Create
Update
DeleteIfExists
EnumeratePromptFiles
```

Implementation:

```csharp
namespace PromptHelper.Services;

public sealed class PromptRepository
{
    private readonly AppPaths _paths;
    private readonly IAtomicTextWriter _writer;
    private readonly IFileDeleter _deleter;

    public PromptRepository(
        AppPaths paths,
        IAtomicTextWriter writer,
        IFileDeleter deleter)
    {
        _paths = paths;
        _writer = writer;
        _deleter = deleter;
    }

    public bool Exists(
        Guid id) =>
        File.Exists(
            _paths.GetPromptPath(id));

    public string Read(
        Guid id) =>
        File.ReadAllText(
            _paths.GetPromptPath(id));

    public void Create(
        Guid id,
        string content)
    {
        string path =
            _paths.GetPromptPath(id);

        if (File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Prompt file already exists: {id}");
        }

        _writer.Write(
            path,
            content);
    }

    public void Update(
        Guid id,
        string content)
    {
        string path =
            _paths.GetPromptPath(id);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Prompt file does not exist.",
                path);
        }

        _writer.Write(
            path,
            content);
    }

    public void DeleteIfExists(
        Guid id)
    {
        _deleter.DeleteIfExists(
            _paths.GetPromptPath(id));
    }

    public IReadOnlyList<string>
        EnumeratePromptFiles() =>
        Directory
            .EnumerateFiles(
                _paths.PromptsDirectory,
                "*.md",
                SearchOption.TopDirectoryOnly)
            .ToList();
}
```

---

# 72. JSON options

Repository uses:

```csharp
private static readonly JsonSerializerOptions
    JsonOptions =
        new()
        {
            PropertyNamingPolicy =
                JsonNamingPolicy.CamelCase,

            WriteIndented = true,

            AllowTrailingCommas = false,

            ReadCommentHandling =
                JsonCommentHandling.Disallow,

            RespectNullableAnnotations = true
        };
```

---

# 73. Strict raw-schema inspection

Before deserializing to `LibraryDocument`:

```text
root must be JSON object
↓
exactly one case-insensitive schemaVersion property
↓
must be integer
↓
> CurrentSchemaVersion:
    UnsupportedLibrarySchemaException
↓
!= CurrentSchemaVersion:
    InvalidDataException
↓
then deserialize v1
```

This ordering is mandatory.

---

# 74. LibraryRepository commit

Pseudo-code:

```text
Validate
Serialize
↓
atomic write library.json
↓
COMMIT POINT
↓
try atomic write library.backup.json
↓
backup success?
    yes → CommitResult(true)
    no  → CommitResult(false, warning)
```

No exception from a backup-only operation may make the caller treat the primary commit as uncommitted.

---

# 75. Backup catch rule

After primary commit:

```csharp
try
{
    _writer.Write(
        _paths.LibraryBackupPath,
        json);
}
catch (Exception)
{
    return new CommitResult(
        false,
        "The library was saved, but its safety backup " +
        "could not be updated. Current data remains " +
        "stored in library.json.");
}
```

This broad catch is deliberate here because:

```text
primary is already committed
backup is best-effort only
```

Do not rethrow from the backup path.

---

# 76. Startup classification

Only these are corruption:

```text
JsonException
InvalidDataException
```

except:

```text
UnsupportedLibrarySchemaException
```

which is its own fatal classification.

Only these are missing:

```text
FileNotFoundException
DirectoryNotFoundException
```

Do **not** classify as corruption:

- UnauthorizedAccessException,
- arbitrary IOException,
- path/security failure.

Those are real startup failures.

---

# 77. Startup matrix

| Primary | Backup | Result |
|---|---|---|
| valid | any | use primary, synchronize backup |
| corrupt | valid | preserve corrupt primary best-effort; recover backup |
| missing | valid | restore primary from backup |
| corrupt | corrupt | fatal |
| corrupt | missing | fatal |
| missing | corrupt | fatal |
| future schema | any | fatal, preserve primary |
| missing | future schema backup | fatal |
| missing | missing | enter first-run decision |

---

# 78. Valid primary always wins

If primary is valid:

```text
do not attempt fallback
do not prefer backup
```

Instead:

```text
load primary
attempt backup synchronization
```

This also repairs:

- missing backup,
- corrupt backup,
- stale backup.

---

# 79. Corrupt-primary preservation

Before overwriting a corrupt primary from a valid backup, best-effort copy it to:

```text
recovery\
library.corrupt-YYYYMMDD-HHmmssfff-<guid>.json
```

Failure to create the extra recovery copy does not prevent valid backup restoration.

---

# 80. Future schema

If primary advertises:

```json
{
  "schemaVersion": 999
}
```

the application must:

```text
stop
preserve primary
do not use old schema-1 backup
do not initialize defaults
```

This is a mandatory test.

---

# 81. First-run detection

True first run requires:

```text
primary missing
backup missing
initialization marker absent
prompt directory contains no .md files
```

Only then may default initialization begin.

---

# 82. Initialization marker

Create:

```text
initializing.marker
```

before writing default prompt files.

Its purpose is to identify an interrupted legitimate initialization.

---

# 83. Interrupted first-run resume

When metadata is missing and marker exists:

Existing `.md` files must all:

1. use one of the known built-in default prompt filenames;
2. contain exactly the expected default text.

Anything else:

```text
fatal
preserve files
```

Missing default files may then be safely created.

---

# 84. Unknown files without marker

If:

```text
primary missing
backup missing
marker missing
at least one .md exists
```

result:

```text
fatal safety stop
defaults not created
existing files untouched
```

---

# 85. Stale marker with valid primary

If primary is valid:

```text
primary wins
marker is deleted best-effort
```

The marker is non-authoritative once valid primary metadata exists.

---

# 86. Default category IDs

```text
Games
10000000-0000-0000-0000-000000000001

Tools
10000000-0000-0000-0000-000000000002

Games > Planning
10000000-0000-0000-0000-000000000011

Games > Implementation
10000000-0000-0000-0000-000000000012

Games > Testing
10000000-0000-0000-0000-000000000013

Tools > Planning
10000000-0000-0000-0000-000000000021

Tools > Implementation
10000000-0000-0000-0000-000000000022

Tools > Testing
10000000-0000-0000-0000-000000000023
```

---

# 87. Default prompt IDs

```text
Games > Planning sample
20000000-0000-0000-0000-000000000001

Tools > Testing sample
20000000-0000-0000-0000-000000000002
```

These IDs are intentionally deterministic to support safe interrupted-initialization recovery.

---

# 88. Default prompt 1

Location:

```text
Games > Planning
```

```md
# Task

Create a detailed implementation plan for the supplied game project.

## Requirements

- identify unclear requirements
- define implementation phases
- minimize decisions left to the implementation agent
- include validation and testing steps
- preserve the supplied product scope
```

---

# 89. Default prompt 2

Location:

```text
Tools > Testing
```

```md
# Task

Perform a thorough quality review of the supplied implementation.

Check for:

- functional defects
- missing requirements
- inconsistent behaviour
- data-loss risks
- error-handling problems
- regression risks

Repair confirmed defects where permitted and run the relevant tests again.
```

---

# 90. Category create operation

```text
normalize
↓
validate parent
↓
validate sibling uniqueness
↓
clone current document
↓
create unique GUID
↓
calculate end SortOrder
↓
add category
↓
validate candidate
↓
commit primary
↓
backup attempt
↓
swap in-memory document
↓
refresh UI
```

---

# 91. Category rename operation

```text
locate category
↓
normalize
↓
validate against siblings excluding itself
↓
clone
↓
change Name only
↓
validate
↓
commit
↓
swap current
↓
refresh
```

---

# 92. Category delete operation

Service must independently re-check emptiness even though UI checks first.

Sequence:

```text
find category
↓
reject if direct child category
↓
reject if direct prompt
↓
clone
↓
remove category
↓
commit
↓
swap
↓
refresh
```

---

# 93. Prompt create transaction

```text
validate destination
↓
generate unused GUID
↓
write .md
↓
clone metadata
↓
add PromptRecord
↓
validate
↓
primary commit
↓
backup attempt
↓
swap current
```

If primary commit fails:

```text
current metadata unchanged
new .md best-effort deleted
```

If cleanup also fails:

```text
orphan .md allowed
```

---

# 94. Unique prompt-ID generation

A generated prompt ID must not collide with:

```text
existing PromptRecord
OR
existing orphan .md file
```

Try up to ten fresh GUIDs.

Failure after ten attempts:

```text
throw InvalidOperationException
```

This is an extreme defensive fallback.

---

# 95. Prompt edit transaction

```text
verify PromptRecord exists
↓
verify .md exists
↓
atomic replacement of same .md
```

Metadata does not change.

A failed atomic write must leave previous target content intact.

---

# 96. Prompt delete transaction

```text
find PromptRecord
↓
clone metadata
↓
remove record
↓
commit primary
↓
backup attempted
```

If primary fails:

```text
prompt remains
file remains
```

If primary succeeds but backup fails:

```text
logical delete committed
.md deliberately retained
warning
```

If primary + backup succeed:

```text
physical .md delete attempted
```

Physical delete failure:

```text
logical delete remains committed
orphan .md retained
warning
```

---

# 97. Why deletion waits for backup

If a stale backup still references:

```text
Prompt X
```

and Prompt X's `.md` were deleted, recovery from that backup would produce broken metadata.

Therefore a prompt file may be deleted only when:

```text
primary no longer references prompt
AND
backup successfully mirrors that primary
```

---

# 98. Prompt move transaction

```text
find prompt
validate destination
↓
same destination?
    return no-op
↓
clone
↓
set CategoryId
↓
set destination-end SortOrder
↓
commit
↓
swap
```

No `.md` operation.

---

# 99. Prompt duplication transaction

```text
find source
validate destination
read source .md
↓
new unused GUID
↓
write duplicate .md
↓
clone metadata
↓
add new PromptRecord
↓
commit
↓
swap
```

If primary commit fails:

```text
duplicate metadata not committed
duplicate .md best-effort deleted
```

---

# 100. Critical persistence failure matrix

| Operation | Failure point | Required result |
|---|---|---|
| Create | `.md` write | nothing committed |
| Create | primary metadata | library unchanged, file cleanup attempted |
| Create | cleanup | orphan allowed |
| Create | backup | prompt committed, warning |
| Edit | temp write | previous prompt survives |
| Edit | replace | previous prompt survives |
| Delete | primary | prompt unchanged |
| Delete | backup | logical delete committed, file retained |
| Delete | file deletion | logical delete committed, orphan retained |
| Move | primary | location unchanged |
| Move | backup | move committed, warning |
| Duplicate | new file | nothing committed |
| Duplicate | primary | metadata unchanged, new file cleanup attempted |
| Duplicate | cleanup | orphan allowed |
| Duplicate | backup | duplicate committed, warning |
| Category operation | primary | state unchanged |
| Category operation | backup | state committed, warning |

---

# 101. SortOrder type

Use:

```csharp
long
```

Normal new values:

```text
10
20
30
40
```

---

# 102. Category visible ordering

```text
SortOrder ascending
then Name OrdinalIgnoreCase
then Id
```

Rename does not change SortOrder.

---

# 103. Prompt visible ordering

```text
SortOrder ascending
then Id
```

Create:

```text
destination end
```

Move:

```text
destination end
```

Duplicate:

```text
destination end
```

Edit:

```text
unchanged
```

---

# 104. Sort overflow

If:

```text
max > long.MaxValue - 10
```

resequence only that destination:

```text
10
20
30
...
```

according to current deterministic visible order.

Then append new entry.

This is an extreme edge path but must be deterministic.

---

# 105. Destination paths

Normal:

```text
Home
Games
Games > Planning
Games > Implementation
Tools
...
```

Home always first.

Other options sorted by complete display path.

---

# 106. Destination collision handling

Build normal display paths.

Compare them case-insensitively including the root label.

If collision occurs, category entries receive:

```text
[xxxxxxxx]
```

where `xxxxxxxx` is the first eight characters of the category's GUID in `N` format.

The internal selection always stores the full `CategoryId`.

---

# 107. Prompt display loading

On navigation:

```text
find PromptRecords for current category
↓
read only those .md files
↓
create cards
```

Do not read every prompt in the entire library at startup.

---

# 108. Expected normal scale

Target comfortable use:

```text
~100 categories
~1,000 prompts
```

A single:

```text
50,000-character prompt
```

must remain usable.

No artificial content limit is added.

---

# 109. MVVM policy

Use a pragmatic split.

ViewModels/services:

- navigation state,
- business operations,
- persistence logic.

Code-behind:

- opening dialogs,
- MessageBoxes,
- click routing,
- clipboard feedback,
- temporary `Copied ✓` display.

Do not add RelayCommand infrastructure.

---

# 110. ObservableObject.cs

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PromptHelper.Infrastructure;

public abstract class ObservableObject :
    INotifyPropertyChanged
{
    public event PropertyChangedEventHandler?
        PropertyChanged;

    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName]
        string? propertyName = null)
    {
        if (EqualityComparer<T>
            .Default
            .Equals(field, value))
        {
            return false;
        }

        field = value;

        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));

        return true;
    }
}
```

No additional ViewModel framework.

---

# 111. MainViewModel state

Required:

```text
CurrentCategoryId

Breadcrumbs
ChildCategories
Prompts

DataFolderPath
```

Collections:

```csharp
ObservableCollection<T>
```

Navigation refresh rebuilds these collections.

---

# 112. MainViewModel operation wrappers

Each successful mutation:

```text
call service
↓
Refresh()
↓
return warning/result
```

Clipboard read:

```text
does not refresh
```

---

# 113. PromptCardViewModel behaviour

Required properties:

```text
Id
Content
IsContentAvailable
LoadError
PreviewTitle
```

`PreviewTitle` uses:

```text
first non-empty line
+
TextUtilities.TruncateWithEllipsis
```

---

# 114. Styling resources

`Styles/Theme.xaml` must define:

```text
AppBackgroundBrush
SurfaceBrush
TextPrimaryBrush
TextSecondaryBrush
SubtleTextBrush
BorderBrush
BorderHoverBrush
AccentBrush
AccentHoverBrush
AccentPressedBrush
AccentLightBrush
SecondaryHoverBrush
DangerBrush
DangerLightBrush
DangerBorderBrush

BaseButtonStyle
PrimaryButtonStyle
SecondaryButtonStyle
DangerButtonStyle
IconButtonStyle
BreadcrumbButtonStyle
CategoryOpenButtonStyle
CardBorderStyle
ModernTextBoxStyle
PromptDisplayTextBoxStyle
PromptEditorTextBoxStyle
FlatListBoxItemStyle
ModernComboBoxStyle
ModernCheckBoxStyle
```

---

# 115. Button visual semantics

Primary:

```text
indigo
white text
```

Secondary:

```text
white
neutral border
dark text
```

Danger:

```text
white
red text
light red border
```

Disabled:

```text
opacity ~0.45
```

Keyboard-focused controls must have a clearly visible accent border.

---

# 116. TextBox template rule

Custom `TextBox` template must include:

```xml
<ScrollViewer x:Name="PART_ContentHost"/>
```

Never replace the default editor template with a control that breaks:

- selection,
- caret,
- scrolling,
- keyboard input.

---

# 117. Prompt display/editor style split

Do not use one identical prompt TextBox style.

Display:

```text
ReadOnly
Wrap
vertical scroll
no horizontal scroll
```

Editor:

```text
editable
NoWrap
AcceptsReturn
AcceptsTab
horizontal + vertical scrolling
```

---

# 118. Delete confirmation dialog

Add:

```text
Views/ConfirmDeleteDialog.xaml
Views/ConfirmDeleteDialog.xaml.cs
```

Constructor:

```csharp
ConfirmDeleteDialog(
    string title,
    string message,
    string actionText = "Delete")
```

Buttons:

```text
Cancel
Delete
```

Delete button uses danger style.

---

# 119. Name dialog

Files:

```text
NameDialog.xaml
NameDialog.xaml.cs
```

Constructor:

```csharp
NameDialog(
    string title,
    string actionText,
    string initialValue,
    Func<string, string?> validator)
```

Result:

```csharp
string ResultName
```

Validation error appears inline.

Do not close dialog on invalid name.

---

# 120. Prompt editor dialog

Constructor:

```csharp
PromptEditorDialog(
    string title,
    string initialText)
```

Result:

```csharp
string ResultText
```

Save accepts empty content.

---

# 121. Move dialog

Constructor:

```csharp
MovePromptDialog(
    string promptPreview,
    IReadOnlyList<DestinationOptionViewModel> destinations,
    Guid? currentCategoryId,
    bool allowDuplicate)
```

Outputs:

```csharp
Guid? DestinationCategoryId

bool CopyInsteadOfMove
```

---

# 122. Help

Help contents:

```text
Prompt Helper

Choose a category to browse your prompt library.

Use + Add to create categories.
Use + Prompt to create prompts.
Use Copy to copy a prompt to the Windows clipboard.

Data folder:
<actual path>

Version:
<assembly version>
```

No online help.

---

# 123. ClipboardService.cs

```csharp
using System.Runtime.InteropServices;
using System.Windows;

namespace PromptHelper.Services;

public sealed class ClipboardService
{
    public void CopyText(
        string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        ExternalException? lastError = null;

        for (int attempt = 1;
             attempt <= 5;
             attempt++)
        {
            try
            {
                Clipboard.SetText(
                    text,
                    TextDataFormat.UnicodeText);

                return;
            }
            catch (ExternalException ex)
            {
                lastError = ex;

                if (attempt == 5)
                {
                    break;
                }

                Thread.Sleep(25);
            }
        }

        throw new InvalidOperationException(
            "Windows clipboard is currently unavailable.",
            lastError);
    }
}
```

Call from WPF UI thread only.

---

# 124. UI refresh semantics

Create category:

```text
remain in current location
new category appears
```

Rename category:

```text
remain current
card updates
```

Delete category:

```text
remain current parent
card disappears
```

Create prompt:

```text
remain current
new card appears
```

Edit:

```text
remain current
card updates
```

Delete:

```text
remain current
card disappears
```

Move elsewhere:

```text
remain source category
source card disappears
```

Duplicate elsewhere:

```text
remain source category
source remains
```

Copy to clipboard:

```text
no refresh
```

---

# 125. Application composition

Final startup order:

```text
1. Ensure root data directory
2. Acquire exclusive app lock
3. Ensure prompts/recovery directories
4. Create AtomicTextWriter
5. Create FileDeleter
6. Create LibraryRepository
7. Create PromptRepository
8. Create DefaultLibraryFactory
9. Create LibraryStartupService
10. LoadOrInitialize
11. Create PromptLibraryService
12. Create MainViewModel
13. Create ClipboardService
14. Create MainWindow
15. Show MainWindow
16. Show nonfatal startup warning if necessary
```

---

# 126. Startup fatal behaviour

If startup cannot safely continue:

```text
display error
do not create MainWindow
do not overwrite uncertain user data
Shutdown()
```

Examples:

- permissions denied,
- unsupported schema,
- corrupt primary + unusable backup,
- missing metadata with unknown prompt files,
- unexplained lock I/O error.

---

# 127. Safety warnings

Backup failure:

```text
Your change was saved, but Prompt Helper could not update its safety backup.

The current library remains available in library.json.
```

Delete file cleanup failure:

```text
The prompt was removed from the library, but its old .md file could not be deleted.

The file was left in the data folder.
```

---

# 128. Recovery warning

After successful backup recovery:

```text
Library data was recovered from the safety backup.

If Prompt Helper had previously warned that the safety backup could not
be updated, the restored library structure may represent an older saved
state. Existing prompt files were not automatically deleted.
```

---

# 129. No pending shutdown save

There is:

- no autosave queue,
- no background writer,
- no delayed metadata write.

Each action is saved synchronously before reporting success.

Shutdown only disposes the application lock.

---

# 130. Threading

Do not add concurrency.

Business/persistence code executes synchronously from UI actions.

The small local files make this acceptable for the MVP.

The only expected asynchronous UI operation is cosmetic:

```csharp
await Task.Delay(900);
```

for `Copied ✓`.

---

# 131. Repository tree

```text
PromptHelper/
│
├── global.json
├── PromptHelper.slnx
├── README.md
├── THIRD_PARTY_NOTICES.md
├── .gitignore
│
├── src/
│   └── PromptHelper/
│       ├── PromptHelper.csproj
│       ├── App.xaml
│       ├── App.xaml.cs
│       ├── MainWindow.xaml
│       ├── MainWindow.xaml.cs
│       │
│       ├── Infrastructure/
│       │   ├── ObservableObject.cs
│       │   └── TextUtilities.cs
│       │
│       ├── Models/
│       │   ├── CategoryRecord.cs
│       │   ├── PromptRecord.cs
│       │   ├── LibraryDocument.cs
│       │   └── OperationResults.cs
│       │
│       ├── Services/
│       │   ├── AppPaths.cs
│       │   ├── AppInstanceLock.cs
│       │   ├── IAtomicTextWriter.cs
│       │   ├── AtomicTextWriter.cs
│       │   ├── IFileDeleter.cs
│       │   ├── FileDeleter.cs
│       │   ├── UnsupportedLibrarySchemaException.cs
│       │   ├── LibraryValidator.cs
│       │   ├── LibraryDocumentCloner.cs
│       │   ├── LibraryRepository.cs
│       │   ├── PromptRepository.cs
│       │   ├── DefaultLibraryFactory.cs
│       │   ├── LibraryStartupService.cs
│       │   ├── PromptLibraryService.cs
│       │   └── ClipboardService.cs
│       │
│       ├── ViewModels/
│       │   ├── BreadcrumbItemViewModel.cs
│       │   ├── CategoryItemViewModel.cs
│       │   ├── DestinationOptionViewModel.cs
│       │   ├── PromptCardViewModel.cs
│       │   └── MainViewModel.cs
│       │
│       ├── Views/
│       │   ├── NameDialog.xaml
│       │   ├── NameDialog.xaml.cs
│       │   ├── PromptEditorDialog.xaml
│       │   ├── PromptEditorDialog.xaml.cs
│       │   ├── MovePromptDialog.xaml
│       │   ├── MovePromptDialog.xaml.cs
│       │   ├── ConfirmDeleteDialog.xaml
│       │   ├── ConfirmDeleteDialog.xaml.cs
│       │   ├── HelpDialog.xaml
│       │   └── HelpDialog.xaml.cs
│       │
│       └── Styles/
│           └── Theme.xaml
│
└── tests/
    └── PromptHelper.Tests/
        ├── PromptHelper.Tests.csproj
        ├── TestDirectory.cs
        ├── FaultInjectingAtomicTextWriter.cs
        ├── FaultInjectingFileDeleter.cs
        ├── TextUtilitiesTests.cs
        ├── LibraryValidatorTests.cs
        ├── AtomicTextWriterTests.cs
        ├── AppInstanceLockTests.cs
        ├── LibraryRepositoryTests.cs
        ├── LibraryStartupServiceTests.cs
        ├── PromptLibraryServiceTests.cs
        └── MainViewModelTests.cs
```

---

# 132. Application project

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>

    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <AssemblyName>PromptHelper</AssemblyName>
    <RootNamespace>PromptHelper</RootNamespace>

    <Version>0.1.0</Version>

    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>

</Project>
```

Do not force `PlatformTarget=x64` in development builds.

The final publish RID provides x64 packaging.

This avoids unnecessary AnyCPU/x64 test-build friction.

---

# 133. Test project

```xml
<Project Sdk="MSTest.Sdk/4.3.3">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>

    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference
      Include="../../src/PromptHelper/PromptHelper.csproj" />
  </ItemGroup>

</Project>
```

---

# 134. Bootstrap commands

```powershell
mkdir PromptHelper
cd PromptHelper

dotnet new sln -n PromptHelper

dotnet new wpf `
  -n PromptHelper `
  -o src/PromptHelper `
  -f net10.0
```

Create:

```text
tests/PromptHelper.Tests/PromptHelper.Tests.csproj
```

using the exact test project above.

Then:

```powershell
dotnet sln PromptHelper.slnx add `
  src/PromptHelper/PromptHelper.csproj

dotnet sln PromptHelper.slnx add `
  tests/PromptHelper.Tests/PromptHelper.Tests.csproj

dotnet restore
dotnet build
dotnet test
```

Under .NET 10, `dotnet new sln` is expected to produce `.slnx`.

---

# 135. .gitignore

```gitignore
bin/
obj/
.vs/
.idea/

artifacts/

TestResults/

*.user
*.suo
```

---

# 136. README minimum

```md
# Prompt Helper

Prompt Helper is a small local Windows utility for organizing and
copying reusable AI prompts.

## Development requirements

- Windows
- stable .NET 10 SDK

## Build

dotnet build

## Test

dotnet test

## Run

dotnet run --project src/PromptHelper/PromptHelper.csproj

## User data

%LOCALAPPDATA%\PromptHelper

## Privacy

Prompt Helper stores its prompt library locally and does not send prompts
or usage data over the network.
```

---

# 137. Third-party notice policy

The application uses Microsoft .NET/WPF.

The tests use MSTest.

No external:

- UI library,
- icon library,
- Markdown library,
- database,
- analytics SDK,
- AI SDK

is included.

Do not claim that every binary in a self-contained Windows runtime is covered exclusively by MIT.

Preserve applicable Microsoft runtime/license notices supplied with the chosen runtime distribution.

---

# 138. THIRD_PARTY_NOTICES.md

```md
# Third-Party Notices

Prompt Helper is built using Microsoft .NET and Windows Presentation
Foundation (WPF).

The Prompt Helper application itself does not require additional
third-party NuGet packages.

The test project uses MSTest and is not part of the published
application output.

A self-contained Windows .NET distribution contains runtime components
distributed under the applicable Microsoft/.NET and Windows SDK license
terms. The authoritative license files and notices supplied for the exact
runtime used for publishing govern those components.

No external icon library, Markdown library, database library, analytics
SDK, AI SDK, or third-party UI framework is included in the MVP.
```

---

# 139. Release-license hygiene

Before distributing a self-contained publish folder:

1. inspect the publish output;
2. retain any license/notice files included by Microsoft tooling/runtime;
3. include the project's `THIRD_PARTY_NOTICES.md`;
4. do not remove notices merely to reduce file count.

No additional source-code license obligations are introduced by copied third-party application code because none is required by this plan.

---

# 140. TestDirectory.cs

```csharp
namespace PromptHelper.Tests;

public sealed class TestDirectory :
    IDisposable
{
    public TestDirectory()
    {
        Root =
            Path.Combine(
                Path.GetTempPath(),
                "PromptHelperTests",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(
                    Root,
                    recursive: true);
            }
        }
        catch
        {
            // Test cleanup only.
        }
    }
}
```

Every filesystem test gets its own directory.

Tests must never touch real `%LOCALAPPDATA%\PromptHelper`.

---

# 141. FaultInjectingAtomicTextWriter.cs

```csharp
using PromptHelper.Services;

namespace PromptHelper.Tests;

public sealed class FaultInjectingAtomicTextWriter :
    IAtomicTextWriter
{
    private readonly IAtomicTextWriter _inner;
    private int _callNumber;

    public FaultInjectingAtomicTextWriter(
        IAtomicTextWriter inner)
    {
        _inner = inner;
    }

    public Func<string, int, bool>?
        ShouldFail { get; set; }

    public int CallCount =>
        _callNumber;

    public void Write(
        string targetPath,
        string content)
    {
        _callNumber++;

        if (ShouldFail?.Invoke(
                targetPath,
                _callNumber)
            == true)
        {
            throw new IOException(
                "Injected write failure.");
        }

        _inner.Write(
            targetPath,
            content);
    }
}
```

---

# 142. FaultInjectingFileDeleter.cs

```csharp
using PromptHelper.Services;

namespace PromptHelper.Tests;

public sealed class FaultInjectingFileDeleter :
    IFileDeleter
{
    public bool Fail { get; set; }

    public void DeleteIfExists(
        string path)
    {
        if (Fail)
        {
            throw new IOException(
                "Injected delete failure.");
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
```

---

# 143. Mandatory JSON/repository tests

Include:

```text
Missing_schemaVersion_fails

Duplicate_schemaVersion_fails

Missing_categories_fails

Missing_prompts_fails

Category_missing_id_fails

Category_empty_guid_fails

Category_missing_parentId_fails

Category_missing_name_fails

Category_missing_sortOrder_fails

Prompt_missing_id_fails

Prompt_empty_guid_fails

Prompt_missing_categoryId_fails

Prompt_missing_sortOrder_fails

Explicit_null_required_nonnullable_property_fails

Future_schema_detected_before_v1_required_property_validation
```

The future-schema test should use:

```json
{
  "schemaVersion": 999
}
```

and still receive:

```text
UnsupportedLibrarySchemaException
```

not ordinary corruption.

---

# 144. Mandatory validator tests

```text
Valid_empty_library_passes

Duplicate_category_id_fails

Duplicate_prompt_id_fails

Empty_category_name_fails

Whitespace_category_name_fails

Category_control_character_fails

Category_over_80_text_elements_fails

Unknown_parent_fails

Self_parent_fails

Two_node_cycle_fails

Three_node_cycle_fails

Duplicate_sibling_name_fails_case_insensitive

Same_name_different_parent_passes

Prompt_unknown_category_fails
```

---

# 145. Mandatory atomic-writer tests

```text
Write_new_file_creates_content

Replace_existing_file_changes_content

Unicode_round_trip

Markdown_round_trip

No_tmp_left_after_success

Failed_write_does_not_modify_existing_target
```

A deterministic unit test for an OS-level power-loss interruption is not required.

---

# 146. Mandatory lock tests

```text
First_acquire_succeeds

Second_acquire_while_first_open_returns_null

Acquire_after_first_disposed_succeeds
```

Additionally perform a two-process manual Windows test.

---

# 147. Mandatory startup tests

```text
Valid_primary_loads

Valid_primary_recreates_missing_backup

Valid_primary_replaces_corrupt_backup

Corrupt_primary_valid_backup_recovers

Missing_primary_valid_backup_recovers

Corrupt_primary_corrupt_backup_fails

Corrupt_primary_missing_backup_fails

Missing_primary_corrupt_backup_fails

Future_primary_never_falls_back_to_old_backup

Future_backup_when_primary_missing_fails

Fresh_start_creates_defaults

Second_start_does_not_duplicate_defaults

Interrupted_init_with_no_prompt_files_resumes

Interrupted_init_with_partial_exact_defaults_resumes

Interrupted_init_with_modified_default_file_stops

Interrupted_init_with_unknown_file_stops

Unknown_prompt_files_without_marker_stop_initialization

Valid_primary_ignores_and_removes_stale_marker_best_effort
```

---

# 148. Mandatory category-service tests

```text
Create_category_at_Home

Create_nested_category

Duplicate_sibling_rejected

Case_variant_sibling_rejected

Same_name_other_parent_allowed

Control_character_name_rejected

Rename

Rename_duplicate_rejected

Delete_empty

Delete_with_prompt_rejected

Delete_with_child_rejected

Category_primary_save_failure_keeps_in_memory_state

Category_backup_failure_commits_with_warning
```

---

# 149. Mandatory prompt-service tests

```text
Create_prompt_on_Home

Create_prompt_in_category

Create_primary_failure_no_metadata_commit

Create_primary_failure_file_cleanup

Create_cleanup_failure_leaves_orphan_only

Create_backup_failure_commits

Edit_prompt

Edit_missing_file_fails

Delete_prompt_success

Delete_primary_failure_preserves_prompt

Delete_backup_failure_keeps_file

Delete_file_failure_leaves_orphan

Move_prompt

Move_to_Home

Move_same_category_noop

Move_backup_failure_commits

Duplicate_prompt

Duplicate_same_category

Duplicate_has_new_id

Duplicate_primary_failure_no_metadata_commit

Duplicate_primary_failure_cleanup

Duplicate_backup_failure_commits

Duplicate_unavailable_source_fails
```

---

# 150. Navigation tests

```text
Home_breadcrumb

One_level_breadcrumb

Deep_breadcrumb

Destination_Home_first

Destination_alphabetic_paths

Destination_root_name_Home_is_disambiguated

Destination_separator_collision_is_disambiguated
```

---

# 151. Preview tests

```text
Empty_prompt_preview

Normal_first_line

Leading_blank_lines_ignored

More_than_80_text_elements_ellipsized

Emoji_not_split_mid_text_element

Unavailable_prompt_preview
```

---

# 152. Failure test: delete + backup fail

Required assertions:

```text
primary no longer references prompt

current service state no longer references prompt

backup still references old prompt

physical .md still exists

OperationResult.Warning is not null
```

This is one of the most important persistence tests.

---

# 153. Failure test: create + primary fail

Assertions:

```text
current metadata unchanged

primary metadata unchanged

backup unchanged

new PromptRecord absent

new .md removed when cleanup succeeds
```

Separate test:

```text
cleanup failure
→ orphan file remains
→ metadata still unchanged
```

---

# 154. Failure test: unexpected backup exception

The injected backup writer may throw any ordinary `Exception`, not only `IOException`.

Required:

```text
primary committed

service in-memory candidate committed

operation reports warning

caller does NOT execute logical rollback cleanup
```

This test protects the commit-point invariant.

---

# 155. Unicode test

Use:

```text
ä ö ü Ä Ö Ü ß
日本語
한국어
中文
Русский
🚀 ✅ ❌
```

Repository round-trip must preserve the string.

---

# 156. Markdown test

Use:

````text
# ROLE

You are an AI agent.

```json
{
  "test": true
}
```

End.
````

Assert exact .NET string equality after repository write/read.

---

# 157. Large prompt test

Generate:

```text
>= 50,000 characters
```

Automated:

- create,
- read,
- edit,
- duplicate,
- read duplicate,
- delete.

Manual:

- render card,
- scroll,
- open editor,
- copy.

---

# 158. Deep hierarchy test

At least:

```text
A
└ B
  └ C
    └ D
      └ E
        └ F
```

Validate:

- tree,
- breadcrumb,
- navigation,
- destinations.

---

# 159. Missing prompt manual test

1. Create prompt.
2. Close app.
3. Move its `.md` outside `prompts`.
4. Restart.

Expected:

```text
Unavailable card
no crash
Delete enabled
Move enabled
Edit disabled
Copy disabled
Duplicate disabled
```

---

# 160. Orphan manual test

1. Close app.
2. Add arbitrary `.md` to `prompts`.
3. Keep valid primary metadata.
4. Restart.

Expected:

```text
normal library works
orphan not shown
orphan not deleted
```

---

# 161. Corruption manual test

Precondition:

```text
valid primary
matching backup
```

Corrupt only:

```text
library.json
```

Restart.

Expected:

```text
backup restored
new primary valid
recovery copy attempted
notice shown
prompt files preserved
```

---

# 162. Double corruption

Corrupt:

```text
library.json
library.backup.json
```

Restart.

Expected:

```text
fatal
no defaults
no prompt deletion
```

---

# 163. Future schema manual test

Use primary:

```json
{
  "schemaVersion": 999
}
```

Keep valid old schema-1 backup.

Expected:

```text
fatal unsupported schema

old backup NOT restored

primary untouched
```

---

# 164. Clipboard manual test

Use prompt containing:

- Markdown,
- Unicode,
- blank lines,
- code fence.

Copy.

Paste in Notepad.

Compare.

Repeat Copy rapidly.

Expected:

```text
full text
no truncation
Copied ✓
```

---

# 165. UI scaling tests

Required:

```text
100%
125%
150%
```

At:

```text
900×600
1100×760
large desktop resolution
```

Check:

- category cards,
- headings,
- dialog buttons,
- prompt list,
- breadcrumb,
- no clipped action controls.

---

# 166. Keyboard QA

Check:

```text
Tab
Shift+Tab

Name dialog:
Enter
Escape

Prompt editor:
Enter
Tab
Escape

Move:
Enter
Escape
```

Ensure focus is visible.

---

# 167. Accessibility basics

Icon buttons require:

```xml
AutomationProperties.Name="Rename category"
AutomationProperties.Name="Delete category"
```

Do not rely solely on icon shape or colour.

Hit targets:

```text
approximately >= 34 × 34 for small icon buttons
```

---

# 168. No external icons

Allowed glyphs:

```text
P
?
✎
×
```

If `✎` is found to render poorly on the actual target system:

replace with text:

```text
Edit
```

Do not add an icon package.

---

# 169. No hidden networking

The runtime application must contain no intentional:

```text
HttpClient
WebView
socket
analytics request
telemetry request
AI request
```

Offline test must pass all features.

Development NuGet restore is naturally allowed.

---

# 170. Security

Prompts are never executed.

No:

- command shell,
- PowerShell invocation,
- process execution,
- HTML execution,
- JavaScript execution.

All file paths are controlled by:

```text
AppPaths
+
internally generated GUID
```

Category names and prompt text never become filesystem paths.

---

# 171. Implementation phases

Use **14 phases**.

Every phase ends with:

```powershell
dotnet build
dotnet test
```

from the repository root unless explicitly stated otherwise.

Do not continue with failing tests.

---

# PHASE 001 — Repository bootstrap

## Purpose

Create a clean, buildable solution.

## Create

```text
global.json
PromptHelper.slnx
README.md
THIRD_PARTY_NOTICES.md
.gitignore

src/PromptHelper/
tests/PromptHelper.Tests/
```

## Tasks

1. Create solution.
2. Create WPF app.
3. Replace app csproj with authoritative content.
4. Create test csproj.
5. Add project references.
6. Add one trivial MSTest smoke test.
7. Restore.
8. Build.
9. Test.

## Gate

```text
dotnet --info → stable .NET 10
dotnet restore → PASS
dotnet build   → PASS
dotnet test    → PASS
```

## Non-goals

No product behaviour.

---

# PHASE 002 — Models, text utilities and validation

## Create

```text
Infrastructure/TextUtilities.cs

Models/CategoryRecord.cs
Models/PromptRecord.cs
Models/LibraryDocument.cs
Models/OperationResults.cs

Services/UnsupportedLibrarySchemaException.cs
Services/LibraryValidator.cs
Services/LibraryDocumentCloner.cs
```

## Tests

- text-element truncation,
- required semantic validation,
- duplicate/cycle tests.

## Gate

All Phase 002 tests pass.

---

# PHASE 003 — Paths, atomic IO and process lock

## Create

```text
AppPaths.cs
AppInstanceLock.cs
IAtomicTextWriter.cs
AtomicTextWriter.cs
IFileDeleter.cs
FileDeleter.cs
```

## Tests

- temp paths,
- atomic create,
- atomic replace,
- Unicode,
- app lock.

## Gate

Build and tests pass.

---

# PHASE 004 — Strict repositories

## Create

```text
LibraryRepository.cs
PromptRepository.cs
```

## Implement

Especially:

```text
raw schemaVersion pre-read
JsonRequired enforcement
primary commit point
best-effort backup
```

## Tests

Repository + strict JSON suite.

## Gate

Build and tests pass.

---

# PHASE 005 — Defaults and startup recovery

## Create

```text
DefaultLibraryFactory.cs
LibraryStartupService.cs
```

## Implement

Complete startup matrix.

## Tests

All first-run/recovery/future-schema tests.

## Gate

No startup-state test failing.

---

# PHASE 006 — Business service

## Create

```text
PromptLibraryService.cs
```

## Implement

- categories,
- prompts,
- move,
- duplicate,
- sorting,
- breadcrumbs,
- destinations,
- unavailable content handling.

## Tests

Full failure-injection suite.

## Gate

All business tests pass.

---

# PHASE 007 — ViewModels

## Create

```text
ObservableObject.cs already exists
BreadcrumbItemViewModel.cs
CategoryItemViewModel.cs
DestinationOptionViewModel.cs
PromptCardViewModel.cs
MainViewModel.cs
```

## Tests

- preview,
- breadcrumb refresh,
- navigation,
- operation refresh semantics.

## Gate

Build/test pass.

---

# PHASE 008 — Visual design system

## Create

```text
Styles/Theme.xaml
```

## Modify

```text
App.xaml
```

At this phase it is acceptable that the application temporarily has no functional window startup after removing template `StartupUri`.

The phase gate is compile-only for UI runtime.

## Gate

```text
XAML build PASS
all tests PASS
```

No manual product-flow test yet.

---

# PHASE 009 — Application composition + shell + category UI

This phase deliberately creates the runnable application before later UI features.

## Create/Modify

```text
App.xaml.cs
MainWindow.xaml
MainWindow.xaml.cs
NameDialog.xaml
NameDialog.xaml.cs
ConfirmDeleteDialog.xaml
ConfirmDeleteDialog.xaml.cs
ClipboardService.cs
```

ClipboardService is created now even though prompt Copy arrives next phase.

## Implement

- complete application startup composition,
- header,
- logo,
- Help button placeholder/wiring may already exist,
- breadcrumbs,
- categories,
- Add,
- rename,
- delete.

## Manual tests

Run application.

Verify:

```text
defaults
navigation
add category
rename
delete empty
reject non-empty
restart persistence
```

## Gate

```text
BUILD PASS
TEST PASS
APPLICATION START PASS
CATEGORY MANUAL PASS
```

---

# PHASE 010 — Prompt cards and editor

## Create

```text
PromptEditorDialog.xaml
PromptEditorDialog.xaml.cs
```

## Implement

- prompt list,
- create,
- edit,
- delete,
- clipboard Copy,
- unavailable prompt UI.

## Manual tests

- blank prompt,
- Markdown,
- Unicode,
- 50k prompt,
- clipboard.

## Gate

Full pass.

---

# PHASE 011 — Move and duplicate

## Create

```text
MovePromptDialog.xaml
MovePromptDialog.xaml.cs
```

## Implement

- destinations,
- Move,
- `Copy instead of move`,
- unavailable-prompt duplicate disable,
- collision-disambiguated display paths.

## Gate

Move/duplicate manual and automated tests pass.

---

# PHASE 012 — Help and UX/error polish

## Create

```text
HelpDialog.xaml
HelpDialog.xaml.cs
```

## Verify

- Help,
- exact data path,
- version,
- warnings,
- failure wording,
- owner/centering of dialogs,
- keyboard behaviour.

No new feature work.

---

# PHASE 013 — Adversarial recovery / UI QA

No features.

Execute:

- corrupt primary,
- corrupt both,
- missing metadata,
- interrupted initialization,
- future schema,
- orphan,
- missing prompt,
- lock,
- 900×600,
- DPI,
- keyboard,
- offline.

Any confirmed defect must be repaired and entire relevant suite rerun.

---

# PHASE 014 — Release and publish

## Commands

```powershell
dotnet restore

dotnet build `
  -c Release

dotnet test `
  -c Release

dotnet publish `
  src/PromptHelper/PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/publish/win-x64
```

All commands:

```text
exit code 0
```

---

# 172. No single-file publish

Do not use:

- `PublishSingleFile`,
- trimming,
- NativeAOT,
- custom packing.

Use the complete publish folder.

This minimizes runtime surprises.

---

# 173. Publish smoke

Launch:

```text
artifacts\publish\win-x64\PromptHelper.exe
```

without IDE.

Test:

```text
start
navigate
create prompt
edit
copy
move
close
restart
verify persistence
```

---

# 174. Optional Windows 10 best-effort smoke

If actual intended deployment includes an unsupported ordinary Windows 10 installation:

run the published folder there as an **additional compatibility smoke test**.

Record:

```text
Windows edition
Windows build
result
```

Do not replace the supported Windows 11 release test with this check.

---

# 175. Mandatory final QA matrix

| ID | Test | Required |
|---|---|---|
| QA-001 | clean restore | PASS |
| QA-002 | Debug build | PASS |
| QA-003 | automated tests | PASS |
| QA-004 | first start defaults | PASS |
| QA-005 | second start no duplicates | PASS |
| QA-006 | category add | PASS |
| QA-007 | category rename | PASS |
| QA-008 | duplicate sibling rejection | PASS |
| QA-009 | non-empty delete rejection | PASS |
| QA-010 | empty delete confirmation | PASS |
| QA-011 | deep hierarchy | PASS |
| QA-012 | Home prompt | PASS |
| QA-013 | prompt create | PASS |
| QA-014 | prompt edit | PASS |
| QA-015 | prompt delete | PASS |
| QA-016 | prompt move | PASS |
| QA-017 | duplicate | PASS |
| QA-018 | clipboard | PASS |
| QA-019 | Unicode | PASS |
| QA-020 | Markdown | PASS |
| QA-021 | empty prompt | PASS |
| QA-022 | 50k prompt | PASS |
| QA-023 | missing prompt file | PASS |
| QA-024 | orphan preservation | PASS |
| QA-025 | corrupt primary recovery | PASS |
| QA-026 | corrupt primary + corrupt backup | PASS |
| QA-027 | future schema safety | PASS |
| QA-028 | interrupted initialization | PASS |
| QA-029 | unknown-data init protection | PASS |
| QA-030 | backup-write failure | PASS |
| QA-031 | delete-file failure | PASS |
| QA-032 | second instance rejected | PASS |
| QA-033 | 900×600 | PASS |
| QA-034 | 125% scaling | PASS |
| QA-035 | 150% scaling | PASS |
| QA-036 | keyboard navigation | PASS |
| QA-037 | offline functionality | PASS |
| QA-038 | Release build | PASS |
| QA-039 | Release tests | PASS |
| QA-040 | self-contained publish | PASS |
| QA-041 | publish smoke | PASS |
| QA-042 | license/notices retained | PASS |

---

# 176. Release test environment record

Final QA must record:

```text
Windows edition:
Windows version/build:

.NET SDK:
dotnet --version output:

MSTest.Sdk:
4.3.3

Publish RID:
win-x64

Application version:
0.1.0
```

This makes release validation reproducible.

---

# 177. Implementation-agent rules

The coding agent must obey:

```text
1. Do not add features.

2. Do not change stack.

3. Do not add application NuGet packages.

4. MSTest.Sdk is test-only and allowed.

5. Do not add a database.

6. Do not add networking.

7. Do not weaken validation to fix tests.

8. Do not delete tests because they fail.

9. Fix root causes.

10. Run build + test after every phase.

11. Do not continue with failing gates.

12. Do not casually change persistence ordering.

13. Do not delete unknown orphan files.

14. Do not overwrite uncertain initialization data.

15. Do not restore schema-1 backup over future primary metadata.

16. Once library.json commits, backup failure is warning-only.

17. Once logical delete commits, cleanup failure is warning-only.

18. Preserve simplicity.
```

---

# 178. Phase completion report format

After every phase output:

```text
PHASE:
<id>

FILES CREATED:
...

FILES MODIFIED:
...

IMPLEMENTED:
...

AUTOMATED TESTS:
PASS / FAIL

BUILD:
PASS / FAIL

MANUAL TESTS:
PASS / FAIL / NOT APPLICABLE

KNOWN DEFECTS:
0
```

Do not claim completion with known defects.

---

# 179. Final product Definition of Done

```text
[ ] Starts successfully

[ ] Exclusive data lock works

[ ] Modern light UI

[ ] Header + logo + Help

[ ] Home

[ ] Breadcrumbs

[ ] Categories

[ ] Nested categories

[ ] Add category

[ ] Rename category

[ ] Safe empty-category deletion

[ ] Non-empty protection

[ ] Home prompts

[ ] Nested prompts

[ ] Create prompt

[ ] Edit prompt

[ ] Delete prompt

[ ] Move prompt

[ ] Duplicate prompt

[ ] Copy to clipboard

[ ] Raw Markdown preserved

[ ] Unicode preserved

[ ] Empty prompts supported

[ ] 50k prompt usable

[ ] Session persistence

[ ] Defaults exactly once

[ ] Interrupted initialization recovery

[ ] Unknown initialization data protection

[ ] Strict JSON required fields

[ ] Empty GUID protection

[ ] Future schema pre-detection

[ ] Atomic primary writes

[ ] Atomic prompt writes

[ ] Safety metadata mirror

[ ] Backup failure warning semantics

[ ] Corrupt primary recovery

[ ] Double corruption safe failure

[ ] Missing prompt graceful degradation

[ ] Orphan preservation

[ ] Offline operation

[ ] 900×600 usable

[ ] 125% scaling usable

[ ] 150% scaling usable

[ ] Keyboard basics usable

[ ] Release build passes

[ ] Tests pass

[ ] self-contained win-x64 publish passes

[ ] published executable smoke passes
```

---

# 180. Architecture Definition of Done

```text
[ ] WPF

[ ] .NET 10

[ ] net10.0-windows

[ ] no application PackageReference

[ ] strict System.Text.Json persistence

[ ] [JsonRequired] on every persisted field

[ ] raw future-schema inspection

[ ] .md prompt files

[ ] candidate-clone metadata mutations

[ ] library.json commit-point semantics

[ ] backup warning-only after commit

[ ] safe physical deletion sequencing

[ ] IAtomicTextWriter test seam

[ ] IFileDeleter test seam

[ ] no DI framework

[ ] no background persistence

[ ] no arbitrary user file paths

[ ] no recursive destructive category delete
```

---

# 181. Test Definition of Done

```text
[ ] Text utility tests

[ ] Validator tests

[ ] Strict JSON tests

[ ] Atomic writer tests

[ ] App lock tests

[ ] Repository tests

[ ] Startup/recovery tests

[ ] Business-service tests

[ ] Failure injection tests

[ ] ViewModel tests

[ ] Manual GUI tests

[ ] Manual clipboard test

[ ] Missing-file test

[ ] Orphan test

[ ] Future-schema test

[ ] DPI tests

[ ] Offline test

[ ] Publish smoke
```

---

# 182. Traceability matrix

| Requirement | Implementation owner | Phase | Verification |
|---|---|---:|---|
| Category tree | model + service | 2/6 | validator/service |
| Navigation | MainVM/UI | 7/9 | VM/manual |
| Create category | service/UI | 6/9 | service/manual |
| Rename | service/UI | 6/9 | service/manual |
| Delete category | service/UI | 6/9 | service/manual |
| Create prompt | repository/service/UI | 4/6/10 | integration/manual |
| Edit | repository/service/UI | 4/6/10 | integration/manual |
| Delete | repository/service/UI | 4/6/10 | failure injection |
| Move | service/UI | 6/11 | service/manual |
| Duplicate | repository/service/UI | 4/6/11 | failure injection |
| Clipboard | ClipboardService/UI | 9/10 | manual |
| Defaults | startup | 5 | startup tests |
| Recovery | repository/startup | 4/5 | recovery tests |
| Future schema | repository/startup | 4/5 | mandatory test |
| Modern GUI | Theme/MainWindow | 8–10 | visual QA |
| Single instance | AppInstanceLock | 3/9 | automated/manual |
| Privacy/offline | architecture | all | offline QA |
| Publish | CLI | 14 | smoke |

No major requirement lacks an implementation owner and verification path.

---

# 183. Static validation performed while producing v1.2.0

The following plan-level checks were actually performed:

```text
C# reference structural/token balance:
12 / 12 checked files PASS

XAML XML well-formedness:
8 / 8 checked files PASS

XAML event-handler name mapping:
PASS

JSON sample parsing:
PASS

Create/backup failure-state simulation:
PASS

Delete/backup failure-state simulation:
PASS

Delete/physical-cleanup failure-state simulation:
PASS

Phase dependency re-audit:
PASS

Future-schema logic re-audit:
PASS

Required-field logic re-audit:
PASS

Destination ambiguity re-audit:
PASS
```

---

# 184. Static-validation limitation

The audit environment used to produce this plan does not provide a Windows WPF build environment.

Therefore these were **not falsely claimed**:

```text
actual WPF compilation
actual MSTest execution
actual win-x64 publish
actual Windows clipboard execution
actual DPI rendering
```

They are mandatory implementation gates.

This limitation does not leave a design decision unresolved.

---

# 185. Scope re-check

The additional items introduced by audits are implementation safeguards, not new product features.

Examples:

```text
JsonRequired
future-schema pre-read
initializing.marker
failure injection
ConfirmDeleteDialog
destination disambiguation
exclusive data lock
```

They exist only to make already required functionality:

- safe,
- testable,
- deterministic,
- understandable.

---

# 186. Final implementation philosophy

Prompt Helper should remain small.

The final architecture is intentionally:

```text
WPF
+
.NET 10
+
one JSON metadata document
+
one safety metadata mirror
+
plain Markdown files
+
small services
+
small ViewModels
+
thin code-behind
+
MSTest
```

Do not make it more complicated unless a concrete confirmed defect requires it.

---

# 187. FINAL QUALITY GATE

```text
Requirement consistency:
PASS

Scope preservation:
PASS

Technology consistency:
PASS

Phase dependency consistency:
PASS

Data-model review:
PASS

Strict JSON review:
PASS

Required-property review:
PASS

Future-schema safety review:
PASS

Category-tree review:
PASS

Prompt lifecycle review:
PASS

Atomic-write review:
PASS

Commit-point review:
PASS

Backup semantics review:
PASS

Delete safety review:
PASS

First-run recovery review:
PASS

Missing-file review:
PASS

Orphan-file review:
PASS

Failure-injection testability:
PASS

Single-instance review:
PASS

Weak-agent executability:
PASS

UI architecture review:
PASS

Modern visual-design review:
PASS

Dialog behaviour review:
PASS

Accessibility-basics review:
PASS

DPI/responsive-plan review:
PASS

Security/privacy review:
PASS

Dependency review:
PASS

License-hygiene review:
PASS

Build/publish-plan review:
PASS

Automated-test plan review:
PASS

Manual-QA review:
PASS

Traceability review:
PASS

C# structural reference checks:
PASS

XAML XML checks:
PASS

XAML/C# handler mapping:
PASS

Persistence failure simulation:
PASS

Actual Windows WPF build in audit environment:
NOT EXECUTED — WINDOWS/.NET WPF ENVIRONMENT UNAVAILABLE

Actual implementation repository test run:
DEFERRED — IMPLEMENTATION REPOSITORY DOES NOT YET EXIST

CONFIRMED REMAINING PLAN DEFECTS:
0

KNOWN IMPLEMENTATION-BLOCKING AMBIGUITIES:
0

KNOWN UNRESOLVED PRODUCT DECISIONS:
0

FINAL AUDIT STATUS:
ACCEPTED
```