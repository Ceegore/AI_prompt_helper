# cruu1.md — Prompt Helper v0.1.0 Feature Implementation Plan

## 0. Purpose

This document is the implementation blueprint for the next feature round of **Prompt Helper**, based on the released repository state at tag `v0.1.0`.

Target repository:

- `Ceegore/AI_prompt_helper`
- baseline: `v0.1.0`
- application type: Windows WPF desktop application
- target framework: `net10.0-windows`
- UI: XAML/WPF
- prompt bodies: individual `.md` files
- prompt/category metadata: `library.json` plus `library.backup.json`
- existing default data root: `%LOCALAPPDATA%\PromptHelper`
- existing test runner: `dotnet test`

This plan is deliberately explicit enough for a weak implementation model. The implementer must not redesign the application, introduce a second UI framework, replace persistence, or make unrelated behavior changes.

---

# 1. Requested features

Implement all seven requested feature areas as one coherent change set:

1. Add optional visual line wrapping/line breaks to the prompt editor via a new checkbox.
2. Give every prompt an editable headline/title while retaining the existing automatic first-line fallback.
3. Change the prompt list from full-width cards to a three-cards-per-row layout and add a delayed full-prompt hover tooltip.
4. Replace the top `?` button with a wrench/settings button, replace the read-only folder textbox flow with a proper Windows folder picker that can change the prompt-library data location, and add `Made by CeeGore` to the bottom of the dialog.
5. Replace the separate category rename and delete buttons with one wrench button that opens a small action overlay containing the existing rename and delete actions.
6. Add a compact recent-copy quick bar directly below the header. It starts empty each application session, shows at most three recently copied prompts, and gives fast access to copy them again.
7. Use the supplied Prompt Helper logo SVG as the source artwork for the Windows application icon, including the built `.exe` icon and the icon shown for the main window in the Windows taskbar.

---

# 2. Existing code map that must be respected

The relevant `v0.1.0` implementation is already split cleanly enough that the requested features should be added by extending it, not replacing it.

## 2.1 Prompt metadata

`src/PromptHelper/Models/PromptRecord.cs`

Current fields:

- `Id`
- `CategoryId`
- `SortOrder`

There is currently **no persisted title/headline**.

## 2.2 Prompt content

`src/PromptHelper/Services/PromptRepository.cs`

Prompt text is stored separately from `library.json`, as a prompt-specific Markdown file. Do not move prompt bodies into JSON.

## 2.3 Automatic title

`src/PromptHelper/ViewModels/PromptCardViewModel.cs`

`ComputePreviewTitle(...)` currently:

- uses `(Unavailable prompt)` if the prompt file cannot be loaded;
- uses `(Empty prompt)` if content is empty/whitespace;
- otherwise scans for the first non-empty line;
- trims it;
- truncates it with ellipsis to 80 characters.

This behavior is the existing automatic fallback and must remain available.

## 2.4 Main prompt UI

`src/PromptHelper/MainWindow.xaml`

Current prompt presentation:

- one vertical `ListBox`;
- every card stretches across essentially the full available line;
- title is `PreviewTitle`;
- actions are `Delete`, `Edit`, `Move`, `Copy`;
- the complete prompt is displayed in a 190px-high read-only textbox.

This is the area that becomes the three-column prompt grid.

## 2.5 Prompt editor

`src/PromptHelper/Views/PromptEditorDialog.xaml`

Current editor has:

- heading;
- one multiline prompt `TextBox`;
- Cancel;
- Save.

`PromptEditorTextBoxStyle` already has:

- `AcceptsReturn=True`;
- `AcceptsTab=True`;
- `TextWrapping=NoWrap`;
- both scrollbars available.

Therefore feature 1 should be implemented as **optional visual wrapping**, not as destructive insertion of newline characters.

## 2.6 Settings/help dialog

`src/PromptHelper/Views/HelpDialog.xaml`

Current top-right `?` button opens `HelpDialog`.

The dialog currently displays the data folder in a read-only `TextBox`; it cannot choose or persist a different root.

## 2.7 Data path startup

`src/PromptHelper/Services/AppPaths.cs`

Default root:

`%LOCALAPPDATA%\PromptHelper`

Paths below it include:

- `.app.lock`
- `initializing.marker`
- `library.json`
- `library.backup.json`
- `prompts\`
- `recovery\`

`src/PromptHelper/App.xaml.cs` currently constructs `new AppPaths()` directly during startup. Therefore a configurable folder must be resolved **before** `AppPaths` and the repositories are constructed.

## 2.8 Category actions

`src/PromptHelper/MainWindow.xaml`

Each category currently has two visible icon buttons:

- `✎` -> rename;
- `×` -> delete.

These actions already work. Feature 5 changes only their discoverability/presentation; it must reuse their existing logic.

## 2.9 Clipboard copy pipeline and session state

`src/PromptHelper/MainWindow.xaml.cs`

The current `CopyPromptButton_Click(...)` handler:

- reads the latest prompt text through `_viewModel.GetPromptContent(card.Id)`;
- sends that exact text to `ClipboardService`;
- changes the source card button from `Copy` to `Copied ✓` for about 900 ms;
- prevents duplicate execution while that card is already copying.

Feature 6 must build on this existing copy path. Do not create a second unrelated clipboard implementation for the quick bar.

The current `MainViewModel` has no recent-copy collection and no persisted clipboard history. The new quick bar should therefore be transient UI/session state owned by the view-model layer.

## 2.10 Windows executable icon baseline

`src/PromptHelper/PromptHelper.csproj`

At `v0.1.0` the project already contains an empty application-icon property:

```xml
<ApplicationIcon></ApplicationIcon>
```

The user-supplied logo SVG is the authoritative source artwork for feature 7.

WPF/Windows executables do not use an SVG directly as their PE executable icon. The implementation therefore needs to keep the SVG as source artwork and generate/commit a multi-resolution `.ico` derived from that SVG.

If the supplied SVG is not yet physically present in the implementation workspace, do **not** invent or redraw a replacement logo. Complete every other feature and report the missing source asset for the icon subtask.

---

# 3. Binding design decisions

The weak implementation model must follow these decisions exactly unless the repository changed after `v0.1.0`.

## 3.1 Do not change prompt-body storage

Prompt bodies remain separate `.md` files.

Only the optional custom headline is stored in `PromptRecord`.

## 3.2 Headline semantics

Add:

```csharp
public string? Title { get; set; }
```

to `PromptRecord`.

Meaning:

- `null` or blank after normalization = automatic title mode;
- non-empty string = user-supplied custom headline.

Display rule:

```text
if custom Title exists:
    display custom Title
else:
    use the existing ComputePreviewTitle(prompt body, availability)
