using System.Text.Json.Serialization;

namespace FluGenPass.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VaultTransferFormat
{
    FluGenPassSecureExport,
    BitwardenCsv
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VaultIntegrityStatus
{
    NotAvailable,
    Verified,
    MissingChecksum,
    MissingSignature,
    UntrustedSignature
}

public sealed class VaultTransferPackage
{
    public string Format { get; set; } = "FluGenPass.Export";

    public int Version { get; set; } = 1;

    public VaultTransferPayload Payload { get; set; } = new();

    public VaultTransferIntegrity Integrity { get; set; } = new();

    public VaultTransferSignature Signature { get; set; } = new();
}

public sealed class VaultTransferPayload
{
    public string SourceApp { get; set; } = "FluGenPass";

    public DateTimeOffset ExportedUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<VaultEntry> Entries { get; set; } = [];
}

public sealed class VaultTransferIntegrity
{
    public string Algorithm { get; set; } = "SHA-256";

    public string PayloadHashHex { get; set; } = string.Empty;
}

public sealed class VaultTransferSignature
{
    public string Algorithm { get; set; } = "ECDSA-P256-SHA256";

    public string SignerKeyId { get; set; } = string.Empty;

    public string PublicKeyBase64 { get; set; } = string.Empty;

    public string SignatureBase64 { get; set; } = string.Empty;
}

public sealed class VaultDetachedSignaturePackage
{
    public string Format { get; set; } = "FluGenPass.DetachedSignature";

    public int Version { get; set; } = 1;

    public string FileName { get; set; } = string.Empty;

    public string IntegrityAlgorithm { get; set; } = "SHA-256";

    public string PayloadHashHex { get; set; } = string.Empty;

    public VaultTransferSignature Signature { get; set; } = new();
}

public sealed record VaultExportResult(
    string FilePath,
    VaultTransferFormat Format,
    int ExportedCount,
    VaultIntegrityStatus IntegrityStatus,
    string IntegritySummary,
    string? ChecksumFilePath = null,
    string? SignatureFilePath = null
);

public sealed record VaultImportResult(
    VaultTransferFormat Format,
    int ImportedCount,
    int SkippedCount,
    VaultIntegrityStatus IntegrityStatus,
    string IntegritySummary,
    IReadOnlyList<VaultEntry> Entries,
    IReadOnlyList<string> Warnings
);

public sealed record VaultVerificationResult(
    string FilePath,
    VaultTransferFormat Format,
    VaultIntegrityStatus IntegrityStatus,
    string IntegritySummary,
    int EntryCount,
    IReadOnlyList<string> Warnings
);
