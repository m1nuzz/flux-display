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
}
