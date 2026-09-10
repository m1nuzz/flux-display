# Flux Display

Windows 11 tray app for saving and applying display presets. One preset can cover one or several monitors, each with its own resolution, refresh rate, and scale.

Presets live in `%LOCALAPPDATA%\FluxDisplay\presets.json` and are applied with `ChangeDisplaySettingsEx`.

[![CI](https://github.com/m1nuzz/flux-display/actions/workflows/ci.yml/badge.svg)](https://github.com/m1nuzz/flux-display/actions/workflows/ci.yml)

## Features

- Multi-monitor presets: pick several displays and set mode/scale per display
- Apply from the main window or the tray menu
- Identify overlays so you can tell which physical monitor is which
- Active-preset badge when the current modes match a saved preset
- Confirm-before-apply, start with Windows, start minimized, theme
- Unpackaged portable build and sideload MSIX from CI

## Requirements

- Windows 11 (or Windows 10 1809+)
- .NET 8 SDK
- Windows 11 SDK 22621 to build the WinUI app

The Core library (`src/FluxDisplay.Core`) builds and tests on Linux. The app itself is WinUI 3 / Windows only.

## Build

```powershell
dotnet restore FluxDisplay.sln
dotnet build FluxDisplay.sln -c Release -p:Platform=x64
dotnet test tests/FluxDisplay.Core.Tests/FluxDisplay.Core.Tests.csproj -c Release
```

Portable unpackaged publish:

```powershell
dotnet publish src/FluxDisplay.App/FluxDisplay.App.csproj `
  -c Release `
  --framework net8.0-windows10.0.22621.0 `
  --runtime win-x64 `
  --self-contained true `
  --output ./artifacts/FluxDisplay-win-x64 `
  -p:Platform=x64 `
  -p:WindowsPackageType=None `
  -p:WindowsAppSDKSelfContained=true
```

Run `FluxDisplay.App.exe` from the publish folder. Windows App SDK is included.

Local smoke (Windows):

```powershell
pwsh -NoProfile -File ./scripts/smoke.ps1 -Configuration Release -Platform x64
```

Core-only on Linux:

```bash
dotnet test tests/FluxDisplay.Core.Tests/FluxDisplay.Core.Tests.csproj --configuration Release
```

CI (`.github/workflows/ci.yml`) runs Core tests on Ubuntu, then builds portable ZIP + test-signed MSIX on Windows.

## Usage

1. Open the app and create a preset.
2. Select one or more monitors (Identify if you need numbers on screen).
3. Set resolution, refresh rate, and scale for each selected display.
4. Name it and create.
5. Apply from the preset list or from the tray (`Monitor N · Hz`, or `Monitors 1+2 · 240/60 Hz`).

v1 JSON (single monitor, no `targets`) is migrated to v2 on load.

## Layout

```
src/FluxDisplay.Core     models, validator, JSON repository
src/FluxDisplay.App      WinUI 3 app, tray, display APIs, wizard
tests/FluxDisplay.Core.Tests
tests/FluxDisplay.App.Tests
scripts/smoke.ps1
```

Stack: C# 12, .NET 8, Windows App SDK 1.6, WinUI 3, CommunityToolkit.Mvvm, H.NotifyIcon.

## License

MIT. See `LICENSE`.
