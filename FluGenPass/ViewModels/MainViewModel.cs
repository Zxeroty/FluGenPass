using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluGenPass.Services;

namespace FluGenPass.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IVaultAccessCoordinator _vaultAccessCoordinator;

    [ObservableProperty]
    private bool _isVaultUnlocked;

    public MainViewModel(ISessionStateService sessionStateService, IVaultAccessCoordinator vaultAccessCoordinator)
    {
        _vaultAccessCoordinator = vaultAccessCoordinator;
        IsVaultUnlocked = sessionStateService.IsUnlocked;

        sessionStateService.UnlockStateChanged += (_, isUnlocked) => IsVaultUnlocked = isUnlocked;
    }

    [RelayCommand]
    private void LockVault()
    {
        _vaultAccessCoordinator.LockVault();
    }
}