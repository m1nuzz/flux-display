# FluxDisplay

FluxDisplay is a Windows 11 utility for saving monitor display presets and applying them from a desktop window or the system tray.

## Development status

The platform-neutral Core library and its unit tests are implemented first. The WinUI 3 application targets Windows and is developed separately because WinUI 3 and Windows SDK components are not available on Linux CI runners.

## Test locally

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet restore
 dotnet test --configuration Release --no-restore
```

## GitHub checks

Every push and pull request runs the Core unit tests and a smoke check that verifies the solution contains the required Core and test projects.
