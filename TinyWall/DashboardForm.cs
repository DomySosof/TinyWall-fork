using pylorak.Windows;
using pylorak.Windows.NetStat;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace pylorak.TinyWall
{
    /// <summary>
    /// GlassWire-style activity dashboard. Shows live system-wide traffic,
    /// active applications and recent firewall events.
    /// </summary>
    internal sealed class DashboardForm : Form
    {
        private sealed class AppActivity
        {
            internal string Key = string.Empty;
            internal string Path = string.Empty;
            internal string Name = string.Empty;
            internal int Connections;
            internal int Blocked;
        }

        private sealed class EventItem
        {
            internal DateTime Time;
            internal string Path = string.Empty;
            internal string Name = string.Empty;
            internal string Detail = string.Empty;
            internal bool Blocked;
            internal int Count = 1;
        }

        private const int HistoryCapacity = 3700;
        private const int MaxAppRows = 12;
        private const int MaxEventRows = 100;

        private readonly TinyWallController Controller;
        private readonly TrafficRateMonitor RateMonitor = new();
        private readonly System.Windows.Forms.Timer SampleTimer = new();
        private readonly System.Windows.Forms.Timer ActivityTimer = new();
        private readonly List<TrafficSample> History = new();
        private readonly List<AppActivity> AppItems = new();
        private readonly List<EventItem> EventItems = new();
        private readonly Dictionary<string, Bitmap?> IconCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<uint, string> ProcessPathCache = new();

        private DashboardHeader Header = null!;
        private DashboardCard TrafficCard = null!;
        private TrafficChart Chart = null!;
        private DashboardTile TileDown = null!;
        private DashboardTile TileUp = null!;
        private DashboardTile TileSession = null!;
        private DashboardTile TileBlocked = null!;
        private DashboardCard AppsCard = null!;
        private DashboardCard EventsCard = null!;
        private ListBox AppList = null!;
        private ListBox EventList = null!;
        private Label AppEmpty = null!;
        private Label EventEmpty = null!;
        private DashboardFlatButton BtnMinute = null!;
        private DashboardFlatButton BtnFiveMinutes = null!;
        private DashboardFlatButton BtnThirtyMinutes = null!;
        private DashboardFlatButton BtnHour = null!;

        private TimeSpan ChartWindow = TimeSpan.FromMinutes(5);
        private DateTime LastSampleTime = DateTime.MinValue;
        private DateTime ProcessCacheStamp = DateTime.MinValue;
        private long SessionRx;
        private long SessionTx;
        private long LastRxRate;
        private long LastTxRate;
        private int BlockedLast5Min;
        private int BlockedAppsLast5Min;
        private int CollectionInProgress;
        private bool IsClosing;

        internal DashboardForm(TinyWallController controller)
        {
            Controller = controller;
            DashboardTheme.Configure(ActiveConfig.Controller);
            ApplyDashboardOpacity();

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            BackColor = DashboardTheme.Background;
            ForeColor = DashboardTheme.Text;
            Font = DashboardTheme.FontBody;
            Text = Resources.Dashboard.WindowTitle;
            Icon = Resources.Icons.firewall;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(DashboardTheme.S(1120), DashboardTheme.S(760));
            MinimumSize = new Size(DashboardTheme.S(920), DashboardTheme.S(640));
            DoubleBuffered = true;
            BuildUi();
            ResumeLayout(true);
        }

        private void BuildUi()
        {
            Header = new DashboardHeader { Dock = DockStyle.Top };

            var btnConnections = new DashboardFlatButton(Resources.Dashboard.BtnConnections)
            {
                IconImage = Resources.Icons.connections,
                SurfaceColor = DashboardTheme.HeaderBackground
            };
            btnConnections.FitToText();
            btnConnections.Click += (s, e) => Controller.OpenConnectionsWindow();

            var btnWidget = new DashboardFlatButton(Resources.Dashboard.WidgetTrayMenuItem)
            {
                IconImage = Resources.Icons.window,
                SurfaceColor = DashboardTheme.HeaderBackground
            };
            btnWidget.FitToText();
            btnWidget.Click += (s, e) => Controller.ToggleTrafficWidget();

            var btnManage = new DashboardFlatButton(Resources.Dashboard.BtnManage)
            {
                Filled = true,
                Accent = DashboardTheme.Accent,
                SurfaceColor = DashboardTheme.HeaderBackground
            };
            btnManage.Click += (s, e) => Controller.OpenSettingsWindow();

            Header.AddAction(btnConnections);
            Header.AddAction(btnWidget);
            Header.AddAction(btnManage);

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = DashboardTheme.Background,
                Padding = new Padding(DashboardTheme.S(16), DashboardTheme.S(4), DashboardTheme.S(16), DashboardTheme.S(16)),
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DashboardTheme.S(340)));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, DashboardTheme.S(268)));
            Utils.SetDoubleBuffering(body, true);

            // --- Traffic chart card -------------------------------------------------
            TrafficCard = new DashboardCard(Resources.Dashboard.TrafficTitle)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, DashboardTheme.S(14), DashboardTheme.S(14))
            };

            BtnMinute = CreateRangeButton(Resources.Dashboard.RangeMinute, TimeSpan.FromMinutes(1));
            BtnFiveMinutes = CreateRangeButton(Resources.Dashboard.Range5Minutes, TimeSpan.FromMinutes(5));
            BtnThirtyMinutes = CreateRangeButton(Resources.Dashboard.Range30Minutes, TimeSpan.FromMinutes(30));
            BtnHour = CreateRangeButton(Resources.Dashboard.RangeHour, TimeSpan.FromHours(1));

            TrafficCard.AddHeaderControl(BtnMinute);
            TrafficCard.AddHeaderControl(BtnFiveMinutes);
            TrafficCard.AddHeaderControl(BtnThirtyMinutes);
            TrafficCard.AddHeaderControl(BtnHour);

            Chart = new TrafficChart { Dock = DockStyle.Fill };
            TrafficCard.Controls.Add(Chart);
            Chart.BringToFront();

            body.Controls.Add(TrafficCard, 0, 0);

            // --- Stat tiles ---------------------------------------------------------
            var tiles = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = DashboardTheme.Background,
                ColumnCount = 1,
                RowCount = 4,
                Margin = new Padding(0, 0, 0, DashboardTheme.S(14))
            };
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 4; ++i)
                tiles.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            Utils.SetDoubleBuffering(tiles, true);

            TileDown = new DashboardTile(Resources.Dashboard.TileDownload, DashboardTheme.ChartDownload)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, DashboardTheme.S(8))
            };
            TileUp = new DashboardTile(Resources.Dashboard.TileUpload, DashboardTheme.ChartUpload)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, DashboardTheme.S(8))
            };
            TileSession = new DashboardTile(Resources.Dashboard.TileSession, DashboardTheme.Accent)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, DashboardTheme.S(8))
            };
            TileBlocked = new DashboardTile(Resources.Dashboard.TileBlocked, DashboardTheme.Red)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };

            tiles.Controls.Add(TileDown, 0, 0);
            tiles.Controls.Add(TileUp, 0, 1);
            tiles.Controls.Add(TileSession, 0, 2);
            tiles.Controls.Add(TileBlocked, 0, 3);
            body.Controls.Add(tiles, 1, 0);

            // --- Active applications ------------------------------------------------
            AppsCard = new DashboardCard(Resources.Dashboard.AppsTitle)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, DashboardTheme.S(14), 0)
            };
            AppList = CreateOwnerDrawnList(DashboardTheme.S(56), AppList_DrawItem);
            AppEmpty = CreateEmptyLabel(Resources.Dashboard.NoApps);
            AppsCard.Controls.Add(AppList);
            AppsCard.Controls.Add(AppEmpty);
            AppEmpty.BringToFront();
            body.Controls.Add(AppsCard, 0, 1);

            // --- Firewall events ----------------------------------------------------
            EventsCard = new DashboardCard(Resources.Dashboard.EventsTitle) { Dock = DockStyle.Fill, Margin = new Padding(0) };
            EventList = CreateOwnerDrawnList(DashboardTheme.S(52), EventList_DrawItem);
            EventEmpty = CreateEmptyLabel(Resources.Dashboard.NoEvents);
            EventsCard.Controls.Add(EventList);
            EventsCard.Controls.Add(EventEmpty);
            EventEmpty.BringToFront();
            body.Controls.Add(EventsCard, 1, 1);

            // Body is added first so that the header (last in the collection) is docked
            // before it and the body only fills the remaining area.
            Controls.Add(body);
            Controls.Add(Header);

            AppList.DoubleClick += (s, e) => Controller.OpenConnectionsWindow();
            EventList.DoubleClick += (s, e) => Controller.OpenConnectionsWindow();

            UpdateRangeButtons();
        }

        private DashboardFlatButton CreateRangeButton(string text, TimeSpan span)
        {
            var button = new DashboardFlatButton(text) { Accent = DashboardTheme.Accent };
            button.Click += (s, e) =>
            {
                ChartWindow = span;
                UpdateRangeButtons();
                Chart.SetData(History, ChartWindow);
            };
            return button;
        }

        private void UpdateRangeButtons()
        {
            BtnMinute.Selected = ChartWindow == TimeSpan.FromMinutes(1);
            BtnFiveMinutes.Selected = ChartWindow == TimeSpan.FromMinutes(5);
            BtnThirtyMinutes.Selected = ChartWindow == TimeSpan.FromMinutes(30);
            BtnHour.Selected = ChartWindow == TimeSpan.FromHours(1);

            BtnMinute.Invalidate();
            BtnFiveMinutes.Invalidate();
            BtnThirtyMinutes.Invalidate();
            BtnHour.Invalidate();
        }

        private static Label CreateEmptyLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = DashboardTheme.Muted,
                BackColor = DashboardTheme.Card,
                Font = DashboardTheme.FontBody,
                Visible = false
            };
        }

        private static ListBox CreateOwnerDrawnList(int itemHeight, DrawItemEventHandler drawHandler)
        {
            var list = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = DashboardTheme.Card,
                ForeColor = DashboardTheme.Text,
                Font = DashboardTheme.FontBody,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = itemHeight,
                IntegralHeight = false,
                ScrollAlwaysVisible = false,
                SelectionMode = SelectionMode.One,
                Margin = new Padding(0)
            };

            list.DrawItem += drawHandler;
            Utils.SetDoubleBuffering(list, true);
            return list;
        }

        // -------------------------------------------------------------------------
        // Lifetime
        // -------------------------------------------------------------------------

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            DashboardTheme.ApplyWindowChrome(this);

            try
            {
                string listTheme = DashboardTheme.IsDark ? "DarkMode_Explorer" : "Explorer";
                NativeMethods.SetWindowTheme(AppList.Handle, listTheme, null);
                NativeMethods.SetWindowTheme(EventList.Handle, listTheme, null);
            }
            catch { }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RestoreWindowGeometry();

            SampleTimer.Interval = 1000;
            SampleTimer.Tick += (s, args) => SampleTick();
            SampleTimer.Start();

            ActivityTimer.Interval = 3000;
            ActivityTimer.Tick += (s, args) => RefreshActivity();
            ActivityTimer.Start();

            UpdateHeader();
            SampleTick();
            RefreshActivity();
        }

        private void RestoreWindowGeometry()
        {
            var cfg = ActiveConfig.Controller;
            if (cfg.DashFormWindowSize.Width > 0)
                Size = cfg.DashFormWindowSize;
            if (cfg.DashFormWindowLoc.X != 0)
            {
                Location = cfg.DashFormWindowLoc;
                Utils.FixupFormPosition(this);
            }

            WindowState = (cfg.DashFormWindowState == FormWindowState.Minimized)
                ? FormWindowState.Normal
                : cfg.DashFormWindowState;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            IsClosing = true;

            var cfg = ActiveConfig.Controller;
            cfg.DashFormWindowState = WindowState;
            if (WindowState == FormWindowState.Normal)
            {
                cfg.DashFormWindowLoc = Location;
                cfg.DashFormWindowSize = Size;
            }
            else
            {
                cfg.DashFormWindowLoc = RestoreBounds.Location;
                cfg.DashFormWindowSize = RestoreBounds.Size;
            }

            cfg.Save();

            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SampleTimer.Stop();
            SampleTimer.Dispose();
            ActivityTimer.Stop();
            ActivityTimer.Dispose();
            RateMonitor.Dispose();

            foreach (var icon in IconCache.Values)
                icon?.Dispose();
            IconCache.Clear();

            base.OnFormClosed(e);
        }

        private void ApplyDashboardOpacity()
        {
            int percent = ActiveConfig.Controller.DashboardOpacity;
            if (percent < 30)
                percent = 30;
            else if (percent > 100)
                percent = 100;

            Opacity = percent / 100.0;
        }

        internal void ApplyAppearance()
        {
            DashboardTheme.Configure(ActiveConfig.Controller);
            ApplyDashboardOpacity();

            BackColor = DashboardTheme.Background;
            ForeColor = DashboardTheme.Text;

            Header.ApplyAppearance();
            TrafficCard.ApplyAppearance();
            AppsCard.ApplyAppearance();
            EventsCard.ApplyAppearance();
            Chart.ApplyAppearance();

            TileDown.ApplyAppearance(DashboardTheme.ChartDownload);
            TileUp.ApplyAppearance(DashboardTheme.ChartUpload);
            TileSession.ApplyAppearance(DashboardTheme.Accent);
            TileBlocked.ApplyAppearance(DashboardTheme.Red);

            foreach (Control control in Header.Controls)
            {
                if (control is DashboardFlatButton button)
                    button.ApplyAppearance(DashboardTheme.HeaderBackground);
            }

            foreach (Control control in TrafficCard.Controls)
            {
                if (control is DashboardFlatButton button)
                    button.ApplyAppearance(DashboardTheme.Card);
            }

            ThemeLayoutPanels(this);

            AppList.BackColor = DashboardTheme.Card;
            AppList.ForeColor = DashboardTheme.Text;
            EventList.BackColor = DashboardTheme.Card;
            EventList.ForeColor = DashboardTheme.Text;

            AppEmpty.BackColor = DashboardTheme.Card;
            AppEmpty.ForeColor = DashboardTheme.Muted;
            EventEmpty.BackColor = DashboardTheme.Card;
            EventEmpty.ForeColor = DashboardTheme.Muted;

            DashboardTheme.ApplyWindowChrome(this);
            try
            {
                string listTheme = DashboardTheme.IsDark ? "DarkMode_Explorer" : "Explorer";
                NativeMethods.SetWindowTheme(AppList.Handle, listTheme, null);
                NativeMethods.SetWindowTheme(EventList.Handle, listTheme, null);
            }
            catch { }

            Invalidate(true);
        }

        private static void ThemeLayoutPanels(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is TableLayoutPanel layout)
                {
                    layout.BackColor = DashboardTheme.Background;
                    layout.ForeColor = DashboardTheme.Text;
                }

                ThemeLayoutPanels(control);
            }
        }

        // -------------------------------------------------------------------------
        // Live traffic sampling
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
            LastRxRate = rx;
            LastTxRate = tx;

            History.Add(new TrafficSample(now, rx, tx));
            if (History.Count > HistoryCapacity)
                History.RemoveRange(0, History.Count - HistoryCapacity);

            Chart.SetData(History, ChartWindow);
            UpdateTrafficTiles();
            UpdateHeader();
        }

        private void UpdateTrafficTiles()
        {
            TileDown.SetValues(DashboardFormat.Rate(LastRxRate), string.Empty);
            TileUp.SetValues(DashboardFormat.Rate(LastTxRate), string.Empty);
            TileSession.SetValues(
                DashboardFormat.Bytes(SessionRx + SessionTx),
                string.Format(CultureInfo.CurrentCulture, Resources.Dashboard.TileSessionDetail,
                    DashboardFormat.Bytes(SessionRx), DashboardFormat.Bytes(SessionTx)));
        }

        private void UpdateHeader()
        {
            Header.SetState(Controller.CurrentFirewallMode, Controller.IsFirewallLocked);
        }

        // -------------------------------------------------------------------------
        // Applications & firewall events
        // -------------------------------------------------------------------------

        private void RefreshActivity()
        {
            if (IsClosing)
                return;

            if (Interlocked.CompareExchange(ref CollectionInProgress, 1, 0) != 0)
                return;

            Task.Run(() =>
            {
                List<AppActivity>? apps = null;
                List<EventItem>? events = null;
                int blocked = 0;
                int blockedApps = 0;

                try
                {
                    FirewallLogEntry[] log = ReadFirewallLog();
                    blocked = CountBlocked(log, out blockedApps);
                    apps = CollectAppActivity(log);
                    events = BuildEventItems(log);
                }
                catch
                {
                    // Service unavailable or shutting down; keep the last known values.
                }
                finally
                {
                    Interlocked.Exchange(ref CollectionInProgress, 0);
                }

                if ((apps is null) && (events is null))
                    return;

                try
                {
                    if (IsDisposed || Disposing || IsClosing)
                        return;

                    BeginInvoke((MethodInvoker)delegate { ApplyActivity(apps, events, blocked, blockedApps); });
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            });
        }

        private static FirewallLogEntry[] ReadFirewallLog()
        {
            var request = GlobalInstances.Controller.BeginReadFwLog();
            return pylorak.TinyWall.Controller.EndReadFwLog(request.Response);
        }

        private static int CountBlocked(FirewallLogEntry[] log, out int affectedApps)
        {
            DateTime cutoff = DateTime.Now.AddMinutes(-5);
            int count = 0;
            var apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in log)
            {
                if ((entry.Event != FirewallLogEvent.ClassifyDrop) || (entry.Timestamp < cutoff))
                    continue;

                count++;
                string? appPath = entry.AppPath;
                if (!string.IsNullOrEmpty(appPath))
                    apps.Add(appPath);
            }

            affectedApps = apps.Count;
            return count;
        }

        private List<AppActivity> CollectAppActivity(FirewallLogEntry[] log)
        {
            var rows = new Dictionary<string, AppActivity>(StringComparer.OrdinalIgnoreCase);

            void Add(uint pid)
            {
                string path = GetProcessPathCached(pid);
                string key = string.IsNullOrEmpty(path)
                    ? "pid:" + pid.ToString(CultureInfo.InvariantCulture)
                    : path;

                if (!rows.TryGetValue(key, out AppActivity? row))
                {
                    row = new AppActivity
                    {
                        Key = key,
                        Path = path,
                        Name = GetDisplayName(pid, path)
                    };
                    rows.Add(key, row);
                }

                row.Connections++;
            }

            foreach (TcpRow tcpRow in NetStat.GetExtendedTcp4Table(false))
            {
                if (tcpRow.State != TcpState.Listen)
                    Add(tcpRow.ProcessId);
            }

            foreach (TcpRow tcpRow in NetStat.GetExtendedTcp6Table(false))
            {
                if (tcpRow.State != TcpState.Listen)
                    Add(tcpRow.ProcessId);
            }

            // Attach recent block counts to the applications we display
            DateTime cutoff = DateTime.Now.AddMinutes(-5);
            var blockedByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in log)
            {
                string? appPath = entry.AppPath;
                if ((entry.Event != FirewallLogEvent.ClassifyDrop) || (entry.Timestamp < cutoff) || string.IsNullOrEmpty(appPath))
                    continue;

                blockedByPath.TryGetValue(appPath, out int current);
                blockedByPath[appPath] = current + 1;
            }

            foreach (var row in rows.Values)
            {
                if (!string.IsNullOrEmpty(row.Path) && blockedByPath.TryGetValue(row.Path, out int blocked))
                    row.Blocked = blocked;
            }

            return rows.Values
                .OrderByDescending(r => r.Connections)
                .ThenByDescending(r => r.Blocked)
                .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxAppRows)
                .ToList();
        }

        private List<EventItem> BuildEventItems(FirewallLogEntry[] log)
        {
            DateTime cutoff = DateTime.Now.AddMinutes(-30);
            var seen = new Dictionary<string, EventItem>(StringComparer.Ordinal);
            var items = new List<EventItem>();

            // The service returns the buffer in chronological order, so walk backwards for newest first.
            for (int i = log.Length - 1; i >= 0; --i)
            {
                FirewallLogEntry entry = log[i];
                if (entry.Timestamp < cutoff)
                    continue;

                string path = entry.AppPath ?? string.Empty;
                if (string.IsNullOrEmpty(path) && (entry.ProcessId != 0))
                    path = GetProcessPathCached(entry.ProcessId);

                string remote = FormatEndpoint(entry.RemoteIp, entry.RemotePort);
                string key = string.Join("|",
                    path.ToLowerInvariant(),
                    entry.Event.ToString(),
                    entry.Direction.ToString(),
                    entry.FilterGroup.ToString(),
                    remote);

                if (seen.TryGetValue(key, out EventItem? existing))
                {
                    existing.Count++;
                    continue;
                }

                string arrow = entry.Direction switch
                {
                    RuleDirection.In => "\u2193",
                    RuleDirection.Out => "\u2191",
                    _ => "\u2195"
                };

                string detail = entry.Event == FirewallLogEvent.ClassifyDrop
                    ? string.Format(CultureInfo.CurrentCulture, "{0} {1}  \u00B7  {2}", arrow, remote, GetFilterGroupText(entry.FilterGroup))
                    : string.Format(CultureInfo.CurrentCulture, "{0} {1}  \u00B7  {2}", arrow, remote, entry.Protocol);

                var item = new EventItem
                {
                    Time = entry.Timestamp,
                    Path = path,
                    Name = GetDisplayName(entry.ProcessId, path),
                    Detail = detail,
                    Blocked = entry.Event == FirewallLogEvent.ClassifyDrop
                };

                seen.Add(key, item);
                items.Add(item);

                if (items.Count >= MaxEventRows)
                    break;
            }

            return items;
        }

        private static string GetFilterGroupText(FilterGroup group)
        {
            return group switch
            {
                FilterGroup.DefaultAction => Resources.Messages.FilterGroupDefaultAction,
                FilterGroup.PortScan => Resources.Messages.FilterGroupPortScan,
                FilterGroup.RawSocket => Resources.Messages.FilterGroupRawSocket,
                FilterGroup.Blocklist => Resources.Messages.FilterGroupBlocklist,
                FilterGroup.User => Resources.Messages.FilterGroupUser,
                FilterGroup.ExternalApp => Resources.Messages.FilterGroupExternalApp,
                _ => group.ToString()
            };
        }

        private static string FormatEndpoint(byte[]? address, int port)
        {
            if ((address is null) || (address.Length == 0))
                return "-";

            string text;
            try
            {
                text = new IPAddress(address).ToString();
            }
            catch
            {
                return "-";
            }

            if (text.IndexOf(':') >= 0)
                text = "[" + text + "]";

            return (port > 0) ? text + ":" + port.ToString(CultureInfo.InvariantCulture) : text;
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

        private static string GetDisplayName(uint pid, string path)
        {
            if ((pid == 0) || (pid == 4) || string.Equals(path, "System", StringComparison.OrdinalIgnoreCase))
                return Resources.Dashboard.SystemApp;

            if (string.IsNullOrEmpty(path))
                return string.Format(CultureInfo.CurrentCulture, Resources.Dashboard.UnknownApp, pid);

            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrEmpty(name) ? System.IO.Path.GetFileName(path) : name;
        }

        private void ApplyActivity(List<AppActivity>? apps, List<EventItem>? events, int blocked, int blockedApps)
        {
            if (IsClosing || IsDisposed)
                return;

            if (apps is not null)
            {
                AppItems.Clear();
                AppItems.AddRange(apps);
                ReplaceListItems(AppList, AppItems);
                AppEmpty.Visible = AppItems.Count == 0;
            }

            if (events is not null)
            {
                EventItems.Clear();
                EventItems.AddRange(events);
                ReplaceListItems(EventList, EventItems);
                EventEmpty.Visible = EventItems.Count == 0;
            }

            BlockedLast5Min = blocked;
            BlockedAppsLast5Min = blockedApps;
            TileBlocked.SetValues(
                blocked.ToString("N0", CultureInfo.CurrentCulture),
                string.Format(CultureInfo.CurrentCulture,
                    (blockedApps == 1) ? Resources.Dashboard.TileBlockedDetailOne : Resources.Dashboard.TileBlockedDetail,
                    blockedApps));

            AppEmpty.Visible = AppItems.Count == 0;
            EventEmpty.Visible = EventItems.Count == 0;
        }

        private static void ReplaceListItems(ListBox list, System.Collections.IList items)
        {
            int topIndex = list.TopIndex;
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (object item in items)
                    list.Items.Add(item);
            }
            finally
            {
                list.EndUpdate();
            }

            if ((topIndex > 0) && (topIndex < list.Items.Count))
                list.TopIndex = topIndex;
        }

        // -------------------------------------------------------------------------
        // Row painting
        // -------------------------------------------------------------------------

        private void AppList_DrawItem(object? sender, DrawItemEventArgs e)
        {
            Graphics g = e.Graphics;
            var row = e.Bounds;
            using (var background = new SolidBrush(((e.State & DrawItemState.Selected) != 0) ? DashboardTheme.CardAlt : DashboardTheme.Card))
                g.FillRectangle(background, row);

            if ((e.Index < 0) || (e.Index >= AppItems.Count))
                return;

            AppActivity item = AppItems[e.Index];
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int pad = DashboardTheme.S(12);
            int iconSize = DashboardTheme.S(30);
            var iconRect = new Rectangle(pad, row.Top + (row.Height - iconSize) / 2, iconSize, iconSize);
            DrawAppIcon(g, item, iconRect);

            int textX = iconRect.Right + DashboardTheme.S(10);
            int rightReserve = (item.Blocked > 0) ? DashboardTheme.S(64) : 0;
            int textWidth = Math.Max(0, row.Right - pad - textX - rightReserve);

            TextRenderer.DrawText(g, item.Name, DashboardTheme.FontApp,
                new Rectangle(textX, row.Top + DashboardTheme.S(8), textWidth, DashboardTheme.S(18)),
                DashboardTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            string subtitle = string.Format(CultureInfo.CurrentCulture,
                (item.Connections == 1) ? Resources.Dashboard.AppConnection : Resources.Dashboard.AppConnections,
                item.Connections);
            if (item.Blocked > 0)
                subtitle += string.Format(CultureInfo.CurrentCulture, Resources.Dashboard.AppBlockedSuffix, item.Blocked);

            TextRenderer.DrawText(g, subtitle, DashboardTheme.FontSmall,
                new Rectangle(textX, row.Top + DashboardTheme.S(26), textWidth, DashboardTheme.S(16)),
                DashboardTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            // Activity bar
            int barY = row.Bottom - DashboardTheme.S(11);
            int barWidth = Math.Max(0, row.Right - pad - textX - ((item.Blocked > 0) ? DashboardTheme.S(70) : 0));
            if (barWidth > 0)
            {
                using (var track = new SolidBrush(DashboardTheme.CardAlt))
                    g.FillRectangle(track, textX, barY, barWidth, DashboardTheme.S(3));

                int maxConnections = 1;
                for (int i = 0; i < AppItems.Count; ++i)
                    maxConnections = Math.Max(maxConnections, AppItems[i].Connections);

                int filled = (int)Math.Round(barWidth * (double)item.Connections / maxConnections);
                using (var fill = new SolidBrush(DashboardTheme.Accent))
                    g.FillRectangle(fill, textX, barY, Math.Max(DashboardTheme.S(2), filled), DashboardTheme.S(3));
            }

            if (item.Blocked > 0)
                DrawBadge(g, row, DashboardTheme.Red, item.Blocked.ToString(CultureInfo.CurrentCulture));
        }

        private void DrawAppIcon(Graphics g, AppActivity item, Rectangle iconRect)
        {
            Bitmap? icon = GetAppIcon(item.Path);
            if (icon is not null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(icon, iconRect);
                return;
            }

            using (var path = DashboardTheme.RoundedRect(iconRect, DashboardTheme.S(6)))
            using (var fill = new SolidBrush(DashboardTheme.CardAlt))
                g.FillPath(fill, path);

            string initial = string.IsNullOrEmpty(item.Name) ? "?" : item.Name.Substring(0, 1).ToUpperInvariant();
            TextRenderer.DrawText(g, initial, DashboardTheme.FontApp, iconRect, DashboardTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private static void DrawBadge(Graphics g, Rectangle row, Color color, string text)
        {
            int width = DashboardTheme.S(50);
            int height = DashboardTheme.S(20);
            int x = row.Right - DashboardTheme.S(12) - width;
            int y = row.Top + (row.Height - height) / 2;
            var rect = new RectangleF(x + 0.5f, y + 0.5f, width - 1f, height - 1f);

            using (var path = DashboardTheme.RoundedRect(rect, height / 2f))
            {
                using (var fill = new SolidBrush(Color.FromArgb(30, color)))
                    g.FillPath(fill, path);
                using (var border = new Pen(Color.FromArgb(100, color)))
                    g.DrawPath(border, path);
            }

            TextRenderer.DrawText(g, text, DashboardTheme.FontSmall, new Rectangle(x, y, width, height), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private void EventList_DrawItem(object? sender, DrawItemEventArgs e)
        {
            Graphics g = e.Graphics;
            var row = e.Bounds;
            using (var background = new SolidBrush(((e.State & DrawItemState.Selected) != 0) ? DashboardTheme.CardAlt : DashboardTheme.Card))
                g.FillRectangle(background, row);

            if ((e.Index < 0) || (e.Index >= EventItems.Count))
                return;

            EventItem item = EventItems[e.Index];
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int pad = DashboardTheme.S(12);
            Color color = item.Blocked ? DashboardTheme.Red : DashboardTheme.Green;
            int dot = DashboardTheme.S(8);
            int dotY = row.Top + DashboardTheme.S(17);
            using (var dotBrush = new SolidBrush(color))
                g.FillEllipse(dotBrush, pad, dotY, dot, dot);

            int textX = pad + dot + DashboardTheme.S(8);
            int rightReserve = DashboardTheme.S(86);
            int textWidth = Math.Max(0, row.Right - pad - textX - rightReserve);

            string name = item.Name;
            if (item.Count > 1)
                name += string.Format(CultureInfo.CurrentCulture, "  x{0}", item.Count);

            TextRenderer.DrawText(g, name, DashboardTheme.FontApp,
                new Rectangle(textX, row.Top + DashboardTheme.S(7), textWidth, DashboardTheme.S(18)),
                DashboardTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            TextRenderer.DrawText(g, item.Detail, DashboardTheme.FontSmall,
                new Rectangle(textX, row.Top + DashboardTheme.S(25), textWidth, DashboardTheme.S(16)),
                DashboardTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            TextRenderer.DrawText(g, item.Blocked ? Resources.Dashboard.EventBlocked : Resources.Dashboard.EventAllowed,
                DashboardTheme.FontSmall,
                new Rectangle(row.Right - DashboardTheme.S(88), row.Top + DashboardTheme.S(7), DashboardTheme.S(76), DashboardTheme.S(16)),
                color, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(g, item.Time.ToString("HH:mm:ss", CultureInfo.CurrentCulture), DashboardTheme.FontSmall,
                new Rectangle(row.Right - DashboardTheme.S(88), row.Top + DashboardTheme.S(25), DashboardTheme.S(76), DashboardTheme.S(16)),
                DashboardTheme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private Bitmap? GetAppIcon(string path)
        {
            if (string.IsNullOrEmpty(path) || string.Equals(path, "System", StringComparison.OrdinalIgnoreCase))
                return null;

            if (IconCache.TryGetValue(path, out Bitmap? cached))
                return cached;

            Bitmap? icon = null;
            try
            {
                icon = Utils.GetIconContained(path, DashboardTheme.S(30), DashboardTheme.S(30));
            }
            catch { }

            if (IconCache.Count > 256)
            {
                foreach (var bitmap in IconCache.Values)
                    bitmap?.Dispose();
                IconCache.Clear();
            }

            IconCache[path] = icon;
            return icon;
        }

        private static class NativeMethods
        {
            [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
            internal static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string? subIdList);
        }
    }
}
