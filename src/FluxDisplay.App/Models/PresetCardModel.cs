namespace FluxDisplay.App.Models;

public sealed class PresetCardModel
{
    public required Preset Preset { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsFavorite { get; init; }

    public string Name => Preset.Name;
    public Guid Id => Preset.Id;
    public string MonitorName => string.Join(" · ", Preset.GetTargets().Select(t => t.FriendlyMonitorName));
    public DisplayMode Mode => Preset.GetTargets()[0].Mode;
    public int ScalePercent => Preset.GetTargets()[0].ScalePercent;

    public string ResolutionText => string.Join(" · ", Preset.GetTargets().Select(t => $"{t.Mode.Width} × {t.Mode.Height}"));
    public string RefreshText => string.Join(" · ", Preset.GetTargets().Select(t => $"{t.Mode.RefreshRate} Hz"));
    public string ScaleText => string.Join(" · ", Preset.GetTargets().Select(t => $"{t.ScalePercent}%"));
    public string BitsText => $"{Mode.BitsPerPel} bit";
    public string ModeText => string.Join(" · ", Preset.GetTargets().Select(t => t.Mode.ToString()));
}
