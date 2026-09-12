using System.Windows;
using System.Windows.Input;
using LyricFloat.App.ViewModels;

namespace LyricFloat.App.Views;

public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
        DataContext = new OverlayViewModel();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // WPF work-area coordinates are device-independent, like Left and Top.
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = Math.Max(workArea.Top, workArea.Bottom - ActualHeight - 64);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
            e.Handled = true;
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();
}
