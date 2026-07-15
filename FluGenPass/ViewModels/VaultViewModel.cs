using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluGenPass.Models;
using FluGenPass.Services;
using Microsoft.Win32;

namespace FluGenPass.ViewModels;

public partial class VaultViewModel : ObservableObject
{
    private readonly IVaultService _vaultService;
    private readonly IClipboardService _clipboardService;
    private readonly INotificationService _notificationService;
    private readonly IMasterPasswordService _masterPasswordService;
    private readonly IVaultTransferService _vaultTransferService;
    private readonly IDialogService _dialogService;
    private readonly ILocalizationService _localizationService;
    private readonly List<VaultEntryItemViewModel> _allEntries = [];

    [ObservableProperty]
    private bool _isVaultUnlocked;

    [ObservableProperty]
    private string _headline = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _transferHeadline = string.Empty;

    [ObservableProperty]
    private string _transferMessage = string.Empty;

    [ObservableProperty]
    private string _selectedTagFilter = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    public VaultViewModel(
        IVaultService vaultService,
        IClipboardService clipboardService,
        INotificationService notificationService,
        IMasterPasswordService masterPasswordService,
        IVaultTransferService vaultTransferService,
        IDialogService dialogService,
        ISessionStateService sessionStateService,
        ILocalizationService localizationService
    )
    {
        _vaultService = vaultService;
        _clipboardService = clipboardService;
        _notificationService = notificationService;
        _masterPasswordService = masterPasswordService;
        _vaultTransferService = vaultTransferService;
        _dialogService = dialogService;
        _localizationService = localizationService;
        IsVaultUnlocked = sessionStateService.IsUnlocked;

        _localizationService.LanguageChanged += OnLanguageChanged;

        Headline = _localizationService.GetString("VaultHeadLocked");
        StatusMessage = _localizationService.GetString("VaultMsgLocked");
        TransferHeadline = _localizationService.GetString("TransferTitle");
        TransferMessage = _localizationService.GetString("TransferDesc");

        AvailableTags.Add(AllTagsLabel);
        SelectedTagFilter = AllTagsLabel;

        Entries.CollectionChanged += OnEntriesChanged;
        sessionStateService.UnlockStateChanged += (_, isUnlocked) =>
        {
            IsVaultUnlocked = isUnlocked;
            _ = RefreshAsync();
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(AllTagsLabel));

        // Refresh static strings
        if (!IsVaultUnlocked)
        {
            Headline = _localizationService.GetString("VaultHeadLocked");
            StatusMessage = _localizationService.GetString("VaultMsgLocked");
        }
        else
        {
            UpdateHeader();
        }

        TransferHeadline = _localizationService.GetString("TransferTitle");
        TransferMessage = _localizationService.GetString("TransferDesc");

        foreach (VaultEntryItemViewModel entry in _allEntries)
        {
            entry.RefreshLocalization();
        }

        // Refresh AvailableTags labels
        RefreshAvailableTags();
    }

    public ObservableCollection<VaultEntryItemViewModel> Entries { get; } = [];

    public ObservableCollection<string> AvailableTags { get; } = [];

    public bool HasEntries => Entries.Count > 0;

    public string AllTagsLabel => _localizationService.GetString("VaultFilterAllTags");

    partial void OnSelectedTagFilterChanged(string value)
    {
        if (AvailableTags.Count == 0)
        {
            return;
        }

        ApplyFilters();
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilters();
    }

    [RelayCommand]
    public Task RefreshAsync()
    {
        return LoadEntriesAsync();
    }

    [RelayCommand]
    private async Task LockVaultAsync()
    {
        _masterPasswordService.Lock();
        
        foreach (VaultEntryItemViewModel entryItem in _allEntries)
        {
            entryItem.Entry.Password.Clear();
        }
        
        await LoadEntriesAsync();
        _notificationService.ShowInfo(
            _localizationService.GetString("NotifLockedTitle"),
            _localizationService.GetString("NotifLockedMsg")
        );
    }

