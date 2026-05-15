using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace FluGenPass.Views.Dialogs;

public partial class KeyFileWarningView : UserControl
{
    public event Action<bool>? ValidationChanged;

    public KeyFileWarningView()
    {
        InitializeComponent();
    }

    private void CheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool allChecked = Check1.IsChecked == true && 
                          Check2.IsChecked == true && 
                          Check3.IsChecked == true;
        
        ValidationChanged?.Invoke(allChecked);
    }
}
