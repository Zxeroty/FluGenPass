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
    private readonly IKeyFileService _keyFileService;
    private readonly IInactivityAutoLockService _autoLockService;
    private readonly ILocalizationService _localizationService;
    private bool _isInitializing;
    private bool _isUpdatingKeyFileState;

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
    private bool _isKeyFileEnabled;

    [ObservableProperty]
    private string _keyFileName = string.Empty;

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
        IKeyFileService keyFileService,
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
        _keyFileService = keyFileService;
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

    public string KeyFileDescription => IsKeyFileEnabled
        ? string.Format(_localizationService.GetString("SettingsKeyFileDescEnabled"), KeyFileName)
        : _localizationService.GetString("SettingsKeyFileDescDisabled");

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
        OnPropertyChanged(nameof(KeyFileDescription));
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

    partial void OnIsKeyFileEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(KeyFileDescription));

        if (_isInitializing || _isUpdatingKeyFileState)
        {
            return;
        }

        _ = ToggleKeyFileAsync(value);
    }

    partial void OnKeyFileNameChanged(string value)
    {
        OnPropertyChanged(nameof(KeyFileDescription));
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
        KeyFileMetadata? keyFile = await _keyFileService.GetMetadataAsync();
        IsKeyFileEnabled = keyFile is not null;
        KeyFileName = keyFile?.FileName ?? string.Empty;
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
                byte[]? keyFileSecret = null;
                try
                {
                    if (IsKeyFileEnabled)
                    {
                        keyFileSecret = await PromptCurrentKeyFileSecretAsync();
                        if (keyFileSecret is null)
                        {
                            return;
                        }
                    }

                    await _masterPasswordService.ChangeMasterPasswordAsync(newPassword, keyFileSecret);
                }
                finally
                {
                    ZeroIfPresent(keyFileSecret);
                }

                _notificationService.ShowSuccess(
                    "Master password updated",
                    "Vault access now uses the new master password."
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

    [RelayCommand]
    private async Task RegenerateKeyFileAsync()
    {
        if (IsBusy || !IsKeyFileEnabled)
        {
            return;
        }

        await CreateOrReplaceKeyFileAsync(revertToggleOnCancel: false, replacingExistingKeyFile: true);
    }

    private async Task ToggleKeyFileAsync(bool enable)
    {
        if (enable)
        {
            await CreateOrReplaceKeyFileAsync(revertToggleOnCancel: true, replacingExistingKeyFile: false);
            return;
        }

        bool confirmed = await _dialogService.ConfirmAsync(
            "Disable key file requirement",
            "The vault will stop asking for the key file after the master password. The file itself will not be deleted from disk.",
            "Disable"
        );

        if (!confirmed)
        {
            SetKeyFileEnabledWithoutPrompt(true);
            return;
        }

        try
        {
            KeyFileAuthResult? auth = await AuthenticateCurrentKeyFileRequirementAsync();
            if (auth is null)
            {
                SetKeyFileEnabledWithoutPrompt(true);
                return;
            }

            try
            {
                await _masterPasswordService.DisableKeyFileAsync(auth.Password);
                KeyFileName = string.Empty;
                _notificationService.ShowInfo("Key file disabled", "The vault will only require the master password.");
            }
            finally
            {
                auth.Clear();
            }
        }
        catch (Exception exception)
        {
            SetKeyFileEnabledWithoutPrompt(true);
            _notificationService.ShowError("Key file update failed", exception.Message);
        }
    }

    private async Task<KeyFileAuthResult?> AuthenticateCurrentKeyFileRequirementAsync()
    {
        bool wasUnlocked = _masterPasswordService.IsUnlocked;

        string? password = await PromptVerifiedPasswordAsync();
        if (password is null)
        {
            return null;
        }

        string? keyFilePath = _dialogService.PromptForOpenKeyFilePath();
        if (string.IsNullOrWhiteSpace(keyFilePath))
        {
            if (!wasUnlocked)
            {
                _masterPasswordService.Lock();
            }

            return null;
        }

        byte[]? keyFileSecret = await _keyFileService.GetAndVerifySecretAsync(keyFilePath);
        if (keyFileSecret is null)
        {
            if (!wasUnlocked)
            {
                _masterPasswordService.Lock();
            }

            _notificationService.ShowError("Access denied", "That key file did not match this vault.");
            return null;
        }

        bool unlocked = await _masterPasswordService.TryUnlockAsync(password, keyFileSecret);
        if (!unlocked)
        {
            ZeroIfPresent(keyFileSecret);
            if (!wasUnlocked)
            {
                _masterPasswordService.Lock();
            }

            _notificationService.ShowError("Access denied", "That key file did not match this vault.");
            return null;
        }

        return new KeyFileAuthResult(password, keyFileSecret);
    }

    private async Task CreateOrReplaceKeyFileAsync(bool revertToggleOnCancel, bool replacingExistingKeyFile)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            string? password;
            KeyFileAuthResult? existingKeyFileAuth = null;

            if (replacingExistingKeyFile)
            {
                existingKeyFileAuth = await AuthenticateCurrentKeyFileRequirementAsync();
                if (existingKeyFileAuth is null)
                {
                    if (revertToggleOnCancel)
                    {
                        SetKeyFileEnabledWithoutPrompt(false);
                    }
                    return;
                }

                password = existingKeyFileAuth.Password;
            }
            else
            {
                password = await PromptVerifiedPasswordAsync();
                if (password is null)
                {
                    if (revertToggleOnCancel)
                    {
                        SetKeyFileEnabledWithoutPrompt(false);
                    }
                    return;
                }

                bool unlocked = await _masterPasswordService.TryUnlockAsync(password);
                if (!unlocked)
                {
                    if (revertToggleOnCancel)
                    {
                        SetKeyFileEnabledWithoutPrompt(false);
                    }
                    _notificationService.ShowError("Access denied", "That master password did not unlock the vault.");
                    return;
                }
            }

            try
            {
                string? filePath = _dialogService.PromptForSaveKeyFilePath();
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    if (revertToggleOnCancel)
                    {
                        SetKeyFileEnabledWithoutPrompt(false);
                    }
                    return;
                }

                KeyFileCreationResult result = await _keyFileService.CreateKeyFileAsync(filePath);
                try
                {
                    await _masterPasswordService.EnableKeyFileAsync(password, result.Metadata, result.Secret);
                    KeyFileName = result.Metadata.FileName;
                    SetKeyFileEnabledWithoutPrompt(true);
                }
                finally
                {
                    ZeroIfPresent(result.Secret);
                }
            }
            finally
            {
                existingKeyFileAuth?.Clear();
            }

            _notificationService.ShowSuccess(
                "Key file ready",
                "The generated key file is now required after the master password."
            );
        }
        catch (Exception exception)
        {
            if (revertToggleOnCancel)
            {
                SetKeyFileEnabledWithoutPrompt(false);
            }

            _notificationService.ShowError("Key file update failed", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetKeyFileEnabledWithoutPrompt(bool value)
    {
        _isUpdatingKeyFileState = true;
        IsKeyFileEnabled = value;
        _isUpdatingKeyFileState = false;
    }

    private async Task<string?> PromptVerifiedPasswordAsync()
    {
        string? password = await _dialogService.PromptForUnlockPasswordAsync();
        if (string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        if (await _masterPasswordService.VerifyPasswordAsync(password))
        {
            return password;
        }

        _notificationService.ShowError("Access denied", "That master password did not unlock the vault.");
        return null;
    }

    private async Task<byte[]?> PromptCurrentKeyFileSecretAsync()
    {
        string? keyFilePath = _dialogService.PromptForOpenKeyFilePath();
        if (string.IsNullOrWhiteSpace(keyFilePath))
        {
            return null;
        }

        byte[]? keyFileSecret = await _keyFileService.GetAndVerifySecretAsync(keyFilePath);
        if (keyFileSecret is null)
        {
            _notificationService.ShowError("Access denied", "That key file did not match this vault.");
        }

        return keyFileSecret;
    }

    private static void ZeroIfPresent(byte[]? value)
    {
        if (value is not null)
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(value);
        }
    }

    private sealed record KeyFileAuthResult(string Password, byte[] KeyFileSecret)
    {
        public void Clear()
        {
            ZeroIfPresent(KeyFileSecret);
        }
    }
}
