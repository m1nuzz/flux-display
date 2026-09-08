namespace FluxDisplay.App.Models;

// User-saved display configuration for a single monitor.
public sealed class Preset
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string DevicePath { get; set; }
    public required string FriendlyMonitorName { get; set; }
    public required DisplayMode Mode { get; set; }
    public int ScalePercent { get; set; } = 100;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? LastAppliedAt { get; set; }
}
