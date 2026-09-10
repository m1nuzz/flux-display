using FluxDisplay.Core.Models;

namespace FluxDisplay.Core.Services;

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