```

This gives backward compatibility automatically because old `library.json` files have no `title` property and deserialize it as `null`.

### Important

Do **not** make `Title` `[JsonRequired]`.

Do **not** require migration of every old prompt.

Do **not** populate old records unnecessarily.

Do **not** remove `ComputePreviewTitle`.

## 3.3 Keep library schema version 1 for this additive optional field

The new `Title` member is backward-compatible for the updated application because missing JSON members are allowed.

For this feature round, do **not** add a risky schema migration framework merely for one nullable additive field.

A later release may choose to bump the schema when an incompatible metadata change is needed.

## 3.4 Blank headline means automatic

For create and edit flows:

```csharp
string? NormalizePromptTitle(string? input)
{
    string trimmed = (input ?? string.Empty).Trim();
    return trimmed.Length == 0 ? null : trimmed;
}
```

A user can therefore delete a previously custom title, save, and return the prompt to automatic title mode.

## 3.5 Editor line-wrap checkbox is visual only

The checkbox must toggle WPF wrapping:

Unchecked:

```text
TextWrapping = NoWrap
HorizontalScrollBarVisibility = Auto
```

Checked:

```text
TextWrapping = Wrap
HorizontalScrollBarVisibility = Disabled
```

It must **not**:

- insert `\r\n`;
- reflow the saved prompt;
- alter clipboard text;
- alter prompt files;
- replace spaces;
- change line endings.

Suggested label:

`Wrap long lines`

Suggested tooltip/help text:

`Changes only how the prompt is displayed in this editor. Saved prompt text is not modified.`

Default: unchecked, preserving v0.1.0 behavior.

## 3.6 Three prompt cards per row is a desktop layout requirement

At supported main-window sizes, the prompt area should render exactly three card columns.

Use a WPF `UniformGrid Columns="3"` inside the prompt list rather than calculating widths in code-behind.

Do not add JavaScript/CSS/web layout code.

## 3.7 Full prompt hover preview

The hover preview is a WPF tooltip attached to the entire prompt card.

Requirements:

- initial delay: **500 ms**;
- show full prompt;
- preserve existing hard line breaks;
- soft-wrap long lines;
- readable max width;
- readable max height;
- do not mutate prompt text;
- do not show broken-file garbage;
- for unavailable content, show the existing unavailable message instead.

Recommended attached properties:

```xml
ToolTipService.InitialShowDelay="500"
ToolTipService.ShowDuration="60000"
```

Recommended tooltip bounds:

- width/max width around 600–720 px;
- max height around 420–500 px;
- padding 12–16 px.

## 3.8 Folder selection must use the native WPF Windows folder picker

The project targets `net10.0-windows`, so use:

```csharp
Microsoft.Win32.OpenFolderDialog
```

No third-party folder-picker package is needed.

Do not use:

- `OpenFileDialog` pretending a file is a folder;
- manual path typing as the primary UX;
- WinForms `FolderBrowserDialog`;
- external shell commands.

## 3.9 Changing the data root must never destroy the old library

Changing the folder is potentially destructive. Use **copy + validate + switch-on-next-start**, not move/delete.

Rules:

1. User selects a folder.
2. Normalize it to an absolute full path.
3. If it equals the current data folder, treat it as no change.
4. If target contains an existing valid Prompt Helper library, do not overwrite it. Allow switching to it only after validation.
5. If target does not contain a Prompt Helper library, copy the current library data into it.
6. Never copy `.app.lock`.
7. Never copy `initializing.marker`.
8. Validate target metadata and referenced prompt files before saving the setting.
9. Only after successful validation persist the chosen root.
10. Keep the original data folder unchanged as a safety copy.
11. Inform the user that the new folder becomes active on the next application start.

This avoids trying to hot-swap repositories that were constructed with the old `AppPaths`.

## 3.10 Category wrench overlay

Use one visible wrench button per category.

On left-click, open a small WPF `ContextMenu`/popup with two actions:

- `✎ Rename`
- `× Delete`

Reuse the existing rename/delete code. Do not duplicate category mutation logic.

## 3.11 Recent-copy quick bar is session-only

The recent-copy quick bar starts empty **every time the application launches**.

Do not persist the recent list in:

- `library.json`;
- prompt `.md` files;
- `settings.json`;
- a new history file;
- the Windows registry.

This matches the explicit “starts empty” requirement and avoids turning clipboard history into new persisted user data.

## 3.12 Recent-copy ordering and duplicate semantics

The quick bar contains at most three **unique prompt IDs**.

Use newest-first ordering from left to right:

```text
slot 1 = newest copied prompt
slot 2 = second newest
slot 3 = oldest retained prompt
```

When a prompt is copied:

1. if that prompt ID is already present, remove its old quick-bar entry;
2. insert a refreshed entry at index 0;
3. while the count exceeds 3, remove the last entry.

Therefore:

```text
copy A -> [A]
copy B -> [B, A]
copy C -> [C, B, A]
copy D -> [D, C, B]
copy C -> [C, D, B]
```

Do not create duplicates such as `[C, C, B]`.

A successful copy initiated from the quick bar itself also counts as a copy and should move that prompt to the newest position.

Failed clipboard writes must **not** change recency.

## 3.13 Recent-copy tile content and action

Each recent entry is a compact two-line visual tile occupying approximately one third of the quick-bar width.

Show:

- line 1: effective prompt headline (`PreviewTitle`);
- line 2: a short compact excerpt from prompt content;
- a small dedicated `Copy` button at the right side.

The tile container itself is informational. The required action is the small `Copy` button; do not invent unrelated navigation/edit behavior for the tile.

Headline line:

- one line;
- bold/semi-bold;
- ellipsis when it does not fit.

Excerpt line:

- one line;
- smaller/subtle text;
- normalize CR/LF/tab and repeated whitespace to single spaces **for the preview only**;
- truncate visually with ellipsis;
- never modify the stored prompt body.

The quick bar should not show a heading label unless later requested. It is simply the compact row directly beneath the main header.

## 3.14 Recent-copy lifecycle consistency

When a prompt already present in the recent list is edited:

- update its recent headline and excerpt to the newly saved values;
- preserve its existing recency position unless that edit also caused a copy.

When a prompt is deleted successfully from the logical library:

- remove it from the recent list immediately.

When a prompt is moved to another category:

- keep it in the recent list; its ID/content are still valid.

When a prompt is duplicated:

- do not automatically add the duplicate; it enters the quick bar only after it is actually copied.

## 3.15 Logo SVG to Windows icon conversion

Keep the source SVG in the repository, preferably:

```text
src/PromptHelper/Assets/PromptHelperLogo.svg
```

Generate and commit:

```text
src/PromptHelper/Assets/PromptHelper.ico
```

The ICO should contain multiple Windows-relevant raster sizes, at least:

```text
16, 24, 32, 48, 64, 128, 256 px
```

Preserve transparency.

Do not stretch the SVG non-uniformly. Render it into a square transparent canvas while preserving aspect ratio if the source itself is not square.

The `.ico` is a generated release asset derived from the SVG; the application must not require ImageMagick/Inkscape/other SVG conversion software at runtime.

## 3.16 Executable and taskbar icon binding

In `PromptHelper.csproj` set:

```xml
<ApplicationIcon>Assets\PromptHelper.ico</ApplicationIcon>
```

Also include the ICO as a WPF resource so the main window can explicitly use it:

```xml
<ItemGroup>
  <Resource Include="Assets\PromptHelper.ico" />
</ItemGroup>
```

In `MainWindow.xaml`, explicitly set the main window `Icon` to the packaged ICO resource. This removes dependence on shell fallback behavior and makes the intended taskbar/window icon explicit.

Use a valid WPF pack/resource URI for the actual assembly/resource layout, for example:

```xml
Icon="/PromptHelper;component/Assets/PromptHelper.ico"
```

If that URI does not compile/load in the actual project, use the equivalent valid pack URI for the same embedded resource; do not switch to an unrelated icon.

---

# 4. New/changed files

## 4.1 Files that must change

At minimum:

- `src/PromptHelper/Models/PromptRecord.cs`
- `src/PromptHelper/Models/OperationResults.cs`
- `src/PromptHelper/Services/LibraryDocumentCloner.cs`
- `src/PromptHelper/Services/LibraryValidator.cs` if title validation is added
- `src/PromptHelper/Services/PromptLibraryService.cs`
- `src/PromptHelper/ViewModels/PromptCardViewModel.cs`
- `src/PromptHelper/ViewModels/MainViewModel.cs`
- `src/PromptHelper/PromptHelper.csproj`
- `src/PromptHelper/Views/PromptEditorDialog.xaml`
- `src/PromptHelper/Views/PromptEditorDialog.xaml.cs`
- `src/PromptHelper/MainWindow.xaml`
- `src/PromptHelper/MainWindow.xaml.cs`
- `src/PromptHelper/Styles/Theme.xaml`
- `src/PromptHelper/App.xaml.cs`
- tests under `tests/PromptHelper.Tests/`

## 4.2 Recommended new files

Create:

- `src/PromptHelper/Models/AppSettings.cs`
- `src/PromptHelper/Services/AppSettingsRepository.cs`
- `src/PromptHelper/Services/DataFolderMigrationService.cs`
- `src/PromptHelper/ViewModels/RecentPromptViewModel.cs`
- `src/PromptHelper/Views/SettingsDialog.xaml`
- `src/PromptHelper/Views/SettingsDialog.xaml.cs`
- `src/PromptHelper/Assets/PromptHelperLogo.svg` — use the supplied logo source asset; do not invent it
- `src/PromptHelper/Assets/PromptHelper.ico` — generated multi-resolution Windows icon
- optional developer helper: `tools/GenerateAppIcon.ps1`

After SettingsDialog is working, delete/retire:

- `Views/HelpDialog.xaml`
- `Views/HelpDialog.xaml.cs`

Do not leave both dialogs reachable from the same button.

---

# 5. Feature 2 first: persisted editable prompt headline

Implement this before the grid because the grid will bind to the new effective headline.

## 5.1 `PromptRecord`

Change to:

```csharp
public sealed class PromptRecord
{
    [JsonRequired]
    public Guid Id { get; set; }

    [JsonRequired]
    public Guid? CategoryId { get; set; }

    [JsonRequired]
    public long SortOrder { get; set; }

    public string? Title { get; set; }
}
```

Do not annotate `Title` with `[JsonRequired]`.

## 5.2 `LibraryDocumentCloner`

Every cloned prompt must copy `Title`.

Required addition:

```csharp
Title = x.Title
```

Missing this produces title loss during almost every service operation because service mutations use a cloned document.

This is a critical regression trap.

## 5.3 `PromptDisplayRecord`

Change from:

```csharp
public sealed record PromptDisplayRecord(
    Guid Id,
    string Content,
    bool IsContentAvailable,
    string? LoadError);
```

to:

```csharp
public sealed record PromptDisplayRecord(
    Guid Id,
    string? Title,
    string Content,
    bool IsContentAvailable,
    string? LoadError);
```

## 5.4 `PromptLibraryService.GetPrompts`

Return `p.Title` with every display record.

## 5.5 `CreatePrompt`

Change signature:

```csharp
public OperationResult<PromptRecord> CreatePrompt(
    Guid? categoryId,
    string content,
    string? title)
```

Normalize:

```csharp
string? normalizedTitle = NormalizePromptTitle(title);
```

Create record:

```csharp
var newPrompt = new PromptRecord
{
    Id = newPromptId,
    CategoryId = categoryId,
    SortOrder = nextSortOrder,
    Title = normalizedTitle
};
```

Returned clone must include title.

### Compatibility helper

Keep an overload temporarily if it prevents widespread test breakage:

```csharp
public OperationResult<PromptRecord> CreatePrompt(Guid? categoryId, string content)
    => CreatePrompt(categoryId, content, null);
```

This is optional but recommended during implementation.

## 5.6 `EditPrompt`

Change signature to:

```csharp
public OperationResult EditPrompt(
    Guid promptId,
    string content,
    string? title)
