using FluxDisplay.Core.Models;

namespace FluxDisplay.Core.Abstractions;

// Persists preset collections to disk (JSON file).
public interface IPresetRepository
{
    string StoragePath { get; }
    Task<PresetCollection> LoadAsync();
    Task SaveAsync(PresetCollection collection);
}
