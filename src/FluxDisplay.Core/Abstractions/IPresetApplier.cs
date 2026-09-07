using FluxDisplay.Core.Models;

namespace FluxDisplay.Core.Abstractions;

public interface IPresetApplier
{
    Task<bool> ApplyAsync(Preset preset, CancellationToken cancellationToken = default);
    Task<bool> IsPresetActiveAsync(Preset preset, CancellationToken cancellationToken = default);
}