```

Because edit now touches both the prompt `.md` and `library.json`, preserve rollback behavior.

Safe sequence:

1. Find target.
2. Read old prompt content.
3. Clone current library document.
4. Find target in clone.
5. Set normalized title in clone.
6. Validate clone.
7. Write new prompt body.
8. Commit cloned metadata.
9. If metadata commit throws, best-effort restore old prompt body, then rethrow.
10. On success assign `_document = candidate`.
11. Return backup warning from metadata commit.

Pseudo-code:

```csharp
string oldContent = _promptRepo.Read(promptId);
var candidate = LibraryDocumentCloner.Clone(_document);
var candidateTarget = candidate.Prompts.Single(p => p.Id == promptId);
candidateTarget.Title = NormalizePromptTitle(title);
LibraryValidator.Validate(candidate);

_promptRepo.Update(promptId, content);

try
{
    CommitResult commit = _libraryRepo.Commit(candidate);
    _document = candidate;
    return new OperationResult(commit.Warning);
}
catch
{
    try
    {
        _promptRepo.Update(promptId, oldContent);
    }
    catch
    {
        // best effort rollback; do not hide original exception
    }

    throw;
}
```

Do not update `_document` before metadata commit succeeds.

## 5.7 Duplicate prompt

Current duplication calls `CreatePrompt(...)`.

A duplicate must preserve the custom title:

```csharp
return CreatePrompt(destinationCategoryId, content, target.Title);
```

If `target.Title` is `null`, the duplicate continues using automatic title mode.

## 5.8 Move prompt

No special title code is needed beyond making sure `LibraryDocumentCloner` copies Title.

## 5.9 `PromptCardViewModel`

Constructor becomes conceptually:

```csharp
public PromptCardViewModel(
    Guid id,
    string? customTitle,
    string content,
    bool isContentAvailable,
    string? loadError)
```

Properties:

```csharp
public string? CustomTitle { get; }
public string PreviewTitle { get; }
```

Compute:

```csharp
CustomTitle = string.IsNullOrWhiteSpace(customTitle) ? null : customTitle.Trim();

PreviewTitle = CustomTitle is not null
    ? TextUtilities.TruncateWithEllipsis(CustomTitle, 80)
    : ComputePreviewTitle(content, isContentAvailable);
```

Keep `ComputePreviewTitle(...)` behavior unchanged.

Add a useful editor value:

```csharp
public string EditableHeadline => CustomTitle ?? PreviewTitle;
```

This lets an existing old prompt open with its automatically generated headline prefilled.

## 5.10 `MainViewModel`

When refreshing cards, pass the new title:

```csharp
Prompts.Add(new PromptCardViewModel(
    prompt.Id,
    prompt.Title,
    prompt.Content,
    prompt.IsContentAvailable,
    prompt.LoadError));
```

Change create signature:

```csharp
public OperationResult<PromptRecord> CreatePrompt(string content, string? title)
```

Change edit signature:

```csharp
public OperationResult EditPrompt(Guid promptId, string content, string? title)
```

## 5.11 Prompt editor XAML

Extend the editor to five rows:

1. dialog heading;
2. headline row;
3. wrap checkbox;
4. prompt body editor;
5. buttons.

Suggested headline block:

```xml
<Grid Grid.Row="1" Margin="0,0,0,10">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>

    <StackPanel Orientation="Horizontal" Margin="0,0,0,5">
        <TextBlock Text="Headline"
                   FontWeight="SemiBold"/>
        <TextBlock Text=" &lt;optional&gt;"
                   Opacity="0.5"/>
    </StackPanel>

    <TextBox x:Name="HeadlineTextBox"
             Grid.Row="1"
             Style="{StaticResource ModernTextBoxStyle}"/>
</Grid>
```

The literal `<optional>` must render visibly and at roughly 50% opacity.

Do not use a placeholder library.

## 5.12 Prompt editor code-behind

Constructor:

```csharp
public PromptEditorDialog(
    string title,
    string initialText,
    string initialHeadline = "")
```

Initialize:

```csharp
HeadlineTextBox.Text = initialHeadline;
```

Add:

```csharp
public string? ResultHeadline { get; private set; }
```

On save:

```csharp
ResultText = EditorTextBox.Text;

string trimmedHeadline = HeadlineTextBox.Text.Trim();
ResultHeadline = trimmedHeadline.Length == 0
    ? null
    : trimmedHeadline;

DialogResult = true;
Close();
```

### Create flow

`AddPromptButton_Click`:

- keep both `promptText` and `headlineText` between retry loops;
- construct editor with both values;
- after successful dialog, call `_viewModel.CreatePrompt(promptText, dialog.ResultHeadline)`.

If save fails, reopen with both fields preserved.

### Edit flow

For an existing card, initialize:

```csharp
string headlineText = card.EditableHeadline;
```

After the user saves, pass `dialog.ResultHeadline`.

If they clear it, automatic title mode returns.

---

# 6. Feature 1: optional editor line wrapping

Implement in the same PromptEditorDialog change.

## 6.1 XAML

Add:

```xml
<CheckBox x:Name="WrapLinesCheckBox"
          Grid.Row="2"
          Content="Wrap long lines"
          Style="{StaticResource ModernCheckBoxStyle}"
          Margin="0,0,0,10"
          Checked="WrapLinesCheckBox_Changed"
          Unchecked="WrapLinesCheckBox_Changed"
          ToolTip="Changes only how the prompt is displayed in this editor. Saved prompt text is not modified."/>
```

Default `IsChecked` should be false unless explicitly set otherwise.

## 6.2 Code-behind

Add one method:

```csharp
private void WrapLinesCheckBox_Changed(object sender, RoutedEventArgs e)
{
    bool wrap = WrapLinesCheckBox.IsChecked == true;

    EditorTextBox.TextWrapping = wrap
        ? TextWrapping.Wrap
        : TextWrapping.NoWrap;

    EditorTextBox.HorizontalScrollBarVisibility = wrap
        ? ScrollBarVisibility.Disabled
        : ScrollBarVisibility.Auto;
}
```

If `TextWrapping`/`ScrollBarVisibility` namespaces are missing, use `System.Windows` and `System.Windows.Controls`.

## 6.3 Test requirement

Given a prompt with one 500-character line:

- checkbox off: editor has one logical line and horizontal scrolling;
- checkbox on: visual text wraps across several display lines;
- `ResultText` before/after toggling is byte-for-byte/string-for-string identical.

---

# 7. Feature 3: three prompt cards per row

Do this only after headline support works.

## 7.1 Replace current vertical item panel

Keep a scrollable `ListBox`, but use a three-column `UniformGrid`.

Example:

```xml
<ListBox ItemsSource="{Binding Prompts}"
         ItemContainerStyle="{StaticResource PromptGridListBoxItemStyle}"
         Background="Transparent"
         BorderThickness="0"
         ScrollViewer.HorizontalScrollBarVisibility="Disabled"
         ScrollViewer.VerticalScrollBarVisibility="Auto">
    <ListBox.ItemsPanel>
        <ItemsPanelTemplate>
            <UniformGrid Columns="3"/>
        </ItemsPanelTemplate>
    </ListBox.ItemsPanel>

    ...
</ListBox>
```

The existing recycling virtualization assumptions no longer apply with a standard `UniformGrid`.

Remove misleading virtualization properties rather than pretending the layout is still virtualized.

For this app, rendering a prompt library as three lightweight cards is acceptable. Do not introduce a custom virtualizing panel in this feature round.

## 7.2 Add a dedicated item-container style

In `Theme.xaml`:

```xml
<Style x:Key="PromptGridListBoxItemStyle"
       TargetType="ListBoxItem"
       BasedOn="{StaticResource FlatListBoxItemStyle}">
    <Setter Property="Margin" Value="0,0,12,12"/>
    <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
    <Setter Property="VerticalContentAlignment" Value="Stretch"/>
</Style>
```

Avoid a right margin on every third item only; a small consistent internal gap is visually acceptable.

## 7.3 Redesign the card for narrow width

The current top row containing the title plus four full-size buttons will not fit reliably.

Use:

- row 0: title;
- row 1: four compact actions in a 4-column `UniformGrid`;
- row 2: short prompt preview.

Suggested structure:

```xml
<Border Style="{StaticResource CardBorderStyle}"
        Margin="0,0,12,12"
        ToolTipService.InitialShowDelay="500"
        ToolTipService.ShowDuration="60000">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <TextBlock Text="{Binding PreviewTitle}"
                   FontSize="14"
                   FontWeight="Bold"
                   TextTrimming="CharacterEllipsis"
                   Margin="0,0,0,10"/>

        <UniformGrid Grid.Row="1"
                     Columns="4"
                     Margin="0,0,0,10">
            <!-- Delete/Edit/Move/Copy buttons -->
        </UniformGrid>

        <TextBlock Grid.Row="2"
                   Text="{Binding DisplayText}"
                   FontFamily="{StaticResource MonospaceFontFamily}"
                   FontSize="12"
                   TextWrapping="Wrap"
                   MaxHeight="110"
                   ClipToBounds="True"/>
    </Grid>
