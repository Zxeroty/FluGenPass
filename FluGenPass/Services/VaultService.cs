using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FluGenPass.Models;

namespace FluGenPass.Services;

public sealed class VaultService(string appDirectory, ISessionStateService sessionStateService) : IVaultService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public string VaultFilePath { get; } = Path.Combine(appDirectory, "vault.dat");

    private string TempVaultFilePath { get; } = Path.Combine(appDirectory, "vault.dat.tmp");

    public async Task<IReadOnlyList<VaultEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(VaultFilePath))
        {
            return Array.Empty<VaultEntry>();
        }

        byte[] vaultKey = sessionStateService.GetRequiredVaultKey();

        try
        {
            await using FileStream stream = File.OpenRead(VaultFilePath);
            EncryptedVaultEnvelope? envelope =
                await JsonSerializer.DeserializeAsync<EncryptedVaultEnvelope>(stream, SerializerOptions, cancellationToken);

            if (envelope is null)
            {
                return Array.Empty<VaultEntry>();
            }

            byte[] nonce = Convert.FromBase64String(envelope.NonceBase64);
            byte[] ciphertext = Convert.FromBase64String(envelope.CiphertextBase64);
            byte[] tag = Convert.FromBase64String(envelope.TagBase64);
            byte[] plaintext = new byte[ciphertext.Length];

            try
            {
                using AesGcm aesGcm = new(vaultKey, tag.Length);
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

                List<VaultEntry>? entries = JsonSerializer.Deserialize<List<VaultEntry>>(plaintext, SerializerOptions);
                return entries?
                    .OrderByDescending(entry => entry.CreatedUtc)
                    .ToArray()
                    ?? Array.Empty<VaultEntry>();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(tag);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vaultKey);
        }
    }

    public async Task SaveAsync(IEnumerable<VaultEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        await _saveGate.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(appDirectory);
            DeleteTempFileIfPresent();

            byte[] vaultKey = sessionStateService.GetRequiredVaultKey();
            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(entries, SerializerOptions);
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];

            try
            {
                using AesGcm aesGcm = new(vaultKey, tag.Length);
                aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

                EncryptedVaultEnvelope envelope = new()
                {
                    Version = 1,
                    NonceBase64 = Convert.ToBase64String(nonce),
                    CiphertextBase64 = Convert.ToBase64String(ciphertext),
                    TagBase64 = Convert.ToBase64String(tag),
                };

                try
                {
                    await using (FileStream stream = new(TempVaultFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await JsonSerializer.SerializeAsync(stream, envelope, SerializerOptions, cancellationToken);
                        await stream.FlushAsync(cancellationToken);
                        stream.Flush(true);
                    }

                    File.Move(TempVaultFilePath, VaultFilePath, overwrite: true);
                }
                catch
                {
                    DeleteTempFileIfPresent();
                    throw;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(vaultKey);
                CryptographicOperations.ZeroMemory(plaintext);
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(tag);
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void DeleteTempFileIfPresent()
    {
        if (!File.Exists(TempVaultFilePath))
        {
            return;
        }

        try
        {
            File.Delete(TempVaultFilePath);
        }
        catch
        {
        }
    }
}