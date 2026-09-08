using System.Text.Json;
using System.Text.Json.Serialization;
using FluxDisplay.Core.Abstractions;
using FluxDisplay.Core.Models;

namespace FluxDisplay.Core.Services;

// Spec-compliant repository — SemaphoreSlim, CamelCase, WriteIndented, LOCALAPPDATA/FluxDisplay/presets.json.
// Kept for backward compatibility; delegates to the same logic as PresetJsonRepository.
public sealed class PresetRepository : IPresetRepository
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public PresetRepository(string? storagePath = null)
    {
        StoragePath = storagePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluxDisplay",
            "presets.json");
    }

    public string StoragePath { get; }

    public async Task<PresetCollection> LoadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(StoragePath))
                return new PresetCollection();

            await using var stream = File.OpenRead(StoragePath);
            return await JsonSerializer.DeserializeAsync<PresetCollection>(stream, _options).ConfigureAwait(false)
                ?? new PresetCollection();
        }
        catch (JsonException)
        {
            return new PresetCollection();
        }
        catch (IOException)
        {
            return new PresetCollection();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(PresetCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var directory = Path.GetDirectoryName(StoragePath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Storage path must include a directory.");

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);
            var temporaryPath = $"{StoragePath}.{Guid.NewGuid():N}.tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, collection, _options).ConfigureAwait(false);
            }

            File.Move(temporaryPath, StoragePath, true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
