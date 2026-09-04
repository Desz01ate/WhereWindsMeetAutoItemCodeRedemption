using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhereWindsMeetItemCodeRedeemer.Models;

[JsonConverter(typeof(ClientSizeJsonConverter))]
public record ClientSize(int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height}";
}

public class ClientSizeJsonConverter : JsonConverter<ClientSize>
{
    public override ClientSize? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected start of array for ClientSize");

        reader.Read();
        int width = reader.GetInt32();

        reader.Read();
        int height = reader.GetInt32();

        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException("Expected end of array for ClientSize");

        return new ClientSize(width, height);
    }

    public override void Write(Utf8JsonWriter writer, ClientSize value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.Width);
        writer.WriteNumberValue(value.Height);
        writer.WriteEndArray();
    }
}
