# Prompt Helper

Prompt Helper is a small, lightweight, local desktop application for organizing and copying reusable AI prompts. It is available for **Windows x64** and **Linux x64**.

## Bundled Premades

The top-level **Premades** library mirrors the familiar **Games** and **Tools** hierarchy and contains reusable prompts for planning, implementation, bug hunting, regression testing, release gates, and security/data-loss reviews. Its test-focused subcategories include general game QA, Unity, Windows/WPF/.NET, Web/HTML/PWA, Capacitor/mobile, CLI, and CI workflows.

The bundled pack is installed once for both new and existing libraries. Existing categories with the same path are reused, personal prompts are never overwritten, and deleting or editing a premade is respected on later starts.

## Data compatibility

Windows and Linux use the same `library.json` schema and the same `prompts/<id>.md` files. No export or conversion step is required when moving a library between the two platforms.

Canonical library JSON intentionally keeps the historical Windows CRLF byte format on both operating systems, so equivalent libraries have the same canonical bytes and SHA-256 value.

## Development requirements

- Stable .NET 10 SDK
- Windows for the WPF Windows application and Windows-specific integration tests
- Linux for the native Linux filesystem integration tests and Avalonia Linux publish

## Build

Windows/full solution:

```powershell
dotnet build PromptHelper.slnx -c Release
```

Linux desktop:

```bash
dotnet build src/PromptHelper.Desktop/PromptHelper.Desktop.csproj -c Release
```

## Test

Windows/full suite:

```powershell
dotnet test PromptHelper.slnx -c Release
```

Linux cross-platform/platform tests:

```bash
dotnet test tests/PromptHelper.Core.Tests/PromptHelper.Core.Tests.csproj -c Release
dotnet test tests/PromptHelper.Platform.Linux.Tests/PromptHelper.Platform.Linux.Tests.csproj -c Release
```

## Publish

### Windows x64

```powershell
dotnet publish src/PromptHelper/PromptHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o artifacts/windows-publish
```

### Linux x64

```bash
dotnet publish src/PromptHelper.Desktop/PromptHelper.Desktop.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -o artifacts/linux-publish
```

The Linux desktop targets X11 directly and therefore also runs through XWayland on normal Wayland desktops. Required native packages on Debian/Ubuntu are `libx11-6 libice6 libsm6 libfontconfig1`.

## Run from source

Windows:

```powershell
dotnet run --project src/PromptHelper/PromptHelper.csproj -c Release
```

Linux:

```bash
dotnet run --project src/PromptHelper.Desktop/PromptHelper.Desktop.csproj -c Release
```

## Crash-recovery guarantee

Prompt Helper's automated migration/recovery behavior is verified for abrupt application termination at enumerated durable-write and rename cuts. These tests kill the process without allowing application cleanup to run.

Abrupt VM reset, kernel failure, storage-controller reordering, and physical power loss remain outside the verified automatic-recovery guarantee. Those events are handled fail-closed/best-effort until a dedicated post-reboot VM-reset matrix is run.

## User data

Default library location:

- Windows: `%LOCALAPPDATA%\PromptHelper`
- Linux: the per-user local application-data location returned by .NET (normally `$XDG_DATA_HOME/PromptHelper` or `~/.local/share/PromptHelper`)

Prompt bodies remain local Markdown files. Prompt Helper contains no telemetry, cloud account, or network-backed prompt storage.

### Custom data folder

Both desktop applications can open a custom Prompt Helper data folder.

- Windows retains its migration workflow for moving/copying an existing library with the established safety checks.
- Linux switches to the selected library folder without deleting or copying the old folder automatically. A new/empty selected folder receives a fresh Prompt Helper library; an existing compatible folder is opened as-is.

A configured data folder must be an absolute path and must satisfy the platform's filesystem safety/authority checks.

## Appearance

Both desktop applications provide a persistent dark-mode setting. The Windows WPF build additionally yields to Windows high-contrast system colors.

## Linux filesystem safety

The Linux implementation uses platform-native primitives rather than weakening the Windows invariants:

- `O_NOFOLLOW` no-symlink opens
- `statx` filesystem-object identity with birth time
- same-directory staging
- file and parent-directory `fsync`
- atomic `rename` / `renameat2(RENAME_NOREPLACE)`
- durable ownership journal and startup reconciliation
- fail-closed recovery when exact object provenance cannot be proven
- single-instance locking through `flock`

## Release assets

v0.5.0 and later cross-platform releases provide:

- portable self-contained Windows x64 ZIP
- portable self-contained Linux x64 `.tar.gz`
- Debian/Ubuntu amd64 `.deb`
- separate SHA-256 files for every package
- SPDX SBOM and per-file checksums inside portable payloads
- optional Authenticode signing on Windows when maintainer signing secrets are configured
