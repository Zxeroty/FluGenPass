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
    private readonly ILocalizationService _localizationService;

    [ObservableProperty]
    private bool _isRevealed;

    public VaultEntryItemViewModel(
        VaultEntry entry,
        Func<VaultEntryItemViewModel, Task> copyAction,
        Func<VaultEntryItemViewModel, Task> deleteAction,
        Func<VaultEntryItemViewModel, Task> editTagsAction,
        ILocalizationService localizationService
    )
    {
        Entry = entry;
        _copyAction = copyAction;
        _deleteAction = deleteAction;
        _editTagsAction = editTagsAction;
        _localizationService = localizationService;
    }

    public VaultEntry Entry { get; }

    public Guid Id => Entry.Id;

    public string SiteName => Entry.SiteName;

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

    public void RefreshTags()
    {
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(TagsDisplay));
    }
}
