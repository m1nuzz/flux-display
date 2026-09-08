using FluxDisplay.Core.Models;

namespace FluxDisplay.Core.Abstractions;

// Platform-neutral display enumeration and mode control.
// Mirrors plan App/Services/Interfaces.cs but without CancellationToken for Core compatibility.
// All operations are async to allow offloading Win32 interop to a background thread.
public interface IDisplayService
{
    Task<IReadOnlyList<DisplayInfo>> GetDisplaysAsync();
    Task<DisplayInfo?> GetDisplayByDevicePathAsync(string devicePath);
    Task<IReadOnlyList<DisplayMode>> GetSupportedModesAsync(string displayName);
    Task<DisplayMode> GetCurrentModeAsync(string displayName);
    Task<int> GetCurrentDpiAsync(string displayName);
    Task<DISP_CHANGE> ChangeDisplayModeAsync(string displayName, DisplayMode mode);
}
