namespace FluxDisplay.Core.Models;

public readonly record struct DisplayRect(int X, int Y, int Width, int Height);

public sealed class DisplayInfo
{
    public required string DevicePath { get; init; }
    public required string DisplayName { get; init; }
    public required string FriendlyName { get; init; }
    public required string AdapterName { get; init; }
    public required DisplayRect Bounds { get; init; }
    public required bool IsPrimary { get; init; }
    public required DisplayMode CurrentMode { get; init; }
    public required int CurrentDpi { get; init; }
    public required int ScalePercent { get; init; }
}