</Border>
```

Add a compact button style rather than shrinking the existing global button style.

Recommended `CompactActionButtonStyle`:

- font size 11–12;
- min height 28;
- small horizontal padding;
- 2–3 px internal margin.

Keep the labels recognizable:

- Delete
- Edit
- Move
- Copy / Copied ✓

Do not remove actions.

## 7.4 Full hover tooltip

On the card Border:

```xml
<Border.ToolTip>
    <ToolTip MaxWidth="700"
             MaxHeight="480"
             Padding="0">
        <Border Background="{StaticResource SurfaceBrush}"
                BorderBrush="{StaticResource BorderBrush}"
                BorderThickness="1"
                CornerRadius="8"
                Padding="14">
            <ScrollViewer MaxHeight="440"
                          VerticalScrollBarVisibility="Auto"
                          HorizontalScrollBarVisibility="Disabled">
                <TextBlock Text="{Binding DisplayText}"
                           FontFamily="{StaticResource MonospaceFontFamily}"
                           FontSize="13"
                           TextWrapping="Wrap"/>
            </ScrollViewer>
        </Border>
    </ToolTip>
</Border.ToolTip>
```

WPF `TextBlock` preserves newline characters contained in the bound string while `TextWrapping=Wrap` also wraps long physical lines.

## 7.5 UX requirements

Verify:

- tooltip is not immediate;
- tooltip opens after approximately 0.5 seconds;
- quickly passing across cards does not spam previews;
- full text is readable;
- multi-line Markdown stays multi-line;
- very long lines wrap;
- unavailable prompt displays the existing unavailable text;
- three cards stay visible per row at the application's minimum supported width.

---

# 8. Feature 5: one category wrench + small action overlay

## 8.1 MainWindow XAML

Remove the two always-visible category buttons.

Add one wrench button:

```xml
<Button x:Name="CategoryActionsButton"
        Content="🔧"
        Style="{StaticResource IconButtonStyle}"
        ToolTip="Category actions"
        AutomationProperties.Name="Category actions"
        Click="CategoryActionsButton_Click">
    <Button.ContextMenu>
        <ContextMenu DataContext="{Binding PlacementTarget.DataContext,
                                           RelativeSource={RelativeSource Self}}">
            <MenuItem Header="✎ Rename"
                      Click="RenameCategoryMenuItem_Click"/>
            <MenuItem Header="× Delete"
                      Click="DeleteCategoryMenuItem_Click"/>
        </ContextMenu>
    </Button.ContextMenu>
</Button>
```

If the binding to ContextMenu DataContext is unreliable in testing, resolve the category from `ContextMenu.PlacementTarget.DataContext` in code-behind.

Do not add a new persistence concept for this UI.

## 8.2 Code-behind refactor

Refactor current handlers so the mutation code exists once.

Create helpers:

```csharp
private void RenameCategory(CategoryItemViewModel cat)
{
    // move existing RenameCategoryButton_Click body here
}

private void DeleteCategory(CategoryItemViewModel cat)
{
    // move existing DeleteCategoryButton_Click body here
}
```

Then:

```csharp
private void CategoryActionsButton_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button button && button.ContextMenu != null)
    {
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }
}
```

Menu item handlers:

```csharp
private void RenameCategoryMenuItem_Click(object sender, RoutedEventArgs e)
{
    if (TryGetCategoryFromMenuItem(sender, out var cat))
        RenameCategory(cat);
}

private void DeleteCategoryMenuItem_Click(object sender, RoutedEventArgs e)
{
    if (TryGetCategoryFromMenuItem(sender, out var cat))
        DeleteCategory(cat);
}
```

Helper must not depend on fragile visual-tree parent walking if `PlacementTarget.DataContext` is available.

## 8.3 Behavior that must remain unchanged

- rename validation;
- duplicate sibling-name protection;
- cannot delete non-empty category;
- delete confirmation dialog;
- persistence warnings;
- refresh after mutation.

---

# 9. Feature 4: wrench/settings menu + native folder picker

This is the riskiest feature and should be implemented after 1/2/3/5 are stable.

## 9.1 Top-right button

In `MainWindow.xaml`, replace:

```xml
Content="?"
ToolTip="Help"
AutomationProperties.Name="Help"
```

with:

```xml
Content="🔧"
ToolTip="Tools and settings"
AutomationProperties.Name="Tools and settings"
```

Rename:

```text
HelpButton
```

to:

```text
SettingsButton
```

and:

```text
HelpButton_Click
```

to:

```text
SettingsButton_Click
```

## 9.2 Rename HelpDialog to SettingsDialog

Create:

- `Views/SettingsDialog.xaml`
- `Views/SettingsDialog.xaml.cs`

Suggested title:

`Tools & Settings — Prompt Helper`

The dialog may retain the small usage hints currently shown in HelpDialog.

Add:

- current data folder;
- read-only selected-path textbox;
- `Browse…` button;
- `Save` button;
- `Cancel`/`Close` button;
- version;
- footer `Made by CeeGore`.

Footer must be visually separated and subtle, e.g.:

```xml
<TextBlock Text="Made by CeeGore"
           HorizontalAlignment="Center"
           Foreground="{StaticResource SubtleTextBrush}"
           Opacity="0.75"
           FontSize="12"
           Margin="0,18,0,0"/>
```

The user explicitly requested this spelling/capitalization: **Made by CeeGore**.

## 9.3 Fixed bootstrap settings file

The application needs one tiny configuration file whose location never depends on the selected library data folder.

Add:

```csharp
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string? DataRootPath { get; set; }
}
```

Recommended fixed settings path:

```text
%LOCALAPPDATA%\PromptHelper\settings.json
```

This path remains fixed even after prompt data is moved elsewhere.

Do not store this preference inside the movable `library.json`, because the application would need to know the selected folder before it can read `library.json`.

## 9.4 `AppSettingsRepository`

Responsibilities:

- load settings from fixed path;
- if missing, return default settings (`DataRootPath=null`);
- save with `AtomicTextWriter`;
- reject malformed schema cleanly;
- normalize blank path to null;
- expose `GetEffectiveDataRoot()`.

Effective root:

```text
configured nonblank DataRootPath
OR
existing AppPaths default root
```

Do not store arbitrary UI state here in this feature round.

## 9.5 Startup change in App.xaml.cs

Current code starts with:

```csharp
var paths = new AppPaths();
```

New sequence:

```text
1. Create AtomicTextWriter.
2. Create/load AppSettingsRepository.
3. Resolve effective data root.
4. Construct AppPaths(effectiveDataRoot).
5. Continue existing lock/repository/startup flow.
```

The writer can be created before `AppPaths`; it does not depend on the data root.

Avoid constructing two different service graphs.

## 9.6 Native folder picker

In `SettingsDialog.xaml.cs`:

```csharp
using Microsoft.Win32;
```

Browse handler:

```csharp
var picker = new OpenFolderDialog
{
    Title = "Select Prompt Helper data folder",
    Multiselect = false,
    InitialDirectory = Directory.Exists(SelectedDataFolder)
        ? SelectedDataFolder
        : CurrentDataFolder
};

if (picker.ShowDialog(this) == true)
{
    SelectedDataFolder = picker.FolderName;
    DataFolderTextBox.Text = SelectedDataFolder;
}
```

Use whichever supported initial-directory property compiles for the target SDK (`InitialDirectory`/`DefaultDirectory` according to the actual API in the project SDK). Do not guess if the compiler rejects one; use the .NET 10 WPF `OpenFolderDialog` API.

## 9.7 DataFolderMigrationService

Create a service that prepares the selected location without deleting source data.

Recommended method:

```csharp
public DataFolderChangeResult PrepareTarget(
    string currentRoot,
    string selectedRoot)
```

Result should tell the caller:

- normalized selected root;
- whether an existing library was found;
- whether data was copied;
- optional warning.

### Path normalization

Use:

```csharp
Path.GetFullPath(path)
```

Compare Windows paths case-insensitively after trimming trailing directory separators.

Reject:

- empty/whitespace;
- path that resolves to a file;
- path that cannot be created/accessed;
- target prompt file paths that would escape root.

### Same directory

Return no-op.

### Existing Prompt Helper library in target

If target contains `library.json`:

1. Read/validate it using `LibraryRepository.InspectAndDeserialize`.
2. Verify every referenced prompt file exists in `target\prompts\<guid>.md`.
3. Do not copy the current library over it.
4. Allow the setting to switch to this existing valid library.

If metadata is invalid, fail and leave settings unchanged.

### Target without an existing library

Create needed target directories.

Copy from current root:

- `library.json` if present;
- `library.backup.json` if present;
- every file in `prompts\`;
- `recovery\` files if desired/present.

Do not copy:

- `.app.lock`;
- `initializing.marker`;
- `settings.json`;
- temporary files;
- unknown unrelated files.

Do not overwrite existing target files.

If a target file already exists unexpectedly, abort rather than overwriting it.

After copy:

1. read target `library.json`;
2. validate;
3. verify all referenced prompt files exist;
4. only then report success.

If validation fails:

- do not save settings;
- leave the source untouched;
- best-effort delete only files created by this attempted migration;
- never delete files that existed before the attempt.

## 9.8 Save setting

Only after successful `PrepareTarget(...)`:

```csharp
_settingsRepository.Save(new AppSettings
{
    SchemaVersion = 1,
    DataRootPath = prepared.NormalizedTargetRoot
});
```

Then set:

```csharp
RestartRequired = true;
DialogResult = true;
```

Main window shows:

```text
The data folder has been saved.
Prompt Helper will use it the next time the application starts.
The previous data folder was left unchanged as a safety copy.
```

No hot reload is required in this feature round.

## 9.9 Cancel semantics

If user browses to a folder but presses Cancel:

- do not save settings;
- ideally do not copy anything yet.

Therefore run migration on **Save**, not on Browse.

---

# 10. Feature 6: recent-copy quick bar

Implement this after effective prompt headlines and the basic prompt-card refactor are stable, because it depends on both the copy flow and the final headline behavior.

## 10.1 Add `RecentPromptViewModel`

Create:

`src/PromptHelper/ViewModels/RecentPromptViewModel.cs`

Recommended shape:

```csharp
public sealed class RecentPromptViewModel : ObservableObject
{
    private string _headline;
    private string _excerpt;
    private string _copyButtonText = "Copy";
    private bool _isCopying;

