using FluxDisplay.Core.Models;

namespace FluxDisplay.Core.Abstractions;

// Applies a preset to the current system and checks whether it is active.
public interface IPresetApplier
{
    Task<bool> ApplyAsync(Preset preset);
    Task<bool> IsPresetActiveAsync(Preset preset);
}
