using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace WarpCord
{
    internal enum TrayState
    {
        Unknown,
        Healthy,
        Problem,
        Busy
    }

    /// <summary>Status icon in the notification area, with a small menu and balloons.</summary>
    internal sealed class TrayIcon : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        private readonly WinForms.NotifyIcon _notify;
        private readonly WinForms.ToolStripMenuItem _openItem;
        private readonly WinForms.ToolStripMenuItem _repairItem;
        private readonly WinForms.ToolStripMenuItem _exitItem;
        private readonly bool _notifications;
        private Icon _icon;
        private TrayState _state = (TrayState)(-1);
        private string _tooltip = "";

        public event Action OpenRequested;
        public event Action RepairRequested;
        public event Action ExitRequested;

        public TrayIcon(bool notifications)
        {
            _notifications = notifications;

            var menu = new WinForms.ContextMenuStrip();
            menu.ShowImageMargin = false;
            _openItem = new WinForms.ToolStripMenuItem(L10n.T("WarpCord'u aç"));
            _repairItem = new WinForms.ToolStripMenuItem(L10n.T("Onar"));
            _exitItem = new WinForms.ToolStripMenuItem(L10n.T("Çıkış"));
            _openItem.Click += delegate { Raise(OpenRequested); };
            _repairItem.Click += delegate { Raise(RepairRequested); };
            _exitItem.Click += delegate { Raise(ExitRequested); };
            menu.Items.Add(_openItem);
            menu.Items.Add(_repairItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(_exitItem);

            _notify = new WinForms.NotifyIcon();
            _notify.Text = "WarpCord";
            _notify.ContextMenuStrip = menu;
            _notify.Visible = false;
            _notify.DoubleClick += delegate { Raise(OpenRequested); };
            ApplyIcon(TrayState.Unknown);
        }

        public bool Visible
        {
            get { return _notify.Visible; }
        }

        public void Show()
        {
            _notify.Visible = true;
        }

        public void Hide()
        {
            _notify.Visible = false;
        }

        public void SetState(TrayState state, string tooltip)
        {
            if (state != _state) ApplyIcon(state);

            string text = string.IsNullOrEmpty(tooltip) ? "WarpCord" : tooltip;
            if (text.Length > 63) text = text.Substring(0, 60) + "...";
            if (_tooltip == text) return;
            _tooltip = text;
            try { _notify.Text = text; }
            catch { }
        }

        public void RefreshLanguage()
        {
            _openItem.Text = L10n.T("WarpCord'u aç");
            _repairItem.Text = L10n.T("Onar");
            _exitItem.Text = L10n.T("Çıkış");
        }

        public void Notify(string title, string body, bool warning)
        {
            if (!_notifications || !_notify.Visible) return;
            try
            {
                _notify.ShowBalloonTip(4000, title, body,
                    warning ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info);
            }
            catch
            {
                // balloons are best effort
            }
        }

        private static void Raise(Action handler)
        {
            if (handler != null) handler();
        }

        private void ApplyIcon(TrayState state)
        {
            Icon icon = BuildIcon(state);
            Icon previous = _icon;
            _icon = icon;
            _state = state;
            try { _notify.Icon = icon; }
            catch { }
            if (previous != null)
            {
                try { previous.Dispose(); }
                catch { }
            }
        }

        private static Color ToneFor(TrayState state)
        {
            switch (state)
            {
                case TrayState.Healthy: return Color.FromArgb(255, 0x34, 0xD3, 0x99);
                case TrayState.Problem: return Color.FromArgb(255, 0xF8, 0x71, 0x71);
                case TrayState.Busy: return Color.FromArgb(255, 0x22, 0xD3, 0xEE);
                default: return Color.FromArgb(255, 0x66, 0x70, 0x8C);
            }
        }

        private static Icon BuildIcon(TrayState state)
        {
            using (var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.Clear(Color.Transparent);

                    using (GraphicsPath shape = RoundedRect(new Rectangle(1, 1, 30, 30), 8))
                    using (var fill = new SolidBrush(Color.FromArgb(255, 12, 15, 25)))
                    using (var border = new Pen(Color.FromArgb(255, 42, 50, 76), 1.6f))
                    {
                        graphics.FillPath(fill, shape);
                        graphics.DrawPath(border, shape);
                    }

                    using (var dot = new SolidBrush(ToneFor(state)))
                        graphics.FillEllipse(dot, 9, 9, 14, 14);
                    using (var highlight = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                        graphics.FillEllipse(highlight, 11, 10, 6, 5);
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon raw = Icon.FromHandle(handle))
                        return (Icon)raw.Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public void Dispose()
        {
            try
            {
                _notify.Visible = false;
                if (_notify.ContextMenuStrip != null) _notify.ContextMenuStrip.Dispose();
                _notify.Dispose();
            }
            catch { }

            if (_icon != null)
            {
                try { _icon.Dispose(); }
                catch { }
                _icon = null;
            }
        }
    }
}