    public RecentPromptViewModel(Guid id, string headline, string excerpt)
    {
        Id = id;
        _headline = headline;
        _excerpt = excerpt;
    }

    public Guid Id { get; }

    public string Headline
    {
        get => _headline;
        private set => SetProperty(ref _headline, value);
    }

    public string Excerpt
    {
        get => _excerpt;
        private set => SetProperty(ref _excerpt, value);
    }

    public string CopyButtonText
    {
        get => _copyButtonText;
        set => SetProperty(ref _copyButtonText, value);
    }

    public bool IsCopying
    {
        get => _isCopying;
        set => SetProperty(ref _isCopying, value);
    }

    public void RefreshDisplay(string headline, string excerpt)
    {
        Headline = headline;
        Excerpt = excerpt;
    }
}
```

Do not store full prompt text in this view model unless a concrete UI need arises. The copy action should read the current prompt content from the existing repository path at copy time.

## 10.2 Add compact excerpt helper

Put the helper in an existing text-utility location if one exists (`TextUtilities` is preferred) rather than duplicating it in XAML code-behind.

Required behavior:

```csharp
public static string CreateCompactPreview(string? content, int maxTextElements = 160)
```

Algorithm:

1. null -> empty string;
2. replace CR/LF/tab and other whitespace runs with a single ordinary space for preview only;
3. trim;
4. truncate safely using existing text-element-aware truncation utilities;
5. return the result.

Representative implementation shape:

```csharp
public static string CreateCompactPreview(string? content, int maxTextElements = 160)
{
    if (string.IsNullOrWhiteSpace(content))
    {
        return string.Empty;
    }

    string normalized = Regex.Replace(content, @"\s+", " ").Trim();
    return TruncateWithEllipsis(normalized, maxTextElements);
}
```

If adding `Regex` would be inconsistent with existing utility style, implement equivalent whitespace normalization without Regex. Correctness matters more than the mechanism.

This helper must never be used for saved/copy text.

## 10.3 MainViewModel recent collection

Add:

```csharp
public ObservableCollection<RecentPromptViewModel> RecentPrompts { get; }
```

Initialize it once in the constructor.

Do **not** clear it inside `Refresh()`, because normal category navigation/refresh must not erase session recency.

Add:

```csharp
public void RecordSuccessfulPromptCopy(
    Guid promptId,
    string headline,
    string currentContent)
```

Required logic:

```csharp
var existing = RecentPrompts.FirstOrDefault(x => x.Id == promptId);
if (existing != null)
{
    RecentPrompts.Remove(existing);
}

var item = existing ?? new RecentPromptViewModel(
    promptId,
    headline,
    TextUtilities.CreateCompactPreview(currentContent));

item.RefreshDisplay(
    headline,
    TextUtilities.CreateCompactPreview(currentContent));

RecentPrompts.Insert(0, item);

while (RecentPrompts.Count > 3)
{
    RecentPrompts.RemoveAt(RecentPrompts.Count - 1);
}
```

Add:

```csharp
public void RemoveRecentPrompt(Guid promptId)
```

which removes the matching item if present.

Add a helper such as:

```csharp
private void RefreshRecentPromptDisplay(Guid promptId)
```

After a successful prompt edit and `Refresh()`:

1. find the recent entry;
2. if absent, do nothing;
3. find the refreshed current `PromptCardViewModel` with same ID;
4. update headline/excerpt from its current title/content;
5. do not change list position.

Because edit originates from the currently displayed prompt card, the refreshed card should still be available in the current category.

## 10.4 Integrate logical deletion

After `DeletePrompt(...)` succeeds:

```csharp
RemoveRecentPrompt(promptId);
```

Do this even when the operation returns a non-fatal backup/file-deletion warning, because the prompt was logically removed from library metadata.

Do not remove recent entries when moving prompts.

## 10.5 Refactor clipboard handling into one safe path

Current source-card copy logic should not be duplicated for quick-bar copy.

Refactor the underlying operation into a helper that:

1. reads the current prompt content;
2. writes it to clipboard;
3. **only after clipboard success** records recent-copy state;
4. returns/propagates errors to the calling UI handler.

Recommended code-behind helper:

```csharp
private string CopyPromptToClipboard(
    Guid promptId,
    string effectiveHeadline)
{
    string textToCopy = _viewModel.GetPromptContent(promptId);
    _clipboardService.CopyText(textToCopy);
    _viewModel.RecordSuccessfulPromptCopy(
        promptId,
        effectiveHeadline,
        textToCopy);
    return textToCopy;
}
```

The existing prompt-card handler can keep its `Copy` -> `Copied ✓` feedback around this shared helper.

The new quick-bar handler can keep independent feedback on `RecentPromptViewModel.CopyButtonText`.

Do not record recency before `_clipboardService.CopyText(...)` succeeds.

## 10.6 Quick-copy handler

Add:

```csharp
private async void RecentPromptCopyButton_Click(
    object sender,
    RoutedEventArgs e)
```

Required behavior:

- resolve `RecentPromptViewModel` from DataContext;
- ignore if `IsCopying`;
- use the shared copy helper;
- set `CopyButtonText = "Copied ✓"` for about 900 ms;
- reset on success/failure;
- use the existing clipboard failure message style;
- because a successful quick copy is a new copy event, the item may move to slot 1.

Be careful: moving the item in the observable collection during the handler must not invalidate the local `item` object reference.

## 10.7 Add the row directly below the header

`MainWindow.xaml` currently has:

```xml
<RowDefinition Height="64"/>
<RowDefinition Height="*"/>
```

Change the root row structure to approximately:

```xml
<Grid.RowDefinitions>
    <RowDefinition Height="64"/>
    <RowDefinition Height="56"/>
    <RowDefinition Height="*"/>
</Grid.RowDefinitions>
```

Keep the existing header at row 0.

Add the quick bar at row 1.

Move the existing main-body grid from row 1 to row 2.

The quick row remains present but visually empty when there are zero recent entries. Do not insert placeholder text.

Suggested container:

```xml
<Border Grid.Row="1"
        Background="{StaticResource AppBackgroundBrush}"
        Padding="32,5">
    <ItemsControl ItemsSource="{Binding RecentPrompts}">
        <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate>
                <UniformGrid Columns="3"/>
            </ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        ...
    </ItemsControl>
</Border>
```

This reserves three equal-width slots and naturally leaves unused slots blank.

## 10.8 Recent tile XAML

Each item should stay small: approximately 44–48 px internal height within the 56 px row.

Suggested shape:

```xml
<DataTemplate DataType="{x:Type vm:RecentPromptViewModel}">
    <Border Style="{StaticResource RecentPromptTileStyle}"
            Margin="0,0,10,0"
            Height="46">
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>

            <Grid Grid.Column="0" Margin="0,0,8,0">
                <Grid.RowDefinitions>
                    <RowDefinition Height="*"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>

                <TextBlock Text="{Binding Headline}"
                           FontSize="12"
                           FontWeight="SemiBold"
                           TextTrimming="CharacterEllipsis"
                           VerticalAlignment="Center"/>

                <TextBlock Grid.Row="1"
                           Text="{Binding Excerpt}"
                           FontSize="10.5"
                           Foreground="{StaticResource SubtleTextBrush}"
                           TextTrimming="CharacterEllipsis"
                           VerticalAlignment="Center"/>
            </Grid>

            <Button Grid.Column="1"
                    Content="{Binding CopyButtonText}"
                    Style="{StaticResource CompactCopyButtonStyle}"
                    Click="RecentPromptCopyButton_Click"
                    ToolTip="Copy prompt"
                    AutomationProperties.Name="Copy recent prompt"/>
        </Grid>
    </Border>
</DataTemplate>
```

Do not introduce a one-off Boolean converter solely for `IsCopying` if the project has no converter infrastructure. The handler's `IsCopying` guard is sufficient, or expose a simple `IsCopyEnabled` property if desired.

Do not put a `Button` inside another `Button`.

## 10.9 Styling

Add `RecentPromptTileStyle` and `CompactCopyButtonStyle` to `Theme.xaml`.

The recent row should be visually subordinate to the main prompt grid:

- smaller text;
- modest border;
- subtle background;
- no oversized shadows;
- no card height larger than needed for two text lines;
- clear hover/focus state on the Copy button.

## 10.10 Edge cases

Explicitly test:

- first successful copy;
- second/third/fourth unique copy;
- copying same prompt repeatedly;
- copy failure;
- quick-bar copy failure;
- edit a recent prompt;
- delete a recent prompt;
- move a recent prompt;
- navigate between categories;
- empty prompt;
- unavailable prompt cannot be copied from the main card, therefore should not enter recent list;
- application restart returns recent list to empty.

---

# 11. Feature 7: use the logo SVG as the Windows EXE/taskbar icon

## 11.1 Source and generated assets

Use the supplied logo SVG as source-of-truth artwork.

Preferred paths:

```text
src/PromptHelper/Assets/PromptHelperLogo.svg
src/PromptHelper/Assets/PromptHelper.ico
```

If the SVG already exists under another sensible repository path, keep one source-of-truth copy and adjust project paths instead of duplicating it.

The ICO is derived from the SVG and may be committed because Windows executable icon embedding requires an ICO-compatible resource.

## 11.2 Generate a multi-resolution ICO

Preferred icon sizes:

```text
16
24
32
48
64
128
256
```

All sizes must preserve alpha transparency.

If ImageMagick is available during development, a suitable one-time conversion command is:

```powershell
magick -background none `
  "src/PromptHelper/Assets/PromptHelperLogo.svg" `
  -define icon:auto-resize=256,128,64,48,32,24,16 `
  "src/PromptHelper/Assets/PromptHelper.ico"
```

