using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluGenPass.Models;

namespace FluGenPass.Services;

public sealed class SettingsService(string appDirectory) : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile AppSettings? _cachedSettings;

    public string SettingsFilePath { get; } = Path.Combine(appDirectory, "settings.json");

    public async Task<AppSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: return a deep copy from cache without acquiring the lock
        AppSettings? snapshot = _cachedSettings;
        if (snapshot is not null)
        {
            return DeepCopy(snapshot);
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            // Double-check after acquiring the lock
            if (_cachedSettings is not null)
            {
                return DeepCopy(_cachedSettings);
            }

            if (!File.Exists(SettingsFilePath))
            {
                _cachedSettings = new AppSettings();
                return DeepCopy(_cachedSettings);
            }

            await using FileStream stream = File.OpenRead(SettingsFilePath);
            _cachedSettings =
                await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken)
                ?? new AppSettings();

            return DeepCopy(_cachedSettings);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(appDirectory);

            _cachedSettings = DeepCopy(settings);

            await using FileStream stream = File.Create(SettingsFilePath);
            await JsonSerializer.SerializeAsync(stream, _cachedSettings, SerializerOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static AppSettings DeepCopy(AppSettings source)
    {
        return source with
        {
            MasterPassword = source.MasterPassword is null ? null : source.MasterPassword with { },
            KeyFile = source.KeyFile is null ? null : source.KeyFile with { },
        };
    }
}