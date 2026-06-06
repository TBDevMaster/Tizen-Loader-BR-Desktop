using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TizenLoaderBRDesktop.ViewModels;

namespace TizenLoaderBRDesktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Loaded += MainWindow_Loaded;
        AddHandler(DataGrid.PreviewMouseWheelEvent, new MouseWheelEventHandler(ScrollableContent_PreviewMouseWheel), true);
        AddHandler(TextBox.PreviewMouseWheelEvent, new MouseWheelEventHandler(ScrollableContent_PreviewMouseWheel), true);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is MainViewModel viewModel)
            {
                await viewModel.InitializeAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Erro ao iniciar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void ScrollableContent_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject source)
        {
            return;
        }

        var scrollViewer = FindVisualChild<ScrollViewer>(source);
        if (scrollViewer is null || scrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        if (e.Delta < 0)
        {
            scrollViewer.LineDown();
        }
        else
        {
            scrollViewer.LineUp();
        }

        e.Handled = true;
    }

    private static T? FindVisualChild<T>(DependencyObject source)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(source); index++)
        {
            var child = VisualTreeHelper.GetChild(source, index);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