    [RelayCommand]
    private async Task ImportPasswordsAsync()
    {
        if (!IsVaultUnlocked)
        {
            _notificationService.ShowError(
                _localizationService.GetString("VaultHeadLocked"),
                _localizationService.GetString("VaultMsgLocked")
            );
            return;
        }

        OpenFileDialog dialog = new()
        {
            Title = _localizationService.GetString("TransferImportTitle"),
            Filter = _localizationService.GetString("TransferImportFilter"),
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            IReadOnlyList<VaultEntry> currentEntries = _allEntries.Select(static entry => entry.Entry).ToList();
            VaultImportResult result = await _vaultTransferService.ImportAsync(dialog.FileName, currentEntries);

            if (result.IntegrityStatus == VaultIntegrityStatus.MissingChecksum)
            {
                bool continueImport = await _dialogService.ConfirmAsync(
                    _localizationService.GetString("TransferDlgChecksumTitle"),
                    _localizationService.GetString("TransferDlgChecksumMsg"),
                    primaryButtonText: _localizationService.GetString("TransferDlgChecksumBtn"),
                    closeButtonText: _localizationService.GetString("CommonBtnCancel")
                );

                if (!continueImport)
                {
                    SetTransferStatus(
                        _localizationService.GetString("TransferStatusCancelled"),
                        _localizationService.GetString("TransferVerifyNoChecksum")
                    );
                    return;
                }
            }
            else if (result.IntegrityStatus == VaultIntegrityStatus.MissingSignature)
            {
                bool continueImport = await _dialogService.ConfirmAsync(
                    _localizationService.GetString("TransferDlgSignatureTitle"),
                    _localizationService.GetString("TransferDlgSignatureMsg"),
                    primaryButtonText: _localizationService.GetString("TransferDlgChecksumBtn"),
                    closeButtonText: _localizationService.GetString("CommonBtnCancel")
                );

                if (!continueImport)
                {
                    SetTransferStatus(
                        _localizationService.GetString("NotifError"),
                        _localizationService.GetString("TransferVerifyNoSignature")
                    );
                    return;
                }
            }
            else if (result.IntegrityStatus == VaultIntegrityStatus.UntrustedSignature)
            {
                bool continueImport = await _dialogService.ConfirmAsync(
                    _localizationService.GetString("TransferDlgTrustTitle"),
                    _localizationService.GetString("TransferDlgTrustMsg"),
                    primaryButtonText: _localizationService.GetString("TransferDlgChecksumBtn"),
                    closeButtonText: _localizationService.GetString("CommonBtnCancel")
                );

                if (!continueImport)
                {
                    SetTransferStatus(
                        _localizationService.GetString("TransferStatusCancelled"),
                        _localizationService.GetString("TransferVerifyNoSignature")
                    );
                    return;
                }
            }

            await _vaultService.SaveAsync(result.Entries);
            await LoadEntriesAsync();

            string warningSummary = result.Warnings.Count == 0
                ? _localizationService.GetString("TransferImportNoWarnings")
                : string.Join(Environment.NewLine, result.Warnings.Take(6));

            SetTransferStatus(
                _localizationService.GetString("TransferStatusCompleted"),
                string.Format(
                    _localizationService.GetString("TransferImportSuccessMsg"),
                    result.ImportedCount,
                    result.SkippedCount,
                    result.IntegritySummary
                )
            );

            _notificationService.ShowSuccess(
                _localizationService.GetString("NotifSuccess"),
                string.Format(_localizationService.GetString("TransferImportNotifSuccess"), result.ImportedCount)
            );
            await _dialogService.ShowMessageAsync(
                _localizationService.GetString("TransferImportSummary"),
                string.Format(
                    _localizationService.GetString("TransferImportDialogSummary"),
                    result.ImportedCount,
                    result.SkippedCount,
                    result.IntegritySummary,
                    warningSummary
                )
            );
        }
        catch (Exception exception)
        {
            SetTransferStatus(_localizationService.GetString("TransferStatusFailed"), exception.Message);
            _notificationService.ShowError(
                _localizationService.GetString("TransferStatusFailed"),
                _localizationService.GetString("NotifError")
            );
        }
    }

