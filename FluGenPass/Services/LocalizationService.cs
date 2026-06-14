using System.Windows;
using FluGenPass.Models;

namespace FluGenPass.Services;

public sealed class LocalizationService(ISettingsService settingsService) : ILocalizationService
{
    public event EventHandler? LanguageChanged;

    public AppLanguageOption CurrentLanguage { get; private set; } = AppLanguageOption.English;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetAsync(cancellationToken);
        await ApplyLanguageAsync(settings.Language, cancellationToken);
    }

    public async Task ApplyLanguageAsync(AppLanguageOption language, CancellationToken cancellationToken = default)
    {
        CurrentLanguage = language;

        var dict = new ResourceDictionary
        {
            Source = new Uri($"/FluGenPass;component/Resources/Strings.{GetLanguageCode(language)}.xaml", UriKind.Relative)
        };

        var oldDict = Application.Current.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Resources/Strings."));

        if (oldDict != null)
        {
            Application.Current.Resources.MergedDictionaries.Remove(oldDict);
        }

        Application.Current.Resources.MergedDictionaries.Add(dict);

        LanguageChanged?.Invoke(this, EventArgs.Empty);

        var settings = await settingsService.GetAsync(cancellationToken);
        if (settings.Language != language)
        {
            settings.Language = language;
            await settingsService.SaveAsync(settings, cancellationToken);
        }
    }

    public string GetString(string key)
    {
        return Application.Current.Resources[key] as string ?? key;
    }

    private static string GetLanguageCode(AppLanguageOption language)
    {
        return language switch
        {
            AppLanguageOption.Russian => "ru",
            _ => "en"
        };
    }
}
