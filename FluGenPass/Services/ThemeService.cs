using System.Windows;
using FluGenPass.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FluGenPass.Services;

public sealed class ThemeService(ISettingsService settingsService) : IThemeService
{
    private Window? _window;

    public AppThemeOption CurrentTheme { get; private set; } = AppThemeOption.System;

    public async Task InitializeAsync(Window window, CancellationToken cancellationToken = default)
    {
        _window = window;

        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        CurrentTheme = settings.Theme;

        ApplyThemeToWindow(CurrentTheme);
    }

    public async Task ApplyThemeAsync(AppThemeOption theme, CancellationToken cancellationToken = default)
    {
        CurrentTheme = theme;
        ApplyThemeToWindow(theme);

        AppSettings settings = await settingsService.GetAsync(cancellationToken);
        settings.Theme = theme;
        await settingsService.SaveAsync(settings, cancellationToken);
    }

    private void ApplyThemeToWindow(AppThemeOption theme)
    {
        if (_window is null)
        {
            return;
        }

        if (theme == AppThemeOption.System)
        {
            SystemTheme systemTheme = ApplicationThemeManager.GetSystemTheme();
            ApplicationTheme applicationTheme = systemTheme switch
            {
                SystemTheme.Dark => ApplicationTheme.Dark,
                _ => ApplicationTheme.Light,
            };

            ApplicationThemeManager.Apply(applicationTheme, WindowBackdropType.Mica, true);
            SystemThemeWatcher.Watch(_window, WindowBackdropType.Mica, true);
            return;
        }

        SystemThemeWatcher.UnWatch(_window);
        ApplicationThemeManager.Apply(
            theme == AppThemeOption.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.Mica,
            true
        );
    }
}