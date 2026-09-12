using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LyricFloat.App.Helpers;

internal static class AppIcon
{
    public static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("LyricFloat.Icon")
            ?? throw new InvalidOperationException("Application icon resource is missing.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    public static ImageSource Image()
    {
        using var icon = Load();
        var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        image.Freeze();
        return image;
    }
}
