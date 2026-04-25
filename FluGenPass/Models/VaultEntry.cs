namespace FluGenPass.Models;

public sealed class VaultEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string SiteName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
