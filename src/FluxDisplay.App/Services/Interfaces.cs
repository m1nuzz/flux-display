using System.Threading;
using FluxDisplay.App.Models;

namespace FluxDisplay.App.Services;

// Enumerates connected displays and controls their modes via ChangeDisplaySettingsEx.
public interface IDisplayService
{
    Task<IReadOnlyList<DisplayInfo>> GetDisplaysAsync(CancellationToken ct = default);
    Task<DisplayInfo?> GetDisplayByDevicePathAsync(string devicePath, CancellationToken ct = default);
    Task<IReadOnlyList<DisplayMode>> GetSupportedModesAsync(string displayName, CancellationToken ct = default);
    Task<DisplayMode> GetCurrentModeAsync(string displayName, CancellationToken ct = default);
    Task<int> GetCurrentDpiAsync(string displayName, CancellationToken ct = default);
    Task<DISP_CHANGE> ChangeDisplayModeAsync(string displayName, DisplayMode mode, CancellationToken ct = default);
}

// Persists preset collections to disk (JSON file).
public interface IPresetRepository
{
    string StoragePath { get; }
    Task<PresetCollection> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(PresetCollection collection, CancellationToken ct = default);
}

// Applies a preset to the current system and checks whether it is active.
public interface IPresetApplier
{
    Task<bool> ApplyAsync(Preset preset, CancellationToken ct = default);
    Task<bool> IsPresetActiveAsync(Preset preset, CancellationToken ct = default);
}

// Manages the system tray icon and its preset menu.
public interface ITrayService : IDisposable
{
    void Initialize();
    bool WarmUpMenuHost();
    void UpdatePresets(IReadOnlyList<Preset> presets, Guid? activePresetId);
    void ShowNotification(string title, string message);
    event EventHandler? OpenRequested;
    event EventHandler<Guid>? PresetApplyRequested;
}

// Controls the Windows startup registry entry.
public interface IStartupManager
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

// Shows an identify overlay on every connected monitor.
public interface IMonitorIdentifier
{
    Task IdentifyAsync(IReadOnlyList<DisplayInfo> displays, CancellationToken ct = default);
    Task IdentifyAsync(IReadOnlyList<DisplayInfo> displays, int durationSeconds, CancellationToken ct = default);
}

// Shows modal dialogs on top of the main window.
public interface IDialogService
{
    Task ShowInfoAsync(string title, string message);
    Task<bool> ShowConfirmAsync(string title, string message, string primaryButtonText = "Apply", string closeButtonText = "Cancel");
    Task ShowErrorAsync(string title, string message);
}
