namespace FluGenPass.Services;

public sealed class VaultAccessCoordinator(
    IDialogService dialogService,
    IMasterPasswordService masterPasswordService,
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

            await masterPasswordService.SetMasterPasswordAsync(newPassword, cancellationToken);
            notificationService.ShowSuccess("Vault ready", "Master password created and vault unlocked.");
            return true;
        }

        string? password = await dialogService.PromptForUnlockPasswordAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        bool isUnlocked = await masterPasswordService.TryUnlockAsync(password, cancellationToken);

        if (isUnlocked)
        {
            notificationService.ShowSuccess("Vault unlocked", "Saved credentials are available for this session.");
            return true;
        }

        notificationService.ShowError("Access denied", "That master password did not unlock the vault.");
        return false;
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