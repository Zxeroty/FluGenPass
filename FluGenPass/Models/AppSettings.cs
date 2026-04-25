using System.Text.Json.Serialization;

namespace FluGenPass.Models;

public sealed class AppSettings
{
    public AppThemeOption Theme { get; set; } = AppThemeOption.System;
    public AppLanguageOption Language { get; set; } = AppLanguageOption.English;

    public MasterPasswordMetadata? MasterPassword { get; set; }

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

public sealed class MasterPasswordMetadata
{
    public KdfAlgorithm Algorithm { get; set; } = KdfAlgorithm.Pbkdf2;

    public string SaltBase64 { get; set; } = string.Empty;

    public string VerificationHashBase64 { get; set; } = string.Empty;

    public int Iterations { get; set; } = 200000;

    public int MemorySizeKb { get; set; }
    public int DegreeOfParallelism { get; set; }
}