This is development tooling only.

If ImageMagick is unavailable, use another trustworthy SVG->ICO tool available in the implementation environment. Do not add a heavy runtime graphics dependency to Prompt Helper merely to convert its own icon at startup.

Do not claim the generated ICO is correct without opening/inspecting it at small and large sizes.

## 11.3 Optional reproducibility helper

If desired, add:

`tools/GenerateAppIcon.ps1`

It should:

1. resolve repository root;
2. verify source SVG exists;
3. verify `magick` exists;
4. run the conversion;
5. verify output `.ico` exists and is non-empty;
6. fail loudly otherwise.

The application build itself should consume the committed ICO and must not require this helper.

## 11.4 Configure the executable icon

Update:

`src/PromptHelper/PromptHelper.csproj`

From:

```xml
<ApplicationIcon></ApplicationIcon>
```

To:

```xml
<ApplicationIcon>Assets\PromptHelper.ico</ApplicationIcon>
```

Add the ICO as a WPF resource if needed for the explicit window icon:

```xml
<ItemGroup>
  <Resource Include="Assets\PromptHelper.ico" />
</ItemGroup>
```

Do not remove the existing LICENSE/THIRD_PARTY_NOTICES content entries.

## 11.5 Configure the main-window/taskbar icon

In `MainWindow.xaml`, add an `Icon` attribute to the `<Window>` root pointing to the same ICO resource.

Preferred form:

```xml
Icon="/PromptHelper;component/Assets/PromptHelper.ico"
```

The main window must use the same artwork as the executable.

No separate taskbar-only artwork is required.

## 11.6 Windows shell cache caveat

Windows Explorer/taskbar can cache executable icons.

If a developer manually replaces the ICO but still sees an older icon while testing:

- first rebuild/publish to a fresh output directory;
- launch the newly built executable;
- unpin/re-pin an old taskbar shortcut if applicable;
- do not treat shell cache as proof that project icon embedding failed until the fresh binary is inspected.

Do not add icon-cache deletion commands to normal application behavior.

## 11.7 Icon acceptance

Verify on the built/published executable, not only inside Visual Studio:

- Explorer shows the logo for `PromptHelper.exe`;
- running application's taskbar button shows the logo;
- Alt+Tab/window chrome uses the logo where Windows displays it;
- icon is legible at small size;
- transparency is correct;
- no white/black square background appeared unintentionally;
- no unrelated default .NET/WPF icon remains.

---

# 12. File-by-file checklist

## `Models/PromptRecord.cs`

- [ ] add nullable `Title`;
- [ ] no `[JsonRequired]`.

## `Models/OperationResults.cs`

- [ ] add Title to `PromptDisplayRecord`;
- [ ] add `DataFolderChangeResult` here or in a separate file.

## `Models/AppSettings.cs`

- [ ] create settings model.

## `Services/LibraryDocumentCloner.cs`

- [ ] clone prompt `Title`.

## `Services/PromptLibraryService.cs`

- [ ] return title from GetPrompts;
- [ ] create with optional title;
- [ ] edit body + title safely;
- [ ] duplicate preserves title;
- [ ] normalize blank title to null.

## `ViewModels/PromptCardViewModel.cs`

- [ ] retain automatic title helper;
- [ ] store optional custom title;
- [ ] effective display chooses custom first;
- [ ] expose editable headline.

## `ViewModels/MainViewModel.cs`

- [ ] pass titles while refreshing;
- [ ] create/edit methods accept optional headline;
- [ ] create session-only `RecentPrompts` collection;
- [ ] record successful copies newest-first;
- [ ] enforce maximum 3 unique prompt IDs;
- [ ] refresh recent display after prompt edit;
- [ ] remove recent entry after logical prompt deletion;
- [ ] do not clear recent entries during normal `Refresh()`/navigation.

## `ViewModels/RecentPromptViewModel.cs`

- [ ] create compact recent-entry VM;
- [ ] expose ID, Headline, Excerpt, CopyButtonText, IsCopying;
- [ ] allow headline/excerpt refresh without changing prompt ID.

## `Views/PromptEditorDialog.xaml`

- [ ] add Headline field;
- [ ] visible `<optional>` at ~50% opacity;
- [ ] add wrap checkbox;
- [ ] preserve existing body editor;
- [ ] keep Cancel/Save.

## `Views/PromptEditorDialog.xaml.cs`

- [ ] result headline;
- [ ] wrapper toggle;
- [ ] preserve exact ResultText.

## `MainWindow.xaml`

- [ ] top wrench;
- [ ] category wrench;
- [ ] three-column prompt list;
- [ ] compact prompt actions;
- [ ] tooltip with 500ms delay;
- [ ] add fixed compact row directly below header;
- [ ] bind recent row to `RecentPrompts`;
- [ ] recent row supports maximum 3 equal-width items;
- [ ] each recent item shows headline + one-line excerpt + small Copy button;
- [ ] set main-window icon to Prompt Helper ICO resource.

## `MainWindow.xaml.cs`

- [ ] settings dialog handler;
- [ ] preserve create/edit retry state for text and headline;
- [ ] category action helper refactor;
- [ ] no duplicate mutation logic;
- [ ] centralize underlying prompt-to-clipboard operation;
- [ ] record recent history only after successful clipboard copy;
- [ ] add recent quick-copy handler with independent `Copied ✓` feedback.

## `Views/SettingsDialog.*`

- [ ] native OpenFolderDialog;
- [ ] data folder selection;
- [ ] save/cancel;
- [ ] version;
- [ ] `Made by CeeGore`.

## `Services/AppSettingsRepository.cs`

- [ ] fixed configuration location;
- [ ] atomic save;
- [ ] default load.

## `Services/DataFolderMigrationService.cs`

- [ ] validate;
- [ ] safe copy;
- [ ] no overwrite;
- [ ] no deletion of old library;
- [ ] verify target.

## `App.xaml.cs`

- [ ] load settings before AppPaths;
- [ ] selected root feeds AppPaths;
- [ ] pass settings/migration dependency to MainWindow or SettingsDialog.

## `Theme.xaml`

- [ ] compact action button style;
- [ ] recent prompt tile style;
- [ ] compact recent Copy button style;
- [ ] any settings-dialog styles required;
- [ ] do not break existing global styles.

## `PromptHelper.csproj`

- [ ] replace empty `ApplicationIcon` with `Assets\PromptHelper.ico`;
- [ ] include the same ICO as a WPF resource if required by `MainWindow.Icon`;
- [ ] preserve existing licensing content entries.

## `Assets/PromptHelperLogo.svg` + `Assets/PromptHelper.ico`

- [ ] use supplied logo SVG as source artwork;
- [ ] do not invent a replacement logo;
- [ ] generate multi-resolution ICO;
- [ ] preserve transparency and aspect ratio;
- [ ] inspect at small and large sizes.

---

# 13. Implementation phases for a weak model

Do not implement all files randomly.

## Phase A — baseline safety

1. Checkout the exact branch/tag-derived working branch.
2. Run:
   - `dotnet build`
   - `dotnet test`
3. Record results.
4. Do not proceed if baseline is already failing unless the failures are known environment-only issues.

Expected gate: current tests pass.

## Phase B — title metadata only

Implement:

- PromptRecord Title;
- cloner;
- display record;
- service create/edit/duplicate;
- card VM;
- Main VM;
- tests.

Do **not** change UI layout yet.

Run build/tests.

Gate: zero regression failures.

## Phase C — editor headline + wrap checkbox

Implement PromptEditorDialog and MainWindow create/edit integration.

Run build/tests.

Manual test create/edit/cancel/retry.

## Phase D — three-column cards + tooltip

Change prompt card XAML and styles.

Run build/tests.

Manual UI test at:

- default 1100x760;
- minimum window size;
- larger window;
- Windows scaling 100%;
- Windows scaling 125% or 150% if available.

## Phase E — recent-copy quick bar

Implement:

- `RecentPromptViewModel`;
- compact excerpt utility;
- `MainViewModel.RecentPrompts`;
- newest-first/unique/max-3 recency behavior;
- shared clipboard helper;
- quick-bar row XAML;
- quick-copy feedback;
- edit/delete lifecycle updates.

Run build/tests.

Manually test copy sequence:

```text
A -> B -> C -> D -> C
```

Expected final order:

```text
C, D, B
```

Confirm normal navigation does not clear the bar and restart does.

## Phase F — category wrench overlay

Refactor old rename/delete logic into helpers.

