using System.Text.Json;
using System.Text.Json.Serialization;

namespace PanelNester.Desktop.Bridge;

internal static class BridgeJson
{
    internal static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    internal static T Deserialize<T>(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new BridgeDispatchException("invalid-payload", $"Bridge payload for {typeof(T).Name} was missing.");
        }

        try
        {
            var value = element.Deserialize<T>(SerializerOptions);
            return value ?? throw new BridgeDispatchException("invalid-payload", $"Bridge payload for {typeof(T).Name} was null.");
        }
        catch (JsonException ex)
        {
            throw new BridgeDispatchException("invalid-payload", $"Bridge payload did not match {typeof(T).Name}.", ex);
        }
    }

    internal static JsonElement ToElement(object? value) =>
        JsonSerializer.SerializeToElement(value, SerializerOptions);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            WriteIndented = false
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
