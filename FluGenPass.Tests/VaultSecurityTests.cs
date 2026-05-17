using FluGenPass.Models;
using FluGenPass.Services;
using System.Security.Cryptography;

namespace FluGenPass.Tests;

public sealed class VaultSecurityTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"FluGenPassTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task MasterPasswordSetupAndVerification_WorksAcrossLockCycle()
    {
        TestServices services = CreateServices();

        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());
        services.MasterPassword.Lock();

        bool unlocked = await services.MasterPassword.TryUnlockAsync("CorrectHorseBatteryStaple!".ToCharArray());

        Assert.True(await services.MasterPassword.HasMasterPasswordAsync());
        Assert.True(unlocked);
        Assert.True(services.Session.IsUnlocked);
    }

    [Fact]
    public async Task VaultEncryption_RoundTripsEntriesThroughAesGcm()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());

        VaultEntry expectedEntry = new()
        {
            SiteName = "example.com",
            Password = "P@ssw0rd!".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await services.Vault.SaveAsync([expectedEntry]);
        IReadOnlyList<VaultEntry> actualEntries = await services.Vault.LoadAsync();

        VaultEntry actual = Assert.Single(actualEntries);
        Assert.Equal(expectedEntry.SiteName, actual.SiteName);
        Assert.Equal(expectedEntry.Password, actual.Password);
    }

    [Fact]
    public async Task SaveAsync_ReplacesExistingVaultContents()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());

        VaultEntry firstEntry = new()
        {
            SiteName = "first.example",
            Password = "FirstSecret!1".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        VaultEntry secondEntry = new()
        {
            SiteName = "second.example",
            Password = "SecondSecret!2".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await services.Vault.SaveAsync([firstEntry]);
        await services.Vault.SaveAsync([secondEntry]);

        IReadOnlyList<VaultEntry> actualEntries = await services.Vault.LoadAsync();

        VaultEntry actual = Assert.Single(actualEntries);
        Assert.Equal(secondEntry.SiteName, actual.SiteName);
        Assert.Equal(secondEntry.Password, actual.Password);
    }

    [Fact]
    public async Task SaveAsync_RemovesTemporaryFileAfterSuccessfulWrite()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());

        VaultEntry entry = new()
        {
            SiteName = "temp-cleanup.example",
            Password = "CleanupSecret!3".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await services.Vault.SaveAsync([entry]);

        Assert.False(File.Exists(GetTempVaultFilePath(services.Vault)));
    }

    [Fact]
    public async Task SaveAsync_DeletesStaleTemporaryFileBeforeReplacingVault()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());

        await File.WriteAllTextAsync(GetTempVaultFilePath(services.Vault), "stale temp data");

        VaultEntry entry = new()
        {
            SiteName = "stale-temp.example",
            Password = "FreshSecret!4".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await services.Vault.SaveAsync([entry]);
        IReadOnlyList<VaultEntry> actualEntries = await services.Vault.LoadAsync();

        VaultEntry actual = Assert.Single(actualEntries);
        Assert.Equal(entry.SiteName, actual.SiteName);
        Assert.Equal(entry.Password, actual.Password);
        Assert.False(File.Exists(GetTempVaultFilePath(services.Vault)));
    }

    [Fact]
    public async Task SaveAsync_KeepsExistingVaultWhenReplaceFails()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());

        VaultEntry existingEntry = new()
        {
            SiteName = "existing.example",
            Password = "ExistingSecret!5".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        VaultEntry replacementEntry = new()
        {
            SiteName = "replacement.example",
            Password = "ReplacementSecret!6".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await services.Vault.SaveAsync([existingEntry]);

        using (FileStream lockStream = new(services.Vault.VaultFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Exception? exception = await Record.ExceptionAsync(() => services.Vault.SaveAsync([replacementEntry]));

            Assert.NotNull(exception);
            Assert.True(exception is IOException or UnauthorizedAccessException);
        }

        IReadOnlyList<VaultEntry> actualEntries = await services.Vault.LoadAsync();

        VaultEntry actual = Assert.Single(actualEntries);
        Assert.Equal(existingEntry.SiteName, actual.SiteName);
        Assert.Equal(existingEntry.Password, actual.Password);
        Assert.False(File.Exists(GetTempVaultFilePath(services.Vault)));
    }

    [Fact]
    public async Task MasterPasswordVerification_FailsForWrongPassword()
    {
        TestServices services = CreateServices();

        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());
        services.MasterPassword.Lock();

        bool unlocked = await services.MasterPassword.TryUnlockAsync("WrongPassword!".ToCharArray());

        Assert.False(unlocked);
        Assert.False(services.Session.IsUnlocked);
    }

    [Fact]
    public async Task VaultEntries_PersistAcrossServiceInstances()
    {
        TestServices firstInstance = CreateServices();
        await firstInstance.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());

        VaultEntry entry = new()
        {
            SiteName = "contoso",
            Password = "S3cret!Value".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await firstInstance.Vault.SaveAsync([entry]);
        firstInstance.MasterPassword.Lock();

        TestServices secondInstance = CreateServices();
        bool unlocked = await secondInstance.MasterPassword.TryUnlockAsync("CorrectHorseBatteryStaple!".ToCharArray());
        IReadOnlyList<VaultEntry> entries = await secondInstance.Vault.LoadAsync();

        Assert.True(unlocked);
        VaultEntry restored = Assert.Single(entries);
        Assert.Equal(entry.SiteName, restored.SiteName);
        Assert.Equal(entry.Password, restored.Password);
    }

    [Fact]
    public async Task ChangeMasterPasswordAsync_PreservesExistingEntries()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());

        VaultEntry entry = new()
        {
            SiteName = "vault.example",
            Password = "OriginalSecret!42".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await services.Vault.SaveAsync([entry]);
        await services.MasterPassword.ChangeMasterPasswordAsync("NewMasterPassword!123".ToCharArray());
        services.MasterPassword.Lock();

        bool unlockedWithOldPassword = await services.MasterPassword.TryUnlockAsync("CorrectHorseBatteryStaple!".ToCharArray());
        services.MasterPassword.Lock();
        bool unlockedWithNewPassword = await services.MasterPassword.TryUnlockAsync("NewMasterPassword!123".ToCharArray());
        IReadOnlyList<VaultEntry> entries = await services.Vault.LoadAsync();

        Assert.False(unlockedWithOldPassword);
        Assert.True(unlockedWithNewPassword);
        VaultEntry restored = Assert.Single(entries);
        Assert.Equal(entry.SiteName, restored.SiteName);
        Assert.Equal(entry.Password, restored.Password);
    }

    [Fact]
    public async Task KeyFileSetupAndVerification_WorksAfterLockCycle()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());

        VaultEntry entry = new()
        {
            SiteName = "keyfile.example",
            Password = "KeyFileSecret!8".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await services.Vault.SaveAsync([entry]);

        string keyFilePath = Path.Combine(_tempDirectory, "vault.fgpkey");
        await CreateAndEnableKeyFileAsync(services, keyFilePath);
        services.MasterPassword.Lock();

        byte[]? keyFileSecret = await services.KeyFile.GetAndVerifySecretAsync(keyFilePath);

        Assert.True(await services.KeyFile.IsEnabledAsync());
        Assert.NotNull(keyFileSecret);
        Assert.False(services.Session.IsUnlocked);
        ZeroIfPresent(keyFileSecret);
    }

    [Fact]
    public async Task KeyFileVerification_FailsForWrongFile()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());

        string keyFilePath = Path.Combine(_tempDirectory, "vault.fgpkey");
        string wrongKeyFilePath = Path.Combine(_tempDirectory, "wrong.fgpkey");

        await CreateAndEnableKeyFileAsync(services, keyFilePath);
        await File.WriteAllTextAsync(wrongKeyFilePath, "{}");
        services.MasterPassword.Lock();

        byte[]? keyFileSecret = await services.KeyFile.GetAndVerifySecretAsync(wrongKeyFilePath);

        Assert.Null(keyFileSecret);
        Assert.False(services.Session.IsUnlocked);
    }

    [Fact]
    public async Task ChangeMasterPasswordAsync_KeepsExistingKeyFileValid()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());

        VaultEntry entry = new()
        {
            SiteName = "stable-keyfile.example",
            Password = "StillWorks!9".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await services.Vault.SaveAsync([entry]);

        string keyFilePath = Path.Combine(_tempDirectory, "stable.fgpkey");
        await CreateAndEnableKeyFileAsync(services, keyFilePath);

        byte[] keyFileSecret = (await services.KeyFile.GetAndVerifySecretAsync(keyFilePath))!;
        await services.MasterPassword.ChangeMasterPasswordAsync("NewMasterPassword!123".ToCharArray(), keyFileSecret);
        services.MasterPassword.Lock();

        bool passwordOnlyUnlocked = await services.MasterPassword.TryUnlockAsync("NewMasterPassword!123".ToCharArray());
        byte[] restoredKeyFileSecret = (await services.KeyFile.GetAndVerifySecretAsync(keyFilePath))!;
        bool passwordAndKeyFileUnlocked = await services.MasterPassword.TryUnlockAsync(
            "NewMasterPassword!123".ToCharArray(),
            restoredKeyFileSecret
        );
        IReadOnlyList<VaultEntry> entries = await services.Vault.LoadAsync();

        Assert.False(passwordOnlyUnlocked);
        Assert.True(passwordAndKeyFileUnlocked);
        VaultEntry restored = Assert.Single(entries);
        Assert.Equal(entry.Password, restored.Password);
        ZeroIfPresent(keyFileSecret);
        ZeroIfPresent(restoredKeyFileSecret);
    }

    [Fact]
    public async Task KeyFileProtectedVault_FailsWithCorrectPasswordOnly()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());

        string keyFilePath = Path.Combine(_tempDirectory, "required.fgpkey");
        await CreateAndEnableKeyFileAsync(services, keyFilePath);
        services.MasterPassword.Lock();

        bool unlocked = await services.MasterPassword.TryUnlockAsync("CorrectHorseBatteryStaple!".ToCharArray());

        Assert.False(unlocked);
        Assert.False(services.Session.IsUnlocked);
    }

    [Fact]
    public async Task LegacyKeyFileSettings_MigrateToCompositeWrappingAfterTwoStepUnlock()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());

        string keyFilePath = Path.Combine(_tempDirectory, "legacy.fgpkey");
        KeyFileCreationResult result = await services.KeyFile.CreateKeyFileAsync(keyFilePath);
        try
        {
            AppSettings settings = await services.Settings.GetAsync();
            settings.KeyFile = result.Metadata;
            await services.Settings.SaveAsync(settings);
        }
        finally
        {
            ZeroIfPresent(result.Secret);
        }

        services.MasterPassword.Lock();
        byte[] keyFileSecret = (await services.KeyFile.GetAndVerifySecretAsync(keyFilePath))!;
        bool unlocked = await services.MasterPassword.TryUnlockAsync("CorrectHorseBatteryStaple!".ToCharArray(), keyFileSecret);
        AppSettings migratedSettings = await services.Settings.GetAsync();
        services.MasterPassword.Lock();

        bool passwordOnlyUnlocked = await services.MasterPassword.TryUnlockAsync("CorrectHorseBatteryStaple!".ToCharArray());
        bool passwordAndKeyFileUnlocked = await services.MasterPassword.TryUnlockAsync(
            "CorrectHorseBatteryStaple!".ToCharArray(),
            keyFileSecret
        );

        Assert.True(unlocked);
        Assert.Equal(VaultKeyProtectionMode.PasswordAndKeyFile, migratedSettings.MasterPassword!.VaultKeyProtectionMode);
        Assert.False(passwordOnlyUnlocked);
        Assert.True(passwordAndKeyFileUnlocked);
        ZeroIfPresent(keyFileSecret);
    }

    [Fact]
    public async Task ReplacingKeyFile_InvalidatesOldKeyFileAndPreservesEntries()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());

        VaultEntry entry = new()
        {
            SiteName = "replace-keyfile.example",
            Password = "ReplacementKeepsMe!10".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };
        await services.Vault.SaveAsync([entry]);

        string oldKeyFilePath = Path.Combine(_tempDirectory, "old.fgpkey");
        string newKeyFilePath = Path.Combine(_tempDirectory, "new.fgpkey");
        await CreateAndEnableKeyFileAsync(services, oldKeyFilePath);

        byte[] oldSecret = (await services.KeyFile.GetAndVerifySecretAsync(oldKeyFilePath))!;
        KeyFileCreationResult newKeyFile = await services.KeyFile.CreateKeyFileAsync(newKeyFilePath);
        try
        {
            await services.MasterPassword.EnableKeyFileAsync(
                "CorrectHorseBatteryStaple!".ToCharArray(),
                newKeyFile.Metadata,
                newKeyFile.Secret
            );
        }
        finally
        {
            ZeroIfPresent(newKeyFile.Secret);
        }

        services.MasterPassword.Lock();
        bool oldSecretUnlocked = await services.MasterPassword.TryUnlockAsync("CorrectHorseBatteryStaple!".ToCharArray(), oldSecret);
        byte[] newSecret = (await services.KeyFile.GetAndVerifySecretAsync(newKeyFilePath))!;
        bool newSecretUnlocked = await services.MasterPassword.TryUnlockAsync("CorrectHorseBatteryStaple!".ToCharArray(), newSecret);
        IReadOnlyList<VaultEntry> entries = await services.Vault.LoadAsync();

        Assert.False(oldSecretUnlocked);
        Assert.True(newSecretUnlocked);
        VaultEntry restored = Assert.Single(entries);
        Assert.Equal(entry.Password, restored.Password);
        Assert.Null(await services.KeyFile.GetAndVerifySecretAsync(oldKeyFilePath));
        ZeroIfPresent(oldSecret);
        ZeroIfPresent(newSecret);
    }

    [Fact]
    public async Task ResetAsync_RemovesMasterPasswordAndClearsVault()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());

        VaultEntry entry = new()
        {
            SiteName = "reset.example",
            Password = "ToBeDeleted!7".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await services.Vault.SaveAsync([entry]);
        await services.MasterPassword.ResetAsync();

        Assert.False(await services.MasterPassword.HasMasterPasswordAsync());
        Assert.False(await services.KeyFile.IsEnabledAsync());
        Assert.False(services.Session.IsUnlocked);
        Assert.False(File.Exists(services.Vault.VaultFilePath));
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
        KeyFileService keyFile = new(settings, session);

        return new TestServices(settings, session, masterPassword, keyFile, vault);
    }

    private static string GetTempVaultFilePath(VaultService vault)
    {
        return $"{vault.VaultFilePath}.tmp";
    }

    private sealed record TestServices(
        SettingsService Settings,
        SessionStateService Session,
        MasterPasswordService MasterPassword,
        KeyFileService KeyFile,
        VaultService Vault
    );

    private static async Task CreateAndEnableKeyFileAsync(
        TestServices services,
        string keyFilePath,
        string password = "CorrectHorseBatteryStaple!"
    )
    {
        KeyFileCreationResult result = await services.KeyFile.CreateKeyFileAsync(keyFilePath);
        try
        {
            await services.MasterPassword.EnableKeyFileAsync(password.ToCharArray(), result.Metadata, result.Secret);
        }
        finally
        {
            ZeroIfPresent(result.Secret);
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
