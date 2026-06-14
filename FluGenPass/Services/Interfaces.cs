using System.Windows;
using FluGenPass.Models;
using Wpf.Ui.Controls;

namespace FluGenPass.Services;

public interface IPasswordGeneratorService
{
    char[] Generate(PasswordOptions options);

    void Generate(PasswordOptions options, Span<char> destination);

    PasswordStrength EvaluateStrength(PasswordOptions options);

    double EstimateEntropy(PasswordOptions options);
}

public interface ISettingsService
{
    string SettingsFilePath { get; }

    Task<AppSettings> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public interface ISessionStateService
{
    event EventHandler<bool>? UnlockStateChanged;

    bool IsUnlocked { get; }

    void SetVaultKey(byte[] key);

    byte[] GetRequiredVaultKey();

    void Lock();
}

public interface IMasterPasswordService
{
    bool IsUnlocked { get; }

    Task<bool> HasMasterPasswordAsync(CancellationToken cancellationToken = default);

    Task SetMasterPasswordAsync(char[] password, byte[]? keyFileSecret = null, CancellationToken cancellationToken = default);

    Task ChangeMasterPasswordAsync(char[] newPassword, byte[]? newKeyFileSecret = null, CancellationToken cancellationToken = default);

    Task ResetAsync(CancellationToken cancellationToken = default);

    Task<bool> TryUnlockAsync(char[] password, byte[]? keyFileSecret = null, CancellationToken cancellationToken = default);

    void Lock();

    Task<bool> VerifyPasswordAsync(char[] password, CancellationToken cancellationToken = default);

    Task EnableKeyFileAsync(
        char[] password,
        KeyFileMetadata keyFileMetadata,
        byte[] keyFileSecret,
        CancellationToken cancellationToken = default
    );

    Task DisableKeyFileAsync(char[] password, CancellationToken cancellationToken = default);
}

public interface IKeyFileService
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    Task<KeyFileMetadata?> GetMetadataAsync(CancellationToken cancellationToken = default);

    Task<KeyFileCreationResult> CreateKeyFileAsync(string filePath, CancellationToken cancellationToken = default);

    Task<bool> VerifyAsync(string filePath, CancellationToken cancellationToken = default);

    Task<byte[]?> GetAndVerifySecretAsync(string filePath, CancellationToken cancellationToken = default);

    Task DisableAsync(CancellationToken cancellationToken = default);
}

public interface IVaultService
{
    string VaultFilePath { get; }

    Task<IReadOnlyList<VaultEntry>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IEnumerable<VaultEntry> entries, CancellationToken cancellationToken = default);
}

public interface IVaultTransferService
{
    Task<VaultExportResult> ExportSecureAsync(
        IEnumerable<VaultEntry> entries,
        string filePath,
        CancellationToken cancellationToken = default
    );

    Task<VaultExportResult> ExportBitwardenCsvAsync(
        IEnumerable<VaultEntry> entries,
        string filePath,
        CancellationToken cancellationToken = default
    );

    Task<VaultImportResult> ImportAsync(
        string filePath,
        IEnumerable<VaultEntry> existingEntries,
        CancellationToken cancellationToken = default
    );

    Task<VaultVerificationResult> VerifyAsync(
        string filePath,
        CancellationToken cancellationToken = default
    );
}

public interface ITransferSignatureService
{
    VaultTransferSignature CreateSignature(byte[] payloadBytes);

    VaultIntegrityStatus VerifySignature(
        byte[] payloadBytes,
        VaultTransferSignature signature,
        out string integritySummary,
        out IReadOnlyList<string> warnings
    );
}

public interface IClipboardService
{
    void SetText(string text);
}

public interface INotificationService
{
    void Initialize(SnackbarPresenter presenter);

    void ShowInfo(string title, string message);

    void ShowSuccess(string title, string message);

    void ShowError(string title, string message);
}

public interface IDialogService
{
    void Initialize(ContentDialogHost dialogHost, Window ownerWindow);

    Task<(string SiteName, string Url, char[] Password)?> PromptForSiteDetailsAsync(string initialSiteName = "", string initialUrl = "", string initialPassword = "", CancellationToken cancellationToken = default);

    Task<string?> PromptForTagsAsync(string initialValue = "", CancellationToken cancellationToken = default);

    Task<char[]?> PromptForNewMasterPasswordAsync(CancellationToken cancellationToken = default);

    Task<char[]?> PromptForUnlockPasswordAsync(CancellationToken cancellationToken = default);

    string? PromptForSaveKeyFilePath();

    string? PromptForOpenKeyFilePath();

    Task<bool> ConfirmAsync(
        string title,
        string message,
        string primaryButtonText = "Confirm",
        string closeButtonText = "Cancel",
        CancellationToken cancellationToken = default
    );

    Task ShowMessageAsync(
        string title,
        string message,
        string closeButtonText = "Close",
        CancellationToken cancellationToken = default
    );

    Task<bool> ShowKeyFileWarningAsync(CancellationToken cancellationToken = default);
}

public interface IThemeService
{
    AppThemeOption CurrentTheme { get; }

    Task InitializeAsync(Window window, CancellationToken cancellationToken = default);

    Task ApplyThemeAsync(AppThemeOption theme, CancellationToken cancellationToken = default);
}

public interface IVaultAccessCoordinator
{
    Task<bool> EnsureAccessAsync(CancellationToken cancellationToken = default);

    void LockVault();
}

public interface IInactivityAutoLockService
{
    bool IsEnabled { get; set; }

    TimeSpan Timeout { get; set; }

    void ResetTimer();

    void Dispose();
}

public interface ILocalizationService
{
    event EventHandler? LanguageChanged;

    AppLanguageOption CurrentLanguage { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task ApplyLanguageAsync(AppLanguageOption language, CancellationToken cancellationToken = default);

    string GetString(string key);
}

public interface IPwnedPasswordService
{
    Task<int> GetPwnCountAsync(string password, CancellationToken cancellationToken = default);
}
