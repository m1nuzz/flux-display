namespace FluxDisplay.App.Models;

// Screen bounds in pixels. X/Y is the origin on the virtual desktop.
public readonly record struct Rect(int X, int Y, int Width, int Height);

// Represents a connected display with current mode, DPI and scale.
public sealed class DisplayInfo
{
    public required string DevicePath { get; init; }
    public required string DisplayName { get; init; }
    public required string FriendlyName { get; init; }
    public required string AdapterName { get; init; }
    public required Rect Bounds { get; init; }
    public required bool IsPrimary { get; init; }
    public required DisplayMode CurrentMode { get; init; }
    public required int CurrentDpi { get; init; }
    public required int ScalePercent { get; init; }
}
