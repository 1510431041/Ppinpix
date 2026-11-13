using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Ppinpix.Core;
using Ppinpix.Core.Settings;

namespace Ppinpix.App.Tray;

/// <summary>系统托盘图标与深色渲染上下文菜单（热键暂停时切灰白图标提示）。</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _captureItem;
    private readonly Forms.ToolStripMenuItem _pinClipboardItem;
    private readonly Forms.ToolStripMenuItem _togglePinsItem;
    private readonly Forms.ToolStripMenuItem _suspendHotkeysItem;

    private readonly Icon _normalIcon;
    private readonly Icon _suspendedIcon;
    private bool _disposed;

    public TrayIcon(
        HotkeySettings hotkeys,
        Action onCapture,
        Action<int> onDelayedCapture,
        Action onPinClipboard,
        Action onTogglePins,
        Action onToggleSuspend,
        Action onOpenSettings,
        Action onExit)
    {
        _normalIcon = LoadAppIcon();
        _suspendedIcon = CreateGrayscaleIcon(_normalIcon);

        _menu = new Forms.ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            ForeColor = Color.FromArgb(0xF2, 0xF4, 0xF7),
            BackColor = Color.FromArgb(0x1F, 0x23, 0x29),
        };

        _captureItem = new Forms.ToolStripMenuItem("立即截图", null, (_, _) => onCapture());
        var delayMenu = new Forms.ToolStripMenuItem("延迟截图");
        delayMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("延迟 3 秒截图", null, (_, _) => onDelayedCapture(3000)));
        delayMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("延迟 5 秒截图", null, (_, _) => onDelayedCapture(5000)));
        delayMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("延迟 10 秒截图", null, (_, _) => onDelayedCapture(10000)));

        _pinClipboardItem = new Forms.ToolStripMenuItem("从剪贴板贴图", null, (_, _) => onPinClipboard());
        _togglePinsItem = new Forms.ToolStripMenuItem("显示/隐藏全部贴图", null, (_, _) => onTogglePins());

        _suspendHotkeysItem = new Forms.ToolStripMenuItem("暂停全局热键", null, (_, _) => onToggleSuspend())
        {
            CheckOnClick = false,
        };
        var settingsItem = new Forms.ToolStripMenuItem("设置…", null, (_, _) => onOpenSettings());
        var exitItem = new Forms.ToolStripMenuItem("退出 Ppinpix", null, (_, _) => onExit());

        _menu.Items.AddRange(
        [
            _captureItem,
            delayMenu,
            _pinClipboardItem,
            _togglePinsItem,
            new Forms.ToolStripSeparator(),
            _suspendHotkeysItem,
            settingsItem,
            new Forms.ToolStripSeparator(),
            exitItem,
        ]);

        UpdateHotkeys(hotkeys, suspended: false);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _normalIcon,
            Text = "Ppinpix",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => onCapture();
    }

    public void UpdateHotkeys(HotkeySettings hotkeys, bool suspended)
    {
        _captureItem.ShortcutKeyDisplayString = hotkeys.Capture;
        _pinClipboardItem.ShortcutKeyDisplayString = hotkeys.PinClipboard;
        _togglePinsItem.ShortcutKeyDisplayString = hotkeys.TogglePins;
        _suspendHotkeysItem.ShortcutKeyDisplayString = hotkeys.SuspendHotkeys;
        _suspendHotkeysItem.Checked = suspended;

        if (_notifyIcon is not null)
        {
            _notifyIcon.Icon = suspended ? _suspendedIcon : _normalIcon;
            _notifyIcon.Text = suspended ? "Ppinpix（全局热键已暂停）" : $"Ppinpix（{hotkeys.Capture} 截图）";
        }
    }

    public void ShowBalloon(string title, string message, bool error = false)
    {
        if (_disposed) return;
        _notifyIcon.ShowBalloonTip(
            3000,
            title,
            message,
            error ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Info);
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var streamInfo = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (streamInfo is not null)
            {
                using var s = streamInfo.Stream;
                return new Icon(s, 32, 32);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("读取嵌入托盘图标失败。", ex);
        }
        return (Icon)SystemIcons.Application.Clone();
    }

    private static Icon CreateGrayscaleIcon(Icon source)
    {
        using var bmp = source.ToBitmap();
        using var gray = new Bitmap(bmp.Width, bmp.Height);
        using (var g = Graphics.FromImage(gray))
        {
            var matrix = new ColorMatrix(
            [
                [0.299f, 0.299f, 0.299f, 0, 0],
                [0.587f, 0.587f, 0.587f, 0, 0],
                [0.114f, 0.114f, 0.114f, 0, 0],
                [0, 0, 0, 0.65f, 0],
                [0, 0, 0, 0, 1],
            ]);
            using var attrs = new ImageAttributes();
            attrs.SetColorMatrix(matrix);
            g.DrawImage(bmp, new Rectangle(0, 0, gray.Width, gray.Height), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attrs);
        }
        nint hIcon = gray.GetHicon();
        using var tmp = Icon.FromHandle(hIcon);
        return (Icon)tmp.Clone();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _normalIcon.Dispose();
        _suspendedIcon.Dispose();
    }

    private sealed class DarkMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            var rect = new Rectangle(4, 1, e.Item.Bounds.Width - 8, e.Item.Bounds.Height - 2);
            using var brush = new SolidBrush(e.Item.Selected
                ? Color.FromArgb(0x2F, 0x80, 0xFF)
                : Color.FromArgb(0x1F, 0x23, 0x29));
            e.Graphics.FillRectangle(brush, rect);
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled
                ? Color.FromArgb(0xF2, 0xF4, 0xF7)
                : Color.FromArgb(0x5C, 0x63, 0x70);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.ContentRectangle.Top + e.Item.ContentRectangle.Height / 2;
            using var pen = new Pen(Color.FromArgb(0x36, 0x3D, 0x48));
            e.Graphics.DrawLine(pen, 8, y, e.Item.Bounds.Width - 8, y);
        }
    }

    private sealed class DarkColorTable : Forms.ProfessionalColorTable
    {
        private static readonly Color Bg = Color.FromArgb(0x1F, 0x23, 0x29);
        private static readonly Color Border = Color.FromArgb(0x38, 0x3E, 0x47);
        public override Color ToolStripDropDownBackground => Bg;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
    }
}
