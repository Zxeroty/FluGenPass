using Wpf.Ui;
using Wpf.Ui.Controls;

namespace FluGenPass.Services;

public sealed class NotificationService : INotificationService
{
    private readonly ISnackbarService _snackbarService = new SnackbarService
    {
        DefaultTimeOut = TimeSpan.FromSeconds(2.5),
    };

    public void Initialize(SnackbarPresenter presenter)
    {
        _snackbarService.SetSnackbarPresenter(presenter);
    }

    public void ShowInfo(string title, string message)
    {
        _snackbarService.Show(title, message, ControlAppearance.Info, null, TimeSpan.FromSeconds(2.5));
    }

    public void ShowSuccess(string title, string message)
    {
        _snackbarService.Show(title, message, ControlAppearance.Success, null, TimeSpan.FromSeconds(2.5));
    }

    public void ShowError(string title, string message)
    {
        _snackbarService.Show(title, message, ControlAppearance.Danger, null, TimeSpan.FromSeconds(3.5));
    }
}