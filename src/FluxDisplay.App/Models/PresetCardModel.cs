namespace FluxDisplay.App.Models;

public sealed class PresetCardModel
{
    public required Preset Preset { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsFavorite { get; init; }

    public string Name => Preset.Name;
    public Guid Id => Preset.Id;
    public string MonitorName => Preset.FriendlyMonitorName;
    public DisplayMode Mode => Preset.Mode;
    public int ScalePercent => Preset.ScalePercent;

    public string ResolutionText => $"{Mode.Width} × {Mode.Height}";
    public string RefreshText => $"{Mode.RefreshRate} Hz";
    public string ScaleText => $"{ScalePercent}%";
    public string BitsText => $"{Mode.BitsPerPel} bit";
    public string ModeText => Mode.ToString();
}
