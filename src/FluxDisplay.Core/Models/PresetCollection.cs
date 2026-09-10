namespace FluxDisplay.Core.Models;

public sealed class PresetCollection
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public List<Preset> Presets { get; set; } = [];
}
