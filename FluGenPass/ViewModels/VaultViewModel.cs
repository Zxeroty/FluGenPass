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

    [ObservableProperty]
    private bool _isVaultUnlocked;

    [ObservableProperty]
    private string _headline = "Vault locked";

    [ObservableProperty]
    private string _statusMessage = "Unlock the vault to browse saved passwords.";

    [ObservableProperty]
    private string _transferHeadline = "Portable transfer";

    [ObservableProperty]
    private string _transferMessage =
        "Export a FluGenPass backup with embedded SHA-256 and ECDSA signature verification, or create a Bitwarden-compatible CSV with .sha256 and .sig.json sidecars. CSV imports currently keep site and password fields.";

    public VaultViewModel(
        IVaultService vaultService,
        IClipboardService clipboardService,
        INotificationService notificationService,
        IMasterPasswordService masterPasswordService,
        IVaultTransferService vaultTransferService,
        IDialogService dialogService,
        ISessionStateService sessionStateService
    )
    {
        _vaultService = vaultService;
        _clipboardService = clipboardService;
        _notificationService = notificationService;
        _masterPasswordService = masterPasswordService;
        _vaultTransferService = vaultTransferService;
        _dialogService = dialogService;
        IsVaultUnlocked = sessionStateService.IsUnlocked;

        Entries.CollectionChanged += OnEntriesChanged;
        sessionStateService.UnlockStateChanged += (_, isUnlocked) =>
        {
            IsVaultUnlocked = isUnlocked;
            _ = RefreshAsync();
        };
    }

    public ObservableCollection<VaultEntryItemViewModel> Entries { get; } = [];

    public bool HasEntries => Entries.Count > 0;

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
        _notificationService.ShowInfo("Vault locked", "The vault is now hidden until you unlock it again.");
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
            IReadOnlyList<VaultEntry> currentEntries = Entries.Select(static entry => entry.Entry).ToList();
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
                    SetTransferStatus("Import cancelled", "Import was cancelled because digital signature verification was unavailable.");
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

            _notificationService.ShowSuccess("Import completed", $"{result.ImportedCount} password(s) imported.");
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
                Entries.Select(static entry => entry.Entry),
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
                Entries.Select(static entry => entry.Entry),
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
            Entries.Clear();
            Headline = "Vault locked";
            StatusMessage = "Unlock the vault to browse saved passwords.";
            return;
        }

        IReadOnlyList<VaultEntry> entries = await _vaultService.LoadAsync();

        Entries.Clear();

        foreach (VaultEntry entry in entries.OrderByDescending(item => item.CreatedUtc))
        {
            Entries.Add(new VaultEntryItemViewModel(entry, CopyEntryAsync, DeleteEntryAsync));
        }

        Headline = Entries.Count == 0 ? "Vault ready" : $"{Entries.Count} secret{(Entries.Count == 1 ? string.Empty : "s")} stored";
        StatusMessage = Entries.Count == 0
            ? "Passwords saved from the generator appear here."
            : "Reveal, copy, or delete any entry below.";
    }

    private async Task CopyEntryAsync(VaultEntryItemViewModel item)
    {
        _clipboardService.SetText(item.Password);
        _notificationService.ShowSuccess("Copied", $"{item.SiteName} copied to the clipboard.");
        await Task.CompletedTask;
    }

    private async Task DeleteEntryAsync(VaultEntryItemViewModel item)
    {
        List<VaultEntry> remainingEntries = Entries
            .Where(entry => entry.Id != item.Id)
            .Select(entry => entry.Entry)
            .ToList();

        await _vaultService.SaveAsync(remainingEntries);
        Entries.Remove(item);
        _notificationService.ShowInfo("Deleted", $"{item.SiteName} was removed from the vault.");

        if (Entries.Count == 0)
        {
            Headline = "Vault ready";
            StatusMessage = "Passwords saved from the generator appear here.";
        }
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
}
