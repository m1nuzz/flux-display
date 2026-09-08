# FluxDisplay

Native Windows 11 utility for saving and restoring monitor display presets (resolution, refresh rate, color depth, scale) from a desktop window or system tray. Presets are stored as JSON under `%LOCALAPPDATA%\FluxDisplay\presets.json` and applied via `ChangeDisplaySettingsEx`.

## Build and run

Prerequisites: .NET 8 SDK, Windows 11 SDK (22621) for the WinUI app. Core library builds on any platform.

```powershell
dotnet restore
dotnet build -c Release
dotnet test
dotnet publish -p:Platform=x64 -p:WindowsPackageType=None
msbuild /p:GenerateAppxPackageOnBuild=true
```

Common variants:

```powershell
dotnet restore
dotnet build FluxDisplay.sln -c Release -p:Platform=x64
dotnet test tests/FluxDisplay.Core.Tests/FluxDisplay.Core.Tests.csproj -c Release
dotnet publish src/FluxDisplay.App/FluxDisplay.App.csproj -c Release -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true
msbuild FluxDisplay.sln /p:Configuration=Release /p:Platform=x64 /p:GenerateAppxPackageOnBuild=true /p:AppxBundle=Never /p:AppxPackageDir="artifacts/Packages/"
```

Linux smoke (Core only):

```bash
dotnet test --configuration Release
```

## Project structure

- `src/FluxDisplay.Core` — platform-neutral models (`DisplayInfo`, `Rect`, `DisplayMode`, `DISP_CHANGE`, `Preset`, `AppSettings`), `PresetValidator`, `PresetJsonRepository`
- `src/FluxDisplay.App` — WinUI 3 app (Mica backdrop, tray, `DisplayService`, `PresetApplier`, Views/ViewModels)
- `tests/FluxDisplay.Core.Tests` — xUnit + coverlet for Core

## Tech stack

C# 12, .NET 8.0, Windows App SDK 1.6.250205002, `net8.0-windows10.0.22621.0` (`x64` + `arm64`), unpackaged + single-project MSIX, Trim/AOT disabled.

## License

MIT
