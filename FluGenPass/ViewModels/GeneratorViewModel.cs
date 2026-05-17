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
    private readonly ILocalizationService _localizationService;

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

    private char[]? _generatedPassword;

    public string GeneratedPassword => _generatedPassword != null ? new string(_generatedPassword) : string.Empty;

    [ObservableProperty]
    private PasswordStrength _strength = PasswordStrength.Strong;

    [ObservableProperty]
    private double _entropyBits;

    [ObservableProperty]
    private int _strengthPercent = 100;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public GeneratorViewModel(
        IPasswordGeneratorService passwordGeneratorService,
        IClipboardService clipboardService,
        INotificationService notificationService,
        IVaultAccessCoordinator vaultAccessCoordinator,
        IDialogService dialogService,
        IVaultService vaultService,
        ILocalizationService localizationService
    )
    {
        _passwordGeneratorService = passwordGeneratorService;
        _clipboardService = clipboardService;
        _notificationService = notificationService;
        _vaultAccessCoordinator = vaultAccessCoordinator;
        _dialogService = dialogService;
        _vaultService = vaultService;
        _localizationService = localizationService;

        StatusMessage = _localizationService.GetString("GenOptionsSubtitle");

        RefreshGeneratedPassword();
    }

    public bool HasCharacterSelection =>
        IncludeUppercase || IncludeLowercase || IncludeNumbers || IncludeSymbols;

    public string StrengthLabel => string.Format(
        _localizationService.GetString("GenStrength"), 
        _localizationService.GetString($"Gen{Strength}Title")
    );

    public string EntropyLabel => string.Format(
        _localizationService.GetString("GenBits"), 
        EntropyBits
    );

    partial void OnIncludeUppercaseChanged(bool value) => RefreshGeneratedPassword();

    partial void OnIncludeLowercaseChanged(bool value) => RefreshGeneratedPassword();

    partial void OnIncludeNumbersChanged(bool value) => RefreshGeneratedPassword();

    partial void OnIncludeSymbolsChanged(bool value) => RefreshGeneratedPassword();

    partial void OnLengthChanged(int value) => RefreshGeneratedPassword();

    partial void OnStrengthChanged(PasswordStrength value) => OnPropertyChanged(nameof(StrengthLabel));

    partial void OnEntropyBitsChanged(double value) => OnPropertyChanged(nameof(EntropyLabel));

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
        _notificationService.ShowSuccess(
            _localizationService.GetString("NotifCopiedTitle"), 
            _localizationService.GetString("GenStatusCopied")
        );
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
                Password = _generatedPassword != null ? (char[])_generatedPassword.Clone() : Array.Empty<char>(),
                CreatedUtc = DateTimeOffset.UtcNow,
            }
        );

        await _vaultService.SaveAsync(entries.OrderByDescending(entry => entry.CreatedUtc));
        _notificationService.ShowSuccess(
            _localizationService.GetString("NotifSuccess"), 
            string.Format(_localizationService.GetString("GenStatusSaved"), siteName)
        );
    }

    private bool CanGeneratePassword() => HasCharacterSelection;

    private bool CanCopyToClipboard() => _generatedPassword != null && _generatedPassword.Length > 0;

    private bool CanSaveToVault() => _generatedPassword != null && _generatedPassword.Length > 0;

    private void RefreshGeneratedPassword()
    {
        if (!HasCharacterSelection)
        {
            _generatedPassword?.Clear();
            _generatedPassword = null;
            EntropyBits = 0;
            Strength = PasswordStrength.Weak;
            StrengthPercent = 0;
            StatusMessage = _localizationService.GetString("GenOptionsSubtitle");
        }
        else
        {
            PasswordOptions options = BuildOptions();
            _generatedPassword?.Clear();
            _generatedPassword = _passwordGeneratorService.Generate(options);
            EntropyBits = Math.Round(_passwordGeneratorService.EstimateEntropy(options), 1);
            Strength = _passwordGeneratorService.EvaluateStrength(options);
            StrengthPercent = Strength switch
            {
                PasswordStrength.Weak => 33,
                PasswordStrength.Medium => 66,
                _ => 100,
            };
            StatusMessage = _localizationService.GetString("GenStatusReady");
        }

        OnPropertyChanged(nameof(GeneratedPassword));
        CopyToClipboardCommand.NotifyCanExecuteChanged();
        SaveToVaultCommand.NotifyCanExecuteChanged();
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
