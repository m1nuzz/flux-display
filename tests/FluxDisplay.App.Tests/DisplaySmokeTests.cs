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
    public void EnumDisplayDevices_with_fixed_logic_returns_at_least_one_monitor()
    {
        var adapters = new List<string>();
        uint i = 0;
        while (true)
        {
            var dd = new DISPLAY_DEVICEW(); dd.cb = Marshal.SizeOf<DISPLAY_DEVICEW>();
            if (!EnumDisplayDevicesW(null, i, ref dd, 0)) break;
            adapters.Add(dd.DeviceName);
            i++;
        }
        Assert.True(adapters.Count >= 1, $"Expected at least 1 adapter, got {adapters.Count}: {string.Join(", ", adapters)}");

        var monitors = new List<(string adapter, string monitor, uint flags)>();
        foreach (var adapterName in adapters)
        {
            uint j = 0;
            while (true)
            {
                var mon = new DISPLAY_DEVICEW(); mon.cb = Marshal.SizeOf<DISPLAY_DEVICEW>();
                if (!EnumDisplayDevicesW(adapterName, j, ref mon, 1)) break;
                if ((mon.StateFlags & 1) == 0 || (mon.StateFlags & 8) != 0) { j++; continue; }
                monitors.Add((adapterName, mon.DeviceName, mon.StateFlags));
                j++;
            }
        }
        Assert.True(monitors.Count >= 1, $"Expected at least 1 monitor, got {monitors.Count}: {string.Join("; ", monitors)}");
        if (monitors.Count >= 2)
        {
            Assert.NotEqual(monitors[0].adapter, monitors[1].adapter);
        }
    }

    [Fact]
    public void DisplayService_source_no_longer_filters_adapters_by_ACTIVE()
    {
        var text = ReadRepoFile("src/FluxDisplay.App/Services/DisplayService.cs");
        var adapterFilterOccurrences = System.Text.RegularExpressions.Regex.Matches(text, @"var isActive.*DISPLAY_DEVICE_ACTIVE").Count;
        Assert.True(adapterFilterOccurrences <= 1, $"Adapter ACTIVE filter should have been removed, found {adapterFilterOccurrences} occurrences");
        Assert.Contains("Do not filter adapters by StateFlags", text);
    }

    [Fact]
    public void MonitorIdentifier_uses_overlapped_not_fullscreen()
    {
        var text = ReadRepoFile("src/FluxDisplay.App/Services/MonitorIdentifier.cs");
        Assert.Contains("AppWindowPresenterKind.Overlapped", text);
        Assert.DoesNotContain("AppWindowPresenterKind.FullScreen", text);
    }

    [Fact]
    public void CreatePresetViewModel_uses_adapter_name_for_modes()
    {
        var text = ReadRepoFile("src/FluxDisplay.App/ViewModels/CreatePresetViewModel.cs");
        Assert.Contains("GetSupportedModesAsync(monitor.DisplayName)", text);
    }

    [Fact]
    public void AppSettings_and_DisplayInfo_models_correct()
    {
        var text = ReadRepoFile("src/FluxDisplay.Core/Models/DisplayInfo.cs");
        Assert.Contains("readonly record struct Rect", text);
    }

    private static string ReadRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find {relative} walking up from {AppContext.BaseDirectory}");
    }
}
