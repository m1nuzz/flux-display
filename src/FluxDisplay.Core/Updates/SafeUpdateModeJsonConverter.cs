using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxDisplay.Core.Updates;

// Reads UpdateMode losslessly when possible, recovers controllably otherwise:
// unknown strings and explicit null fall back to Automatic (the product
// default) WITHOUT failing the whole settings document. Anything that is not
// a string or null (numbers, objects) is a corrupt document: throw and let the
// repository apply its document-level fallback policy.
public sealed class SafeUpdateModeJsonConverter : JsonConverter<UpdateMode>
{
    private readonly Action<string>? _log;

    public SafeUpdateModeJsonConverter(Action<string>? log = null)
    {
        _log = log;
    }

    public override UpdateMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return UpdateMode.Automatic;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected string or null for UpdateMode, got {reader.TokenType}.");
        }

        var text = reader.GetString();
        foreach (UpdateMode mode in Enum.GetValues(typeof(UpdateMode)))
        {
            if (string.Equals(mode.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                return mode;
            }
        }

        _log?.Invoke($"Unknown UpdateMode '{text}', falling back to Automatic.");
        return UpdateMode.Automatic;
    }

    public override void Write(Utf8JsonWriter writer, UpdateMode value, JsonSerializerOptions options)
    {
        var text = value.ToString();
        writer.WriteStringValue(char.ToLowerInvariant(text[0]) + text.Substring(1));
    }
}
