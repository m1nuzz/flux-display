using FluxDisplay.Core.Models;

namespace FluxDisplay.Core.Abstractions;

public interface IPresetRepository
{
    string StoragePath { get; }
    Task<PresetCollection> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(PresetCollection collection, CancellationToken cancellationToken = default);
}
