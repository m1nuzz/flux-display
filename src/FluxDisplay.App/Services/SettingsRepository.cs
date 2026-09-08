using System.Text.Json;
using System.Text.Json.Serialization;
using FluxDisplay.App.Models;

namespace FluxDisplay.App.Services;

public sealed class SettingsRepository
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public SettingsRepository(string? storagePath = null)
    {
        StoragePath = storagePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluxDisplay",
            "settings.json");
    }

    public string StoragePath { get; }

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(StoragePath))
            {
                return new AppSettings();
            }

            await using var stream = File.OpenRead(StoragePath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, _options, ct).ConfigureAwait(false);
            return settings ?? new AppSettings();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new AppSettings();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
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
                await JsonSerializer.SerializeAsync(stream, settings, _options, ct).ConfigureAwait(false);
            }

            File.Move(temporaryPath, StoragePath, true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
