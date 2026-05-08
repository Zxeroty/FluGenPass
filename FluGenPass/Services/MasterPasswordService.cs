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

    private static readonly byte[] VerificationPurpose = Encoding.UTF8.GetBytes("FluGenPass.Verify");
    private static readonly byte[] VaultPurpose = Encoding.UTF8.GetBytes("FluGenPass.Vault");
    private static readonly byte[] VaultWrappingPurpose = Encoding.UTF8.GetBytes("FluGenPass.VaultKeyWrap");
    private static readonly byte[] CompositeVaultWrappingPurpose =
        Encoding.UTF8.GetBytes("FluGenPass.CompositeVaultKeyWrap.v1");

    private record Argon2Parameters(int Iterations, int MemoryKb, int Parallelism);

    private static Argon2Parameters GetAdaptiveParameters()
    {
        // Parallelism: Use available cores, but cap at 8 to avoid excessive overhead
        int parallelism = Math.Clamp(Environment.ProcessorCount, 1, 8);

        // Memory: Aim for 256MB as a modern baseline, but adjust based on system RAM
        // We'll try to use ~1/32 of total RAM, but between 128MB and 1GB
        long totalMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        int memoryKb = 262144; // Default 256MB

        if (totalMemoryBytes > 0)
        {
            long targetMemoryKb = totalMemoryBytes / 1024 / 32;
            memoryKb = (int)Math.Clamp(targetMemoryKb, 131072, 1048576); // 128MB to 1GB
        }

        // Iterations: 3 is a good balance for Argon2id
        return new Argon2Parameters(3, memoryKb, parallelism);
    }

    public bool IsUnlocked => sessionStateService.IsUnlocked;

    public async Task<bool> HasMasterPasswordAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        return settings.MasterPassword is not null;
    }

    public async Task<bool> VerifyPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        MasterPasswordMetadata? metadata = settings.MasterPassword;

        if (metadata is null)
        {
            return false;
        }

        byte[] salt = Convert.FromBase64String(metadata.SaltBase64);
        byte[] expectedHash = Convert.FromBase64String(metadata.VerificationHashBase64);
        byte[] baseKey = DeriveBaseKey(password, metadata, salt);
        byte[] candidateHash = DerivePurposeHash(baseKey, VerificationPurpose);

        try
        {
            return CryptographicOperations.FixedTimeEquals(expectedHash, candidateHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(expectedHash);
            CryptographicOperations.ZeroMemory(baseKey);
            CryptographicOperations.ZeroMemory(candidateHash);
        }
    }

    public async Task SetMasterPasswordAsync(
        string password,
        byte[]? keyFileSecret = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Master password cannot be empty.", nameof(password));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        Argon2Parameters argon2 = GetAdaptiveParameters();
        byte[] baseKey = DeriveBaseKeyArgon2id(password, salt, argon2.Iterations, argon2.MemoryKb, argon2.Parallelism);
        byte[] verificationHash = DerivePurposeHash(baseKey, VerificationPurpose);
        byte[] passwordWrappingKey = DerivePurposeHash(baseKey, VaultWrappingPurpose);
        byte[]? effectiveWrappingKey = keyFileSecret is null
            ? passwordWrappingKey
            : DeriveCompositeWrappingKey(passwordWrappingKey, keyFileSecret);
        byte[] vaultKey = RandomNumberGenerator.GetBytes(KeySize);
        WrappedVaultKey wrappedVaultKey = WrapVaultKey(vaultKey, effectiveWrappingKey);

        try
        {
            AppSettings settings = await settingsService.GetAsync(cancellationToken);
            settings.MasterPassword = CreateArgon2idMetadata(
                salt,
                verificationHash,
                wrappedVaultKey,
                keyFileSecret is null ? VaultKeyProtectionMode.PasswordOnly : VaultKeyProtectionMode.PasswordAndKeyFile,
                argon2
            );

            await settingsService.SaveAsync(settings, cancellationToken);
            sessionStateService.SetVaultKey(vaultKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(baseKey);
            CryptographicOperations.ZeroMemory(verificationHash);
            CryptographicOperations.ZeroMemory(passwordWrappingKey);
            if (!ReferenceEquals(passwordWrappingKey, effectiveWrappingKey))
            {
                CryptographicOperations.ZeroMemory(effectiveWrappingKey);
            }
            CryptographicOperations.ZeroMemory(vaultKey);
            wrappedVaultKey.Clear();
        }
    }

    public async Task ChangeMasterPasswordAsync(
        string newPassword,
        byte[]? newKeyFileSecret = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            throw new ArgumentException("Master password cannot be empty.", nameof(newPassword));
        }

        if (!sessionStateService.IsUnlocked)
        {
            throw new InvalidOperationException("Unlock the vault before changing the master password.");
        }

        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        MasterPasswordMetadata? previousMetadata = CloneMetadata(settings.MasterPassword);
        bool requiresKeyFile = settings.KeyFile is not null ||
                               previousMetadata?.VaultKeyProtectionMode == VaultKeyProtectionMode.PasswordAndKeyFile;

        if (requiresKeyFile && newKeyFileSecret is null)
        {
            throw new InvalidOperationException("The current key file is required before changing the master password.");
        }

        byte[] currentVaultKey = sessionStateService.GetRequiredVaultKey();
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        Argon2Parameters argon2 = GetAdaptiveParameters();
        byte[] baseKey = DeriveBaseKeyArgon2id(newPassword, salt, argon2.Iterations, argon2.MemoryKb, argon2.Parallelism);
        byte[] verificationHash = DerivePurposeHash(baseKey, VerificationPurpose);
        byte[] passwordWrappingKey = DerivePurposeHash(baseKey, VaultWrappingPurpose);
        byte[]? effectiveWrappingKey = requiresKeyFile
            ? DeriveCompositeWrappingKey(passwordWrappingKey, newKeyFileSecret!)
            : passwordWrappingKey;
        WrappedVaultKey wrappedVaultKey = WrapVaultKey(currentVaultKey, effectiveWrappingKey);

        try
        {
            settings.MasterPassword = CreateArgon2idMetadata(
                salt,
                verificationHash,
                wrappedVaultKey,
                requiresKeyFile ? VaultKeyProtectionMode.PasswordAndKeyFile : VaultKeyProtectionMode.PasswordOnly,
                argon2
            );

            await settingsService.SaveAsync(settings, cancellationToken);
        }
        catch
        {
            try
            {
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
            CryptographicOperations.ZeroMemory(currentVaultKey);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(baseKey);
            CryptographicOperations.ZeroMemory(verificationHash);
            CryptographicOperations.ZeroMemory(passwordWrappingKey);
            if (!ReferenceEquals(passwordWrappingKey, effectiveWrappingKey))
            {
                CryptographicOperations.ZeroMemory(effectiveWrappingKey);
            }
            wrappedVaultKey.Clear();
        }
    }

    public async Task EnableKeyFileAsync(
        string password,
        KeyFileMetadata keyFileMetadata,
        byte[] keyFileSecret,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(keyFileMetadata);
        ArgumentNullException.ThrowIfNull(keyFileSecret);

        if (!sessionStateService.IsUnlocked)
        {
            throw new InvalidOperationException("Unlock the vault before enabling key file protection.");
        }

        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        MasterPasswordMetadata? metadata = settings.MasterPassword;
        if (metadata is null)
        {
            throw new InvalidOperationException("Create a master password before enabling key file protection.");
        }

        MasterPasswordMetadata? previousMetadata = CloneMetadata(settings.MasterPassword);
        KeyFileMetadata? previousKeyFileMetadata = CloneKeyFileMetadata(settings.KeyFile);
        byte[] currentVaultKey = sessionStateService.GetRequiredVaultKey();
        byte[] salt = Convert.FromBase64String(metadata.SaltBase64);
        byte[] expectedHash = Convert.FromBase64String(metadata.VerificationHashBase64);
        byte[] baseKey = DeriveBaseKey(password, metadata, salt);
        byte[] candidateHash = DerivePurposeHash(baseKey, VerificationPurpose);
        byte[] passwordWrappingKey = DerivePurposeHash(baseKey, VaultWrappingPurpose);
        byte[] compositeWrappingKey = DeriveCompositeWrappingKey(passwordWrappingKey, keyFileSecret);
        WrappedVaultKey wrappedVaultKey = WrapVaultKey(currentVaultKey, compositeWrappingKey);

        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, candidateHash))
            {
                throw new UnauthorizedAccessException("The master password did not unlock the vault.");
            }

            settings.MasterPassword = CloneMetadata(metadata);
            settings.MasterPassword!.VaultKeyNonceBase64 = Convert.ToBase64String(wrappedVaultKey.Nonce);
            settings.MasterPassword.VaultKeyCiphertextBase64 = Convert.ToBase64String(wrappedVaultKey.Ciphertext);
            settings.MasterPassword.VaultKeyTagBase64 = Convert.ToBase64String(wrappedVaultKey.Tag);
            settings.MasterPassword.VaultKeyProtectionMode = VaultKeyProtectionMode.PasswordAndKeyFile;
            settings.KeyFile = CloneKeyFileMetadata(keyFileMetadata);
            await settingsService.SaveAsync(settings, cancellationToken);
        }
        catch
        {
            try
            {
                settings.MasterPassword = CloneMetadata(previousMetadata);
                settings.KeyFile = CloneKeyFileMetadata(previousKeyFileMetadata);
                await settingsService.SaveAsync(settings, cancellationToken);
            }
            catch
            {
            }

            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(currentVaultKey);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(expectedHash);
            CryptographicOperations.ZeroMemory(baseKey);
            CryptographicOperations.ZeroMemory(candidateHash);
            CryptographicOperations.ZeroMemory(passwordWrappingKey);
            CryptographicOperations.ZeroMemory(compositeWrappingKey);
            wrappedVaultKey.Clear();
        }
    }

    public async Task DisableKeyFileAsync(string password, CancellationToken cancellationToken = default)
    {
        if (!sessionStateService.IsUnlocked)
        {
            throw new InvalidOperationException("Unlock the vault before disabling key file protection.");
        }

        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        MasterPasswordMetadata? metadata = settings.MasterPassword;
        if (metadata is null)
        {
            return;
        }

        MasterPasswordMetadata? previousMetadata = CloneMetadata(settings.MasterPassword);
        KeyFileMetadata? previousKeyFileMetadata = CloneKeyFileMetadata(settings.KeyFile);
        byte[] currentVaultKey = sessionStateService.GetRequiredVaultKey();
        byte[] salt = Convert.FromBase64String(metadata.SaltBase64);
        byte[] expectedHash = Convert.FromBase64String(metadata.VerificationHashBase64);
        byte[] baseKey = DeriveBaseKey(password, metadata, salt);
        byte[] candidateHash = DerivePurposeHash(baseKey, VerificationPurpose);
        byte[] passwordWrappingKey = DerivePurposeHash(baseKey, VaultWrappingPurpose);
        WrappedVaultKey wrappedVaultKey = WrapVaultKey(currentVaultKey, passwordWrappingKey);

        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, candidateHash))
            {
                throw new UnauthorizedAccessException("The master password did not unlock the vault.");
            }

            settings.MasterPassword = CloneMetadata(metadata);
            settings.MasterPassword!.VaultKeyNonceBase64 = Convert.ToBase64String(wrappedVaultKey.Nonce);
            settings.MasterPassword.VaultKeyCiphertextBase64 = Convert.ToBase64String(wrappedVaultKey.Ciphertext);
            settings.MasterPassword.VaultKeyTagBase64 = Convert.ToBase64String(wrappedVaultKey.Tag);
            settings.MasterPassword.VaultKeyProtectionMode = VaultKeyProtectionMode.PasswordOnly;
            settings.KeyFile = null;
            await settingsService.SaveAsync(settings, cancellationToken);
        }
        catch
        {
            try
            {
                settings.MasterPassword = CloneMetadata(previousMetadata);
                settings.KeyFile = CloneKeyFileMetadata(previousKeyFileMetadata);
                await settingsService.SaveAsync(settings, cancellationToken);
            }
            catch
            {
            }

            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(currentVaultKey);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(expectedHash);
            CryptographicOperations.ZeroMemory(baseKey);
            CryptographicOperations.ZeroMemory(candidateHash);
            CryptographicOperations.ZeroMemory(passwordWrappingKey);
            wrappedVaultKey.Clear();
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        MasterPasswordMetadata? previousMetadata = CloneMetadata(settings.MasterPassword);
        KeyFileMetadata? previousKeyFileMetadata = CloneKeyFileMetadata(settings.KeyFile);
        byte[]? vaultBackup = null;

        if (File.Exists(vaultService.VaultFilePath))
        {
            vaultBackup = await File.ReadAllBytesAsync(vaultService.VaultFilePath, cancellationToken);
        }

        try
        {
            SecureDeleteFile(vaultService.VaultFilePath);

            settings.MasterPassword = null;
            settings.KeyFile = null;
            await settingsService.SaveAsync(settings, cancellationToken);
            sessionStateService.Lock();
        }
        catch
        {
            try
            {
                settings.MasterPassword = CloneMetadata(previousMetadata);
                settings.KeyFile = CloneKeyFileMetadata(previousKeyFileMetadata);
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

    public async Task<bool> TryUnlockAsync(
        string password,
        byte[]? keyFileSecret = null,
        CancellationToken cancellationToken = default
    )
    {
        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        MasterPasswordMetadata? metadata = settings.MasterPassword;

        if (metadata is null)
        {
            return false;
        }

        byte[] salt = Convert.FromBase64String(metadata.SaltBase64);
        byte[] expectedHash = Convert.FromBase64String(metadata.VerificationHashBase64);
        byte[] baseKey = DeriveBaseKey(password, metadata, salt);
        byte[] candidateHash = DerivePurposeHash(baseKey, VerificationPurpose);
        byte[] passwordWrappingKey = DerivePurposeHash(baseKey, VaultWrappingPurpose);
        byte[]? effectiveWrappingKey = null;
        byte[]? vaultKey = null;
        WrappedVaultKey? migratedWrappedVaultKey = null;

        try
        {
            bool isMatch = CryptographicOperations.FixedTimeEquals(expectedHash, candidateHash);

            if (!isMatch)
            {
                return false;
            }

            if (metadata.VaultKeyProtectionMode == VaultKeyProtectionMode.PasswordAndKeyFile &&
                keyFileSecret is null)
            {
                return false;
            }

            effectiveWrappingKey = metadata.VaultKeyProtectionMode == VaultKeyProtectionMode.PasswordAndKeyFile
                ? DeriveCompositeWrappingKey(passwordWrappingKey, keyFileSecret!)
                : passwordWrappingKey;

            vaultKey = HasWrappedVaultKey(metadata)
                ? UnwrapVaultKey(metadata, effectiveWrappingKey)
                : DerivePurposeHash(baseKey, VaultPurpose);

            sessionStateService.SetVaultKey(vaultKey);

            if (settings.KeyFile is not null &&
                keyFileSecret is not null &&
                metadata.VaultKeyProtectionMode != VaultKeyProtectionMode.PasswordAndKeyFile)
            {
                byte[] compositeWrappingKey = DeriveCompositeWrappingKey(passwordWrappingKey, keyFileSecret);
                try
                {
                    migratedWrappedVaultKey = WrapVaultKey(vaultKey, compositeWrappingKey);
                    MasterPasswordMetadata migratedMetadata = CloneMetadata(metadata)!;
                    migratedMetadata.VaultKeyNonceBase64 = Convert.ToBase64String(migratedWrappedVaultKey.Nonce);
                    migratedMetadata.VaultKeyCiphertextBase64 = Convert.ToBase64String(migratedWrappedVaultKey.Ciphertext);
                    migratedMetadata.VaultKeyTagBase64 = Convert.ToBase64String(migratedWrappedVaultKey.Tag);
                    migratedMetadata.VaultKeyProtectionMode = VaultKeyProtectionMode.PasswordAndKeyFile;
                    settings.MasterPassword = migratedMetadata;
                    await settingsService.SaveAsync(settings, cancellationToken);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(compositeWrappingKey);
                }
            }

            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(expectedHash);
            CryptographicOperations.ZeroMemory(baseKey);
            CryptographicOperations.ZeroMemory(candidateHash);
            CryptographicOperations.ZeroMemory(passwordWrappingKey);
            if (effectiveWrappingKey is not null && !ReferenceEquals(effectiveWrappingKey, passwordWrappingKey))
            {
                CryptographicOperations.ZeroMemory(effectiveWrappingKey);
            }
            if (vaultKey is not null)
            {
                CryptographicOperations.ZeroMemory(vaultKey);
            }
            migratedWrappedVaultKey?.Clear();
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

    private static byte[] DeriveBaseKey(string password, MasterPasswordMetadata metadata, byte[] salt)
    {
        return metadata.Algorithm switch
        {
            KdfAlgorithm.Pbkdf2 => DeriveBaseKeyPbkdf2(password, salt, metadata.Iterations),
            KdfAlgorithm.Argon2id => DeriveBaseKeyArgon2id(
                password,
                salt,
                metadata.Iterations,
                metadata.MemorySizeKb,
                metadata.DegreeOfParallelism
            ),
            _ => throw new NotSupportedException($"KDF algorithm '{metadata.Algorithm}' is not supported.")
        };
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

    private static byte[] DeriveCompositeWrappingKey(byte[] passwordWrappingKey, byte[] keyFileSecret)
    {
        byte[] message = new byte[CompositeVaultWrappingPurpose.Length + keyFileSecret.Length];
        Buffer.BlockCopy(CompositeVaultWrappingPurpose, 0, message, 0, CompositeVaultWrappingPurpose.Length);
        Buffer.BlockCopy(keyFileSecret, 0, message, CompositeVaultWrappingPurpose.Length, keyFileSecret.Length);

        try
        {
            return HMACSHA256.HashData(passwordWrappingKey, message);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(message);
        }
    }

    private static MasterPasswordMetadata CreateArgon2idMetadata(
        byte[] salt,
        byte[] verificationHash,
        WrappedVaultKey wrappedVaultKey,
        VaultKeyProtectionMode protectionMode,
        Argon2Parameters argon2
    )
    {
        return new MasterPasswordMetadata
        {
            Algorithm = KdfAlgorithm.Argon2id,
            SaltBase64 = Convert.ToBase64String(salt),
            VerificationHashBase64 = Convert.ToBase64String(verificationHash),
            Iterations = argon2.Iterations,
            MemorySizeKb = argon2.MemoryKb,
            DegreeOfParallelism = argon2.Parallelism,
            VaultKeyNonceBase64 = Convert.ToBase64String(wrappedVaultKey.Nonce),
            VaultKeyCiphertextBase64 = Convert.ToBase64String(wrappedVaultKey.Ciphertext),
            VaultKeyTagBase64 = Convert.ToBase64String(wrappedVaultKey.Tag),
            VaultKeyProtectionMode = protectionMode,
        };
    }

    private static WrappedVaultKey WrapVaultKey(byte[] vaultKey, byte[] wrappingKey)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] ciphertext = new byte[vaultKey.Length];
        byte[] tag = new byte[16];

        using AesGcm aesGcm = new(wrappingKey, tag.Length);
        aesGcm.Encrypt(nonce, vaultKey, ciphertext, tag);

        return new WrappedVaultKey(nonce, ciphertext, tag);
    }

    private static byte[] UnwrapVaultKey(MasterPasswordMetadata metadata, byte[] wrappingKey)
    {
        byte[] nonce = Convert.FromBase64String(metadata.VaultKeyNonceBase64!);
        byte[] ciphertext = Convert.FromBase64String(metadata.VaultKeyCiphertextBase64!);
        byte[] tag = Convert.FromBase64String(metadata.VaultKeyTagBase64!);
        byte[] vaultKey = new byte[ciphertext.Length];

        try
        {
            using AesGcm aesGcm = new(wrappingKey, tag.Length);
            aesGcm.Decrypt(nonce, ciphertext, tag, vaultKey);

            if (vaultKey.Length != KeySize)
            {
                throw new CryptographicException("The stored vault key has an invalid length.");
            }

            return vaultKey;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(vaultKey);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    private static bool HasWrappedVaultKey(MasterPasswordMetadata metadata)
    {
        return !string.IsNullOrWhiteSpace(metadata.VaultKeyNonceBase64) &&
               !string.IsNullOrWhiteSpace(metadata.VaultKeyCiphertextBase64) &&
               !string.IsNullOrWhiteSpace(metadata.VaultKeyTagBase64);
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
            VaultKeyNonceBase64 = metadata.VaultKeyNonceBase64,
            VaultKeyCiphertextBase64 = metadata.VaultKeyCiphertextBase64,
            VaultKeyTagBase64 = metadata.VaultKeyTagBase64,
            VaultKeyProtectionMode = metadata.VaultKeyProtectionMode,
        };
    }

    private static KeyFileMetadata? CloneKeyFileMetadata(KeyFileMetadata? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        return new KeyFileMetadata
        {
            Version = metadata.Version,
            FileName = metadata.FileName,
            SaltBase64 = metadata.SaltBase64,
            VerificationHashBase64 = metadata.VerificationHashBase64,
            CreatedUtc = metadata.CreatedUtc,
        };
    }

    private sealed record WrappedVaultKey(byte[] Nonce, byte[] Ciphertext, byte[] Tag)
    {
        public void Clear()
        {
            CryptographicOperations.ZeroMemory(Nonce);
            CryptographicOperations.ZeroMemory(Ciphertext);
            CryptographicOperations.ZeroMemory(Tag);
        }
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
