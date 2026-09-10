using FluxDisplay.App.Models;

namespace FluxDisplay.App.Services;

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
        var targets = preset.GetTargets();
        using var _ = Helpers.AppLog.Scope("PresetApplier.ApplyAsync", $"'{preset.Name}' targets={targets.Count}");

        foreach (var target in targets)
        {
            var display = await _displayService.GetDisplayByDevicePathAsync(target.DevicePath, ct).ConfigureAwait(false);
            if (display is null)
            {
                await _dialogService.ShowErrorAsync("Display not found", $"Display '{target.FriendlyMonitorName}' not found.").ConfigureAwait(false);
                return false;
            }

            var result = await _displayService.ChangeDisplayModeAsync(display.DisplayName, target.Mode, ct).ConfigureAwait(false);
            if (result == DISP_CHANGE.Success)
                continue;

            if (result == DISP_CHANGE.BadMode)
            {
                await _dialogService.ShowErrorAsync("Mode unavailable", "Режим недоступен").ConfigureAwait(false);
                return false;
            }

            await _dialogService.ShowErrorAsync("Failed to apply preset", $"Failed to apply '{preset.Name}' to '{target.FriendlyMonitorName}'. Error: {result}").ConfigureAwait(false);
            return false;
        }

        preset.LastAppliedAt = DateTime.UtcNow;
        try
        {
            var collection = await _presetRepository.LoadAsync(ct).ConfigureAwait(false);
            var existing = collection.Presets.FirstOrDefault(p => p.Id == preset.Id);
            if (existing is not null)
                existing.LastAppliedAt = preset.LastAppliedAt;
            else
                collection.Presets.Add(preset);

            await _presetRepository.SaveAsync(collection, ct).ConfigureAwait(false);
        }
        catch
        {
        }

        _trayService.ShowNotification("Preset applied", $"'{preset.Name}' applied successfully.");
        return true;
    }

    public async Task<bool> IsPresetActiveAsync(Preset preset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preset);

        foreach (var target in preset.GetTargets())
        {
            var display = await _displayService.GetDisplayByDevicePathAsync(target.DevicePath, ct).ConfigureAwait(false);
            if (display is null)
                return false;

            var current = await _displayService.GetCurrentModeAsync(display.DisplayName, ct).ConfigureAwait(false);
            if (current.Width != target.Mode.Width
                || current.Height != target.Mode.Height
                || current.RefreshRate != target.Mode.RefreshRate
                || current.BitsPerPel != target.Mode.BitsPerPel)
            {
                return false;
            }
        }

        return preset.GetTargets().Count > 0;
    }
}
