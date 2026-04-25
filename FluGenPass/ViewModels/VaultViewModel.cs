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
    private string _transferHeadline = "Portable transfer";

    [ObservableProperty]
    private string _transferMessage =
        "Export a FluGenPass backup with embedded SHA-256 and ECDSA signature verification, or create a Bitwarden-compatible CSV with .sha256 and .sig.json sidecars. CSV imports currently keep site and password fields.";

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

        Headline = _localizationService.GetString("VaultHeadLocked");
        StatusMessage = _localizationService.GetString("VaultMsgLocked");

        AvailableTags.Add(AllTagsLabel);
        SelectedTagFilter = AllTagsLabel;

        Entries.CollectionChanged += OnEntriesChanged;
        sessionStateService.UnlockStateChanged += (_, isUnlocked) =>
        {
            IsVaultUnlocked = isUnlocked;
            _ = RefreshAsync();
        };
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
            _notificationService.ShowError("Vault locked", "Unlock the vault before importing passwords.");
            return;
        }

        OpenFileDialog dialog = new()
        {
            Title = "Import passwords",
            Filter =
                "Supported files (*.fgpexport.json;*.json;*.csv)|*.fgpexport.json;*.json;*.csv|" +
                "FluGenPass secure export (*.fgpexport.json;*.json)|*.fgpexport.json;*.json|" +
                "CSV files (*.csv)|*.csv",
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
                    "Checksum not found",
                    "The selected CSV file has no .sha256 checksum next to it. Import anyway?",
                    primaryButtonText: "Import anyway",
                    closeButtonText: "Cancel"
                );

                if (!continueImport)
                {
                    SetTransferStatus("Import cancelled", "CSV import was cancelled because checksum verification was unavailable.");
                    return;
                }
            }
            else if (result.IntegrityStatus == VaultIntegrityStatus.MissingSignature)
            {
                bool continueImport = await _dialogService.ConfirmAsync(
                    "Signature not found",
                    "The selected file has no digital signature, or the CSV .sig.json sidecar is missing. Import anyway?",
                    primaryButtonText: "Import anyway",
                    closeButtonText: "Cancel"
                );

                if (!continueImport)
                {
                    SetTransferStatus(
                        _localizationService.GetString("NotifError"),
                        "Import was cancelled because digital signature verification was unavailable."
                    );
                    return;
                }
            }
            else if (result.IntegrityStatus == VaultIntegrityStatus.UntrustedSignature)
            {
                bool continueImport = await _dialogService.ConfirmAsync(
                    "Unknown signer",
                    "The file signature is cryptographically valid, but the signer is not trusted by this FluGenPass instance. Import anyway?",
                    primaryButtonText: "Import anyway",
                    closeButtonText: "Cancel"
                );

                if (!continueImport)
                {
                    SetTransferStatus("Import cancelled", "Import was cancelled because the signer is not trusted locally.");
                    return;
                }
            }

            await _vaultService.SaveAsync(result.Entries);
            await LoadEntriesAsync();

            string warningSummary = result.Warnings.Count == 0
                ? "No import warnings."
                : string.Join(Environment.NewLine, result.Warnings.Take(6));

            SetTransferStatus(
                "Import completed",
                $"{result.ImportedCount} item(s) added, {result.SkippedCount} skipped. {result.IntegritySummary}"
            );

            _notificationService.ShowSuccess(
                _localizationService.GetString("NotifSuccess"),
                $"{result.ImportedCount} password(s) imported."
            );
            await _dialogService.ShowMessageAsync(
                "Import summary",
                $"{result.ImportedCount} password(s) imported.\n" +
                $"{result.SkippedCount} duplicate or invalid row(s) skipped.\n" +
                $"{result.IntegritySummary}\n" +
                warningSummary
            );
        }
        catch (Exception exception)
        {
            SetTransferStatus("Import failed", exception.Message);
            _notificationService.ShowError("Import failed", "The selected file could not be imported.");
        }
    }

    [RelayCommand]
    private async Task ExportSecureAsync()
    {
        if (!IsVaultUnlocked)
        {
            _notificationService.ShowError("Vault locked", "Unlock the vault before exporting passwords.");
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Export secure backup",
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

            SetTransferStatus("Secure export ready", result.IntegritySummary);
            _notificationService.ShowSuccess("Export completed", $"{result.ExportedCount} password(s) exported.");
        }
        catch (Exception exception)
        {
            SetTransferStatus("Export failed", exception.Message);
            _notificationService.ShowError("Export failed", "The secure export file could not be created.");
        }
    }

    [RelayCommand]
    private async Task ExportBitwardenCsvAsync()
    {
        if (!IsVaultUnlocked)
        {
            _notificationService.ShowError("Vault locked", "Unlock the vault before exporting passwords.");
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Export Bitwarden CSV",
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
                : $"{result.IntegritySummary} Sidecars saved next to the CSV file.";

            SetTransferStatus("CSV export ready", checksumMessage);
            _notificationService.ShowSuccess("Export completed", $"{result.ExportedCount} password(s) exported to CSV.");
        }
        catch (Exception exception)
        {
            SetTransferStatus("Export failed", exception.Message);
            _notificationService.ShowError("Export failed", "The CSV export file could not be created.");
        }
    }

    [RelayCommand]
    private async Task VerifyTransferFileAsync()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Verify transfer file",
            Filter =
                "Transfer files (*.fgpexport.json;*.json;*.csv)|*.fgpexport.json;*.json;*.csv|" +
                "FluGenPass secure export (*.fgpexport.json;*.json)|*.fgpexport.json;*.json|" +
                "CSV files (*.csv)|*.csv",
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
            SetTransferStatus("Integrity checked", $"{result.IntegritySummary} Entries detected: {result.EntryCount}.");

            if (result.IntegrityStatus == VaultIntegrityStatus.MissingChecksum)
            {
                _notificationService.ShowInfo("Verification incomplete", "Checksum file was not found for the selected CSV.");
            }
            else if (result.IntegrityStatus == VaultIntegrityStatus.MissingSignature)
            {
                _notificationService.ShowInfo("Verification incomplete", "Digital signature was not found for the selected file.");
            }
            else if (result.IntegrityStatus == VaultIntegrityStatus.UntrustedSignature)
            {
                _notificationService.ShowInfo("Unknown signer", "The signature is valid, but the signer is not trusted locally.");
            }
            else
            {
                _notificationService.ShowSuccess("Verification passed", "The selected file passed integrity verification.");
            }

            string warningSummary = result.Warnings.Count == 0
                ? "No warnings."
                : string.Join(Environment.NewLine, result.Warnings.Take(6));

            await _dialogService.ShowMessageAsync(
                "Verification summary",
                $"{result.IntegritySummary}\nEntries detected: {result.EntryCount}\n{warningSummary}"
            );
        }
        catch (Exception exception)
        {
            SetTransferStatus("Verification failed", exception.Message);
            _notificationService.ShowError("Verification failed", "The selected file did not pass integrity verification.");
        }
    }

    private async Task LoadEntriesAsync()
    {
        if (!IsVaultUnlocked)
        {
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
            _allEntries.Add(new VaultEntryItemViewModel(entry, CopyEntryAsync, DeleteEntryAsync, EditTagsAsync, _localizationService));
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

        _notificationService.ShowInfo(
            _localizationService.GetString("NotifDeletedTitle"),
            string.Format(_localizationService.GetString("NotifDeletedMsg"), item.SiteName)
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
