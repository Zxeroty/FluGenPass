using System.Windows.Controls;
using FluGenPass.ViewModels;

namespace FluGenPass.Views.Pages;

public partial class GeneratorPage : Page
{
    public GeneratorPage()
    {
        ViewModel = App.GetRequiredService<GeneratorViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public GeneratorViewModel ViewModel { get; }
}