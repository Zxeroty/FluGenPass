using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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

    private void OnEntriesDataGridPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (EntriesDataGrid.ContextMenu is null ||
            e.OriginalSource is not DependencyObject source ||
            FindVisualParent<DataGridRow>(source) is not { } row)
        {
            return;
        }

        row.IsSelected = true;
        row.Focus();

        Point position = e.GetPosition(row);
        EntriesDataGrid.ContextMenu.DataContext = row.DataContext;
        EntriesDataGrid.ContextMenu.PlacementTarget = row;
        EntriesDataGrid.ContextMenu.Placement = PlacementMode.RelativePoint;
        EntriesDataGrid.ContextMenu.HorizontalOffset = Math.Round(position.X);
        EntriesDataGrid.ContextMenu.VerticalOffset = Math.Round(position.Y);
        EntriesDataGrid.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private static T? FindVisualParent<T>(DependencyObject source)
        where T : DependencyObject
    {
        DependencyObject? current = source;

        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
