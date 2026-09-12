using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using LyricFloat.App.Models;
using LyricFloat.App.ViewModels;

namespace LyricFloat.App.Views;

public partial class OverlayWindow : Window
{
    private bool _positionInitialized;
    public bool IsLocked { get; private set; }
    public event EventHandler? DragCompleted;

    public void SetPosition(double left, double top)
    {
        _positionInitialized = true;
        Left = left;
        Top = top;
    }

    public void ApplyAppearance(AppSettings settings)
    {
        var secondaryVisibility = settings.DisplayMode == OverlayDisplayMode.ThreeLines ? Visibility.Visible : Visibility.Collapsed;
        PreviousText.Visibility = NextText.Visibility = SongCaption.Visibility = PauseCaption.Visibility = secondaryVisibility;
        foreach (var text in new[] { PreviousText, CurrentText, NextText, SongCaption, PauseCaption })
        {
            text.FontFamily = new FontFamily(settings.FontFamily);
            text.TextAlignment = settings.TextAlignment switch
            { LyricAlignment.Left => TextAlignment.Left, LyricAlignment.Right => TextAlignment.Right, _ => TextAlignment.Center };
        }
        CurrentText.FontSize = settings.CurrentFontSize;
        CurrentText.MaxHeight = settings.CurrentFontSize * 2.7;
        CurrentText.Opacity = settings.TextOpacity;
        CurrentText.FontWeight = settings.FontWeight switch
        { LyricFontWeight.Normal => FontWeights.Normal, LyricFontWeight.Bold => FontWeights.Bold, _ => FontWeights.SemiBold };
        PreviousText.FontSize = NextText.FontSize = settings.SecondaryFontSize;
        PreviousText.MaxHeight = NextText.MaxHeight = settings.SecondaryFontSize * 2.6;
        PreviousText.Opacity = NextText.Opacity = settings.SecondaryTextOpacity;
        LyricContainer.Background = new SolidColorBrush(Color.FromArgb(
            settings.BackgroundMode == OverlayBackgroundMode.Subtle ? (byte)Math.Round(settings.BackgroundOpacity * 255) : (byte)0, 16, 18, 22));
        LyricContainer.CornerRadius = new CornerRadius(settings.BackgroundMode == OverlayBackgroundMode.Subtle ? 18 : 0);
        Height = settings.DisplayMode == OverlayDisplayMode.Minimal ? settings.CurrentFontSize * 2.7 + 60
            : settings.CurrentFontSize * 2.7 + settings.SecondaryFontSize * 5.2 + 100;
    }

    public void SetLocked(bool locked)
    {
        IsLocked = locked;
        if (locked) LyricContainer.ToolTip = null;
        else LyricContainer.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(OverlayViewModel.InteractionHint)));
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
            DragCompleted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
