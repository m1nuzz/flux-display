using FluxDisplay.App.Models;

namespace FluxDisplay.App.Services;

public static class PresetMigrator
{
    public static PresetCollection Normalize(PresetCollection? collection)
    {
        collection ??= new PresetCollection();
        foreach (var preset in collection.Presets)
            preset.EnsureTargets();

        collection.Version = PresetCollection.CurrentVersion;
        return collection;
    }
}
