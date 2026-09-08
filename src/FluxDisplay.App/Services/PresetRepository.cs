using System.Text.Json;
using System.Text.Json.Serialization;
using FluxDisplay.App.Models;

namespace FluxDisplay.App.Services;

// JSON persistence for presets: WriteIndented true, CamelCase, path %LOCALAPPDATA%/FluxDisplay/presets.json,
// ensures directory creation, returns empty collection on missing/corrupt, thread-safe via SemaphoreSlim.
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

    public async Task<PresetCollection> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(StoragePath))
            {
                return new PresetCollection();
            }

            await using var stream = File.OpenRead(StoragePath);
            var collection = await JsonSerializer.DeserializeAsync<PresetCollection>(stream, _options, ct).ConfigureAwait(false);
            return collection ?? new PresetCollection();
        }
        catch (JsonException)
        {
            // Corrupt JSON — return empty collection without throwing.
            return new PresetCollection();
        }
        catch (IOException)
        {
            return new PresetCollection();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new PresetCollection();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(PresetCollection collection, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var directory = Path.GetDirectoryName(StoragePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Storage path must include a directory.");
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);
            var temporaryPath = $"{StoragePath}.{Guid.NewGuid():N}.tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, collection, _options, ct).ConfigureAwait(false);
            }

            File.Move(temporaryPath, StoragePath, true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