    [RelayCommand]
    private async Task ExportSecureAsync()
    {
        if (!IsVaultUnlocked)
        {
            _notificationService.ShowError(
                _localizationService.GetString("VaultHeadLocked"),
                _localizationService.GetString("VaultMsgLocked")
            );
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = _localizationService.GetString("TransferExportSecureTitle"),
            Filter = "FluGenPass secure export (*.fgpexport.json)|*.fgpexport.json",
            AddExtension = true,
            DefaultExt = ".fgpexport.json",
            FileName = $"vault-export-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.fgpexport.json",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            VaultExportResult result = await _vaultTransferService.ExportSecureAsync(
                _allEntries.Select(static entry => entry.Entry),
                dialog.FileName
            );

            SetTransferStatus(_localizationService.GetString("TransferStatusExportReady"), result.IntegritySummary);
            _notificationService.ShowSuccess(
                _localizationService.GetString("NotifSuccess"),
                string.Format(_localizationService.GetString("TransferExportSuccessMsg"), result.ExportedCount)
            );
        }
        catch (Exception exception)
        {
            SetTransferStatus(_localizationService.GetString("TransferStatusFailed"), exception.Message);
            _notificationService.ShowError(
                _localizationService.GetString("TransferStatusFailed"),
                _localizationService.GetString("NotifError")
            );
        }
    }

    [RelayCommand]
    private async Task ExportBitwardenCsvAsync()
    {
        if (!IsVaultUnlocked)
        {
            _notificationService.ShowError(
                _localizationService.GetString("VaultHeadLocked"),
                _localizationService.GetString("VaultMsgLocked")
            );
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = _localizationService.GetString("TransferExportCsvTitle"),
            Filter = "Bitwarden CSV (*.csv)|*.csv",
            AddExtension = true,
            DefaultExt = ".csv",
            FileName = $"vault-export-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.csv",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            VaultExportResult result = await _vaultTransferService.ExportBitwardenCsvAsync(
                _allEntries.Select(static entry => entry.Entry),
                dialog.FileName
            );

            string checksumMessage = result.ChecksumFilePath is null
                ? result.IntegritySummary
                : $"{result.IntegritySummary} " + _localizationService.GetString("TransferExportSidecarsMsg");

            SetTransferStatus(_localizationService.GetString("TransferStatusExportReady"), checksumMessage);
            _notificationService.ShowSuccess(
                _localizationService.GetString("NotifSuccess"),
                string.Format(_localizationService.GetString("TransferExportCsvSuccessMsg"), result.ExportedCount)
            );
        }
        catch (Exception exception)
        {
            SetTransferStatus(_localizationService.GetString("TransferStatusFailed"), exception.Message);
            _notificationService.ShowError(
                _localizationService.GetString("TransferStatusFailed"),
                _localizationService.GetString("NotifError")
            );
        }
    }

    [RelayCommand]
    private async Task VerifyTransferFileAsync()
    {
        OpenFileDialog dialog = new()
        {
            Title = _localizationService.GetString("TransferVerifyTitle"),
            Filter = _localizationService.GetString("TransferImportFilter"),
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            VaultVerificationResult result = await _vaultTransferService.VerifyAsync(dialog.FileName);
            SetTransferStatus(
                _localizationService.GetString("TransferStatusIntegrityChecked"),
                $"{result.IntegritySummary}"
            );

            if (result.IntegrityStatus == VaultIntegrityStatus.MissingChecksum)
            {
                _notificationService.ShowInfo(
                    _localizationService.GetString("TransferVerifyIncomplete"),
                    _localizationService.GetString("TransferVerifyNoChecksum")
                );
            }
            else if (result.IntegrityStatus == VaultIntegrityStatus.MissingSignature)
            {
                _notificationService.ShowInfo(
                    _localizationService.GetString("TransferVerifyIncomplete"),
                    _localizationService.GetString("TransferVerifyNoSignature")
                );
            }
            else if (result.IntegrityStatus == VaultIntegrityStatus.UntrustedSignature)
            {
                _notificationService.ShowInfo(
                    _localizationService.GetString("TransferDlgTrustTitle"),
                    _localizationService.GetString("TransferDlgTrustMsg")
                );
            }
            else
            {
                _notificationService.ShowSuccess(
                    _localizationService.GetString("TransferVerifySuccess"),
                    _localizationService.GetString("TransferVerifySuccessMsg")
                );
            }

            string warningSummary = result.Warnings.Count == 0
                ? _localizationService.GetString("TransferImportNoWarnings")
                : string.Join(Environment.NewLine, result.Warnings.Take(6));

            await _dialogService.ShowMessageAsync(
                _localizationService.GetString("TransferVerifySummary"),
                $"{result.IntegritySummary}\n{warningSummary}"
            );
        }
        catch (Exception exception)
        {
            SetTransferStatus(_localizationService.GetString("TransferStatusFailed"), exception.Message);
            _notificationService.ShowError(
                _localizationService.GetString("TransferStatusFailed"),
                _localizationService.GetString("NotifError")
            );
        }
    }

