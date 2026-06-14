using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace FluGenPass.Services;

public sealed class PwnedPasswordService : IPwnedPasswordService, IDisposable
{
    private readonly HttpClient _httpClient;

    public PwnedPasswordService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public PwnedPasswordService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<int> GetPwnCountAsync(string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(password))
        {
            return 0;
        }

        // 1. Calculate SHA-1 hash of the password
        byte[] inputBytes = Encoding.UTF8.GetBytes(password);
        byte[] hashBytes = SHA1.HashData(inputBytes);
        string hashHex = Convert.ToHexString(hashBytes);

        string prefix = hashHex[..5];
        string suffix = hashHex[5..];

        // 2. Query HIBP range API
        string url = $"https://api.pwnedpasswords.com/range/{prefix}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "FluGenPass-PasswordManager");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        // 3. Search response for matching suffix
        return ParseResponse(responseContent, suffix);
    }

    private static int ParseResponse(string content, string suffix)
    {
        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            int colonIndex = line.IndexOf(':');
            if (colonIndex != -1)
            {
                string lineSuffix = line.Substring(0, colonIndex);
                if (lineSuffix.Equals(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(line.Substring(colonIndex + 1), out int count))
                    {
                        return count;
                    }
                }
            }
        }
        return 0;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
