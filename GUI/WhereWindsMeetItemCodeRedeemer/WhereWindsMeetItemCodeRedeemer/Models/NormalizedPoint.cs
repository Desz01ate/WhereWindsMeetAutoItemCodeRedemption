using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhereWindsMeetItemCodeRedeemer.Models;

[JsonConverter(typeof(NormalizedPointJsonConverter))]
public record NormalizedPoint(double X, double Y)
{
    public override string ToString() => $"({X:F4}, {Y:F4})";
}

public class NormalizedPointJsonConverter : JsonConverter<NormalizedPoint>
{
    public override NormalizedPoint? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected start of array for NormalizedPoint");

        reader.Read();
        double x = reader.GetDouble();

        reader.Read();
        double y = reader.GetDouble();

        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException("Expected end of array for NormalizedPoint");

        return new NormalizedPoint(x, y);
    }

    public override void Write(Utf8JsonWriter writer, NormalizedPoint value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(Math.Round(value.X, 6));
        writer.WriteNumberValue(Math.Round(value.Y, 6));
        writer.WriteEndArray();
    }
}
