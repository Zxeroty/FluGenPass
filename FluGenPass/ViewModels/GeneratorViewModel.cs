using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluGenPass.Models;
using FluGenPass.Services;

namespace FluGenPass.ViewModels;

public partial class GeneratorViewModel : ObservableObject
{
    private readonly IPasswordGeneratorService _passwordGeneratorService;
    private readonly IClipboardService _clipboardService;
    private readonly INotificationService _notificationService;
    private readonly IVaultAccessCoordinator _vaultAccessCoordinator;
    private readonly IDialogService _dialogService;
    private readonly IVaultService _vaultService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCharacterSelection))]
    [NotifyCanExecuteChangedFor(nameof(GeneratePasswordCommand))]
    private bool _includeUppercase = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCharacterSelection))]
    [NotifyCanExecuteChangedFor(nameof(GeneratePasswordCommand))]
    private bool _includeLowercase = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCharacterSelection))]
    [NotifyCanExecuteChangedFor(nameof(GeneratePasswordCommand))]
    private bool _includeNumbers = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCharacterSelection))]
    [NotifyCanExecuteChangedFor(nameof(GeneratePasswordCommand))]
    private bool _includeSymbols;

    [ObservableProperty]
    private int _length = 16;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveToVaultCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyToClipboardCommand))]
    private string _generatedPassword = string.Empty;

    [ObservableProperty]
    private PasswordStrength _strength = PasswordStrength.Strong;

    [ObservableProperty]
    private double _entropyBits;

    [ObservableProperty]
    private int _strengthPercent = 100;

    [ObservableProperty]
    private string _statusMessage = "Adjust the options and FluGenPass will refresh the password instantly.";

    public GeneratorViewModel(
        IPasswordGeneratorService passwordGeneratorService,
        IClipboardService clipboardService,
        INotificationService notificationService,
        IVaultAccessCoordinator vaultAccessCoordinator,
        IDialogService dialogService,
        IVaultService vaultService
    )
    {
        _passwordGeneratorService = passwordGeneratorService;
        _clipboardService = clipboardService;
        _notificationService = notificationService;
        _vaultAccessCoordinator = vaultAccessCoordinator;
        _dialogService = dialogService;
        _vaultService = vaultService;

        RefreshGeneratedPassword();
    }

    public bool HasCharacterSelection =>
        IncludeUppercase || IncludeLowercase || IncludeNumbers || IncludeSymbols;

    public string StrengthLabel => Strength.ToString();

    partial void OnIncludeUppercaseChanged(bool value) => RefreshGeneratedPassword();

    partial void OnIncludeLowercaseChanged(bool value) => RefreshGeneratedPassword();

    partial void OnIncludeNumbersChanged(bool value) => RefreshGeneratedPassword();

    partial void OnIncludeSymbolsChanged(bool value) => RefreshGeneratedPassword();

    partial void OnLengthChanged(int value) => RefreshGeneratedPassword();

    partial void OnStrengthChanged(PasswordStrength value) => OnPropertyChanged(nameof(StrengthLabel));

    [RelayCommand(CanExecute = nameof(CanGeneratePassword))]
    private void GeneratePassword()
    {
        RefreshGeneratedPassword();
    }

    [RelayCommand(CanExecute = nameof(CanCopyToClipboard))]
    private void CopyToClipboard()
    {
        if (string.IsNullOrWhiteSpace(GeneratedPassword))
        {
            return;
        }

        _clipboardService.SetText(GeneratedPassword);
        _notificationService.ShowSuccess("Copied", "The generated password is now on your clipboard.");
    }

    [RelayCommand(CanExecute = nameof(CanSaveToVault))]
    private async Task SaveToVaultAsync()
    {
        if (!await _vaultAccessCoordinator.EnsureAccessAsync())
        {
            return;
        }

        string? siteName = await _dialogService.PromptForSiteNameAsync();

        if (string.IsNullOrWhiteSpace(siteName))
        {
            return;
        }

        List<VaultEntry> entries = (await _vaultService.LoadAsync()).ToList();
        entries.Add(
            new VaultEntry
            {
                SiteName = siteName,
                Password = GeneratedPassword,
                CreatedUtc = DateTimeOffset.UtcNow,
            }
        );

        await _vaultService.SaveAsync(entries.OrderByDescending(entry => entry.CreatedUtc));
        _notificationService.ShowSuccess("Saved", $"{siteName} was added to the local vault.");
    }

    private bool CanGeneratePassword() => HasCharacterSelection;

    private bool CanCopyToClipboard() => !string.IsNullOrWhiteSpace(GeneratedPassword);

    private bool CanSaveToVault() => !string.IsNullOrWhiteSpace(GeneratedPassword);

    private void RefreshGeneratedPassword()
    {
        if (!HasCharacterSelection)
        {
            GeneratedPassword = string.Empty;
            EntropyBits = 0;
            Strength = PasswordStrength.Weak;
            StrengthPercent = 0;
            StatusMessage = "Select at least one character group to generate a password.";
            return;
        }

        PasswordOptions options = BuildOptions();
        GeneratedPassword = _passwordGeneratorService.Generate(options);
        EntropyBits = Math.Round(_passwordGeneratorService.EstimateEntropy(options), 1);
        Strength = _passwordGeneratorService.EvaluateStrength(options);
        StrengthPercent = Strength switch
        {
            PasswordStrength.Weak => 33,
            PasswordStrength.Medium => 66,
            _ => 100,
        };
        StatusMessage = "Cryptographically secure password ready.";
    }

    private PasswordOptions BuildOptions()
    {
        return new PasswordOptions
        {
            Length = Math.Clamp(Length, 8, 64),
            IncludeUppercase = IncludeUppercase,
            IncludeLowercase = IncludeLowercase,
            IncludeNumbers = IncludeNumbers,
            IncludeSymbols = IncludeSymbols,
        };
    }
}