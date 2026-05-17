using FluGenPass.Models;
using FluGenPass.Services;

namespace FluGenPass.Tests;

public sealed class VaultTransferServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"FluGenPassTransferTests-{Guid.NewGuid():N}");
    private VaultTransferService CreateService(string? directory = null)
    {
        string targetDirectory = directory ?? _tempDirectory;
        return new VaultTransferService(new TransferSignatureService(targetDirectory));
    }

    [Fact]
    public async Task ExportSecureAsync_ProducesVerifiablePackageAndRoundTripsImport()
    {
        Directory.CreateDirectory(_tempDirectory);
        string filePath = Path.Combine(_tempDirectory, "vault.fgpexport.json");
        VaultTransferService service = CreateService();

        VaultEntry entry = new()
        {
            SiteName = "example.com",
            Password = "Secret!123".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        VaultExportResult exportResult = await service.ExportSecureAsync([entry], filePath);
        VaultVerificationResult verificationResult = await service.VerifyAsync(filePath);
        VaultImportResult importResult = await service.ImportAsync(filePath, []);

        Assert.Equal(VaultTransferFormat.FluGenPassSecureExport, exportResult.Format);
        Assert.Equal(VaultIntegrityStatus.Verified, verificationResult.IntegrityStatus);
        Assert.Contains("trusted ECDSA signature", verificationResult.IntegritySummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, importResult.ImportedCount);

        VaultEntry importedEntry = Assert.Single(importResult.Entries);
        Assert.Equal(entry.SiteName, importedEntry.SiteName);
        Assert.Equal(entry.Password, importedEntry.Password);
    }

    [Fact]
    public async Task ExportBitwardenCsvAsync_CreatesChecksumAndImportsEntries()
    {
        Directory.CreateDirectory(_tempDirectory);
        string filePath = Path.Combine(_tempDirectory, "vault.csv");
        VaultTransferService service = CreateService();

        VaultEntry entry = new()
        {
            SiteName = "bitwarden.example",
            Password = "CsvSecret!456".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        VaultExportResult exportResult = await service.ExportBitwardenCsvAsync([entry], filePath);
        VaultVerificationResult verificationResult = await service.VerifyAsync(filePath);
        VaultImportResult importResult = await service.ImportAsync(filePath, []);

        Assert.NotNull(exportResult.ChecksumFilePath);
        Assert.NotNull(exportResult.SignatureFilePath);
        Assert.True(File.Exists(exportResult.ChecksumFilePath));
        Assert.True(File.Exists(exportResult.SignatureFilePath));
        Assert.Equal(VaultIntegrityStatus.Verified, verificationResult.IntegrityStatus);
        Assert.Equal(1, importResult.ImportedCount);

        VaultEntry importedEntry = Assert.Single(importResult.Entries);
        Assert.Equal(entry.SiteName, importedEntry.SiteName);
        Assert.Equal(entry.Password, importedEntry.Password);
    }

    [Fact]
    public async Task VerifyAsync_ForCsvWithoutChecksum_ReturnsMissingChecksum()
    {
        Directory.CreateDirectory(_tempDirectory);
        string filePath = Path.Combine(_tempDirectory, "manual.csv");
        VaultTransferService service = CreateService();

        await File.WriteAllTextAsync(
            filePath,
            "folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp\r\n" +
            ",false,login,manual.example,,,0,,,Password!789,\r\n"
        );

        VaultVerificationResult result = await service.VerifyAsync(filePath);

        Assert.Equal(VaultTransferFormat.BitwardenCsv, result.Format);
        Assert.Equal(VaultIntegrityStatus.MissingChecksum, result.IntegrityStatus);
        Assert.Equal(1, result.EntryCount);
    }

    [Fact]
    public async Task ImportAsync_SkipsDuplicateSiteAndPasswordPairs()
    {
        Directory.CreateDirectory(_tempDirectory);
        string filePath = Path.Combine(_tempDirectory, "duplicates.fgpexport.json");
        VaultTransferService service = CreateService();

        VaultEntry existingEntry = new()
        {
            SiteName = "dup.example",
            Password = "KeepMe!123".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await service.ExportSecureAsync([existingEntry], filePath);
        VaultImportResult result = await service.ImportAsync(filePath, [existingEntry]);

        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Single(result.Entries);
    }

    [Fact]
    public async Task VerifyAsync_FailsWhenSecureExportPayloadIsTampered()
    {
        Directory.CreateDirectory(_tempDirectory);
        string filePath = Path.Combine(_tempDirectory, "tampered.fgpexport.json");
        VaultTransferService service = CreateService();

        VaultEntry entry = new()
        {
            SiteName = "tampered.example",
            Password = "Original!123".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await service.ExportSecureAsync([entry], filePath);

        string contents = await File.ReadAllTextAsync(filePath);
        contents = contents.Replace("Original!123", "Changed!456", StringComparison.Ordinal);
        await File.WriteAllTextAsync(filePath, contents);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.VerifyAsync(filePath));
    }

    [Fact]
    public async Task VerifyAsync_ReturnsUntrustedSignatureWhenSignerDiffersFromLocalKey()
    {
        string exportDirectory = Path.Combine(_tempDirectory, "exporter");
        string verifyDirectory = Path.Combine(_tempDirectory, "verifier");
        Directory.CreateDirectory(exportDirectory);
        Directory.CreateDirectory(verifyDirectory);

        string filePath = Path.Combine(exportDirectory, "trusted-elsewhere.fgpexport.json");
        VaultTransferService exportService = CreateService(exportDirectory);
        VaultTransferService verifyService = CreateService(verifyDirectory);

        VaultEntry entry = new()
        {
            SiteName = "cross-device.example",
            Password = "Signed!321".ToCharArray(),
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await exportService.ExportSecureAsync([entry], filePath);
        VaultVerificationResult result = await verifyService.VerifyAsync(filePath);

        Assert.Equal(VaultIntegrityStatus.UntrustedSignature, result.IntegrityStatus);
        Assert.Single(result.Warnings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }
}
