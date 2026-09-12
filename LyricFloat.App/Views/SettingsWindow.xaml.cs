using System.Windows;
using LyricFloat.App.ViewModels;

namespace LyricFloat.App.Views;

public partial class SettingsWindow : Window
{
    internal SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        Icon = Helpers.AppIcon.Image();
        DataContext = viewModel;
    }
    private SettingsViewModel Model => (SettingsViewModel)DataContext;
    private void OnResetPosition(object sender, RoutedEventArgs e) => Model.ResetPosition();
    private void OnResetTiming(object sender, RoutedEventArgs e) => Model.TimingOffset = 0;
    private void OnSpotify(object sender, RoutedEventArgs e) => Model.ToggleSpotify();
    private void OnOpenLogs(object sender, RoutedEventArgs e) => Model.OpenLogs();
    private async void OnSaveClientId(object sender, RoutedEventArgs e)
    {
        var button = (System.Windows.Controls.Button)sender;
        button.IsEnabled = false;
        try { await Model.SaveClientIdAsync(); }
        finally { button.IsEnabled = true; }
    }
    private void OnResetDefaults(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Reset normal preferences and Windows startup to defaults? Spotify login and cached lyrics will be kept.",
            "Reset LyricFloat preferences", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
            Model.ResetDefaults();
    }
    private async void OnClearCache(object sender, RoutedEventArgs e)
    {
        var button = (System.Windows.Controls.Button)sender;
        button.IsEnabled = false;
        try { await Model.ClearCacheAsync(); }
        finally { button.IsEnabled = true; }
    }
}
