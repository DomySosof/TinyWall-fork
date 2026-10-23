using DarkModeForms;
using pylorak.Windows;
using pylorak.Windows.NetStat;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace pylorak.TinyWall
{
    /// <summary>
    /// Compact always-on-top window that mirrors the traffic chart of the dashboard.
    /// The widget keeps its own sampling history and can be made translucent.
    /// </summary>
    internal sealed class TrafficWidgetForm : Form
    {
        private const int HistoryCapacity = 3700;
        private const int MinOpacityPercent = 30;
        private const int MaxOpacityPercent = 100;
        private const int OpacityStep = 5;

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private readonly TinyWallController Controller;
        private readonly TrafficRateMonitor RateMonitor = new();
        private readonly System.Windows.Forms.Timer SampleTimer = new();
        private readonly System.Windows.Forms.Timer ActivityTimer = new();
        private readonly System.Windows.Forms.Timer SaveTimer = new() { Interval = 1500 };
        private readonly ToolTip Tip = new();
        private readonly List<TrafficSample> History = new();
        private readonly Dictionary<uint, string> ProcessPathCache = new();

        private readonly Panel DragBar = new();
        private readonly Label TitleLabel = new();
        private readonly Label DownLabel = new();
        private readonly Label UpLabel = new();
        private readonly Label CloseLabel = new();
        private readonly Panel StatusBar = new();
        private readonly Label StatusDot = new();
        private readonly Label StatusText = new();
        private readonly new ContextMenuStrip Menu = new();
        private readonly ToolStripMenuItem RangeMenu = new();
        private readonly ToolStripMenuItem OpacityMenu = new();

        private TrafficChart Chart = null!;
        private TimeSpan ChartWindow = TimeSpan.FromMinutes(5);
        private DateTime LastSampleTime = DateTime.MinValue;
        private DateTime ProcessCacheStamp = DateTime.MinValue;
        private long SessionRx;
        private long SessionTx;
        private int OnlineApps;
        private int ActivityInProgress;
        private int OpacityPercent = 90;
        private bool IsClosing;
        private bool GeometryDirty;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        internal TrafficWidgetForm(TinyWallController controller)
        {
            Controller = controller;
            DashboardTheme.Configure(ActiveConfig.Controller);

            var cfg = ActiveConfig.Controller;
            OpacityPercent = (cfg.WidgetOpacity >= MinOpacityPercent && cfg.WidgetOpacity <= MaxOpacityPercent)
                ? cfg.WidgetOpacity
                : 90;
            ChartWindow = MinutesToWindow(cfg.WidgetChartMinutes);

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = DashboardTheme.Card;
            ForeColor = DashboardTheme.Text;
            Font = DashboardTheme.FontBody;
            DoubleBuffered = true;
            MinimumSize = new Size(DashboardTheme.S(260), DashboardTheme.S(120));
            Size = (cfg.WidgetWindowSize.Width >= MinimumSize.Width && cfg.WidgetWindowSize.Height >= MinimumSize.Height)
                ? cfg.WidgetWindowSize
                : new Size(DashboardTheme.S(380), DashboardTheme.S(180));
            Opacity = OpacityPercent / 100.0;

            BuildUi();
            BuildMenu();
            ApplyMenuTheme();
            RestoreLocation(cfg);
            ResumeLayout(true);
        }

        // -------------------------------------------------------------------------
        // UI construction
        // -------------------------------------------------------------------------

        private void BuildUi()
        {
            DragBar.Dock = DockStyle.Top;
            DragBar.Height = DashboardTheme.S(30);
            DragBar.BackColor = DashboardTheme.HeaderBackground;
            DragBar.Cursor = Cursors.SizeAll;
            DragBar.MouseDown += DragBar_MouseDown;
            DragBar.DoubleClick += (s, e) => Controller.OpenDashboardWindow();

            TitleLabel.Text = Resources.Dashboard.WidgetTitle;
            TitleLabel.Font = DashboardTheme.FontCardTitle;
            TitleLabel.ForeColor = DashboardTheme.Text;
            TitleLabel.BackColor = Color.Transparent;
            TitleLabel.TextAlign = ContentAlignment.MiddleLeft;
            TitleLabel.AutoSize = false;
            TitleLabel.MouseDown += DragBar_MouseDown;
            TitleLabel.DoubleClick += (s, e) => Controller.OpenDashboardWindow();

            DownLabel.Text = "--";
            DownLabel.Font = DashboardTheme.FontSmall;
            DownLabel.ForeColor = DashboardTheme.ChartDownload;
            DownLabel.BackColor = Color.Transparent;
            DownLabel.TextAlign = ContentAlignment.MiddleRight;
            DownLabel.AutoSize = false;

            UpLabel.Text = "--";
            UpLabel.Font = DashboardTheme.FontSmall;
            UpLabel.ForeColor = DashboardTheme.ChartUpload;
            UpLabel.BackColor = Color.Transparent;
            UpLabel.TextAlign = ContentAlignment.MiddleRight;
            UpLabel.AutoSize = false;

            CloseLabel.Text = "\u2715";
            CloseLabel.Font = DashboardTheme.FontBody;
            CloseLabel.ForeColor = DashboardTheme.Muted;
            CloseLabel.BackColor = Color.Transparent;
            CloseLabel.TextAlign = ContentAlignment.MiddleCenter;
            CloseLabel.Cursor = Cursors.Hand;
            CloseLabel.Click += (s, e) => Close();
            CloseLabel.MouseEnter += (s, e) => CloseLabel.ForeColor = DashboardTheme.Red;
            CloseLabel.MouseLeave += (s, e) => CloseLabel.ForeColor = DashboardTheme.Muted;

            DragBar.Controls.Add(TitleLabel);
            DragBar.Controls.Add(DownLabel);
            DragBar.Controls.Add(UpLabel);
            DragBar.Controls.Add(CloseLabel);

            StatusBar.Dock = DockStyle.Bottom;
            StatusBar.Height = DashboardTheme.S(24);
            StatusBar.BackColor = DashboardTheme.HeaderBackground;
            StatusBar.Cursor = Cursors.Hand;
            StatusBar.Click += (s, e) => Controller.OpenConnectionsWindow();

            StatusDot.Text = "\u25CF";
            StatusDot.Font = DashboardTheme.FontBody;
            StatusDot.ForeColor = DashboardTheme.Muted;
            StatusDot.BackColor = Color.Transparent;
            StatusDot.TextAlign = ContentAlignment.MiddleCenter;
            StatusDot.AutoSize = false;
            StatusDot.Cursor = Cursors.Hand;
            StatusDot.Click += (s, e) => Controller.OpenConnectionsWindow();

            StatusText.Text = "--";
            StatusText.Font = DashboardTheme.FontBody;
            StatusText.ForeColor = DashboardTheme.Text;
            StatusText.BackColor = Color.Transparent;
            StatusText.TextAlign = ContentAlignment.MiddleLeft;
            StatusText.AutoSize = false;
            StatusText.AutoEllipsis = true;
            StatusText.Cursor = Cursors.Hand;
            StatusText.Click += (s, e) => Controller.OpenConnectionsWindow();

            StatusBar.Controls.Add(StatusDot);
            StatusBar.Controls.Add(StatusText);
            StatusBar.Resize += (s, e) => LayoutStatusBar();

            Chart = new TrafficChart { Dock = DockStyle.Fill };
            Chart.SetData(History, ChartWindow);

            // Body first, header and status bar last, so docking leaves the chart the remaining space.
            Controls.Add(Chart);
            Controls.Add(StatusBar);
            Controls.Add(DragBar);
        }

        private void BuildMenu()
        {
            Menu.ShowImageMargin = false;

            var openItem = new ToolStripMenuItem(Resources.Dashboard.WidgetOpenPanel);
            openItem.Click += (s, e) => Controller.OpenDashboardWindow();

            RangeMenu.Text = Resources.Dashboard.WidgetRange;
            AddRangeItem(RangeMenu, Resources.Dashboard.RangeMinute, TimeSpan.FromMinutes(1));
            AddRangeItem(RangeMenu, Resources.Dashboard.Range5Minutes, TimeSpan.FromMinutes(5));
            AddRangeItem(RangeMenu, Resources.Dashboard.Range30Minutes, TimeSpan.FromMinutes(30));
            AddRangeItem(RangeMenu, Resources.Dashboard.RangeHour, TimeSpan.FromHours(1));

            OpacityMenu.Text = Resources.Dashboard.WidgetOpacity;
            for (int percent = MaxOpacityPercent; percent >= MinOpacityPercent; percent -= 10)
                AddOpacityItem(OpacityMenu, percent);

            var closeItem = new ToolStripMenuItem(Resources.Dashboard.WidgetClose);
            closeItem.Click += (s, e) => Close();

            Menu.Items.Add(openItem);
            Menu.Items.Add(new ToolStripSeparator());
            Menu.Items.Add(RangeMenu);
            Menu.Items.Add(OpacityMenu);
            Menu.Items.Add(new ToolStripSeparator());
            Menu.Items.Add(closeItem);
            Menu.Opening += (s, e) => UpdateMenuChecks();

            ContextMenuStrip = Menu;
            foreach (Control control in new Control[] { DragBar, Chart, TitleLabel, DownLabel, UpLabel, CloseLabel, StatusBar, StatusDot, StatusText })
            {
                control.ContextMenuStrip = Menu;
                control.MouseWheel += OnWidgetMouseWheel;
            }
        }

        private void AddRangeItem(ToolStripMenuItem parent, string text, TimeSpan span)
        {
            var item = new ToolStripMenuItem(text) { Tag = span };
            item.Click += (s, e) => SetChartWindow(span);
            parent.DropDownItems.Add(item);
        }

        private void AddOpacityItem(ToolStripMenuItem parent, int percent)
        {
            var item = new ToolStripMenuItem(string.Format(CultureInfo.CurrentCulture, "{0} %", percent)) { Tag = percent };
            item.Click += (s, e) => SetOpacity(percent);
            parent.DropDownItems.Add(item);
        }

        private static TimeSpan MinutesToWindow(int minutes)
        {
            return minutes switch
            {
                <= 1 => TimeSpan.FromMinutes(1),
                5 => TimeSpan.FromMinutes(5),
                30 => TimeSpan.FromMinutes(30),
                >= 60 => TimeSpan.FromHours(1),
                _ => TimeSpan.FromMinutes(5)
            };
        }

        private void RestoreLocation(ControllerSettings cfg)
        {
            if ((cfg.WidgetWindowLoc.X != 0) || (cfg.WidgetWindowLoc.Y != 0))
            {
                Location = cfg.WidgetWindowLoc;
                Utils.FixupFormPosition(this);
                return;
            }

            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(
                area.Right - Width - DashboardTheme.S(24),
                area.Bottom - Height - DashboardTheme.S(24));
        }

        // -------------------------------------------------------------------------
        // Lifetime
        // -------------------------------------------------------------------------

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateRegion();
            DashboardTheme.ApplyWindowChrome(this);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            LayoutBar();
            LayoutStatusBar();

            SaveTimer.Tick += (s, args) =>
            {
                SaveTimer.Stop();
                if (GeometryDirty)
                    SaveGeometry();
            };

            SampleTimer.Interval = 1000;
            SampleTimer.Tick += (s, args) => SampleTick();
            SampleTimer.Start();
            SampleTick();

            ActivityTimer.Interval = 3000;
            ActivityTimer.Tick += (s, args) => RefreshAppsOnline();
            ActivityTimer.Start();
            RefreshAppsOnline();
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            MarkGeometryDirty();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
            LayoutBar();
            LayoutStatusBar();
            MarkGeometryDirty();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            IsClosing = true;
            SaveTimer.Stop();

            var cfg = ActiveConfig.Controller;
            SaveGeometry();
            cfg.WidgetVisible = e.CloseReason != CloseReason.UserClosing;
            cfg.Save();

            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SampleTimer.Stop();
            SampleTimer.Dispose();
            ActivityTimer.Stop();
            ActivityTimer.Dispose();
            SaveTimer.Dispose();
            RateMonitor.Dispose();
            Tip.Dispose();
            Menu.Dispose();

            base.OnFormClosed(e);
        }

        internal void ApplyAppearance()
        {
            DashboardTheme.Configure(ActiveConfig.Controller);

            BackColor = DashboardTheme.Card;
            ForeColor = DashboardTheme.Text;

            DragBar.BackColor = DashboardTheme.HeaderBackground;
            TitleLabel.ForeColor = DashboardTheme.Text;
            DownLabel.ForeColor = DashboardTheme.ChartDownload;
            UpLabel.ForeColor = DashboardTheme.ChartUpload;
            CloseLabel.ForeColor = DashboardTheme.Muted;

            StatusBar.BackColor = DashboardTheme.HeaderBackground;
            StatusDot.ForeColor = (OnlineApps > 0) ? DashboardTheme.Green : DashboardTheme.Muted;
            StatusText.ForeColor = DashboardTheme.Text;

            Chart.ApplyAppearance();
            ApplyMenuTheme();
            DashboardTheme.ApplyWindowChrome(this);
            Invalidate(true);
        }

        private void ApplyMenuTheme()
        {
            if (DashboardTheme.IsDark)
            {
                var colors = DarkModeCS.GetSystemColors(0);
                Menu.RenderMode = ToolStripRenderMode.Professional;
                Menu.Renderer = new MyRenderer(new CustomColorTable(colors)) { MyColors = colors };
            }
            else
            {
                Menu.RenderMode = ToolStripRenderMode.System;
                Menu.Renderer = null;
            }
        }

        private void MarkGeometryDirty()
        {
            if (IsClosing || !IsHandleCreated)
                return;

            GeometryDirty = true;
            SaveTimer.Stop();
            SaveTimer.Start();
        }

        private void SaveGeometry()
        {
            GeometryDirty = false;

            var cfg = ActiveConfig.Controller;
            cfg.WidgetWindowLoc = Location;
            cfg.WidgetWindowSize = Size;
            cfg.WidgetOpacity = OpacityPercent;
            cfg.WidgetChartMinutes = (int)Math.Round(ChartWindow.TotalMinutes);
            cfg.Save();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using var path = DashboardTheme.RoundedRect(rect, DashboardTheme.FS(10));
            using var border = new Pen(DashboardTheme.Border);
            e.Graphics.DrawPath(border, path);
        }

        // Keep the widget out of Alt+Tab and the taskbar.
        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x80;
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        private void UpdateRegion()
        {
            if ((Width <= 0) || (Height <= 0))
                return;

            using var path = DashboardTheme.RoundedRect(new RectangleF(0, 0, Width, Height), DashboardTheme.FS(10));
            var old = Region;
            Region = new Region(path);
            old?.Dispose();
        }

        private void LayoutBar()
        {
            int height = DragBar.Height;
            int pad = DashboardTheme.S(10);
            int closeWidth = DashboardTheme.S(22);
            int rateWidth = DashboardTheme.S(118);

            CloseLabel.SetBounds(DragBar.Width - pad - closeWidth, 0, closeWidth, height);
            UpLabel.SetBounds(CloseLabel.Left - rateWidth, 0, rateWidth, height);
            DownLabel.SetBounds(UpLabel.Left - rateWidth, 0, rateWidth, height);
            TitleLabel.SetBounds(pad, 0, Math.Max(0, DownLabel.Left - pad - DashboardTheme.S(4)), height);
        }

        private void LayoutStatusBar()
        {
            int pad = DashboardTheme.S(10);
            int height = StatusBar.Height;
            int dotWidth = DashboardTheme.S(12);
            int dotHeight = DashboardTheme.S(12);

            StatusDot.SetBounds(pad, (height - dotHeight) / 2, dotWidth, dotHeight);
            int textX = StatusDot.Right + DashboardTheme.S(6);
            StatusText.SetBounds(textX, 0, Math.Max(0, StatusBar.Width - textX - pad), height);
        }

        // -------------------------------------------------------------------------
        // Interaction
        // -------------------------------------------------------------------------

        private void DragBar_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            Focus();
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (TryChangeOpacityFromWheel(e))
                return;

            base.OnMouseWheel(e);
        }

        private void OnWidgetMouseWheel(object? sender, MouseEventArgs e)
        {
            if (TryChangeOpacityFromWheel(e))
                return;
        }

        private bool TryChangeOpacityFromWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == 0)
                return false;

            SetOpacity(OpacityPercent + (e.Delta > 0 ? OpacityStep : -OpacityStep));
            return true;
        }

        private void SetChartWindow(TimeSpan span)
        {
            ChartWindow = span;
            Chart.SetData(History, ChartWindow);

            var cfg = ActiveConfig.Controller;
            cfg.WidgetChartMinutes = (int)Math.Round(ChartWindow.TotalMinutes);
            cfg.Save();
        }

        private void SetOpacity(int percent)
        {
            OpacityPercent = Math.Max(MinOpacityPercent, Math.Min(MaxOpacityPercent, percent));
            Opacity = OpacityPercent / 100.0;

            var cfg = ActiveConfig.Controller;
            cfg.WidgetOpacity = OpacityPercent;
            cfg.Save();
        }

        private void UpdateMenuChecks()
        {
            foreach (ToolStripItem item in RangeMenu.DropDownItems)
            {
                if (item is ToolStripMenuItem menuItem)
                    menuItem.Checked = (menuItem.Tag is TimeSpan span) && (span == ChartWindow);
            }

            foreach (ToolStripItem item in OpacityMenu.DropDownItems)
            {
                if (item is ToolStripMenuItem menuItem)
                    menuItem.Checked = (menuItem.Tag is int percent) && (percent == OpacityPercent);
            }
        }

        // -------------------------------------------------------------------------
        // Traffic sampling
        // -------------------------------------------------------------------------

        private void SampleTick()
        {
            if (IsClosing)
                return;

            try
            {
                RateMonitor.Update();
            }
            catch
            {
                return;
            }

            DateTime now = DateTime.Now;
            long rx = Math.Max(0, RateMonitor.BytesReceivedPerSec);
            long tx = Math.Max(0, RateMonitor.BytesSentPerSec);
            double elapsed = 1.0;
            if (LastSampleTime != DateTime.MinValue)
                elapsed = Math.Min(5.0, Math.Max(0.0, (now - LastSampleTime).TotalSeconds));
            LastSampleTime = now;

            SessionRx += (long)(rx * elapsed);
            SessionTx += (long)(tx * elapsed);

            History.Add(new TrafficSample(now, rx, tx));
            if (History.Count > HistoryCapacity)
                History.RemoveRange(0, History.Count - HistoryCapacity);

            Chart.SetData(History, ChartWindow);
            DownLabel.Text = "\u2193 " + DashboardFormat.Rate(rx);
            UpLabel.Text = "\u2191 " + DashboardFormat.Rate(tx);
            UpdateToolTip();
        }

        private void UpdateToolTip()
        {
            try
            {
                string text = string.Format(CultureInfo.CurrentCulture, Resources.Dashboard.WidgetTooltip,
                    DashboardFormat.Bytes(SessionRx), DashboardFormat.Bytes(SessionTx));
                Tip.SetToolTip(DragBar, text);
            }
            catch { }
        }

        // -------------------------------------------------------------------------
        // Applications with Internet access
        // -------------------------------------------------------------------------

        private void RefreshAppsOnline()
        {
            if (IsClosing)
                return;

            if (Interlocked.CompareExchange(ref ActivityInProgress, 1, 0) != 0)
                return;

            Task.Run(() =>
            {
                int? count = null;

                try
                {
                    count = CountOnlineApps();
                }
                catch
                {
                    // Service unavailable or shutting down; keep the last known value.
                }
                finally
                {
                    Interlocked.Exchange(ref ActivityInProgress, 0);
                }

                if (count is null)
                    return;

                try
                {
                    if (IsDisposed || Disposing || IsClosing)
                        return;

                    BeginInvoke((MethodInvoker)delegate { ApplyAppsOnline(count.Value); });
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            });
        }

        private int CountOnlineApps()
        {
            var apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (TcpRow row in NetStat.GetExtendedTcp4Table(false))
            {
                if (IsActiveTcpState(row.State) && !IsLoopbackEndpoint(row.RemoteEndPoint))
                    AddOnlineApp(apps, row.ProcessId);
            }

            foreach (TcpRow row in NetStat.GetExtendedTcp6Table(false))
            {
                if (IsActiveTcpState(row.State) && !IsLoopbackEndpoint(row.RemoteEndPoint))
                    AddOnlineApp(apps, row.ProcessId);
            }

            return apps.Count;
        }

        private static bool IsActiveTcpState(TcpState state)
        {
            return state is TcpState.Established
                or TcpState.SynSent
                or TcpState.SynReceived
                or TcpState.CloseWait;
        }

        private static bool IsLoopbackEndpoint(IPEndPoint? endpoint)
        {
            if (endpoint is null)
                return true;

            IPAddress address = endpoint.Address;
            return IPAddress.IsLoopback(address)
                || address.Equals(IPAddress.Any)
                || address.Equals(IPAddress.IPv6Any);
        }

        private void AddOnlineApp(HashSet<string> apps, uint pid)
        {
            string path = GetProcessPathCached(pid);
            apps.Add(string.IsNullOrEmpty(path)
                ? "pid:" + pid.ToString(CultureInfo.InvariantCulture)
                : path);
        }

        private string GetProcessPathCached(uint pid)
        {
            if ((DateTime.UtcNow - ProcessCacheStamp).TotalSeconds > 60)
            {
                ProcessPathCache.Clear();
                ProcessCacheStamp = DateTime.UtcNow;
            }

            if (!ProcessPathCache.TryGetValue(pid, out string path))
            {
                try
                {
                    path = Utils.GetPathOfProcessUseTwService(pid, GlobalInstances.Controller);
                }
                catch
                {
                    path = string.Empty;
                }

                ProcessPathCache[pid] = path ?? string.Empty;
            }

            return path ?? string.Empty;
        }

        private void ApplyAppsOnline(int count)
        {
            if (IsClosing || IsDisposed)
                return;

            OnlineApps = count;
            UpdateAppsLabel();
        }

        private void UpdateAppsLabel()
        {
            try
            {
                StatusText.Text = string.Format(CultureInfo.CurrentCulture,
                    (OnlineApps == 1) ? Resources.Dashboard.WidgetAppsOnlineOne : Resources.Dashboard.WidgetAppsOnline,
                    OnlineApps);
                StatusDot.ForeColor = (OnlineApps > 0) ? DashboardTheme.Green : DashboardTheme.Muted;

                string tip = string.Format(CultureInfo.CurrentCulture, Resources.Dashboard.WidgetAppsTooltip, OnlineApps);
                Tip.SetToolTip(StatusBar, tip);
                Tip.SetToolTip(StatusDot, tip);
                Tip.SetToolTip(StatusText, tip);
            }
            catch { }
        }
    }
}
