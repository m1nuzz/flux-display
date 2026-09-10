using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FluxDisplay.App.Models;

public sealed partial class MonitorSettingsDraft : ObservableObject
{
    public MonitorSettingsDraft(MonitorOption monitor, IEnumerable<int> scaleOptions)
    {
        Monitor = monitor;
        foreach (var option in scaleOptions)
            ScaleOptions.Add(option);
    }

    public MonitorOption Monitor { get; }
    public ObservableCollection<string> Resolutions { get; } = [];
    public ObservableCollection<int> RefreshRates { get; } = [];
    public ObservableCollection<int> ScaleOptions { get; } = [];

    [ObservableProperty] private string? _selectedResolution;
    [ObservableProperty] private int? _selectedRefreshRate;
    [ObservableProperty] private int _scalePercent = 100;
    [ObservableProperty] private int _currentDpi = 96;

    internal IReadOnlyList<DisplayMode> AllModes { get; set; } = [];

    public string Title => Monitor.DisplayTitle;
    public bool IsComplete => SelectedResolution is not null && (SelectedRefreshRate ?? 0) > 0;

    partial void OnSelectedResolutionChanged(string? value) => RebuildRefreshRates();
    partial void OnScalePercentChanged(int value) => CurrentDpi = (int)Math.Round(value * 96.0 / 100.0);

    public void ApplyModes(IReadOnlyList<DisplayMode> modes)
    {
        AllModes = modes;
        ScalePercent = Monitor.ScalePercent;
        if (!ScaleOptions.Contains(ScalePercent))
            ScaleOptions.Add(ScalePercent);
        CurrentDpi = (int)Math.Round(ScalePercent * 96.0 / 100.0);

        Resolutions.Clear();
        foreach (var resolution in modes.Select(m => $"{m.Width} × {m.Height}").Distinct())
            Resolutions.Add(resolution);

        var current = $"{Monitor.CurrentMode.Width} × {Monitor.CurrentMode.Height}";
        SelectedResolution = Resolutions.Contains(current) ? current : Resolutions.FirstOrDefault();
        RebuildRefreshRates();

        var currentHz = Monitor.CurrentMode.RefreshRate;
        if (RefreshRates.Contains(currentHz))
            SelectedRefreshRate = currentHz;
        else if (RefreshRates.Count > 0)
            SelectedRefreshRate = RefreshRates[0];
    }

    public DisplayMode? TryBuildMode()
    {
        if (!TryParseResolution(SelectedResolution, out var width, out var height) || (SelectedRefreshRate ?? 0) <= 0)
            return null;

        return new DisplayMode
        {
            Width = width,
            Height = height,
            RefreshRate = SelectedRefreshRate ?? 0,
            BitsPerPel = 32
        };
    }

    public static bool TryParseResolution(string? value, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split('×', 'x');
        return parts.Length == 2
            && int.TryParse(parts[0].Trim(), out width)
            && int.TryParse(parts[1].Trim(), out height);
    }

    private void RebuildRefreshRates()
    {
        RefreshRates.Clear();
        if (!TryParseResolution(SelectedResolution, out var width, out var height))
            return;

        foreach (var rate in AllModes.Where(m => m.Width == width && m.Height == height).Select(m => m.RefreshRate).Distinct().OrderByDescending(x => x))
            RefreshRates.Add(rate);

        if (SelectedRefreshRate is null || !RefreshRates.Contains(SelectedRefreshRate.Value))
            SelectedRefreshRate = RefreshRates.Count > 0 ? RefreshRates[0] : null;
    }
}
