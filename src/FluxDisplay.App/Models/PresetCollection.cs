namespace FluxDisplay.App.Models;

// Persisted container. Version allows future migrations.
public sealed class PresetCollection
{
    public int Version { get; set; } = 1;
    public List<Preset> Presets { get; set; } = new();
}