Add menu.

Run build/tests.

Manual category create/rename/delete/non-empty-delete tests.

## Phase G — settings/data-folder infrastructure

Implement:

- AppSettings;
- repository;
- migration service;
- startup selection.

Run unit tests before adding UI.

## Phase H — settings dialog

Replace HelpDialog with SettingsDialog.

Use OpenFolderDialog.

Add footer.

Manual migration tests.

## Phase I — application/executable icon

Only begin icon binding after the supplied source SVG is available.

Implement:

- source SVG asset placement;
- generated multi-resolution ICO;
- `ApplicationIcon`;
- main-window Icon binding.

Run build.

Publish to a fresh output directory and manually inspect Explorer + taskbar icon.

If the SVG source is unavailable, do not fabricate an asset; report this single asset-dependent subtask as unverified/incomplete while continuing all other tests.

## Phase J — final regression

Run:

```text
dotnet clean
dotnet build
dotnet test
dotnet publish src/PromptHelper/PromptHelper.csproj -c Release
```

Then run the application and execute the full acceptance matrix below, including the recent-copy and icon checks.

---

# 14. Required automated tests

Do not stop at compilation.

Add or extend tests under `tests/PromptHelper.Tests`.

## 14.1 Headline tests

### Old library compatibility

Load a schema-1 JSON prompt without `title`.

Expected:

- deserializes;
- title is null;
- library validates;
- prompt displays automatic title.

### Explicit title

Prompt with:

```text
Title = "My custom headline"
Content first line = "Different first line"
```

Expected card title:

`My custom headline`

### Blank title

Create/edit with:

```text
"   "
```

Expected persisted title:

`null`

Expected card title:

automatic first-line title.

### Custom title survives unrelated operations

Create custom title, then:

- move prompt;
- duplicate prompt;
- rename category;
- create another category.

Expected:

- original title preserved;
- duplicate has same custom title;
- no title loss from document cloning.

### Clearing a title

Edit custom title to blank.

Expected:

- metadata `Title=null`;
- display reverts to automatic fallback.

### Empty prompt

No custom title + empty body.

Expected:

`(Empty prompt)`

### Unavailable prompt

Missing prompt file.

Expected:

`(Unavailable prompt)`

## 14.2 Edit transaction tests

Inject metadata-write failure after prompt-body write.

Expected:

- edit returns failure;
- old body is restored if rollback succeeds;
- `_document` title remains old;
- no silent partial metadata update.

Test backup synchronization warning separately; primary metadata success should still be treated as a successful edit with warning.

## 14.3 Wrap tests

A pure helper test may not cover WPF UI state, so at minimum make the wrapping update method simple and manually test.

Required invariant:

```text
toggling wrap does not alter EditorTextBox.Text
```

## 14.4 Grid/view-model tests

No business logic should move into XAML code-behind merely for the grid.

Existing VM tests must still pass.

Add tests only if new effective-title properties have logic.

## 14.5 App settings tests

- missing settings -> default data root;
- valid configured path -> selected path;
- whitespace path -> default;
- malformed JSON -> explicit controlled failure or safe documented fallback;
- settings save is valid JSON;
- roundtrip retains path.

## 14.6 Data-folder migration tests

Use temporary directories.

### Empty target

Source has:

- valid `library.json`;
- backup;
- 2 prompt files.

Expected:

- copied;
- target validates;
- source unchanged.

### Existing valid target library

Expected:

- target is not overwritten;
- it is accepted as switch target.

### Existing corrupt target metadata

Expected:

- reject;
- settings unchanged;
- source unchanged.

### Target file collision

Expected:

- reject without overwrite.

### Missing referenced prompt in target

Expected:

- reject.

### Copy failure

Use fault injection or read-only/inaccessible path where practical.

Expected:

- settings unchanged;
- source unchanged.

### Do not copy lock

Expected target has no copied `.app.lock`.

## 14.7 Recent-copy quick-bar tests

Add focused unit tests for `MainViewModel`/recent-history logic.

### Starts empty

New MainViewModel.

Expected:

```text
RecentPrompts.Count == 0
```

### Adds successful copies newest-first

Record A, then B, then C.

Expected:

```text
[C, B, A]
```

### Fourth unique copy evicts oldest

Record D.

Expected:

```text
[D, C, B]
```

A is gone.

### Existing prompt moves instead of duplicating

From:

```text
[D, C, B]
```

record C again.

Expected:

```text
[C, D, B]
```

Count remains 3.

### Refresh/navigation does not erase recency

Record A/B, call normal view-model `Refresh()` and navigate categories.

Expected A/B remain.

### Edit refreshes display without changing order

Recent list:

```text
[B, A]
```

Edit A's headline/body successfully.

Expected:

- order remains `[B, A]`;
- A headline/excerpt reflect new values.

### Delete removes recent item

Recent list contains A.

Delete A successfully.

Expected A removed even if a non-fatal backup/file cleanup warning is returned.

### Compact excerpt is preview-only

Input contains multiple line breaks/tabs/repeated spaces.

Expected:

- preview is normalized/truncated;
- original string remains exactly unchanged.

### Clipboard failure does not alter history

This behavior sits at the UI/clipboard integration boundary. Use a fault-injecting clipboard abstraction if practical. If `ClipboardService` is not abstracted and adding an interface is reasonable, introduce a small `IClipboardService` solely to test this failure path; otherwise cover it with a manual failure-path check and do not over-engineer the application.

Required invariant:

```text
history changes only after clipboard write succeeds
```

## 14.8 Icon configuration tests

Do not rely on an image library merely to unit-test the icon.

Add a lightweight source/config regression test if the existing test suite already performs repository/XAML configuration checks.

Verify:

- `PromptHelper.csproj` has a non-empty `ApplicationIcon`;
- configured path ends in the intended `PromptHelper.ico`;
- ICO file exists in the repository/build input;
- ICO file is non-empty;
- `MainWindow.xaml` references the same icon resource.

Actual shell/taskbar rendering remains a manual Windows acceptance test.

## 14.9 Existing regression suites

Run every existing test. Do not selectively run only new tests for final acceptance.

---

# 15. Required manual UI acceptance matrix

## 15.1 Prompt creation

1. Open `+ Prompt`.
2. Confirm Headline field exists.
3. Confirm `<optional>` is clearly visible but about 50% opacity.
4. Leave headline blank.
5. Enter multi-line prompt.
6. Save.
7. Card title must use automatic first non-empty line.
8. Copy must copy full prompt exactly.

## 15.2 Custom headline on creation

1. Add prompt.
2. Headline: `Release audit`.
3. Body starts with unrelated long text.
4. Save.
5. Card headline is `Release audit`.
6. Body remains unchanged.

## 15.3 Edit headline

1. Edit existing prompt.
2. Confirm title/headline is editable.
3. Change headline only.
4. Save.
5. Prompt body unchanged.
6. New headline shown.

## 15.4 Return to auto headline

1. Edit a custom-title prompt.
2. Delete all text from Headline.
3. Save.
4. Card uses automatic first-line title again.

## 15.5 Wrap option

1. Edit a prompt containing a very long physical line.
2. Checkbox off: horizontal line/scroll behavior.
3. Checkbox on: soft wrapped.
4. Toggle several times.
5. Save.
6. Reopen.
7. Prompt body text must be identical.

## 15.6 Three-column layout

With at least seven prompts:

```text
row 1: 3
row 2: 3
row 3: 1
```

No card should span the full window.

Buttons remain clickable.

Titles do not overlap actions.

## 15.7 Tooltip delay

1. Move pointer across a card for < 0.5s: no tooltip.
2. Hover > 0.5s: tooltip appears.
3. Full prompt visible.
4. Explicit line breaks preserved.
5. Long lines wrapped.
6. Move away: tooltip disappears normally.

## 15.8 Category action menu

1. Category shows one wrench instead of pen + X.
2. Click wrench.
3. Small action overlay opens.
4. Rename works.
5. Delete works.
6. Delete non-empty category still refuses.
7. Clicking elsewhere closes overlay.

## 15.9 Top settings wrench

1. Header shows wrench, not `?`.
2. Wrench opens tools/settings dialog.
3. Current data folder shown.
4. Browse opens native Windows folder selector.
5. Version shown.
6. Footer says exactly `Made by CeeGore`.

## 15.10 Change data folder to empty folder

1. Select empty temporary folder.
2. Save.
3. App reports restart requirement.
4. Current app continues using old folder until exit.
5. Restart.
6. Prompt library matches old library.
7. New edits save in new root.
8. Old root remains intact.

## 15.11 Switch to an existing Prompt Helper folder

1. Prepare a valid second library folder.
2. Select it.
3. Save/restart.
4. Second library opens.
5. Its files were not overwritten by the first library.

## 15.12 Recent-copy bar basic behavior

Start the application fresh.

Expected:

- compact row exists directly below header;
- row contains no prompt entries.

Then copy prompts A, B, C.

Expected visible order left-to-right:

```text
C | B | A
```

Each item shows:

- headline;
- small one-line body excerpt;
- small Copy button.

Copy D.

Expected:

```text
D | C | B
```

A disappears.

Copy C again.

Expected:

```text
C | D | B
```

No duplicate C exists.

## 15.13 Recent-copy bar lifecycle

With recent prompts present:

