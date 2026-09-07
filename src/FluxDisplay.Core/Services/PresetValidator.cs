using FluxDisplay.Core.Models;

namespace FluxDisplay.Core.Services;

public sealed class PresetValidator
{
    public const int MaxNameLength = 80;

    public IReadOnlyList<string> Validate(Preset? preset)
    {
        var errors = new List<string>();
        if (preset is null)
        {
            errors.Add("Preset is required.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(preset.Name))
            errors.Add("Preset name is required.");
        else if (preset.Name.Trim().Length > MaxNameLength)
            errors.Add($"Preset name must be {MaxNameLength} characters or fewer.");

        if (string.IsNullOrWhiteSpace(preset.DevicePath))
            errors.Add("Display device path is required.");
        if (string.IsNullOrWhiteSpace(preset.FriendlyMonitorName))
            errors.Add("Monitor name is required.");
        if (preset.Mode is null)
        {
            errors.Add("Display mode is required.");
        }
        else
        {
            if (preset.Mode.Width <= 0 || preset.Mode.Height <= 0)
                errors.Add("Display dimensions must be positive.");
            if (preset.Mode.RefreshRate <= 0)
                errors.Add("Refresh rate must be positive.");
            if (preset.Mode.BitsPerPel <= 0)
                errors.Add("Color depth must be positive.");
        }

        if (preset.ScalePercent is < 50 or > 500)
            errors.Add("Scale must be between 50 and 500 percent.");
        return errors;
    }

    public bool IsValid(Preset? preset) => Validate(preset).Count == 0;
}