    private async Task LoadEntriesAsync()
    {
        if (!IsVaultUnlocked)
        {
            foreach (VaultEntryItemViewModel entryItem in _allEntries)
            {
                entryItem.Entry.Password.Clear();
            }
            _allEntries.Clear();
            Entries.Clear();
            AvailableTags.Clear();
            AvailableTags.Add(AllTagsLabel);
            SelectedTagFilter = AllTagsLabel;
            Headline = _localizationService.GetString("VaultHeadLocked");
            StatusMessage = _localizationService.GetString("VaultMsgLocked");
            return;
        }

        IReadOnlyList<VaultEntry> entries = await _vaultService.LoadAsync();

        _allEntries.Clear();

        foreach (VaultEntry entry in entries.OrderByDescending(item => item.CreatedUtc))
        {
            _allEntries.Add(new VaultEntryItemViewModel(
                entry,
                CopyEntryAsync,
                DeleteEntryAsync,
                EditTagsAsync,
                CloneEntryAsync,
                EditDetailsAsync,
                _localizationService
            ));
        }

        RefreshAvailableTags();
        ApplyFilters();
    }

    private async Task CopyEntryAsync(VaultEntryItemViewModel item)
    {
        _clipboardService.SetText(item.Password);
        _notificationService.ShowSuccess(
            _localizationService.GetString("NotifCopiedTitle"),
            string.Format(_localizationService.GetString("NotifCopiedMsg"), item.SiteName)
        );
        await Task.CompletedTask;
    }

    private async Task DeleteEntryAsync(VaultEntryItemViewModel item)
    {
        List<VaultEntry> remainingEntries = _allEntries
            .Where(entry => entry.Id != item.Id)
            .Select(entry => entry.Entry)
            .ToList();

        await _vaultService.SaveAsync(remainingEntries);

        VaultEntryItemViewModel? existingItem = _allEntries.FirstOrDefault(entry => entry.Id == item.Id);
        if (existingItem is not null)
        {
            _allEntries.Remove(existingItem);
        }

        RefreshAvailableTags();
        ApplyFilters();

        _notificationService.ShowAction(
            _localizationService.GetString("NotifDeletedTitle"),
            string.Format(_localizationService.GetString("NotifDeletedMsg"), item.SiteName),
            _localizationService.GetString("CommonBtnUndo"),
            () =>
            {
                _allEntries.Add(item);
                RefreshAvailableTags();
                ApplyFilters();
                _ = _vaultService.SaveAsync(_allEntries.Select(static entry => entry.Entry));
            },
            TimeSpan.FromSeconds(4)
        );
    }

    private async Task EditTagsAsync(VaultEntryItemViewModel item)
    {
        string initialValue = item.Tags.Count == 0 ? string.Empty : string.Join(", ", item.Tags);
        string? editedTags = await _dialogService.PromptForTagsAsync(initialValue);

        if (editedTags is null)
        {
            return;
        }

        item.Entry.Tags = ParseTags(editedTags);
        item.RefreshTags();

        await _vaultService.SaveAsync(_allEntries.Select(static entry => entry.Entry));

        RefreshAvailableTags();
        ApplyFilters();

        _notificationService.ShowSuccess(
            _localizationService.GetString("NotifSuccess"),
            string.Format(_localizationService.GetString("VaultTagsUpdated"), item.SiteName)
        );
    }

