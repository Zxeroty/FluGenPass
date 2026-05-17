using System.Text.Json.Serialization;
using FluGenPass.Converters;

namespace FluGenPass.Models;

public sealed class VaultEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string SiteName { get; set; } = string.Empty;

    [JsonConverter(typeof(SecurePasswordConverter))]
    public char[] Password { get; set; } = Array.Empty<char>();

    public List<string> Tags { get; set; } = [];

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
