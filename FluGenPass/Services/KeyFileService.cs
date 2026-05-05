using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluGenPass.Models;

namespace FluGenPass.Services;

public sealed class KeyFileService(
    ISettingsService settingsService,
    ISessionStateService sessionStateService
) : IKeyFileService
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int KeyFileVersion = 1;
    private const string KeyFileFormat = "FluGenPass Key File";

    private static readonly byte[] VerificationPurpose = Encoding.UTF8.GetBytes("FluGenPass.KeyFile.Verify");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        return settings.KeyFile is not null;
    }

    public async Task<KeyFileMetadata?> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        return settings.KeyFile is null
            ? null
            : new KeyFileMetadata
            {
                Version = settings.KeyFile.Version,
                FileName = settings.KeyFile.FileName,
                SaltBase64 = settings.KeyFile.SaltBase64,
                VerificationHashBase64 = settings.KeyFile.VerificationHashBase64,
                CreatedUtc = settings.KeyFile.CreatedUtc,
            };
    }

    public async Task<KeyFileCreationResult> CreateKeyFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Key file path cannot be empty.", nameof(filePath));
        }

        byte[] currentVaultKey = sessionStateService.GetRequiredVaultKey();
        byte[] keyFileSecret = RandomNumberGenerator.GetBytes(KeySize);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] verificationHash = ComputeVerificationHash(keyFileSecret, salt);

        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            KeyFileDocument document = new()
            {
                Format = KeyFileFormat,
                Version = KeyFileVersion,
                KeyBase64 = Convert.ToBase64String(keyFileSecret),
                CreatedUtc = DateTimeOffset.UtcNow,
            };

            await using (FileStream stream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken);
            }

            KeyFileMetadata metadata = new KeyFileMetadata
            {
                Version = KeyFileVersion,
                FileName = Path.GetFileName(filePath),
                SaltBase64 = Convert.ToBase64String(salt),
                VerificationHashBase64 = Convert.ToBase64String(verificationHash),
                CreatedUtc = document.CreatedUtc,
            };

            return new KeyFileCreationResult(metadata, keyFileSecret);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(keyFileSecret);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(currentVaultKey);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(verificationHash);
        }
    }

    public async Task<bool> VerifyAsync(string filePath, CancellationToken cancellationToken = default)
    {
        byte[]? secret = await GetAndVerifySecretAsync(filePath, cancellationToken);
        if (secret is not null)
        {
            CryptographicOperations.ZeroMemory(secret);
            return true;
        }

        return false;
    }

    public async Task<byte[]?> GetAndVerifySecretAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        KeyFileMetadata? metadata = settings.KeyFile;
        if (metadata is null)
        {
            return null;
        }

        byte[]? keyFileSecret = null;
        byte[]? salt = null;
        byte[]? expectedHash = null;
        byte[]? candidateHash = null;

        try
        {
            await using FileStream stream = File.OpenRead(filePath);
            KeyFileDocument? document =
                await JsonSerializer.DeserializeAsync<KeyFileDocument>(stream, SerializerOptions, cancellationToken);

            if (document is null ||
                document.Version != KeyFileVersion ||
                !string.Equals(document.Format, KeyFileFormat, StringComparison.Ordinal))
            {
                return null;
            }

            keyFileSecret = Convert.FromBase64String(document.KeyBase64);
            if (keyFileSecret.Length != KeySize)
            {
                ZeroIfPresent(keyFileSecret);
                return null;
            }

            salt = Convert.FromBase64String(metadata.SaltBase64);
            expectedHash = Convert.FromBase64String(metadata.VerificationHashBase64);
            candidateHash = ComputeVerificationHash(keyFileSecret, salt);

            if (CryptographicOperations.FixedTimeEquals(expectedHash, candidateHash))
            {
                return keyFileSecret;
            }
            else
            {
                ZeroIfPresent(keyFileSecret);
                return null;
            }
        }
        catch
        {
            ZeroIfPresent(keyFileSecret);
            return null;
        }
        finally
        {
            ZeroIfPresent(salt);
            ZeroIfPresent(expectedHash);
            ZeroIfPresent(candidateHash);
        }
    }

    public async Task DisableAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        settings.KeyFile = null;
        await settingsService.SaveAsync(settings, cancellationToken);
    }

    private static byte[] ComputeVerificationHash(byte[] keyFileSecret, byte[] salt)
    {
        byte[] buffer = new byte[salt.Length + keyFileSecret.Length + VerificationPurpose.Length];
        Buffer.BlockCopy(salt, 0, buffer, 0, salt.Length);
        Buffer.BlockCopy(keyFileSecret, 0, buffer, salt.Length, keyFileSecret.Length);
        Buffer.BlockCopy(VerificationPurpose, 0, buffer, salt.Length + keyFileSecret.Length, VerificationPurpose.Length);

        try
        {
            return SHA256.HashData(buffer);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static void ZeroIfPresent(byte[]? value)
    {
        if (value is not null)
        {
            CryptographicOperations.ZeroMemory(value);
        }
    }
}
