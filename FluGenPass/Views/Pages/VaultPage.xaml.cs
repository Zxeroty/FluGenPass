using System.Windows;
using System.Windows.Controls;
using FluGenPass.ViewModels;

namespace FluGenPass.Views.Pages;

public partial class VaultPage : Page
{
    public VaultPage()
    {
        ViewModel = App.GetRequiredService<VaultViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public VaultViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
    }
}
