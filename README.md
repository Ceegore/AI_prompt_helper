# Prompt Helper

Prompt Helper is a small, lightweight, local Windows desktop application for organizing and copying reusable AI prompts.

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

## User Data & Data Folder Transitions

By default, Prompt Helper stores its library in:

`%LOCALAPPDATA%\PromptHelper`

The active data folder can be changed from the top-right wrench icon (**Tools and settings**):

- **Selecting an EMPTY folder**: The current library and all prompts are copied to the new folder while preserving the previous folder as an intact safety copy. Prompt Helper will close immediately; reopen the application to start using the new data folder.
- **Selecting an EXISTING Prompt Helper library**: The current library is **not** copied, merged, or overwritten. Prompt Helper prompts for explicit confirmation, updates the data-folder setting, and closes immediately. Reopening the application opens the pre-existing library at the chosen location.

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

- Release asset pending: `PromptHelperLogo.svg`