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

        var targets = preset.GetTargets();
        if (targets.Count == 0)
        {
            errors.Add("At least one display is required.");
            return errors;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < targets.Count; i++)
        {
            var label = targets.Count == 1 ? "Display" : $"Display {i + 1}";
            ValidateTarget(targets[i], label, errors);
            if (!string.IsNullOrWhiteSpace(targets[i].DevicePath) && !seen.Add(targets[i].DevicePath))
                errors.Add($"{label} is duplicated.");
        }

        return errors;
    }

    public bool IsValid(Preset? preset) => Validate(preset).Count == 0;

    private static void ValidateTarget(PresetTarget target, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(target.DevicePath))
            errors.Add($"{label} device path is required.");
        if (string.IsNullOrWhiteSpace(target.FriendlyMonitorName))
            errors.Add($"{label} name is required.");
        if (target.Mode is null)
        {
            errors.Add($"{label} mode is required.");
        }
        else
        {
            if (target.Mode.Width <= 0 || target.Mode.Height <= 0)
                errors.Add($"{label} dimensions must be positive.");
            if (target.Mode.RefreshRate <= 0)
                errors.Add($"{label} refresh rate must be positive.");
            if (target.Mode.BitsPerPel <= 0)
                errors.Add($"{label} color depth must be positive.");
        }

        if (target.ScalePercent is < 50 or > 500)
            errors.Add($"{label} scale must be between 50 and 500 percent.");
    }
}
