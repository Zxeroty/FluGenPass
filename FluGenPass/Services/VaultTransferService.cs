using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration.Attributes;
using FluGenPass.Models;

namespace FluGenPass.Services;

public sealed class VaultTransferService(ITransferSignatureService transferSignatureService, ILocalizationService? localizationService = null) : IVaultTransferService
{
    private const string SecureExportFormatName = "FluGenPass.Export";
    private const string DetachedSignatureFormatName = "FluGenPass.DetachedSignature";

    private string GetString(string key, string fallback)
    {
        return localizationService?.GetString(key) ?? fallback;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public async Task<VaultExportResult> ExportSecureAsync(
        IEnumerable<VaultEntry> entries,
        string filePath,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        List<VaultEntry> clonedEntries = CloneEntries(entries);
        VaultTransferPayload payload = new()
        {
            ExportedUtc = DateTimeOffset.UtcNow,
            Entries = clonedEntries,
        };

        byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
        try
        {
            string payloadHashHex = Convert.ToHexString(SHA256.HashData(payloadBytes));
            VaultTransferSignature signature = transferSignatureService.CreateSignature(payloadBytes);

            VaultTransferPackage package = new()
            {
                Format = SecureExportFormatName,
                Version = 1,
                Payload = payload,
                Integrity = new VaultTransferIntegrity
                {
                    Algorithm = "SHA-256",
                    PayloadHashHex = payloadHashHex,
                },
                Signature = signature,
            };

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);

            await using (FileStream stream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, package, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }

            return new VaultExportResult(
                filePath,
                VaultTransferFormat.FluGenPassSecureExport,
                clonedEntries.Count,
                VaultIntegrityStatus.Verified,
                string.Format(GetString("VerifyMsgSecureExportCreated", "Embedded SHA-256 and trusted ECDSA signature created. Signer key: {0}."), signature.SignerKeyId)
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payloadBytes);
        }
    }

    public async Task<VaultExportResult> ExportBitwardenCsvAsync(
        IEnumerable<VaultEntry> entries,
        string filePath,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        List<VaultEntry> clonedEntries = CloneEntries(entries);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);

