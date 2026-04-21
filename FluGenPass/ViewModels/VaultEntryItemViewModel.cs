using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluGenPass.Models;

namespace FluGenPass.ViewModels;

public partial class VaultEntryItemViewModel : ObservableObject
{
    private readonly Func<VaultEntryItemViewModel, Task> _copyAction;
    private readonly Func<VaultEntryItemViewModel, Task> _deleteAction;

    [ObservableProperty]
    private bool _isRevealed;

    public VaultEntryItemViewModel(
        VaultEntry entry,
        Func<VaultEntryItemViewModel, Task> copyAction,
        Func<VaultEntryItemViewModel, Task> deleteAction
    )
    {
        Entry = entry;
        _copyAction = copyAction;
        _deleteAction = deleteAction;
    }

    public VaultEntry Entry { get; }

    public Guid Id => Entry.Id;

    public string SiteName => Entry.SiteName;

    public string Password => Entry.Password;

    public DateTimeOffset CreatedUtc => Entry.CreatedUtc;

    public string PasswordDisplay => IsRevealed ? Entry.Password : new string('*', Math.Max(12, Entry.Password.Length));

    public string RevealButtonText => IsRevealed ? "Hide" : "Reveal";

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
}