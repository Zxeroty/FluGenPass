using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FluGenPass.Models;

namespace FluGenPass.Services;

public sealed class TransferSignatureService(string appDirectory) : ITransferSignatureService
{
    private const string SignatureAlgorithm = "ECDSA-P256-SHA256";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _keyLock = new();
    private string KeyFilePath { get; } = Path.Combine(appDirectory, "transfer-signing-key.json");

    public VaultTransferSignature CreateSignature(byte[] payloadBytes)
    {
        ArgumentNullException.ThrowIfNull(payloadBytes);

        SigningKeyMaterial keyMaterial = GetOrCreateKeyMaterial();
        byte[] privateKeyBytes = Convert.FromBase64String(keyMaterial.PrivateKeyPkcs8Base64);

        try
        {
            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
            byte[] signatureBytes = ecdsa.SignData(payloadBytes, HashAlgorithmName.SHA256);

            return new VaultTransferSignature
            {
                Algorithm = SignatureAlgorithm,
                SignerKeyId = keyMaterial.KeyId,
                PublicKeyBase64 = keyMaterial.PublicKeySpkiBase64,
                SignatureBase64 = Convert.ToBase64String(signatureBytes),
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKeyBytes);
        }
    }

    public VaultIntegrityStatus VerifySignature(
        byte[] payloadBytes,
        VaultTransferSignature signature,
        out string integritySummary,
        out IReadOnlyList<string> warnings
    )
    {
        ArgumentNullException.ThrowIfNull(payloadBytes);
        ArgumentNullException.ThrowIfNull(signature);

        if (string.IsNullOrWhiteSpace(signature.PublicKeyBase64) || string.IsNullOrWhiteSpace(signature.SignatureBase64))
        {
            integritySummary = "Digital signature is missing.";
            warnings = ["The file has no usable digital signature."];
            return VaultIntegrityStatus.MissingSignature;
        }

        if (!string.Equals(signature.Algorithm, SignatureAlgorithm, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Signature algorithm '{signature.Algorithm}' is not supported.");
        }

        byte[] publicKeyBytes = Convert.FromBase64String(signature.PublicKeyBase64);
        byte[] signatureBytes = Convert.FromBase64String(signature.SignatureBase64);

        try
        {
            string computedKeyId = ComputeKeyId(publicKeyBytes);

            if (!string.Equals(signature.SignerKeyId, computedKeyId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Digital signature signer fingerprint does not match the embedded public key.");
            }

            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);

            if (!ecdsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256))
            {
                throw new InvalidDataException("Digital signature verification failed.");
            }

            SigningKeyMaterial localKey = GetOrCreateKeyMaterial();
            bool trustedSigner = string.Equals(localKey.KeyId, computedKeyId, StringComparison.OrdinalIgnoreCase);

            if (trustedSigner)
            {
                integritySummary = $"SHA-256 and trusted ECDSA signature verified. Signer key: {computedKeyId}.";
                warnings = [];
                return VaultIntegrityStatus.Verified;
            }

            integritySummary = $"SHA-256 and ECDSA signature verified, but signer is not trusted locally. Signer key: {computedKeyId}.";
            warnings =
            [
                "The file was signed with a valid key, but not with the local FluGenPass signing key for this app instance."
            ];
            return VaultIntegrityStatus.UntrustedSignature;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(publicKeyBytes);
            CryptographicOperations.ZeroMemory(signatureBytes);
        }
    }

    private SigningKeyMaterial GetOrCreateKeyMaterial()
    {
        lock (_keyLock)
        {
            Directory.CreateDirectory(appDirectory);

            if (File.Exists(KeyFilePath))
            {
                SigningKeyMaterial? existing = JsonSerializer.Deserialize<SigningKeyMaterial>(
                    File.ReadAllText(KeyFilePath),
                    SerializerOptions
                );

                if (existing is not null
                    && !string.IsNullOrWhiteSpace(existing.PrivateKeyPkcs8Base64)
                    && !string.IsNullOrWhiteSpace(existing.PublicKeySpkiBase64)
                    && !string.IsNullOrWhiteSpace(existing.KeyId))
                {
                    return existing;
                }
            }

            using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
            byte[] publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();

            try
            {
                SigningKeyMaterial keyMaterial = new()
                {
                    KeyId = ComputeKeyId(publicKeyBytes),
                    CreatedUtc = DateTimeOffset.UtcNow,
                    PrivateKeyPkcs8Base64 = Convert.ToBase64String(privateKeyBytes),
                    PublicKeySpkiBase64 = Convert.ToBase64String(publicKeyBytes),
                };

                File.WriteAllText(KeyFilePath, JsonSerializer.Serialize(keyMaterial, SerializerOptions));
                return keyMaterial;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateKeyBytes);
                CryptographicOperations.ZeroMemory(publicKeyBytes);
            }
        }
    }

    private static string ComputeKeyId(byte[] publicKeyBytes)
    {
        return Convert.ToHexString(SHA256.HashData(publicKeyBytes));
    }

    private sealed class SigningKeyMaterial
    {
        public string Algorithm { get; set; } = "ECDSA-P256";

        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        public string KeyId { get; set; } = string.Empty;

        public string PrivateKeyPkcs8Base64 { get; set; } = string.Empty;

        public string PublicKeySpkiBase64 { get; set; } = string.Empty;
    }
}
