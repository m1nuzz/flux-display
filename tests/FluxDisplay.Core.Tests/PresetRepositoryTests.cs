using FluxDisplay.Core.Models;
using FluxDisplay.Core.Services;
using Xunit;

namespace FluxDisplay.Core.Tests;

public sealed class PresetRepositoryTests
{
    [Fact]
    public async Task Save_and_load_round_trip_preserves_preset_data()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = Path.Combine(temporaryDirectory.Path, "nested", "presets.json");
        var repository = new PresetJsonRepository(path);
        var collection = new PresetCollection
        {
            Presets =
            {
                new Preset
                {
                    Name = "Gaming 1080p",
                    DevicePath = "device-1",
                    FriendlyMonitorName = "Test Monitor",
                    Mode = new DisplayMode { Width = 1920, Height = 1080, RefreshRate = 240, BitsPerPel = 32 },
                    ScalePercent = 100
                }
            }
        };

        await repository.SaveAsync(collection);
        var loaded = await repository.LoadAsync();

        var preset = Assert.Single(loaded.Presets);
        Assert.Equal("Gaming 1080p", preset.Name);
        Assert.Equal(1920, preset.Mode.Width);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Missing_file_returns_empty_collection()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var repository = new PresetJsonRepository(Path.Combine(temporaryDirectory.Path, "missing.json"));

        var loaded = await repository.LoadAsync();

        Assert.Empty(loaded.Presets);
        Assert.Equal(1, loaded.Version);
    }

    [Fact]
    public async Task Corrupt_file_returns_empty_collection_without_throwing()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = Path.Combine(temporaryDirectory.Path, "presets.json");
        Directory.CreateDirectory(temporaryDirectory.Path);
        await File.WriteAllTextAsync(path, "{ not valid json");
        var repository = new PresetJsonRepository(path);

        var loaded = await repository.LoadAsync();

        Assert.Empty(loaded.Presets);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"flux-display-tests-{Guid.NewGuid():N}");
        public string Path { get; }
        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
