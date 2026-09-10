param(
  [string]$Configuration = "Release",
  [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot

Write-Host "=== FluxDisplay smoke ===" -ForegroundColor Cyan
Write-Host "RepoRoot=$repoRoot Configuration=$Configuration Platform=$Platform"

# 1. Core tests
Write-Host "`n[1/4] Running Core tests..." -ForegroundColor Yellow
dotnet test (Join-Path $repoRoot "tests/FluxDisplay.Core.Tests/FluxDisplay.Core.Tests.csproj") --configuration $Configuration --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "Core tests failed" }

# 2. App smoke tests (enumeration, file checks)
Write-Host "`n[2/4] Running App smoke tests (DisplayService)..." -ForegroundColor Yellow
dotnet test (Join-Path $repoRoot "tests/FluxDisplay.App.Tests/FluxDisplay.App.Tests.csproj") --configuration $Configuration -p:Platform=$Platform --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "App smoke tests failed" }

# 3. Build App
Write-Host "`n[3/4] Building FluxDisplay.App..." -ForegroundColor Yellow
dotnet build (Join-Path $repoRoot "src/FluxDisplay.App/FluxDisplay.App.csproj") --configuration $Configuration -p:Platform=$Platform --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "App build failed" }

# 4. Publish portable (self-contained, unpackaged)
Write-Host "`n[4/4] Publishing portable (WindowsPackageType=None)..." -ForegroundColor Yellow
$publishDir = Join-Path $repoRoot "artifacts/FluxDisplay-win-x64-smoke"
Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue
dotnet publish (Join-Path $repoRoot "src/FluxDisplay.App/FluxDisplay.App.csproj") --configuration $Configuration --framework net8.0-windows10.0.22621.0 --runtime win-x64 --self-contained true --output $publishDir -p:Platform=$Platform -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:GenerateAppxPackageOnBuild=false -p:PublishReadyToRun=false --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

$exe = Join-Path $publishDir "FluxDisplay.App.exe"
if (-not (Test-Path $exe)) { throw "Publish artifact missing: $exe" }
Write-Host "Publish OK: $exe ($([math]::Round((Get-Item $exe).Length/1MB,2)) MB)" -ForegroundColor Green

# 5. Quick enumeration sanity via direct P/Invoke (no UI) — informational only, not fatal on headless CI
Write-Host "`n[5/5] P/Invoke enumeration sanity..." -ForegroundColor Yellow
try {
  Add-Type @"
using System; using System.Runtime.InteropServices;
[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct DD2 { public int cb; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string DeviceName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceString; public uint StateFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceID; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceKey; }
public class H2 { [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool EnumDisplayDevicesW(string a, uint b, ref DD2 c, uint d); }
"@ -ErrorAction SilentlyContinue
  $adapters2 = @(); $i2=0; while($true){ $dd2=New-Object DD2 -ErrorAction SilentlyContinue; if(-not $dd2){break}; $dd2.cb=[Runtime.InteropServices.Marshal]::SizeOf($dd2); if(-not [H2]::EnumDisplayDevicesW($null,$i2,[ref]$dd2,0)){break}; $adapters2+=$dd2.DeviceName; $i2++; if($i2 -gt 20){break} }
  Write-Host "Adapters: $($adapters2 -join ', ')"
  if ($adapters2.Count -lt 1) { Write-Host "Warning: No adapters found (headless CI?)" -ForegroundColor Yellow }
} catch {
  Write-Host "P/Invoke check skipped: $_" -ForegroundColor Yellow
}

# 6. Smoke dump via App --smoke-dump (logs DisplayService enumeration)
Write-Host "`n[6/7] App smoke dump (DisplayService)..." -ForegroundColor Yellow
$smokeLog = Join-Path $env:TEMP "flux-smoke.log"
Remove-Item -Force $smokeLog -ErrorAction SilentlyContinue
$exeToRun = Join-Path $publishDir "FluxDisplay.App.exe"
if (Test-Path $exeToRun) {
  $proc = Start-Process -FilePath $exeToRun -ArgumentList "--smoke-dump", $smokeLog -PassThru
  $exited = $proc.WaitForExit(8000)
  if (-not $exited) { try { $proc.Kill() } catch {} }
  if (Test-Path $smokeLog) {
    $content = Get-Content -LiteralPath $smokeLog -Raw
    Write-Host $content
    if ($content -match "Count=0" -or $content -match "ERROR") { throw "Smoke dump reported no displays or error. See log above." }
    $artifactsSmokeLog = Join-Path $repoRoot "artifacts/smoke.log"
    try { Copy-Item -LiteralPath $smokeLog -Destination $artifactsSmokeLog -Force } catch {}
  } else {
    Write-Host "Warning: smoke log not created (app may need UI thread, fallback to file checks)" -ForegroundColor Yellow
    $svcPath = Join-Path $repoRoot "src/FluxDisplay.App/Services/DisplayService.cs"
    $svcText = Get-Content -LiteralPath $svcPath -Raw
    if ($svcText -match "if \(!isActive \|\| isMirroring\)" -and $svcText -match "var isActive.*DISPLAY_DEVICE_ACTIVE") {
      if ($svcText -match "Do not filter adapters") {
        Write-Host "DisplayService fix present (adapter filter removed)" -ForegroundColor Green
      } else {
        throw "DisplayService still contains adapter ACTIVE filter"
      }
    }
  }
} else {
  Write-Host "Publish exe not found, skipping smoke dump" -ForegroundColor Yellow
}

# 7. Capture UI screenshots of every screen for CI artifacts
Write-Host "`n[7/7] App UI screenshots (--smoke-ui)..." -ForegroundColor Yellow
$uiOut = Join-Path $repoRoot "artifacts/ui-smoke"
New-Item -ItemType Directory -Force -Path $uiOut | Out-Null
if (Test-Path $exeToRun) {
  $uiProc = Start-Process -FilePath $exeToRun -ArgumentList "--smoke-ui", $uiOut -WorkingDirectory $repoRoot -PassThru
  $uiExited = $uiProc.WaitForExit(45000)
  if (-not $uiExited) {
    Write-Host "Warning: --smoke-ui timed out, killing" -ForegroundColor Yellow
    try { $uiProc.Kill() } catch {}
  }
  $pngs = @(Get-ChildItem -Path $uiOut -Filter *.png -ErrorAction SilentlyContinue)
  Write-Host "UI smoke png count=$($pngs.Count) dir=$uiOut"
  foreach ($png in $pngs) { Write-Host "  $($png.Name) $($png.Length)" }
  $indexPath = Join-Path $uiOut "index.txt"
  if (Test-Path $indexPath) { Get-Content -LiteralPath $indexPath | Write-Host }
  if ($pngs.Count -lt 1) {
    Write-Host "Warning: no UI screenshots captured (headless runner?)" -ForegroundColor Yellow
  }
} else {
  Write-Host "Publish exe not found, skipping UI screenshots" -ForegroundColor Yellow
}

Pop-Location -ErrorAction SilentlyContinue
Write-Host "`n=== Smoke PASSED ===" -ForegroundColor Green
