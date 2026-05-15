using System.Windows;
using System.Windows.Controls;
using FluGenPass.ViewModels;

namespace FluGenPass.Views.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.GetRequiredService<SettingsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }
}