    private async Task CloneEntryAsync(VaultEntryItemViewModel item)
    {
        VaultEntry clonedEntry = new()
        {
            SiteName = item.SiteName + " (Copy)",
            Password = (char[])item.Entry.Password.Clone(),
            Url = item.Url,
            Tags = [.. item.Tags],
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        List<VaultEntry> all = _allEntries.Select(static entry => entry.Entry).ToList();
        all.Add(clonedEntry);

        await _vaultService.SaveAsync(all.OrderByDescending(entry => entry.CreatedUtc));

        VaultEntryItemViewModel clonedVM = new(
            clonedEntry,
            CopyEntryAsync,
            DeleteEntryAsync,
            EditTagsAsync,
            CloneEntryAsync,
            EditDetailsAsync,
            _localizationService
        );

        _allEntries.Insert(0, clonedVM);
        RefreshAvailableTags();
        ApplyFilters();

        _notificationService.ShowSuccess(
            _localizationService.GetString("NotifSuccess"),
            string.Format(_localizationService.GetString("NotifClonedMsg"), item.SiteName)
        );
    }

    private async Task EditDetailsAsync(VaultEntryItemViewModel item)
    {
        var details = await _dialogService.PromptForSiteDetailsAsync(item.SiteName, item.Url, new string(item.Entry.Password));

        if (details is null)
        {
            return;
        }

        item.Entry.SiteName = details.Value.SiteName;
        item.Entry.Url = details.Value.Url;

        if (details.Value.Password.Length > 0)
        {
            item.Entry.Password = details.Value.Password;
        }

        item.RefreshDetails();

        await _vaultService.SaveAsync(_allEntries.Select(static entry => entry.Entry));

        ApplyFilters();

        _notificationService.ShowSuccess(
            _localizationService.GetString("NotifSuccess"),
            _localizationService.GetString("NotifDetailsUpdatedMsg")
        );
    }

    private void RefreshAvailableTags()
    {
        string currentSelection = SelectedTagFilter;

        AvailableTags.Clear();
        AvailableTags.Add(AllTagsLabel);

        foreach (string tag in _allEntries
                     .SelectMany(static entry => entry.Tags)
                     .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static tag => tag, StringComparer.CurrentCultureIgnoreCase))
        {
            AvailableTags.Add(tag);
        }

        if (string.IsNullOrWhiteSpace(currentSelection) ||
            string.Equals(currentSelection, AllTagsLabel, StringComparison.Ordinal) ||
            !AvailableTags.Any(tag => string.Equals(tag, currentSelection, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedTagFilter = AllTagsLabel;
            return;
        }

        SelectedTagFilter = AvailableTags.First(tag =>
            string.Equals(tag, currentSelection, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyFilters()
    {
        Entries.Clear();

        IEnumerable<VaultEntryItemViewModel> filteredEntries = _allEntries;

        // Apply Tag Filter
        if (!string.IsNullOrWhiteSpace(SelectedTagFilter) && !string.Equals(SelectedTagFilter, AllTagsLabel, StringComparison.Ordinal))
        {
            filteredEntries = filteredEntries.Where(item =>
                item.Tags.Any(tag => string.Equals(tag, SelectedTagFilter, StringComparison.OrdinalIgnoreCase)));
        }

        // Apply Text Search
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string query = SearchQuery.Trim();
            filteredEntries = filteredEntries.Where(item =>
                item.SiteName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (VaultEntryItemViewModel entry in filteredEntries.OrderByDescending(item => item.CreatedUtc))
        {
            Entries.Add(entry);
        }

        UpdateHeader();
    }

    private void UpdateHeader()
    {
        int totalCount = _allEntries.Count;
        int visibleCount = Entries.Count;

        Headline = totalCount == 0
            ? _localizationService.GetString("VaultHeadReady")
            : string.Format(
                _localizationService.GetString(totalCount == 1 ? "VaultHeadCountOne" : "VaultHeadCountMany"),
                totalCount
            );

        if (totalCount == 0)
        {
            StatusMessage = _localizationService.GetString("VaultMsgReady");
            return;
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string query = SearchQuery.Trim();
            StatusMessage = visibleCount == 0
                ? string.Format(_localizationService.GetString("VaultSearchNoMatches"), query)
                : string.Format(_localizationService.GetString("VaultSearchMatches"), visibleCount, query);
            return;
        }

        if (!string.Equals(SelectedTagFilter, AllTagsLabel, StringComparison.Ordinal))
        {
            StatusMessage = visibleCount == 0
                ? string.Format(_localizationService.GetString("VaultFilterNoMatches"), SelectedTagFilter)
                : string.Format(_localizationService.GetString("VaultFilterMatches"), visibleCount, totalCount, SelectedTagFilter);
            return;
        }

        StatusMessage = _localizationService.GetString("VaultSubtitle");
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasEntries));
    }

    private void SetTransferStatus(string headline, string message)
    {
        TransferHeadline = headline;
        TransferMessage = message;
    }

    private static List<string> ParseTags(string value)
    {
        return value
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
