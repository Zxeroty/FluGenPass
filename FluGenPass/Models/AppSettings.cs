using System.Text.Json.Serialization;

namespace FluGenPass.Models;

public sealed record AppSettings
{
    public AppThemeOption Theme { get; set; } = AppThemeOption.System;
    public AppLanguageOption Language { get; set; } = AppLanguageOption.English;

    public MasterPasswordMetadata? MasterPassword { get; set; }

    public KeyFileMetadata? KeyFile { get; set; }

    public int AutoLockTimeoutMinutes { get; set; }

    public bool AutoLockEnabled { get; set; } = true;

    public int AuthFailedAttempts { get; set; }
    public DateTimeOffset? AuthLockoutUntilUtc { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppLanguageOption
{
    English,
    Russian,
    Polish,
    German,
    Ukrainian
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

public sealed record MasterPasswordMetadata
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

public sealed record KeyFileMetadata
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
