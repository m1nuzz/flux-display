using System.Text.Json;
using System.Text.Json.Serialization;
using FluxDisplay.Core.Abstractions;
using FluxDisplay.Core.Models;

namespace FluxDisplay.Core.Services;

// Stores preset collections as indented camelCase JSON under LOCALAPPDATA/FluxDisplay/presets.json.
// Uses a SemaphoreSlim gate to serialize concurrent read/write and an atomic temp-file move.
public sealed class PresetJsonRepository : IPresetRepository
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly JsonSerializerOptions options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public PresetJsonRepository(string? storagePath = null)
    {
        StoragePath = storagePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluxDisplay",
            "presets.json");
    }

    public string StoragePath { get; }

    public async Task<PresetCollection> LoadAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(StoragePath))
                return new PresetCollection();

            await using var stream = File.OpenRead(StoragePath);
            var collection = await JsonSerializer.DeserializeAsync<PresetCollection>(stream, options).ConfigureAwait(false);
            return PresetMigrator.Normalize(collection);
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
            gate.Release();
        }
    }

    public async Task SaveAsync(PresetCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        PresetMigrator.Normalize(collection);
        var directory = Path.GetDirectoryName(StoragePath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Storage path must include a directory.");

        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);
            var temporaryPath = $"{StoragePath}.{Guid.NewGuid():N}.tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, collection, options).ConfigureAwait(false);
            }

            File.Move(temporaryPath, StoragePath, true);
        }
        finally
        {
            gate.Release();
        }
    }
}
