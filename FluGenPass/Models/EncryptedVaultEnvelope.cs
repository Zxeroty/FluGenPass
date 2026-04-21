namespace FluGenPass.Models;

public sealed class EncryptedVaultEnvelope
{
    public int Version { get; set; } = 1;

    public string NonceBase64 { get; set; } = string.Empty;

    public string CiphertextBase64 { get; set; } = string.Empty;

    public string TagBase64 { get; set; } = string.Empty;
}