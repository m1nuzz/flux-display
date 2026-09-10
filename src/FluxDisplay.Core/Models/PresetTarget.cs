namespace FluxDisplay.Core.Models;

public sealed class PresetTarget
{
    public required string DevicePath { get; set; }
    public required string FriendlyMonitorName { get; set; }
    public required DisplayMode Mode { get; set; }
    public int ScalePercent { get; set; } = 100;
}
