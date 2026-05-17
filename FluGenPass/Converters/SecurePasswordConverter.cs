using System.Text.Json;
using System.Text.Json.Serialization;
using FluGenPass.Services;

namespace FluGenPass.Converters;

public sealed class SecurePasswordConverter : JsonConverter<char[]>
{
    public override char[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected string for password.");
        }

        string? s = reader.GetString();
        if (s == null) return Array.Empty<char>();
        
        char[] result = s.ToCharArray();
        return result;
    }

    public override void Write(Utf8JsonWriter writer, char[] value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        
        // This still creates a temporary string, but it's transient during serialization.
        // The ciphertext will be encrypted immediately after.
        writer.WriteStringValue(new string(value));
    }
}
