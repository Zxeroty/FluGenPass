using System.Text.Json.Serialization;

namespace FluGenPass.Models;

public sealed class AppSettings
{
    public AppThemeOption Theme { get; set; } = AppThemeOption.System;
    public AppLanguageOption Language { get; set; } = AppLanguageOption.English;

    public MasterPasswordMetadata? MasterPassword { get; set; }

    public KeyFileMetadata? KeyFile { get; set; }

    public int AutoLockTimeoutMinutes { get; set; }

    public bool AutoLockEnabled { get; set; } = true;

    public AppSettings Clone()
    {
        return new AppSettings
        {
            Theme = Theme,
            MasterPassword = MasterPassword is null
                ? null
                : new MasterPasswordMetadata
                {
                    Algorithm = MasterPassword.Algorithm,
                    SaltBase64 = MasterPassword.SaltBase64,
                    VerificationHashBase64 = MasterPassword.VerificationHashBase64,
                    Iterations = MasterPassword.Iterations,
                    MemorySizeKb = MasterPassword.MemorySizeKb,
                    DegreeOfParallelism = MasterPassword.DegreeOfParallelism,
                    VaultKeyNonceBase64 = MasterPassword.VaultKeyNonceBase64,
                    VaultKeyCiphertextBase64 = MasterPassword.VaultKeyCiphertextBase64,
                    VaultKeyTagBase64 = MasterPassword.VaultKeyTagBase64,
                    VaultKeyProtectionMode = MasterPassword.VaultKeyProtectionMode,
                },
            KeyFile = KeyFile is null
                ? null
                : new KeyFileMetadata
                {
                    Version = KeyFile.Version,
                    FileName = KeyFile.FileName,
                    SaltBase64 = KeyFile.SaltBase64,
                    VerificationHashBase64 = KeyFile.VerificationHashBase64,
                    CreatedUtc = KeyFile.CreatedUtc,
                },
            AutoLockTimeoutMinutes = AutoLockTimeoutMinutes,
            AutoLockEnabled = AutoLockEnabled,
            Language = Language
        };
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppLanguageOption
{
    English,
    Russian
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KdfAlgorithm
{
    Pbkdf2,
    Argon2id
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VaultKeyProtectionMode
{
    PasswordOnly,
    PasswordAndKeyFile
}

public sealed class MasterPasswordMetadata
{
    public KdfAlgorithm Algorithm { get; set; } = KdfAlgorithm.Pbkdf2;

    public string SaltBase64 { get; set; } = string.Empty;

    public string VerificationHashBase64 { get; set; } = string.Empty;

    public int Iterations { get; set; } = 200000;

    public int MemorySizeKb { get; set; }
    public int DegreeOfParallelism { get; set; }

    public string? VaultKeyNonceBase64 { get; set; }
    public string? VaultKeyCiphertextBase64 { get; set; }
    public string? VaultKeyTagBase64 { get; set; }

    public VaultKeyProtectionMode VaultKeyProtectionMode { get; set; } = VaultKeyProtectionMode.PasswordOnly;
}

public sealed class KeyFileMetadata
{
    public int Version { get; set; } = 1;

    public string FileName { get; set; } = string.Empty;

    public string SaltBase64 { get; set; } = string.Empty;

    public string VerificationHashBase64 { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class KeyFileDocument
{
    public string Format { get; set; } = "FluGenPass Key File";

    public int Version { get; set; } = 1;

    public string KeyBase64 { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record KeyFileCreationResult(KeyFileMetadata Metadata, byte[] Secret);
