using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluGenPass.Models;
using FluGenPass.Services;

namespace FluGenPass.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notificationService;
    private readonly IVaultAccessCoordinator _vaultAccessCoordinator;
    private readonly IThemeService _themeService;
    private readonly IVaultService _vaultService;
    private readonly ISettingsService _settingsService;
    private readonly IMasterPasswordService _masterPasswordService;
    private readonly IInactivityAutoLockService _autoLockService;
    private readonly ILocalizationService _localizationService;
    private bool _isInitializing;

    [ObservableProperty]
    private AppThemeOption _selectedTheme = AppThemeOption.System;

    [ObservableProperty]
    private bool _isVaultUnlocked;

    [ObservableProperty]
    private bool _hasMasterPassword;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _autoLockEnabled = true;

    [ObservableProperty]
    private int _autoLockTimeoutMinutes = 5;

    [ObservableProperty]
    private AppLanguageOption _selectedLanguage = AppLanguageOption.English;

    public SettingsViewModel(
        IDialogService dialogService,
        INotificationService notificationService,
        IVaultAccessCoordinator vaultAccessCoordinator,
        IThemeService themeService,
        IVaultService vaultService,
        ISettingsService settingsService,
        IMasterPasswordService masterPasswordService,
        ISessionStateService sessionStateService,
        IInactivityAutoLockService autoLockService,
        ILocalizationService localizationService
    )
    {
        _dialogService = dialogService;
        _notificationService = notificationService;
        _vaultAccessCoordinator = vaultAccessCoordinator;
        _themeService = themeService;
        _vaultService = vaultService;
        _settingsService = settingsService;
        _masterPasswordService = masterPasswordService;
        _autoLockService = autoLockService;
        _localizationService = localizationService;
        _isInitializing = true;
        SelectedTheme = _themeService.CurrentTheme;
        SelectedLanguage = _localizationService.CurrentLanguage;
        _isInitializing = false;
        IsVaultUnlocked = sessionStateService.IsUnlocked;

        sessionStateService.UnlockStateChanged += (_, isUnlocked) => IsVaultUnlocked = isUnlocked;
    }

    public IReadOnlyList<AppThemeOption> ThemeOptions { get; } = Enum.GetValues<AppThemeOption>();

    public IReadOnlyList<AppLanguageOption> LanguageOptions { get; } = Enum.GetValues<AppLanguageOption>();

    public IReadOnlyList<int> AutoLockTimeoutOptions { get; } = new[] { 1, 2, 3, 5, 10, 15, 30, 60 };

    public string AppDirectory => Path.GetDirectoryName(_settingsService.SettingsFilePath) ?? StoragePaths.GetAppDirectory();

    public string SettingsFilePath => _settingsService.SettingsFilePath;

    public string VaultFilePath => _vaultService.VaultFilePath;

    public string PrimaryVaultPasswordActionLabel => HasMasterPassword 
        ? _localizationService.GetString("SettingsVaultBtnChange") 
        : _localizationService.GetString("SettingsVaultBtnCreate");

    public string VaultAccessDescription => HasMasterPassword
        ? IsVaultUnlocked
            ? _localizationService.GetString("SettingsVaultDescUnlocked")
            : _localizationService.GetString("SettingsVaultDescLocked")
        : _localizationService.GetString("SettingsVaultDescNone");

    partial void OnSelectedThemeChanged(AppThemeOption value)
    {
        if (_isInitializing)
        {
            return;
        }

        _ = _themeService.ApplyThemeAsync(value);
    }

    partial void OnSelectedLanguageChanged(AppLanguageOption value)
    {
        if (_isInitializing)
        {
            return;
        }

        _ = _localizationService.ApplyLanguageAsync(value);
        
        // Refresh localized properties
        OnPropertyChanged(nameof(PrimaryVaultPasswordActionLabel));
        OnPropertyChanged(nameof(VaultAccessDescription));
    }

    partial void OnHasMasterPasswordChanged(bool value)
    {
        OnPropertyChanged(nameof(PrimaryVaultPasswordActionLabel));
        OnPropertyChanged(nameof(VaultAccessDescription));
    }

    partial void OnIsVaultUnlockedChanged(bool value)
    {
        OnPropertyChanged(nameof(VaultAccessDescription));
    }

    partial void OnAutoLockEnabledChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = SaveAutoLockSettingsAsync();
        }
    }

    partial void OnAutoLockTimeoutMinutesChanged(int value)
    {
        if (!_isInitializing)
        {
            _ = SaveAutoLockSettingsAsync();
        }
    }

    private async Task SaveAutoLockSettingsAsync()
    {
        try
        {
            var settings = await _settingsService.GetAsync();
            settings.AutoLockEnabled = AutoLockEnabled;
            settings.AutoLockTimeoutMinutes = AutoLockTimeoutMinutes;
            await _settingsService.SaveAsync(settings);

            _autoLockService.IsEnabled = AutoLockEnabled && AutoLockTimeoutMinutes > 0;
            if (AutoLockTimeoutMinutes > 0)
            {
                _autoLockService.Timeout = TimeSpan.FromMinutes(AutoLockTimeoutMinutes);
            }
        }
        catch
        {
            
        }
    }

    public async Task InitializeAsync()
    {
        HasMasterPassword = await _masterPasswordService.HasMasterPasswordAsync();

        var settings = await _settingsService.GetAsync();
        _isInitializing = true;
        AutoLockEnabled = settings.AutoLockEnabled;
        AutoLockTimeoutMinutes = settings.AutoLockTimeoutMinutes > 0 ? settings.AutoLockTimeoutMinutes : 5;
        _isInitializing = false;
    }

    [RelayCommand]
    private async Task ConfigureMasterPasswordAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            if (HasMasterPassword && !await _vaultAccessCoordinator.EnsureAccessAsync())
            {
                return;
            }

            string? newPassword = await _dialogService.PromptForNewMasterPasswordAsync();

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                return;
            }

            if (HasMasterPassword)
            {
                await _masterPasswordService.ChangeMasterPasswordAsync(newPassword);
                _notificationService.ShowSuccess(
                    "Master password updated",
                    "Vault entries were re-encrypted with the new password."
                );
            }
            else
            {
                await _masterPasswordService.SetMasterPasswordAsync(newPassword);
                _notificationService.ShowSuccess(
                    "Master password created",
                    "Vault protection is now enabled for this device."
                );
            }

            await InitializeAsync();
        }
        catch (Exception exception)
        {
            _notificationService.ShowError("Password update failed", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResetVaultProtectionAsync()
    {
        if (IsBusy || !HasMasterPassword)
        {
            return;
        }

        bool confirmed = await _dialogService.ConfirmAsync(
            "Reset vault protection",
            "This will remove the master password, delete all saved vault entries, and lock the vault. This cannot be undone.",
            "Reset vault"
        );

        if (!confirmed)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _masterPasswordService.ResetAsync();
            _notificationService.ShowInfo(
                "Vault reset",
                "Master password removed and local vault contents cleared."
            );
            await InitializeAsync();
        }
        catch (Exception exception)
        {
            _notificationService.ShowError("Reset failed", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void LockVault()
    {
        _masterPasswordService.Lock();
    }
}