        await using (FileStream stream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (StreamWriter writer = new(stream, new UTF8Encoding(false)))
        await using (CsvWriter csv = new(writer, CultureInfo.InvariantCulture))
        {
            await csv.WriteRecordsAsync(clonedEntries.Select(ToBitwardenRecord), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            stream.Flush(true);
        }

        string checksumPath = $"{filePath}.sha256";
        string signaturePath = GetSignaturePath(filePath);
        byte[] csvBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);

        try
        {
            string hashHex = Convert.ToHexString(SHA256.HashData(csvBytes));
            string checksumContents = $"{hashHex} *{Path.GetFileName(filePath)}{Environment.NewLine}";
            await File.WriteAllTextAsync(checksumPath, checksumContents, Encoding.ASCII, cancellationToken);

            VaultDetachedSignaturePackage detachedSignature = new()
            {
                Format = DetachedSignatureFormatName,
                Version = 1,
                FileName = Path.GetFileName(filePath),
                IntegrityAlgorithm = "SHA-256",
                PayloadHashHex = hashHex,
                Signature = transferSignatureService.CreateSignature(csvBytes),
            };

            await using (FileStream signatureStream = new(signaturePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(signatureStream, detachedSignature, SerializerOptions, cancellationToken);
                await signatureStream.FlushAsync(cancellationToken);
                signatureStream.Flush(true);
            }

            return new VaultExportResult(
                filePath,
                VaultTransferFormat.BitwardenCsv,
                clonedEntries.Count,
                VaultIntegrityStatus.Verified,
                string.Format(GetString("VerifyMsgCsvExportCreated", "SHA-256 and ECDSA sidecars created. Signer key: {0}."), detachedSignature.Signature.SignerKeyId),
                checksumPath,
                signaturePath
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(csvBytes);
        }
    }

    public async Task<VaultImportResult> ImportAsync(
        string filePath,
        IEnumerable<VaultEntry> existingEntries,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(existingEntries);

        string extension = Path.GetExtension(filePath);

        return extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
            ? await ImportBitwardenCsvAsync(filePath, existingEntries, cancellationToken)
            : await ImportSecureAsync(filePath, existingEntries, cancellationToken);
    }

    public async Task<VaultVerificationResult> VerifyAsync(
        string filePath,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string extension = Path.GetExtension(filePath);

        return extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
            ? await VerifyBitwardenCsvAsync(filePath, cancellationToken)
            : await VerifySecureAsync(filePath, cancellationToken);
    }

    private async Task<VaultImportResult> ImportSecureAsync(
        string filePath,
        IEnumerable<VaultEntry> existingEntries,
        CancellationToken cancellationToken
    )
    {
        List<VaultEntry> existingEntryList = CloneEntries(existingEntries);
        VaultVerificationResult verification = await VerifySecureAsync(filePath, cancellationToken);

        await using FileStream stream = File.OpenRead(filePath);
        VaultTransferPackage? package =
            await JsonSerializer.DeserializeAsync<VaultTransferPackage>(stream, SerializerOptions, cancellationToken);

        if (package?.Payload is null)
        {
            throw new InvalidDataException("Secure export payload is missing.");
        }

        IReadOnlyList<VaultEntry> mergedEntries = MergeEntries(existingEntryList, package.Payload.Entries, out int skippedCount);

        return new VaultImportResult(
            VaultTransferFormat.FluGenPassSecureExport,
            mergedEntries.Count - existingEntryList.Count,
            skippedCount,
            verification.IntegrityStatus,
            verification.IntegritySummary,
            mergedEntries,
            verification.Warnings
        );
    }

    private async Task<VaultImportResult> ImportBitwardenCsvAsync(
        string filePath,
        IEnumerable<VaultEntry> existingEntries,
        CancellationToken cancellationToken
    )
    {
        List<VaultEntry> existingEntryList = CloneEntries(existingEntries);
        VaultVerificationResult verification = await VerifyBitwardenCsvAsync(filePath, cancellationToken);
        List<string> warnings = verification.Warnings.ToList();
        List<VaultEntry> importedEntries = [];
        int ignoredMetadataRows = 0;

        using FileStream stream = File.OpenRead(filePath);
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using CsvReader csv = new(reader, CultureInfo.InvariantCulture);

        if (!await csv.ReadAsync())
        {
            return new VaultImportResult(
                VaultTransferFormat.BitwardenCsv,
                0,
                0,
                verification.IntegrityStatus,
                verification.IntegritySummary,
                existingEntryList,
                warnings
            );
        }

        csv.ReadHeader();

        int rowNumber = 1;
        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;

            string siteName = GetFirstNonEmptyField(csv, "name", "site", "site_name", "login_uri");
            string password = GetFirstNonEmptyField(csv, "login_password", "password");
            string username = GetFirstNonEmptyField(csv, "login_username", "username");
            string uri = GetFirstNonEmptyField(csv, "login_uri", "uri", "website");
            string notes = GetFirstNonEmptyField(csv, "notes");
            string totp = GetFirstNonEmptyField(csv, "login_totp", "totp");

            if (string.IsNullOrWhiteSpace(siteName) || string.IsNullOrWhiteSpace(password))
            {
                warnings.Add($"Row {rowNumber}: skipped because required fields are missing.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(username)
                || !string.IsNullOrWhiteSpace(uri)
                || !string.IsNullOrWhiteSpace(notes)
                || !string.IsNullOrWhiteSpace(totp))
            {
                ignoredMetadataRows++;
            }

            importedEntries.Add(new VaultEntry
            {
                SiteName = siteName.Trim(),
                Password = password.ToCharArray(),
                CreatedUtc = DateTimeOffset.UtcNow.AddTicks(rowNumber),
            });
        }

        if (ignoredMetadataRows > 0)
        {
            warnings.Add(
                $"{ignoredMetadataRows} row(s) contained username, URI, notes, or TOTP data that FluGenPass does not store yet."
            );
        }

        IReadOnlyList<VaultEntry> mergedEntries = MergeEntries(existingEntryList, importedEntries, out int skippedCount);
        skippedCount += warnings.Count(static warning => warning.Contains("skipped", StringComparison.OrdinalIgnoreCase));

        return new VaultImportResult(
            VaultTransferFormat.BitwardenCsv,
            mergedEntries.Count - existingEntryList.Count,
            skippedCount,
            verification.IntegrityStatus,
            verification.IntegritySummary,
            mergedEntries,
            warnings
        );
    }

    private async Task<VaultVerificationResult> VerifySecureAsync(
        string filePath,
        CancellationToken cancellationToken
    )
    {
        await using FileStream stream = File.OpenRead(filePath);
        VaultTransferPackage? package =
            await JsonSerializer.DeserializeAsync<VaultTransferPackage>(stream, SerializerOptions, cancellationToken);

        if (package is null)
        {
            throw new InvalidDataException("The selected file is empty or not valid JSON.");
        }

        if (!string.Equals(package.Format, SecureExportFormatName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The selected JSON file is not a FluGenPass secure export.");
        }

        if (package.Payload is null)
        {
            throw new InvalidDataException("Secure export payload is missing.");
        }

        if (package.Integrity is null || string.IsNullOrWhiteSpace(package.Integrity.PayloadHashHex))
        {
            throw new InvalidDataException("Secure export integrity metadata is missing.");
        }

        byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(package.Payload, SerializerOptions);
        try
        {
            string actualHashHex = Convert.ToHexString(SHA256.HashData(payloadBytes));

            if (!string.Equals(package.Integrity.PayloadHashHex, actualHashHex, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Integrity verification failed: the embedded SHA-256 hash does not match the payload.");
            }

            VaultIntegrityStatus signatureStatus = transferSignatureService.VerifySignature(
                payloadBytes,
                package.Signature,
                out string signatureSummary,
                out IReadOnlyList<string> signatureWarnings
            );

            string integritySummary = string.Format(GetString("VerifyMsgSecureEmbeddedVerified", "Embedded SHA-256 verified: {0}. {1}"), actualHashHex, signatureSummary);

            return new VaultVerificationResult(
                filePath,
                VaultTransferFormat.FluGenPassSecureExport,
                signatureStatus,
                integritySummary,
                package.Payload.Entries.Count,
                signatureWarnings
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payloadBytes);
        }
    }

    private async Task<VaultVerificationResult> VerifyBitwardenCsvAsync(
        string filePath,
        CancellationToken cancellationToken
    )
    {
        string checksumPath = $"{filePath}.sha256";
        string signaturePath = GetSignaturePath(filePath);
        List<string> warnings = [];

        int entryCount = await CountBitwardenCsvRowsAsync(filePath, cancellationToken);
        byte[] fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);

        try
        {
            string actualHash = Convert.ToHexString(SHA256.HashData(fileBytes));
            bool checksumVerified = false;
            string checksumSummary;

            if (File.Exists(checksumPath))
            {
                string expectedHash = await ReadChecksumHashAsync(checksumPath, cancellationToken);

                if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Integrity verification failed: the CSV checksum does not match.");
                }

                checksumVerified = true;
                checksumSummary = string.Format(GetString("VerifyMsgCsvHashVerified", "CSV SHA-256 verified: {0}."), actualHash);
            }
            else
            {
                checksumSummary = GetString("VerifyMsgCsvNoChecksum", "CSV SHA-256 sidecar is missing.");
                warnings.Add(GetString("VerifyMsgCsvNoChecksumWarning", "Checksum sidecar was not found next to the CSV file."));
            }

            VaultIntegrityStatus signatureStatus;
            string signatureSummary;
            IReadOnlyList<string> signatureWarnings;

            if (File.Exists(signaturePath))
            {
                await using FileStream signatureStream = File.OpenRead(signaturePath);
                VaultDetachedSignaturePackage? detachedSignature =
                    await JsonSerializer.DeserializeAsync<VaultDetachedSignaturePackage>(signatureStream, SerializerOptions, cancellationToken);

                if (detachedSignature is null)
                {
                    throw new InvalidDataException("Signature sidecar is empty or invalid.");
                }

                if (!string.Equals(detachedSignature.Format, DetachedSignatureFormatName, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The detached signature file is not a FluGenPass signature package.");
                }

                if (!string.Equals(detachedSignature.FileName, Path.GetFileName(filePath), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The detached signature file does not belong to the selected CSV file.");
                }

                if (!string.Equals(detachedSignature.PayloadHashHex, actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Integrity verification failed: the signature sidecar hash does not match the CSV file.");
                }

                signatureStatus = transferSignatureService.VerifySignature(
                    fileBytes,
                    detachedSignature.Signature,
                    out signatureSummary,
                    out signatureWarnings
                );
            }
            else
            {
                signatureStatus = VaultIntegrityStatus.MissingSignature;
                signatureSummary = GetString("VerifyMsgCsvNoSignature", "Digital signature sidecar is missing.");
                signatureWarnings = [GetString("VerifyMsgCsvNoSignatureWarning", "The .sig.json signature file was not found next to the CSV file.")];
            }

            warnings.AddRange(signatureWarnings);

            VaultIntegrityStatus combinedStatus = DetermineCsvStatus(checksumVerified, signatureStatus);

            return new VaultVerificationResult(
                filePath,
                VaultTransferFormat.BitwardenCsv,
                combinedStatus,
                $"{checksumSummary} {signatureSummary}",
                entryCount,
                warnings
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileBytes);
        }
    }

    private static async Task<int> CountBitwardenCsvRowsAsync(string filePath, CancellationToken cancellationToken)
    {
        int rowCount = 0;

        using FileStream stream = File.OpenRead(filePath);
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using CsvReader csv = new(reader, CultureInfo.InvariantCulture);

        if (!await csv.ReadAsync())
        {
            return 0;
        }

        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowCount++;
        }

        return rowCount;
    }

    private static async Task<string> ReadChecksumHashAsync(string checksumPath, CancellationToken cancellationToken)
    {
        string checksumContents = await File.ReadAllTextAsync(checksumPath, Encoding.ASCII, cancellationToken);
        string firstToken = checksumContents
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(firstToken))
        {
            throw new InvalidDataException("Checksum file is empty.");
        }

        return firstToken.Trim();
    }

    private static string GetSignaturePath(string filePath)
    {
        return $"{filePath}.sig.json";
    }

    private static VaultIntegrityStatus DetermineCsvStatus(bool checksumVerified, VaultIntegrityStatus signatureStatus)
    {
        if (signatureStatus == VaultIntegrityStatus.UntrustedSignature)
        {
            return VaultIntegrityStatus.UntrustedSignature;
        }

        if (!checksumVerified)
        {
            return VaultIntegrityStatus.MissingChecksum;
        }

        if (signatureStatus == VaultIntegrityStatus.MissingSignature)
        {
            return VaultIntegrityStatus.MissingSignature;
        }

        return VaultIntegrityStatus.Verified;
    }

    private static IReadOnlyList<VaultEntry> MergeEntries(
        IEnumerable<VaultEntry> existingEntries,
        IEnumerable<VaultEntry> importedEntries,
        out int skippedCount
    )
    {
        List<VaultEntry> mergedEntries = CloneEntries(existingEntries);
        HashSet<string> knownEntries = mergedEntries
            .Select(CreateDuplicateKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        skippedCount = 0;

        foreach (VaultEntry entry in importedEntries)
        {
            string duplicateKey = CreateDuplicateKey(entry);

            if (!knownEntries.Add(duplicateKey))
            {
                skippedCount++;
                continue;
            }

            mergedEntries.Add(CloneEntry(entry));
        }

        return mergedEntries
            .OrderByDescending(static entry => entry.CreatedUtc)
            .ToList();
    }

    private static string CreateDuplicateKey(VaultEntry entry)
    {
        return $"{entry.SiteName.Trim()}::{new string(entry.Password)}";
    }

    private static List<VaultEntry> CloneEntries(IEnumerable<VaultEntry> entries)
    {
        return entries.Select(CloneEntry).ToList();
    }

    private static VaultEntry CloneEntry(VaultEntry entry)
    {
        return new VaultEntry
        {
            Id = entry.Id,
            SiteName = entry.SiteName,
            Password = (char[])entry.Password.Clone(),
            Tags = [.. entry.Tags],
            CreatedUtc = entry.CreatedUtc,
        };
    }

    private static string GetFirstNonEmptyField(CsvReader csv, params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (!csv.TryGetField(candidate, out string? value) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            return value;
        }

        return string.Empty;
    }

    private static BitwardenCsvRecord ToBitwardenRecord(VaultEntry entry)
    {
        return new BitwardenCsvRecord
        {
            Type = "login",
            Name = entry.SiteName,
            LoginPassword = new string(entry.Password),
        };
    }

    private sealed class BitwardenCsvRecord
    {
        [Name("folder")]
        public string Folder { get; set; } = string.Empty;

        [Name("favorite")]
        public bool Favorite { get; set; }

        [Name("type")]
        public string Type { get; set; } = "login";

        [Name("name")]
        public string Name { get; set; } = string.Empty;

        [Name("notes")]
        public string Notes { get; set; } = string.Empty;

        [Name("fields")]
        public string Fields { get; set; } = string.Empty;

        [Name("reprompt")]
        public int Reprompt { get; set; }

        [Name("login_uri")]
        public string LoginUri { get; set; } = string.Empty;

        [Name("login_username")]
        public string LoginUsername { get; set; } = string.Empty;

        [Name("login_password")]
        public string LoginPassword { get; set; } = string.Empty;

        [Name("login_totp")]
        public string LoginTotp { get; set; } = string.Empty;
    }
}
