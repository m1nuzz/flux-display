using Xunit;
using FluxDisplay.Core.Models;
using FluxDisplay.Core.Services;

namespace FluxDisplay.Core.Tests;

// Validates PresetValidator rules: required fields, scale bounds, display mode constraints.
public sealed class PresetValidatorTests
{
    private static Preset CreateValidPreset() => new()
    {
        Name = "Gaming 1080p",
        DevicePath = "device-1",
        FriendlyMonitorName = "Test Monitor",
        Mode = new DisplayMode { Width = 1920, Height = 1080, RefreshRate = 240, BitsPerPel = 32 },
        ScalePercent = 100
    };

    [Fact]
    public void Valid_preset_passes_validation()
    {
        var validator = new PresetValidator();
        Assert.True(validator.IsValid(CreateValidPreset()));
    }

    [Theory]
    [InlineData(0, 1080, 60)]
    [InlineData(1920, 0, 60)]
    [InlineData(1920, 1080, 0)]
    public void Invalid_mode_values_are_rejected(int width, int height, int refreshRate)
    {
        var preset = CreateValidPreset();
        preset.Mode = new DisplayMode { Width = width, Height = height, RefreshRate = refreshRate, BitsPerPel = 32 };
        var errors = new PresetValidator().Validate(preset);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Blank_name_is_rejected()
    {
        var preset = CreateValidPreset();
        preset.Name = "  ";
        Assert.Contains(new PresetValidator().Validate(preset), error => error.Contains("name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scale_outside_supported_range_is_rejected()
    {
        var preset = CreateValidPreset();
        preset.ScalePercent = 40;
        Assert.False(new PresetValidator().IsValid(preset));
    }

    [Fact]
    public void Multi_target_preset_is_valid()
    {
        var preset = CreateValidPreset();
        preset.Targets =
        [
            new PresetTarget
            {
                DevicePath = "device-1",
                FriendlyMonitorName = "Left",
                Mode = new DisplayMode { Width = 1920, Height = 1080, RefreshRate = 240, BitsPerPel = 32 },
                ScalePercent = 100
            },
            new PresetTarget
            {
                DevicePath = "device-2",
                FriendlyMonitorName = "Right",
                Mode = new DisplayMode { Width = 2560, Height = 1440, RefreshRate = 144, BitsPerPel = 32 },
                ScalePercent = 125
            }
        ];

        Assert.True(new PresetValidator().IsValid(preset));
    }

    [Fact]
    public void Duplicate_device_paths_are_rejected()
    {
        var preset = CreateValidPreset();
        var mode = new DisplayMode { Width = 1920, Height = 1080, RefreshRate = 60, BitsPerPel = 32 };
        preset.Targets =
        [
            new PresetTarget { DevicePath = "same", FriendlyMonitorName = "A", Mode = mode, ScalePercent = 100 },
            new PresetTarget { DevicePath = "same", FriendlyMonitorName = "B", Mode = mode, ScalePercent = 100 }
        ];

        Assert.Contains(new PresetValidator().Validate(preset), error => error.Contains("duplicated", StringComparison.OrdinalIgnoreCase));
    }
}
