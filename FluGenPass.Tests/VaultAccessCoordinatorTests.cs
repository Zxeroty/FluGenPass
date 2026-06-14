using System.Windows;
using System.Security.Cryptography;
using FluGenPass.Models;
using FluGenPass.Services;
using Wpf.Ui.Controls;

namespace FluGenPass.Tests;

public sealed class VaultAccessCoordinatorTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"FluGenPassAccessTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task EnsureAccessAsync_WithKeyFileEnabled_RequiresPasswordAndKeyFile()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());

        string keyFilePath = Path.Combine(_tempDirectory, "vault.fgpkey");
        await CreateAndEnableKeyFileAsync(services, keyFilePath);
        services.MasterPassword.Lock();

        VaultAccessCoordinator coordinator = new(
            new TestDialogService("CorrectHorseBatteryStaple!", keyFilePath),
            services.MasterPassword,
            services.KeyFile,
            new TestNotificationService(),
            services.Settings
        );

        bool accessGranted = await coordinator.EnsureAccessAsync();

        Assert.True(accessGranted);
        Assert.True(services.Session.IsUnlocked);
    }

    [Fact]
    public async Task EnsureAccessAsync_WithKeyFileEnabled_LocksWhenKeyFileIsCancelled()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());

        string keyFilePath = Path.Combine(_tempDirectory, "vault.fgpkey");
        await CreateAndEnableKeyFileAsync(services, keyFilePath);
        services.MasterPassword.Lock();

        VaultAccessCoordinator coordinator = new(
            new TestDialogService("CorrectHorseBatteryStaple!", keyFilePath: null),
            services.MasterPassword,
            services.KeyFile,
            new TestNotificationService(),
            services.Settings
        );

        bool accessGranted = await coordinator.EnsureAccessAsync();

        Assert.False(accessGranted);
        Assert.False(services.Session.IsUnlocked);
    }

    [Fact]
    public async Task EnsureAccessAsync_WithKeyFileEnabled_LocksWhenKeyFileIsWrong()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!".ToCharArray());

        string keyFilePath = Path.Combine(_tempDirectory, "vault.fgpkey");
        string wrongKeyFilePath = Path.Combine(_tempDirectory, "wrong.fgpkey");
        await CreateAndEnableKeyFileAsync(services, keyFilePath);
        await File.WriteAllTextAsync(wrongKeyFilePath, "{}");
        services.MasterPassword.Lock();

        VaultAccessCoordinator coordinator = new(
            new TestDialogService("CorrectHorseBatteryStaple!", wrongKeyFilePath),
            services.MasterPassword,
            services.KeyFile,
            new TestNotificationService(),
            services.Settings
        );

        bool accessGranted = await coordinator.EnsureAccessAsync();

        Assert.False(accessGranted);
        Assert.False(services.Session.IsUnlocked);
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

    private sealed record TestServices(
        SettingsService Settings,
        SessionStateService Session,
        MasterPasswordService MasterPassword,
        KeyFileService KeyFile,
        VaultService Vault
    );

    private static async Task CreateAndEnableKeyFileAsync(TestServices services, string keyFilePath)
    {
        KeyFileCreationResult result = await services.KeyFile.CreateKeyFileAsync(keyFilePath);
        try
        {
            await services.MasterPassword.EnableKeyFileAsync(
                "CorrectHorseBatteryStaple!".ToCharArray(),
                result.Metadata,
                result.Secret
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(result.Secret);
        }
    }

    private sealed class TestDialogService(string? password, string? keyFilePath) : IDialogService
    {
        public void Initialize(ContentDialogHost dialogHost, Window ownerWindow)
        {
        }

        public Task<(string SiteName, string Url, char[] Password)?> PromptForSiteDetailsAsync(string initialSiteName = "", string initialUrl = "", string initialPassword = "", CancellationToken cancellationToken = default)
        {
            return Task.FromResult<(string SiteName, string Url, char[] Password)?>(null);
        }

        public Task<string?> PromptForTagsAsync(string initialValue = "", CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<char[]?> PromptForNewMasterPasswordAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(password?.ToCharArray());
        }

        public Task<char[]?> PromptForUnlockPasswordAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(password?.ToCharArray());
        }

        public string? PromptForSaveKeyFilePath()
        {
            return null;
        }

        public string? PromptForOpenKeyFilePath()
        {
            return keyFilePath;
        }

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string primaryButtonText = "Confirm",
            string closeButtonText = "Cancel",
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(false);
        }

        public Task ShowMessageAsync(
            string title,
            string message,
            string closeButtonText = "Close",
            CancellationToken cancellationToken = default
        )
        {
            return Task.CompletedTask;
        }

        public Task<bool> ShowKeyFileWarningAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class TestNotificationService : INotificationService
    {
        public void Initialize(SnackbarPresenter presenter)
        {
        }

        public void ShowInfo(string title, string message)
        {
        }

        public void ShowSuccess(string title, string message)
        {
        }

        public void ShowError(string title, string message)
        {
        }
    }
}
