using System.Windows;
using System.Security.Cryptography;
using FluGenPass.Models;
using FluGenPass.Services;
using FluGenPass.ViewModels;
using Wpf.Ui.Controls;

namespace FluGenPass.Tests;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"FluGenPassSettingsTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task EnablingKeyFile_RewrapsVaultAndKeepsRequirementEnabled()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!");

        string keyFilePath = Path.Combine(_tempDirectory, "created.fgpkey");
        SettingsViewModel viewModel = CreateViewModel(
            services,
            new TestDialogService(
                confirmResult: true,
                password: "CorrectHorseBatteryStaple!",
                keyFilePath: null,
                saveKeyFilePath: keyFilePath
            )
        );
        await viewModel.InitializeAsync();

        viewModel.IsKeyFileEnabled = true;
        bool enabled = await WaitForConditionAsync(async () => await services.KeyFile.IsEnabledAsync());

        Assert.True(enabled);
        Assert.True(viewModel.IsKeyFileEnabled);
        Assert.False(await services.MasterPassword.TryUnlockAsync("CorrectHorseBatteryStaple!"));
    }

    [Fact]
    public async Task DisablingKeyFile_RequiresPasswordAndCurrentKeyFile()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!");

        string keyFilePath = Path.Combine(_tempDirectory, "vault.fgpkey");
        await CreateAndEnableKeyFileAsync(services, keyFilePath);

        SettingsViewModel viewModel = CreateViewModel(
            services,
            new TestDialogService(confirmResult: true, password: "CorrectHorseBatteryStaple!", keyFilePath)
        );
        await viewModel.InitializeAsync();

        viewModel.IsKeyFileEnabled = false;
        bool disabled = await WaitForConditionAsync(async () => !await services.KeyFile.IsEnabledAsync());

        Assert.True(disabled);
        Assert.False(await services.KeyFile.IsEnabledAsync());
    }

    [Fact]
    public async Task DisablingKeyFile_WithWrongKeyFile_KeepsRequirementEnabled()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!");

        string keyFilePath = Path.Combine(_tempDirectory, "vault.fgpkey");
        string wrongKeyFilePath = Path.Combine(_tempDirectory, "wrong.fgpkey");
        await CreateAndEnableKeyFileAsync(services, keyFilePath);
        await File.WriteAllTextAsync(wrongKeyFilePath, "{}");

        SettingsViewModel viewModel = CreateViewModel(
            services,
            new TestDialogService(confirmResult: true, password: "CorrectHorseBatteryStaple!", wrongKeyFilePath)
        );
        await viewModel.InitializeAsync();

        viewModel.IsKeyFileEnabled = false;
        await Task.Delay(200);

        Assert.True(await services.KeyFile.IsEnabledAsync());
        Assert.True(viewModel.IsKeyFileEnabled);
    }

    [Fact]
    public async Task DisablingKeyFile_WithWrongPassword_KeepsRequirementEnabled()
    {
        TestServices services = CreateServices();
        await services.MasterPassword.SetMasterPasswordAsync("CorrectHorseBatteryStaple!");

        string keyFilePath = Path.Combine(_tempDirectory, "vault.fgpkey");
        await CreateAndEnableKeyFileAsync(services, keyFilePath);

        SettingsViewModel viewModel = CreateViewModel(
            services,
            new TestDialogService(confirmResult: true, password: "WrongPassword!", keyFilePath)
        );
        await viewModel.InitializeAsync();

        viewModel.IsKeyFileEnabled = false;
        await Task.Delay(200);

        Assert.True(await services.KeyFile.IsEnabledAsync());
        Assert.True(viewModel.IsKeyFileEnabled);
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

    private static SettingsViewModel CreateViewModel(TestServices services, IDialogService dialogService)
    {
        return new SettingsViewModel(
            dialogService,
            new TestNotificationService(),
            new TestVaultAccessCoordinator(),
            new TestThemeService(),
            services.Vault,
            services.Settings,
            services.MasterPassword,
            services.KeyFile,
            services.Session,
            new TestAutoLockService(),
            new TestLocalizationService()
        );
    }

    private static async Task<bool> WaitForConditionAsync(Func<Task<bool>> condition)
    {
        for (int i = 0; i < 120; i++)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(250);
        }

        return false;
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
                "CorrectHorseBatteryStaple!",
                result.Metadata,
                result.Secret
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(result.Secret);
        }
    }

    private sealed class TestDialogService(
        bool confirmResult,
        string? password,
        string? keyFilePath,
        string? saveKeyFilePath = null
    ) : IDialogService
    {
        public void Initialize(ContentDialogHost dialogHost, Window ownerWindow)
        {
        }

        public Task<string?> PromptForSiteNameAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PromptForTagsAsync(string initialValue = "", CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PromptForNewMasterPasswordAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(password);
        }

        public Task<string?> PromptForUnlockPasswordAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(password);
        }

        public string? PromptForSaveKeyFilePath()
        {
            return saveKeyFilePath;
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
            return Task.FromResult(confirmResult);
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
            return Task.FromResult(confirmResult);
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

    private sealed class TestVaultAccessCoordinator : IVaultAccessCoordinator
    {
        public Task<bool> EnsureAccessAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public void LockVault()
        {
        }
    }

    private sealed class TestThemeService : IThemeService
    {
        public AppThemeOption CurrentTheme => AppThemeOption.System;

        public Task InitializeAsync(Window window, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ApplyThemeAsync(AppThemeOption theme, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestAutoLockService : IInactivityAutoLockService
    {
        public bool IsEnabled { get; set; }

        public TimeSpan Timeout { get; set; }

        public void ResetTimer()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public AppLanguageOption CurrentLanguage => AppLanguageOption.English;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ApplyLanguageAsync(AppLanguageOption language, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public string GetString(string key)
        {
            return key switch
            {
                "SettingsVaultBtnChange" => "Change Master Password",
                "SettingsVaultBtnCreate" => "Create Master Password",
                "SettingsVaultDescUnlocked" => "Unlocked",
                "SettingsVaultDescLocked" => "Locked",
                "SettingsVaultDescNone" => "No password",
                "SettingsKeyFileDescEnabled" => "Enabled: {0}",
                "SettingsKeyFileDescDisabled" => "Disabled",
                _ => key,
            };
        }
    }
}
