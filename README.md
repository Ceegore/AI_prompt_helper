# Prompt Helper

Prompt Helper is a small local Windows desktop application for organizing and
copying reusable AI prompts.

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

## Run

```powershell
dotnet run --project src/PromptHelper/PromptHelper.csproj -c Release
```

## User data

By default, Prompt Helper stores its library in:

`%LOCALAPPDATA%\PromptHelper`

The data folder can be changed from the top-right wrench (**Tools and settings**) dialog. A custom data folder becomes active after restarting Prompt Helper.

The small bootstrap configuration and safety backup files remain at:

- `%LOCALAPPDATA%\PromptHelper\settings.json`
- `%LOCALAPPDATA%\PromptHelper\settings.backup.json`

Prompt bodies remain local `.md` files; Prompt Helper does not upload prompts or usage data over the network.

## Privacy

Prompt Helper operates strictly locally and offline. It does not contain telemetry, cloud accounts, or network dependencies.