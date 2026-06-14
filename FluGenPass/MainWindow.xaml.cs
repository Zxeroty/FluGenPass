using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using FluGenPass.Services;
using FluGenPass.ViewModels;
using FluGenPass.Views.Pages;
using Wpf.Ui.Controls;

namespace FluGenPass;

public partial class MainWindow : FluentWindow
{
    private readonly IThemeService _themeService;
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notificationService;
    private readonly IVaultAccessCoordinator _vaultAccessCoordinator;
    private readonly IInactivityAutoLockService _autoLockService;
    private readonly ISettingsService _settingsService;

    public MainWindow(
        MainViewModel viewModel,
        IThemeService themeService,
        IDialogService dialogService,
        INotificationService notificationService,
        IVaultAccessCoordinator vaultAccessCoordinator,
        IInactivityAutoLockService autoLockService,
        ISettingsService settingsService
    )
    {
        ViewModel = viewModel;
        _themeService = themeService;
        _dialogService = dialogService;
        _notificationService = notificationService;
        _vaultAccessCoordinator = vaultAccessCoordinator;
        _autoLockService = autoLockService;
        _settingsService = settingsService;

        DataContext = ViewModel;
        InitializeComponent();

        _dialogService.Initialize(RootContentDialogHost, this);
        _notificationService.Initialize(RootSnackbarPresenter);

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    public MainViewModel ViewModel { get; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_MONITOR = 0x00000001;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        try
        {
#if !DEBUG
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            SetWindowDisplayAffinity(hwnd, WDA_MONITOR);
#endif
        }
        catch
        {
            // Ignore if OS does not support display affinity
        }

        try
        {
            await _themeService.InitializeAsync(this);
            await InitializeAutoLockAsync();
            RootNavigationView.Navigate(typeof(GeneratorPage));
        }
        catch (Exception ex)
        {
            _notificationService.ShowError("Initialization error", ex.Message);
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        Closing -= OnClosing;
        _autoLockService.Dispose();
    }

    private async Task InitializeAutoLockAsync()
    {
        var settings = await _settingsService.GetAsync();
        _autoLockService.IsEnabled = settings.AutoLockEnabled && settings.AutoLockTimeoutMinutes > 0;
        if (settings.AutoLockTimeoutMinutes > 0)
        {
            _autoLockService.Timeout = TimeSpan.FromMinutes(settings.AutoLockTimeoutMinutes);
        }
    }

    private async void OnNavigationViewNavigating(NavigationView sender, NavigatingCancelEventArgs args)
    {
        if (args.Page is not VaultPage || ViewModel.IsVaultUnlocked)
        {
            return;
        }

        args.Cancel = true;

        try
        {
            if (await _vaultAccessCoordinator.EnsureAccessAsync())
            {
                sender.Navigate(typeof(VaultPage));
            }
        }
        catch (Exception ex)
        {
            _notificationService.ShowError("Navigation error", ex.Message);
        }
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        RootNavigationView.GoBack();
    }
}