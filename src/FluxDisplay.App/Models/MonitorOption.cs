using FluxDisplay.Core.Models;

namespace FluxDisplay.App.Models;

public sealed class MonitorOption
{
    public required string DevicePath { get; init; }
    public required string DisplayName { get; init; }
    public required string FriendlyName { get; init; }
    public required string AdapterName { get; init; }
    public required bool IsPrimary { get; init; }
    public required Rect Bounds { get; init; }
    public required DisplayMode CurrentMode { get; init; }
    public required int CurrentDpi { get; init; }
    public required int ScalePercent { get; init; }

    public string DisplayTitle => $"{FriendlyName} ({DisplayName})";

    public string ResolutionText => $"{CurrentMode.Width} × {CurrentMode.Height} @ {CurrentMode.RefreshRate} Hz";

    public static MonitorOption FromDisplayInfo(DisplayInfo info) => new()
    {
        DevicePath = info.DevicePath,
        DisplayName = info.DisplayName,
        FriendlyName = info.FriendlyName,
        AdapterName = info.AdapterName,
        IsPrimary = info.IsPrimary,
        Bounds = info.Bounds,
        CurrentMode = info.CurrentMode,
        CurrentDpi = info.CurrentDpi,
        ScalePercent = info.ScalePercent
    };
}
