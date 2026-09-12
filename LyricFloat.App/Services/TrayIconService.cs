using Forms = System.Windows.Forms;

namespace LyricFloat.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly OverlayController _overlay;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly Forms.ToolStripMenuItem _visibility = new();
    private readonly Forms.ToolStripMenuItem _lock = new();
    private readonly System.Drawing.Icon _image;
    private bool _disposed;

    public TrayIconService(OverlayController overlay, Action exit)
    {
        _overlay = overlay;
        _menu.Items.Add(new Forms.ToolStripMenuItem("LyricFloat") { Enabled = false });
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(_visibility);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(_lock);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => exit());
        _visibility.Click += (_, _) => _overlay.ToggleVisibility();
        _lock.Click += (_, _) => _overlay.ToggleLock();
        _image = (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = "LyricFloat",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => _overlay.ToggleVisibility();
        _overlay.StateChanged += UpdateMenu;
        UpdateMenu(this, EventArgs.Empty);
    }

    private void UpdateMenu(object? sender, EventArgs e)
    {
        _visibility.Text = _overlay.IsOverlayVisible ? "Hide Lyrics" : "Show Lyrics";
        _lock.Text = _overlay.IsOverlayLocked ? "Unlock Overlay" : "Lock Overlay";
        _icon.Text = $"LyricFloat · {(_overlay.IsOverlayLocked ? "Locked" : "Unlocked")}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _overlay.StateChanged -= UpdateMenu;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _image.Dispose();
    }
}
