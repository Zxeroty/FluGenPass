using FluGenPass.Models;
using FluGenPass.Services;
using System.Security.Cryptography;

namespace FluGenPass.Tests;

public sealed class Argon2idSecurityTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"FluGenPassArgonTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task MasterPasswordSetupArgon2id_WorksRoundTrip()
    {
        TestServices services = CreateServices();

        await services.MasterPassword.SetMasterPasswordAsync("SecurePassword123!");
        services.MasterPassword.Lock();

        bool unlocked = await services.MasterPassword.TryUnlockAsync("SecurePassword123!");
        AppSettings settings = await services.Settings.GetAsync();

        Assert.True(unlocked);
        Assert.Equal(KdfAlgorithm.Argon2id, settings.MasterPassword!.Algorithm);
        Assert.InRange(settings.MasterPassword.MemorySizeKb, 131_072, 1_048_576);
        Assert.InRange(settings.MasterPassword.DegreeOfParallelism, 1, 8);
    }

    [Fact]
    public async Task LegacyPbkdf2Unlock_StillWorks()
    {
        TestServices services = CreateServices();
        
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes("OldSchoolPassword");
        byte[] baseKey = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, 200000, HashAlgorithmName.SHA256, 32);
        
        byte[] verificationBuffer = new byte[baseKey.Length + 17];
        Buffer.BlockCopy(baseKey, 0, verificationBuffer, 0, 32);
        Buffer.BlockCopy(System.Text.Encoding.UTF8.GetBytes("FluGenPass.Verify"), 0, verificationBuffer, 32, 17);
        byte[] verificationHash = SHA256.HashData(verificationBuffer);

        AppSettings settings = await services.Settings.GetAsync();
        settings.MasterPassword = new MasterPasswordMetadata
        {
            Algorithm = KdfAlgorithm.Pbkdf2,
            SaltBase64 = Convert.ToBase64String(salt),
            VerificationHashBase64 = Convert.ToBase64String(verificationHash),
            Iterations = 200000
        };
        await services.Settings.SaveAsync(settings);

        bool unlocked = await services.MasterPassword.TryUnlockAsync("OldSchoolPassword");

        Assert.True(unlocked);
        Assert.True(services.Session.IsUnlocked);
    }

    [Fact]
    public async Task ChangePassword_UpgradesToArgon2id()
    {
        TestServices services = CreateServices();
        
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes("OldPassword");
        byte[] baseKey = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, 1000, HashAlgorithmName.SHA256, 32);
        byte[] verificationBuffer = new byte[32 + 17];
        Buffer.BlockCopy(baseKey, 0, verificationBuffer, 0, 32);
        Buffer.BlockCopy(System.Text.Encoding.UTF8.GetBytes("FluGenPass.Verify"), 0, verificationBuffer, 32, 17);

        AppSettings settings = await services.Settings.GetAsync();
        settings.MasterPassword = new MasterPasswordMetadata
        {
            Algorithm = KdfAlgorithm.Pbkdf2,
            SaltBase64 = Convert.ToBase64String(salt),
            VerificationHashBase64 = Convert.ToBase64String(SHA256.HashData(verificationBuffer)),
            Iterations = 1000
        };
        await services.Settings.SaveAsync(settings);

        await services.MasterPassword.TryUnlockAsync("OldPassword");
        
        await services.MasterPassword.ChangeMasterPasswordAsync("NewArgonPassword");
        
        AppSettings updatedSettings = await services.Settings.GetAsync();
        Assert.Equal(KdfAlgorithm.Argon2id, updatedSettings.MasterPassword!.Algorithm);
        
        services.MasterPassword.Lock();
        bool unlockedNew = await services.MasterPassword.TryUnlockAsync("NewArgonPassword");
        Assert.True(unlockedNew);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    private TestServices CreateServices()
    {
        Directory.CreateDirectory(_tempDirectory);
        SettingsService settings = new(_tempDirectory);
        SessionStateService session = new();
        VaultService vault = new(_tempDirectory, session);
        MasterPasswordService masterPassword = new(settings, session, vault);
        return new TestServices(settings, session, masterPassword, vault);
    }

    private sealed record TestServices(
        SettingsService Settings,
        SessionStateService Session,
        MasterPasswordService MasterPassword,
        VaultService Vault
    );
}
