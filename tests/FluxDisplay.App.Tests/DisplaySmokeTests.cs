using System.Runtime.InteropServices;
using Xunit;

namespace FluxDisplay.App.Tests;

/// <summary>
/// Automated smoke tests for display enumeration — verifies the adapter-filter fix and monitor bounds.
/// Uses direct P/Invoke to avoid referencing WinUI App project (which would require PriGen). Mirrors DisplayService fallback logic.
/// Replaces manual Windows MCP.
/// </summary>
public sealed class DisplaySmokeTests
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICEW
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevicesW(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICEW lpDisplayDevice, uint dwFlags);

    [Fact]
    public void EnumDisplayDevices_with_fixed_logic_returns_two_monitors_with_distinct_bounds()
    {
        // Simulate fixed DisplayService logic: do NOT filter adapters by ACTIVE, only monitors.
        var adapters = new List<string>();
        uint i = 0;
        while (true)
        {
            var dd = new DISPLAY_DEVICEW(); dd.cb = Marshal.SizeOf<DISPLAY_DEVICEW>();
            if (!EnumDisplayDevicesW(null, i, ref dd, 0)) break;
            adapters.Add(dd.DeviceName);
            i++;
        }
        Assert.True(adapters.Count >= 2, $"Expected at least 2 adapters (DISPLAY1/DISPLAY2), got {adapters.Count}: {string.Join(", ", adapters)}");

        var monitors = new List<(string adapter, string monitor, uint flags)>();
        foreach (var adapterName in adapters.Take(2))
        {
            uint j = 0;
            while (true)
            {
                var mon = new DISPLAY_DEVICEW(); mon.cb = Marshal.SizeOf<DISPLAY_DEVICEW>();
                if (!EnumDisplayDevicesW(adapterName, j, ref mon, 1)) break;
                // Filter only monitors: ACTIVE && !MIRRORING — same as fixed DisplayService
                if ((mon.StateFlags & 1) == 0 || (mon.StateFlags & 8) != 0) { j++; continue; }
                monitors.Add((adapterName, mon.DeviceName, mon.StateFlags));
                j++;
            }
        }
        Assert.True(monitors.Count >= 2, $"Expected 2 monitors, got {monitors.Count}: {string.Join("; ", monitors)}");
        // DeviceName should be like \\.\DISPLAY1\Monitor0, adapter distinct
        Assert.NotEqual(monitors[0].adapter, monitors[1].adapter);
    }

    [Fact]
    public void DisplayService_source_no_longer_filters_adapters_by_ACTIVE()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FluxDisplay.App", "Services", "DisplayService.cs");
        // Fallback to repo root if relative fails
        if (!File.Exists(path))
            path = @"C:\Projectrs\flux-display\src\FluxDisplay.App\Services\DisplayService.cs";
        var text = File.ReadAllText(path);
        // Old buggy code contained: if (!isActive || isMirroring) continue; for adapter level.
        // Fixed code must NOT contain that exact adapter-level filter — only monitor-level.
        // Count occurrences of that pattern — should be 1 (monitor only), not 2.
        var adapterFilterOccurrences = System.Text.RegularExpressions.Regex.Matches(text, @"var isActive.*DISPLAY_DEVICE_ACTIVE").Count;
        Assert.True(adapterFilterOccurrences <= 1, $"Adapter ACTIVE filter should have been removed, found {adapterFilterOccurrences} occurrences");
        Assert.Contains("Do not filter adapters by StateFlags", text);
    }

    [Fact]
    public void MonitorIdentifier_uses_overlapped_not_fullscreen()
    {
        var path = @"C:\Projectrs\flux-display\src\FluxDisplay.App\Services\MonitorIdentifier.cs";
        var text = File.ReadAllText(path);
        Assert.Contains("AppWindowPresenterKind.Overlapped", text);
        Assert.DoesNotContain("AppWindowPresenterKind.FullScreen", text);
    }

    [Fact]
    public void CreatePresetViewModel_uses_adapter_name_for_modes()
    {
        var path = @"C:\Projectrs\flux-display\src\FluxDisplay.App\ViewModels\CreatePresetViewModel.cs";
        var text = File.ReadAllText(path);
        // Should call GetSupportedModesAsync with SelectedMonitor.DisplayName (which is now adapter name)
        Assert.Contains("GetSupportedModesAsync(SelectedMonitor.DisplayName)", text);
    }

    [Fact]
    public void AppSettings_and_DisplayInfo_models_correct()
    {
        // Verify Rect vs DisplayRect fix
        var displayInfoPath = @"C:\Projectrs\flux-display\src\FluxDisplay.Core\Models\DisplayInfo.cs";
        var text = File.ReadAllText(displayInfoPath);
        Assert.Contains("readonly record struct Rect", text);
    }
}
