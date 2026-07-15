using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace FluGenPass.Services;

public sealed class NotificationService : INotificationService
{
    private readonly ISnackbarService _snackbarService = new SnackbarService
    {
        DefaultTimeOut = TimeSpan.FromSeconds(2.5),
    };

    private SnackbarPresenter? _presenter;

    public void Initialize(SnackbarPresenter presenter)
    {
        _presenter = presenter;
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

    public void ShowAction(string title, string message, string actionButtonText, Action action, TimeSpan timeout)
    {
        if (_presenter is null)
        {
            return;
        }

        var button = new Wpf.Ui.Controls.Button
        {
            Content = actionButtonText,
            Appearance = ControlAppearance.Primary,
            FontSize = 12,
            Height = 24,
            Padding = new System.Windows.Thickness(8, 0, 8, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new System.Windows.Thickness(12, 0, 0, 0),
        };

        var textBlock = new System.Windows.Controls.TextBlock
        {
            Text = message,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            TextWrapping = System.Windows.TextWrapping.Wrap,
        };

        var panel = new System.Windows.Controls.Grid();
        panel.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
        panel.Children.Add(textBlock);
        panel.Children.Add(button);
        System.Windows.Controls.Grid.SetColumn(button, 1);

        var snackbar = new Snackbar(_presenter)
        {
            Title = title,
            Content = panel,
            Appearance = ControlAppearance.Info,
            Timeout = timeout,
            IsCloseButtonEnabled = true,
        };

        button.Click += (_, _) =>
        {
            action();
            _ = _presenter.HideCurrent();
        };

        snackbar.Show(true);
    }
}
