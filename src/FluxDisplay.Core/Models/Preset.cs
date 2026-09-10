namespace FluxDisplay.Core.Models;

public sealed class Preset
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string DevicePath { get; set; }
    public required string FriendlyMonitorName { get; set; }
    public required DisplayMode Mode { get; set; }
    public int ScalePercent { get; set; } = 100;
    public List<PresetTarget> Targets { get; set; } = [];
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? LastAppliedAt { get; set; }

    public IReadOnlyList<PresetTarget> GetTargets()
    {
        Targets ??= [];
        if (Targets.Count > 0)
            return Targets;

        return
        [
            new PresetTarget
            {
                DevicePath = DevicePath,
                FriendlyMonitorName = FriendlyMonitorName,
                Mode = Mode,
                ScalePercent = ScalePercent
            }
        ];
    }

    public void EnsureTargets()
    {
        Targets ??= [];
        if (Targets.Count == 0 && !string.IsNullOrWhiteSpace(DevicePath))
        {
            Targets.Add(new PresetTarget
            {
                DevicePath = DevicePath,
                FriendlyMonitorName = FriendlyMonitorName,
                Mode = Mode,
                ScalePercent = ScalePercent
            });
        }

        if (Targets.Count == 0)
            return;

        var primary = Targets[0];
        DevicePath = primary.DevicePath;
        FriendlyMonitorName = primary.FriendlyMonitorName;
        Mode = primary.Mode;
        ScalePercent = primary.ScalePercent;
    }
}
