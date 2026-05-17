using System.Security.Cryptography;
using FluGenPass.Models;

namespace FluGenPass.Services;

public sealed class VaultAccessCoordinator(
    IDialogService dialogService,
    IMasterPasswordService masterPasswordService,
    IKeyFileService keyFileService,
    INotificationService notificationService,
    ISettingsService settingsService
) : IVaultAccessCoordinator
{
    private const int MaxFailedAttempts = 3;
    private const int LockoutMinutes = 5;

    private async Task RecordFailedAttemptAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        settings.AuthFailedAttempts++;
        
        if (settings.AuthFailedAttempts >= MaxFailedAttempts)
        {
            settings.AuthLockoutUntilUtc = DateTimeOffset.UtcNow.AddMinutes(LockoutMinutes);
        }
        
        await settingsService.SaveAsync(settings, ct);
    }

    private async Task ResetFailedAttemptsAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        if (settings.AuthFailedAttempts > 0 || settings.AuthLockoutUntilUtc != null)
        {
            settings.AuthFailedAttempts = 0;
            settings.AuthLockoutUntilUtc = null;
            await settingsService.SaveAsync(settings, ct);
        }
    }

    public async Task<bool> EnsureAccessAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetAsync(cancellationToken);

        if (settings.AuthLockoutUntilUtc.HasValue && DateTimeOffset.UtcNow < settings.AuthLockoutUntilUtc.Value)
        {
            TimeSpan remaining = settings.AuthLockoutUntilUtc.Value - DateTimeOffset.UtcNow;
            notificationService.ShowError(
                "Locked out", 
                $"Too many failed attempts. Try again in {Math.Ceiling(remaining.TotalSeconds)} seconds."
            );
            return false;
        }

        // If lockout expired but we still have failed attempts, reset them on first attempt
        if (settings.AuthFailedAttempts >= MaxFailedAttempts)
        {
            await ResetFailedAttemptsAsync(cancellationToken);
        }

        if (masterPasswordService.IsUnlocked)
        {
            return true;
        }

        if (!await masterPasswordService.HasMasterPasswordAsync(cancellationToken))
        {
            char[]? newPassword = await dialogService.PromptForNewMasterPasswordAsync(cancellationToken);

            if (newPassword == null || newPassword.Length == 0)
            {
                return false;
            }

            try
            {
                await masterPasswordService.SetMasterPasswordAsync(newPassword, cancellationToken: cancellationToken);
                notificationService.ShowSuccess("Vault ready", "Master password created and vault unlocked.");
                return true;
            }
            finally
            {
                newPassword.Clear();
            }
        }

        char[]? password = await dialogService.PromptForUnlockPasswordAsync(cancellationToken);

        if (password == null || password.Length == 0)
        {
            return false;
        }

        try
        {
            bool passwordVerified = await masterPasswordService.VerifyPasswordAsync(password, cancellationToken);

            if (!passwordVerified)
            {
                await RecordFailedAttemptAsync(cancellationToken);
                notificationService.ShowError("Access denied", "That master password did not unlock the vault.");
                return false;
            }

            if (!await keyFileService.IsEnabledAsync(cancellationToken))
            {
                bool passwordUnlocked = await masterPasswordService.TryUnlockAsync(
                    password,
                    cancellationToken: cancellationToken
                );
                if (!passwordUnlocked)
                {
                    await RecordFailedAttemptAsync(cancellationToken);
                    notificationService.ShowError("Access denied", "That master password did not unlock the vault.");
                    return false;
                }

                await ResetFailedAttemptsAsync(cancellationToken);
                notificationService.ShowSuccess("Vault unlocked", "Saved credentials are available for this session.");
                return true;
            }

            string? keyFilePath = dialogService.PromptForOpenKeyFilePath();
            if (string.IsNullOrWhiteSpace(keyFilePath))
            {
                masterPasswordService.Lock();
                return false;
            }

            byte[]? keyFileSecret = await keyFileService.GetAndVerifySecretAsync(keyFilePath, cancellationToken);
            if (keyFileSecret is null)
            {
                await RecordFailedAttemptAsync(cancellationToken);
                masterPasswordService.Lock();
                notificationService.ShowError("Access denied", "That key file did not unlock the vault.");
                return false;
            }

            try
            {
                bool unlocked = await masterPasswordService.TryUnlockAsync(password, keyFileSecret, cancellationToken);
                if (!unlocked)
                {
                    await RecordFailedAttemptAsync(cancellationToken);
                    masterPasswordService.Lock();
                    notificationService.ShowError("Access denied", "That key file did not unlock the vault.");
                    return false;
                }

                await ResetFailedAttemptsAsync(cancellationToken);
                notificationService.ShowSuccess(
                    "Vault unlocked",
                    "Master password and key file were accepted for this session."
                );
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(keyFileSecret);
            }
        }
        finally
        {
            password?.Clear();
        }
    }

    public void LockVault()
    {
        if (!masterPasswordService.IsUnlocked)
        {
            return;
        }

        masterPasswordService.Lock();
        notificationService.ShowInfo("Vault locked", "The in-memory vault key has been cleared.");
    }
}
