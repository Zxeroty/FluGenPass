using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using PasswordBox = System.Windows.Controls.PasswordBox;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace FluGenPass.Services;

public sealed class DialogService : IDialogService
{
    private readonly IContentDialogService _contentDialogService = new ContentDialogService();
    private Window? _ownerWindow;
    private bool _isInitialized;

    public void Initialize(ContentDialogHost dialogHost, Window ownerWindow)
    {
        _contentDialogService.SetDialogHost(dialogHost);
        _ownerWindow = ownerWindow;
        _isInitialized = true;
    }

    public Task<string?> PromptForSiteNameAsync(CancellationToken cancellationToken = default)
    {
        return PromptForTextAsync(
            title: "Save to vault",
            description: "Add the site or service label for this password.",
            primaryButtonText: "Save",
            validationMessageFactory: value =>
                string.IsNullOrWhiteSpace(value)
                    ? "Site or service name is required."
                    : null,
            cancellationToken
        );
    }

    public Task<string?> PromptForTagsAsync(string initialValue = "", CancellationToken cancellationToken = default)
    {
        return PromptForTextAsync(
            title: "Edit tags",
            description: "Add one or more tags separated by commas. Leave the field empty to clear tags for this entry.",
            primaryButtonText: "Save",
            validationMessageFactory: static _ => null,
            cancellationToken: cancellationToken,
            initialValue: initialValue
        );
    }

    public async Task<string?> PromptForNewMasterPasswordAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            return null;
        }

        string? validationMessage = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            PasswordBox passwordBox = new() { MinWidth = 320, Margin = new Thickness(0, 8, 0, 0) };
            PasswordBox confirmPasswordBox = new() { MinWidth = 320, Margin = new Thickness(0, 8, 0, 0) };

            StackPanel panel = new() { Margin = new Thickness(0, 8, 0, 0) };
            panel.Children.Add(new TextBlock
            {
                Text = "Create a master password to protect the local vault.",
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(new TextBlock { Margin = new Thickness(0, 12, 0, 0), Text = "Master password" });
            panel.Children.Add(passwordBox);
            panel.Children.Add(new TextBlock { Margin = new Thickness(0, 12, 0, 0), Text = "Confirm password" });
            panel.Children.Add(confirmPasswordBox);

            if (!string.IsNullOrWhiteSpace(validationMessage))
            {
                panel.Children.Add(CreateValidationMessage(validationMessage));
            }

            ContentDialog dialog = new()
            {
                Title = "Create master password",
                Content = panel,
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };

            ContentDialogResult result = await _contentDialogService.ShowAsync(dialog, cancellationToken);

            if (result != ContentDialogResult.Primary)
            {
                
                passwordBox.Clear();
                confirmPasswordBox.Clear();
                return null;
            }

            int passwordLength = passwordBox.Password.Length;
            int confirmLength = confirmPasswordBox.Password.Length;

            if (passwordLength < 8)
            {
                validationMessage = "Use at least 8 characters for the master password.";
                passwordBox.Clear();
                confirmPasswordBox.Clear();
                continue;
            }

            if (passwordLength != confirmLength || !passwordBox.Password.Equals(confirmPasswordBox.Password, StringComparison.Ordinal))
            {
                validationMessage = "The passwords do not match.";
                passwordBox.Clear();
                confirmPasswordBox.Clear();
                continue;
            }

            string password = passwordBox.Password;
            passwordBox.Clear();
            confirmPasswordBox.Clear();

            return password;
        }

        return null;
    }

    public Task<string?> PromptForUnlockPasswordAsync(CancellationToken cancellationToken = default)
    {
        return PromptForPasswordAsync(
            title: "Unlock vault",
            description: "Enter the master password to unlock the local password vault.",
            primaryButtonText: "Unlock",
            cancellationToken
        );
    }

    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string primaryButtonText = "Confirm",
        string closeButtonText = "Cancel",
        CancellationToken cancellationToken = default
    )
    {
        if (!_isInitialized)
        {
            return MessageBox.Show(
                    _ownerWindow,
                    message,
                    title,
                    System.Windows.MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                ) == System.Windows.MessageBoxResult.Yes;
        }

        ContentDialog dialog = new()
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = closeButtonText,
            DefaultButton = ContentDialogButton.Close,
        };

        ContentDialogResult result = await _contentDialogService.ShowAsync(dialog, cancellationToken);
        return result == ContentDialogResult.Primary;
    }

    public async Task ShowMessageAsync(
        string title,
        string message,
        string closeButtonText = "Close",
        CancellationToken cancellationToken = default
    )
    {
        if (!_isInitialized)
        {
            MessageBox.Show(
                _ownerWindow,
                message,
                title,
                System.Windows.MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        ContentDialog dialog = new()
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = closeButtonText,
        };

        await _contentDialogService.ShowAsync(dialog, cancellationToken);
    }

    private async Task<string?> PromptForPasswordAsync(
        string title,
        string description,
        string primaryButtonText,
        CancellationToken cancellationToken
    )
    {
        if (!_isInitialized)
        {
            return null;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            PasswordBox passwordBox = new() { MinWidth = 320, Margin = new Thickness(0, 8, 0, 0) };

            StackPanel panel = new() { Margin = new Thickness(0, 8, 0, 0) };
            panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(passwordBox);

            ContentDialog dialog = new()
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };

            ContentDialogResult result = await _contentDialogService.ShowAsync(dialog, cancellationToken);

            if (result != ContentDialogResult.Primary)
            {
                passwordBox.Clear();
                return null;
            }

            string password = passwordBox.Password.Trim();
            passwordBox.Clear();

            if (!string.IsNullOrWhiteSpace(password))
            {
                return password;
            }
        }

        return null;
    }

    private async Task<string?> PromptForTextAsync(
        string title,
        string description,
        string primaryButtonText,
        Func<string, string?> validationMessageFactory,
        CancellationToken cancellationToken,
        string initialValue = ""
    )
    {
        if (!_isInitialized)
        {
            return null;
        }

        string? validationMessage = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            TextBox textBox = new() { MinWidth = 320, Margin = new Thickness(0, 8, 0, 0), Text = initialValue };

            StackPanel panel = new() { Margin = new Thickness(0, 8, 0, 0) };
            panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(textBox);

            if (!string.IsNullOrWhiteSpace(validationMessage))
            {
                panel.Children.Add(CreateValidationMessage(validationMessage));
            }

            ContentDialog dialog = new()
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };

            ContentDialogResult result = await _contentDialogService.ShowAsync(dialog, cancellationToken);

            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            string value = textBox.Text.Trim();
            validationMessage = validationMessageFactory(value);

            if (validationMessage is null)
            {
                return value;
            }
        }

        return null;
    }

    private static TextBlock CreateValidationMessage(string message)
    {
        return new TextBlock
        {
            Margin = new Thickness(0, 12, 0, 0),
            Foreground = System.Windows.Media.Brushes.IndianRed,
            Text = message,
            TextWrapping = TextWrapping.Wrap,
        };
    }
}
