using System.Drawing;
using System.Windows.Forms;
using Nocturne.Models;

namespace Nocturne.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _nextItem;

    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? TogglePauseRequested;
    public event EventHandler? NextRequested;
    public event EventHandler? SettingsRequested;

    public TrayService()
    {
        _icon = new NotifyIcon
        {
            Text = "Nocturne",
            Icon = LoadIcon(),
            Visible = true
        };

        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            BackColor = Color.FromArgb(21, 21, 28),
            ForeColor = Color.FromArgb(245, 244, 249),
            Font = new Font("Segoe UI", 9f)
        };

        var open = new ToolStripMenuItem("Open Nocturne");
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        var settings = new ToolStripMenuItem("Settings");
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        _pauseItem = new ToolStripMenuItem("Pause wallpaper");
        _pauseItem.Click += (_, _) => TogglePauseRequested?.Invoke(this, EventArgs.Empty);

        _nextItem = new ToolStripMenuItem("Next wallpaper");
        _nextItem.Click += (_, _) => NextRequested?.Invoke(this, EventArgs.Empty);

        var exit = new ToolStripMenuItem("Exit Nocturne");
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(open);
        menu.Items.Add(settings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_nextItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateState(bool paused, string? wallpaperName)
    {
        try
        {
            _pauseItem.Text = paused ? "Resume wallpaper" : "Pause wallpaper";
            _nextItem.Enabled = true;

            string status = paused ? "Paused" : string.IsNullOrWhiteSpace(wallpaperName) ? "Idle" : wallpaperName!;
            string text = $"Nocturne — {status}";
            _icon.Text = text.Length > 63 ? text[..60] + "…" : text;
        }
        catch { }
    }

    public void ShowNotification(string title, string message)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = message;
            _icon.ShowBalloonTip(3000);
        }
        catch { }
    }

    private static Icon LoadIcon()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                Icon? icon = Icon.ExtractAssociatedIcon(exe);
                if (icon is not null)
                    return icon;
            }
        }
        catch { }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        try
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
        catch { }
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item?.Selected == true ? Color.White : Color.FromArgb(232, 230, 240);
            base.OnRenderItemText(e);
        }

        private sealed class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Color.FromArgb(21, 21, 28);
            public override Color ImageMarginGradientBegin => Color.FromArgb(21, 21, 28);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(21, 21, 28);
            public override Color ImageMarginGradientEnd => Color.FromArgb(21, 21, 28);
            public override Color MenuItemSelected => Color.FromArgb(42, 31, 71);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(42, 31, 71);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(42, 31, 71);
            public override Color MenuItemBorder => Color.FromArgb(42, 31, 71);
            public override Color MenuBorder => Color.FromArgb(45, 45, 58);
            public override Color SeparatorDark => Color.FromArgb(45, 45, 58);
            public override Color SeparatorLight => Color.FromArgb(45, 45, 58);
        }
    }
}
