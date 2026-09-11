using System.Text.Json;
using Xunit;
using FluxDisplay.Core.Updates;

namespace FluxDisplay.Core.Tests;

// Settings JSON compatibility for UpdateMode: missing -> Automatic, valid
// camelCase round-trips, unknown strings recover without losing the document,
// null recovers, non-string tokens are a corrupt document.
public sealed class UpdateSettingsJsonTests
{
    private sealed class Doc
    {
        public string Name { get; set; } = string.Empty;
        public UpdateMode UpdateMode { get; set; } = UpdateMode.Automatic;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new SafeUpdateModeJsonConverter() }
    };

    [Fact]
    public void Missing_mode_defaults_to_automatic()
    {
        var doc = JsonSerializer.Deserialize<Doc>("{\"name\":\"kept\"}", Options);
        Assert.NotNull(doc);
        Assert.Equal("kept", doc!.Name);
        Assert.Equal(UpdateMode.Automatic, doc.UpdateMode);
    }

    [Theory]
    [InlineData("automatic", UpdateMode.Automatic)]
    [InlineData("notifyOnly", UpdateMode.NotifyOnly)]
    [InlineData("disabled", UpdateMode.Disabled)]
    [InlineData("Automatic", UpdateMode.Automatic)]
    public void Valid_modes_parse(string text, UpdateMode expected)
    {
        var doc = JsonSerializer.Deserialize<Doc>("{\"updateMode\":\"" + text + "\"}", Options);
        Assert.Equal(expected, doc!.UpdateMode);
    }

    [Fact]
    public void Unknown_mode_recovers_and_keeps_other_properties()
    {
        string? logged = null;
        var options = new JsonSerializerOptions(Options);
        options.Converters.Clear();
        options.Converters.Add(new SafeUpdateModeJsonConverter(m => logged = m));
        var doc = JsonSerializer.Deserialize<Doc>("{\"name\":\"kept\",\"updateMode\":\"turbo\"}", options);
        Assert.NotNull(doc);
        Assert.Equal("kept", doc!.Name);
        Assert.Equal(UpdateMode.Automatic, doc.UpdateMode);
        Assert.NotNull(logged);
    }

    [Fact]
    public void Null_mode_recovers_to_automatic()
    {
        var doc = JsonSerializer.Deserialize<Doc>("{\"updateMode\":null}", Options);
        Assert.Equal(UpdateMode.Automatic, doc!.UpdateMode);
    }

    [Fact]
    public void Numeric_mode_is_corrupt_document()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Doc>("{\"updateMode\":42}", Options));
    }

    [Fact]
    public void Mode_serializes_camel_case()
    {
        var json = JsonSerializer.Serialize(new Doc { UpdateMode = UpdateMode.NotifyOnly }, Options);
        Assert.Contains("\"updateMode\":\"notifyOnly\"", json);
    }
}
