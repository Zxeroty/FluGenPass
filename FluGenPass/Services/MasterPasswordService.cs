using System.IO;
using System.Security.Cryptography;
using System.Text;
using FluGenPass.Models;
using Konscious.Security.Cryptography;

namespace FluGenPass.Services;

public sealed class MasterPasswordService(
    ISettingsService settingsService,
    ISessionStateService sessionStateService,
    IVaultService vaultService
) : IMasterPasswordService
{
    private const int SaltSize = 16;
    private const int KeySize = 32;

    private const int Pbkdf2Iterations = 200000;

    private const int Argon2Iterations = 3;
    private const int Argon2MemoryKb = 65536;
    private const int Argon2Parallelism = 4;

    private static readonly byte[] VerificationPurpose = Encoding.UTF8.GetBytes("FluGenPass.Verify");
    private static readonly byte[] VaultPurpose = Encoding.UTF8.GetBytes("FluGenPass.Vault");

    public bool IsUnlocked => sessionStateService.IsUnlocked;

    public async Task<bool> HasMasterPasswordAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        return settings.MasterPassword is not null;
    }

    public async Task SetMasterPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Master password cannot be empty.", nameof(password));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] baseKey = DeriveBaseKeyArgon2id(password, salt, Argon2Iterations, Argon2MemoryKb, Argon2Parallelism);
        byte[] verificationHash = DerivePurposeHash(baseKey, VerificationPurpose);
        byte[] vaultKey = DerivePurposeHash(baseKey, VaultPurpose);

        try
        {
            AppSettings settings = await settingsService.GetAsync(cancellationToken);
            settings.MasterPassword = new MasterPasswordMetadata
            {
                Algorithm = KdfAlgorithm.Argon2id,
                SaltBase64 = Convert.ToBase64String(salt),
                VerificationHashBase64 = Convert.ToBase64String(verificationHash),
                Iterations = Argon2Iterations,
                MemorySizeKb = Argon2MemoryKb,
                DegreeOfParallelism = Argon2Parallelism,
            };

            await settingsService.SaveAsync(settings, cancellationToken);
            sessionStateService.SetVaultKey(vaultKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(baseKey);
            CryptographicOperations.ZeroMemory(verificationHash);
            CryptographicOperations.ZeroMemory(vaultKey);
        }
    }

    public async Task ChangeMasterPasswordAsync(string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            throw new ArgumentException("Master password cannot be empty.", nameof(newPassword));
        }

        if (!sessionStateService.IsUnlocked)
        {
            throw new InvalidOperationException("Unlock the vault before changing the master password.");
        }

        IReadOnlyList<VaultEntry> entries = await vaultService.LoadAsync(cancellationToken);
        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        MasterPasswordMetadata? previousMetadata = CloneMetadata(settings.MasterPassword);
        byte[] previousVaultKey = sessionStateService.GetRequiredVaultKey();

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] baseKey = DeriveBaseKeyArgon2id(newPassword, salt, Argon2Iterations, Argon2MemoryKb, Argon2Parallelism);
        byte[] verificationHash = DerivePurposeHash(baseKey, VerificationPurpose);
        byte[] newVaultKey = DerivePurposeHash(baseKey, VaultPurpose);

        try
        {
            sessionStateService.SetVaultKey(newVaultKey);
            await vaultService.SaveAsync(entries, cancellationToken);

            settings.MasterPassword = new MasterPasswordMetadata
            {
                Algorithm = KdfAlgorithm.Argon2id,
                SaltBase64 = Convert.ToBase64String(salt),
                VerificationHashBase64 = Convert.ToBase64String(verificationHash),
                Iterations = Argon2Iterations,
                MemorySizeKb = Argon2MemoryKb,
                DegreeOfParallelism = Argon2Parallelism,
            };

            await settingsService.SaveAsync(settings, cancellationToken);
        }
        catch
        {
            try
            {
                sessionStateService.SetVaultKey(previousVaultKey);
                await vaultService.SaveAsync(entries, cancellationToken);

                settings.MasterPassword = CloneMetadata(previousMetadata);
                await settingsService.SaveAsync(settings, cancellationToken);
            }
            catch
            {
            }

            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(previousVaultKey);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(baseKey);
            CryptographicOperations.ZeroMemory(verificationHash);
            CryptographicOperations.ZeroMemory(newVaultKey);
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        MasterPasswordMetadata? previousMetadata = CloneMetadata(settings.MasterPassword);
        byte[]? vaultBackup = null;

        if (File.Exists(vaultService.VaultFilePath))
        {
            vaultBackup = await File.ReadAllBytesAsync(vaultService.VaultFilePath, cancellationToken);
        }

        try
        {
            SecureDeleteFile(vaultService.VaultFilePath);

            settings.MasterPassword = null;
            await settingsService.SaveAsync(settings, cancellationToken);
            sessionStateService.Lock();
        }
        catch
        {
            try
            {
                settings.MasterPassword = CloneMetadata(previousMetadata);
                await settingsService.SaveAsync(settings, cancellationToken);

                if (vaultBackup is not null)
                {
                    await File.WriteAllBytesAsync(vaultService.VaultFilePath, vaultBackup, cancellationToken);
                }
            }
            catch
            {
            }

            throw;
        }
        finally
        {
            if (vaultBackup is not null)
            {
                CryptographicOperations.ZeroMemory(vaultBackup);
            }
        }
    }

    public async Task<bool> TryUnlockAsync(string password, CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        MasterPasswordMetadata? metadata = settings.MasterPassword;

        if (metadata is null)
        {
            return false;
        }

        byte[] salt = Convert.FromBase64String(metadata.SaltBase64);
        byte[] expectedHash = Convert.FromBase64String(metadata.VerificationHashBase64);
        byte[] baseKey = metadata.Algorithm switch
        {
            KdfAlgorithm.Pbkdf2 => DeriveBaseKeyPbkdf2(password, salt, metadata.Iterations),
            KdfAlgorithm.Argon2id => DeriveBaseKeyArgon2id(password, salt, metadata.Iterations, metadata.MemorySizeKb, metadata.DegreeOfParallelism),
            _ => throw new NotSupportedException($"KDF algorithm '{metadata.Algorithm}' is not supported.")
        };

        byte[] candidateHash = DerivePurposeHash(baseKey, VerificationPurpose);
        byte[] vaultKey = DerivePurposeHash(baseKey, VaultPurpose);

        try
        {
            bool isMatch = CryptographicOperations.FixedTimeEquals(expectedHash, candidateHash);

            if (isMatch)
            {
                sessionStateService.SetVaultKey(vaultKey);
            }

            return isMatch;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(expectedHash);
            CryptographicOperations.ZeroMemory(baseKey);
            CryptographicOperations.ZeroMemory(candidateHash);
            CryptographicOperations.ZeroMemory(vaultKey);
        }
    }

    public void Lock()
    {
        sessionStateService.Lock();
    }

    private static byte[] DeriveBaseKeyPbkdf2(string password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySize);
    }

    private static byte[] DeriveBaseKeyArgon2id(string password, byte[] salt, int iterations, int memoryKb, int parallelism)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password));
        argon2.Salt = salt;
        argon2.DegreeOfParallelism = parallelism;
        argon2.MemorySize = memoryKb;
        argon2.Iterations = iterations;

        return argon2.GetBytes(KeySize);
    }

    private static byte[] DerivePurposeHash(byte[] baseKey, byte[] purpose)
    {
        byte[] buffer = new byte[baseKey.Length + purpose.Length];
        Buffer.BlockCopy(baseKey, 0, buffer, 0, baseKey.Length);
        Buffer.BlockCopy(purpose, 0, buffer, baseKey.Length, purpose.Length);

        try
        {
            return SHA256.HashData(buffer);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static MasterPasswordMetadata? CloneMetadata(MasterPasswordMetadata? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        return new MasterPasswordMetadata
        {
            Algorithm = metadata.Algorithm,
            SaltBase64 = metadata.SaltBase64,
            VerificationHashBase64 = metadata.VerificationHashBase64,
            Iterations = metadata.Iterations,
            MemorySizeKb = metadata.MemorySizeKb,
            DegreeOfParallelism = metadata.DegreeOfParallelism,
        };
    }

    private static void SecureDeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            long length = new FileInfo(path).Length;
            int bufferSize = (int)Math.Min(length, 1024 * 1024);
            byte[] randomData = new byte[bufferSize];

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                long remaining = length;
                while (remaining > 0)
                {
                    int toWrite = (int)Math.Min(remaining, bufferSize);
                    RandomNumberGenerator.Fill(randomData);
                    fs.Write(randomData, 0, toWrite);
                    remaining -= toWrite;
                }
                fs.Flush(true);
            }
            File.Delete(path);
        }
        catch
        {
            try { File.Delete(path); } catch { }
        }
    }
}
