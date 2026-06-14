using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluGenPass.Models;
using FluGenPass.Services;

namespace FluGenPass.ViewModels;

public partial class VaultEntryItemViewModel : ObservableObject
{
    private readonly Func<VaultEntryItemViewModel, Task> _copyAction;
    private readonly Func<VaultEntryItemViewModel, Task> _deleteAction;
    private readonly Func<VaultEntryItemViewModel, Task> _editTagsAction;
    private readonly Func<VaultEntryItemViewModel, Task> _cloneAction;
    private readonly Func<VaultEntryItemViewModel, Task> _editDetailsAction;
    private readonly ILocalizationService _localizationService;

    [ObservableProperty]
    private bool _isRevealed;

    public VaultEntryItemViewModel(
        VaultEntry entry,
        Func<VaultEntryItemViewModel, Task> copyAction,
        Func<VaultEntryItemViewModel, Task> deleteAction,
        Func<VaultEntryItemViewModel, Task> editTagsAction,
        Func<VaultEntryItemViewModel, Task> cloneAction,
        Func<VaultEntryItemViewModel, Task> editDetailsAction,
        ILocalizationService localizationService
    )
    {
        Entry = entry;
        _copyAction = copyAction;
        _deleteAction = deleteAction;
        _editTagsAction = editTagsAction;
        _cloneAction = cloneAction;
        _editDetailsAction = editDetailsAction;
        _localizationService = localizationService;
    }

    public VaultEntry Entry { get; }

    public Guid Id => Entry.Id;

    public string SiteName => Entry.SiteName;

    public string Url => Entry.Url;

    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);

    public string Password => new string(Entry.Password);

    public IReadOnlyList<string> Tags => Entry.Tags;

    public bool HasTags => Tags.Count > 0;

    public string TagsDisplay => Tags.Count == 0 ? "-" : string.Join(", ", Tags);

    public DateTimeOffset CreatedUtc => Entry.CreatedUtc;

    public string PasswordDisplay => IsRevealed ? new string(Entry.Password) : new string('*', Math.Max(12, Entry.Password.Length));

    public string RevealButtonText => IsRevealed 
        ? _localizationService.GetString("VaultBtnHide") 
        : _localizationService.GetString("VaultBtnReveal");

    partial void OnIsRevealedChanged(bool value)
    {
        OnPropertyChanged(nameof(PasswordDisplay));
        OnPropertyChanged(nameof(RevealButtonText));
    }

    [RelayCommand]
    private void ToggleReveal()
    {
        IsRevealed = !IsRevealed;
    }

    [RelayCommand]
    private Task CopyEntryAsync()
    {
        return _copyAction(this);
    }

    [RelayCommand]
    private Task DeleteEntryAsync()
    {
        return _deleteAction(this);
    }

    [RelayCommand]
    private Task EditTagsAsync()
    {
        return _editTagsAction(this);
    }

    [RelayCommand]
    private void CopySiteName()
    {
        App.GetRequiredService<IClipboardService>().SetText(SiteName);
        App.GetRequiredService<INotificationService>().ShowSuccess(
            _localizationService.GetString("NotifCopiedTitle"),
            string.Format(_localizationService.GetString("NotifCopiedSiteMsg"), SiteName)
        );
    }

    [RelayCommand]
    private void CopyUrl()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        App.GetRequiredService<IClipboardService>().SetText(Url);
        App.GetRequiredService<INotificationService>().ShowSuccess(
            _localizationService.GetString("NotifCopiedTitle"),
            string.Format(_localizationService.GetString("NotifCopiedUrlMsg"), SiteName)
        );
    }

    [RelayCommand]
    private void OpenUrl()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            return;
        }

        try
        {
            string targetUrl = Url;
            if (!targetUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !targetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                targetUrl = "https://" + targetUrl;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = targetUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore if OS doesn't support shell launch
        }
    }

    [RelayCommand]
    private Task CloneEntryAsync()
    {
        return _cloneAction(this);
    }

    [RelayCommand]
    private Task EditDetailsAsync()
    {
        return _editDetailsAction(this);
    }

    [RelayCommand]
    private async Task CheckPwnedAsync()
    {
        var pwnedService = App.GetRequiredService<IPwnedPasswordService>();
        var dialogService = App.GetRequiredService<IDialogService>();

        try
        {
            int count = await pwnedService.GetPwnCountAsync(Password);
            if (count > 0)
            {
                await dialogService.ShowMessageAsync(
                    _localizationService.GetString("DlgPwnedTitle"),
                    string.Format(_localizationService.GetString("DlgPwnedWarning"), count),
                    _localizationService.GetString("DlgPwnedClose")
                );
            }
            else
            {
                await dialogService.ShowMessageAsync(
                    _localizationService.GetString("DlgPwnedTitle"),
                    _localizationService.GetString("DlgPwnedSafe"),
                    _localizationService.GetString("DlgPwnedClose")
                );
            }
        }
        catch
        {
            await dialogService.ShowMessageAsync(
                _localizationService.GetString("DlgPwnedTitle"),
                _localizationService.GetString("DlgPwnedError"),
                _localizationService.GetString("DlgPwnedClose")
            );
        }
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(RevealButtonText));
    }

    public void RefreshTags()
    {
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(TagsDisplay));
    }

    public void RefreshDetails()
    {
        OnPropertyChanged(nameof(SiteName));
        OnPropertyChanged(nameof(Url));
        OnPropertyChanged(nameof(HasUrl));
    }
}
