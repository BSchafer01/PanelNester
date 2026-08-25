using System.Text.Json;
using System.Text.Json.Serialization;

namespace PanelNester.Domain.Models;

public sealed class OptimizationResultStatusJsonConverter : JsonConverter<OptimizationResultStatus>
{
    public override OptimizationResultStatus Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            Enum.TryParse<OptimizationResultStatus>(reader.GetString(), ignoreCase: true, out var status))
        {
            return status;
        }

        if (reader.TokenType == JsonTokenType.Number &&
            reader.TryGetInt32(out var numericStatus) &&
            Enum.IsDefined(typeof(OptimizationResultStatus), numericStatus))
        {
            return (OptimizationResultStatus)numericStatus;
        }

        throw new JsonException("Optimization result status must be 'none', 'valid', or 'stale'.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        OptimizationResultStatus value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            OptimizationResultStatus.None => "none",
            OptimizationResultStatus.Valid => "valid",
            OptimizationResultStatus.Stale => "stale",
            _ => throw new JsonException($"Unsupported optimization result status '{value}'.")
        });
}
