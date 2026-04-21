using FluGenPass.Models;
using FluGenPass.Services;

namespace FluGenPass.Tests;

public sealed class VaultSecurityTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"FluGenPassTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task MasterPasswordSetupAndVerification_WorksAcrossLockCycle()
    {
        TestServices services = CreateServices();

        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!");
        services.MasterPassword.Lock();

        bool unlocked = await services.MasterPassword.TryUnlockAsync("CorrectHorseBatteryStaple!");

        Assert.True(await services.MasterPassword.HasMasterPasswordAsync());
        Assert.True(unlocked);
        Assert.True(services.Session.IsUnlocked);
    }

    [Fact]
    public async Task VaultEncryption_RoundTripsEntriesThroughAesGcm()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!");

        VaultEntry expectedEntry = new()
        {
            SiteName = "example.com",
            Password = "P@ssw0rd!",
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
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!");

        VaultEntry firstEntry = new()
        {
            SiteName = "first.example",
            Password = "FirstSecret!1",
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        VaultEntry secondEntry = new()
        {
            SiteName = "second.example",
            Password = "SecondSecret!2",
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
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!");

        VaultEntry entry = new()
        {
            SiteName = "temp-cleanup.example",
            Password = "CleanupSecret!3",
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await services.Vault.SaveAsync([entry]);

        Assert.False(File.Exists(GetTempVaultFilePath(services.Vault)));
    }

    [Fact]
    public async Task SaveAsync_DeletesStaleTemporaryFileBeforeReplacingVault()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!");

        await File.WriteAllTextAsync(GetTempVaultFilePath(services.Vault), "stale temp data");

        VaultEntry entry = new()
        {
            SiteName = "stale-temp.example",
            Password = "FreshSecret!4",
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
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!");

        VaultEntry existingEntry = new()
        {
            SiteName = "existing.example",
            Password = "ExistingSecret!5",
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        VaultEntry replacementEntry = new()
        {
            SiteName = "replacement.example",
            Password = "ReplacementSecret!6",
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

        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!");
        services.MasterPassword.Lock();

        bool unlocked = await services.MasterPassword.TryUnlockAsync("WrongPassword!");

        Assert.False(unlocked);
        Assert.False(services.Session.IsUnlocked);
    }

    [Fact]
    public async Task VaultEntries_PersistAcrossServiceInstances()
    {
        TestServices firstInstance = CreateServices();
        await firstInstance.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!");

        VaultEntry entry = new()
        {
            SiteName = "contoso",
            Password = "S3cret!Value",
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await firstInstance.Vault.SaveAsync([entry]);
        firstInstance.MasterPassword.Lock();

        TestServices secondInstance = CreateServices();
        bool unlocked = await secondInstance.MasterPassword.TryUnlockAsync("CorrectHorseBatteryStaple!");
        IReadOnlyList<VaultEntry> entries = await secondInstance.Vault.LoadAsync();

        Assert.True(unlocked);
        VaultEntry restored = Assert.Single(entries);
        Assert.Equal(entry.SiteName, restored.SiteName);
        Assert.Equal(entry.Password, restored.Password);
    }

    [Fact]
    public async Task ChangeMasterPasswordAsync_ReencryptsExistingEntries()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!");

        VaultEntry entry = new()
        {
            SiteName = "vault.example",
            Password = "OriginalSecret!42",
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await services.Vault.SaveAsync([entry]);
        await services.MasterPassword.ChangeMasterPasswordAsync("NewMasterPassword!123");
        services.MasterPassword.Lock();

        bool unlockedWithOldPassword = await services.MasterPassword.TryUnlockAsync("CorrectHorseBatteryStaple!");
        services.MasterPassword.Lock();
        bool unlockedWithNewPassword = await services.MasterPassword.TryUnlockAsync("NewMasterPassword!123");
        IReadOnlyList<VaultEntry> entries = await services.Vault.LoadAsync();

        Assert.False(unlockedWithOldPassword);
        Assert.True(unlockedWithNewPassword);
        VaultEntry restored = Assert.Single(entries);
        Assert.Equal(entry.SiteName, restored.SiteName);
        Assert.Equal(entry.Password, restored.Password);
    }

    [Fact]
    public async Task ResetAsync_RemovesMasterPasswordAndClearsVault()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!");

        VaultEntry entry = new()
        {
            SiteName = "reset.example",
            Password = "ToBeDeleted!7",
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await services.Vault.SaveAsync([entry]);
        await services.MasterPassword.ResetAsync();

        Assert.False(await services.MasterPassword.HasMasterPasswordAsync());
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

        return new TestServices(settings, session, masterPassword, vault);
    }

    private static string GetTempVaultFilePath(VaultService vault)
    {
        return $"{vault.VaultFilePath}.tmp";
    }

    private sealed record TestServices(
        SettingsService Settings,
        SessionStateService Session,
        MasterPasswordService MasterPassword,
        VaultService Vault
    );
}