1. navigate to other categories -> history remains;
2. edit a recent prompt -> headline/excerpt update but position remains;
3. move a recent prompt -> entry remains;
4. delete a recent prompt -> entry disappears;
5. use a quick-bar Copy button -> clipboard contains the current full prompt, not the truncated excerpt;
6. close/relaunch application -> recent row is empty again.

Also confirm the body excerpt's whitespace normalization never changes the underlying prompt file or copied prompt.

## 15.14 Windows EXE/taskbar icon

Build/publish to a fresh directory.

Verify:

1. `PromptHelper.exe` shows the supplied logo in Windows Explorer;
2. launch that exact binary;
3. taskbar button shows the same logo;
4. Alt+Tab/window chrome uses the expected icon where applicable;
5. icon remains recognizable at small size;
6. transparent regions remain transparent;
7. no default WPF/.NET icon remains.

If an old pinned shortcut still shows a stale icon, unpin/re-pin or test the fresh binary before diagnosing project failure.

---

# 16. Error handling requirements

Do not swallow user-impacting errors.

The existing application already catches common filesystem/security exceptions in many UI paths. Preserve that style.

For settings/migration, handle at least:

- `IOException`
- `UnauthorizedAccessException`
- `SecurityException`
- `InvalidDataException`
- `ArgumentException`
- `NotSupportedException`

Error messages must state:

- what failed;
- which selected folder was involved when safe to show;
- that the existing library was left unchanged when true.

Do not claim success if only part of a migration copied.

For recent-copy behavior:

- a clipboard exception must leave recent ordering unchanged;
- quick-copy UI must reset from any temporary `Copied ✓` state after an error;
- deleting a prompt must remove its recent entry only after logical deletion succeeds;
- an unavailable/deleted prompt encountered unexpectedly from the recent row should show the existing controlled copy error and remove or disable the stale recent entry rather than crashing.

For the icon:

- do not set `ApplicationIcon` to a missing file and leave the project unbuildable;
- generate/verify the ICO first, then wire the project property;
- if the source SVG is missing, report that asset dependency instead of inventing artwork.

---

# 17. Accessibility and usability requirements

For every new button:

- useful ToolTip;
- `AutomationProperties.Name`;
- keyboard focusability retained.

For tooltip:

- sufficient contrast;
- readable font;
- bounded size.

For `<optional>`:

- opacity ~0.5, but still readable.

For compact card actions:

- do not reduce target height to unusably tiny values;
- aim for at least ~28–32 px in this desktop utility.

For category/settings wrench:

- same icon meaning in both locations;
- tooltip distinguishes `Category actions` vs `Tools and settings`.

For the recent-copy bar:

- each small Copy button has a clear tooltip and `AutomationProperties.Name`;
- keyboard focus can reach the quick Copy buttons in a predictable order;
- headline/excerpt truncation must not be the only way to discover what is copied: tooltip may expose the full headline if needed, while the actual action always copies the full prompt;
- do not make the two-line row so small that Copy controls become difficult to activate.

---

# 18. Non-goals

Do not add:

- cloud sync;
- accounts;
- analytics;
- telemetry;
- prompt search;
- drag-and-drop;
- category reordering;
- Markdown rendering;
- syntax highlighting;
- custom icon UI packages;
- persisted clipboard/recent-prompt history;
- more than three recent-copy entries;
- main-tile navigation/edit behavior in the recent-copy row;
- runtime SVG-to-ICO conversion;
- a new database;
- MVVM framework migration;
- dependency injection container;
- web server;
- Electron;
- WinUI rewrite.

These are outside this request.

---

# 19. Common failure traps

A weak model is especially likely to make these mistakes. Explicitly check all of them.

1. **Adds `Title` but forgets `LibraryDocumentCloner`:**
   custom titles disappear after unrelated mutations.

2. **Adds title only to card VM, not persistence:**
   title disappears after restart.

3. **Marks Title as required:**
   old libraries fail to load.

4. **Replaces auto-title behavior:**
   blank title should still work.

5. **Wrap checkbox rewrites prompt text:**
   prohibited.

6. **Keeps four large buttons in one narrow card header:**
   causes clipping at three-column width.

7. **Tooltip shows truncated preview instead of full prompt:**
   must bind to `DisplayText`.

8. **Tooltip has no 500ms delay:**
   causes distracting popups.

9. **Changes data root after services are already constructed:**
   current repositories still point at old root.

10. **Moves/deletes old library immediately:**
    data-loss risk.

11. **Copies `.app.lock` into target:**
    can falsely block startup.

12. **Overwrites an existing library in target:**
    data loss.

13. **Stores selected root only inside selected root:**
    chicken-and-egg startup bug.

14. **Uses file picker instead of folder picker:**
    wrong UX.

15. **Creates new rename/delete logic instead of reusing existing handlers:**
    behavior divergence.

16. **Leaves old `?` button or old category action buttons visible:**
    feature not actually complete.

17. **Runs only new tests:**
    regressions can remain.

18. **Persists the recent-copy list:**
    violates the requirement that it starts empty each application session.

19. **Adds duplicate recent entries for the same prompt:**
    re-copy must move the existing prompt to newest position.

20. **Updates recency before clipboard success:**
    a failed copy must not appear as a successful recent copy.

21. **Copies the quick-bar excerpt instead of the full prompt:**
    the excerpt is display-only; clipboard must read the current full prompt file.

22. **Clears recent history from `MainViewModel.Refresh()`:**
    category navigation would unexpectedly erase the session quick bar.

23. **Leaves deleted prompts in the recent row:**
    creates stale quick actions and can expose orphaned prompt files.

24. **Uses the SVG directly as `<ApplicationIcon>`:**
    Windows executable icon embedding requires an ICO-compatible resource.

25. **Generates only one huge ICO bitmap:**
    small taskbar/Explorer sizes may look poor; use a multi-resolution icon.

26. **Invents a substitute logo because the SVG is missing:**
    the supplied logo is authoritative; missing asset is an explicit dependency.

27. **Tests only Visual Studio/window chrome and not the published EXE/taskbar:**
    the user specifically requires Windows executable and taskbar icon behavior.

---

# 20. Suggested final test commands

From repository root:

```powershell
dotnet --info
dotnet clean
dotnet build
dotnet test
dotnet publish src/PromptHelper/PromptHelper.csproj -c Release -o artifacts/publish-check
dotnet run --project src/PromptHelper/PromptHelper.csproj
```

For the icon acceptance check, also launch the freshly published `PromptHelper.exe` from `artifacts/publish-check` and inspect its Explorer/taskbar icon on Windows.

Do not report a command as passing unless it actually ran and returned success.

If exact local toolchain execution is unavailable, explicitly report it as an unexecuted environment check; do not invent a pass.

---

# 21. Definition of done

This feature round is complete only when all of the following are true:

- [ ] existing `v0.1.0` behavior still works;
- [ ] old libraries load without title migration;
- [ ] every prompt has a visible effective headline;
- [ ] custom headline is editable;
- [ ] blank headline uses automatic first-line fallback;
- [ ] duplicate retains custom headline;
- [ ] editor wrap checkbox works without content mutation;
- [ ] prompt area displays three cards per row;
- [ ] narrow cards remain usable;
- [ ] hover tooltip appears after ~0.5s;
- [ ] tooltip displays full formatted prompt;
- [ ] top `?` is gone;
- [ ] top wrench opens settings/tools dialog;
- [ ] native Windows folder picker works;
- [ ] data-root selection persists;
- [ ] empty target can receive a validated copy safely;
- [ ] existing valid target can be selected without overwrite;
- [ ] old data folder is preserved;
- [ ] `Made by CeeGore` is present;
- [ ] category pen/X pair is gone;
- [ ] category wrench opens rename/delete overlay;
- [ ] category mutation behavior remains unchanged;
- [ ] recent-copy row exists directly below header and starts empty each launch;
- [ ] first/second/third successful copies create 1/2/3 recent entries;
- [ ] fourth unique copy evicts the oldest;
- [ ] re-copying an existing recent prompt moves it to newest without duplication;
- [ ] recent entry shows headline + compact body excerpt + small Copy button;
- [ ] quick Copy copies the current complete prompt, never the excerpt;
- [ ] failed clipboard operations do not change recent ordering;
- [ ] editing a recent prompt refreshes its headline/excerpt;
- [ ] deleting a recent prompt removes its quick entry;
- [ ] normal category navigation does not clear recent entries;
- [ ] recent entries are not persisted across restart;
- [ ] supplied logo SVG remains the source artwork;
- [ ] multi-resolution ICO is generated and committed;
- [ ] built/published `.exe` uses the Prompt Helper icon;
- [ ] running main window/taskbar uses the same icon;
- [ ] `dotnet build` passes;
- [ ] all existing + new tests pass;
- [ ] manual acceptance matrix passes;
- [ ] no unrelated redesign or scope expansion landed.

---

# 22. Final instruction to the implementation agent

Implement the phases in the order given. After every phase, build and run the relevant tests. Fix failures before proceeding.

Do not reinterpret the requested UX into a different feature.

Do not delete or silently migrate user data.

Do not persist the session-only recent-copy history.

Do not invent a replacement logo if the supplied SVG asset is unavailable.

Do not claim successful testing that was not actually performed.

At the end, provide:

1. files changed;
2. concise feature summary;
3. automated test results;
4. manual checks performed;
5. any check that could not be executed;
6. any residual risk.

The implementation is not complete while any requested feature, regression, or known medium-or-higher defect remains.
