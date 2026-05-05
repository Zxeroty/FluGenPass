using System.Security.Cryptography;

namespace FluGenPass.Services;

public sealed class VaultAccessCoordinator(
    IDialogService dialogService,
    IMasterPasswordService masterPasswordService,
    IKeyFileService keyFileService,
    INotificationService notificationService
) : IVaultAccessCoordinator
{
    public async Task<bool> EnsureAccessAsync(CancellationToken cancellationToken = default)
    {
        if (masterPasswordService.IsUnlocked)
        {
            return true;
        }

        if (!await masterPasswordService.HasMasterPasswordAsync(cancellationToken))
        {
            string? newPassword = await dialogService.PromptForNewMasterPasswordAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                return false;
            }

            await masterPasswordService.SetMasterPasswordAsync(newPassword, cancellationToken: cancellationToken);
            notificationService.ShowSuccess("Vault ready", "Master password created and vault unlocked.");
            return true;
        }

        string? password = await dialogService.PromptForUnlockPasswordAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        bool passwordVerified = await masterPasswordService.VerifyPasswordAsync(password, cancellationToken);

        if (!passwordVerified)
        {
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
                notificationService.ShowError("Access denied", "That master password did not unlock the vault.");
                return false;
            }

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
            masterPasswordService.Lock();
            notificationService.ShowError("Access denied", "That key file did not unlock the vault.");
            return false;
        }

        try
        {
            bool unlocked = await masterPasswordService.TryUnlockAsync(password, keyFileSecret, cancellationToken);
            if (!unlocked)
            {
                masterPasswordService.Lock();
                notificationService.ShowError("Access denied", "That key file did not unlock the vault.");
                return false;
            }

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
