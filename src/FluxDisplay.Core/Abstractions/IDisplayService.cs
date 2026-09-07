using FluxDisplay.Core.Models;

namespace FluxDisplay.Core.Abstractions;

public interface IDisplayService
{
    Task<IReadOnlyList<DisplayInfo>> GetDisplaysAsync(CancellationToken cancellationToken = default);
    Task<DisplayInfo?> GetDisplayByDevicePathAsync(string devicePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DisplayMode>> GetSupportedModesAsync(string displayName, CancellationToken cancellationToken = default);
    Task<DisplayMode> GetCurrentModeAsync(string displayName, CancellationToken cancellationToken = default);
    Task<int> GetCurrentDpiAsync(string displayName, CancellationToken cancellationToken = default);
}
