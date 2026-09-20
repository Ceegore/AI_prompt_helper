# Prompt Helper

Prompt Helper is a small, lightweight, local Windows desktop application for organizing and copying reusable AI prompts.

## Bundled Premades

The top-level **Premades** library mirrors the familiar **Games** and **Tools** hierarchy and contains reusable prompts for planning, implementation, bug hunting, regression testing, release gates, and security/data-loss reviews. Its test-focused subcategories include general game QA, Unity, Windows/WPF/.NET, Web/HTML/PWA, Capacitor/mobile, CLI, and CI workflows.

The bundled pack is installed once for both new and existing libraries. Existing categories with the same path are reused, personal prompts are never overwritten, and deleting or editing a premade is respected on later starts.

## Development requirements

- Windows
- stable .NET 10 SDK

## Build

```powershell
dotnet build PromptHelper.slnx -c Release
```

## Test

```powershell
dotnet test PromptHelper.slnx -c Release
```

## Publish (Self-Contained win-x64)

```powershell
dotnet publish src/PromptHelper/PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/publish-check
```

## Run

```powershell
dotnet run --project src/PromptHelper/PromptHelper.csproj -c Release
```

## Crash-Recovery Guarantee

Prompt Helper's automated migration recovery is verified for abrupt application termination.
Process termination is exercised at the enumerated durable-write and rename cuts. These tests kill the process
without allowing application cleanup to run.

Abrupt VM reset, kernel failure, storage-controller reordering, and physical power loss are
outside the verified automatic-recovery guarantee. Those events remain best-effort and
fail-closed until a dedicated post-reboot VM-reset test matrix is run; process-kill evidence
must not be presented as proof of power-loss durability.

## User Data & Data Folder Transitions

By default, Prompt Helper stores its library in:

`%LOCALAPPDATA%\PromptHelper`

The active data folder can be changed from the top-right wrench icon (**Tools and settings**):

- **Selecting an EMPTY folder**: The current library and all prompts are copied to the new folder while preserving the previous folder as an intact safety copy. Prompt Helper will close immediately; reopen the application to start using the new data folder.
- **Selecting an EXISTING Prompt Helper library**: The current library is **not** copied, merged, or overwritten. Prompt Helper prompts for explicit confirmation, updates the data-folder setting, and closes immediately. Reopening the application opens the pre-existing library at the chosen location.

## Appearance

Open **Tools and settings** and use the **Dark mode** switch to change the appearance immediately. The choice is saved locally. Windows high-contrast mode always takes priority so the application continues to use the system accessibility colors.

### Target Folder Constraints

A configured data folder must:
- Be a fully qualified, absolute filesystem path.
- Not be a drive volume root (such as `C:\` or `D:\`).
- Not be nested inside the current data folder, nor contain the current data folder.
- Not be nested inside or contain the `%LOCALAPPDATA%\PromptHelper` bootstrap directory (unless selecting the exact default root).
- Support standard create, atomic replace (`File.Replace`), and delete write capabilities.
- Not be actively held/locked by another running instance of Prompt Helper.

### Settings Recovery Authority

The application bootstrap configuration is stored at:

- `%LOCALAPPDATA%\PromptHelper\settings.json` (authoritative primary)
- `%LOCALAPPDATA%\PromptHelper\settings.backup.json` (automatic safety backup)

If `settings.json` is missing or corrupt, Prompt Helper automatically recovers the data-folder configuration from `settings.backup.json`. Settings created by a newer schema version are never downgraded or overwritten.

## Privacy & Offline Execution

Prompt Helper operates strictly locally and offline. Prompt bodies remain local `.md` files on disk. The application does not contain telemetry, cloud accounts, or external network dependencies.

## Release Assets

- Portable self-contained Windows x64 ZIP (no .NET installation required)
- Separate SHA-256 checksum for verifying the downloaded ZIP
- SPDX software bill of materials and per-file checksums inside the ZIP
- Unsigned by default; optional Authenticode signing remains available for maintainers who already own a certificate
