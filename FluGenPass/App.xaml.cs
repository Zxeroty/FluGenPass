using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using FluGenPass.Services;
using FluGenPass.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FluGenPass;

public partial class App : Application
{
    private static ServiceProvider? _services;
    private static int _isHandlingFatalError;

    public static T GetRequiredService<T>()
        where T : notnull
    {
        if (_services is null)
        {
            throw new InvalidOperationException("Application services are not initialized.");
        }

        return _services.GetRequiredService<T>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        _services = ConfigureServices();

        MainWindow = GetRequiredService<MainWindow>();
        _ = GetRequiredService<ILocalizationService>().InitializeAsync();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _services?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowFatalError(e.Exception);
        e.Handled = true;
        Shutdown(-1);
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        ShowFatalError(e.ExceptionObject as Exception ?? new Exception("Unknown fatal error."));
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ShowFatalError(e.Exception);
        e.SetObserved();
    }

    private static void ShowFatalError(Exception exception)
    {
        if (Interlocked.Exchange(ref _isHandlingFatalError, 1) == 1)
        {
            return;
        }

        try
        {
            string appDirectory = StoragePaths.GetAppDirectory();
            Directory.CreateDirectory(appDirectory);

            string logPath = Path.Combine(appDirectory, "crash.log");
            string sanitizedMessage = SanitizeExceptionMessage(exception);
            string message = $"""
                [{DateTimeOffset.Now:O}]
                {sanitizedMessage}

                """;

            File.AppendAllText(logPath, message, Encoding.UTF8);

            MessageBox.Show(
                "FluGenPass encountered an unexpected error and needs to close.\n\n" +
                $"Details: {sanitizedMessage}\n\n" +
                $"A crash log was written to:\n{logPath}",
                "FluGenPass Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        catch
        {
            
        }
        finally
        {
            Interlocked.Exchange(ref _isHandlingFatalError, 0);
        }
    }

    private static string SanitizeExceptionMessage(Exception exception)
    {
        var sb = new System.Text.StringBuilder();
        SanitizeException(exception, sb, 0);
        return sb.ToString();
    }

    private static void SanitizeException(Exception exception, System.Text.StringBuilder sb, int depth)
    {
        string indent = new(' ', depth * 2);
        string type = exception.GetType().Name;

        string message = RedactSensitiveData(exception.Message);
        sb.AppendLine($"{indent}{type}: {message}");

        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            string stackTrace = RedactSensitiveData(exception.StackTrace);
            sb.AppendLine($"{indent}Stack Trace:");
            sb.AppendLine(stackTrace);
        }

        if (exception.InnerException != null && depth < 5)
        {
            SanitizeException(exception.InnerException, sb, depth + 1);
        }
    }

    // Require an explicit assignment operator (:, =) before redacting the next word,
    // to avoid redacting normal sentences like "The password field is empty".
    private static readonly System.Text.RegularExpressions.Regex SensitiveDataRegex = new(
        @"(?i)(password|master password|vault key|secret|token|credential)\s*[:=]\s*[^\s]+",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string RedactSensitiveData(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        string redacted = SensitiveDataRegex.Replace(message, "$1=[REDACTED]");

        if (redacted.Length > 500)
        {
            return redacted[..500] + "... [truncated]";
        }

        return redacted;
    }

    private static ServiceProvider ConfigureServices()
    {
        string appDirectory = StoragePaths.GetAppDirectory();
        ServiceCollection services = new();

        services.AddSingleton<ISettingsService>(_ => new SettingsService(appDirectory));
        services.AddSingleton<ISessionStateService, SessionStateService>();
        services.AddSingleton<IMasterPasswordService, MasterPasswordService>();
        services.AddSingleton<IKeyFileService, KeyFileService>();
        services.AddSingleton<IVaultService>(serviceProvider =>
            new VaultService(appDirectory, serviceProvider.GetRequiredService<ISessionStateService>())
        );
        services.AddSingleton<ITransferSignatureService>(sp => new TransferSignatureService(appDirectory, sp.GetRequiredService<ILocalizationService>()));
        services.AddSingleton<IVaultTransferService, VaultTransferService>();
        services.AddSingleton<IPasswordGeneratorService, PasswordGeneratorService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IPwnedPasswordService, PwnedPasswordService>();
        services.AddSingleton<IVaultAccessCoordinator>(sp => new VaultAccessCoordinator(
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<IMasterPasswordService>(),
            sp.GetRequiredService<IKeyFileService>(),
            sp.GetRequiredService<INotificationService>(),
            sp.GetRequiredService<ISettingsService>()
        ));
        services.AddSingleton<IInactivityAutoLockService>(serviceProvider =>
        {
            var sessionState = serviceProvider.GetRequiredService<ISessionStateService>();
            return new InactivityAutoLockService(sessionState, TimeSpan.FromMinutes(5));
        });

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<GeneratorViewModel>();
        services.AddSingleton<VaultViewModel>();
        services.AddSingleton<SettingsViewModel>();

        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
