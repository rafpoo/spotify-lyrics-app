using System.Windows;
using System.Windows.Input;
using LyricFloat.App.ViewModels;

namespace LyricFloat.App.Views;

public partial class OverlayWindow : Window
{
    private bool _positionInitialized;
    public bool IsLocked { get; private set; }

    public void SetLocked(bool locked)
    {
        IsLocked = locked;
        LyricContainer.ToolTip = locked ? null : "Unlocked — drag to move · Controls in the system tray";
        LyricContainer.Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;
    }

    public OverlayWindow()
    {
        InitializeComponent();
        DataContext = new OverlayViewModel();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_positionInitialized) return;
        _positionInitialized = true;
        // WPF work-area coordinates are device-independent, like Left and Top.
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = Math.Max(workArea.Top, workArea.Bottom - ActualHeight - 64);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsLocked && e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
            e.Handled = true;
        }
    }
}
