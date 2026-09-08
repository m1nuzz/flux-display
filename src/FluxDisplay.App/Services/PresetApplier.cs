using FluxDisplay.App.Models;

namespace FluxDisplay.App.Services;

// Applies a preset to the system: resolves display, changes mode, persists LastAppliedAt,
// shows notifications or error dialogs. Also checks if a preset is currently active.
public sealed class PresetApplier : IPresetApplier
{
    private readonly IDisplayService _displayService;
    private readonly IPresetRepository _presetRepository;
    private readonly IDialogService _dialogService;
    private readonly ITrayService _trayService;

    public PresetApplier(
        IDisplayService displayService,
        IPresetRepository presetRepository,
        IDialogService dialogService,
        ITrayService trayService)
    {
        _displayService = displayService;
        _presetRepository = presetRepository;
        _dialogService = dialogService;
        _trayService = trayService;
    }

    // Compatibility constructor for legacy single-dependency usage (Core-style).
    public PresetApplier(IDisplayService displayService)
        : this(displayService,
               new PresetRepository(),
               new DialogService(),
               new TrayService())
    {
    }

    public async Task<bool> ApplyAsync(Preset preset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preset);

        // Resolve display by DevicePath.
        var display = await _displayService.GetDisplayByDevicePathAsync(preset.DevicePath, ct).ConfigureAwait(false);
        if (display is null)
        {
            await _dialogService.ShowErrorAsync("Display not found", $"Display '{preset.FriendlyMonitorName}' not found.").ConfigureAwait(false);
            return false;
        }

        // Attempt to change display mode.
        var result = await _displayService.ChangeDisplayModeAsync(display.DisplayName, preset.Mode, ct).ConfigureAwait(false);

        if (result == DISP_CHANGE.Success)
        {
            // Update last applied timestamp and persist.
            preset.LastAppliedAt = DateTime.UtcNow;
            try
            {
                var collection = await _presetRepository.LoadAsync(ct).ConfigureAwait(false);
                var existing = collection.Presets.FirstOrDefault(p => p.Id == preset.Id);
                if (existing is not null)
                {
                    existing.LastAppliedAt = preset.LastAppliedAt;
                }
                else
                {
                    // If preset not yet in collection, add it.
                    collection.Presets.Add(preset);
                }

                await _presetRepository.SaveAsync(collection, ct).ConfigureAwait(false);
            }
            catch
            {
                // Persistence failure should not hide successful mode change.
            }

            _trayService.ShowNotification("Preset applied", $"'{preset.Name}' applied successfully.");
            return true;
        }

        if (result == DISP_CHANGE.BadMode)
        {
            await _dialogService.ShowErrorAsync("Mode unavailable", "Режим недоступен").ConfigureAwait(false);
            return false;
        }

        // Generic failure.
        await _dialogService.ShowErrorAsync("Failed to apply preset", $"Failed to apply preset '{preset.Name}'. Error: {result}").ConfigureAwait(false);
        return false;
    }

    public async Task<bool> IsPresetActiveAsync(Preset preset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var display = await _displayService.GetDisplayByDevicePathAsync(preset.DevicePath, ct).ConfigureAwait(false);
        if (display is null)
        {
            return false;
        }

        var current = await _displayService.GetCurrentModeAsync(display.DisplayName, ct).ConfigureAwait(false);

        // Compare current mode with preset mode.
        return current.Width == preset.Mode.Width
            && current.Height == preset.Mode.Height
            && current.RefreshRate == preset.Mode.RefreshRate
            && current.BitsPerPel == preset.Mode.BitsPerPel;
    }
}
